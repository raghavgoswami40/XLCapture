#!/usr/bin/env python
# build_deck.py - builds the PowerPoint for one or more XLCapture slide pack folders on the client's template.
#   python build_deck.py PACK [PACK ...] [--template template.pptx] [--out deck.pptx] [--author "Name"] [--date "24 October 2026"] [--email a@b.com]
#   python build_deck.py PACK --append existing.pptx [--replace] [--out updated.pptx]     adds the pack's activity to an existing deck, in its own look,
#       in its place by activity number (before the next higher one, else before the closing slide). The existing deck is never changed: the result is a new file.
# Needs python-pptx (pip install python-pptx). Layout rules come from Ventia's completed decks:
#   cover, then per activity a divider slide (+ the process diagram slide when the pack has one), then one slide per screenshot, then the closing slide.
#   The screenshot is placed in a 16:9 frame centred on the slide (7956000 x 4475163 EMU, top 2168525), scaled to fit, exactly like the completed decks.
#   Boxes become rounded rectangles on it; each step text becomes a text box at the position it had in the editor, so text that was
#   outside the screenshot is outside the picture on the slide.
import argparse, copy, json, os, sys
from xml.sax.saxutils import escape
from lxml import etree
from pptx import Presentation
from pptx.util import Emu

NS = ('xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" '
      'xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main" '
      'xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"')
A = '{http://schemas.openxmlformats.org/drawingml/2006/main}'
P = '{http://schemas.openxmlformats.org/presentationml/2006/main}'

# the picture frame on a content slide (EMU)
FRAME_CX, FRAME_TOP, FRAME_W, FRAME_H = 6096000, 2168525, 7956000, 4475163
SLIDE_W = 12192000
# the highlight round the activity on the diagram slide (EMU, as in the completed decks)
HL_W, HL_H = 2013358, 1451311
DIAG_BOX = (513047, 1256401, 10690371, 3074234)         # where the diagram picture sits on the activity slide (high enough to clear a two-line title, as in the completed decks)


def die(msg):
    sys.stderr.write('ERROR: ' + msg + '\n'); sys.exit(1)


def next_id(slide):
    ids = [int(e.get('id')) for e in slide.shapes._spTree.iter() if e.tag.endswith('}cNvPr') and e.get('id', '').isdigit()]
    return (max(ids) if ids else 1) + 1


def ph(slide, idx):
    for s in slide.placeholders:
        if s.placeholder_format.idx == idx: return s
    return None


def set_text(shape, text):
    """replace the text of a shape, keeping the formatting of its first run"""
    tf = shape.text_frame; p = tf.paragraphs[0]
    if p.runs:
        p.runs[0].text = text
        for r in p.runs[1:]: r._r.getparent().remove(r._r)
    else:
        tf.text = text
    for extra in tf.paragraphs[1:]: extra._p.getparent().remove(extra._p)


def clone(prs, src):
    """a new slide with the same layout and copies of the source slide's shapes"""
    new = prs.slides.add_slide(src.slide_layout)
    for sh in list(new.shapes): sh._element.getparent().remove(sh._element)
    for sh in src.shapes:
        # pictures, screenshot highlights and step texts of a finished slide are not copied: only its frame (title, activity line, task line)
        if sh._element.tag == P + 'pic' or sh.name.startswith(('Action ', 'Rectangle', 'Picture')): continue
        new.shapes._spTree.append(copy.deepcopy(sh._element))
    return new


def drop(prs, slide):
    lst = prs.slides._sldIdLst
    for sid in list(lst):
        if prs.part.related_part(sid.rId) is slide.part:
            prs.part.drop_rel(sid.rId); lst.remove(sid)


def role_of(slide):
    name = slide.slide_layout.name
    return ('cover' if name.startswith('Cover') else 'divider' if name == 'Divider 02' else 'diagram' if name == '1_Divider 06'
            else 'content' if name == 'Default layout' else 'closing' if name == 'Thank you' else None)


def role_slides(prs, last=False):
    """the example slide for each kind: the first one in the deck (the last one with last=True, for a finished deck)"""
    roles = {}
    for s in prs.slides:
        key = role_of(s)
        if key and (last or key not in roles): roles[key] = s
    return roles


