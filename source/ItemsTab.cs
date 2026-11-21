using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

using com.lufiaIIEditor.RomInfo;

namespace com.lufiaIIEditor.UI
{
    /// <summary>
    /// "Items" tab: everything that isn't worn - consumables, key items and dungeon tools.
    /// Name (12 letters), price, and the numbers inside the item's use effects
    /// (heal amounts, cure chances, damage...).
    /// </summary>
    class ItemsTab : TabPage
    {
        private ItemData items;
        private List<int> itemIds = new List<int>();
        private int currentIndex = -1;
        private bool loading = false;

        private ComboBox comboItem;
        private Label labelInfo;
        private TextBox textName;
        private NumericUpDown numPrice;
        private Panel panelEffects;
        private Label labelEffectsNote;
        private List<ScriptParam> currentParams = new List<ScriptParam>();
        private List<NumericUpDown> paramBoxes = new List<NumericUpDown>();

        public ItemsTab()
        {
            this.Text = "Items";
            this.UseVisualStyleBackColor = true;
            Build();
            this.Enabled = false;
        }

        private void Build()
        {
            Controls.Add(UiHelpers.MakeLabel("Item:", 12, 15));
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

            GroupBox gEffects = UiHelpers.MakeGroup("Effects", 12, 110, 640, 330);
            Controls.Add(UiHelpers.Anchor(this, gEffects, AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right));
            panelEffects = UiHelpers.Anchor(this, new Panel(), AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right);
            panelEffects.Location = new Point(10, 20);
            panelEffects.Size = new Size(620, 260);
            panelEffects.AutoScroll = true;
            gEffects.Controls.Add(panelEffects);
            labelEffectsNote = UiHelpers.Anchor(this, UiHelpers.MakeNote("", 10, 290), AnchorStyles.Bottom | AnchorStyles.Left);
            gEffects.Controls.Add(labelEffectsNote);
        }

        public void LoadRom(ItemData itemData)
        {
            int keep = comboItem.SelectedIndex;
            items = itemData;
            currentIndex = -1;

            loading = true;
            comboItem.Items.Clear();
            itemIds.Clear();
            for (int i = 1; i < ItemData.Count; i++)   // skip 0 "No equip"
            {
                if (items.IsEquipment(i)) continue;
                itemIds.Add(i);
                comboItem.Items.Add(Describe(i));
            }
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
            string kind = items.GetSlot(id) == 0x40 ? "  (dungeon tool)" : "";
            return items.GetName(id) + kind;
        }

        public void CommitCurrent()
        {
            if (items == null || currentIndex < 0) return;
            int id = itemIds[currentIndex];
            items.SetName(id, textName.Text);
            items.SetPrice(id, (int)numPrice.Value);
            for (int k = 0; k < currentParams.Count; k++)
                items.SetParam(currentParams[k], (int)paramBoxes[k].Value);

            loading = true;
            UiHelpers.RenameItem(comboItem, currentIndex, Describe(id));
            loading = false;
        }

        public void RefreshView() { if (items != null && currentIndex >= 0) ShowItem(currentIndex); }

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

            labelInfo.Text = "Item ID: " + id + " (" + id.ToString("X3") + ")    Offset: " +
                items.RecordOffset(id).ToString("X") + "    Flags: " + items.FixedBytesHex(id);

            ItemData r = Reference.Items;
            ChangeHighlighter.Set(textName, r == null ? null : Reference.Try(delegate { return r.GetName(id); }));
            ChangeHighlighter.Set(numPrice, r == null ? null : Reference.Try(delegate { return r.GetPrice(id); }));

            panelEffects.SuspendLayout();
            UiHelpers.ClearAndDispose(panelEffects);
            paramBoxes.Clear();
            bool complete;
            currentParams = items.GetEffectParams(id, out complete);
            for (int k = 0; k < currentParams.Count; k++)
            {
                ScriptParam p = currentParams[k];
                panelEffects.Controls.Add(UiHelpers.MakeLabel(p.Label, 5, 8 + k * 30));
                NumericUpDown n = UiHelpers.MakeNumeric(400, 5 + k * 30, 0, p.Max);
                UiHelpers.SetValueClamped(n, items.GetParam(p));
                panelEffects.Controls.Add(n);
                int ro = Reference.Shift(p.Offset);
                ChangeHighlighter.Set(n, !Reference.InRange(ro, p.Size) ? null :
                    (object)(p.Size == 1 ? Reference.Data[ro] : (Reference.Data[ro] | (Reference.Data[ro + 1] << 8))));
                paramBoxes.Add(n);
            }
            panelEffects.ResumeLayout();

            if (currentParams.Count == 0)
                labelEffectsNote.Text = complete ? "This item has no editable effect values." :
                    "This item's effects use script commands that aren't decoded yet, so they're left unchanged.";
            else
                labelEffectsNote.Text = complete ? "\"Random extra\" is added on top of the amount at random." :
                    "Some of this item's effects aren't decoded yet; only the values shown here can be changed.";

            loading = false;
        }
    }
}
