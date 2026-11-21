using System;
using System.Drawing;
using System.Windows.Forms;

using com.lufiaIIEditor.RomInfo;

namespace com.lufiaIIEditor.UI
{
    /// <summary>
    /// "Capsule Monsters" tab: name, base stats, ATP/DFP bonuses and growth for all 35
    /// capsule monster forms, with a stat preview and a level 1-99 growth chart.
    ///
    /// Growth: the shared curve table has 5 curves per stat, fastest to slowest
    /// (HP rows 1-5, STR 6-10, AGL 11-15, INT 16-20, GUT 21-25, MGR 26-30; row 0 = none),
    /// so each stat is edited as a grade 1-5. "Apply to all 5 forms" copies grades you
    /// change to the whole family.
    /// </summary>
    class CapsuleMonsterTab : TabPage
    {
        private static readonly string[] StatNames = { "HP", "STR", "AGL", "INT", "GUT", "MGR" };
        private static readonly int[] BaseOffsets = {
            CapsuleMonsterData.OFS_HP,
            CapsuleMonsterData.OFS_STR, CapsuleMonsterData.OFS_STR + 1, CapsuleMonsterData.OFS_STR + 2,
            CapsuleMonsterData.OFS_STR + 3, CapsuleMonsterData.OFS_STR + 4 };
        private const int GradeNone = 5;   // combo index for "none"
        private const int GradeOther = 6;  // combo index for a row outside the stat's group

        private CapsuleMonsterData capsules;
        private int currentIndex = -1;
        private bool loading = false;
        private int[] loadedRows = new int[6];  // growth rows as shown, to see what changed
        private int[] otherRows = new int[6];   // row kept for the "other" entry

        private ComboBox comboCapsule;
        private Label labelInfo;
        private TextBox textName;
        private NumericUpDown[] numBase = new NumericUpDown[6];
        private ComboBox[] comboGrade = new ComboBox[6];
        private CheckBox checkAllForms;
        private NumericUpDown numAtpBonus;
        private NumericUpDown numDfpBonus;
        private NumericUpDown numPreviewLevel;
        private Label[] labelPreview = new Label[8];
        private GrowthChart chart;
        private Label labelSkills;

        public CapsuleMonsterTab()
        {
            this.Text = "Capsule Monsters";
            this.Padding = new Padding(3);
            this.UseVisualStyleBackColor = true;
            BuildControls();
            this.Enabled = false;
        }