def sld_el(prs, slide):
    for sid in prs.slides._sldIdLst:
        if prs.part.related_part(sid.rId) is slide.part: return sid
    return None


def activity_number(slide):
    p = ph(slide, 17)
    try: return int(p.text_frame.text.strip()) if p is not None else None
    except ValueError: return None


def main_append(a, packs):
    """add the packs' activities to an existing deck, in its own look"""
    if not os.path.exists(a.append): die('the existing deck was not found: ' + a.append)
    prs = Presentation(a.append); roles = role_slides(prs, last=True)
    for need in ('divider', 'content'):
        if need not in roles: die('the existing deck has no "%s" slide to copy the look from' % need)
    slides = list(prs.slides); closing = next((s for s in slides if role_of(s) == 'closing'), None)
    first_content = next((s for s in slides if role_of(s) == 'content'), None)
    p_title = (ph(first_content, 0).text_frame.text.strip() if first_content is not None and ph(first_content, 0) is not None else '')
    div = next((s for s in slides if role_of(s) == 'divider'), None)
    process_label = (ph(div, 16).text_frame.text.strip() if div is not None and ph(div, 16) is not None else '')
    first_dir, first = packs[0]; proc = first.get('process') or {}
    if not p_title: p_title = (proc.get('title') or '').strip() or (first.get('activity') or {}).get('title') or 'Process'
    if not process_label: process_label = ((proc.get('code') or '') + ' ' + p_title).strip()
    out = a.out or os.path.join(first_dir, os.path.splitext(os.path.basename(a.append))[0] + ' - updated.pptx')
    if os.path.abspath(out) == os.path.abspath(a.append): die('the result must be a new file, not the existing deck')
    added = 0
    for pack_dir, j in packs:
        act = j.get('activity') or {}; act_title = act.get('title') or j.get('name') or 'Activity'; act_code = (act.get('code') or '').strip()
        mine = int(act_code) if act_code.isdigit() else None
        number = ('%02d' % mine) if mine is not None else act_code
        slides = list(prs.slides)
        # where it goes: before the first activity with a higher number, else before the closing slide, else at the end
        divs = [(i, s, activity_number(s)) for i, s in enumerate(slides) if role_of(s) == 'divider']
        same = [d for d in divs if mine is not None and d[2] == mine]
        if same:
            if not a.replace: die('the deck already has activity %s. Use --replace to put the new one in its place.' % number)
            start = same[0][0]; nxt = next((d[0] for d in divs if d[0] > start), None)
            stop = nxt if nxt is not None else (slides.index(closing) if closing in slides else len(slides))
            for s in slides[start:stop]: drop(prs, s)
            slides = list(prs.slides)
        higher = [d for d in [(i, s, activity_number(s)) for i, s in enumerate(slides) if role_of(s) == 'divider'] if mine is not None and d[2] is not None and d[2] > mine]
        target = higher[0][1] if higher else (closing if closing is not None and closing in slides else None)
        act_label = (act_code + ' ' + act_title).strip(); created = []
        created.append(put_divider(prs, roles['divider'], process_label, {'title': act_title}, number))
        if j.get('diagram') and 'diagram' in roles:
            created.append(put_diagram(prs, roles['diagram'], pack_dir, process_label, {'title': act_title}, number, j['diagram']))
        counter = [0]
        for s in j['slides']: created.append(put_content_slide(prs, roles['content'], pack_dir, s, p_title, act_label, counter))
        lst = prs.slides._sldIdLst
        els = [sld_el(prs, s) for s in created]
        if target is not None:
            tel = sld_el(prs, target)
            for el in els: tel.addprevious(el)
        added += len(created)
    prs.save(out)
    print('Saved %s (%d slides; %d added to %s)' % (out, len(prs.slides), added, os.path.basename(a.append)))


