using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

using com.lufiaIIEditor.RomInfo;

namespace com.lufiaIIEditor.UI
{
    /// <summary>
    /// "Enemy AI" tab: walks an enemy's AI script (record +0x21 up to the next enemy) and lets
    /// you edit what's in it, in place: which skill name is shown, attack damage and element,
    /// spells cast, odds of random choices, numbers in conditions and formulas, and status
    /// protections / attacks. Nothing is added or removed, so the AI never changes size.
    /// (The class keeps its old name so the rest of the code doesn't change.)
    /// </summary>
    class EnemyStatusTab : TabPage
    {
        private const int EnemyCount = 224;
        private const int AfterLastEnemy = 0xB5168; // same end marker the original editor uses
        private const int ScriptStart = 0x21;

        private Rom rom;
        private int[] offsets;
        private int currentIndex = -1;
        private bool loading = false;

        private ComboBox comboEnemy;
        private Label labelInfo;
        private EffectEditorPanel effectPanel;
        private ComboBox comboSkill;
        private TextBox textSkillName;
        private Label labelSkillInfo;
        private int currentSkill = -1;
        private Panel elemPanel;
        private List<EnemyElementEntry> elemEntries = new List<EnemyElementEntry>();
        private List<ComboBox[]> elemCombos = new List<ComboBox[]>();

        public EnemyStatusTab()
        {
            this.Text = "Enemy AI";
            this.UseVisualStyleBackColor = true;
            Build();
            this.Enabled = false;
        }

        private void Build()
        {
            Controls.Add(UiHelpers.MakeLabel("Enemy:", 12, 15));
            comboEnemy = UiHelpers.MakeDropDown(80, 12, 230);
            comboEnemy.SelectedIndexChanged += new EventHandler(comboEnemy_SelectedIndexChanged);
            Controls.Add(comboEnemy);
            labelInfo = UiHelpers.MakeLabel("", 330, 15);
            Controls.Add(labelInfo);

            GroupBox g = UiHelpers.MakeGroup("What the enemy's AI does (in script order)", 12, 45, 600, 460);
            Controls.Add(UiHelpers.Anchor(this, g, AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left));
            effectPanel = UiHelpers.Anchor(this, new EffectEditorPanel(580, 430, 300), AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right);
            effectPanel.Location = new Point(10, 20);
            g.Controls.Add(effectPanel);

            GroupBox gSkill = UiHelpers.MakeGroup("Enemy skill names", 625, 45, 350, 96);
            Controls.Add(gSkill);
            comboSkill = UiHelpers.MakeDropDown(10, 20, 250);
            comboSkill.SelectedIndexChanged += new EventHandler(comboSkill_SelectedIndexChanged);
            gSkill.Controls.Add(comboSkill);
            labelSkillInfo = UiHelpers.MakeNote("", 266, 23);
            gSkill.Controls.Add(labelSkillInfo);
            textSkillName = UiHelpers.MakeNameBox(10, 50, 16);
            textSkillName.Size = new Size(160, 20);
            gSkill.Controls.Add(textSkillName);
            gSkill.Controls.Add(UiHelpers.MakeNote(
                "Names are packed back to back, so each one can only use\nthe letters it already has (shorter is fine).", 178, 48));

            Controls.Add(UiHelpers.MakeNote(
                "Values are listed in the order the AI runs them.\n" +
                "Lines after \"Uses skill\" belong to that skill and are\n" +
                "labelled with its name, e.g. [Voltage bolt].\n\n" +
                "Each enemy has its own copy of its attacks: changing\n" +
                "one enemy's Voltage bolt doesn't change another's.\n" +
                "Spells it casts use the Spell Effects tab, so editing\n" +
                "a spell there changes it for enemies too.\n\n" +
                "Random: the number is the chance (out of 255) to SKIP\n" +
                "the next part, so 0 = always runs, 255 = never runs.\n" +
                "Conditions: the next part runs only if the value the\n" +
                "AI loaded just before (e.g. target's current HP)\n" +
                "passes the test, e.g. Goblin Mage heals when <= 30.\n" +
                "Status chances: % (100 = always). Protection: an\n" +
                "incoming attempt gets this % instead (0 = immune).",
                625, 150));

            GroupBox gElem = UiHelpers.MakeGroup("Elements: weaknesses and resistances", 625, 338, 350, 167);
            Controls.Add(UiHelpers.Anchor(this, gElem, AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right));
            elemPanel = UiHelpers.Anchor(this, new Panel(), AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right);
            elemPanel.Location = new Point(6, 18);
            elemPanel.Size = new Size(338, 143);
            elemPanel.AutoScroll = true;
            gElem.Controls.Add(elemPanel);
            new ToolTip().SetToolTip(gElem,
                "How the enemy takes elemental damage. Its own attacks' elements are in the AI list\n" +
                "(\"Normal attack - element\", and each skill's element).\n" +
                "Each line can be changed into any other, but lines can't be added or removed.");
        }

