using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

using com.lufiaIIEditor.RomInfo;

namespace com.lufiaIIEditor.UI
{
    /// <summary>
    /// "Shops" tab: what every item shop, magic shop and casino prize counter sells.
    /// All shops share one block of ROM space, so a shop can sell more only if another
    /// sells less; the space bar at the top shows what's left.
    /// </summary>
    class ShopsTab : TabPage
    {
        private class Section
        {
            public int Bit;
            public GroupBox Group;
            public ListBox List;
            public ComboBox Picker;
            public List<int> PickerIds = new List<int>();
            public Button Add, Replace, Remove, Up, Down;
            public Label Note;
        }

        private ShopData shops;
        private ItemData items;
        private string[] spellNames;
        private int currentIndex = -1;
        private bool loading = false;
        private bool dirty = false;

        private ComboBox comboShop;
        private Label labelInfo;
        private Label labelSpace;
        private ProgressBar barSpace;
        private CheckBox checkAllItems;
        private Button buttonRestoreShop;
        private Label labelRestore;
        private Section[] sections = new Section[3];

        public ShopsTab()
        {
            this.Text = "Shops";
            this.UseVisualStyleBackColor = true;
            Build();
            this.Enabled = false;
        }

        // ---------------- layout ----------------

        private void Build()
        {
            Controls.Add(UiHelpers.MakeLabel("Shop:", 12, 15));
            comboShop = UiHelpers.MakeDropDown(60, 12, 520);
            comboShop.SelectedIndexChanged += new EventHandler(comboShop_SelectedIndexChanged);
            Controls.Add(comboShop);
            labelInfo = UiHelpers.MakeNote("", 595, 15);
            Controls.Add(labelInfo);

            labelSpace = UiHelpers.MakeLabel("", 12, 46);
            Controls.Add(labelSpace);
            barSpace = new ProgressBar();
            barSpace.Location = new Point(600, 44);
            barSpace.Size = new Size(240, 16);
            barSpace.Maximum = 1000;
            Controls.Add(barSpace);
            Controls.Add(UiHelpers.MakeNote(
                "All shops share one block of space, and the original game uses every byte of it. " +
                "Remove entries from any shop to make room for more somewhere else.", 12, 68));
            Controls.Add(UiHelpers.MakeNote(
                "Each item costs 2 bytes and each spell 1 byte. Prices come from the Items, Equipment and Spells tabs. " +
                "Blue = different from the original game.", 12, 86));

            for (int k = 0; k < sections.Length; k++) sections[k] = BuildSection(12 + k * 318);

            checkAllItems = UiHelpers.Anchor(this, new CheckBox(), AnchorStyles.Bottom | AnchorStyles.Left);
            checkAllItems.Text = "Show every item in the weapon and armor pickers";
            checkAllItems.Location = new Point(12, 482);
            checkAllItems.AutoSize = true;
            checkAllItems.CheckedChanged += delegate(object s, EventArgs e) { if (!loading) FillPickers(); };
            Controls.Add(checkAllItems);

            buttonRestoreShop = UiHelpers.Anchor(this, new Button(), AnchorStyles.Bottom | AnchorStyles.Left);
            buttonRestoreShop.Text = "Restore this shop to the original";
            buttonRestoreShop.Location = new Point(330, 478);
            buttonRestoreShop.Size = new Size(200, 24);
            buttonRestoreShop.Click += new EventHandler(buttonRestoreShop_Click);
            Controls.Add(buttonRestoreShop);
            labelRestore = UiHelpers.Anchor(this, UiHelpers.MakeNote("", 540, 483), AnchorStyles.Bottom | AnchorStyles.Left);
            Controls.Add(labelRestore);
        }

