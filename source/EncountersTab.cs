using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

using com.lufiaIIEditor.RomInfo;

namespace com.lufiaIIEditor.UI
{
    /// <summary>
    /// "Encounters" tab: monster groups, world map zones, sea encounters and fixed battles
    /// (see EncounterData). Every edit is written to the ROM data straight away, in place.
    /// </summary>
    class EncountersTab : TabPage
    {
        private EncounterData enc;
        private EnemyData enemies;
        private bool loading;

        private ComboBox comboSection;
        private Panel pGroups, pWorld, pSea, pBattles, pMaps, pCave, pFind, pRate;

        // groups
        private ListBox listGroups;
        private ComboBox[] groupSlot = new ComboBox[EncounterData.GroupSize];
        private Label[] groupSlotLabel = new Label[EncounterData.GroupSize];
        private Label groupExplain, groupUsage, groupStats;
        private NumericUpDown groupSprite;
        private Label groupSpriteNote;
        private int currentGroup = -1;

        // world
        private ListBox listZones;
        private ComboBox[] zoneGroup = new ComboBox[EncounterData.ZoneSlots];
        private NumericUpDown[] zoneWeight = new NumericUpDown[EncounterData.ZoneSlots];
        private Label[] zonePct = new Label[EncounterData.ZoneSlots];
        private PictureBox zoneMap;
        private Label zoneMapNote, zoneSummary;
        private byte[] zoneGrid;
        private int currentZone = -1;

        // sea
        private ComboBox[] seaGroup = new ComboBox[EncounterData.SeaCount];

        // battles
        private ListBox listBattles;
        private ComboBox[] battleEnemy = new ComboBox[6];
        private NumericUpDown[] battleX = new NumericUpDown[6], battleY = new NumericUpDown[6];
        private Label battleInfo;
        private int currentBattle = -1;

        // dungeon maps
        private ListBox listMaps;
        private Panel mapRows;
        private Label mapNote;
        private TextBox mapName;
        private NumericUpDown rateSafe, rateRamp, rateThreshold, rateSea;
        private Label ratePreview;
        private List<ComboBox> mapCombos = new List<ComboBox>();
        private int currentMap = -1;

        // cave
        private NumericUpDown caveFloor;
        private ComboBox[] caveGroup = new ComboBox[6];
        private Label[] caveEntryLabel = new Label[6], caveShare = new Label[6];
        private ListBox listFloors;

        // find
        private ComboBox findEnemy;
        private TextBox findResult;

        private static readonly Color[] ZoneColors = {
            Color.FromArgb(40, 60, 110), Color.FromArgb(230, 200, 90), Color.FromArgb(120, 190, 80), Color.FromArgb(200, 120, 70),
            Color.FromArgb(150, 110, 200), Color.FromArgb(90, 180, 180), Color.FromArgb(220, 120, 160), Color.FromArgb(170, 170, 60),
            Color.FromArgb(80, 140, 220), Color.FromArgb(210, 90, 90), Color.FromArgb(110, 200, 140), Color.FromArgb(240, 160, 60),
            Color.FromArgb(140, 90, 60), Color.FromArgb(180, 220, 230), Color.FromArgb(100, 100, 170), Color.FromArgb(200, 200, 200),
            Color.FromArgb(60, 140, 90), Color.FromArgb(90, 90, 90) };

        public EncountersTab()
        {
            this.Text = "Encounters";
            this.UseVisualStyleBackColor = true;
            Build();
            this.Enabled = false;
        }

        private Panel MakePanel()
        {
            Panel p = new Panel();
            p.Location = new Point(0, 40);
            p.Size = new Size(985, 475);
            p.Visible = false;
            Controls.Add(UiHelpers.Anchor(this, p, AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right));
            return p;
        }

        private void Build()
        {
            Controls.Add(UiHelpers.MakeLabel("Show:", 12, 15));
            comboSection = UiHelpers.MakeDropDown(60, 12, 220);
            comboSection.Items.AddRange(new object[] { "Monster groups", "World map zones", "Sea encounters", "Boss and scripted battles",
                "Dungeon and town maps", "Ancient Cave floors", "Find an enemy", "World map encounter rate" });
            comboSection.SelectedIndexChanged += delegate { ShowSection(); };
            Controls.Add(comboSection);
            Controls.Add(UiHelpers.MakeNote("Random fights are built from monster groups; the world map, sea, dungeons and the Ancient Cave pick groups.", 295, 15));

            pGroups = MakePanel(); pWorld = MakePanel(); pSea = MakePanel(); pBattles = MakePanel();
            pMaps = MakePanel(); pCave = MakePanel(); pFind = MakePanel(); pRate = MakePanel();
            BuildGroups(); BuildWorld(); BuildSea(); BuildBattles(); BuildMaps(); BuildCave(); BuildFind(); BuildRate();
        }

        // ---------------- building ----------------

        private void BuildGroups()
        {
            listGroups = new ListBox();
            listGroups.Location = new Point(12, 5);
            listGroups.Size = new Size(330, 460);
            listGroups.IntegralHeight = false;
            listGroups.SelectedIndexChanged += delegate { if (!loading) ShowGroup(listGroups.SelectedIndex); };
            pGroups.Controls.Add(UiHelpers.Anchor(this, listGroups, AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left));

            string[] names = { "1  Leader", "2  Alternate leader", "3", "4", "5", "6", "7", "8" };
            for (int k = 0; k < EncounterData.GroupSize; k++)
            {
                groupSlotLabel[k] = UiHelpers.MakeLabel(names[k], 360, 10 + k * 30);
                pGroups.Controls.Add(groupSlotLabel[k]);
                groupSlot[k] = UiHelpers.MakeDropDown(490, 7 + k * 30, 200);
                int slot = k;
                groupSlot[k].SelectedIndexChanged += delegate { GroupSlotChanged(slot); };
                pGroups.Controls.Add(groupSlot[k]);
            }
            pGroups.Controls.Add(UiHelpers.MakeLabel("Cave map sprite", 360, 253));
            groupSprite = UiHelpers.MakeNumeric(490, 250, 0, 255);
            groupSprite.Size = new Size(60, 20);
            groupSprite.ValueChanged += delegate { GroupSpriteChanged(); };
            pGroups.Controls.Add(groupSprite);
            groupSpriteNote = UiHelpers.MakeNote("", 558, 253);
            pGroups.Controls.Add(groupSpriteNote);

            groupStats = UiHelpers.MakeLabel("", 705, 7);
            groupStats.AutoSize = false;
            groupStats.Size = new Size(275, 235);
            groupStats.Font = new Font(FontFamily.GenericMonospace, 8.25f);
            pGroups.Controls.Add(groupStats);

            groupExplain = UiHelpers.MakeNote("", 360, 280);
            groupExplain.AutoSize = false;
            groupExplain.Size = new Size(600, 80);
            pGroups.Controls.Add(groupExplain);
            groupUsage = UiHelpers.MakeLabel("", 360, 362);
            groupUsage.AutoSize = false;
            groupUsage.Size = new Size(600, 100);
            pGroups.Controls.Add(groupUsage);
        }

