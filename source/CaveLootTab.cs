using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

using com.lufiaIIEditor.RomInfo;

namespace com.lufiaIIEditor.UI
{
    /// <summary>
    /// "Cave Loot" tab: what Ancient Cave chests contain - the odds of each kind of chest,
    /// the fixed item tables (Iris, rare blue-chest table, potion and common tables),
    /// Providence, and the price rule for equipment, with a per-floor preview.
    /// Everything is edited in place (tables keep their size).
    /// </summary>
    class CaveLootTab : TabPage
    {
        private CaveLootData loot;
        private ItemData items;
        private bool loading = false;

        private NumericUpDown[] numShare = new NumericUpDown[CaveLootData.Sources];
        private Label[] labelShare = new Label[CaveLootData.Sources];
        private Label labelRest;
        private NumericUpDown numIris, numProvFloor, numPriceStep, numNoLimit;
        private Label labelIris, labelProv, labelPrice;
        private CheckBox checkBugFix;
        private ComboBox comboProvItem;

        private ComboBox comboTable;
        private Label labelTableNote;
        private ListBox listEntries;
        private ComboBox comboPicker;
        private Button buttonReplace, buttonRestoreEntry;
        private int currentTable = -1;

        private NumericUpDown numFloor;
        private Label labelFloor;
        private ListBox listFloor;

        private static readonly Font ListFont = new Font(FontFamily.GenericMonospace, 8.5f);

        public CaveLootTab()
        {
            this.Text = "Cave Loot";
            this.UseVisualStyleBackColor = true;
            Build();
            this.Enabled = false;
        }

        // ---------------- layout ----------------

