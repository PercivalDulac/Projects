using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

using com.lufiaIIEditor.RomInfo;

namespace com.lufiaIIEditor.UI
{
    /// <summary>
    /// "Equipment" tab: weapons, armor, shields, helmets, rings and jewels.
    /// Name (12 letters), price, who can equip, stat bonuses and the IP move.
    /// Bonuses and the IP move share a fixed number of slots per item (see ItemData.SetPoolValues).
    /// </summary>
    class EquipmentTab : TabPage
    {
        private ItemData items;
        private IPData ip;
        private List<int> itemIds = new List<int>();   // combo index -> item ID
        private int currentIndex = -1;
        private bool loading = false;

        private ComboBox comboItem;
        private Label labelInfo;
        private TextBox textName;
        private NumericUpDown numPrice;
        private CheckBox[] checkEquip = new CheckBox[7];
        private NumericUpDown[] numBonus = new NumericUpDown[7];
        private ComboBox comboIP;
        private Label labelSlots;
        private StatusEffectsPanel statusPanel;
        private CheckBox checkCave;
        private Label labelCave;
        private GroupBox gElements;
        private ComboBox comboAttack, comboAlways, comboGuardElement, comboGuardAction, comboGuardFlag;
        private Label labelAlways;
        private CheckBox checkFixed;
        private NumericUpDown numFixed;
        private bool alwaysExact;   // Pearl brace style: the "always" combo lists elements

        public EquipmentTab()
        {
            this.Text = "Equipment";
            this.UseVisualStyleBackColor = true;
            Build();
            this.Enabled = false;
        }

