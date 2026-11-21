using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

using com.lufiaIIEditor.RomInfo;

namespace com.lufiaIIEditor.UI
{
    /// <summary>
    /// "Capsule Feeding" tab: what capsules ask to eat (16 wanted-item lists, by appetite
    /// level), the price brackets that set an item's level, how much each feeding grows the
    /// capsule, and the fruits. Items that share a name with a different item are flagged,
    /// since feeding the look-alike doesn't count as the wanted item.
    /// </summary>
    class CapsuleFeedingTab : TabPage
    {
        private FeedingData feed;
        private ItemData items;
        private bool loading = false;
        private bool dirty = false;
        private FeedingData original;   // from the reference ROM, for highlighting
        private Dictionary<string, List<int>> sameName = new Dictionary<string, List<int>>();

        private ListBox listBrackets, listItems;
        private ComboBox comboPicker;
        private Button buttonAdd, buttonReplace, buttonRemove, buttonSwap;
        private Label labelSpace, labelWarn;
        private Button buttonClean;
        private NumericUpDown[] numPrice = new NumericUpDown[FeedingData.Brackets];
        private NumericUpDown[,] numGain = new NumericUpDown[FeedingData.Forms, FeedingData.Groups];
        private ComboBox[] comboFruit = new ComboBox[7];
        private ComboBox comboMagic;
        private int currentBracket = -1;

        private static readonly Font ListFont = new Font(FontFamily.GenericMonospace, 8.5f);

        public CapsuleFeedingTab()
        {
            this.Text = "Capsule Feeding";
            this.UseVisualStyleBackColor = true;
            Build();
            this.Enabled = false;
        }

        // ---------------- layout ----------------

