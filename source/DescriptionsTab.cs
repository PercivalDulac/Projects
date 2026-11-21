using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Windows.Forms;

using com.lufiaIIEditor.RomInfo;

namespace com.lufiaIIEditor.UI
{
    /// <summary>
    /// "Descriptions" tab: the in-game help text of items, spells and IP moves (see DescriptionData).
    /// Plain text in, compressed with the game's own word lists on save.
    /// </summary>
    class DescriptionsTab : TabPage
    {
        private class Entry { public int Index; public string Label; public int Kind; public int Id; }
        private const int KIND_ITEM = 0, KIND_SPELL = 1, KIND_IP = 2;

        private DescriptionData desc;
        private ItemData items;
        private IPData ip;
        private Rom rom;
        private List<Entry> entries = new List<Entry>();
        private List<Entry> shown = new List<Entry>();
        private Entry current;
        private bool loading;

        private ComboBox comboKind;
        private TextBox textFilter;
        private ListBox listEntries;
        private Label labelTitle, labelLines, labelProblem, labelBytes, labelSpace;
        private TextBox textEdit;
        private Panel preview;
        private TextBox textFind;
        private ListBox listWords;
        private Button buttonSuggest, buttonRestore;

        public DescriptionsTab()
        {
            this.Text = "Descriptions";
            this.UseVisualStyleBackColor = true;
            Build();
            this.Enabled = false;
        }

        private void Build()
        {
            // ---- left: list ----
            Controls.Add(UiHelpers.MakeLabel("Show:", 12, 15));
            comboKind = UiHelpers.MakeDropDown(55, 12, 120);
            comboKind.Items.AddRange(new object[] { "Items", "Spells", "IP moves", "Everything" });
            comboKind.SelectedIndex = 0;
            comboKind.SelectedIndexChanged += delegate { FillList(); };
            Controls.Add(comboKind);
            Controls.Add(UiHelpers.MakeLabel("Find:", 12, 43));
            textFilter = new TextBox();
            textFilter.Location = new Point(55, 40);
            textFilter.Size = new Size(170, 20);
            textFilter.TextChanged += delegate { FillList(); };
            Controls.Add(textFilter);
            listEntries = new ListBox();
            listEntries.Location = new Point(12, 68);
            listEntries.Size = new Size(213, 440);
            listEntries.IntegralHeight = false;
            listEntries.SelectedIndexChanged += new EventHandler(listEntries_SelectedIndexChanged);
            Controls.Add(UiHelpers.Anchor(this, listEntries, AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left));

            // ---- middle: editor ----
            labelTitle = UiHelpers.MakeLabel("", 240, 15);
            labelTitle.Font = new Font(labelTitle.Font, FontStyle.Bold);
            Controls.Add(labelTitle);

            textEdit = new TextBox();
            textEdit.Multiline = true;
            textEdit.AcceptsReturn = true;
            textEdit.WordWrap = false;
            textEdit.Font = new Font(FontFamily.GenericMonospace, 11f);
            textEdit.Location = new Point(240, 40);
            textEdit.Size = new Size(300, 90);
            textEdit.TextChanged += new EventHandler(textEdit_TextChanged);
            Controls.Add(textEdit);

            labelLines = UiHelpers.MakeNote("", 548, 42);
            Controls.Add(labelLines);

            labelProblem = UiHelpers.MakeLabel("", 240, 136);
            labelProblem.ForeColor = Color.DarkRed;
            Controls.Add(labelProblem);

            Controls.Add(UiHelpers.MakeNote("In game:", 240, 160));
            preview = new Panel();
            preview.Location = new Point(240, 176);
            preview.Size = new Size(300, 92);
            preview.BackColor = Color.FromArgb(24, 40, 120);
            preview.Paint += new PaintEventHandler(preview_Paint);
            Controls.Add(preview);
            Label shade = UiHelpers.MakeNote("White = stored as a word (cheap). Orange = spelled out, 1 byte per letter.", 240, 272);
            shade.AutoSize = false;
            shade.Size = new Size(385, 28);
            Controls.Add(shade);

            labelBytes = UiHelpers.MakeLabel("", 240, 302);
            Controls.Add(labelBytes);
            labelSpace = UiHelpers.MakeLabel("", 240, 322);
            labelSpace.Font = new Font(labelSpace.Font, FontStyle.Bold);
            Controls.Add(labelSpace);

            buttonSuggest = new Button();
            buttonSuggest.Text = "Suggest";
            buttonSuggest.Location = new Point(240, 358);
            buttonSuggest.Size = new Size(90, 26);
            buttonSuggest.Click += new EventHandler(buttonSuggest_Click);
            Controls.Add(buttonSuggest);
            new ToolTip().SetToolTip(buttonSuggest,
                "Writes a factual draft from what the editor knows (bonuses, elements, effects).\nEdit it into your own words.");
            buttonRestore = new Button();
            buttonRestore.Text = "Restore original";
            buttonRestore.Location = new Point(338, 358);
            buttonRestore.Size = new Size(110, 26);
            buttonRestore.Click += new EventHandler(buttonRestore_Click);
            Controls.Add(buttonRestore);

            Label note = UiHelpers.MakeNote(
                "The box shows 26 letters on up to 4 lines. The game doesn't wrap text, so press Enter " +
                "for a new line. Identical descriptions are stored once. All descriptions share one block " +
                "of space: if a change doesn't fit, shorten this text or another one. Edits that are too " +
                "long are not saved.", 240, 394);
            note.AutoSize = false;
            note.Size = new Size(385, 75);
            Controls.Add(note);

            // ---- right: word finder ----
            GroupBox gWords = UiHelpers.MakeGroup("Word finder", 640, 12, 335, 496);
            Controls.Add(UiHelpers.Anchor(this, gWords, AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right));
            gWords.Controls.Add(UiHelpers.MakeNote("Words the game stores cheaply. Double-click one\nto insert it at the cursor (· = a space).", 10, 20));
            textFind = new TextBox();
            textFind.Location = new Point(10, 52);
            textFind.Size = new Size(200, 20);
            textFind.TextChanged += delegate { FindWords(); };
            gWords.Controls.Add(textFind);
            listWords = new ListBox();
            listWords.Location = new Point(10, 80);
            listWords.Size = new Size(315, 405);
            listWords.IntegralHeight = false;
            listWords.DoubleClick += new EventHandler(listWords_DoubleClick);
            gWords.Controls.Add(UiHelpers.Anchor(this, listWords, AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right));
        }