        private void Build()
        {
            Controls.Add(UiHelpers.MakeLabel("Equipment:", 12, 15));
            comboItem = UiHelpers.MakeDropDown(110, 12, 260);
            comboItem.SelectedIndexChanged += new EventHandler(comboItem_SelectedIndexChanged);
            Controls.Add(comboItem);

            labelInfo = UiHelpers.MakeLabel("", 390, 15);
            Controls.Add(labelInfo);

            Controls.Add(UiHelpers.MakeLabel("Name:", 12, 47));
            textName = UiHelpers.MakeNameBox(110, 44, ItemData.NameLength);
            Controls.Add(textName);
            Controls.Add(UiHelpers.MakeNote("(max " + ItemData.NameLength + " letters - the game's fixed name size)", 280, 47));

            Controls.Add(UiHelpers.MakeLabel("Price:", 12, 77));
            numPrice = UiHelpers.MakeNumeric(110, 74, 0, 65535);
            Controls.Add(numPrice);

            GroupBox gEquip = UiHelpers.MakeGroup("Who can equip", 12, 110, 200, 245);
            Controls.Add(gEquip);
            for (int c = 0; c < 7; c++)
            {
                checkEquip[c] = new CheckBox();
                checkEquip[c].Text = ItemData.CharacterNames[c];
                checkEquip[c].Location = new Point(15, 25 + c * 30);
                checkEquip[c].AutoSize = true;
                gEquip.Controls.Add(checkEquip[c]);
            }

            checkCave = new CheckBox();
            checkCave.Text = "Found in Ancient Cave red chests";
            checkCave.Location = new Point(12, 364);
            checkCave.AutoSize = true;
            Controls.Add(checkCave);
            labelCave = UiHelpers.MakeNote("", 12, 386);
            labelCave.AutoSize = false;
            labelCave.Size = new Size(205, 80);
            Controls.Add(labelCave);

            GroupBox gBonus = UiHelpers.MakeGroup("Bonuses and IP move", 225, 110, 420, 300);
            Controls.Add(gBonus);
            for (int b = 0; b < 7; b++)
            {
                gBonus.Controls.Add(UiHelpers.MakeLabel(ItemData.BonusNames[b], 15, 28 + b * 30));
                numBonus[b] = UiHelpers.MakeNumeric(70, 25 + b * 30, 0, 65535);
                numBonus[b].ValueChanged += new EventHandler(PoolValueChanged);
                gBonus.Controls.Add(numBonus[b]);
            }
            gBonus.Controls.Add(UiHelpers.MakeLabel("IP move", 15, 241));
            comboIP = UiHelpers.MakeDropDown(70, 238, 200);
            comboIP.SelectedIndexChanged += new EventHandler(PoolValueChanged);
            gBonus.Controls.Add(comboIP);

            checkFixed = new CheckBox();
            checkFixed.Text = "Fixed attack power (ignores ATP):";
            checkFixed.Location = new Point(15, 270);
            checkFixed.AutoSize = true;
            checkFixed.CheckedChanged += delegate { numFixed.Enabled = checkFixed.Enabled && checkFixed.Checked; };
            gBonus.Controls.Add(checkFixed);
            numFixed = UiHelpers.MakeNumeric(215, 268, 0, 999);
            gBonus.Controls.Add(numFixed);
            new ToolTip().SetToolTip(checkFixed,
                "Normal weapons attack with the wielder's ATP total (base + equipment + boosts).\n" +
                "The Bunny sword instead attacks with a fixed power of 120, so its ATP bonus never\n" +
                "counts in a regular attack (IP moves that use a normal attack still use ATP).\n" +
                "Untick to make it attack normally. Only weapons with room for this switch\n" +
                "(just the Bunny sword in the original) can use it.");

            labelSlots = UiHelpers.MakeLabel("", 160, 28);
            labelSlots.Font = new Font(labelSlots.Font, FontStyle.Bold);
            gBonus.Controls.Add(labelSlots);
            gBonus.Controls.Add(UiHelpers.MakeNote(
                "Each item has a fixed number of slots for\n" +
                "bonuses + IP move (the item's size in the ROM\n" +
                "can't grow). Set a value to 0 / (none) to free a\n" +
                "slot, then use it for a different stat.", 160, 55));

            GroupBox gStatus = UiHelpers.MakeGroup("Status effects", 655, 110, 320, 188);
            Controls.Add(UiHelpers.Anchor(this, gStatus, AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right));
            statusPanel = UiHelpers.Anchor(this, new StatusEffectsPanel(), AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right);
            statusPanel.Location = new Point(5, 18);
            statusPanel.Size = new Size(310, 122);
            gStatus.Controls.Add(statusPanel);
            gStatus.Controls.Add(UiHelpers.MakeNote(
                "Chances are % (100 = always). Protection: an\n" +
                "incoming attempt at that status gets this % instead.\n" +
                "Only existing entries can be changed.", 8, 142));

            gElements = UiHelpers.MakeGroup("Elements", 655, 302, 320, 208);
            Controls.Add(UiHelpers.Anchor(this, gElements, AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right));
            ToolTip tip = new ToolTip();
            gElements.Controls.Add(UiHelpers.MakeLabel("Attack element:", 10, 25));
            comboAttack = UiHelpers.MakeDropDown(110, 22, 200);
            tip.SetToolTip(comboAttack, "The weapon's attacks carry this element (enemies that resist or are weak to it react).");
            gElements.Controls.Add(comboAttack);
            labelAlways = UiHelpers.MakeLabel("Always:", 10, 53);
            gElements.Controls.Add(labelAlways);
            comboAlways = UiHelpers.MakeDropDown(110, 50, 200);
            tip.SetToolTip(comboAlways, "Works all the time while the item is worn.");
            gElements.Controls.Add(comboAlways);

            Label guardHead = UiHelpers.MakeLabel("Only while an IP move is switched on:", 10, 84);
            guardHead.Font = new Font(guardHead.Font, FontStyle.Bold);
            gElements.Controls.Add(guardHead);
            comboGuardElement = UiHelpers.MakeDropDown(10, 104, 140);
            gElements.Controls.Add(comboGuardElement);
            comboGuardAction = UiHelpers.MakeDropDown(155, 104, 155);
            gElements.Controls.Add(comboGuardAction);
            gElements.Controls.Add(UiHelpers.MakeLabel("Switched on by:", 10, 135));
            comboGuardFlag = UiHelpers.MakeDropDown(110, 132, 200);
            string guardTip =
                "The element and what happens to it belong to this item. The IP move only switches it on\n" +
                "for a few turns (5 in the original - set on the IP Moves tab). Guard IPs: Flame guard ... Holy wall,\n" +
                "Fury force, Miracle care, Ancient power. Mirror IPs: Flame return ... Brilliance.\n" +
                "Any IP of the right kind works, whatever element its name says.";
            tip.SetToolTip(comboGuardFlag, guardTip);
            tip.SetToolTip(comboGuardElement, guardTip);
            tip.SetToolTip(comboGuardAction, guardTip);
            gElements.Controls.Add(comboGuardFlag);

            Label n = UiHelpers.MakeNote(
                "The IP move is just the on switch; the element and effect are the item's. " +
                "Greyed out = this item has no such effect (no room to add one).", 10, 162);
            n.AutoSize = false;
            n.Size = new Size(300, 40);
            gElements.Controls.Add(n);
        }

