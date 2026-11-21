using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

using com.lufiaIIEditor.RomInfo;

namespace com.lufiaIIEditor.UI
{
    /// <summary>
    /// "Chests" tab: what every treasure chest on the normal maps holds (ChestData).
    /// Pick a map on the left, a chest on the right, then an item. Edits go straight into the ROM
    /// data. The Ancient Cave's chests are random and live on the Cave Loot tab.
    /// </summary>
    class ChestsTab : TabPage
    {
        private ChestData chests;
        private ItemData items;
        private bool loading = false;

        private TextBox textFind;
        private ListBox listMaps;
        private TextBox textMapName;
        private Label labelMap, labelShared;
        private ListBox listChests;
        private ComboBox comboItem;
        private Button buttonReplace, buttonRestore, buttonRestoreMap;
        private readonly List<int> shownMaps = new List<int>();
        private int currentMap = -1;

        private static readonly Font ListFont = new Font(FontFamily.GenericMonospace, 8.5f);

        public ChestsTab()
        {
            this.Text = "Chests";
            this.UseVisualStyleBackColor = true;
            Build();
            this.Enabled = false;
        }

        // ---------------- layout ----------------

        private void Build()
        {
            GroupBox gMaps = UiHelpers.Anchor(this, UiHelpers.MakeGroup("Maps with treasure chests", 12, 8, 330, 498),
                AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left);
            Controls.Add(gMaps);
            gMaps.Controls.Add(UiHelpers.MakeLabel("Find item:", 10, 24));
            textFind = new TextBox();
            textFind.Location = new Point(76, 21);
            textFind.Size = new Size(240, 20);
            textFind.TextChanged += delegate { FillMaps(); SelectMap(currentMap); };
            gMaps.Controls.Add(textFind);
            listMaps = UiHelpers.Anchor(this, new ListBox(), AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left);
            listMaps.Location = new Point(10, 50);
            listMaps.Size = new Size(306, 415);
            listMaps.IntegralHeight = false;
            listMaps.Font = ListFont;
            listMaps.SelectedIndexChanged += delegate { if (!loading) ShowMap(); };
            gMaps.Controls.Add(listMaps);
            gMaps.Controls.Add(UiHelpers.Anchor(this, UiHelpers.MakeNote(
                "Ancient Cave chests are random: see the Cave Loot tab.", 10, 472), AnchorStyles.Bottom | AnchorStyles.Left));

            GroupBox gChests = UiHelpers.Anchor(this, UiHelpers.MakeGroup("Chests on this map", 352, 8, 420, 498),
                AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left);
            Controls.Add(gChests);
            labelMap = UiHelpers.MakeLabel("", 10, 24);
            gChests.Controls.Add(labelMap);
            gChests.Controls.Add(UiHelpers.MakeLabel("Map name:", 10, 50));
            textMapName = new TextBox();
            textMapName.Location = new Point(76, 47);
            textMapName.Size = new Size(240, 20);
            textMapName.KeyDown += delegate(object o, KeyEventArgs k)
            {
                if (k.KeyCode == Keys.Enter) { SaveMapName(); k.SuppressKeyPress = true; }
            };
            gChests.Controls.Add(textMapName);
            Button saveName = new Button();
            saveName.Text = "Save name";
            saveName.Location = new Point(322, 45);
            saveName.Size = new Size(85, 24);
            saveName.Click += delegate { SaveMapName(); };
            gChests.Controls.Add(saveName);
            labelShared = UiHelpers.MakeNote("", 10, 74);
            gChests.Controls.Add(labelShared);

            listChests = UiHelpers.Anchor(this, new ListBox(), AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left);
            listChests.Location = new Point(10, 94);
            listChests.Size = new Size(398, 290);
            listChests.IntegralHeight = false;
            listChests.DrawMode = DrawMode.OwnerDrawFixed;
            listChests.ItemHeight = 16;
            listChests.DrawItem += new DrawItemEventHandler(listChests_DrawItem);
            listChests.SelectedIndexChanged += delegate { if (!loading) SyncPicker(); };
            gChests.Controls.Add(listChests);

            comboItem = UiHelpers.Anchor(this, UiHelpers.MakeDropDown(10, 392, 300), AnchorStyles.Bottom | AnchorStyles.Left);
            gChests.Controls.Add(comboItem);
            buttonReplace = UiHelpers.Anchor(this, new Button(), AnchorStyles.Bottom | AnchorStyles.Left);
            buttonReplace.Text = "Replace";
            buttonReplace.Location = new Point(10, 420);
            buttonReplace.Size = new Size(80, 24);
            buttonReplace.Click += new EventHandler(buttonReplace_Click);
            gChests.Controls.Add(buttonReplace);
            buttonRestore = UiHelpers.Anchor(this, new Button(), AnchorStyles.Bottom | AnchorStyles.Left);
            buttonRestore.Text = "Restore original";
            buttonRestore.Location = new Point(96, 420);
            buttonRestore.Size = new Size(180, 24);
            buttonRestore.Click += new EventHandler(buttonRestore_Click);
            gChests.Controls.Add(buttonRestore);
            buttonRestoreMap = UiHelpers.Anchor(this, new Button(), AnchorStyles.Bottom | AnchorStyles.Left);
            buttonRestoreMap.Text = "Restore this map";
            buttonRestoreMap.Location = new Point(282, 420);
            buttonRestoreMap.Size = new Size(126, 24);
            buttonRestoreMap.Click += new EventHandler(buttonRestoreMap_Click);
            gChests.Controls.Add(buttonRestoreMap);
            gChests.Controls.Add(UiHelpers.Anchor(this, UiHelpers.MakeNote(
                "Each chest can hold any item, but chests can't be added or removed\n" +
                "(the table is full, and the chests themselves are placed in the map data).",
                10, 452), AnchorStyles.Bottom | AnchorStyles.Left));
        }