        private void BuildWorld()
        {
            listZones = new ListBox();
            listZones.Location = new Point(12, 5);
            listZones.Size = new Size(330, 230);
            listZones.IntegralHeight = false;
            listZones.SelectedIndexChanged += delegate { if (!loading) ShowZone(listZones.SelectedIndex + 1); };
            pWorld.Controls.Add(listZones);

            pWorld.Controls.Add(UiHelpers.MakeLabel("Group", 12, 248));
            pWorld.Controls.Add(UiHelpers.MakeLabel("Weight", 250, 248));
            for (int k = 0; k < EncounterData.ZoneSlots; k++)
            {
                int slot = k;
                zoneGroup[k] = UiHelpers.MakeDropDown(12, 268 + k * 30, 230);
                zoneGroup[k].SelectedIndexChanged += delegate { ZoneChanged(slot); };
                pWorld.Controls.Add(zoneGroup[k]);
                zoneWeight[k] = UiHelpers.MakeNumeric(250, 268 + k * 30, 0, 255);
                zoneWeight[k].Size = new Size(55, 20);
                zoneWeight[k].ValueChanged += delegate { ZoneChanged(slot); };
                pWorld.Controls.Add(zoneWeight[k]);
                zonePct[k] = UiHelpers.MakeNote("", 310, 271 + k * 30);
                pWorld.Controls.Add(zonePct[k]);
            }
            pWorld.Controls.Add(UiHelpers.MakeNote("Each fight picks one group; the chance is its weight / the total.", 12, 420));
            zoneSummary = UiHelpers.MakeLabel("", 12, 440);
            zoneSummary.AutoSize = false;
            zoneSummary.Size = new Size(380, 34);
            pWorld.Controls.Add(zoneSummary);

            zoneMap = new PictureBox();
            zoneMap.Location = new Point(400, 5);
            zoneMap.Size = new Size(384, 384);
            zoneMap.SizeMode = PictureBoxSizeMode.StretchImage;
            zoneMap.BorderStyle = BorderStyle.FixedSingle;
            zoneMap.MouseClick += new MouseEventHandler(zoneMap_MouseClick);
            pWorld.Controls.Add(zoneMap);
            zoneMapNote = UiHelpers.MakeNote("The world map's encounter zones (64 x 64 blocks of 4 x 4 tiles). Click a zone to\n" +
                "select it. The selected zone is bright. The zone layout itself isn't editable here.", 400, 395);
            pWorld.Controls.Add(zoneMapNote);
        }

        private void BuildSea()
        {
            pSea.Controls.Add(UiHelpers.MakeNote(
                "At sea, the game picks one of 6 neighbouring entries at random. Which 6 moves down the list as a party\n" +
                "value grows (the address $0BBB, probably a level): below 32 the first six, at 48 and up the last six.", 12, 5));
            for (int i = 0; i < EncounterData.SeaCount; i++)
            {
                int slot = i;
                int col = i < 8 ? 0 : 1, row = i < 8 ? i : i - 8;
                int lo = 30 + 2 * Math.Max(0, i - 5), hi = 31 + 2 * Math.Min(9, i);
                string when = (i <= 5 ? "below " + (hi + 1) : i >= 9 ? lo + " and up" : lo + "-" + hi);
                pSea.Controls.Add(UiHelpers.MakeLabel("Entry " + (i + 1), 12 + col * 470, 48 + row * 32));
                seaGroup[i] = UiHelpers.MakeDropDown(80 + col * 470, 45 + row * 32, 250);
                seaGroup[i].SelectedIndexChanged += delegate { SeaChanged(slot); };
                pSea.Controls.Add(seaGroup[i]);
                pSea.Controls.Add(UiHelpers.MakeNote(when, 338 + col * 470, 48 + row * 32));
            }
        }

        private void BuildBattles()
        {
            listBattles = new ListBox();
            listBattles.Location = new Point(12, 5);
            listBattles.Size = new Size(330, 460);
            listBattles.IntegralHeight = false;
            listBattles.SelectedIndexChanged += delegate { if (!loading) ShowBattle(listBattles.SelectedIndex); };
            pBattles.Controls.Add(UiHelpers.Anchor(this, listBattles, AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left));
            pBattles.Controls.Add(UiHelpers.MakeLabel("Enemy", 360, 10));
            pBattles.Controls.Add(UiHelpers.MakeLabel("X", 580, 10));
            pBattles.Controls.Add(UiHelpers.MakeLabel("Y", 650, 10));
            for (int k = 0; k < 6; k++)
            {
                int slot = k;
                battleEnemy[k] = UiHelpers.MakeDropDown(360, 30 + k * 30, 210);
                battleEnemy[k].SelectedIndexChanged += delegate { BattleChanged(slot); };
                pBattles.Controls.Add(battleEnemy[k]);
                battleX[k] = UiHelpers.MakeNumeric(580, 30 + k * 30, 0, 255);
                battleX[k].Size = new Size(60, 20);
                battleX[k].ValueChanged += delegate { BattleChanged(slot); };
                pBattles.Controls.Add(battleX[k]);
                battleY[k] = UiHelpers.MakeNumeric(650, 30 + k * 30, 0, 255);
                battleY[k].Size = new Size(60, 20);
                battleY[k].ValueChanged += delegate { BattleChanged(slot); };
                pBattles.Controls.Add(battleY[k]);
            }
            battleInfo = UiHelpers.MakeNote("", 360, 220);
            battleInfo.AutoSize = false;
            battleInfo.Size = new Size(600, 120);
            pBattles.Controls.Add(battleInfo);
        }