        private void ShowElements(byte[] data, int start, int end)
        {
            elemPanel.SuspendLayout();
            UiHelpers.ClearAndDispose(elemPanel);
            elemCombos.Clear();
            elemEntries = EnemyElements.Find(data, start, end);
            if (elemEntries.Count == 0)
            {
                Label none = UiHelpers.MakeNote("No elemental weaknesses or resistances: takes normal\n" +
                    "damage from everything. (There's no room to add one.)", 3, 5);
                elemPanel.Controls.Add(none);
                elemPanel.ResumeLayout();
                return;
            }
            byte[] refData = Reference.Data;
            for (int k = 0; k < elemEntries.Count; k++)
            {
                EnemyElementEntry e = elemEntries[k];
                int y = 3 + k * 28;
                if (e.Kind == EnemyElementEntry.KIND_SHARED)
                {
                    ComboBox c = UiHelpers.MakeDropDown(3, y, 290);
                    foreach (string n in EnemyElements.SharedNames) c.Items.Add(n);
                    c.SelectedIndex = Array.IndexOf(EnemyElements.SharedScripts, (int)data[e.ScriptOffset]);
                    elemPanel.Controls.Add(c);
                    object orig = null;
                    int ro = Reference.Shift(e.ScriptOffset);
                    if (refData != null && Reference.InRange(ro - 1, 3) && refData[ro - 1] == 0x42)
                    {
                        int idx = Array.IndexOf(EnemyElements.SharedScripts, (int)refData[ro]);
                        if (idx >= 0) orig = idx;
                    }
                    ChangeHighlighter.Set(c, orig);
                    elemCombos.Add(new ComboBox[] { c });
                }
                else
                {
                    elemPanel.Controls.Add(UiHelpers.MakeLabel("Immune to", 3, y + 3));
                    ComboBox[] cs = new ComboBox[e.ElementOffsets.Count];
                    for (int j = 0; j < cs.Length; j++)
                    {
                        int o = e.ElementOffsets[j];
                        int v = EnemyElements.GetElement(data, o);
                        int rv = -1;
                        int ro = Reference.Shift(o);
                        if (refData != null && Reference.InRange(ro, 2)) rv = EnemyElements.GetElement(refData, ro);
                        List<int> vals = new List<int>(ScriptTools.ElementValues);
                        if (!vals.Contains(v)) vals.Add(v);
                        if (rv >= 0 && !vals.Contains(rv)) vals.Add(rv);
                        ComboBox c = UiHelpers.MakeDropDown(68 + j * 115, y, 110);
                        foreach (int x in vals) c.Items.Add(ScriptTools.DescribeElement(x));
                        c.Tag = vals;
                        c.SelectedIndex = vals.IndexOf(v);
                        elemPanel.Controls.Add(c);
                        ChangeHighlighter.Set(c, rv >= 0 ? (object)vals.IndexOf(rv) : null);
                        cs[j] = c;
                    }
                    elemCombos.Add(cs);
                }
                if (e.Conditional)
                {
                    Label sometimes = UiHelpers.MakeNote("sometimes", 296, y + 4);
                    new ToolTip().SetToolTip(sometimes, "A condition in the script decides whether this line runs.");
                    elemPanel.Controls.Add(sometimes);
                }
            }
            elemPanel.ResumeLayout();
        }

