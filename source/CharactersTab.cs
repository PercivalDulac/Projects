using System;
using System.Drawing;
using System.Windows.Forms;

using com.lufiaIIEditor.RomInfo;

namespace com.lufiaIIEditor.UI
{
    /// <summary>
    /// "Characters" tab: base stats and growth for the seven party members, with a stat
    /// preview and a level 1-99 chart.
    ///
    /// Each stat's growth is 13 bands of 8 levels (band value = gain per level x 16, 0-255).
    /// The "Edit growth" dropdown switches the growth box between:
    ///   Overall  - one % per stat that scales the whole curve as it's currently shaped
    ///              (shown as % of the original game's curve);
    ///   Band N   - the raw value of that band for every stat.
    /// One working curve per stat is the source of truth, so both views always agree.
    /// </summary>
    class CharactersTab : TabPage
    {
        private static readonly string[] Names = ItemData.CharacterNames;
        private static readonly int[] ChartStats = { 0, 1, 2, 3, 4, 6 };   // HP MP STR AGL INT MGR
        private static readonly string[] ChartNames = { "HP", "MP", "STR", "AGL", "INT", "MGR" };
        private const int Stats = CharacterData.Stats;
        private const int Bands = 13;   // bands used up to level 99

        private CharacterData chars;
        private int current = -1;
        private bool loading = false;

        private int[][] work = new int[Stats][];      // curves that will be saved
        private int[][] baseCurves = new int[Stats][]; // original game's curves (or as opened)
        // % scaling works from a snapshot taken when you start changing the %, so it never compounds.
        private int[][] snap = new int[Stats][];
        private int[] snapPercent = new int[Stats];
        private int[] maxPercent = new int[Stats];

        private ComboBox comboChar;
        private ComboBox comboMode;
        private Label labelInfo;
        private NumericUpDown[] numBase = new NumericUpDown[Stats];
        private GroupBox gGrowth;
        private NumericUpDown[] numPercent = new NumericUpDown[Stats];
        private NumericUpDown[] numBand = new NumericUpDown[Stats];
        private Label[] labelUnit = new Label[Stats];
        private Label[] labelSummary = new Label[Stats];
        private Button buttonRestoreGrowth;
        private NumericUpDown numLevel;
        private Label[] labelPreview = new Label[Stats];
        private GrowthChart chart;
        private GroupBox gCave;
        private NumericUpDown[] numCave = new NumericUpDown[Stats];
        private CheckBox checkCave;
        private ToolTip tip = new ToolTip();

        public CharactersTab()
        {
            this.Text = "Characters";
            this.UseVisualStyleBackColor = true;
            Build();
            this.Enabled = false;
        }