        // ---------------- load ----------------

        public void LoadRom(ChestData data, ItemData itemData)
        {
            chests = data;
            items = itemData;
            loading = true;
            comboItem.Items.Clear();
            for (int id = 0; id < ItemData.Count; id++) comboItem.Items.Add(ItemLabel(id));
            loading = false;
            this.Enabled = true;
            int keep = currentMap;
            FillMaps();
            SelectMap(keep);
        }

        public void Disable()
        {
            chests = null; items = null; currentMap = -1;
            this.Enabled = false;
        }

        /// <summary>Every edit is written at once.</summary>
        public void CommitCurrent() { }

        public void RefreshView()
        {
            if (chests == null) return;
            loading = true;
            for (int id = 0; id < ItemData.Count && id < comboItem.Items.Count; id++)
                UiHelpers.RenameItem(comboItem, id, ItemLabel(id));
            loading = false;
            int keep = currentMap;
            FillMaps();
            SelectMap(keep);
        }

        private string ItemLabel(int id)
        {
            if (id == 0) return "(nothing)  (000)";
            return items.GetName(id).Trim() + "  (" + id.ToString("X3") + ")";
        }

        private string ItemName(int id)
        {
            if (id < 0) return "?";
            if (id == 0) return "(nothing)";
            if (id >= ItemData.Count) return "#" + id.ToString("X3");
            return items.GetName(id).Trim();
        }

        // ---------------- maps ----------------