        private void BuildControls()
        {
            Controls.Add(UiHelpers.MakeLabel("Capsule monster:", 12, 15));
            comboCapsule = UiHelpers.MakeDropDown(110, 12, 260);
            comboCapsule.SelectedIndexChanged += new EventHandler(comboCapsule_SelectedIndexChanged);
            Controls.Add(comboCapsule);

            labelInfo = UiHelpers.MakeLabel("", 390, 15);
            Controls.Add(labelInfo);

            Controls.Add(UiHelpers.MakeLabel("Name:", 12, 47));
            textName = UiHelpers.MakeNameBox(110, 44, CapsuleMonsterData.NameLength);
            Controls.Add(textName);

            // Base stats
            GroupBox gBase = UiHelpers.MakeGroup("Base stats (at level 1)", 12, 80, 200, 215);
            Controls.Add(gBase);
            for (int i = 0; i < 6; i++)
            {
                gBase.Controls.Add(UiHelpers.MakeLabel(StatNames[i], 12, 27 + i * 30));
                numBase[i] = UiHelpers.MakeNumeric(80, 24 + i * 30, 0, 255);
                gBase.Controls.Add(numBase[i]);
            }

            // Bonuses
            GroupBox gBonus = UiHelpers.MakeGroup("Built-in bonuses (fixed per form)", 12, 305, 200, 120);
            Controls.Add(gBonus);
            gBonus.Controls.Add(UiHelpers.MakeLabel("ATP bonus", 12, 27));
            numAtpBonus = UiHelpers.MakeNumeric(80, 24, 0, 255);
            gBonus.Controls.Add(numAtpBonus);
            gBonus.Controls.Add(UiHelpers.MakeLabel("DFP bonus", 12, 57));
            numDfpBonus = UiHelpers.MakeNumeric(80, 54, 0, 255);
            gBonus.Controls.Add(numDfpBonus);
            gBonus.Controls.Add(UiHelpers.MakeNote("ATP = STR + ATP bonus\nDFP = (STR + AGL) / 2 + DFP bonus", 12, 82));

            // Growth grades
            GroupBox gGrowth = UiHelpers.MakeGroup("Growth (grade 1 = fastest, 5 = slowest)", 225, 80, 330, 215);
            Controls.Add(gGrowth);
            for (int i = 0; i < 6; i++)
            {
                gGrowth.Controls.Add(UiHelpers.MakeLabel(StatNames[i], 12, 27 + i * 30));
                comboGrade[i] = UiHelpers.MakeDropDown(55, 24 + i * 30, 262);
                comboGrade[i].SelectedIndexChanged += new EventHandler(AnyValueChanged);
                gGrowth.Controls.Add(comboGrade[i]);
            }

            checkAllForms = new CheckBox();
            checkAllForms.Text = "Apply growth changes to all 5 forms of this capsule";
            checkAllForms.Location = new Point(228, 300);
            checkAllForms.AutoSize = true;
            checkAllForms.Checked = true;
            Controls.Add(checkAllForms);

            // Preview (compact: two rows of four)
            GroupBox gPreview = UiHelpers.MakeGroup("Stats at level (before items / boosts)", 225, 325, 330, 100);
            Controls.Add(gPreview);
            gPreview.Controls.Add(UiHelpers.MakeLabel("Level", 10, 23));
            numPreviewLevel = UiHelpers.MakeNumeric(50, 20, 1, 99);
            numPreviewLevel.Size = new Size(50, 20);
            numPreviewLevel.Value = 1;
            gPreview.Controls.Add(numPreviewLevel);
            string[] previewNames = { "HP", "ATP", "DFP", "STR", "AGL", "INT", "GUT", "MGR" };
            for (int i = 0; i < 8; i++)
            {
                int x = 10 + (i % 4) * 80, y = 50 + (i / 4) * 22;
                gPreview.Controls.Add(UiHelpers.MakeLabel(previewNames[i], x, y));
                labelPreview[i] = UiHelpers.MakeLabel("-", x + 32, y);
                labelPreview[i].Font = new Font(labelPreview[i].Font, FontStyle.Bold);
                gPreview.Controls.Add(labelPreview[i]);
            }

            // Chart
            GroupBox gChart = UiHelpers.Anchor(this, UiHelpers.MakeGroup("Growth, level 1-99 (this form)", 565, 80, 410, 345), AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right);
            Controls.Add(gChart);
            chart = UiHelpers.Anchor(this, new GrowthChart(), AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right);
            chart.Location = new Point(8, 18);
            chart.Size = new Size(394, 318);
            gChart.Controls.Add(chart);

            Controls.Add(UiHelpers.Anchor(this, UiHelpers.MakeNote(
                "Each grade's gain is halved in the 16-level band matching the form number (form 1: Lv 16-31)\n" +
                "and quartered after that, so later forms keep growing for longer. The grade curves are shared.",
                225, 432), AnchorStyles.Bottom | AnchorStyles.Left));

            labelSkills = UiHelpers.Anchor(this, UiHelpers.MakeNote("", 12, 470), AnchorStyles.Bottom | AnchorStyles.Left);
            Controls.Add(labelSkills);

            EventHandler recalc = new EventHandler(AnyValueChanged);
            for (int i = 0; i < 6; i++) numBase[i].ValueChanged += recalc;
            numAtpBonus.ValueChanged += recalc;
            numDfpBonus.ValueChanged += recalc;
            numPreviewLevel.ValueChanged += recalc;
        }

        // ---------------- grades <-> rows ----------------

        private static int RowFor(int stat, int grade) { return stat * 5 + grade; } // grade 1..5

        /// <summary>Combo index for a row: 0-4 = grade 1-5, GradeNone, or GradeOther.</summary>
        private static int IndexForRow(int stat, int row)
        {
            if (row == 0) return GradeNone;
            int g = row - stat * 5;
            return (g >= 1 && g <= 5) ? g - 1 : GradeOther;
        }

        private int RowOf(int stat)
        {
            int i = comboGrade[stat].SelectedIndex;
            if (i >= 0 && i < 5) return RowFor(stat, i + 1);
            if (i == GradeNone) return 0;
            return otherRows[stat];
        }

        private string CurveText(int row)
        {
            if (row == 0) return "no growth";
            return (capsules.CurveValue(row, 0) / 16.0).ToString("0.0") + " -> " +
                   (capsules.CurveValue(row, 6) / 16.0).ToString("0.0") + " per level";
        }

        private void FillGradeChoices(int stat, int row)
        {
            ComboBox c = comboGrade[stat];
            c.Items.Clear();
            string[] tag = { "1 fastest", "2", "3", "4", "5 slowest" };
            for (int g = 1; g <= 5; g++) c.Items.Add(tag[g - 1] + "   (" + CurveText(RowFor(stat, g)) + ")");
            c.Items.Add("none   (no growth)");
            otherRows[stat] = row;
            int idx = IndexForRow(stat, row);
            if (idx == GradeOther) c.Items.Add("other: curve " + row + "   (" + CurveText(row) + ")");
            c.SelectedIndex = idx;
        }