        private Section BuildSection(int x)
        {
            Section s = new Section();
            s.Group = UiHelpers.Anchor(this, UiHelpers.MakeGroup("", x, 110, 310, 362), AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left);
            Controls.Add(s.Group);

            s.List = UiHelpers.Anchor(this, new ListBox(), AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right);
            s.List.Location = new Point(10, 20);
            s.List.Size = new Size(290, 238);
            s.List.DrawMode = DrawMode.OwnerDrawFixed;
            s.List.ItemHeight = 16;
            s.List.IntegralHeight = false;
            s.List.Tag = s;
            s.List.DrawItem += new DrawItemEventHandler(List_DrawItem);
            s.List.SelectedIndexChanged += delegate(object o, EventArgs e) { if (!loading) EntrySelected(s); };
            s.Group.Controls.Add(s.List);

            s.Picker = UiHelpers.Anchor(this, UiHelpers.MakeDropDown(10, 266, 290), AnchorStyles.Bottom | AnchorStyles.Left);
            s.Group.Controls.Add(s.Picker);

            s.Add = MakeButton(s, "Add", 10, 60);
            s.Replace = MakeButton(s, "Replace", 74, 66);
            s.Remove = MakeButton(s, "Remove", 144, 66);
            s.Up = MakeButton(s, "Up", 214, 42);
            s.Down = MakeButton(s, "Down", 258, 42);
            s.Add.Click += delegate(object o, EventArgs e) { AddEntry(s); };
            s.Replace.Click += delegate(object o, EventArgs e) { ReplaceEntry(s); };
            s.Remove.Click += delegate(object o, EventArgs e) { RemoveEntry(s); };
            s.Up.Click += delegate(object o, EventArgs e) { MoveEntry(s, -1); };
            s.Down.Click += delegate(object o, EventArgs e) { MoveEntry(s, 1); };

            s.Note = UiHelpers.Anchor(this, UiHelpers.MakeNote("", 10, 326), AnchorStyles.Bottom | AnchorStyles.Left);
            s.Note.AutoSize = false;
            s.Note.Size = new Size(292, 30);
            s.Group.Controls.Add(s.Note);
            return s;
        }

        private Button MakeButton(Section s, string text, int x, int w)
        {
            Button b = UiHelpers.Anchor(this, new Button(), AnchorStyles.Bottom | AnchorStyles.Left);
            b.Text = text;
            b.Location = new Point(x, 294);
            b.Size = new Size(w, 24);
            s.Group.Controls.Add(b);
            return b;
        }

        // ---------------- loading ----------------

        public void LoadRom(ShopData shopData, ItemData itemData, string[] spells)
        {
            int keep = comboShop.SelectedIndex;
            shops = shopData;
            items = itemData;
            spellNames = spells;
            currentIndex = -1;
            dirty = false;

            loading = true;
            comboShop.Items.Clear();
            foreach (ShopData.Shop s in shops.Shops) comboShop.Items.Add(Describe(s));
            loading = false;

            this.Enabled = true;
            comboShop.SelectedIndex = (keep >= 0 && keep < comboShop.Items.Count) ? keep : 0;
        }

        public void Disable()
        {
            shops = null; items = null; currentIndex = -1; dirty = false;
            comboShop.Items.Clear();
            this.Enabled = false;
        }

        /// <summary>Writes the shops into the ROM data (only if something was edited).</summary>
        public void CommitCurrent()
        {
            if (shops == null || !dirty) return;
            if (shops.Write()) dirty = false;
        }

        /// <summary>Shows everything again (names and prices may have changed in other tabs).</summary>
        public void RefreshView()
        {
            if (shops == null) return;
            loading = true;
            for (int i = 0; i < shops.Shops.Count; i++) UiHelpers.RenameItem(comboShop, i, Describe(shops.Shops[i]));
            loading = false;
            if (currentIndex >= 0) ShowShop(currentIndex);
        }

        private static readonly Font ListFont = new Font(FontFamily.GenericMonospace, 8.5f);
        private static readonly int[] ListOrder = { 2, 3, 4, 1, 5 };

        private string Describe(ShopData.Shop s)
        {
            List<string> kinds = new List<string>();
            foreach (int b in ListOrder)
                if (s.Lists[b] != null) kinds.Add(ListTitle(s, b).ToLower());
            if (kinds.Count == 0) kinds.Add("list built by the game");
            string text = s.Id.ToString("X2") + "  " + ShopData.LocationOf(s.Id) + "  -  " + string.Join(", ", kinds.ToArray());
            if (IsShopChanged(s)) text += "   [changed]";
            return text;
        }