        private void Build()
        {
            GroupBox gLists = UiHelpers.Anchor(this, UiHelpers.MakeGroup("What capsules ask to eat", 12, 8, 420, 500),
                AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left);
            Controls.Add(gLists);
            gLists.Controls.Add(UiHelpers.MakeNote("Appetite level (bracket of the best food eaten).  ! look-alike  * changed", 10, 18));
            listBrackets = new ListBox();
            listBrackets.Location = new Point(10, 36);
            listBrackets.Size = new Size(400, 150);
            listBrackets.IntegralHeight = false;
            listBrackets.Font = ListFont;
            listBrackets.HorizontalScrollbar = true;   // just in case a line is still too long
            listBrackets.SelectedIndexChanged += new EventHandler(listBrackets_SelectedIndexChanged);
            gLists.Controls.Add(listBrackets);

            gLists.Controls.Add(UiHelpers.MakeNote("Items it may ask for at that level (one is picked at random):", 10, 192));
            listItems = UiHelpers.Anchor(this, new ListBox(), AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right);
            listItems.Location = new Point(10, 210);
            listItems.Size = new Size(400, 170);
            listItems.IntegralHeight = false;
            listItems.DrawMode = DrawMode.OwnerDrawFixed;
            listItems.ItemHeight = 16;
            listItems.DrawItem += new DrawItemEventHandler(listItems_DrawItem);
            listItems.SelectedIndexChanged += delegate(object s, EventArgs e) { if (!loading) SyncPicker(); };
            gLists.Controls.Add(listItems);

            comboPicker = UiHelpers.Anchor(this, UiHelpers.MakeDropDown(10, 386, 400), AnchorStyles.Bottom | AnchorStyles.Left);
            gLists.Controls.Add(comboPicker);
            buttonAdd = MakeButton(gLists, "Add", 10, 70);
            buttonReplace = MakeButton(gLists, "Replace", 84, 70);
            buttonRemove = MakeButton(gLists, "Remove", 158, 70);
            buttonSwap = MakeButton(gLists, "Use the other version", 232, 178);
            buttonAdd.Click += delegate(object s, EventArgs e) { AddEntry(); };
            buttonReplace.Click += delegate(object s, EventArgs e) { ReplaceEntry(); };
            buttonRemove.Click += delegate(object s, EventArgs e) { RemoveEntry(); };
            buttonSwap.Click += delegate(object s, EventArgs e) { SwapVersion(); };
            labelWarn = UiHelpers.Anchor(this, UiHelpers.MakeLabel("", 10, 444), AnchorStyles.Bottom | AnchorStyles.Left);
            labelWarn.AutoSize = false;
            labelWarn.Size = new Size(400, 30);
            labelWarn.ForeColor = Color.DarkRed;
            gLists.Controls.Add(labelWarn);
            labelSpace = UiHelpers.Anchor(this, UiHelpers.MakeNote("", 10, 476), AnchorStyles.Bottom | AnchorStyles.Left);
            gLists.Controls.Add(labelSpace);
            buttonClean = UiHelpers.Anchor(this, new Button(), AnchorStyles.Bottom | AnchorStyles.Left);
            buttonClean.Text = "Remove impossible requests";
            buttonClean.Location = new Point(222, 470);
            buttonClean.Size = new Size(188, 24);
            buttonClean.Click += new EventHandler(buttonClean_Click);
            gLists.Controls.Add(buttonClean);

            // Price brackets
            GroupBox gPrice = UiHelpers.MakeGroup("Price brackets (upper limit of each)", 442, 8, 260, 290);
            Controls.Add(gPrice);
            for (int g = 0; g < FeedingData.Brackets; g++)
            {
                int x = 10 + (g / 8) * 125, y = 22 + (g % 8) * 32;
                gPrice.Controls.Add(UiHelpers.MakeLabel((g + 1).ToString(), x, y + 3));
                numPrice[g] = UiHelpers.MakeNumeric(x + 24, y, 0, 65535);
                numPrice[g].Size = new Size(80, 20);
                numPrice[g].ValueChanged += new EventHandler(PriceChanged);
                gPrice.Controls.Add(numPrice[g]);
            }

            // Growth
            GroupBox gGain = UiHelpers.MakeGroup("Growth per feeding (bar is full at 32)", 712, 8, 265, 170);
            Controls.Add(gGain);
            string[] groupNames = { "1-4", "5-8", "9-12", "13-16" };
            gGain.Controls.Add(UiHelpers.MakeNote("brackets:", 10, 20));
            for (int c = 0; c < FeedingData.Groups; c++) gGain.Controls.Add(UiHelpers.MakeNote(groupNames[c], 72 + c * 48, 20));
            for (int f = 0; f < FeedingData.Forms; f++)
            {
                gGain.Controls.Add(UiHelpers.MakeLabel("Form " + (f + 1), 10, 42 + f * 27));
                for (int c = 0; c < FeedingData.Groups; c++)
                {
                    numGain[f, c] = UiHelpers.MakeNumeric(70 + c * 48, 39 + f * 27, 0, 32);
                    numGain[f, c].Size = new Size(44, 20);
                    numGain[f, c].ValueChanged += new EventHandler(GainChanged);
                    gGain.Controls.Add(numGain[f, c]);
                }
            }
            gGain.Controls.Add(UiHelpers.MakeNote("x3 when it's the item the capsule wants.", 10, 148));

            // Fruits
            GroupBox gFruit = UiHelpers.MakeGroup("Fruits (special feeding items)", 712, 185, 265, 290);
            Controls.Add(gFruit);
            for (int k = 0; k < 7; k++)
            {
                gFruit.Controls.Add(UiHelpers.MakeLabel(FeedingData.Families[k], 10, 25 + k * 27));
                comboFruit[k] = UiHelpers.MakeDropDown(70, 22 + k * 27, 185);
                comboFruit[k].SelectedIndexChanged += new EventHandler(FruitChanged);
                gFruit.Controls.Add(comboFruit[k]);
            }
            gFruit.Controls.Add(UiHelpers.MakeLabel("Magic fruit", 10, 25 + 7 * 27));
            comboMagic = UiHelpers.MakeDropDown(70, 22 + 7 * 27, 185);
            comboMagic.SelectedIndexChanged += new EventHandler(FruitChanged);
            gFruit.Controls.Add(comboMagic);
            Label fruitNote = UiHelpers.MakeNote("Feeding a family its own fruit, or the Magic fruit, skips the normal rules.", 10, 245);
            fruitNote.AutoSize = false;
            fruitNote.Size = new Size(245, 36);
            gFruit.Controls.Add(fruitNote);

            // Wrapping note under the price brackets (fixed width so it never runs into the fruits box).
            Label how = UiHelpers.MakeNote(
                "How feeding works: an item's price puts it in a bracket. Better food raises the capsule's " +
                "appetite level to that bracket; cheaper food lowers it by one (items under 200G always do). " +
                "The capsule asks for a random item from its level's list, and eating exactly that item grows it 3x as much.\n\n" +
                "Look-alikes (marked !): some items exist twice with the same name (normal and cursed versions). " +
                "Feeding the other version doesn't count, so it looks as if the capsule asked for the wrong item.",
                442, 306);
            how.AutoSize = false;
            how.Size = new Size(260, 200);
            Controls.Add(how);
        }