def rect_xml(sid, name, x, y, w, h, adj=7123, line=15875):
    gd = '<a:gd name="adj" fmla="val %d"/>' % adj if adj else ''
    return etree.fromstring(
        '<p:sp %s><p:nvSpPr><p:cNvPr id="%d" name="%s"/><p:cNvSpPr/><p:nvPr/></p:nvSpPr><p:spPr><a:xfrm><a:off x="%d" y="%d"/><a:ext cx="%d" cy="%d"/></a:xfrm>'
        '<a:prstGeom prst="roundRect"><a:avLst>%s</a:avLst></a:prstGeom><a:noFill/><a:ln w="%d"><a:solidFill><a:schemeClr val="accent2"/></a:solidFill></a:ln></p:spPr>'
        '<p:style><a:lnRef idx="2"><a:schemeClr val="accent1"><a:shade val="15000"/></a:schemeClr></a:lnRef><a:fillRef idx="1"><a:schemeClr val="accent1"/></a:fillRef>'
        '<a:effectRef idx="0"><a:schemeClr val="accent1"/></a:effectRef><a:fontRef idx="minor"><a:schemeClr val="lt1"/></a:fontRef></p:style>'
        '<p:txBody><a:bodyPr rtlCol="0" anchor="ctr"/><a:lstStyle/><a:p><a:pPr algn="ctr"/><a:endParaRPr lang="en-AU"/></a:p></p:txBody></p:sp>'
        % (NS, sid, escape(name), x, y, w, h, gd, line))


def text_xml(sid, name, x, y, w, h, text, align):
    return etree.fromstring(
        '<p:sp %s><p:nvSpPr><p:cNvPr id="%d" name="%s"/><p:cNvSpPr txBox="1"/><p:nvPr/></p:nvSpPr><p:spPr><a:xfrm><a:off x="%d" y="%d"/><a:ext cx="%d" cy="%d"/></a:xfrm>'
        '<a:prstGeom prst="rect"><a:avLst/></a:prstGeom><a:solidFill><a:schemeClr val="bg1"/></a:solidFill></p:spPr>'
        '<p:txBody><a:bodyPr vertOverflow="overflow" vert="horz" wrap="square" rtlCol="0" anchor="t" anchorCtr="0"><a:noAutofit/></a:bodyPr><a:lstStyle/>'
        '<a:p><a:pPr algn="%s"/><a:r><a:rPr lang="en-AU" sz="1200"><a:solidFill><a:srgbClr val="A0CD60"/></a:solidFill></a:rPr><a:t>%s</a:t></a:r></a:p></p:txBody></p:sp>'
        % (NS, sid, escape(name), x, y, w, h, align, escape(text)))


def pic_placeholder_xml(sid, name, rid, x, y, w, h, idx=15):
    return etree.fromstring(
        '<p:pic %s><p:nvPicPr><p:cNvPr id="%d" name="%s"/><p:cNvPicPr><a:picLocks noGrp="1" noChangeAspect="1"/></p:cNvPicPr><p:nvPr><p:ph sz="quarter" idx="%d"/></p:nvPr></p:nvPicPr>'
        '<p:blipFill><a:blip r:embed="%s"/><a:srcRect/><a:stretch/></p:blipFill><p:spPr><a:xfrm><a:off x="%d" y="%d"/><a:ext cx="%d" cy="%d"/></a:xfrm>'
        '<a:prstGeom prst="rect"><a:avLst/></a:prstGeom></p:spPr></p:pic>' % (NS, sid, escape(name), idx, rid, x, y, w, h))


def estimate_height(text, width_emu):
    """height of a 12 pt text box for the text (EMU): about 6 pt per character, 14.4 pt per line, 7.2 pt padding top and bottom"""
    cpl = max(4, int((width_emu / 12700.0 - 14.4) / 7.0)); words = text.split(); lines, cur = 1, 0
    for w in words:
        n = len(w) + (1 if cur else 0)
        if cur + n > cpl and cur: lines += 1; cur = len(w)
        else: cur += n
    return int((lines * 14.4 + 14.4) * 12700)


