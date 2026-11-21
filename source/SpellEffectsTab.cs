using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

using com.lufiaIIEditor.RomInfo;

namespace com.lufiaIIEditor.UI
{
    /// <summary>
    /// "Spell Effects" tab: edits what each spell's script does (damage, element, healing,
    /// stat changes, statuses). The script is at record + the 16-bit offset stored at +0x11
    /// (always 0x13 in the US ROM) and runs to the next spell. Name, MP and gold cost stay
    /// on the original Spells tab.
    /// </summary>
    class SpellEffectsTab : TabPage
    {
        private const int SpellCount = 40;
        private const int AfterLastSpell = 0xB0115; // same end marker the original editor uses

        private Rom rom;
        private IPData ip;
        private int[] offsets;
        private int currentIndex = -1;
        private bool loading = false;
        private TextBox textName;
        private NumericUpDown numMP, numGold;

        private ComboBox comboSpell;
        private Label labelInfo;
        private Label labelCastBy;
        private EffectEditorPanel effectPanel;
        private CheckBox[] checkLearn = new CheckBox[7];
        private Label labelLearnWarn;
        private DataGridView grid;
        private const int OFS_LEARN = 0x0A;   // who can learn the spell: bit per character, like equipment
        private static readonly string[] ShortNames = { "Max", "Sel", "Guy", "Art", "Tia", "Dek", "Lex" };

        public SpellEffectsTab()
        {
            this.Text = "Spell Effects";
            this.UseVisualStyleBackColor = true;
            Build();
            this.Enabled = false;
        }

        private void Build()
        {
            Controls.Add(UiHelpers.MakeLabel("Spell:", 12, 15));
            comboSpell = UiHelpers.MakeDropDown(80, 12, 230);
            comboSpell.SelectedIndexChanged += new EventHandler(comboSpell_SelectedIndexChanged);
            Controls.Add(comboSpell);
            Controls.Add(UiHelpers.MakeLabel("Name:", 325, 15));
            textName = UiHelpers.MakeNameBox(372, 12, 8);
            textName.Size = new Size(100, 20);
            Controls.Add(textName);
            Controls.Add(UiHelpers.MakeLabel("MP:", 486, 15));
            numMP = UiHelpers.MakeNumeric(520, 12, 0, 65535);
            numMP.Size = new Size(60, 20);
            Controls.Add(numMP);
            Controls.Add(UiHelpers.MakeLabel("Gold:", 592, 15));
            numGold = UiHelpers.MakeNumeric(634, 12, 0, 65535);
            numGold.Size = new Size(70, 20);
            Controls.Add(numGold);
            labelInfo = UiHelpers.MakeLabel("", 716, 15);
            Controls.Add(labelInfo);

            labelCastBy = UiHelpers.MakeLabel("", 12, 45);
            labelCastBy.AutoSize = false;
            labelCastBy.Size = new Size(960, 30);
            Controls.Add(labelCastBy);

            GroupBox g = UiHelpers.MakeGroup("What the spell does", 12, 75, 560, 430);
            Controls.Add(UiHelpers.Anchor(this, g, AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left));
            effectPanel = UiHelpers.Anchor(this, new EffectEditorPanel(540, 400), AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right);
            effectPanel.Location = new Point(10, 20);
            g.Controls.Add(effectPanel);

            // Who can learn the selected spell
            GroupBox gLearn = UiHelpers.MakeGroup("Who can learn it", 585, 75, 390, 78);
            Controls.Add(gLearn);
            for (int c = 0; c < 7; c++)
            {
                checkLearn[c] = new CheckBox();
                checkLearn[c].Text = ItemData.CharacterNames[c];
                checkLearn[c].Location = new Point(10 + (c % 4) * 94, 20 + (c / 4) * 24);
                checkLearn[c].AutoSize = true;
                checkLearn[c].CheckedChanged += new EventHandler(checkLearn_CheckedChanged);
                gLearn.Controls.Add(checkLearn[c]);
            }
            labelLearnWarn = UiHelpers.MakeLabel("", 200, 48);
            labelLearnWarn.ForeColor = Color.DarkRed;
            gLearn.Controls.Add(labelLearnWarn);

            // Overview of every spell
            GroupBox gGrid = UiHelpers.Anchor(this, UiHelpers.MakeGroup("All spells: who can learn them (click to change)", 585, 158, 390, 280),
                AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right);
            Controls.Add(gGrid);
            grid = UiHelpers.Anchor(this, new DataGridView(), AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right);
            grid.Location = new Point(8, 18);
            grid.Size = new Size(374, 254);
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.RowHeadersVisible = false;
            grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
            grid.MultiSelect = false;
            grid.RowTemplate.Height = 18;
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            grid.ColumnHeadersHeight = 22;
            DataGridViewTextBoxColumn nameCol = new DataGridViewTextBoxColumn();
            nameCol.HeaderText = "Spell";
            nameCol.ReadOnly = true;
            nameCol.Width = 84;
            nameCol.SortMode = DataGridViewColumnSortMode.NotSortable;
            grid.Columns.Add(nameCol);
            for (int c = 0; c < 7; c++)
            {
                DataGridViewCheckBoxColumn col = new DataGridViewCheckBoxColumn();
                col.HeaderText = ShortNames[c];
                col.Width = 38;
                col.SortMode = DataGridViewColumnSortMode.NotSortable;
                grid.Columns.Add(col);
            }
            grid.CurrentCellDirtyStateChanged += delegate(object s, EventArgs e)
            {
                if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            grid.CellValueChanged += new DataGridViewCellEventHandler(grid_CellValueChanged);
            grid.CellClick += delegate(object s, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex >= 0 && e.ColumnIndex == 0) comboSpell.SelectedIndex = e.RowIndex;
            };
            gGrid.Controls.Add(grid);

            Controls.Add(UiHelpers.Anchor(this, UiHelpers.MakeNote(
                "Click a spell's name to edit its effect. Blue = differs from the clean ROM.\n" +
                "Magic shops sell to anyone; only these characters can take the spell.\n" +
                "Names are 8 letters (the game's fixed size). Status chances are %\n" +
                "(100 = always); stat changes are % of the stat, each use going half way.", 585, 444), AnchorStyles.Bottom | AnchorStyles.Left));
        }