        private bool Matches(int m, string find)
        {
            if (find.Length == 0) return true;
            foreach (ChestData.Chest c in chests.ForMap(m))
                if (ItemName(chests.GetItem(c)).IndexOf(find, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private bool MapChanged(int m)
        {
            foreach (ChestData.Chest c in chests.ForMap(m))
            {
                int o = Original(c);
                if (o >= 0 && o != chests.GetItem(c)) return true;
            }
            return false;
        }

        private void FillMaps()
        {
            if (chests == null) return;
            string find = textFind.Text.Trim();
            bool was = loading;
            loading = true;
            listMaps.BeginUpdate();
            listMaps.Items.Clear();
            shownMaps.Clear();
            foreach (int m in chests.MapsWithChests())
            {
                if (!Matches(m, find)) continue;
                int n = chests.ForMap(m).Count;
                string label = MapNames.Label(m);
                listMaps.Items.Add(label + "  (" + n + (n == 1 ? " chest" : " chests") + ")" + (MapChanged(m) ? "  [changed]" : ""));
                shownMaps.Add(m);
            }
            listMaps.EndUpdate();
            loading = was;
        }

        private void SelectMap(int m)
        {
            int i = shownMaps.IndexOf(m);
            if (i < 0 && shownMaps.Count > 0) i = 0;
            if (i >= 0) listMaps.SelectedIndex = i;   // fires ShowMap
            else { currentMap = -1; listChests.Items.Clear(); }
        }

        private void ShowMap()
        {
            int i = listMaps.SelectedIndex;
            if (chests == null || i < 0 || i >= shownMaps.Count) return;
            int keepChest = (shownMaps[i] == currentMap) ? listChests.SelectedIndex : 0;
            currentMap = shownMaps[i];
            labelMap.Text = "Map " + currentMap.ToString("X2");
            textMapName.Text = MapNames.Get(currentMap);
            List<int> shared = chests.SharedWith(currentMap);
            if (shared.Count == 0) labelShared.Text = "";
            else
            {
                List<string> s = new List<string>();
                foreach (int x in shared) s.Add(x.ToString("X2"));
                labelShared.Text = "This list is shared with map " + string.Join(", ", s.ToArray()) + ": edits change those too.";
            }
            bool was = loading;
            loading = true;
            listChests.Items.Clear();
            List<ChestData.Chest> list = chests.ForMap(currentMap);
            for (int k = 0; k < list.Count; k++)
            {
                ChestData.Chest c = list[k];
                listChests.Items.Add((k + 1).ToString().PadLeft(2) + "  " + ItemName(chests.GetItem(c)).PadRight(14) +
                    "  object " + c.Object.ToString("X2") + ", flag " + c.Flag.ToString("X3"));
            }
            if (list.Count > 0) listChests.SelectedIndex = Math.Max(0, Math.Min(keepChest, list.Count - 1));
            loading = was;
            SyncPicker();
            UpdateRestoreMap();
        }

        private void SaveMapName()
        {
            if (currentMap < 0) return;
            if (!MapNames.Set(currentMap, textMapName.Text))
                MessageBox.Show("Couldn't write " + MapNames.FilePath + ".", "Map names", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            int keep = currentMap;
            FillMaps();
            SelectMap(keep);
        }

        // ---------------- chests ----------------

        private ChestData.Chest Current()
        {
            if (chests == null || currentMap < 0) return null;
            List<ChestData.Chest> list = chests.ForMap(currentMap);
            int k = listChests.SelectedIndex;
            return k >= 0 && k < list.Count ? list[k] : null;
        }

        /// <summary>The item in this chest in the reference ROM, or -1 if there's nothing to compare with.</summary>
        private static int Original(ChestData.Chest c)
        {
            if (!Reference.Available) return -1;
            int r = Reference.Shift(c.Offset);
            if (!Reference.InRange(r, 3)) return -1;
            return ChestData.ItemIn(Reference.Data, r);
        }

        private void SyncPicker()
        {
            ChestData.Chest c = Current();
            buttonReplace.Enabled = c != null;
            if (c == null) { buttonRestore.Enabled = false; return; }
            int item = chests.GetItem(c);
            bool was = loading;
            loading = true;
            if (item < comboItem.Items.Count) comboItem.SelectedIndex = item;
            loading = was;
            int orig = Original(c);
            buttonRestore.Enabled = orig >= 0 && orig != item;
            buttonRestore.Text = orig >= 0 ? "Restore (" + ItemName(orig) + ")" : "Restore original";
        }

        private void UpdateRestoreMap()
        {
            buttonRestoreMap.Enabled = currentMap >= 0 && MapChanged(currentMap);
        }

        private void Changed()
        {
            int keepMap = currentMap;
            int keepChest = listChests.SelectedIndex;
            FillMaps();
            SelectMap(keepMap);
            if (keepChest >= 0 && keepChest < listChests.Items.Count) listChests.SelectedIndex = keepChest;
        }

        private void buttonReplace_Click(object sender, EventArgs e)
        {
            ChestData.Chest c = Current();
            int id = comboItem.SelectedIndex;
            if (c == null || id < 0) return;
            if (id == 0 && MessageBox.Show("Item 000 is \"No equip\", not a real item, and the game may not handle\n" +
                    "a chest that holds it. Use it anyway?", "Chests",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            chests.SetItem(c, id);
            Changed();
        }

        private void buttonRestore_Click(object sender, EventArgs e)
        {
            ChestData.Chest c = Current();
            if (c == null) return;
            int orig = Original(c);
            if (orig < 0) return;
            chests.SetItem(c, orig);
            Changed();
        }

        private void buttonRestoreMap_Click(object sender, EventArgs e)
        {
            if (currentMap < 0) return;
            foreach (ChestData.Chest c in chests.ForMap(currentMap))
            {
                int orig = Original(c);
                if (orig >= 0) chests.SetItem(c, orig);
            }
            Changed();
        }

        private void listChests_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (chests == null || currentMap < 0 || e.Index < 0 || e.Index >= listChests.Items.Count) return;
            List<ChestData.Chest> list = chests.ForMap(currentMap);
            bool selected = (e.State & DrawItemState.Selected) != 0;
            bool changed = false;
            if (e.Index < list.Count)
            {
                int orig = Original(list[e.Index]);
                changed = orig >= 0 && orig != chests.GetItem(list[e.Index]);
            }
            Color back = selected ? SystemColors.Highlight : changed ? ChangeHighlighter.ChangedColor : listChests.BackColor;
            Color fore = selected ? SystemColors.HighlightText : listChests.ForeColor;
            using (SolidBrush b = new SolidBrush(back)) e.Graphics.FillRectangle(b, e.Bounds);
            TextRenderer.DrawText(e.Graphics, (string)listChests.Items[e.Index], ListFont, e.Bounds, fore,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }
    }
}