        private void CommitElements()
        {
            byte[] data = rom.RawData;
            for (int k = 0; k < elemEntries.Count && k < elemCombos.Count; k++)
            {
                EnemyElementEntry e = elemEntries[k];
                ComboBox[] cs = elemCombos[k];
                if (e.Kind == EnemyElementEntry.KIND_SHARED)
                {
                    if (cs[0].SelectedIndex >= 0) EnemyElements.SetShared(data, e, EnemyElements.SharedScripts[cs[0].SelectedIndex]);
                }
                else
                {
                    for (int j = 0; j < cs.Length && j < e.ElementOffsets.Count; j++)
                    {
                        List<int> vals = cs[j].Tag as List<int>;
                        if (vals != null && cs[j].SelectedIndex >= 0) EnemyElements.SetElement(data, e.ElementOffsets[j], vals[cs[j].SelectedIndex]);
                    }
                }
            }
        }

        public void LoadRom(Rom romData)
        {
            int keep = comboEnemy.SelectedIndex;
            rom = romData;
            currentIndex = -1;
            // The offsets file (like the rest of the original editor) assumes a headered ROM.
            offsets = new int[EnemyCount + 1];
            for (int i = 0; i < EnemyCount; i++)
                offsets[i] = int.Parse(rom.getEnemyOffset(i), NumberStyles.HexNumber);
            offsets[EnemyCount] = AfterLastEnemy;

            loading = true;
            comboEnemy.Items.Clear();
            for (int i = 0; i < EnemyCount; i++)
                comboEnemy.Items.Add(rom.readDataString(rom.getEnemyOffset(i), 13).TrimEnd(' ') + "  (" + i.ToString("X2") + ")");
            loading = false;

            FillSkillList();
            this.Enabled = true;
            comboEnemy.SelectedIndex = (keep >= 0 && keep < EnemyCount) ? keep : 0;
        }

        /// <summary>Switched off for ROMs whose enemy data isn't where this tab reads it.</summary>
        public void Disable()
        {
            rom = null;
            currentIndex = -1;
            currentSkill = -1;
            this.Enabled = false;
        }

        public void CommitCurrent()
        {
            if (rom == null) return;
            CommitSkillName();
            if (currentIndex < 0) return;
            effectPanel.Commit();
            CommitElements();
        }

        // ---------------- enemy skill names ----------------

        private void FillSkillList()
        {
            bool was = loading;
            loading = true;
            int keep = comboSkill.SelectedIndex;
            comboSkill.Items.Clear();
            for (int i = 0; i < rom.skillNames.Length; i++)
                comboSkill.Items.Add(rom.skillNames[i].Trim() + "  (" + (i + 1).ToString("X2") + ")");
            loading = was;
            currentSkill = -1;
            comboSkill.SelectedIndex = (keep >= 0 && keep < comboSkill.Items.Count) ? keep : 0;
        }

        private void comboSkill_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (loading || rom == null) return;
            CommitSkillName();
            ShowSkillName(comboSkill.SelectedIndex);
        }

        private void ShowSkillName(int i)
        {
            if (rom == null || i < 0 || i >= rom.skillNamesLength.Length) return;
            bool was = loading;
            loading = true;
            currentSkill = i;
            string offset = rom.getSkillOffset(i);
            int len = rom.skillNamesLength[i];
            textSkillName.MaxLength = Math.Max(1, len);
            textSkillName.Text = rom.readDataString(offset, len);
            labelSkillInfo.Text = "max " + len + " letters";
            int ro = Reference.Shift(int.Parse(offset, NumberStyles.HexNumber));
            ChangeHighlighter.Set(textSkillName, Reference.InRange(ro, len) ? (object)RefText(ro, len) : null);
            loading = was;
        }