        private void BuildMaps()
        {
            listMaps = new ListBox();
            listMaps.Location = new Point(12, 5);
            listMaps.Size = new Size(330, 460);
            listMaps.IntegralHeight = false;
            listMaps.SelectedIndexChanged += delegate { if (!loading) ShowMap(listMaps.SelectedIndex); };
            pMaps.Controls.Add(UiHelpers.Anchor(this, listMaps, AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left));
            pMaps.Controls.Add(UiHelpers.MakeLabel("Name", 360, 8));
            mapName = new TextBox();
            mapName.Location = new Point(400, 5);
            mapName.Size = new Size(250, 20);
            mapName.MaxLength = 40;
            pMaps.Controls.Add(mapName);
            Button saveName = new Button();
            saveName.Text = "Save name";
            saveName.Location = new Point(658, 3);
            saveName.Size = new Size(85, 24);
            saveName.Click += delegate { SaveMapName(); };
            pMaps.Controls.Add(saveName);
            mapName.KeyDown += delegate(object o, KeyEventArgs k) { if (k.KeyCode == Keys.Enter) { SaveMapName(); k.SuppressKeyPress = true; } };
            pMaps.Controls.Add(UiHelpers.MakeNote("The game has no map names; yours are kept in MapNames.txt\nnext to the editor, so you can share it.", 750, 3));
            mapNote = UiHelpers.MakeNote("", 360, 35);
            mapNote.AutoSize = false;
            mapNote.Size = new Size(610, 60);
            pMaps.Controls.Add(mapNote);
            mapRows = new Panel();
            mapRows.Location = new Point(360, 98);
            mapRows.Size = new Size(610, 370);
            mapRows.AutoScroll = true;
            pMaps.Controls.Add(UiHelpers.Anchor(this, mapRows, AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right));
        }

        private void BuildCave()
        {
            pCave.Controls.Add(UiHelpers.MakeLabel("Floor", 12, 10));
            caveFloor = UiHelpers.MakeNumeric(60, 7, 1, 99);
            caveFloor.Size = new Size(60, 20);
            caveFloor.ValueChanged += delegate { ShowCaveFloor(); };
            pCave.Controls.Add(caveFloor);
            Label n = UiHelpers.MakeNote(
                "The cave's groups are one long list. Floor n picks at random from 6 entries in a row, starting at entry 2n - 1, " +
                "so each entry is shared with the floors around it (shown on the right of each entry).", 12, 35);
            n.AutoSize = false;
            n.Size = new Size(470, 45);
            pCave.Controls.Add(n);
            for (int k = 0; k < 6; k++)
            {
                int slot = k;
                caveEntryLabel[k] = UiHelpers.MakeLabel("", 12, 90 + k * 30);
                pCave.Controls.Add(caveEntryLabel[k]);
                caveGroup[k] = UiHelpers.MakeDropDown(90, 87 + k * 30, 260);
                caveGroup[k].SelectedIndexChanged += delegate { CaveChanged(slot); };
                pCave.Controls.Add(caveGroup[k]);
                caveShare[k] = UiHelpers.MakeNote("", 358, 90 + k * 30);
                pCave.Controls.Add(caveShare[k]);
            }
            pCave.Controls.Add(UiHelpers.MakeNote(
                "A group's look on the cave map is its \"Cave map sprite\" (Monster groups section).", 12, 275));
            listFloors = new ListBox();
            listFloors.Location = new Point(500, 5);
            listFloors.Size = new Size(470, 460);
            listFloors.IntegralHeight = false;
            listFloors.SelectedIndexChanged += delegate { if (!loading && listFloors.SelectedIndex >= 0) caveFloor.Value = listFloors.SelectedIndex + 1; };
            pCave.Controls.Add(UiHelpers.Anchor(this, listFloors, AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right));
        }

        private void BuildFind()
        {
            pFind.Controls.Add(UiHelpers.MakeLabel("Enemy", 12, 10));
            findEnemy = UiHelpers.MakeDropDown(65, 7, 230);
            findEnemy.MaxDropDownItems = 20;
            findEnemy.SelectedIndexChanged += delegate { ShowFind(); };
            pFind.Controls.Add(findEnemy);
            findResult = new TextBox();
            findResult.Multiline = true;
            findResult.ReadOnly = true;
            findResult.ScrollBars = ScrollBars.Vertical;
            findResult.Location = new Point(12, 40);
            findResult.Size = new Size(960, 425);
            pFind.Controls.Add(UiHelpers.Anchor(this, findResult, AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right));
        }

        private void BuildRate()
        {
            Label n = UiHelpers.MakeNote(
                "How often fights happen while walking on the world map ($86:9C71). Each step adds 1 to a step counter " +
                "(back to 0 after a fight). Below the safe steps nothing can happen. After that the game rolls a number " +
                "from 0 to (steps + ramp - 1), at most 254, and a fight starts when the roll is the threshold or more. So the " +
                "chance grows with every step until it levels off. Dungeons use visible monsters instead.", 12, 5);
            n.AutoSize = false;
            n.Size = new Size(700, 60);
            pRate.Controls.Add(n);
            string[] labels = { "Safe steps", "Ramp", "Threshold", "Sea: fight chance %" };
            string[] notes = { "steps with no fights after each fight (original 6)",
                "higher = the chance climbs sooner (original 214)",
                "higher = rarer fights (original 220)",
                "at sea, a fight that passes the check above still needs this % roll (original 40)" };
            NumericUpDown[] nums = new NumericUpDown[4];
            for (int k = 0; k < 4; k++)
            {
                pRate.Controls.Add(UiHelpers.MakeLabel(labels[k], 12, 78 + k * 32));
                nums[k] = UiHelpers.MakeNumeric(150, 75 + k * 32, 0, k == 3 ? 100 : 255);
                nums[k].Size = new Size(60, 20);
                nums[k].ValueChanged += delegate { RateChanged(); };
                pRate.Controls.Add(nums[k]);
                pRate.Controls.Add(UiHelpers.MakeNote(notes[k], 220, 78 + k * 32));
            }
            rateSafe = nums[0]; rateRamp = nums[1]; rateThreshold = nums[2]; rateSea = nums[3];
            ratePreview = UiHelpers.MakeLabel("", 12, 215);
            ratePreview.AutoSize = false;
            ratePreview.Size = new Size(700, 200);
            ratePreview.Font = new Font(FontFamily.GenericMonospace, 9f);
            pRate.Controls.Add(ratePreview);
        }