        /// <summary>In a casino the item list is the prize counter (the Coins list is what's sold for gold).</summary>
        private static bool IsCasino(ShopData.Shop s) { return s.Lists[1] != null; }

        private static string ListTitle(ShopData.Shop s, int bit)
        {
            if (bit == 2 && IsCasino(s)) return "Prizes";
            return ShopData.ListNames[bit];
        }

        private string EntryName(int bit, int id)
        {
            if (bit == 5)
                return (spellNames != null && id >= 0 && id < spellNames.Length && spellNames[id] != null)
                    ? spellNames[id].Trim() : "Spell " + id;
            if (items != null && id > 0 && id < ItemData.Count) return items.GetName(id).Trim();
            return "Item " + id.ToString("X3");
        }

        private string EntryText(ShopData.Shop shop, int bit, int id)
        {
            string name = EntryName(bit, id);
            if (bit == 5 || items == null || id <= 0 || id >= ItemData.Count) return name;
            int price = items.GetPrice(id);
            // Prize counters charge the item's price x 200 in coins (matches every prize in the original game).
            if (bit == 2 && IsCasino(shop)) return name.PadRight(14) + (price * 200) + " coins";
            return name.PadRight(14) + price + "G";
        }

        // ---------------- comparison ----------------

        private ShopData.Shop Original(int id)
        {
            ShopData r = Reference.Shops;
            return r == null ? null : r.Find(id);
        }

        private bool IsShopChanged(ShopData.Shop s)
        {
            if (!Reference.Available) return false;
            ShopData.Shop o = Original(s.Id);
            if (o == null) return Reference.Shops != null;
            for (int b = 1; b < ShopData.ListCount; b++)
            {
                List<int> a = s.Lists[b], c = o.Lists[b];
                if ((a == null) != (c == null)) return true;
                if (a == null) continue;
                if (a.Count != c.Count) return true;
                for (int k = 0; k < a.Count; k++) if (a[k] != c[k]) return true;
            }
            return false;
        }

        private bool IsEntryChanged(int bit, int index)
        {
            if (currentIndex < 0 || Reference.Shops == null) return false;
            ShopData.Shop s = shops.Shops[currentIndex];
            ShopData.Shop o = Original(s.Id);
            if (o == null || o.Lists[bit] == null) return true;
            return index >= o.Lists[bit].Count || o.Lists[bit][index] != s.Lists[bit][index];
        }

        // ---------------- showing a shop ----------------

        private void comboShop_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (loading || shops == null) return;
            int i = comboShop.SelectedIndex;
            if (i < 0 || i == currentIndex) return;
            ShowShop(i);
        }

        private void ShowShop(int index)
        {
            loading = true;
            currentIndex = index;
            ShopData.Shop shop = shops.Shops[index];
            labelInfo.Text = "ID " + shop.Id.ToString("X2") + "   type byte " + shop.Type.ToString("X2") +
                "   flags " + shop.Flags.ToString("X4");

            int k = 0;
            foreach (int b in ListOrder)
            {
                if (shop.Lists[b] == null || k >= sections.Length) continue;
                sections[k].Bit = b;
                sections[k].Group.Visible = true;
                k++;
            }
            for (; k < sections.Length; k++) { sections[k].Bit = 0; sections[k].Group.Visible = false; }

            foreach (Section s in sections) if (s.Bit != 0) FillList(s, 0);
            FillPickers();
            loading = false;

            foreach (Section s in sections) if (s.Bit != 0) EntrySelected(s);
            UpdateSpace();
        }