        public void LoadRom(DescriptionData d, ItemData itemData, IPData ipData, Rom romData)
        {
            desc = d; items = itemData; ip = ipData; rom = romData;
            entries.Clear();
            for (int id = 1; id < DescriptionData.ItemCount && id < ItemData.Count; id++)
                entries.Add(MakeEntry(id, KIND_ITEM, id, items.GetName(id).Trim() + "  (" + id.ToString("X3") + ")"));
            string[] sp = rom.SpellNames;
            for (int s = 0; sp != null && s < sp.Length && DescriptionData.SpellBase + s < DescriptionData.IPBase; s++)
                entries.Add(MakeEntry(DescriptionData.SpellBase + s, KIND_SPELL, s, sp[s].Trim() + "  (spell " + s + ")"));
            for (int id = 1; ip != null && id <= ip.Count && DescriptionData.IPBase + id < DescriptionData.Count; id++)
                entries.Add(MakeEntry(DescriptionData.IPBase + id, KIND_IP, id, ip.GetName(id).Trim() + "  (IP " + id + ")"));
            current = null;
            this.Enabled = true;
            FillList();
            UpdateSpace();
        }

        public void Disable()
        {
            desc = null; current = null;
            listEntries.Items.Clear();
            this.Enabled = false;
        }

        private Entry MakeEntry(int index, int kind, int id, string label)
        {
            Entry e = new Entry(); e.Index = index; e.Kind = kind; e.Id = id; e.Label = label;
            return e;
        }

        private static string OneLine(string t) { return t.Replace("\n", " "); }