        private static string RefText(int offset, int len)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int k = 0; k < len; k++) sb.Append((char)Reference.Data[offset + k]);
            return sb.ToString();
        }

        /// <summary>
        /// Writes the name, but only into a real name slot (type byte E0 before it and 00 right
        /// after its last letter), so a ROM damaged by the old Enemy Skills tab isn't made worse.
        /// </summary>
        private void CommitSkillName()
        {
            if (rom == null || currentSkill < 0 || currentSkill >= rom.skillNamesLength.Length) return;
            string offset = rom.getSkillOffset(currentSkill);
            int len = rom.skillNamesLength[currentSkill];
            if (textSkillName.Text.Trim() == rom.readDataString(offset, len).Trim()) return;
            int o = int.Parse(offset, NumberStyles.HexNumber);
            byte[] data = rom.RawData;
            if (o < 1 || o + len >= data.Length || data[o - 1] != 0xE0 || data[o + len] != 0)
            {
                MessageBox.Show("The name slot for this skill doesn't look right, so the new name was not saved.\n" +
                    "(Try File > Repair enemy skill names from clean ROM.)", "Enemy skill names",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            rom.writeDataString(textSkillName.Text, offset, len);
            rom.skillNames[currentSkill] = rom.readDataString(offset, len);
            bool was = loading;
            loading = true;
            UiHelpers.RenameItem(comboSkill, currentSkill, rom.skillNames[currentSkill].Trim() + "  (" + (currentSkill + 1).ToString("X2") + ")");
            loading = was;
            effectPanel.SkillNames = rom.skillNames;
            if (currentIndex >= 0) ShowEnemy(currentIndex);
        }

        public void RefreshView()
        {
            if (rom == null) return;
            if (currentSkill >= 0) ShowSkillName(currentSkill);
            if (currentIndex >= 0) ShowEnemy(currentIndex);
        }

        /// <summary>Re-reads the skill names (after File &gt; Repair enemy skill names).</summary>
        public void RefreshSkillNames()
        {
            if (rom == null) return;
            FillSkillList();
            effectPanel.SkillNames = rom.skillNames;
            if (currentIndex >= 0) ShowEnemy(currentIndex);
        }

        /// <summary>Re-reads the enemy names (they can be renamed on other tabs).</summary>
        public void RefreshNames()
        {
            if (rom == null) return;
            bool was = loading;
            loading = true;
            for (int i = 0; i < EnemyCount && i < comboEnemy.Items.Count; i++)
            {
                string n = rom.readDataString(rom.getEnemyOffset(i), 13).TrimEnd(' ') + "  (" + i.ToString("X2") + ")";
                if ((string)comboEnemy.Items[i] != n) comboEnemy.Items[i] = n;
            }
            loading = was;
        }

        /// <summary>Shows one enemy (used by the Enemy Overview's AI buttons).</summary>
        public void SelectEnemy(int i)
        {
            if (rom == null || i < 0 || i >= comboEnemy.Items.Count) return;
            comboEnemy.SelectedIndex = i;
        }

        private void comboEnemy_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (loading || rom == null) return;
            int newIndex = comboEnemy.SelectedIndex;
            if (newIndex == currentIndex || newIndex < 0) return;
            CommitCurrent();
            ShowEnemy(newIndex);
        }

        private void ShowEnemy(int i)
        {
            currentIndex = i;
            int start = offsets[i] + ScriptStart;
            int end = offsets[i + 1];
            byte[] data = rom.RawData;
            labelInfo.Text = "Offset: " + offsets[i].ToString("X");

            ShowElements(data, start, end);
            if (end <= start || ScriptTools.Walk(data, start, end) == null)
            {
                effectPanel.ShowNothing("This enemy's AI script couldn't be decoded, so it isn't editable here.");
                return;
            }
            effectPanel.SkillNames = rom.skillNames;
            effectPanel.ShowScript(data, start, end, rom.SpellNames, "AI");
        }
    }
}