        public void LoadRom(ItemData itemData, IPData ipData)
        {
            int keep = comboItem.SelectedIndex;
            items = itemData;
            ip = ipData;
            currentIndex = -1;

            loading = true;
            comboItem.Items.Clear();
            itemIds.Clear();
            for (int i = 0; i < ItemData.Count; i++)
            {
                if (!items.IsEquipment(i)) continue;
                itemIds.Add(i);
                comboItem.Items.Add(Describe(i));
            }
            RefreshIPList();
            loading = false;

            this.Enabled = true;
            comboItem.SelectedIndex = (keep >= 0 && keep < comboItem.Items.Count) ? keep : 0;
        }

        public void Disable()
        {
            items = null; currentIndex = -1;
            comboItem.Items.Clear();
            this.Enabled = false;
        }

        private string Describe(int id)
        {
            return items.GetName(id) + "  (" + ItemData.SlotName(items.GetSlot(id)) + ")";
        }

        private void RefreshIPList()
        {
            bool wasLoading = loading;
            loading = true;
            int sel = comboIP.SelectedIndex;
            comboIP.Items.Clear();
            comboIP.Items.Add("(none)");
            for (int id = 1; id <= ip.Count; id++) comboIP.Items.Add(ip.GetName(id) + "  (" + id + ")");
            if (sel >= 0 && sel < comboIP.Items.Count) comboIP.SelectedIndex = sel;
            loading = wasLoading;
        }

        /// <summary>Re-reads IP names (they may have been renamed on the IP Moves tab).</summary>
        public void RefreshFromRom()
        {
            if (items == null) return;
            RefreshIPList();
            if (currentIndex >= 0) ShowItem(currentIndex);
        }

        private int UsedSlots()
        {
            int used = 0;
            for (int b = 0; b < 7; b++) if (numBonus[b].Value != 0) used++;
            if (comboIP.SelectedIndex > 0) used++;
            return used;
        }

        private void UpdateSlotLabel()
        {
            if (items == null || currentIndex < 0) return;
            int cap = items.PoolCapacity(itemIds[currentIndex]);
            labelSlots.Text = "Slots used: " + UsedSlots() + " of " + cap;
            labelSlots.ForeColor = UsedSlots() >= cap ? Color.DarkRed : SystemColors.ControlText;
        }