        private Button MakeButton(GroupBox g, string text, int x, int w)
        {
            Button b = UiHelpers.Anchor(this, new Button(), AnchorStyles.Bottom | AnchorStyles.Left);
            b.Text = text;
            b.Location = new Point(x, 414);
            b.Size = new Size(w, 24);
            g.Controls.Add(b);
            return b;
        }

        // ---------------- load / commit ----------------

        public void LoadRom(FeedingData data, ItemData itemData)
        {
            feed = data; items = itemData; dirty = false;
            original = null;
            if (Reference.Available)
            {
                FeedingData r = new FeedingData(Reference.Data, Reference.HasHeader);
                if (r.Load() == null) original = r;
            }
            sameName.Clear();
            for (int id = 1; id < ItemData.Count; id++)
            {
                string n = items.GetName(id).Trim();
                if (n.Length == 0) continue;
                List<int> l;
                if (!sameName.TryGetValue(n, out l)) { l = new List<int>(); sameName[n] = l; }
                l.Add(id);
            }
            loading = true;
            comboPicker.Items.Clear(); comboMagic.Items.Clear();
            foreach (ComboBox cb in comboFruit) cb.Items.Clear();
            for (int id = 0; id < ItemData.Count; id++)
            {
                string label = ItemLabel(id);
                comboPicker.Items.Add(label);
                comboMagic.Items.Add(label);
                foreach (ComboBox cb in comboFruit) cb.Items.Add(label);
            }
            loading = false;
            this.Enabled = true;
            ShowAll();
            listBrackets.SelectedIndex = currentBracket >= 0 ? currentBracket : 0;
        }

        public void Disable()
        {
            feed = null; items = null; currentBracket = -1;
            this.Enabled = false;
        }

        public void CommitCurrent()
        {
            if (feed != null && dirty && feed.WriteLists()) dirty = false;
        }

        public void RefreshView() { if (feed != null) { LoadRomKeep(); } }

        private void LoadRomKeep()
        {
            int keep = currentBracket;
            CommitCurrent();
            LoadRom(feed, items);
            if (keep >= 0) listBrackets.SelectedIndex = keep;
        }

        /// <summary>The bracket an item's price falls in (0-based), or -1 for items under 200G.</summary>
        private int ItemBracket(int id)
        {
            int price = items.GetPrice(id);
            if (price < 200) return -1;
            for (int g = 0; g < FeedingData.Brackets; g++) if (feed.GetPriceLimit(g) >= price) return g;
            return FeedingData.Brackets - 1;
        }

        /// <summary>
        /// Can the capsule ever accept this request? The game rejects food from a lower bracket
        /// than the appetite level ("Disgusting", $82:C6FA) before it checks the wanted item, so a
        /// request priced below its own list's bracket is never met. Fruits are handled first and
        /// never count as the wanted item either.
        /// </summary>
        private bool Impossible(int list, int id)
        {
            if (id == feed.MagicFruit) return true;
            for (int k = 0; k < 7; k++) if (id == feed.GetFruit(k)) return true;
            return ItemBracket(id) < list;
        }

        private int ImpossibleCount(int list)
        {
            int n = 0;
            foreach (int id in feed.Wanted[list]) if (Impossible(list, id)) n++;
            return n;
        }

        private bool IsLookAlike(int id)
        {
            if (id <= 0 || id >= ItemData.Count) return false;
            List<int> l;
            return sameName.TryGetValue(items.GetName(id).Trim(), out l) && l.Count > 1;
        }

        private string ItemLabel(int id)
        {
            if (id == 0) return "(none)  (000)";
            return items.GetName(id).Trim() + "  (" + id.ToString("X3") + ")" + (IsLookAlike(id) ? "  !" : "");
        }

        private void ShowAll()
        {
            loading = true;
            for (int g = 0; g < FeedingData.Brackets; g++)
            {
                UiHelpers.SetValueClamped(numPrice[g], feed.GetPriceLimit(g));
                numPrice[g].Enabled = g < FeedingData.Brackets - 1;   // the last bracket catches everything
                ChangeHighlighter.Set(numPrice[g], original == null ? null : (object)original.GetPriceLimit(g));
            }
            for (int f = 0; f < FeedingData.Forms; f++)
                for (int c = 0; c < FeedingData.Groups; c++)
                {
                    UiHelpers.SetValueClamped(numGain[f, c], feed.GetGain(f, c));
                    ChangeHighlighter.Set(numGain[f, c], original == null ? null : (object)original.GetGain(f, c));
                }
            for (int k = 0; k < 7; k++)
            {
                comboFruit[k].SelectedIndex = feed.GetFruit(k);
                ChangeHighlighter.Set(comboFruit[k], original == null ? null : (object)original.GetFruit(k));
            }
            comboMagic.SelectedIndex = feed.MagicFruit;
            ChangeHighlighter.Set(comboMagic, original == null ? null : (object)original.MagicFruit);
            FillBrackets();
            loading = false;
            UpdateSpace();
        }