        private void FillList(Section s, int select)
        {
            ShopData.Shop shop = shops.Shops[currentIndex];
            List<int> list = shop.Lists[s.Bit];
            bool was = loading;
            loading = true;
            s.List.BeginUpdate();
            s.List.Items.Clear();
            foreach (int id in list) s.List.Items.Add(EntryText(shop, s.Bit, id));
            s.List.EndUpdate();
            if (list.Count > 0) s.List.SelectedIndex = Math.Max(0, Math.Min(select, list.Count - 1));
            loading = was;

            string title = ListTitle(shop, s.Bit) + " (" + list.Count + ")";
            ShopData.Shop o = Original(shop.Id);
            if (o != null && o.Lists[s.Bit] != null && o.Lists[s.Bit].Count != list.Count)
                title += "  -  original had " + o.Lists[s.Bit].Count;
            s.Group.Text = title;
        }

        private void FillPickers()
        {
            foreach (Section s in sections)
            {
                if (s.Bit == 0) continue;
                bool was = loading;
                loading = true;
                s.Picker.BeginUpdate();
                s.Picker.Items.Clear();
                s.PickerIds.Clear();
                if (s.Bit == 5)
                {
                    for (int i = 0; spellNames != null && i < spellNames.Length; i++)
                    {
                        s.PickerIds.Add(i);
                        s.Picker.Items.Add(EntryName(5, i) + "  (" + i.ToString("X2") + ")");
                    }
                }
                else
                {
                    for (int id = 1; id < ItemData.Count; id++)
                    {
                        if (!Fits(s.Bit, id)) continue;
                        s.PickerIds.Add(id);
                        s.Picker.Items.Add(EntryName(s.Bit, id) + "  (" + id.ToString("X3") + ")");
                    }
                }
                s.Picker.EndUpdate();
                loading = was;
                SyncPicker(s);
            }
        }

        /// <summary>Weapon and armor lists offer only matching equipment unless "show every item" is ticked.</summary>
        private bool Fits(int bit, int id)
        {
            if (checkAllItems.Checked || bit == 1 || bit == 2) return true;
            int slot = items.GetSlot(id);
            if (bit == 3) return slot == 0x01;
            return slot == 0x02 || slot == 0x04 || slot == 0x08 || slot == 0x10 || slot == 0x20;
        }

        private void SyncPicker(Section s)
        {
            int i = s.List.SelectedIndex;
            if (i < 0) return;
            int id = shops.Shops[currentIndex].Lists[s.Bit][i];
            int p = s.PickerIds.IndexOf(id);
            if (p >= 0) { bool was = loading; loading = true; s.Picker.SelectedIndex = p; loading = was; }
        }

        private void EntrySelected(Section s)
        {
            SyncPicker(s);
            UpdateButtons(s);
        }

