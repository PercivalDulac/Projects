using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

using com.lufiaIIEditor.RomInfo;

namespace com.lufiaIIEditor.UI
{
    /// <summary>"Dialogue" tab: story text, edited in place (see DialogueData).</summary>
    class DialogueTab : TabPage
    {
        private DialogueData dlg;
        private List<int> shown = new List<int>();
        private int current = -1;
        private bool loading;
        private string[] cache;          // decoded text per line (current ROM)

        private TextBox textFilter, textEdit, textFind;
        private CheckBox checkChanged;
        private ListBox listLines, listWords;
        private Label labelCount, labelTitle, labelLines, labelProblem, labelBytes, labelReuse;
        private Panel preview;
        private Button buttonRestore;

        public DialogueTab()
        {
            this.Text = "Dialogue";
            this.UseVisualStyleBackColor = true;
            Build();
            this.Enabled = false;
        }

        private void Build()
        {
            Controls.Add(UiHelpers.MakeLabel("Find:", 12, 15));
            textFilter = new TextBox();
            textFilter.Location = new Point(50, 12);
            textFilter.Size = new Size(180, 20);
            textFilter.TextChanged += delegate { FillList(); };
            Controls.Add(textFilter);
            checkChanged = new CheckBox();
            checkChanged.Text = "Changed only";
            checkChanged.Location = new Point(240, 13);
            checkChanged.AutoSize = true;
            checkChanged.CheckedChanged += delegate { FillList(); };
            Controls.Add(checkChanged);
            labelCount = UiHelpers.MakeNote("", 12, 40);
            Controls.Add(labelCount);

            listLines = new ListBox();
            listLines.Location = new Point(12, 58);
            listLines.Size = new Size(330, 450);
            listLines.IntegralHeight = false;
            listLines.SelectedIndexChanged += delegate { if (!loading) SelectLine(listLines.SelectedIndex); };
            Controls.Add(UiHelpers.Anchor(this, listLines, AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left));

            labelTitle = UiHelpers.MakeLabel("", 355, 15);
            labelTitle.Font = new Font(labelTitle.Font, FontStyle.Bold);
            Controls.Add(labelTitle);

            textEdit = new TextBox();
            textEdit.Multiline = true;
            textEdit.AcceptsReturn = true;
            textEdit.WordWrap = false;
            textEdit.ScrollBars = ScrollBars.Horizontal;
            textEdit.Font = new Font(FontFamily.GenericMonospace, 11f);
            textEdit.Location = new Point(355, 40);
            textEdit.Size = new Size(320, 100);
            textEdit.TextChanged += delegate { if (!loading) UpdateInfo(); };
            textEdit.Leave += delegate { Store(); };
            Controls.Add(textEdit);
            labelLines = UiHelpers.MakeNote("", 683, 42);
            Controls.Add(labelLines);

            labelProblem = UiHelpers.MakeLabel("", 355, 146);
            labelProblem.ForeColor = Color.DarkRed;
            labelProblem.AutoSize = false;
            labelProblem.Size = new Size(420, 32);
            Controls.Add(labelProblem);
            labelBytes = UiHelpers.MakeLabel("", 355, 180);
            labelBytes.Font = new Font(labelBytes.Font, FontStyle.Bold);
            Controls.Add(labelBytes);

            Controls.Add(UiHelpers.MakeNote("In game:", 355, 204));
            preview = new Panel();
            preview.Location = new Point(355, 220);
            preview.Size = new Size(320, 100);
            preview.BackColor = Color.FromArgb(24, 40, 120);
            preview.Paint += new PaintEventHandler(preview_Paint);
            Controls.Add(preview);

            buttonRestore = new Button();
            buttonRestore.Text = "Restore original";
            buttonRestore.Location = new Point(355, 328);
            buttonRestore.Size = new Size(110, 26);
            buttonRestore.Click += delegate
            {
                if (current < 0) return;
                string r = dlg.GetReferenceText(dlg.Lines[current]);
                if (r != null) textEdit.Text = r.Replace("\n", "\r\n");
            };
            Controls.Add(buttonRestore);
            labelReuse = UiHelpers.MakeNote("", 472, 328);
            labelReuse.AutoSize = false;
            labelReuse.Size = new Size(305, 32);
            Controls.Add(labelReuse);

            Label n = UiHelpers.MakeNote(
                "Each line keeps exactly its original size in the ROM (the text sits inside the game's event scripts, " +
                "so nothing can move). Shorter is always fine; longer only fits if it compresses better (use words from " +
                "the word finder). Press Enter for a new line; the game doesn't wrap.\n" +
                "Tags: [Hero] = the hero's name. [pause N], [voice N] and tags like [02:08] are game codes: " +
                "keep them as they are. \"?\", \"!\", \",\" or \".\" at the end of a line are stored in one byte.", 355, 362);
            n.AutoSize = false;
            n.Size = new Size(420, 140);
            Controls.Add(n);

            GroupBox gWords = UiHelpers.MakeGroup("Word finder", 785, 12, 190, 496);
            Controls.Add(UiHelpers.Anchor(this, gWords, AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right));
            gWords.Controls.Add(UiHelpers.MakeNote("Cheap words. Double-click\nto insert (· = space).", 8, 20));
            textFind = new TextBox();
            textFind.Location = new Point(8, 52);
            textFind.Size = new Size(170, 20);
            textFind.TextChanged += delegate
            {
                listWords.BeginUpdate();
                listWords.Items.Clear();
                if (dlg != null) foreach (string s in dlg.FindWords(textFind.Text, 300)) listWords.Items.Add(s);
                listWords.EndUpdate();
            };
            gWords.Controls.Add(textFind);
            listWords = new ListBox();
            listWords.Location = new Point(8, 80);
            listWords.Size = new Size(172, 405);
            listWords.IntegralHeight = false;
            listWords.HorizontalScrollbar = false;
            listWords.DoubleClick += delegate
            {
                if (current < 0 || listWords.SelectedItem == null) return;
                string s = listWords.SelectedItem.ToString();
                int cut = s.IndexOf("  -  ");
                if (cut > 0) s = s.Substring(0, cut);
                textEdit.SelectedText = s.Replace("·", " ");
                textEdit.Focus();
            };
            gWords.Controls.Add(UiHelpers.Anchor(this, listWords, AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right));
        }