        private void ShowRate()
        {
            if (enc == null) return;
            bool has = enc.HasRate;
            rateSafe.Enabled = rateRamp.Enabled = rateThreshold.Enabled = rateSea.Enabled = has;
            if (!has) { ratePreview.Text = "The encounter-rate code isn't the original, so it isn't editable here."; return; }
            loading = true;
            UiHelpers.SetValueClamped(rateSafe, enc.SafeSteps);
            UiHelpers.SetValueClamped(rateRamp, enc.Ramp);
            UiHelpers.SetValueClamped(rateThreshold, enc.Threshold);
            UiHelpers.SetValueClamped(rateSea, enc.SeaChance);
            ChangeHighlighter.Set(rateSafe, RefByte(enc.SafeStepsOffset));
            ChangeHighlighter.Set(rateRamp, RefByte(enc.RampOffset));
            ChangeHighlighter.Set(rateThreshold, RefByte(enc.ThresholdOffset));
            ChangeHighlighter.Set(rateSea, RefByte(enc.SeaChanceOffset));
            loading = false;
            UpdateRatePreview();
        }

        private void RateChanged()
        {
            if (loading || enc == null || !enc.HasRate) return;
            enc.SafeSteps = (int)rateSafe.Value;
            enc.Ramp = (int)rateRamp.Value;
            enc.Threshold = (int)rateThreshold.Value;
            enc.SeaChance = (int)rateSea.Value;
            UpdateRatePreview();
        }

        private void UpdateRatePreview()
        {
            int safe = (int)rateSafe.Value, ramp = (int)rateRamp.Value, th = (int)rateThreshold.Value, sea = (int)rateSea.Value;
            System.Text.StringBuilder sb = new System.Text.StringBuilder("Chance of a fight on a step:\n");
            foreach (int st in new int[] { safe, safe + 5, safe + 10, safe + 20, safe + 30, safe + 50 })
                sb.Append("  step " + st.ToString().PadLeft(3) + ": " + (EncounterData.StepChance(st, safe, ramp, th) * 100).ToString("0.0").PadLeft(5) + "%\n");
            double avg = EncounterData.AverageSteps(safe, ramp, th);
            sb.Append("\n");
            if (avg < 0) sb.Append("Fights (almost) never happen with these numbers.\n");
            else
            {
                sb.Append("Average steps between fights on land: " + avg.ToString("0") + "\n");
                sb.Append("At sea: " + (sea == 0 ? "no fights" : "about " + (avg * 100.0 / sea).ToString("0") + " steps") + "\n");
            }
            sb.Append("(The original game: about 25 steps on land, about 62 at sea.)");
            ratePreview.Text = sb.ToString();
        }

        // ---------------- loading ----------------

        public void LoadRom(EncounterData e, EnemyData en)
        {
            enc = e; enemies = en;
            loading = true;
            foreach (ComboBox c in groupSlot) FillEnemyList(c, true);
            foreach (ComboBox c in battleEnemy) FillEnemyList(c, true);
            RefreshGroupLists();
            listZones.Items.Clear();
            for (int z = 1; z < EncounterData.ZoneCount; z++) listZones.Items.Add(ZoneLabel(z));
            listBattles.Items.Clear();
            for (int b = 0; b < enc.BattleCount; b++) listBattles.Items.Add(BattleLabel(b));
            listMaps.Items.Clear();
            for (int m = 0; m < EncounterData.MapCount; m++) listMaps.Items.Add(MapLabel(m));
            foreach (ComboBox c in caveGroup) FillGroupCombo(c);
            RefreshFloors();
            findEnemy.Items.Clear();
            for (int v = 0; v < EnemyData.Count; v++) findEnemy.Items.Add(EnemyName(v));
            zoneGrid = enc.Resource(EncounterData.ZoneGridResource);
            if (zoneGrid == null || zoneGrid.Length < 4096) { zoneGrid = null; zoneMap.Visible = false; zoneMapNote.Text = "(The zone map couldn't be read.)"; }
            loading = false;
            this.Enabled = true;
            if (comboSection.SelectedIndex < 0) comboSection.SelectedIndex = 0; else ShowSection();
            if (listGroups.Items.Count > 0 && listGroups.SelectedIndex < 0) listGroups.SelectedIndex = 0;
            if (listZones.Items.Count > 0 && listZones.SelectedIndex < 0) listZones.SelectedIndex = 0;
            if (listBattles.Items.Count > 0 && listBattles.SelectedIndex < 0) listBattles.SelectedIndex = 0;
            ShowSea();
            if (listMaps.Items.Count > 0 && listMaps.SelectedIndex < 0) listMaps.SelectedIndex = 0;
            ShowCaveFloor();
            ShowRate();
        }

        public void Disable()
        {
            enc = null;
            this.Enabled = false;
        }

        public void RefreshView()
        {
            if (enc == null) return;
            loading = true;
            foreach (ComboBox c in groupSlot) RenameEnemyList(c);
            foreach (ComboBox c in battleEnemy) RenameEnemyList(c);
            RefreshGroupLists();
            for (int z = 1; z < EncounterData.ZoneCount; z++) listZones.Items[z - 1] = ZoneLabel(z);
            for (int b = 0; b < enc.BattleCount; b++) listBattles.Items[b] = BattleLabel(b);
            for (int m = 0; m < EncounterData.MapCount; m++) listMaps.Items[m] = MapLabel(m);
            for (int v = 0; v < EnemyData.Count && v < findEnemy.Items.Count; v++) findEnemy.Items[v] = EnemyName(v);
            RefreshFloors();
            loading = false;
            ShowGroup(currentGroup); ShowZone(currentZone); ShowSea(); ShowBattle(currentBattle);
            ShowMap(currentMap); ShowCaveFloor(); ShowFind(); ShowRate();
        }