        private void List_DrawItem(object sender, DrawItemEventArgs e)
        {
            ListBox lb = (ListBox)sender;
            Section s = (Section)lb.Tag;
            if (e.Index < 0 || e.Index >= lb.Items.Count) return;
            bool selected = (e.State & DrawItemState.Selected) != 0;
            bool changed = !selected && IsEntryChanged(s.Bit, e.Index);
            Color back = selected ? SystemColors.Highlight : changed ? ChangeHighlighter.ChangedColor : lb.BackColor;
            Color fore = selected ? SystemColors.HighlightText : lb.ForeColor;
            using (SolidBrush b = new SolidBrush(back)) e.Graphics.FillRectangle(b, e.Bounds);
            TextRenderer.DrawText(e.Graphics, (e.Index + 1).ToString().PadLeft(2) + "  " + lb.Items[e.Index],
                ListFont, e.Bounds, fore, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }

        // ---------------- editing ----------------

        private List<int> ListOf(Section s) { return shops.Shops[currentIndex].Lists[s.Bit]; }

        private int PickedId(Section s)
        {
            int p = s.Picker.SelectedIndex;
            return p < 0 ? -1 : s.PickerIds[p];
        }

        private void AddEntry(Section s)
        {
            int id = PickedId(s);
            if (id < 0) return;
            if (s.Bit != 5 && id == 0) return;   // 0 ends a list
            if (shops.Free < ShopData.EntrySize(s.Bit)) return;
            List<int> list = ListOf(s);
            int at = s.List.SelectedIndex < 0 ? list.Count : s.List.SelectedIndex + 1;
            list.Insert(at, id);
            Changed(s, at);
        }

        private void ReplaceEntry(Section s)
        {
            int id = PickedId(s);
            int i = s.List.SelectedIndex;
            if (id < 0 || i < 0) return;
            if (s.Bit != 5 && id == 0) return;
            ListOf(s)[i] = id;
            Changed(s, i);
        }

        private void RemoveEntry(Section s)
        {
            List<int> list = ListOf(s);
            int i = s.List.SelectedIndex;
            if (i < 0 || list.Count <= 1) return;
            list.RemoveAt(i);
            Changed(s, Math.Min(i, list.Count - 1));
        }

        private void MoveEntry(Section s, int dir)
        {
            List<int> list = ListOf(s);
            int i = s.List.SelectedIndex, j = i + dir;
            if (i < 0 || j < 0 || j >= list.Count) return;
            int t = list[i]; list[i] = list[j]; list[j] = t;
            Changed(s, j);
        }

        private void Changed(Section s, int select)
        {
            dirty = true;
            FillList(s, select);
            SyncPicker(s);
            UpdateSpace();
            loading = true;
            UiHelpers.RenameItem(comboShop, currentIndex, Describe(shops.Shops[currentIndex]));
            loading = false;
        }

        private void UpdateSpace()
        {
            int used = shops.Used, cap = shops.Capacity, free = cap - used;
            labelSpace.Text = "Shop space: " + used + " of " + cap + " bytes used  -  " + free + " free" +
                (free > 0 ? "  (room for " + (free / 2) + " more items or " + free + " more spells)" : "");
            labelSpace.ForeColor = free > 0 ? Color.DarkGreen : SystemColors.ControlText;
            barSpace.Value = Math.Max(0, Math.Min(1000, (int)((long)used * 1000 / Math.Max(1, cap))));
            foreach (Section s in sections) if (s.Bit != 0) UpdateButtons(s);
            UpdateRestore();
        }

        private void UpdateButtons(Section s)
        {
            List<int> list = ListOf(s);
            int i = s.List.SelectedIndex;
            bool room = shops.Free >= ShopData.EntrySize(s.Bit);
            s.Add.Enabled = room;
            s.Replace.Enabled = i >= 0;
            s.Remove.Enabled = i >= 0 && list.Count > 1;
            s.Up.Enabled = i > 0;
            s.Down.Enabled = i >= 0 && i < list.Count - 1;
            if (!room) s.Note.Text = "No space left to add. Remove an entry here or in another shop first.";
            else if (list.Count <= 1) s.Note.Text = "A list needs at least one entry, so the last one can only be replaced.";
            else s.Note.Text = "Add puts the picked " + (s.Bit == 5 ? "spell" : "item") + " after the selected entry.";
        }

        private void UpdateRestore()
        {
            ShopData.Shop s = shops.Shops[currentIndex];
            ShopData.Shop o = Original(s.Id);
            bool changed = o != null && IsShopChanged(s);
            bool fits = o != null && shops.Free + s.Size - o.Size >= 0;
            buttonRestoreShop.Enabled = changed && fits;
            if (o == null) labelRestore.Text = Reference.Shops == null ? "(original unknown)" : "(not in the original game)";
            else if (!changed) labelRestore.Text = "";
            else if (!fits) labelRestore.Text = "Needs " + (o.Size - s.Size - shops.Free) + " more free bytes to restore.";
            else labelRestore.Text = "";
        }

        private void buttonRestoreShop_Click(object sender, EventArgs e)
        {
            ShopData.Shop s = shops.Shops[currentIndex];
            ShopData.Shop o = Original(s.Id);
            if (o == null || shops.Free + s.Size - o.Size < 0) return;
            if (s.Flags != o.Flags) return;   // list types differ; leave it alone
            for (int b = 1; b < ShopData.ListCount; b++)
                s.Lists[b] = o.Lists[b] == null ? null : new List<int>(o.Lists[b]);
            dirty = true;
            loading = true;
            UiHelpers.RenameItem(comboShop, currentIndex, Describe(s));
            loading = false;
            ShowShop(currentIndex);
        }
    }
}
