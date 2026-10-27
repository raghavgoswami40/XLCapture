// The two windows around the slide pack export: what to build (a new deck from the template, or a new activity added to an existing deck)
// and the "saved" window that has the prompt for Claude with a button to copy it again.
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace SnapLite
{
    class SlidePackOptionsForm : Form
    {
        public string Template = "", Existing = "";
        readonly RadioButton rNew, rAdd; readonly Label tplLabel, deckLabel; readonly FlatButton tplBtn, deckBtn, ok;
        public SlidePackOptionsForm(string template, string existing)
        {
            Text = "Slide pack"; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false; Icon = AppIcon.Get();
            StartPosition = FormStartPosition.CenterParent; BackColor = Color.White; Font = Theme.Body(9.5f); ClientSize = Dpi.S(640, 388);
            Template = template ?? ""; Existing = existing ?? "";
            var head = new Label { AutoSize = false, Location = new Point(Dpi.S(24), Dpi.S(20)), Size = Dpi.S(592, 30), Font = Theme.Semi(15f), ForeColor = Theme.Ink, Text = "Build the slide pack" };
            var sub = new Label { AutoSize = false, Location = new Point(Dpi.S(24), Dpi.S(54)), Size = Dpi.S(592, 40), ForeColor = Theme.Slate, Text = "XLCapture saves a folder with one picture per screen and the step texts. Claude then builds the PowerPoint from it." };
            rNew = new RadioButton { Text = "Make a new deck from the slide template", Location = new Point(Dpi.S(24), Dpi.S(108)), Size = Dpi.S(592, 26), Checked = true, AccessibleName = "New deck" };
            tplBtn = new FlatButton("Choose the template...", false) { Location = new Point(Dpi.S(48), Dpi.S(142)), Size = Dpi.S(210, 36), Icon = Ic.Open };
            tplLabel = new Label { AutoSize = false, Location = new Point(Dpi.S(270), Dpi.S(149)), Size = Dpi.S(346, 24), AutoEllipsis = true };
            rAdd = new RadioButton { Text = "Add this activity to an existing deck", Location = new Point(Dpi.S(24), Dpi.S(206)), Size = Dpi.S(592, 26), AccessibleName = "Existing deck" };
            deckBtn = new FlatButton("Choose the deck...", false) { Location = new Point(Dpi.S(48), Dpi.S(240)), Size = Dpi.S(210, 36), Icon = Ic.Open };
            deckLabel = new Label { AutoSize = false, Location = new Point(Dpi.S(270), Dpi.S(247)), Size = Dpi.S(346, 24), AutoEllipsis = true };
            var note = new Label { AutoSize = false, Location = new Point(Dpi.S(48), Dpi.S(282)), Size = Dpi.S(568, 40), ForeColor = Theme.Slate, Font = Theme.Body(9f), Text = "The activity goes in its place by number, in the deck's own look. Your deck is not changed: the result is saved as a new copy." };
            ok = new FlatButton("Next", true) { Location = new Point(Dpi.S(404), Dpi.S(330)), Size = Dpi.S(110, 38) };
            var cancel = new FlatButton("Cancel", false) { Location = new Point(Dpi.S(522), Dpi.S(330)), Size = Dpi.S(94, 38) };
            Controls.AddRange(new Control[] { head, sub, rNew, tplBtn, tplLabel, rAdd, deckBtn, deckLabel, note, ok, cancel });
            tplBtn.Click += delegate { string f = Pick("Choose the slide template", "PowerPoint template or deck|*.pptx;*.potx|All files|*.*"); if (f != null) { Template = f; rNew.Checked = true; Sync(); } };
            deckBtn.Click += delegate { string f = Pick("Choose the existing deck", "PowerPoint deck|*.pptx|All files|*.*"); if (f != null) { Existing = f; rAdd.Checked = true; Sync(); } };
            rNew.CheckedChanged += delegate { Sync(); }; rAdd.CheckedChanged += delegate { Sync(); };
            ok.Click += delegate { DialogResult = DialogResult.OK; Close(); };
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            CancelButton = cancel; Sync();
        }
        string Pick(string title, string filter) { using (var o = new OpenFileDialog { Title = title, Filter = filter }) return o.ShowDialog(this) == DialogResult.OK ? o.FileName : null; }
        void Sync()
        {
            tplLabel.Text = Template.Length == 0 ? "No template chosen" : Path.GetFileName(Template); tplLabel.ForeColor = File.Exists(Template) ? Theme.Ink : Theme.Slate;
            deckLabel.Text = Existing.Length == 0 ? "No deck chosen" : Path.GetFileName(Existing); deckLabel.ForeColor = File.Exists(Existing) ? Theme.Ink : Theme.Slate;
            bool add = rAdd.Checked; ok.Enabled = add ? File.Exists(Existing) : File.Exists(Template);
        }
        // "" for a new deck, else the existing deck's path
        public bool AddToExisting { get { return rAdd.Checked; } }
        public string ExistingChosen { get { return rAdd.Checked ? Existing : ""; } }
    }

    // shown when the folder is saved: the prompt is already on the clipboard, and can be copied again
    class SlidePackDoneForm : Form
    {
        public SlidePackDoneForm(string dir, string prompt, bool copied)
        {
            Text = "Slide pack saved"; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false; Icon = AppIcon.Get();
            StartPosition = FormStartPosition.CenterParent; BackColor = Color.White; Font = Theme.Body(9.5f); ClientSize = Dpi.S(620, 292);
            var head = new Label { AutoSize = false, Location = new Point(Dpi.S(24), Dpi.S(20)), Size = Dpi.S(572, 30), Font = Theme.Semi(15f), ForeColor = Theme.Ink, Text = copied ? "Prompt copied to the clipboard" : "Slide pack saved" };
            var where = new Label { AutoSize = false, Location = new Point(Dpi.S(24), Dpi.S(56)), Size = Dpi.S(572, 40), ForeColor = Theme.Slate, Text = "The slide pack is saved in:\n" + dir, AutoEllipsis = true };
            var box = new TextBox { Location = new Point(Dpi.S(24), Dpi.S(104)), Size = Dpi.S(572, 96), Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.FixedSingle, BackColor = Theme.Sheet, Text = prompt, AccessibleName = "The prompt for Claude" };
            var hint = new Label { AutoSize = false, Location = new Point(Dpi.S(24), Dpi.S(206)), Size = Dpi.S(572, 24), ForeColor = Theme.Slate, Text = "Paste it into Claude Code to build the deck." };
            var copy = new FlatButton("Copy prompt", true) { Location = new Point(Dpi.S(24), Dpi.S(238)), Size = Dpi.S(150, 38), Icon = Ic.Copy };
            var open = new FlatButton("Open folder", false) { Location = new Point(Dpi.S(184), Dpi.S(238)), Size = Dpi.S(140, 38), Icon = Ic.Folder };
            var close = new FlatButton("Close", false) { Location = new Point(Dpi.S(504), Dpi.S(238)), Size = Dpi.S(92, 38) };
            Controls.AddRange(new Control[] { head, where, box, hint, copy, open, close });
            copy.Click += delegate
            {
                try { Clipboard.SetText(prompt); copy.Text = "Copied"; head.Text = "Prompt copied to the clipboard"; }
                catch (Exception ex) { copy.Text = "Copy failed"; hint.Text = "Could not use the clipboard: " + ex.Message; }
            };
            open.Click += delegate { try { Process.Start("explorer.exe", "\"" + dir + "\""); } catch { } };
            close.Click += delegate { DialogResult = DialogResult.OK; Close(); };
            AcceptButton = close; CancelButton = close;
            Shown += delegate { box.Select(0, 0); copy.Focus(); };
        }
    }
}