def put_content_slide(prs, tpl, pack_dir, slide, process_title, act_label, counter):
    new = clone(prs, tpl)
    t = ph(new, 0); set_text(t, process_title)
    a = ph(new, 13); set_text(a, act_label)
    for sh in new.shapes:
        if sh.name == 'Task line':
            label = slide['task'] or slide.get('taskTitle', '')
            set_text(sh, label + (' (continued)' if slide.get('continued') else ''))
    img = os.path.join(pack_dir, slide['image']); pw, ph_px = int(slide['width']), int(slide['height'])
    s = min(FRAME_W / float(pw), FRAME_H / float(ph_px)); fw, fh = int(round(pw * s)), int(round(ph_px * s))
    left, top = int(round(FRAME_CX - fw / 2.0)), int(round(FRAME_TOP + (FRAME_H - fh) / 2.0))
    # the picture goes into the template's content placeholder, like Insert Picture does
    old = ph(new, 15); tree = new.shapes._spTree
    _, rid = new.part.get_or_add_image_part(img)
    pic = pic_placeholder_xml(next_id(new), 'Content Placeholder 15', rid, left, top, fw, fh)
    if old is not None: old._element.addprevious(pic); old._element.getparent().remove(old._element)
    else: tree.append(pic)
    k = fw / float(pw)                       # EMU per picture pixel
    right_edge = left + fw
    boxes, texts = [], []
    for st in slide['steps']:
        b = st.get('box'); bx = None
        if b:
            bx = (left + int(b['x'] * k), top + int(b['y'] * k), max(40000, int(b['w'] * k)), max(40000, int(b['h'] * k))); boxes.append(bx)
        tb = st.get('textBox'); txt = (st.get('text') or '').strip()
        if not (tb and txt): continue
        counter[0] += 1
        body = '%d. %s' % (st.get('index', len(texts) + 1), txt)
        # where the author put it in the editor (beside its number by default, even outside the screenshot): the same place on the slide
        w = min(3000000, max(1150000, int(tb['w'] * k))); x = left + int(tb['x'] * k); y = top + int(tb['y'] * k); align = 'l'
        if tb.get('outside') and tb['x'] + tb['w'] / 2.0 < 0:        # outside on the left: keep its right edge, text right aligned
            right = left + int((tb['x'] + tb['w']) * k); x = right - w; align = 'r'
            if x < 150000: x = 150000; w = max(600000, right - x)       # not enough room on the slide: narrower, not shifted over the picture
        x = max(150000, min(SLIDE_W - 150000 - w, x))
        texts.append([counter[0], x, y, w, estimate_height(body, w), body, align, True])
    for n, x, y, w, h, body, align, _ in sorted(texts, key=lambda r: r[0]): tree.append(text_xml(next_id(new), 'Action %d' % n, x, max(1500000, y), w, h, body, align))
    for i, (x, y, w, h) in enumerate(boxes): tree.append(rect_xml(next_id(new), 'Rectangle: Rounded Corners %d' % (i + 1), x, y, w, h))
    return new


def put_divider(prs, tpl, process_label, activity, number):
    new = clone(prs, tpl)
    set_text(ph(new, 16), process_label); set_text(ph(new, 0), activity['title']); set_text(ph(new, 17), number)
    return new


def put_diagram(prs, tpl, pack_dir, process_label, activity, number, diagram):
    new = clone(prs, tpl)
    set_text(ph(new, 16), process_label); set_text(ph(new, 0), activity['title']); set_text(ph(new, 17), number)
    from PIL import Image
    path = os.path.join(pack_dir, diagram['image']); iw, ih = Image.open(path).size
    bx, by, bw, bh = DIAG_BOX; s = min(bw / float(iw), bh / float(ih)); w, h = int(iw * s), int(ih * s)
    x, y = bx + (bw - w) // 2, by + (bh - h) // 2
    new.shapes.add_picture(path, Emu(x), Emu(y), Emu(w), Emu(h)).name = 'Picture 5'
    hl = diagram.get('highlight')
    if hl:      # the activity's node as fractions of the picture: centre (cx, cy) and, when known, size (w, h); else the completed decks' fixed size
        cx, cy = x + float(hl['cx']) * w, y + float(hl['cy']) * h
        if hl.get('w') and hl.get('h'): hw, hh = int(float(hl['w']) * w), int(float(hl['h']) * h)
        else: hw, hh = int(HL_W * w / float(DIAG_BOX[2])), int(HL_H * w / float(DIAG_BOX[2]))
        new.shapes._spTree.append(rect_xml(next_id(new), 'Rectangle: Rounded Corners 6', int(cx - hw / 2), int(cy - hh / 2), hw, hh, adj=0, line=28575))
    return new