        private void PoolValueChanged(object sender, EventArgs e)
        {
            if (loading || items == null || currentIndex < 0) return;
            int cap = items.PoolCapacity(itemIds[currentIndex]);
            if (UsedSlots() > cap)
            {
                // Undo the change that went over the limit.
                loading = true;
                NumericUpDown n = sender as NumericUpDown;
                if (n != null) n.Value = 0;
                ComboBox c = sender as ComboBox;
                if (c != null) c.SelectedIndex = 0;
                loading = false;
                MessageBox.Show("This item has no free slot left (" + cap + " total).\n" +
                    "Set another bonus to 0 (or the IP move to none) first.",
                    "Equipment", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            UpdateSlotLabel();
        }

        public void CommitCurrent()
        {
            if (items == null || currentIndex < 0) return;
            int id = itemIds[currentIndex];

            items.SetName(id, textName.Text);
            items.SetPrice(id, (int)numPrice.Value);

            int mask = 0;
            for (int c = 0; c < 7; c++) if (checkEquip[c].Checked) mask |= (1 << c);
            items.SetEquipMask(id, mask);
            if (checkCave.Enabled) items.SetInCaveLists(id, checkCave.Checked);

            Dictionary<int, int> pool = new Dictionary<int, int>();
            for (int b = 0; b < 7; b++) pool[ItemData.FIELD_ATP + b] = (int)numBonus[b].Value;
            pool[ItemData.FIELD_IP] = comboIP.SelectedIndex < 0 ? 0 : comboIP.SelectedIndex;
            statusPanel.Commit();
            CommitElements(id);

            if (!items.SetPoolValues(id, pool))
            {
                MessageBox.Show("Bonuses for " + items.GetName(id) + " were not saved: too many values for its slots.",
                    "Equipment", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            loading = true;
            UiHelpers.RenameItem(comboItem, currentIndex, Describe(id));
            loading = false;
        }

        // ---------------- elements ----------------

        private static void FillElements(ComboBox cb, int value, int refValue)
        {
            List<int> vals = new List<int>(ScriptTools.ElementValues);
            if (value >= 0 && !vals.Contains(value)) vals.Add(value);
            if (refValue >= 0 && !vals.Contains(refValue)) vals.Add(refValue);
            cb.Items.Clear();
            foreach (int v in vals) cb.Items.Add(ScriptTools.DescribeElement(v));
            cb.Tag = vals;
            cb.SelectedIndex = value >= 0 ? vals.IndexOf(value) : -1;
        }

        private static void FillList(ComboBox cb, int[] values, string[] names, int value)
        {
            List<int> vals = new List<int>(values);
            cb.Items.Clear();
            for (int k = 0; k < values.Length; k++) cb.Items.Add(names[k]);
            if (value >= 0 && !vals.Contains(value)) { vals.Add(value); cb.Items.Add("(other: " + value.ToString("X2") + ")"); }
            cb.Tag = vals;
            cb.SelectedIndex = value >= 0 ? vals.IndexOf(value) : -1;
        }

        private static int Selected(ComboBox cb)
        {
            List<int> vals = cb.Tag as List<int>;
            if (vals == null || cb.SelectedIndex < 0 || cb.SelectedIndex >= vals.Count) return -1;
            return vals[cb.SelectedIndex];
        }

        private static object RefIndex(ComboBox cb, int refValue)
        {
            List<int> vals = cb.Tag as List<int>;
            if (vals == null || refValue < 0) return null;
            int k = vals.IndexOf(refValue);
            return k < 0 ? null : (object)k;
        }

        private static void Blank(ComboBox cb)
        {
            cb.Items.Clear();
            cb.Items.Add("(none)");
            cb.Tag = null;
            cb.SelectedIndex = 0;
            cb.Enabled = false;
            ChangeHighlighter.Set(cb, null);
        }

        private void ShowElements(int id, ItemData r)
        {
            byte[] d = items.RawData;
            EquipElements e = EquipElements.Find(items, id);
            EquipElements re = null;
            if (r != null)
            {
                object o = Reference.Try(delegate { return EquipElements.Find(r, id); });
                re = o as EquipElements;
            }
            byte[] rd = r == null ? null : r.RawData;

            checkFixed.Enabled = e.AttackModeOffset >= 0;
            checkFixed.Checked = e.IsFixedPower(d);
            UiHelpers.SetValueClamped(numFixed, e.IsFixedPower(d) ? e.GetFixedPower(d) : 120);
            numFixed.Enabled = checkFixed.Enabled && checkFixed.Checked;
            bool refMode = re != null && re.AttackModeOffset >= 0;
            ChangeHighlighter.Set(checkFixed, refMode && checkFixed.Enabled ? (object)re.IsFixedPower(rd) : null);
            ChangeHighlighter.Set(numFixed, refMode && checkFixed.Enabled && re.IsFixedPower(rd) ? (object)re.GetFixedPower(rd) : null);

            if (e.AttackOffset >= 0)
            {
                comboAttack.Enabled = true;
                int rv = re != null && re.AttackOffset >= 0 ? re.GetAttack(rd) : -1;
                FillElements(comboAttack, e.GetAttack(d), rv);
                ChangeHighlighter.Set(comboAttack, RefIndex(comboAttack, rv));
            }
            else Blank(comboAttack);

            alwaysExact = false;
            if (e.AlwaysOffset >= 0)
            {
                comboAlways.Enabled = true;
                labelAlways.Text = "Always:";
                int rv = re != null && re.AlwaysOffset >= 0 ? re.GetAlways(rd) : -1;
                FillList(comboAlways, EquipElements.AlwaysScripts, EquipElements.AlwaysNames, e.GetAlways(d));
                ChangeHighlighter.Set(comboAlways, RefIndex(comboAlways, rv));
            }
            else if (e.ExactElementOffset >= 0)
            {
                alwaysExact = true;
                comboAlways.Enabled = true;
                labelAlways.Text = "Halves:";
                int rv = re != null && re.ExactElementOffset >= 0 ? re.GetExact(rd) : -1;
                FillElements(comboAlways, e.GetExact(d), rv);
                ChangeHighlighter.Set(comboAlways, RefIndex(comboAlways, rv));
            }
            else
            {
                labelAlways.Text = "Always:";
                Blank(comboAlways);
            }

            if (e.GuardElementOffset >= 0)
            {
                comboGuardElement.Enabled = comboGuardAction.Enabled = comboGuardFlag.Enabled = true;
                bool hasRef = re != null && re.GuardElementOffset >= 0;
                int rv = hasRef ? re.GetGuardElement(rd) : -1;
                FillElements(comboGuardElement, e.GetGuardElement(d), rv);
                ChangeHighlighter.Set(comboGuardElement, RefIndex(comboGuardElement, rv));
                FillList(comboGuardAction, EquipElements.GuardActions, EquipElements.GuardActionNames, e.GetGuardAction(d));
                ChangeHighlighter.Set(comboGuardAction, RefIndex(comboGuardAction, hasRef ? re.GetGuardAction(rd) : -1));
                FillList(comboGuardFlag, EquipElements.GuardFlags, EquipElements.GuardFlagNames, e.GetGuardFlag(d));
                ChangeHighlighter.Set(comboGuardFlag, RefIndex(comboGuardFlag, hasRef ? re.GetGuardFlag(rd) : -1));
            }
            else
            {
                Blank(comboGuardElement); Blank(comboGuardAction); Blank(comboGuardFlag);
            }
        }

        private void CommitElements(int id)
        {
            byte[] d = items.RawData;
            EquipElements e = EquipElements.Find(items, id);
            if (checkFixed.Enabled) e.SetAttackMode(d, checkFixed.Checked, (int)numFixed.Value);
            if (comboAttack.Enabled) e.SetAttack(d, Selected(comboAttack));
            if (comboAlways.Enabled)
            {
                if (alwaysExact) e.SetExact(d, Selected(comboAlways));
                else e.SetAlways(d, Selected(comboAlways));
            }
            if (comboGuardElement.Enabled)
            {
                e.SetGuardElement(d, Selected(comboGuardElement));
                e.SetGuardAction(d, Selected(comboGuardAction));
                e.SetGuardFlag(d, Selected(comboGuardFlag));
            }
        }

        private void comboItem_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (loading || items == null) return;
            int newIndex = comboItem.SelectedIndex;
            if (newIndex == currentIndex || newIndex < 0) return;
            CommitCurrent();
            if (comboItem.SelectedIndex != newIndex)
            {
                loading = true; comboItem.SelectedIndex = newIndex; loading = false;
            }
            ShowItem(newIndex);
        }

        private void ShowItem(int index)
        {
            loading = true;
            currentIndex = index;
            int id = itemIds[index];

            textName.Text = items.GetName(id);
            UiHelpers.SetValueClamped(numPrice, items.GetPrice(id));

            int mask = items.GetEquipMask(id);
            for (int c = 0; c < 7; c++) checkEquip[c].Checked = ((mask >> c) & 1) != 0;

            SortedDictionary<int, int> f = items.GetFields(id);
            for (int b = 0; b < 7; b++)
            {
                int v;
                if (!f.TryGetValue(ItemData.FIELD_ATP + b, out v)) v = 0;
                UiHelpers.SetValueClamped(numBonus[b], v);
            }
            int ipId;
            if (!f.TryGetValue(ItemData.FIELD_IP, out ipId)) ipId = 0;
            // Never drop an ID we don't recognise: add placeholder entries up to it.
            while (ipId >= comboIP.Items.Count && comboIP.Items.Count < 1024)
                comboIP.Items.Add("(unknown)  (" + comboIP.Items.Count + ")");
            comboIP.SelectedIndex = (ipId >= 0 && ipId < comboIP.Items.Count) ? ipId : 0;

            // Highlight what differs from the reference ROM.
            ItemData r = Reference.Items;
            ShowElements(id, r);
            ChangeHighlighter.Set(textName, r == null ? null : Reference.Try(delegate { return r.GetName(id); }));
            ChangeHighlighter.Set(numPrice, r == null ? null : Reference.Try(delegate { return r.GetPrice(id); }));
            bool capable = items.CaveCapable(id);
            checkCave.Enabled = capable;
            checkCave.Checked = capable && items.GetInCaveLists(id);
            ChangeHighlighter.Set(checkCave, (r == null || !capable) ? null : Reference.Try(delegate { return r.GetInCaveLists(id); }));
            labelCave.Text = capable
                ? "Red chests hold equipment priced under 1000 x (floor + 1) (see Cave Loot). Blue chests come only from the Cave Loot tab\'s Blue chests table."
                : "This item can't be in the cave's random equipment lists (it's a key item, Iris treasure or not equipment).";
            object refMask = r == null ? null : Reference.Try(delegate { return r.GetEquipMask(id); });
            for (int c = 0; c < 7; c++)
                ChangeHighlighter.Set(checkEquip[c], refMask == null ? null : (object)((((int)refMask >> c) & 1) != 0));
            SortedDictionary<int, int> rf = r == null ? null : (SortedDictionary<int, int>)Reference.Try(delegate { return r.GetFields(id); });
            for (int b = 0; b < 7; b++)
            {
                int v;
                ChangeHighlighter.Set(numBonus[b], rf == null ? null : (object)(rf.TryGetValue(ItemData.FIELD_ATP + b, out v) ? v : 0));
            }
            int refIp;
            ChangeHighlighter.Set(comboIP, rf == null ? null : (object)(rf.TryGetValue(ItemData.FIELD_IP, out refIp) ? refIp : 0));

            statusPanel.Clear();
            statusPanel.AddEffects(items.RawData, items.GetStatusEffects(id, ItemData.FIELD_WEAPON_SCRIPT), "On hit:");
            statusPanel.AddEffects(items.RawData, items.GetStatusEffects(id, ItemData.FIELD_ARMOR_SCRIPT), "Worn:");
            statusPanel.Build();

            labelInfo.Text = "Item ID: " + id + " (" + id.ToString("X3") + ")    Slot: " +
                ItemData.SlotName(items.GetSlot(id)) + "    Offset: " + items.RecordOffset(id).ToString("X") +
                "    Flags: " + items.FixedBytesHex(id);

            loading = false;
            UpdateSlotLabel();
        }
    }
}