        private void ShowSection()
        {
            int s = comboSection.SelectedIndex;
            pGroups.Visible = s == 0; pWorld.Visible = s == 1; pSea.Visible = s == 2; pBattles.Visible = s == 3;
            pMaps.Visible = s == 4; pCave.Visible = s == 5; pFind.Visible = s == 6; pRate.Visible = s == 7;
            if (s == 0 && enc != null) ShowGroup(currentGroup);   // usage may have changed
        }

        // ---------------- names ----------------

        private string EnemyName(int id)
        {
            if (id == 0xFF) return "(none)";
            if (enemies != null && id < EnemyData.Count) return enemies.GetName(id) + " (" + id.ToString("X2") + ")";
            return "value " + id.ToString("X2");
        }

        private string EnemyShort(int id)
        {
            return enemies != null && id < EnemyData.Count ? enemies.GetName(id) : "#" + id.ToString("X2");
        }

        /// <summary>Items: index 0..255 = byte value (FF = none).</summary>
        private void FillEnemyList(ComboBox c, bool withNone)
        {
            c.Items.Clear();
            for (int v = 0; v < 256; v++) c.Items.Add(EnemyName(v));
            c.MaxDropDownItems = 20;
        }

        private void RenameEnemyList(ComboBox c)
        {
            for (int v = 0; v < 256; v++)
            {
                string n = EnemyName(v);
                if ((string)c.Items[v] != n) c.Items[v] = n;
            }
        }

        private string GroupSummary(int g)
        {
            if (enc.IsFixedLineup(g))
            {
                List<string> f = new List<string>();
                for (int k = 2; k < EncounterData.GroupSize; k++) if (enc.GetGroupSlot(g, k) != 0xFF) f.Add(EnemyShort(enc.GetGroupSlot(g, k)));
                return "fixed: " + string.Join(", ", f.ToArray());
            }
            List<string> l = new List<string>();
            int a = enc.GetGroupSlot(g, 0), b = enc.GetGroupSlot(g, 1);
            if (a != 0xFF) l.Add(b != 0xFF ? EnemyShort(a) + "/" + EnemyShort(b) : EnemyShort(a));
            for (int k = 2; k < EncounterData.GroupSize; k++) if (enc.GetGroupSlot(g, k) != 0xFF) l.Add(EnemyShort(enc.GetGroupSlot(g, k)));
            return l.Count == 0 ? "(empty)" : string.Join(", ", l.ToArray());
        }

        private string GroupLabel(int g) { return "G" + g.ToString("D3") + "  " + GroupSummary(g); }

        private void RefreshGroupLists()
        {
            bool was = loading; loading = true;
            if (listGroups.Items.Count != EncounterData.GroupCount)
            {
                listGroups.Items.Clear();
                for (int g = 0; g < EncounterData.GroupCount; g++) listGroups.Items.Add(GroupLabel(g));
            }
            else for (int g = 0; g < EncounterData.GroupCount; g++)
                {
                    string n = GroupLabel(g);
                    if ((string)listGroups.Items[g] != n) listGroups.Items[g] = n;
                }
            List<ComboBox> lists = new List<ComboBox>(zoneGroup);
            lists.AddRange(seaGroup);
            lists.AddRange(caveGroup);
            lists.AddRange(mapCombos);
            foreach (ComboBox c in lists) FillGroupCombo(c);
            loading = was;
        }

        private void FillGroupCombo(ComboBox c)
        {
            bool was = loading; loading = true;
            if (c.Items.Count != 256)
            {
                c.Items.Clear();
                for (int v = 0; v < 256; v++) c.Items.Add(v < EncounterData.GroupCount ? GroupLabel(v) : "value " + v.ToString("X2"));
                c.DropDownWidth = 360;
                c.MaxDropDownItems = 20;
            }
            else for (int v = 0; v < EncounterData.GroupCount; v++)
                {
                    string n = GroupLabel(v);
                    if ((string)c.Items[v] != n) c.Items[v] = n;
                }
            loading = was;
        }

        // ---------------- enemy stats ----------------

        private string Stats(int id)
        {
            if (enemies == null || id >= EnemyData.Count) return "";
            return string.Format("Lv{0,3} HP{1,6} EXP{2,6} G{3,6}",
                enemies.GetByte(id, EnemyData.OFS_LEVEL), enemies.GetWord(id, EnemyData.OFS_HP),
                enemies.GetWord(id, EnemyData.OFS_EXP), enemies.GetWord(id, EnemyData.OFS_GOLD));
        }

        /// <summary>The kinds of enemy a group can bring (each once).</summary>
        private List<int> GroupKinds(int g)
        {
            List<int> l = new List<int>();
            for (int k = enc.IsFixedLineup(g) ? 2 : 0; k < EncounterData.GroupSize; k++)
            {
                int v = enc.GetGroupSlot(g, k);
                if (v != 0xFF && v < EnemyData.Count && !l.Contains(v)) l.Add(v);
            }
            return l;
        }

        private string ZoneLabel(int z)
        {
            List<string> l = new List<string>();
            for (int k = 0; k < EncounterData.ZoneSlots; k++)
                if (enc.GetZoneWeight(z, k) > 0) l.Add("G" + enc.GetZoneGroup(z, k).ToString("D3"));
            return "Zone " + z + ":  " + (l.Count == 0 ? "no encounters" : string.Join(" ", l.ToArray()));
        }

        private string BattleLabel(int b)
        {
            List<string> l = new List<string>();
            for (int k = 0; k < 6; k++) if (enc.GetBattleEnemy(b, k) != 0xFF) l.Add(EnemyShort(enc.GetBattleEnemy(b, k)));
            return b.ToString("D2") + "  " + (l.Count == 0 ? "(empty)" : string.Join(", ", l.ToArray()));
        }