        private void Build()
        {
            GroupBox gOdds = UiHelpers.MakeGroup("What a chest holds (each chest rolls 0-255)", 12, 8, 330, 225);
            Controls.Add(gOdds);
            for (int i = 0; i < CaveLootData.Sources; i++)
            {
                int y = 24 + i * 28;
                gOdds.Controls.Add(UiHelpers.MakeLabel(CaveLootData.SourceNames[i], 10, y + 3));
                if (i < 5)
                {
                    numShare[i] = UiHelpers.MakeNumeric(212, y, i == 0 ? 1 : 0, 256);
                    numShare[i].Size = new Size(50, 20);
                    numShare[i].ValueChanged += new EventHandler(ShareChanged);
                    gOdds.Controls.Add(numShare[i]);
                }
                labelShare[i] = UiHelpers.MakeLabel("", 268, y + 3);
                gOdds.Controls.Add(labelShare[i]);
            }
            labelRest = UiHelpers.MakeNote("", 212, 24 + 5 * 28 + 3);
            gOdds.Controls.Add(labelRest);
            gOdds.Controls.Add(UiHelpers.MakeNote("Numbers are chests out of 256. The common table gets whatever is left.", 10, 196));

            GroupBox gSpecial = UiHelpers.MakeGroup("Special items and the price rule", 12, 240, 330, 225);
            Controls.Add(gSpecial);
            gSpecial.Controls.Add(UiHelpers.MakeLabel("Iris chance per floor", 10, 27));
            numIris = UiHelpers.MakeNumeric(150, 24, 0, 255);
            numIris.Size = new Size(50, 20);
            numIris.ValueChanged += new EventHandler(AnyChanged);
            gSpecial.Controls.Add(numIris);
            labelIris = UiHelpers.MakeLabel("", 206, 27);
            gSpecial.Controls.Add(labelIris);

            gSpecial.Controls.Add(UiHelpers.MakeLabel("Providence item", 10, 57));
            comboProvItem = UiHelpers.MakeDropDown(150, 54, 170);
            comboProvItem.SelectedIndexChanged += new EventHandler(AnyChanged);
            gSpecial.Controls.Add(comboProvItem);
            gSpecial.Controls.Add(UiHelpers.MakeLabel("...from floor", 10, 87));
            numProvFloor = UiHelpers.MakeNumeric(150, 84, 1, 255);
            numProvFloor.Size = new Size(50, 20);
            numProvFloor.ValueChanged += new EventHandler(AnyChanged);
            gSpecial.Controls.Add(numProvFloor);
            labelProv = UiHelpers.MakeNote("", 10, 108);
            labelProv.AutoSize = false;
            labelProv.Size = new Size(312, 28);
            gSpecial.Controls.Add(labelProv);

            gSpecial.Controls.Add(UiHelpers.MakeLabel("Price limit per floor", 10, 143));
            numPriceStep = UiHelpers.MakeNumeric(150, 140, 1, 32767);
            numPriceStep.ValueChanged += new EventHandler(AnyChanged);
            gSpecial.Controls.Add(numPriceStep);
            gSpecial.Controls.Add(UiHelpers.MakeLabel("G", 224, 143));
            gSpecial.Controls.Add(UiHelpers.MakeLabel("No price limit from floor", 10, 171));
            numNoLimit = UiHelpers.MakeNumeric(150, 168, 1, 255);
            numNoLimit.Size = new Size(50, 20);
            numNoLimit.ValueChanged += new EventHandler(AnyChanged);
            gSpecial.Controls.Add(numNoLimit);
            labelPrice = UiHelpers.MakeNote("", 10, 194);
            labelPrice.AutoSize = false;
            labelPrice.Size = new Size(312, 28);
            gSpecial.Controls.Add(labelPrice);

            // ---- tables ----
            GroupBox gTables = UiHelpers.Anchor(this, UiHelpers.MakeGroup("Chest tables", 352, 8, 300, 457), AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left);
            Controls.Add(gTables);
            comboTable = UiHelpers.MakeDropDown(10, 20, 280);
            comboTable.SelectedIndexChanged += new EventHandler(comboTable_SelectedIndexChanged);
            gTables.Controls.Add(comboTable);
            labelTableNote = UiHelpers.MakeNote("", 10, 46);
            labelTableNote.AutoSize = false;
            labelTableNote.Size = new Size(282, 30);
            gTables.Controls.Add(labelTableNote);

            listEntries = UiHelpers.Anchor(this, new ListBox(), AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right);
            listEntries.Location = new Point(10, 78);
            listEntries.Size = new Size(280, 300);
            listEntries.MultiColumn = true;      // all 41 blue chest items fit in two columns
            listEntries.ColumnWidth = 138;
            listEntries.IntegralHeight = false;
            listEntries.DrawMode = DrawMode.OwnerDrawFixed;
            listEntries.ItemHeight = 14;
            listEntries.DrawItem += new DrawItemEventHandler(listEntries_DrawItem);
            listEntries.SelectedIndexChanged += new EventHandler(listEntries_SelectedIndexChanged);
            gTables.Controls.Add(listEntries);

            comboPicker = UiHelpers.Anchor(this, UiHelpers.MakeDropDown(10, 385, 280), AnchorStyles.Bottom | AnchorStyles.Left);
            gTables.Controls.Add(comboPicker);
            buttonReplace = UiHelpers.Anchor(this, new Button(), AnchorStyles.Bottom | AnchorStyles.Left);
            buttonReplace.Text = "Replace";
            buttonReplace.Location = new Point(10, 411);
            buttonReplace.Size = new Size(80, 24);
            buttonReplace.Click += new EventHandler(buttonReplace_Click);
            gTables.Controls.Add(buttonReplace);
            buttonRestoreEntry = UiHelpers.Anchor(this, new Button(), AnchorStyles.Bottom | AnchorStyles.Left);
            buttonRestoreEntry.Text = "Restore original";
            buttonRestoreEntry.Location = new Point(96, 411);
            buttonRestoreEntry.Size = new Size(110, 24);
            buttonRestoreEntry.Click += new EventHandler(buttonRestoreEntry_Click);
            gTables.Controls.Add(buttonRestoreEntry);
            gTables.Controls.Add(UiHelpers.Anchor(this, UiHelpers.MakeNote("Entries are equally likely: list an item twice for double odds.", 10, 439), AnchorStyles.Bottom | AnchorStyles.Left));

            // ---- floor preview ----
            GroupBox gFloor = UiHelpers.Anchor(this, UiHelpers.MakeGroup("Equipment that can appear on a floor", 662, 8, 315, 457), AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right);
            Controls.Add(gFloor);
            gFloor.Controls.Add(UiHelpers.MakeLabel("Floor", 10, 23));
            numFloor = UiHelpers.MakeNumeric(50, 20, 1, 99);
            numFloor.Size = new Size(50, 20);
            numFloor.Value = 10;
            numFloor.ValueChanged += new EventHandler(AnyChanged);
            gFloor.Controls.Add(numFloor);
            checkBugFix = new CheckBox();
            checkBugFix.Text = "Fix the floor 10+ equipment bug";
            checkBugFix.Location = new Point(112, 21);
            checkBugFix.AutoSize = true;
            checkBugFix.CheckedChanged += new EventHandler(checkBugFix_CheckedChanged);
            gFloor.Controls.Add(checkBugFix);
            labelFloor = UiHelpers.MakeNote("", 10, 46);
            labelFloor.AutoSize = false;
            labelFloor.Size = new Size(297, 42);
            gFloor.Controls.Add(labelFloor);
            listFloor = UiHelpers.Anchor(this, new ListBox(), AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right);
            listFloor.Location = new Point(10, 90);
            listFloor.Size = new Size(295, 330);
            listFloor.IntegralHeight = false;
            listFloor.Font = ListFont;
            gFloor.Controls.Add(listFloor);
            gFloor.Controls.Add(UiHelpers.Anchor(this, UiHelpers.MakeNote("Weapons and other equipment come from these lists.\nPrices are set in the Equipment tab.", 10, 425), AnchorStyles.Bottom | AnchorStyles.Left));

            Controls.Add(UiHelpers.Anchor(this, UiHelpers.MakeNote(
                "Blue chests come only from the Blue chests table. Iris treasures and Providence are in red chests, but Iris treasures are kept when you leave the cave.\n" +
                "A spell chest teaches the spell to the whole party. Changes here are written straight into the ROM data (saved with File > Save).", 12, 470), AnchorStyles.Bottom | AnchorStyles.Left));
        }