        public void LoadRom(Rom romData, IPData ipData)
        {
            int keep = comboSpell.SelectedIndex;
            rom = romData;
            ip = ipData;
            currentIndex = -1;

            // The offsets file (like the rest of the original editor) assumes a headered ROM.
            offsets = new int[SpellCount + 1];
            for (int i = 0; i < SpellCount; i++)
                offsets[i] = int.Parse(rom.getSpellOffset(i), NumberStyles.HexNumber);
            offsets[SpellCount] = AfterLastSpell;

            loading = true;
            comboSpell.Items.Clear();
            for (int i = 0; i < SpellCount; i++)
                comboSpell.Items.Add(SpellName(i) + "  (" + i + ")");
            loading = false;

            this.Enabled = true;
            FillGrid();
            comboSpell.SelectedIndex = (keep >= 0 && keep < SpellCount) ? keep : 0;
        }

        private string SpellName(int i)
        {
            return rom.readDataString(rom.getSpellOffset(i), 8).TrimEnd(' ');
        }

        private static string RefName(int refOffset)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int k = 0; k < 8; k++) sb.Append((char)Reference.Data[refOffset + k]);
            return sb.ToString().TrimEnd(' ');
        }

        private string[] AllSpellNames()
        {
            string[] names = new string[SpellCount];
            for (int i = 0; i < SpellCount; i++) names[i] = SpellName(i);
            return names;
        }

        public void CommitCurrent()
        {
            if (rom == null || currentIndex < 0) return;
            effectPanel.Commit();
            CommitHeader(currentIndex);
        }

        /// <summary>Name (8 letters, space-padded), MP cost (+0C) and gold cost (+0F).</summary>
        private void CommitHeader(int i)
        {
            byte[] d = rom.RawData;
            int rec = offsets[i];
            string name = textName.Text;
            bool renamed = name.TrimEnd(' ') != SpellName(i);
            for (int k = 0; k < 8; k++)
            {
                char c = k < name.Length ? name[k] : ' ';
                d[rec + k] = (byte)(c < 0x20 || c > 0x7E ? ' ' : c);
            }
            int mp = (int)numMP.Value, gold = (int)numGold.Value;
            d[rec + 0x0C] = (byte)mp; d[rec + 0x0D] = (byte)(mp >> 8);
            d[rec + 0x0F] = (byte)gold; d[rec + 0x10] = (byte)(gold >> 8);
            if (renamed)
            {
                bool was = loading; loading = true;
                UiHelpers.RenameItem(comboSpell, i, SpellName(i) + "  (" + i + ")");
                if (grid.Rows.Count > i) grid.Rows[i].Cells[0].Value = SpellName(i);
                loading = was;
            }
        }

        public void RefreshView()
        {
            if (rom == null) return;
            FillGrid();
            if (currentIndex >= 0) ShowSpell(currentIndex);
        }

        // ---------------- who can learn ----------------

        private int LearnOffset(int spell) { return offsets[spell] + OFS_LEARN; }
        private int GetLearn(int spell) { return rom.RawData[LearnOffset(spell)]; }

        private int OriginalLearn(int spell)
        {
            int r = Reference.Shift(LearnOffset(spell));
            return Reference.InRange(r, 1) ? Reference.Data[r] : -1;
        }

        private void SetLearnBit(int spell, int c, bool on)
        {
            int o = LearnOffset(spell);
            byte[] d = rom.RawData;
            d[o] = (byte)(on ? d[o] | (1 << c) : d[o] & ~(1 << c));   // bit 7 and other bits are kept
        }

        private void ShowLearn()
        {
            if (currentIndex < 0) return;
            bool was = loading;
            loading = true;
            int m = GetLearn(currentIndex), orig = OriginalLearn(currentIndex);
            for (int c = 0; c < 7; c++)
            {
                checkLearn[c].Checked = ((m >> c) & 1) != 0;
                ChangeHighlighter.Set(checkLearn[c], orig < 0 ? null : (object)(((orig >> c) & 1) != 0));
            }
            labelLearnWarn.Text = (m & 0x7F) == 0 ? "Nobody can learn this spell." : "";
            loading = was;
        }

        private void FillGrid()
        {
            bool was = loading;
            loading = true;
            grid.Rows.Clear();
            for (int i = 0; i < SpellCount; i++)
            {
                object[] row = new object[8];
                row[0] = SpellName(i);
                int m = GetLearn(i);
                for (int c = 0; c < 7; c++) row[c + 1] = ((m >> c) & 1) != 0;
                grid.Rows.Add(row);
                ColorRow(i);
            }
            loading = was;
        }

        private void ColorRow(int i)
        {
            int m = GetLearn(i), orig = OriginalLearn(i);
            for (int c = 0; c < 7; c++)
            {
                bool changed = orig >= 0 && (((m ^ orig) >> c) & 1) != 0;
                grid.Rows[i].Cells[c + 1].Style.BackColor = changed ? ChangeHighlighter.ChangedColor : Color.Empty;
            }
            grid.Rows[i].Cells[0].Style.ForeColor = (m & 0x7F) == 0 ? Color.DarkRed : Color.Empty;
        }

        private void checkLearn_CheckedChanged(object sender, EventArgs e)
        {
            if (loading || rom == null || currentIndex < 0) return;
            for (int c = 0; c < 7; c++)
                if (sender == checkLearn[c]) SetLearnBit(currentIndex, c, checkLearn[c].Checked);
            loading = true;
            for (int c = 0; c < 7; c++) grid.Rows[currentIndex].Cells[c + 1].Value = checkLearn[c].Checked;
            loading = false;
            ColorRow(currentIndex);
            labelLearnWarn.Text = (GetLearn(currentIndex) & 0x7F) == 0 ? "Nobody can learn this spell." : "";
        }

        private void grid_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (loading || rom == null || e.RowIndex < 0 || e.ColumnIndex < 1) return;
            bool on = (bool)grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value;
            SetLearnBit(e.RowIndex, e.ColumnIndex - 1, on);
            ColorRow(e.RowIndex);
            if (e.RowIndex == currentIndex) ShowLearn();
        }

        private void comboSpell_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (loading || rom == null) return;
            int newIndex = comboSpell.SelectedIndex;
            if (newIndex == currentIndex || newIndex < 0) return;
            CommitCurrent();
            ShowSpell(newIndex);
        }

        /// <summary>IP moves whose effect script casts this spell (command 54).</summary>
        private List<string> IPMovesCasting(int spell)
        {
            List<string> result = new List<string>();
            if (ip == null) return result;
            byte[] data = rom.RawData;
            for (int id = 1; id <= ip.Count; id++)
            {
                int s, e;
                if (!ip.GetEffectRange(ip.GetWord(id, IPData.OFS_EFFECT), out s, out e)) continue;
                List<int> ins = ScriptTools.Walk(data, s, e);
                if (ins == null) continue;
                foreach (int p in ins)
                {
                    if (data[p] == 0x54 && data[p + 1] == spell) { result.Add(ip.GetName(id)); break; }
                }
            }
            return result;
        }

        private void ShowSpell(int i)
        {
            currentIndex = i;
            byte[] data = rom.RawData;
            int rec = offsets[i];
            int end = offsets[i + 1];
            int rel = data[rec + 0x11] | (data[rec + 0x12] << 8);
            int start = rec + rel;

            labelInfo.Text = "ID: " + i + "   Offset: " + rec.ToString("X");
            bool wasLoading = loading;
            loading = true;
            textName.Text = SpellName(i);
            UiHelpers.SetValueClamped(numMP, data[rec + 0x0C] | (data[rec + 0x0D] << 8));
            UiHelpers.SetValueClamped(numGold, data[rec + 0x0F] | (data[rec + 0x10] << 8));
            int ro = Reference.Shift(rec);
            bool haveRef = Reference.Data != null && Reference.InRange(ro, 0x13);
            ChangeHighlighter.Set(textName, haveRef ? (object)RefName(ro) : null);
            ChangeHighlighter.Set(numMP, haveRef ? (object)(Reference.Data[ro + 0x0C] | (Reference.Data[ro + 0x0D] << 8)) : null);
            ChangeHighlighter.Set(numGold, haveRef ? (object)(Reference.Data[ro + 0x0F] | (Reference.Data[ro + 0x10] << 8)) : null);
            loading = wasLoading;
            ShowLearn();
            if (grid.Rows.Count > i) { grid.ClearSelection(); grid.Rows[i].Cells[0].Selected = true; }
            List<string> casters = IPMovesCasting(i);
            labelCastBy.Text = casters.Count == 0 ? "" :
                "Also used by IP moves (they change too): " + string.Join(", ", casters.ToArray());

            if (rel < 0x13 || start > end)
            {
                effectPanel.ShowNothing("This spell's script couldn't be located.");
                return;
            }
            if (start == end)
            {
                effectPanel.ShowNothing("This spell has no battle script (it's a field spell).");
                return;
            }
            effectPanel.ShowScript(data, start, end, AllSpellNames(), "Spell");
        }
    }
}