        private void FillBrackets()
        {
            bool was = loading;
            loading = true;
            int sel = listBrackets.SelectedIndex;
            listBrackets.BeginUpdate();
            listBrackets.Items.Clear();
            for (int g = 0; g < FeedingData.Brackets; g++)
            {
                List<int> l = feed.Wanted[g];
                bool look = false;
                foreach (int id in l) if (IsLookAlike(id)) look = true;
                // Kept short so a line fits the list without scrolling sideways.
                int bad = ImpossibleCount(g);
                string text = (g + 1).ToString().PadLeft(2) + " " + feed.BracketRange(g).PadRight(14) +
                    (l.Count == 0 ? "empty: uses next list's 1st item" :
                        l.Count + (l.Count == 1 ? " item" : " items") + (bad > 0 ? ", " + bad + " never accepted" : "")) +
                    (look ? " !" : "") + (ListChanged(g) ? " *" : "");
                listBrackets.Items.Add(text);
            }
            listBrackets.EndUpdate();
            if (sel >= 0) listBrackets.SelectedIndex = sel;
            loading = was;
        }

        private bool ListChanged(int g)
        {
            if (original == null) return false;
            List<int> a = feed.Wanted[g], b = original.Wanted[g];
            if (a.Count != b.Count) return true;
            for (int k = 0; k < a.Count; k++) if (a[k] != b[k]) return true;
            return false;
        }

        // ---------------- lists ----------------

        private void listBrackets_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (loading || feed == null || listBrackets.SelectedIndex < 0) return;
            currentBracket = listBrackets.SelectedIndex;
            FillItems(0);
        }

        private void FillItems(int select)
        {
            if (currentBracket < 0) return;
            List<int> l = feed.Wanted[currentBracket];
            bool was = loading;
            loading = true;
            listItems.BeginUpdate();
            listItems.Items.Clear();
            foreach (int id in l)
                listItems.Items.Add((Impossible(currentBracket, id) ? "X " : "  ") + ItemLabel(id) + "   " + items.GetPrice(id) + "G");
            listItems.EndUpdate();
            if (l.Count > 0) listItems.SelectedIndex = Math.Max(0, Math.Min(select, l.Count - 1));
            loading = was;
            SyncPicker();
        }

        private void SyncPicker()
        {
            List<int> l = currentBracket >= 0 ? feed.Wanted[currentBracket] : null;
            int i = listItems.SelectedIndex;
            if (l != null && i >= 0 && i < l.Count) { bool was = loading; loading = true; comboPicker.SelectedIndex = l[i]; loading = was; }
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            List<int> l = currentBracket >= 0 ? feed.Wanted[currentBracket] : null;
            int i = listItems.SelectedIndex;
            bool sel = l != null && i >= 0 && i < l.Count;
            buttonAdd.Enabled = l != null && feed.Free > 0;
            buttonReplace.Enabled = sel;
            buttonRemove.Enabled = sel;
            buttonSwap.Enabled = sel && IsLookAlike(l[i]);
            if (sel && IsLookAlike(l[i]))
            {
                List<int> versions = sameName[items.GetName(l[i]).Trim()];
                List<string> ids = new List<string>();
                foreach (int v in versions) ids.Add(v.ToString("X3"));
                labelWarn.Text = "\"" + items.GetName(l[i]).Trim() + "\" exists " + versions.Count + " times (" + string.Join(", ", ids.ToArray()) +
                    "). Only " + l[i].ToString("X3") + " counts as the wanted item.";
            }
            else if (sel && Impossible(currentBracket, l[i]))
            {
                int id = l[i], b = ItemBracket(id);
                bool fruit = id == feed.MagicFruit;
                for (int k = 0; k < 7; k++) if (id == feed.GetFruit(k)) fruit = true;
                labelWarn.Text = fruit
                    ? "Never accepted as the wanted item: fruits have their own special effect."
                    : "Never accepted: " + items.GetName(id).Trim() + " (" + items.GetPrice(id) + "G) is " +
                      (b < 0 ? "under 200G" : "bracket " + (b + 1)) + ", cheaper than this list's level " + (currentBracket + 1) + ", so the capsule calls it disgusting.";
            }
            else labelWarn.Text = "";
            int total = 0;
            for (int g = 0; g < FeedingData.Brackets; g++) total += ImpossibleCount(g);
            buttonClean.Enabled = total > 0;
        }