def main():
    ap = argparse.ArgumentParser(); ap.add_argument('packs', nargs='+'); ap.add_argument('--template'); ap.add_argument('--out')
    ap.add_argument('--append'); ap.add_argument('--replace', action='store_true')
    ap.add_argument('--author', default=''); ap.add_argument('--date', default=''); ap.add_argument('--email', default='')
    a = ap.parse_args()
    packs = []
    for d in a.packs:
        f = os.path.join(d, 'slides.json')
        if not os.path.exists(f): die('no slides.json in ' + d)
        j = json.load(open(f, encoding='utf-8-sig'))
        if j.get('schemaVersion') != 2: die('slides.json in %s has schemaVersion %s; this builder needs 2. Export the pack again from XLCapture.' % (d, j.get('schemaVersion')))
        packs.append((d, j))
    if a.append: return main_append(a, packs)
    first_dir, first = packs[0]
    tpl_path = a.template or (os.path.join(first_dir, first['template']) if first.get('template') else None)
    if not tpl_path or not os.path.exists(tpl_path): die('no template: give --template or export the pack with a template')
    prs = Presentation(tpl_path); roles = role_slides(prs)
    for need in ('cover', 'divider', 'content', 'closing'):
        if need not in roles: die('the template has no example slide for "%s" (looked at layout names)' % need)
    originals = list(prs.slides)

    proc = first.get('process') or {}
    p_code, p_title = (proc.get('code') or '').strip(), (proc.get('title') or '').strip()
    if not p_title: p_title = (first.get('activity') or {}).get('title') or first.get('name') or 'Process'
    process_label = (p_code + ' ' + p_title).strip()

    # cover and closing are edited in place
    cover = roles['cover']; set_text(ph(cover, 0), process_label)
    au = ph(cover, 11)
    if au is not None: set_text(au, a.author)
    closing = roles['closing']; d = ph(closing, 73)
    if d is not None: set_text(d, a.date)
    first_mail = True
    for sh in closing.shapes:      # the template has two e-mail lines: the author's goes on the first, the second is left empty
        if sh.has_text_frame and 'email@' in sh.text_frame.text: set_text(sh, a.email if first_mail else ''); first_mail = False

    created = []
    for pack_dir, j in packs:
        act = j.get('activity') or {}
        act_title = act.get('title') or j.get('name') or 'Activity'; act_code = (act.get('code') or '').strip()
        number = ('%02d' % int(act_code)) if act_code.isdigit() else act_code
        act_label = (act_code + ' ' + act_title).strip()
        created.append(put_divider(prs, roles['divider'], process_label, {'title': act_title}, number))
        if j.get('diagram') and 'diagram' in roles:
            created.append(put_diagram(prs, roles['diagram'], pack_dir, process_label, {'title': act_title}, number, j['diagram']))
        counter = [0]
        for s in j['slides']:
            created.append(put_content_slide(prs, roles['content'], pack_dir, s, p_title, act_label, counter))

    # the template's other example slides (including the instructions) go; the cover stays first and the closing slide last
    for s in originals:
        if s is not cover and s is not closing: drop(prs, s)
    lst = prs.slides._sldIdLst
    for sid in list(lst):
        if prs.part.related_part(sid.rId) is cover.part: lst.remove(sid); lst.insert(0, sid)
    for sid in list(lst):
        if prs.part.related_part(sid.rId) is closing.part: lst.remove(sid); lst.append(sid)

    out = a.out or os.path.join(first_dir, ((first.get('activity') or {}).get('title') or first.get('name') or 'slide pack') + ' - slide pack.pptx')
    prs.save(out)
    print('Saved %s (%d slides: cover, %d built, closing)' % (out, len(prs.slides), len(created)))


if __name__ == '__main__':
    main()