        private string ListText(Entry e)
        {
            string t = desc.GetText(e.Index);
            string mark = IsChanged(e) ? "* " : "  ";
            return mark + e.Label + (t.Length == 0 ? "   (empty)" : "");
        }

        private DescriptionData refDesc;
        private byte[] refDescFor;

        private DescriptionData RefDesc()
        {
            if (Reference.Data == null) return null;
            if (refDesc == null || refDescFor != Reference.Data)
            {
                refDescFor = Reference.Data;
                refDesc = null;
                object o = Reference.Try(delegate
                {
                    DescriptionData r = new DescriptionData(Reference.Data, Reference.HasHeader);
                    return r.Load() == null ? r : null;
                });
                refDesc = o as DescriptionData;
            }
            return refDesc;
        }

        private bool IsChanged(Entry e)
        {
            DescriptionData r = RefDesc();
            return r != null && r.GetText(e.Index) != desc.GetText(e.Index);
        }

        private void FillList()
        {
            if (desc == null) return;
            CommitCurrent();
            string f = textFilter.Text.Trim();
            int kind = comboKind.SelectedIndex;
            loading = true;
            Entry keep = current;
            listEntries.BeginUpdate();
            listEntries.Items.Clear();
            shown.Clear();
            foreach (Entry e in entries)
            {
                if (kind < 3 && e.Kind != kind) continue;
                if (f.Length > 0 && e.Label.IndexOf(f, StringComparison.OrdinalIgnoreCase) < 0 &&
                    desc.GetText(e.Index).IndexOf(f, StringComparison.OrdinalIgnoreCase) < 0) continue;
                shown.Add(e);
                listEntries.Items.Add(ListText(e));
            }
            listEntries.EndUpdate();
            loading = false;
            int at = keep == null ? -1 : shown.IndexOf(keep);
            if (at < 0 && shown.Count > 0) at = 0;
            current = null;
            if (at >= 0) listEntries.SelectedIndex = at;
            else Show(null);
        }

        private void listEntries_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (loading || desc == null) return;
            CommitCurrent();
            int i = listEntries.SelectedIndex;
            Show(i >= 0 && i < shown.Count ? shown[i] : null);
        }

        private void Show(Entry e)
        {
            loading = true;
            current = e;
            bool on = e != null;
            textEdit.Enabled = buttonSuggest.Enabled = buttonRestore.Enabled = on;
            labelTitle.Text = on ? e.Label.Trim() : "";
            textEdit.Text = on ? desc.GetText(e.Index).Replace("\n", "\r\n") : "";
            loading = false;
            UpdateInfo();
        }

        /// <summary>Stores the text being edited, if it's valid and fits.</summary>
        public void CommitCurrent()
        {
            if (desc == null) return;
            if (current != null) Store(false);
            desc.Write();
        }

        private bool Store(bool showWhy)
        {
            string t = DescriptionData.Normalize(textEdit.Text);
            if (t == desc.GetText(current.Index)) return true;
            if (desc.Problem(t) != null) return false;
            string old = desc.GetText(current.Index);
            desc.SetText(current.Index, t);
            if (desc.Free < 0)
            {
                desc.SetText(current.Index, old);
                return false;
            }
            int i = shown.IndexOf(current);
            if (i >= 0 && i < listEntries.Items.Count)
            {
                bool was = loading; loading = true;
                listEntries.Items[i] = ListText(current);
                loading = was;
            }
            return true;
        }

        public void RefreshView()
        {
            if (desc == null) return;
            if (current != null) Show(current);
            UpdateSpace();
        }

        private void textEdit_TextChanged(object sender, EventArgs e)
        {
            if (loading || current == null) return;
            UpdateInfo();
        }

        private void UpdateInfo()
        {
            preview.Invalidate();
            if (current == null || desc == null)
            {
                labelLines.Text = ""; labelProblem.Text = ""; labelBytes.Text = "";
                return;
            }
            string t = DescriptionData.Normalize(textEdit.Text);
            string[] lines = t.Split('\n');
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < lines.Length; i++)
                sb.Append("Line " + (i + 1) + ": " + lines[i].Length + "/" + DescriptionData.LineLength +
                    (lines[i].Length > DescriptionData.LineLength ? "  too long" : "") + "\n");
            labelLines.Text = sb.ToString();
            labelLines.ForeColor = SystemColors.GrayText;