        // ---------------- highlighting ----------------

        private static object RefByte(int offset)
        {
            int r = Reference.Shift(offset);
            if (Reference.Data == null || !Reference.InRange(r, 1)) return null;
            return (int)Reference.Data[r];
        }

        // ---------------- groups ----------------

        private void ShowGroup(int g)
        {
            if (enc == null || g < 0) return;
            loading = true;
            currentGroup = g;
            bool fixedLineup = enc.IsFixedLineup(g);
            for (int k = 0; k < EncounterData.GroupSize; k++)
            {
                groupSlot[k].SelectedIndex = enc.GetGroupSlot(g, k);
                object r = RefByte(enc.GroupOffset(g) + k);
                ChangeHighlighter.Set(groupSlot[k], r);
            }
            groupSprite.Value = enc.GetCaveSprite(g);
            ChangeHighlighter.Set(groupSprite, RefByte(enc.CaveSprites + g));
            groupSlotLabel[0].Text = fixedLineup ? "1  (empty: fixed)" : "1  Leader";
            groupSlotLabel[1].Text = fixedLineup ? "2  Raw value" : "2  Alternate leader";
            for (int k = 2; k < EncounterData.GroupSize; k++)
                groupSlotLabel[k].Text = fixedLineup ? (k + 1) + "  Enemy " + (k - 1) : (k + 1) + "  Extra kind";
            loading = false;
            Explain(g);
            UpdateGroupStats(g);
        }

        private void UpdateGroupStats(int g)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder("Enemies in this group:\n");
            foreach (int id in GroupKinds(g))
            {
                string n = EnemyShort(id);
                if (n.Length > 13) n = n.Substring(0, 13);
                sb.Append(n.PadRight(13) + "\n  " + Stats(id) + "\n");
            }
            groupStats.Text = sb.ToString();
            int sp = enc.GetCaveSprite(g);
            string like = "";
            for (int o = 0; o < EncounterData.GroupCount; o++)
                if (o != g && enc.GetCaveSprite(o) == sp)
                {
                    List<int> k = GroupKinds(o);
                    if (k.Count > 0) { like = "same look as G" + o.ToString("D3") + " (" + EnemyShort(k[0]) + ")"; break; }
                }
            groupSpriteNote.Text = like.Length > 0 ? like : "(no other group uses this look)";
        }

        private void GroupSpriteChanged()
        {
            if (loading || enc == null || currentGroup < 0) return;
            enc.SetCaveSprite(currentGroup, (int)groupSprite.Value);
            UpdateGroupStats(currentGroup);
        }

        private void Explain(int g)
        {
            if (enc.IsFixedLineup(g))
            {
                groupExplain.Text = "Fixed line-up: because slot 1 is empty and slot 2 is set, slots 3-8 are the exact enemies " +
                    "of the fight and slot 2 is a raw number (it's stored where the game keeps its item-drop bits; " +
                    "value " + enc.GetGroupSlot(g, 1).ToString("X2") + " here), not an enemy. Set slot 2 to (none) to make it a normal group.";
            }
            else
            {
                groupExplain.Text = "Slot 1 is always in the fight (or slot 2 instead, 50/50, when slot 2 is set). Up to two more kinds " +
                    "are picked at random from slots 3-8 and the leader that wasn't chosen. Then the game adds enemies of " +
                    "these kinds at random until their total size reaches its limit, so big enemies come in smaller numbers. " +
                    "An empty slot 1 with a set slot 2 makes this a fixed line-up.";
            }
            List<string> use = enc.GroupUsage(g);
            groupUsage.Text = "Used by: " + (use.Count == 0 ? "nothing found (no zone, sea, map or cave floor uses it)" : string.Join(";  ", use.ToArray())) +
                "\nChanging a group changes every place that uses it.";
        }

        private void GroupSlotChanged(int k)
        {
            if (loading || enc == null || currentGroup < 0 || groupSlot[k].SelectedIndex < 0) return;
            enc.SetGroupSlot(currentGroup, k, groupSlot[k].SelectedIndex);
            bool was = loading; loading = true;
            listGroups.Items[currentGroup] = GroupLabel(currentGroup);
            loading = was;
            ShowGroup(currentGroup);   // labels change with the mode
            RefreshGroupLists();
        }

        // ---------------- world ----------------

        private void ShowZone(int z)
        {
            if (enc == null || z < 1) return;
            loading = true;
            currentZone = z;
            for (int k = 0; k < EncounterData.ZoneSlots; k++)
            {
                zoneGroup[k].SelectedIndex = enc.GetZoneGroup(z, k);
                UiHelpers.SetValueClamped(zoneWeight[k], enc.GetZoneWeight(z, k));
                ChangeHighlighter.Set(zoneGroup[k], RefByte(enc.ZoneOffset(z, k)));
                ChangeHighlighter.Set(zoneWeight[k], RefByte(enc.ZoneOffset(z, k) + 1));
            }
            loading = false;
            UpdatePercents();
            DrawZoneMap();
        }

        private void UpdatePercents()
        {
            int total = 0;
            for (int k = 0; k < EncounterData.ZoneSlots; k++) total += (int)zoneWeight[k].Value;
            for (int k = 0; k < EncounterData.ZoneSlots; k++)
                zonePct[k].Text = total == 0 ? "" : ((int)zoneWeight[k].Value * 100.0 / total).ToString("0.#") + "%";
            // Typical enemy here: kinds of each group averaged, groups weighted by their chance.
            int lo = int.MaxValue, hi = 0;
            double exp = 0, gold = 0, wsum = 0;
            for (int k = 0; k < EncounterData.ZoneSlots; k++)
            {
                int w = (int)zoneWeight[k].Value, g = zoneGroup[k].SelectedIndex;
                if (w == 0 || g < 0 || g >= EncounterData.GroupCount) continue;
                List<int> kinds = GroupKinds(g);
                if (kinds.Count == 0) continue;
                double e = 0, gd = 0;
                foreach (int id in kinds)
                {
                    int lv = enemies.GetByte(id, EnemyData.OFS_LEVEL);
                    lo = Math.Min(lo, lv); hi = Math.Max(hi, lv);
                    e += enemies.GetWord(id, EnemyData.OFS_EXP); gd += enemies.GetWord(id, EnemyData.OFS_GOLD);
                }
                exp += w * e / kinds.Count; gold += w * gd / kinds.Count; wsum += w;
            }
            zoneSummary.Text = wsum == 0 ? "No fights in this zone." :
                "Enemies here: level " + (lo == hi ? lo.ToString() : lo + "-" + hi) + ", about " + (exp / wsum).ToString("0") +
                " EXP and " + (gold / wsum).ToString("0") + " gold per enemy.";
        }