        private void Build()
        {
            Controls.Add(UiHelpers.MakeLabel("Character:", 12, 15));
            comboChar = UiHelpers.MakeDropDown(90, 12, 180);
            comboChar.SelectedIndexChanged += new EventHandler(comboChar_SelectedIndexChanged);
            Controls.Add(comboChar);
            labelInfo = UiHelpers.MakeNote("", 290, 15);
            Controls.Add(labelInfo);

            Controls.Add(UiHelpers.MakeLabel("Edit growth:", 12, 45));
            comboMode = UiHelpers.MakeDropDown(90, 42, 230);
            comboMode.Items.Add("Overall (% of the original curve)");
            for (int b = 0; b < Bands; b++)
                comboMode.Items.Add("Band " + (b + 1) + ":  levels " + BandFrom(b) + "-" + BandTo(b));
            comboMode.SelectedIndex = 0;
            comboMode.SelectedIndexChanged += new EventHandler(comboMode_SelectedIndexChanged);
            Controls.Add(comboMode);
            Controls.Add(UiHelpers.MakeNote("a band's value = gain per level x16 (255 = about 16 per level)", 330, 45));

            GroupBox gBase = UiHelpers.MakeGroup("Base stats (before any growth)", 12, 75, 200, 250);
            Controls.Add(gBase);
            for (int s = 0; s < Stats; s++)
            {
                gBase.Controls.Add(UiHelpers.MakeLabel(CharacterData.StatNames[s], 12, 27 + s * 30));
                numBase[s] = UiHelpers.MakeNumeric(80, 24 + s * 30, 0, 255); // the game reads one byte
                numBase[s].ValueChanged += new EventHandler(AnyValueChanged);
                gBase.Controls.Add(numBase[s]);
            }

            gGrowth = UiHelpers.MakeGroup("", 225, 75, 330, 250);
            Controls.Add(gGrowth);
            for (int s = 0; s < Stats; s++)
            {
                int y = 24 + s * 30;
                gGrowth.Controls.Add(UiHelpers.MakeLabel(CharacterData.StatNames[s], 12, y + 3));

                numPercent[s] = UiHelpers.MakeNumeric(55, y, 0, 400);
                numPercent[s].Size = new Size(55, 20);
                numPercent[s].Increment = 5;
                numPercent[s].ValueChanged += new EventHandler(PercentChanged);
                gGrowth.Controls.Add(numPercent[s]);

                numBand[s] = UiHelpers.MakeNumeric(55, y, 0, 255);
                numBand[s].Size = new Size(55, 20);
                numBand[s].ValueChanged += new EventHandler(BandChanged);
                numBand[s].Visible = false;
                gGrowth.Controls.Add(numBand[s]);

                labelUnit[s] = UiHelpers.MakeLabel("%", 112, y + 3);
                gGrowth.Controls.Add(labelUnit[s]);
                labelSummary[s] = UiHelpers.MakeLabel("", 130, y + 3);
                labelSummary[s].AutoSize = false;
                labelSummary[s].Size = new Size(195, 17);
                gGrowth.Controls.Add(labelSummary[s]);
            }

            buttonRestoreGrowth = new Button();
            buttonRestoreGrowth.Text = "Restore original growth";
            buttonRestoreGrowth.Location = new Point(225, 330);
            buttonRestoreGrowth.Size = new Size(160, 23);
            buttonRestoreGrowth.Click += new EventHandler(buttonRestoreGrowth_Click);
            Controls.Add(buttonRestoreGrowth);
            Controls.Add(UiHelpers.MakeNote("blue = differs from clean ROM", 392, 335));

            gCave = UiHelpers.MakeGroup("Ancient Cave start bonus", 12, 330, 200, 123);
            Controls.Add(gCave);
            for (int s = 0; s < Stats; s++)
            {
                int x = s < 4 ? 8 : 106, y = 22 + (s % 4) * 24;
                gCave.Controls.Add(UiHelpers.MakeLabel(CharacterData.StatNames[s], x, y + 3));
                numCave[s] = UiHelpers.MakeNumeric(x + 36, y, 0, CharacterData.CaveBonusMax);
                numCave[s].Size = new Size(52, 20);
                numCave[s].ValueChanged += new EventHandler(AnyValueChanged);
                gCave.Controls.Add(numCave[s]);
                tip.SetToolTip(numCave[s], "Added on top of base + growth for the whole Ancient Cave run (the cave starts everyone at level 1).");
            }

            GroupBox gPreview = UiHelpers.MakeGroup("Stats at level (before equipment)", 225, 358, 330, 95);
            Controls.Add(gPreview);
            gPreview.Controls.Add(UiHelpers.MakeLabel("Level", 10, 23));
            numLevel = UiHelpers.MakeNumeric(50, 20, 1, CharacterData.MaxLevel);
            numLevel.Size = new Size(50, 20);
            numLevel.Value = 1;
            numLevel.ValueChanged += new EventHandler(AnyValueChanged);
            gPreview.Controls.Add(numLevel);
            checkCave = new CheckBox();
            checkCave.Text = "In the Ancient Cave (add the cave bonus)";
            checkCave.Location = new Point(112, 21);
            checkCave.AutoSize = true;
            checkCave.CheckedChanged += new EventHandler(AnyValueChanged);
            gPreview.Controls.Add(checkCave);
            for (int s = 0; s < Stats; s++)
            {
                int x = 10 + (s % 4) * 80, y = 48 + (s / 4) * 22;
                gPreview.Controls.Add(UiHelpers.MakeLabel(CharacterData.StatNames[s], x, y));
                labelPreview[s] = UiHelpers.MakeLabel("-", x + 28, y);
                labelPreview[s].Font = new Font(labelPreview[s].Font, FontStyle.Bold);
                gPreview.Controls.Add(labelPreview[s]);
            }

            GroupBox gChart = UiHelpers.Anchor(this, UiHelpers.MakeGroup("Growth, level 1-99", 565, 75, 410, 378), AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right);
            Controls.Add(gChart);
            chart = UiHelpers.Anchor(this, new GrowthChart(), AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right);
            chart.SeriesNames = ChartNames;
            chart.Location = new Point(8, 18);
            chart.Size = new Size(394, 351);
            gChart.Controls.Add(chart);

            Controls.Add(UiHelpers.Anchor(this, UiHelpers.MakeNote(
                "Overall %: scales a stat's curve as you've shaped it, up to where its fastest band reaches 255. Bands: set each 8-level band directly.\n" +
                "Stats stop at 999. GUT has no growth, and battles count it only up to 199. Changes show in new games and future level-ups.\n" +
                "Each level-up also rolls a random 0-4 on top of every stat that isn't 0 (even GUT; stats never drop), so the preview shows the range you can get.\n" +
                "Ancient Cave: the game puts everyone back to level 1 and adds the cave bonus for the whole run; it doesn't apply outside the cave.",
                12, 460), AnchorStyles.Bottom | AnchorStyles.Left));
            UpdateModeView();
        }