            string problem = desc.Problem(t);
            int bytes = desc.Encode(t).Length;
            labelBytes.Text = "This description: " + t.Replace("\n", "").Length + " letters stored in " + bytes + " bytes";

            // Would it fit?
            int over = 0;
            if (problem == null)
            {
                string old = desc.GetText(current.Index);
                desc.SetText(current.Index, t);
                over = -desc.Free;
                desc.SetText(current.Index, old);
            }
            if (problem != null) labelProblem.Text = "Not saved: " + problem + ".";
            else if (over > 0) labelProblem.Text = "Not saved: " + over + " bytes over the space left. Shorten this or another description.";
            else labelProblem.Text = "";
            DescriptionData r = RefDesc();
            textEdit.BackColor = (r != null && r.GetText(current.Index) != t) ? ChangeHighlighter.ChangedColor : SystemColors.Window;
            UpdateSpace(problem == null && over <= 0 ? t : null);
        }

        private void UpdateSpace() { UpdateSpace(null); }

        private void UpdateSpace(string pending)
        {
            if (desc == null) return;
            int free;
            if (pending != null && current != null)
            {
                string old = desc.GetText(current.Index);
                desc.SetText(current.Index, pending);
                free = desc.Free;
                desc.SetText(current.Index, old);
            }
            else free = desc.Free;
            labelSpace.Text = "Space: " + (desc.Capacity - free) + " of " + desc.Capacity + " bytes used, " + free + " free" +
                (desc.IsMoved ? "\n(in the expanded ROM)" : "\n(File > Expand ROM... gives about 31 KB)");
            labelSpace.ForeColor = free < 0 ? Color.DarkRed : free < 50 ? Color.DarkOrange : SystemColors.ControlText;
        }

        private void preview_Paint(object sender, PaintEventArgs e)
        {
            if (current == null || desc == null) return;
            string t = DescriptionData.Normalize(textEdit.Text);
            bool[] lit = desc.LiteralMask(t);
            using (Font f = new Font(FontFamily.GenericMonospace, 11f))
            {
                Size cell = TextRenderer.MeasureText(e.Graphics, "M", f, new Size(100, 100), TextFormatFlags.NoPadding);
                int x0 = 8, y0 = 6, x = 0, y = 0;
                for (int i = 0; i < t.Length; i++)
                {
                    char c = t[i];
                    if (c == '\n') { x = 0; y++; continue; }
                    Color col = x >= DescriptionData.LineLength || y >= DescriptionData.MaxLines ? Color.Red
                        : (i < lit.Length && lit[i] && c != ' ') ? Color.FromArgb(255, 190, 110) : Color.White;
                    TextRenderer.DrawText(e.Graphics, c.ToString(), f, new Point(x0 + x * cell.Width, y0 + y * (cell.Height + 2)), col,
                        TextFormatFlags.NoPadding);
                    x++;
                }
                using (Pen p = new Pen(Color.FromArgb(90, 110, 190)))
                    e.Graphics.DrawRectangle(p, x0 - 3, y0 - 3, DescriptionData.LineLength * cell.Width + 6,
                        DescriptionData.MaxLines * (cell.Height + 2) + 4);
            }
        }

        private void buttonRestore_Click(object sender, EventArgs e)
        {
            DescriptionData r = RefDesc();
            if (current == null || r == null) return;
            textEdit.Text = r.GetText(current.Index).Replace("\n", "\r\n");
        }

        // ---------------- word finder ----------------

        private void FindWords()
        {
            listWords.BeginUpdate();
            listWords.Items.Clear();
            if (desc != null)
                foreach (string s in desc.FindWords(textFind.Text, 300)) listWords.Items.Add(s);
            listWords.EndUpdate();
        }

        private void listWords_DoubleClick(object sender, EventArgs e)
        {
            if (current == null || listWords.SelectedItem == null) return;
            string s = listWords.SelectedItem.ToString();
            int cut = s.IndexOf("  -  ");
            if (cut > 0) s = s.Substring(0, cut);
            s = s.Replace("·", " ");
            textEdit.SelectedText = s;
            textEdit.Focus();
        }

        // ---------------- suggestions ----------------

        private void buttonSuggest_Click(object sender, EventArgs e)
        {
            if (current == null) return;
            List<string> facts = new List<string>();
            try
            {
                if (current.Kind == KIND_ITEM) ItemFacts(current.Id, facts);
                else if (current.Kind == KIND_SPELL) SpellFacts(current.Id, facts);
                else IPFacts(current.Id, facts);
            }
            catch (Exception) { }
            if (facts.Count == 0)
            {
                MessageBox.Show("The editor doesn't know enough about this one to suggest anything.", "Suggest");
                return;
            }
            textEdit.Text = Wrap(facts).Replace("\n", "\r\n");
        }

        /// <summary>Joins facts into sentences and wraps them at 26 letters, 4 lines at most.</summary>
        private static string Wrap(List<string> facts)
        {
            List<string> words = new List<string>();
            foreach (string f in facts) words.AddRange((f + ".").Split(' '));
            List<string> lines = new List<string>();
            string line = "";
            foreach (string w in words)
            {
                if (w.Length == 0) continue;
                string tryLine = line.Length == 0 ? w : line + " " + w;
                if (tryLine.Length <= DescriptionData.LineLength) { line = tryLine; continue; }
                if (line.Length > 0) lines.Add(line);
                line = w.Length > DescriptionData.LineLength ? w.Substring(0, DescriptionData.LineLength) : w;
            }
            if (line.Length > 0) lines.Add(line);
            if (lines.Count > DescriptionData.MaxLines) lines.RemoveRange(DescriptionData.MaxLines, lines.Count - DescriptionData.MaxLines);
            return string.Join("\n", lines.ToArray());
        }

        private void ScriptFacts(byte[] d, int start, int end, List<string> facts)
        {
            List<ScriptValue> vals = ScriptTools.GetScriptValues(d, start, end);
            if (vals != null)
            {
                string pendingElement = null;
                foreach (ScriptValue v in vals)
                {
                    int x = ScriptTools.GetScriptValue(d, v);
                    string L = v.Label;
                    if (v.Kind == ScriptValue.KIND_ELEMENT)
                    {
                        if (x != 0) pendingElement = ScriptTools.DescribeElement(x);
                        continue;
                    }
                    if (L.EndsWith(": base amount") && L.IndexOf("damage") >= 0)
                    {
                        facts.Add((L.StartsWith("Magic") ? "Magic" : "Physical") + " damage " + x);
                        continue;
                    }
                    if (L.StartsWith("Restore ") && L.EndsWith(": amount"))
                    {
                        string what = L.Substring(8, L.Length - 8 - 8).Replace("current ", "");
                        facts.Add("Restores " + x + " " + what);
                        continue;
                    }
                    if (v.Hint == ScriptValue.HINT_STAT_PERCENT && x != 0)
                    {
                        facts.Add((v.Subject ?? "Stat").Replace("temp ", "") + " " + (x > 0 ? "+" : "") + x + "%");
                        continue;
                    }
                    if (L.StartsWith("Casts spell"))
                    {
                        string[] sp = rom.SpellNames;
                        if (sp != null && x < sp.Length) facts.Add("Casts " + sp[x].Trim());
                    }
                }
                if (pendingElement != null) facts.Insert(0, pendingElement);
            }
            foreach (StatusEffect s in ScriptTools.FindStatusEffects(d, start, end))
            {
                int st = ScriptTools.GetStatus(d, s);
                if (st < 0 || st >= ScriptTools.StatusNames.Length) continue;
                string name = ScriptTools.StatusNames[st];
                if (s.Kind == StatusEffect.KIND_CURE) facts.Add("Cures " + name);
                else if (s.Kind == StatusEffect.KIND_INFLICT) facts.Add("Inflicts " + name);
                else if (s.Editable && ScriptTools.GetValue(d, s) == 0) facts.Add("Blocks " + name);
                else facts.Add("Resists " + name);
            }
        }

        private void ItemFacts(int id, List<string> facts)
        {
            byte[] d = items.RawData;
            if (items.IsEquipment(id))
            {
                SortedDictionary<int, int> f = items.GetFields(id);
                List<string> bon = new List<string>();
                for (int b = 0; b < 7; b++)
                {
                    int v;
                    if (f.TryGetValue(ItemData.FIELD_ATP + b, out v) && v != 0) bon.Add(ItemData.BonusNames[b] + " +" + v);
                }
                if (bon.Count > 0) facts.Add(string.Join(", ", bon.ToArray()));
                EquipElements el = EquipElements.Find(items, id);
                if (el.AttackModeOffset >= 0 && el.IsFixedPower(d)) facts.Add("Attack power fixed at " + el.GetFixedPower(d));
                if (el.AttackOffset >= 0 && el.GetAttack(d) != 0) facts.Add(ScriptTools.DescribeElement(el.GetAttack(d)) + " attacks");
                if (el.AlwaysOffset >= 0) facts.Add(EquipElements.AlwaysName(el.GetAlways(d)).Replace(" damage", ""));
                if (el.ExactElementOffset >= 0) facts.Add("Halves " + ScriptTools.DescribeElement(el.GetExact(d)));
                if (el.GuardElementOffset >= 0)
                    facts.Add((el.GetGuardFlag(d) == 3 ? "Mirror IP " : "Guard IP ") + EquipElements.GuardActionName(el.GetGuardAction(d)) +
                        " " + ScriptTools.DescribeElement(el.GetGuardElement(d)));
                foreach (int field in new int[] { ItemData.FIELD_WEAPON_SCRIPT, ItemData.FIELD_ARMOR_SCRIPT })
                    foreach (StatusEffect st in items.GetStatusEffects(id, field))
                    {
                        int k = ScriptTools.GetStatus(d, st);
                        if (k < 0 || k >= ScriptTools.StatusNames.Length) continue;
                        string name = ScriptTools.StatusNames[k];
                        if (st.Kind == StatusEffect.KIND_INFLICT) facts.Add("May cause " + name);
                        else if (st.Kind == StatusEffect.KIND_PROTECT)
                            facts.Add((st.Editable && ScriptTools.GetValue(d, st) == 0 ? "Blocks " : "Resists ") + name);
                    }
                int ipId;
                if (f.TryGetValue(ItemData.FIELD_IP, out ipId) && ip != null && ipId >= 1 && ipId <= ip.Count)
                    facts.Add("IP: " + ip.GetName(ipId).Trim());
                return;
            }
            int start, end;
            if (items.GetScriptRange(id, ItemData.FIELD_BATTLE_SCRIPT, out start, out end) ||
                items.GetScriptRange(id, ItemData.FIELD_MENU_SCRIPT, out start, out end))
                ScriptFacts(d, start, end, facts);
        }

        private void SpellFacts(int s, List<string> facts)
        {
            byte[] d = rom.RawData;
            int adjust = rom.HasHeader ? 0 : -0x200;
            int rec = int.Parse(rom.getSpellOffset(s), NumberStyles.HexNumber) + adjust;
            int end = (s + 1 < rom.SpellNames.Length ? int.Parse(rom.getSpellOffset(s + 1), NumberStyles.HexNumber) : 0xB0115) + adjust;
            int start = rec + (d[rec + 0x11] | (d[rec + 0x12] << 8));
            if (start > rec && start < end) ScriptFacts(d, start, end, facts);
        }

        private void IPFacts(int id, List<string> facts)
        {
            int s, e;
            if (ip.GetEffectRange(ip.GetWord(id, IPData.OFS_EFFECT), out s, out e)) ScriptFacts(ip.RawData, s, e, facts);
        }
    }
}