        private void ZoneChanged(int k)
        {
            if (loading || enc == null || currentZone < 1) return;
            if (zoneGroup[k].SelectedIndex >= 0) enc.SetZoneGroup(currentZone, k, zoneGroup[k].SelectedIndex);
            enc.SetZoneWeight(currentZone, k, (int)zoneWeight[k].Value);
            UpdatePercents();
            bool was = loading; loading = true;
            listZones.Items[currentZone - 1] = ZoneLabel(currentZone);
            loading = was;
        }

        private void DrawZoneMap()
        {
            if (zoneGrid == null) return;
            Bitmap bmp = new Bitmap(64, 64);
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    int z = zoneGrid[y * 64 + x];
                    Color c = ZoneColors[Math.Min(z, ZoneColors.Length - 1)];
                    if (z != currentZone) c = Color.FromArgb((c.R + 2 * 70) / 3, (c.G + 2 * 70) / 3, (c.B + 2 * 70) / 3);
                    else c = Color.FromArgb(255, 255, 255);
                    bmp.SetPixel(x, y, c);
                }
            Image old = zoneMap.Image;
            zoneMap.Image = bmp;
            if (old != null) old.Dispose();
        }

        private void zoneMap_MouseClick(object sender, MouseEventArgs e)
        {
            if (zoneGrid == null) return;
            int x = e.X * 64 / zoneMap.ClientSize.Width, y = e.Y * 64 / zoneMap.ClientSize.Height;
            if (x < 0 || y < 0 || x >= 64 || y >= 64) return;
            int z = zoneGrid[y * 64 + x];
            if (z >= 1 && z < EncounterData.ZoneCount) listZones.SelectedIndex = z - 1;
        }

        // ---------------- sea ----------------

        private void ShowSea()
        {
            if (enc == null) return;
            loading = true;
            for (int i = 0; i < EncounterData.SeaCount; i++)
            {
                seaGroup[i].SelectedIndex = enc.GetSea(i);
                ChangeHighlighter.Set(seaGroup[i], RefByte(enc.Sea + i));
            }
            loading = false;
        }

        private void SeaChanged(int i)
        {
            if (loading || enc == null || seaGroup[i].SelectedIndex < 0) return;
            enc.SetSea(i, seaGroup[i].SelectedIndex);
        }

        // ---------------- battles ----------------

        private void ShowBattle(int b)
        {
            if (enc == null || b < 0) return;
            loading = true;
            currentBattle = b;
            int rec = enc.BattleOffset(b);
            for (int k = 0; k < 6; k++)
            {
                battleEnemy[k].SelectedIndex = enc.GetBattleEnemy(b, k);
                UiHelpers.SetValueClamped(battleX[k], enc.GetBattlePos(b, k, 0));
                UiHelpers.SetValueClamped(battleY[k], enc.GetBattlePos(b, k, 1));
                ChangeHighlighter.Set(battleEnemy[k], RefByte(rec + k));
                ChangeHighlighter.Set(battleX[k], RefByte(rec + 6 + k * 2));
                ChangeHighlighter.Set(battleY[k], RefByte(rec + 7 + k * 2));
            }
            loading = false;
            List<int> share = enc.BattlesSharing(b);
            string s = "";
            if (share.Count > 0)
            {
                List<string> l = new List<string>();
                foreach (int x in share) l.Add(x.ToString("D2"));
                s = "This record is shared with battle " + string.Join(", ", l.ToArray()) + ": edits change both.\n\n";
            }
            battleInfo.Text = s + "X and Y are the enemy's place on the battle screen (pixels). If you put in a bigger " +
                "enemy, move it so it doesn't overlap the others or the edge.\nThe battle's music, background and " +
                "rules (e.g. Pierre and Daniele's, or the Sinistral fights) are in the record too and are kept as they are.";
        }

        private void BattleChanged(int k)
        {
            if (loading || enc == null || currentBattle < 0) return;
            if (battleEnemy[k].SelectedIndex >= 0) enc.SetBattleEnemy(currentBattle, k, battleEnemy[k].SelectedIndex);
            enc.SetBattlePos(currentBattle, k, 0, (int)battleX[k].Value);
            enc.SetBattlePos(currentBattle, k, 1, (int)battleY[k].Value);
            bool was = loading; loading = true;
            listBattles.Items[currentBattle] = BattleLabel(currentBattle);
            foreach (int x in enc.BattlesSharing(currentBattle)) listBattles.Items[x] = BattleLabel(x);
            loading = was;
        }

        // ---------------- dungeon maps ----------------

        private string MapLabel(int m)
        {
            List<string> l = new List<string>();
            for (int k = 0; k < enc.MapListLength(m); k++)
            {
                int g = enc.GetMapEntry(m, k);
                if (g >= EncounterData.GroupCount) continue;
                foreach (int id in GroupKinds(g)) { string n = EnemyShort(id); if (!l.Contains(n)) l.Add(n); }
            }
            string t = l.Count == 0 ? "(no monsters)" : string.Join(", ", l.ToArray());
            if (t.Length > 60) t = t.Substring(0, 57) + "...";
            return "Map " + MapNames.Label(m) + ":  " + t;
        }

        private void ShowMap(int m)
        {
            if (enc == null || m < 0) return;
            loading = true;
            currentMap = m;
            mapRows.SuspendLayout();
            UiHelpers.ClearAndDispose(mapRows);
            mapCombos.Clear();
            int n = enc.MapListLength(m);
            for (int k = 0; k < n; k++)
            {
                mapRows.Controls.Add(UiHelpers.MakeLabel("Monster " + k, 3, 6 + k * 30));
                ComboBox c = UiHelpers.MakeDropDown(90, 3 + k * 30, 300);
                FillGroupCombo(c);
                c.SelectedIndex = enc.GetMapEntry(m, k);
                ChangeHighlighter.Set(c, RefByte(enc.MapListStart(m) + k));
                int slot = k;
                c.SelectedIndexChanged += delegate { MapChanged(slot); };
                mapRows.Controls.Add(c);
                mapCombos.Add(c);
            }
            mapRows.ResumeLayout();
            List<int> share = enc.MapsSharing(m);
            string s = "Each visible monster on this map starts a fight with the group chosen here (monsters are numbered " +
                "by the map). The monster's look on the map comes from the map itself and doesn't change.";
            if (n == 0) s = "This map has no monster list.";
            if (share.Count > 0)
            {
                List<string> l = new List<string>();
                foreach (int x in share) l.Add(x.ToString("X2"));
                s += "\nShared with map " + string.Join(", ", l.ToArray()) + ": edits change those too.";
            }
            mapNote.Text = s;
            mapName.Text = MapNames.Get(m);
            loading = false;
        }

        private void SaveMapName()
        {
            if (currentMap < 0) return;
            if (!MapNames.Set(currentMap, mapName.Text))
                MessageBox.Show("Couldn't write " + MapNames.FilePath + ".", "Map names", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            bool was = loading; loading = true;
            listMaps.Items[currentMap] = MapLabel(currentMap);
            loading = was;
        }

        private void MapChanged(int k)
        {
            if (loading || enc == null || currentMap < 0 || k >= mapCombos.Count || mapCombos[k].SelectedIndex < 0) return;
            enc.SetMapEntry(currentMap, k, mapCombos[k].SelectedIndex);
            bool was = loading; loading = true;
            listMaps.Items[currentMap] = MapLabel(currentMap);
            foreach (int x in enc.MapsSharing(currentMap)) listMaps.Items[x] = MapLabel(x);
            loading = was;
        }

        // ---------------- Ancient Cave ----------------

        private string FloorLabel(int f)
        {
            List<string> l = new List<string>();
            for (int k = 0; k < 6; k++)
            {
                int i = EncounterData.CaveFirstEntry(f) + k;
                if (i >= EncounterData.CaveEntries) continue;
                int g = enc.GetCave(i);
                if (g >= EncounterData.GroupCount) continue;
                foreach (int id in GroupKinds(g)) { string n = EnemyShort(id); if (!l.Contains(n)) l.Add(n); }
            }
            string t = "B" + f + ":  " + string.Join(", ", l.ToArray());
            return t.Length > 78 ? t.Substring(0, 75) + "..." : t;
        }

        private void RefreshFloors()
        {
            bool was = loading; loading = true;
            if (listFloors.Items.Count != 99)
            {
                listFloors.Items.Clear();
                for (int f = 1; f <= 99; f++) listFloors.Items.Add(FloorLabel(f));
            }
            else for (int f = 1; f <= 99; f++)
                {
                    string n = FloorLabel(f);
                    if ((string)listFloors.Items[f - 1] != n) listFloors.Items[f - 1] = n;
                }
            loading = was;
        }

        private void ShowCaveFloor()
        {
            if (enc == null) return;
            loading = true;
            int f = (int)caveFloor.Value;
            for (int k = 0; k < 6; k++)
            {
                int i = EncounterData.CaveFirstEntry(f) + k;
                bool ok = i < EncounterData.CaveEntries;
                caveGroup[k].Enabled = ok;
                caveEntryLabel[k].Text = "Entry " + (i + 1);
                if (!ok) { caveShare[k].Text = ""; continue; }
                caveGroup[k].SelectedIndex = enc.GetCave(i);
                ChangeHighlighter.Set(caveGroup[k], RefByte(enc.Cave + i));
                int lo, hi;
                EncounterData.FloorsOfEntry(i, out lo, out hi);
                caveShare[k].Text = lo == hi ? "floor " + lo + " only" : "floors " + lo + "-" + hi;
            }
            if (listFloors.SelectedIndex != f - 1) listFloors.SelectedIndex = f - 1;
            loading = false;
        }

        private void CaveChanged(int k)
        {
            if (loading || enc == null || caveGroup[k].SelectedIndex < 0) return;
            int i = EncounterData.CaveFirstEntry((int)caveFloor.Value) + k;
            if (i >= EncounterData.CaveEntries) return;
            enc.SetCave(i, caveGroup[k].SelectedIndex);
            RefreshFloors();
        }

        // ---------------- find an enemy ----------------

        /// <summary>Shows where an enemy appears (used by the Enemy Overview's "Where" button).</summary>
        public void FindEnemy(int id)
        {
            if (enc == null || id < 0 || id >= findEnemy.Items.Count) return;
            comboSection.SelectedIndex = 6;
            findEnemy.SelectedIndex = id;
            ShowFind();
        }

        private void ShowFind()
        {
            if (enc == null || findEnemy.SelectedIndex < 0) return;
            int id = findEnemy.SelectedIndex;
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append(EnemyShort(id) + ":  " + Stats(id) + "\r\n\r\n");
            List<int> groups = enc.GroupsWithEnemy(id);
            if (groups.Count == 0) sb.Append("Not in any monster group.\r\n");
            foreach (int g in groups)
            {
                List<string> use = enc.GroupUsage(g);
                sb.Append("Group G" + g.ToString("D3") + " (" + GroupSummary(g) + ")\r\n    used by: " +
                    (use.Count == 0 ? "nothing" : string.Join(";  ", use.ToArray())) + "\r\n");
            }
            List<int> battles = enc.BattlesWithEnemy(id);
            sb.Append("\r\n");
            if (battles.Count == 0) sb.Append("Not in any boss or scripted battle.\r\n");
            foreach (int b in battles) sb.Append("Boss / scripted battle " + BattleLabel(b) + "\r\n");
            sb.Append("\r\n(Enemies called in by another enemy's AI or by map events aren't listed.)");
            findResult.Text = sb.ToString();
        }
    }
}