        // Band b covers the level-ups from level BandFrom(b) to BandTo(b).
        private static int BandFrom(int b) { return b == 0 ? 1 : b * 8; }
        private static int BandTo(int b) { return Math.Min(b * 8 + 8, CharacterData.MaxLevel); }

        private int Band { get { return comboMode.SelectedIndex - 1; } }  // -1 = overall

        // ---------------- load / commit ----------------

        public void LoadRom(CharacterData data)
        {
            int keep = comboChar.SelectedIndex;
            chars = data;
            current = -1;
            loading = true;
            comboChar.Items.Clear();
            foreach (string n in Names) comboChar.Items.Add(n);
            loading = false;
            this.Enabled = true;
            comboChar.SelectedIndex = (keep >= 0 && keep < CharacterData.Count) ? keep : 0;
        }

        public void Disable()
        {
            chars = null; current = -1;
            comboChar.Items.Clear();
            this.Enabled = false;
        }

        public void RefreshView() { if (chars != null && current >= 0) ShowCharacter(current); }

        public void CommitCurrent()
        {
            if (chars == null || current < 0) return;
            for (int s = 0; s < Stats; s++)
            {
                chars.SetBase(current, s, (int)numBase[s].Value);
                chars.SetCurve(current, s, work[s]);   // unchanged curves write back the same bytes
                if (chars.HasCave) chars.SetCaveBonus(current, s, (int)numCave[s].Value);
            }
        }

        private void comboChar_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (loading || chars == null) return;
            int n = comboChar.SelectedIndex;
            if (n < 0 || n == current) return;
            CommitCurrent();
            ShowCharacter(n);
        }

        private void ShowCharacter(int c)
        {
            loading = true;
            current = c;
            CharacterData r = Reference.Characters;
            CharacterData baseline = r != null ? r : chars;
            for (int s = 0; s < Stats; s++)
            {
                UiHelpers.SetValueClamped(numBase[s], chars.GetBase(c, s));
                work[s] = chars.GetCurve(c, s);
                baseCurves[s] = baseline.GetCurve(c, s);
                snap[s] = null;
                int stat = s;
                ChangeHighlighter.Set(numBase[s], r == null ? null : Reference.Try(delegate { return r.GetBase(c, stat); }));
            }
            bool cave = chars.HasCave;
            gCave.Enabled = cave;
            checkCave.Enabled = cave;
            if (!cave) checkCave.Checked = false;
            for (int s = 0; s < Stats; s++)
            {
                UiHelpers.SetValueClamped(numCave[s], cave ? chars.GetCaveBonus(c, s) : 0);
                int stat = s;
                ChangeHighlighter.Set(numCave[s], (r == null || !cave || !r.HasCave) ? null :
                    Reference.Try(delegate { return r.GetCaveBonus(c, stat); }));
            }
            gCave.Text = cave ? "Ancient Cave start bonus (level " + chars.GetCaveLevel(c) + ")" : "Ancient Cave bonus (not found)";
            buttonRestoreGrowth.Enabled = r != null && Reference.IsCleanRom;
            labelInfo.Text = "Growth table: " + (0xBB82C + c * 0x70).ToString("X") + "   Base stats: " + (0xBBB3C + c * 14).ToString("X");
            loading = false;
            UpdateModeView();
        }

        // ---------------- edit modes ----------------