        // ---------------- load / commit ----------------

        public void LoadRom(CaveLootData data, ItemData itemData)
        {
            loot = data;
            items = itemData;
            loading = true;
            comboTable.Items.Clear();
            foreach (CaveLootData.Table t in loot.Tables) comboTable.Items.Add(t.Name + "  (" + t.Count + ")");
            comboPicker.Items.Clear();
            comboProvItem.Items.Clear();
            for (int id = 0; id < ItemData.Count; id++)
            {
                string n = ItemLabel(id);
                comboPicker.Items.Add(n);
                comboProvItem.Items.Add(n);
            }
            loading = false;
            this.Enabled = true;
            ShowAll();
            if (comboTable.SelectedIndex < 0) comboTable.SelectedIndex = 0; else ShowTable(comboTable.SelectedIndex);
        }

        public void Disable()
        {
            loot = null; items = null; currentTable = -1;
            this.Enabled = false;
        }

        /// <summary>Everything is written to the ROM data as it's edited; this re-reads names.</summary>
        public void CommitCurrent() { }

        public void RefreshView()
        {
            if (loot == null) return;
            loading = true;
            for (int id = 0; id < ItemData.Count; id++)
            {
                UiHelpers.RenameItem(comboPicker, id, ItemLabel(id));
                UiHelpers.RenameItem(comboProvItem, id, ItemLabel(id));
            }
            loading = false;
            ShowAll();
            if (currentTable >= 0) ShowTable(currentTable);
        }

        private string ItemLabel(int id)
        {
            if (id == 0) return "(none)  (000)";
            return items.GetName(id).Trim() + "  (" + id.ToString("X3") + ")";
        }

        private static object RefByte(int o) { return Reference.InRange(Reference.Shift(o), 1) ? (object)(int)Reference.Data[Reference.Shift(o)] : null; }
        private static object RefWord9(int o)
        {
            int r = Reference.Shift(o);
            return Reference.InRange(r, 2) ? (object)((Reference.Data[r] | (Reference.Data[r + 1] << 8)) & 0x1FF) : null;
        }