        public void LoadRom(DialogueData d)
        {
            dlg = d;
            current = -1;
            cache = new string[dlg.Lines.Count];
            for (int i = 0; i < cache.Length; i++) cache[i] = dlg.GetText(dlg.Lines[i]) ?? "";
            this.Enabled = true;
            FillList();
        }

        public void Disable()
        {
            dlg = null; current = -1;
            listLines.Items.Clear();
            this.Enabled = false;
        }

        private string LineLabel(int i)
        {
            string t = cache[i].Replace("\n", " ");
            if (t.Length > 50) t = t.Substring(0, 48) + "...";
            return (Changed(i) ? "* " : "  ") + "#" + (i + 1).ToString("D4") + "  " + t;
        }

        private bool Changed(int i)
        {
            string r = dlg.GetReferenceText(dlg.Lines[i]);
            return r != null && r != cache[i];
        }

        private void FillList()
        {
            if (dlg == null) return;
            Store();
            string f = textFilter.Text.Trim();
            int keep = current;
            loading = true;
            listLines.BeginUpdate();
            listLines.Items.Clear();
            shown.Clear();
            for (int i = 0; i < cache.Length; i++)
            {
                if (f.Length > 0 && cache[i].IndexOf(f, StringComparison.OrdinalIgnoreCase) < 0 &&
                    ("#" + (i + 1).ToString("D4")).IndexOf(f, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (checkChanged.Checked && !Changed(i)) continue;
                shown.Add(i);
                listLines.Items.Add(LineLabel(i));
            }
            listLines.EndUpdate();
            labelCount.Text = shown.Count + " of " + cache.Length + " lines";
            loading = false;
            int at = shown.IndexOf(keep);
            if (at < 0 && shown.Count > 0) at = 0;
            current = -1;
            if (at >= 0) listLines.SelectedIndex = at; else ShowLine(-1);
        }

        private void SelectLine(int listIndex)
        {
            Store();
            ShowLine(listIndex >= 0 && listIndex < shown.Count ? shown[listIndex] : -1);
        }

        private void ShowLine(int i)
        {
            loading = true;
            current = i;
            bool on = i >= 0;
            textEdit.Enabled = buttonRestore.Enabled = on;
            if (on)
            {
                DialogueData.Line l = dlg.Lines[i];
                string who = l.Opcode == 0x13 || l.Opcode == 0x9E ? "  (speaker/style " + l.Param.ToString("X2") + ")" : "";
                labelTitle.Text = "Line #" + (i + 1).ToString("D4") + " at " + dlg.Where(l) + who;
                textEdit.Text = cache[i].Replace("\n", "\r\n");
                List<int> reuse = dlg.ReusedBy(l);
                if (reuse.Count == 0) labelReuse.Text = "";
                else
                {
                    List<string> ids = new List<string>();
                    foreach (int x in reuse) ids.Add("#" + (x + 1).ToString("D4"));
                    labelReuse.Text = "Part of this line is reused by line " + string.Join(", ", ids.ToArray()) +
                        ". Those are kept as they are when you change this one (if they still fit).";
                }
            }
            else { labelTitle.Text = ""; textEdit.Text = ""; labelReuse.Text = ""; }
            loading = false;
            UpdateInfo();
        }

        private string Edited() { return textEdit.Text.Replace("\r\n", "\n").Replace('\r', '\n'); }

        private int OriginalWidest(int i)
        {
            string r = dlg.GetReferenceText(dlg.Lines[i]) ?? cache[i];
            return DialogueData.Widest(r);
        }

        private int OriginalLines(int i)
        {
            string r = dlg.GetReferenceText(dlg.Lines[i]) ?? cache[i];
            return DialogueData.LineCount(r);
        }

        private void UpdateInfo()
        {
            preview.Invalidate();
            if (current < 0 || dlg == null) { labelLines.Text = labelProblem.Text = labelBytes.Text = ""; return; }
            string t = Edited();
            string[] lines = DialogueData.Preview(t).Split('\n');
            StringBuilder sb = new StringBuilder();
            int limit = Math.Max(DialogueData.MaxLineLetters, OriginalWidest(current));
            for (int k = 0; k < lines.Length; k++)
                sb.Append("Line " + (k + 1) + ": " + lines[k].Length + "/" + limit + (lines[k].Length > limit ? "  too long" : "") + "\n");
            labelLines.Text = sb.ToString();

            DialogueData.Line l = dlg.Lines[current];
            string problem = dlg.Problem(t, OriginalWidest(current), OriginalLines(current));
            string err = null; int min = 0;
            if (problem == null) dlg.Encode(t, l.Length, out err, out min);
            labelBytes.Text = problem == null && min != int.MaxValue
                ? "Space: needs " + min + " of " + l.Length + " bytes" + (min <= l.Length ? "" : "  (" + (min - l.Length) + " over)")
                : "Space: " + l.Length + " bytes";
            labelBytes.ForeColor = problem == null && err == null ? SystemColors.ControlText : Color.DarkRed;
            labelProblem.Text = problem != null ? "Not saved: " + problem + "." : err != null ? "Not saved: " + err + ". Shorten it or use cheaper words." : "";
            string orig = dlg.GetReferenceText(l);
            textEdit.BackColor = orig != null && orig != t ? ChangeHighlighter.ChangedColor : SystemColors.Window;
        }

        /// <summary>Writes the text being edited, if it's valid and fits.</summary>
        private void Store()
        {
            if (dlg == null || current < 0 || loading) return;
            string t = Edited();
            if (t == cache[current]) return;
            DialogueData.Line l = dlg.Lines[current];
            if (dlg.Problem(t, OriginalWidest(current), OriginalLines(current)) != null) return;
            string err, err2; int min2;
            if (!dlg.SetText(l, t, out err))
            {
                if (dlg.Encode(t, l.Length, out err2, out min2) != null)   // fits, so the reason is reused text
                    MessageBox.Show("Line #" + (current + 1).ToString("D4") + " wasn't changed: " + err + ".",
                        "Dialogue", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            cache[current] = dlg.GetText(l) ?? t;
            foreach (int x in dlg.ReusedBy(l)) cache[x] = dlg.GetText(dlg.Lines[x]) ?? cache[x];
            int at = shown.IndexOf(current);
            if (at >= 0)
            {
                bool was = loading; loading = true;
                listLines.Items[at] = LineLabel(current);
                loading = was;
            }
        }

        public void CommitCurrent() { Store(); }

        public void RefreshView()
        {
            if (dlg == null) return;
            for (int i = 0; i < cache.Length; i++) cache[i] = dlg.GetText(dlg.Lines[i]) ?? "";
            if (current >= 0) ShowLine(current);
        }

        private void preview_Paint(object sender, PaintEventArgs e)
        {
            if (current < 0 || dlg == null) return;
            string[] lines = DialogueData.Preview(Edited()).Split('\n');
            int limit = Math.Max(DialogueData.MaxLineLetters, OriginalWidest(current));
            using (Font f = new Font(FontFamily.GenericMonospace, 10f))
            {
                Size cell = TextRenderer.MeasureText(e.Graphics, "M", f, new Size(100, 100), TextFormatFlags.NoPadding);
                for (int y = 0; y < lines.Length; y++)
                    for (int x = 0; x < lines[y].Length; x++)
                    {
                        Color c = x >= limit || y >= DialogueData.MaxLines ? Color.Red : Color.White;
                        TextRenderer.DrawText(e.Graphics, lines[y][x].ToString(), f,
                            new Point(6 + x * cell.Width, 5 + y * (cell.Height + 2)), c, TextFormatFlags.NoPadding);
                    }
            }
        }
    }
}