        private void comboMode_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateModeView();
        }

        /// <summary>Shows % boxes or band boxes and fills them from the working curves.</summary>
        private void UpdateModeView()
        {
            int band = Band;
            bool overall = band < 0;
            gGrowth.Text = overall
                ? (Reference.IsCleanRom ? "Growth: overall (% of the original game's curve)" : "Growth: overall (% of the curve as opened)")
                : "Growth: band " + (band + 1) + ", levels " + BandFrom(band) + "-" + BandTo(band) + "  (raw value)";
            for (int s = 0; s < Stats; s++)
            {
                numPercent[s].Visible = overall;
                numBand[s].Visible = !overall;
                labelUnit[s].Text = overall ? "%" : "";
            }
            if (chars == null || current < 0) { UpdateAll(); return; }

            loading = true;
            CharacterData r = Reference.Characters;
            for (int s = 0; s < Stats; s++)
            {
                if (overall) RefreshPercentBox(s);
                else
                {
                    numBand[s].Value = work[s][band];
                    int stat = s, b = band;
                    ChangeHighlighter.Set(numBand[s], r == null ? null : Reference.Try(delegate { return r.GetGain(current, b, stat); }));
                    tip.SetToolTip(numBand[s], "Gain per level x16 for levels " + BandFrom(band) + "-" + BandTo(band) + " (0-255).");
                }
            }
            loading = false;
            chart.SetShade(overall ? 0 : BandFrom(band), overall ? 0 : BandTo(band));
            UpdateAll();
        }

        /// <summary>Sets a stat's % box from its working curve (no snapshot yet).</summary>
        private void RefreshPercentBox(int s)
        {
            int maxBand = 0;
            for (int i = 0; i < Bands; i++) if (work[s][i] > maxBand) maxBand = work[s][i];
            int pct = EstimatePercent(baseCurves[s], work[s]);
            int baseMax = 0;
            foreach (int v in baseCurves[s]) if (v > baseMax) baseMax = v;
            // Highest % that keeps the current shape: where the fastest band reaches 255.
            // (A curve set to 0% scales back up using the original shape.)
            if (maxBand > 0) maxPercent[s] = Math.Max(pct, pct * 255 / maxBand);
            else maxPercent[s] = baseMax > 0 ? 25500 / baseMax : 100;
            numPercent[s].Enabled = SumOf(baseCurves[s]) > 0;
            numPercent[s].Maximum = Math.Max(1, maxPercent[s]);
            UiHelpers.SetValueClamped(numPercent[s], pct);
            snap[s] = null;
            tip.SetToolTip(numPercent[s], !numPercent[s].Enabled
                ? "This stat has no growth curve to scale."
                : "Up to " + maxPercent[s] + "%: beyond that the fastest band would pass 255 (about 16 per level).");
        }

        private void PercentChanged(object sender, EventArgs e)
        {
            if (loading || current < 0) return;
            for (int s = 0; s < Stats; s++)
            {
                if (sender != numPercent[s]) continue;
                if (snap[s] == null)
                {
                    // First change since the curve was last shaped: remember it and its %.
                    if (SumOf(work[s]) == 0)
                    {
                        snap[s] = (int[])baseCurves[s].Clone();   // nothing left to scale: use the original shape
                        snapPercent[s] = 100;
                    }
                    else
                    {
                        snap[s] = (int[])work[s].Clone();
                        snapPercent[s] = EstimatePercent(baseCurves[s], work[s]);
                        if (snapPercent[s] <= 0) snapPercent[s] = 100;
                    }
                }
                int pct = (int)numPercent[s].Value;
                for (int i = 0; i < work[s].Length; i++)
                {
                    int v = (int)Math.Round(snap[s][i] * (double)pct / snapPercent[s], MidpointRounding.AwayFromZero);
                    work[s][i] = v > 255 ? 255 : v;
                }
            }
            UpdateAll();
        }

        private void BandChanged(object sender, EventArgs e)
        {
            if (loading || current < 0 || Band < 0) return;
            for (int s = 0; s < Stats; s++)
            {
                if (sender != numBand[s]) continue;
                work[s][Band] = (int)numBand[s].Value;
                snap[s] = null;   // the shape changed; the next % change starts from here
            }
            UpdateAll();
        }

        private void buttonRestoreGrowth_Click(object sender, EventArgs e)
        {
            CharacterData r = Reference.Characters;
            if (r == null || current < 0) return;
            for (int s = 0; s < Stats; s++)
            {
                work[s] = r.GetCurve(current, s);
                snap[s] = null;
            }
            UpdateModeView();
        }

        private void AnyValueChanged(object sender, EventArgs e)
        {
            if (loading) return;
            UpdateAll();
        }

        // ---------------- helpers ----------------

        private static int SumOf(int[] a) { int t = 0; foreach (int v in a) t += v; return t; }

        private static int EstimatePercent(int[] baseline, int[] cur)
        {
            int sb = SumOf(baseline);
            if (sb == 0) return 100;
            return (int)Math.Round(SumOf(cur) * 100.0 / sb, MidpointRounding.AwayFromZero);
        }

        /// <summary>The game's stat limits: 999, except GUT at 255.</summary>
        private static int StatCap(int stat) { return stat == 5 ? 255 : 999; }
        private static int Capped(int stat, int v) { int c = StatCap(stat); return v > c ? c : v; }

        /// <summary>What the game shows: base + growth (+ cave bonus), with GUT counted up to 199 ($81:F51C).</summary>
        private int Shown(int stat, int value, bool cave, int bonus)
        {
            int v = Capped(stat, value) + (cave ? bonus : 0);
            if (stat == 5 && v > 199) v = 199;
            return v > 999 ? 999 : v;
        }

        private bool CaveView { get { return checkCave.Checked && chars != null && chars.HasCave; } }

        // ---------------- summaries, preview, chart ----------------

        private void UpdateAll()
        {
            if (chars == null || current < 0) return;
            CharacterData r = Reference.IsCleanRom ? Reference.Characters : null;
            int level = (int)numLevel.Value;
            int band = Band;

            for (int s = 0; s < Stats; s++)
            {
                int b = (int)numBase[s].Value;
                int raw99 = CharacterData.StatAt(b, work[s], 99);
                int lv99 = Capped(s, raw99);
                bool changed = false;
                int[] orig = null;
                if (r != null)
                {
                    orig = r.GetCurve(current, s);
                    for (int i = 0; i < orig.Length; i++) if (orig[i] != work[s][i]) { changed = true; break; }
                }

                string text;
                if (SumOf(work[s]) == 0) text = b > 0 ? "no growth  (base, +0-4 from level-up rolls)" : "no growth  (stays at 0)";
                else if (band < 0)
                {
                    text = "Lv50 " + Capped(s, CharacterData.StatAt(b, work[s], 50)) + "  Lv99 " + lv99 + (raw99 > StatCap(s) ? " (max)" : "");
                    if (changed) text += " (was " + Capped(s, CharacterData.StatAt(r.GetBase(current, s), orig, 99)) + ")";
                }
                else
                {
                    text = "= " + (work[s][band] / 16.0).ToString("0.0") + " per level    Lv99 " + lv99 + (raw99 > StatCap(s) ? " (max)" : "");
                }
                labelSummary[s].Text = text;
                labelSummary[s].BackColor = changed ? ChangeHighlighter.ChangedColor : Color.Transparent;
                int f = CharacterData.StatAt(b, work[s], level);
                int bonus = (int)numCave[s].Value;
                int lo = Shown(s, f, CaveView, bonus);
                // Each level-up sets a stat to at least formula + random 0-4 ($81:F7ED) unless the
                // formula gives 0 (e.g. no MP); level 1 has had no level-ups yet.
                int hi = (level > 1 && f > 0) ? Shown(s, f + 4, CaveView, bonus) : lo;
                labelPreview[s].Text = hi > lo ? lo + "-" + hi : lo.ToString();
            }

            int[][] cur = new int[6][];
            int[][] orig6 = r != null ? new int[6][] : null;
            for (int k = 0; k < 6; k++)
            {
                int s = ChartStats[k];
                cur[k] = new int[GrowthChart.MaxLevel];
                for (int lv = 1; lv <= GrowthChart.MaxLevel; lv++)
                    cur[k][lv - 1] = Shown(s, CharacterData.StatAt((int)numBase[s].Value, work[s], lv), CaveView, (int)numCave[s].Value);
                if (orig6 != null)
                {
                    int[] oc = r.GetCurve(current, s);
                    int ob = r.GetBase(current, s);
                    int obonus = r.HasCave ? r.GetCaveBonus(current, s) : 0;
                    orig6[k] = new int[GrowthChart.MaxLevel];
                    for (int lv = 1; lv <= GrowthChart.MaxLevel; lv++)
                        orig6[k][lv - 1] = Shown(s, CharacterData.StatAt(ob, oc, lv), CaveView, obonus);
                }
            }
            chart.SetData(cur, orig6, level);
        }
    }
}