        private void ShowAll()
        {
            loading = true;
            int[] s = loot.GetShares();
            for (int i = 0; i < 5; i++)
            {
                UiHelpers.SetValueClamped(numShare[i], s[i]);
                // Original share from the reference ROM's thresholds.
                object a = RefByte(loot.ThresholdOffset(i));
                object b = i == 0 ? (object)256 : RefByte(loot.ThresholdOffset(i - 1));
                ChangeHighlighter.Set(numShare[i], (a == null || b == null) ? null : (object)((int)b - (int)a));
            }
            UiHelpers.SetValueClamped(numIris, loot.IrisChance);
            ChangeHighlighter.Set(numIris, RefByte(loot.IrisChanceOffset));
            comboProvItem.SelectedIndex = loot.ProvidenceItem;
            ChangeHighlighter.Set(comboProvItem, RefWord9(loot.ProvidenceItemOffset));
            UiHelpers.SetValueClamped(numProvFloor, loot.ProvidenceFloor);
            ChangeHighlighter.Set(numProvFloor, RefByte(loot.ProvidenceFloorOffset));
            UiHelpers.SetValueClamped(numPriceStep, loot.PriceStep);
            object lo = RefByte(loot.PriceLoOffset), hi = RefByte(loot.PriceHiOffset);
            ChangeHighlighter.Set(numPriceStep, (lo == null || hi == null) ? null : (object)((int)lo | ((int)hi << 8)));
            int state = loot.BugFixState;
            checkBugFix.Enabled = state >= 0;
            checkBugFix.Checked = state == 1;
            object origState = Reference.Available ? (object)(new CaveLootData(Reference.Data, Reference.HasHeader).BugFixState == 1) : null;
            ChangeHighlighter.Set(checkBugFix, origState);
            UiHelpers.SetValueClamped(numNoLimit, loot.NoLimitFloor);
            ChangeHighlighter.Set(numNoLimit, RefByte(loot.NoLimitFloorOffset));
            loading = false;
            UpdateLabels();
        }

        // ---------------- odds and specials ----------------

        private void ShareChanged(object sender, EventArgs e)
        {
            if (loading || loot == null) return;
            int[] s = new int[CaveLootData.Sources];
            int used = 0;
            for (int i = 0; i < 5; i++)
            {
                s[i] = (int)numShare[i].Value;
                // Don't let the five shares add up past 256.
                if (used + s[i] > 256)
                {
                    s[i] = 256 - used;
                    loading = true; UiHelpers.SetValueClamped(numShare[i], s[i]); loading = false;
                }
                used += s[i];
            }
            loot.SetShares(s);
            UpdateLabels();
        }

        private void AnyChanged(object sender, EventArgs e)
        {
            if (loading || loot == null) return;
            if (sender == numIris) loot.IrisChance = (int)numIris.Value;
            else if (sender == numProvFloor) loot.ProvidenceFloor = (int)numProvFloor.Value;
            else if (sender == comboProvItem && comboProvItem.SelectedIndex > 0) loot.ProvidenceItem = comboProvItem.SelectedIndex;
            else if (sender == numPriceStep) loot.PriceStep = (int)numPriceStep.Value;
            else if (sender == numNoLimit) loot.NoLimitFloor = (int)numNoLimit.Value;
            UpdateLabels();
        }

        private static string Pct(int n, int of) { return (n * 100.0 / of).ToString("0.0") + "%"; }

        private void UpdateLabels()
        {
            int[] s = loot.GetShares();
            for (int i = 0; i < CaveLootData.Sources; i++) labelShare[i].Text = Pct(s[i], 256);
            labelRest.Text = s[5] + " left";
            labelIris.Text = "/ 256 = " + Pct(loot.IrisChance, 256) + " per floor";
            int pf = loot.ProvidenceFloor;
            labelProv.Text = "On each floor from " + pf + " until found: floor/60 chance (" +
                Pct(Math.Min(pf, 60), 60) + " on floor " + pf + ", certain from floor 60).";
            labelPrice.Text = "Equipment must cost less than " + loot.PriceStep + " x (floor + 1); " +
                "anything goes from floor " + loot.NoLimitFloor + ".";
            UpdateFloor();
        }

        private void UpdateFloor()
        {
            if (items == null) return;
            int floor = (int)numFloor.Value;
            int limit = loot.PriceLimit(floor);
            long raw = (long)loot.PriceStep * (floor + 1);
            List<string> weapons = new List<string>(), others = new List<string>();
            for (int id = 1; id < ItemData.Count; id++)
            {
                if (!CaveLootData.CaveEligible(items, id)) continue;
                int price = items.GetPrice(id);
                if (limit >= 0 && price >= limit) continue;
                string line = items.GetName(id).Trim().PadRight(13) + price.ToString().PadLeft(6) + "G";
                if ((items.GetSlot(id) & 0x01) != 0) weapons.Add(line); else others.Add(line);
            }
            int pw = loot.Pickable(weapons.Count), po = loot.Pickable(others.Count);
            listFloor.BeginUpdate();
            listFloor.Items.Clear();
            AddList("Weapons", weapons, pw);
            AddList("Other equipment", others, po);
            listFloor.EndUpdate();
            string text = limit < 0 ? "No price limit on this floor." : "Price limit on this floor: under " + limit + "G.";
            if (limit >= 0 && raw > 65535)
                text += "  Warning: " + raw + " is past the game's 65535 limit, so it wraps around to " + limit + "G.";
            if (pw < weapons.Count || po < others.Count)
                text += loot.BugFixState == 1
                    ? "  Lists stop at 255 items; the rest (x) can't drop."
                    : "  Bug: 128+ items in a list - only the first few can drop; the rest (x) never do.";
            labelFloor.Text = text;
        }