        // ---------------- load / commit ----------------

        /// <summary>Called by Form1 after a ROM is opened or reloaded.</summary>
        public void LoadRom(Rom rom)
        {
            CapsuleMonsterData data = new CapsuleMonsterData(rom.RawData, rom.HasHeader);
            string problem = data.Validate();
            if (problem != null)
            {
                capsules = null;
                currentIndex = -1;
                comboCapsule.Items.Clear();
                this.Enabled = false;
                MessageBox.Show("Capsule monster editing is disabled for this ROM:\n" + problem,
                    "Capsule Monsters", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            int repaired = data.RepairNameEnds();
            if (repaired > 0)
                MessageBox.Show("Repaired " + repaired + " capsule monster name ending(s).\n\n" +
                    "An earlier version of this editor overwrote the end marker after each capsule\n" +
                    "monster's name, which makes the game reset when the capsule menu opens.\n" +
                    "Save the ROM to keep the repair.", "Capsule Monsters", MessageBoxButtons.OK, MessageBoxIcon.Information);

            int keep = comboCapsule.SelectedIndex;
            capsules = data;
            currentIndex = -1;
            loading = true;
            comboCapsule.Items.Clear();
            for (int i = 0; i < CapsuleMonsterData.Count; i++) comboCapsule.Items.Add(Describe(i));
            loading = false;
            this.Enabled = true;
            comboCapsule.SelectedIndex = (keep >= 0 && keep < CapsuleMonsterData.Count) ? keep : 0;
        }

        private string Describe(int i)
        {
            return capsules.GetName(i) + "  (" + CapsuleMonsterData.FamilyOf(i) + ", form " + CapsuleMonsterData.FormOf(i) + ")";
        }

        /// <summary>Writes the fields on screen back into the ROM data. Form1 calls this before saving.</summary>
        public void CommitCurrent()
        {
            if (capsules == null || currentIndex < 0) return;
            int idx = currentIndex;

            capsules.SetName(idx, textName.Text);
            for (int i = 0; i < 6; i++) capsules.SetByte(idx, BaseOffsets[i], (int)numBase[i].Value);
            capsules.SetByte(idx, CapsuleMonsterData.OFS_ATP_BONUS, (int)numAtpBonus.Value);
            capsules.SetByte(idx, CapsuleMonsterData.OFS_DFP_BONUS, (int)numDfpBonus.Value);

            int firstForm = idx - (idx % CapsuleMonsterData.FormsPerMonster);
            for (int i = 0; i < 6; i++)
            {
                int row = RowOf(i);
                capsules.SetByte(idx, CapsuleMonsterData.OFS_GROWTH + i, row);
                // Only grades you actually changed are copied to the other forms.
                if (checkAllForms.Checked && row != loadedRows[i])
                    for (int f = 0; f < CapsuleMonsterData.FormsPerMonster; f++)
                        capsules.SetByte(firstForm + f, CapsuleMonsterData.OFS_GROWTH + i, row);
                loadedRows[i] = row;
            }

            loading = true;
            UiHelpers.RenameItem(comboCapsule, idx, Describe(idx));
            loading = false;
        }

        public void RefreshView() { if (capsules != null && currentIndex >= 0) ShowCapsule(currentIndex); }

        private void comboCapsule_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (loading || capsules == null) return;
            int newIndex = comboCapsule.SelectedIndex;
            if (newIndex == currentIndex || newIndex < 0) return;
            CommitCurrent();
            if (comboCapsule.SelectedIndex != newIndex)
            {
                loading = true; comboCapsule.SelectedIndex = newIndex; loading = false;
            }
            ShowCapsule(newIndex);
        }

        private void ShowCapsule(int idx)
        {
            loading = true;
            currentIndex = idx;

            textName.Text = capsules.GetName(idx);
            for (int i = 0; i < 6; i++)
            {
                numBase[i].Value = capsules.GetByte(idx, BaseOffsets[i]);
                int row = capsules.GetByte(idx, CapsuleMonsterData.OFS_GROWTH + i);
                if (row > CapsuleMonsterData.MaxCurveRow) row = CapsuleMonsterData.MaxCurveRow;
                loadedRows[i] = row;
                FillGradeChoices(i, row);
            }
            numAtpBonus.Value = capsules.GetByte(idx, CapsuleMonsterData.OFS_ATP_BONUS);
            numDfpBonus.Value = capsules.GetByte(idx, CapsuleMonsterData.OFS_DFP_BONUS);

            labelInfo.Text = "Family: " + CapsuleMonsterData.FamilyOf(idx) +
                "    Form: " + CapsuleMonsterData.FormOf(idx) +
                "    Element: " + capsules.ElementName(idx) +
                "    Offset: " + capsules.RecordOffset(idx).ToString("X");

            string skills = "";
            for (int i = 0; i < 6; i++)
            {
                int s = capsules.GetByte(idx, CapsuleMonsterData.OFS_SKILLS + i);
                if (s != 0) skills += (skills.Length > 0 ? ", " : "") + s.ToString("X2");
            }
            labelSkills.Text = "Skill IDs (read-only for now): " + (skills.Length > 0 ? skills : "none");

            // Highlight what differs from the reference ROM.
            CapsuleMonsterData r = Reference.Capsules;
            ChangeHighlighter.Set(textName, r == null ? null : Reference.Try(delegate { return r.GetName(idx); }));
            for (int i = 0; i < 6; i++)
            {
                int bo = BaseOffsets[i], go = CapsuleMonsterData.OFS_GROWTH + i, stat = i;
                ChangeHighlighter.Set(numBase[i], r == null ? null : Reference.Try(delegate { return (int)r.GetByte(idx, bo); }));
                object origIndex = null;
                if (r != null)
                {
                    object origRow = Reference.Try(delegate { return (int)r.GetByte(idx, go); });
                    if (origRow != null)
                    {
                        int oi = IndexForRow(stat, (int)origRow);
                        // "other" only matches if it's still the same row.
                        if (oi != GradeOther || (int)origRow == otherRows[stat]) origIndex = oi;
                        else origIndex = -2;
                    }
                }
                ChangeHighlighter.Set(comboGrade[i], origIndex);
            }
            ChangeHighlighter.Set(numAtpBonus, r == null ? null : Reference.Try(delegate { return (int)r.GetByte(idx, CapsuleMonsterData.OFS_ATP_BONUS); }));
            ChangeHighlighter.Set(numDfpBonus, r == null ? null : Reference.Try(delegate { return (int)r.GetByte(idx, CapsuleMonsterData.OFS_DFP_BONUS); }));

            loading = false;
            UpdatePreview();
        }

        private void AnyValueChanged(object sender, EventArgs e)
        {
            if (loading) return;
            UpdatePreview();
        }

        // ---------------- preview + chart ----------------

        private int[] StatsAt(CapsuleMonsterData d, int[] bases, int[] rows, int atpBonus, int dfpBonus, int level, int form)
        {
            int[] g = new int[6];
            for (int i = 0; i < 6; i++) g[i] = d.GrownStat(bases[i], rows[i], level, form);
            int hp = g[0], str = g[1], agl = g[2], intel = g[3], gut = Math.Min(g[4], 199), mgr = g[5];
            return new int[] { hp, str + atpBonus, (str + agl) / 2 + dfpBonus, str, agl, intel, gut, mgr };
        }

        private void UpdatePreview()
        {
            if (capsules == null || currentIndex < 0) return;
            int form = CapsuleMonsterData.FormOf(currentIndex);
            int level = (int)numPreviewLevel.Value;

            int[] bases = new int[6], rows = new int[6];
            for (int i = 0; i < 6; i++) { bases[i] = (int)numBase[i].Value; rows[i] = RowOf(i); }
            int atpB = (int)numAtpBonus.Value, dfpB = (int)numDfpBonus.Value;

            int[] shown = StatsAt(capsules, bases, rows, atpB, dfpB, level, form);
            for (int i = 0; i < 8; i++) labelPreview[i].Text = shown[i].ToString();

            // Chart: HP, STR, AGL, INT, GUT, MGR for levels 1-99.
            int[][] cur = Curves(capsules, bases, rows, form);
            int[][] orig = null;
            CapsuleMonsterData r = Reference.Capsules;
            if (r != null && Reference.IsCleanRom)
            {
                try
                {
                    int[] ob = new int[6], orow = new int[6];
                    for (int i = 0; i < 6; i++)
                    {
                        ob[i] = r.GetByte(currentIndex, BaseOffsets[i]);
                        orow[i] = Math.Min((int)r.GetByte(currentIndex, CapsuleMonsterData.OFS_GROWTH + i), CapsuleMonsterData.MaxCurveRow);
                    }
                    orig = Curves(r, ob, orow, form);
                }
                catch (Exception) { orig = null; }
            }
            chart.SetData(cur, orig, level);
        }

        private static int[][] Curves(CapsuleMonsterData d, int[] bases, int[] rows, int form)
        {
            int[][] c = new int[6][];
            for (int s = 0; s < 6; s++)
            {
                c[s] = new int[GrowthChart.MaxLevel];
                for (int lv = 1; lv <= GrowthChart.MaxLevel; lv++)
                {
                    int v = d.GrownStat(bases[s], rows[s], lv, form);
                    if (s == 4 && v > 199) v = 199; // GUT is capped by the game
                    c[s][lv - 1] = v;
                }
            }
            return c;
        }
    }
}