        private void UpdateSpace()
        {
            labelSpace.Text = "Space: " + feed.Used + "/" + FeedingData.Capacity + " used, " + feed.Free + " free";
            UpdateButtons();
        }

        private void Changed(int select)
        {
            dirty = true;
            FillBrackets();
            FillItems(select);
            UpdateSpace();
        }

        private void AddEntry()
        {
            int id = comboPicker.SelectedIndex;
            if (currentBracket < 0 || id <= 0 || feed.Free <= 0) return;
            List<int> l = feed.Wanted[currentBracket];
            int at = listItems.SelectedIndex < 0 ? l.Count : listItems.SelectedIndex + 1;
            l.Insert(at, id);
            Changed(at);
        }

        private void ReplaceEntry()
        {
            int id = comboPicker.SelectedIndex, i = listItems.SelectedIndex;
            if (currentBracket < 0 || id <= 0 || i < 0) return;
            feed.Wanted[currentBracket][i] = id;
            Changed(i);
        }

        private void RemoveEntry()
        {
            int i = listItems.SelectedIndex;
            if (currentBracket < 0 || i < 0) return;
            feed.Wanted[currentBracket].RemoveAt(i);
            Changed(i);
        }

        private void SwapVersion()
        {
            int i = listItems.SelectedIndex;
            if (currentBracket < 0 || i < 0) return;
            int id = feed.Wanted[currentBracket][i];
            List<int> versions;
            if (!sameName.TryGetValue(items.GetName(id).Trim(), out versions) || versions.Count < 2) return;
            feed.Wanted[currentBracket][i] = versions[(versions.IndexOf(id) + 1) % versions.Count];
            Changed(i);
        }

        private void buttonClean_Click(object sender, EventArgs e)
        {
            int total = 0;
            for (int g = 0; g < FeedingData.Brackets; g++) total += ImpossibleCount(g);
            if (total == 0) return;
            if (MessageBox.Show("Remove all " + total + " requests the capsules can never accept (items cheaper than their list's level, and fruits)?\n\n" +
                    "Lists that end up empty will borrow the next list's first item, as the game does.",
                    "Capsule Feeding", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            for (int g = 0; g < FeedingData.Brackets; g++)
                feed.Wanted[g].RemoveAll(delegate(int id) { return Impossible(g, id); });
            Changed(0);
        }

        private void listItems_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (feed == null || currentBracket < 0 || e.Index < 0 || e.Index >= listItems.Items.Count) return;
            bool selected = (e.State & DrawItemState.Selected) != 0;
            List<int> l = feed.Wanted[currentBracket];
            bool changed = false;
            if (original != null)
            {
                List<int> o = original.Wanted[currentBracket];
                changed = e.Index >= o.Count || o[e.Index] != l[e.Index];
            }
            Color back = selected ? SystemColors.Highlight : changed ? ChangeHighlighter.ChangedColor : listItems.BackColor;
            Color fore = selected ? SystemColors.HighlightText : Impossible(currentBracket, l[e.Index]) ? SystemColors.GrayText :
                IsLookAlike(l[e.Index]) ? Color.DarkRed : listItems.ForeColor;
            using (SolidBrush b = new SolidBrush(back)) e.Graphics.FillRectangle(b, e.Bounds);
            TextRenderer.DrawText(e.Graphics, (string)listItems.Items[e.Index], ListFont, e.Bounds, fore,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }

        // ---------------- brackets, gains, fruits ----------------

        private void PriceChanged(object sender, EventArgs e)
        {
            if (loading || feed == null) return;
            for (int g = 0; g < FeedingData.Brackets - 1; g++)
                if (sender == numPrice[g]) feed.SetPriceLimit(g, (int)numPrice[g].Value);
            FillBrackets();
            FillItems(listItems.SelectedIndex);
        }

        private void GainChanged(object sender, EventArgs e)
        {
            if (loading || feed == null) return;
            for (int f = 0; f < FeedingData.Forms; f++)
                for (int c = 0; c < FeedingData.Groups; c++)
                    if (sender == numGain[f, c]) feed.SetGain(f, c, (int)numGain[f, c].Value);
        }

        private void FruitChanged(object sender, EventArgs e)
        {
            if (loading || feed == null) return;
            for (int k = 0; k < 7; k++)
                if (sender == comboFruit[k] && comboFruit[k].SelectedIndex > 0) feed.SetFruit(k, comboFruit[k].SelectedIndex);
            if (sender == comboMagic && comboMagic.SelectedIndex > 0) feed.MagicFruit = comboMagic.SelectedIndex;
        }
    }
}