        private void AddList(string title, List<string> lines, int pickable)
        {
            listFloor.Items.Add("-- " + title + " (" + lines.Count + (pickable < lines.Count ? ", only " + pickable + " can drop" : "") + ") --");
            for (int k = 0; k < lines.Count; k++) listFloor.Items.Add((k < pickable ? "  " : "x ") + lines[k]);
        }

        private void checkBugFix_CheckedChanged(object sender, EventArgs e)
        {
            if (loading || loot == null || loot.BugFixState < 0) return;
            loot.SetBugFix(checkBugFix.Checked);
            UpdateFloor();
        }

        // ---------------- tables ----------------

        private void comboTable_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (loading || loot == null || comboTable.SelectedIndex < 0) return;
            ShowTable(comboTable.SelectedIndex);
        }

        private void ShowTable(int t)
        {
            currentTable = t;
            CaveLootData.Table table = loot.Tables[t];
            labelTableNote.Text = table.Note;
            int keep = listEntries.SelectedIndex;
            loading = true;
            listEntries.BeginUpdate();
            listEntries.Items.Clear();
            for (int k = 0; k < table.Count; k++)
                listEntries.Items.Add((k + 1).ToString().PadLeft(2) + "  " + items.GetName(loot.GetEntry(t, k)).Trim());
            listEntries.EndUpdate();
            listEntries.SelectedIndex = (keep >= 0 && keep < table.Count) ? keep : 0;
            loading = false;
            SyncPicker();
        }

        private int OriginalEntry(int t, int k)
        {
            object o = RefWord9(loot.EntryOffset(t, k));
            return o == null ? -1 : (int)o;
        }

        private void listEntries_SelectedIndexChanged(object sender, EventArgs e) { if (!loading) SyncPicker(); }

        private void SyncPicker()
        {
            int k = listEntries.SelectedIndex;
            if (currentTable < 0 || k < 0) return;
            comboPicker.SelectedIndex = loot.GetEntry(currentTable, k);
            int orig = OriginalEntry(currentTable, k);
            buttonRestoreEntry.Enabled = orig >= 0 && orig != loot.GetEntry(currentTable, k);
            buttonRestoreEntry.Text = orig >= 0 ? "Restore (" + items.GetName(orig).Trim() + ")" : "Restore original";
            buttonRestoreEntry.Width = 180;
        }

        private void buttonReplace_Click(object sender, EventArgs e)
        {
            int k = listEntries.SelectedIndex, id = comboPicker.SelectedIndex;
            if (currentTable < 0 || k < 0 || id <= 0) return;   // 0 isn't an item
            loot.SetEntry(currentTable, k, id);
            ShowTable(currentTable);
        }

        private void buttonRestoreEntry_Click(object sender, EventArgs e)
        {
            int k = listEntries.SelectedIndex;
            if (currentTable < 0 || k < 0) return;
            int orig = OriginalEntry(currentTable, k);
            if (orig <= 0) return;
            loot.SetEntry(currentTable, k, orig);
            ShowTable(currentTable);
        }

        private void listEntries_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (loot == null || e.Index < 0 || e.Index >= listEntries.Items.Count) return;
            bool selected = (e.State & DrawItemState.Selected) != 0;
            int orig = currentTable >= 0 ? OriginalEntry(currentTable, e.Index) : -1;
            bool changed = !selected && orig >= 0 && orig != loot.GetEntry(currentTable, e.Index);
            Color back = selected ? SystemColors.Highlight : changed ? ChangeHighlighter.ChangedColor : listEntries.BackColor;
            Color fore = selected ? SystemColors.HighlightText : listEntries.ForeColor;
            using (SolidBrush b = new SolidBrush(back)) e.Graphics.FillRectangle(b, e.Bounds);
            TextRenderer.DrawText(e.Graphics, (string)listEntries.Items[e.Index], ListFont, e.Bounds, fore,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }
    }
}
