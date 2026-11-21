using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

using com.lufiaIIEditor.RomInfo;

namespace com.lufiaIIEditor.UI
{
    /// <summary>
    /// "Party & EXP" tab: what each character starts with in a new game (join level, EXP,
    /// equipment, spells), the starting inventory, the Ancient Cave start level, and the
    /// experience curve (base value and growth per 8-level band), with the resulting EXP table.
    /// </summary>
    class PartyTab : TabPage
    {
        private PartyData party, partyRef;
        private CharacterData chars;
        private ItemData items;
        private string[] spellNames;
        private int current = -1;
        private bool loading = false, dirty = false;

        private ComboBox comboChar;
        private Label labelInfo;
        private NumericUpDown numLevel, numExp;
        private CheckBox checkMatch;
        private Label labelExpRange;
        private ComboBox[] comboEquip = new ComboBox[7];
        private Label[] labelEquipWarn = new Label[7];
        private CheckedListBox listSpells;
        private Label labelSpellNote, labelSpace;

        private ListBox listInv;
        private ComboBox comboInvItem;
        private NumericUpDown numInvQty;
        private Button buttonInvAdd, buttonInvSet, buttonInvRemove;

        private NumericUpDown numCaveLevel;
        private Label labelCave;

        private NumericUpDown numExpBase;
        private NumericUpDown[] numRate = new NumericUpDown[13];
        private Label[] labelRate = new Label[13];
        private CheckBox checkSameCurve;
        private ListBox listExp;
        private Label labelExpTotal;

        private static readonly Font ListFont = new Font(FontFamily.GenericMonospace, 8.5f);

        public PartyTab()
        {
            this.Text = "Party & EXP";
            this.UseVisualStyleBackColor = true;
            Build();
            this.Enabled = false;
        }

        // ---------------- layout ----------------

        private void Build()
        {
            Controls.Add(UiHelpers.MakeLabel("Character:", 12, 15));
            comboChar = UiHelpers.MakeDropDown(85, 12, 150);
            comboChar.SelectedIndexChanged += new EventHandler(comboChar_SelectedIndexChanged);
            Controls.Add(comboChar);
            labelInfo = UiHelpers.MakeNote("", 245, 15);
            Controls.Add(labelInfo);

            GroupBox gStart = UiHelpers.Anchor(this, UiHelpers.MakeGroup("Starts a new game with", 12, 40, 470, 468),
                AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left);
            Controls.Add(gStart);
            gStart.Controls.Add(UiHelpers.MakeLabel("Joins at level", 10, 23));
            numLevel = UiHelpers.MakeNumeric(95, 20, 1, 97);
            numLevel.Size = new Size(50, 20);
            numLevel.ValueChanged += new EventHandler(LevelOrExpChanged);
            gStart.Controls.Add(numLevel);
            gStart.Controls.Add(UiHelpers.MakeLabel("EXP", 160, 23));
            numExp = UiHelpers.MakeNumeric(190, 20, 0, 9999999);
            numExp.Size = new Size(85, 20);
            numExp.ThousandsSeparator = true;
            numExp.ValueChanged += new EventHandler(LevelOrExpChanged);
            gStart.Controls.Add(numExp);
            checkMatch = new CheckBox();
            checkMatch.Text = "match EXP to level";
            checkMatch.Location = new Point(290, 21);
            checkMatch.AutoSize = true;
            checkMatch.Checked = true;
            checkMatch.CheckedChanged += new EventHandler(LevelOrExpChanged);
            gStart.Controls.Add(checkMatch);
            labelExpRange = UiHelpers.MakeNote("", 10, 46);
            gStart.Controls.Add(labelExpRange);

            for (int k = 0; k < 7; k++)
            {
                int y = 70 + k * 26;
                gStart.Controls.Add(UiHelpers.MakeLabel(PartyData.SlotNames[k], 10, y + 3));
                comboEquip[k] = UiHelpers.MakeDropDown(70, y, 200);
                comboEquip[k].SelectedIndexChanged += new EventHandler(EquipChanged);
                gStart.Controls.Add(comboEquip[k]);
                labelEquipWarn[k] = UiHelpers.MakeLabel("", 278, y + 3);
                labelEquipWarn[k].ForeColor = Color.DarkRed;
                gStart.Controls.Add(labelEquipWarn[k]);
            }

            gStart.Controls.Add(UiHelpers.MakeLabel("Spells known", 10, 256));
            listSpells = UiHelpers.Anchor(this, new CheckedListBox(), AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left);
            listSpells.Location = new Point(10, 274);
            listSpells.Size = new Size(200, 168);
            listSpells.CheckOnClick = true;
            listSpells.IntegralHeight = false;
            listSpells.ItemCheck += new ItemCheckEventHandler(listSpells_ItemCheck);
            gStart.Controls.Add(listSpells);
            labelSpellNote = UiHelpers.MakeNote("", 220, 274);
            labelSpellNote.AutoSize = false;
            labelSpellNote.Size = new Size(240, 120);
            gStart.Controls.Add(labelSpellNote);
            labelSpace = UiHelpers.Anchor(this, UiHelpers.MakeNote("", 10, 447), AnchorStyles.Bottom | AnchorStyles.Left);
            gStart.Controls.Add(labelSpace);

            // Starting inventory
            GroupBox gInv = UiHelpers.MakeGroup("Starting inventory (whole party)", 492, 8, 240, 198);
            Controls.Add(gInv);
            listInv = new ListBox();
            listInv.Location = new Point(10, 20);
            listInv.Size = new Size(220, 92);
            listInv.IntegralHeight = false;
            listInv.SelectedIndexChanged += delegate(object s, EventArgs e) { if (!loading) SyncInv(); };
            gInv.Controls.Add(listInv);
            comboInvItem = UiHelpers.MakeDropDown(10, 116, 220);
            gInv.Controls.Add(comboInvItem);
            gInv.Controls.Add(UiHelpers.MakeLabel("Quantity", 10, 145));
            numInvQty = UiHelpers.MakeNumeric(70, 142, 1, 99);
            numInvQty.Size = new Size(50, 20);
            gInv.Controls.Add(numInvQty);
            buttonInvAdd = new Button(); buttonInvAdd.Text = "Add"; buttonInvAdd.Location = new Point(10, 168); buttonInvAdd.Size = new Size(66, 24);
            buttonInvSet = new Button(); buttonInvSet.Text = "Replace"; buttonInvSet.Location = new Point(82, 168); buttonInvSet.Size = new Size(70, 24);
            buttonInvRemove = new Button(); buttonInvRemove.Text = "Remove"; buttonInvRemove.Location = new Point(158, 168); buttonInvRemove.Size = new Size(72, 24);
            buttonInvAdd.Click += delegate(object s, EventArgs e) { InvAdd(); };
            buttonInvSet.Click += delegate(object s, EventArgs e) { InvSet(); };
            buttonInvRemove.Click += delegate(object s, EventArgs e) { InvRemove(); };
            gInv.Controls.Add(buttonInvAdd); gInv.Controls.Add(buttonInvSet); gInv.Controls.Add(buttonInvRemove);

            // Ancient Cave
            GroupBox gCave = UiHelpers.MakeGroup("Ancient Cave start level", 740, 8, 237, 198);
            Controls.Add(gCave);
            gCave.Controls.Add(UiHelpers.MakeLabel("This character:", 10, 25));
            numCaveLevel = UiHelpers.MakeNumeric(110, 22, 1, 97);
            numCaveLevel.Size = new Size(50, 20);
            numCaveLevel.ValueChanged += new EventHandler(numCaveLevel_ValueChanged);
            gCave.Controls.Add(numCaveLevel);
            Button allCave = new Button();
            allCave.Text = "Use for all characters";
            allCave.Location = new Point(10, 50);
            allCave.Size = new Size(150, 24);
            allCave.Click += new EventHandler(allCave_Click);
            gCave.Controls.Add(allCave);
            labelCave = UiHelpers.MakeNote("", 10, 82);
            labelCave.AutoSize = false;
            labelCave.Size = new Size(220, 110);
            gCave.Controls.Add(labelCave);

            // Experience curve
            GroupBox gExp = UiHelpers.Anchor(this, UiHelpers.MakeGroup("Experience curve", 492, 212, 485, 296),
                AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right);
            Controls.Add(gExp);
            gExp.Controls.Add(UiHelpers.MakeLabel("Start value", 10, 23));
            numExpBase = UiHelpers.MakeNumeric(80, 20, 1, 255);
            numExpBase.Size = new Size(50, 20);
            numExpBase.ValueChanged += new EventHandler(CurveChanged);
            gExp.Controls.Add(numExpBase);
            checkSameCurve = new CheckBox();
            checkSameCurve.Text = "same for everyone";
            checkSameCurve.Location = new Point(140, 22);
            checkSameCurve.AutoSize = true;
            gExp.Controls.Add(checkSameCurve);
            gExp.Controls.Add(UiHelpers.MakeNote("EXP needed grows per level by:", 10, 46));
            for (int r = 0; r < 13; r++)
            {
                int y = 64 + r * 17;
                int from = r == 0 ? 1 : r * 8, to = Math.Min(r * 8 + 7, 98);
                gExp.Controls.Add(UiHelpers.MakeLabel("Lv " + from + "-" + to, 10, y + 1));
                numRate[r] = UiHelpers.MakeNumeric(80, y, 0, 255);
                numRate[r].Size = new Size(50, 16);
                numRate[r].ValueChanged += new EventHandler(CurveChanged);
                gExp.Controls.Add(numRate[r]);
                labelRate[r] = UiHelpers.MakeNote("", 135, y + 1);
                gExp.Controls.Add(labelRate[r]);
            }
            listExp = UiHelpers.Anchor(this, new ListBox(), AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right);
            listExp.Location = new Point(250, 20);
            listExp.Size = new Size(225, 250);
            listExp.IntegralHeight = false;
            listExp.Font = ListFont;
            gExp.Controls.Add(listExp);
            labelExpTotal = UiHelpers.Anchor(this, UiHelpers.MakeNote("", 250, 274), AnchorStyles.Bottom | AnchorStyles.Left);
            gExp.Controls.Add(labelExpTotal);
        }

        // ---------------- load / commit ----------------

        private bool hasHeader;

        public void LoadRom(PartyData p, CharacterData c, ItemData i, Rom romData)
        {
            party = p; chars = c; items = i; rom = romData; spellNames = romData.SpellNames; hasHeader = romData.HasHeader; dirty = false;
            partyRef = null;
            if (Reference.Available)
            {
                PartyData r = new PartyData(Reference.Data, Reference.HasHeader);
                if (r.Load() == null) partyRef = r;
            }
            int keep = current;
            current = -1;
            loading = true;
            comboChar.Items.Clear();
            for (int k = 0; k < PartyData.Characters; k++) comboChar.Items.Add(party.NameOf(k));
            foreach (ComboBox cb in comboEquip) cb.Items.Clear();
            comboInvItem.Items.Clear();
            for (int id = 0; id < ItemData.Count; id++)
            {
                string label = id == 0 ? "(none)" : items.GetName(id).Trim() + "  (" + id.ToString("X3") + ")";
                foreach (ComboBox cb in comboEquip) cb.Items.Add(label);
                comboInvItem.Items.Add(label);
            }
            listSpells.Items.Clear();
            for (int s = 0; s < spellNames.Length; s++) listSpells.Items.Add(spellNames[s].Trim());
            loading = false;
            this.Enabled = true;
            FillInventory(0);
            bool same = true;
            for (int k = 1; k < PartyData.Characters; k++)
            {
                if (chars.GetExpBase(k) != chars.GetExpBase(0)) same = false;
                for (int r = 0; r < 13; r++) if (chars.GetExpRate(k, r) != chars.GetExpRate(0, r)) same = false;
            }
            checkSameCurve.Checked = same;
            comboChar.SelectedIndex = keep >= 0 && keep < PartyData.Characters ? keep : 0;
        }

        public void Disable()
        {
            party = null; current = -1;
            this.Enabled = false;
        }

        public void CommitCurrent()
        {
            if (party == null) return;
            if (dirty && party.Write()) dirty = false;
        }

        public void RefreshView()
        {
            if (party == null) return;
            CommitCurrent();
            PartyData fresh = new PartyData(chars.RawData, hasHeader);
            if (fresh.Load() == null) LoadRom(fresh, chars, items, rom);
        }

        // ---------------- character ----------------

        private void comboChar_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (loading || party == null || comboChar.SelectedIndex < 0) return;
            current = comboChar.SelectedIndex;
            ShowCharacter();
        }

        private void ShowCharacter()
        {
            PartyData.Member m = party.Members[current];
            PartyData.Member o = partyRef != null ? partyRef.Members[current] : null;
            loading = true;
            UiHelpers.SetValueClamped(numLevel, m.Level);
            UiHelpers.SetValueClamped(numExp, m.Exp);
            ChangeHighlighter.Set(numLevel, o == null ? null : (object)o.Level);
            ChangeHighlighter.Set(numExp, o == null ? null : (object)o.Exp);
            checkMatch.Checked = m.Exp == chars.MinExpForLevel(current, m.Level);
            for (int k = 0; k < 7; k++)
            {
                int id = m.Equip[k] & 0x1FF;
                comboEquip[k].SelectedIndex = id < comboEquip[k].Items.Count ? id : 0;
                ChangeHighlighter.Set(comboEquip[k], o == null ? null : (object)(o.Equip[k] & 0x1FF));
            }
            for (int s = 0; s < listSpells.Items.Count; s++) listSpells.SetItemChecked(s, m.Spells.Contains(s));
            numCaveLevel.Enabled = chars.HasCave;
            if (chars.HasCave) UiHelpers.SetValueClamped(numCaveLevel, chars.GetCaveLevel(current));
            CharacterData cr = Reference.Characters;
            ChangeHighlighter.Set(numCaveLevel, cr != null && cr.HasCave ? (object)cr.GetCaveLevel(current) : null);
            UiHelpers.SetValueClamped(numExpBase, chars.GetExpBase(current));
            ChangeHighlighter.Set(numExpBase, cr == null ? null : (object)cr.GetExpBase(current));
            for (int r = 0; r < 13; r++)
            {
                UiHelpers.SetValueClamped(numRate[r], chars.GetExpRate(current, r));
                ChangeHighlighter.Set(numRate[r], cr == null ? null : (object)cr.GetExpRate(current, r));
            }
            loading = false;
            labelInfo.Text = "Setup data: " + party.StartOffset.ToString("X") + "-" + (party.EndOffset - 1).ToString("X");
            UpdateExpInfo();
            UpdateEquipWarnings();
            UpdateSpellNote();
            UpdateSpace();
            UpdateCurve();
            UpdateCave();
        }

        private void UpdateExpInfo()
        {
            PartyData.Member m = party.Members[current];
            long lo = chars.MinExpForLevel(current, m.Level), hi = m.Level < 98 ? chars.ExpTable(current)[m.Level] - 1 : CharacterData.ExpCap;
            labelExpRange.Text = "Level " + m.Level + " covers " + lo.ToString("N0") + " - " + hi.ToString("N0") + " EXP" +
                (m.Exp < lo || m.Exp > hi ? "   (EXP doesn't match the level!)" : "");
            labelExpRange.ForeColor = m.Exp < lo || m.Exp > hi ? Color.DarkRed : SystemColors.GrayText;
            numExp.Enabled = !checkMatch.Checked;
        }

        private void LevelOrExpChanged(object sender, EventArgs e)
        {
            if (loading || current < 0) return;
            PartyData.Member m = party.Members[current];
            m.Level = (int)numLevel.Value;
            if (checkMatch.Checked)
            {
                m.Exp = (int)chars.MinExpForLevel(current, m.Level);
                loading = true; UiHelpers.SetValueClamped(numExp, m.Exp); loading = false;
            }
            else m.Exp = (int)numExp.Value;
            dirty = true;
            UpdateExpInfo();
        }

        private void EquipChanged(object sender, EventArgs e)
        {
            if (loading || current < 0) return;
            PartyData.Member m = party.Members[current];
            for (int k = 0; k < 7; k++)
                if (sender == comboEquip[k] && comboEquip[k].SelectedIndex >= 0) m.Equip[k] = comboEquip[k].SelectedIndex;
            dirty = true;
            UpdateEquipWarnings();
        }

        private void UpdateEquipWarnings()
        {
            PartyData.Member m = party.Members[current];
            for (int k = 0; k < 7; k++)
            {
                int id = m.Equip[k] & 0x1FF;
                string w = "";
                if (id > 0 && id < ItemData.Count)
                {
                    if ((items.GetSlot(id) & (1 << k)) == 0) w = "not a " + PartyData.SlotNames[k].ToLower();
                    else if (k < 6 && ((items.GetEquipMask(id) >> current) & 1) == 0) w = "can't equip it";
                }
                labelEquipWarn[k].Text = w;
            }
        }

        // ---------------- spells ----------------

        private void listSpells_ItemCheck(object sender, ItemCheckEventArgs e)
        {
            if (loading || current < 0) return;
            PartyData.Member m = party.Members[current];
            if (e.NewValue == CheckState.Checked)
            {
                if (party.Free < 1) { e.NewValue = CheckState.Unchecked; labelSpellNote.Text = "No space left: remove a spell or an inventory item first."; return; }
                if (!m.Spells.Contains(e.Index)) m.Spells.Add(e.Index);
            }
            else m.Spells.Remove(e.Index);
            dirty = true;
            BeginInvoke(new MethodInvoker(delegate { UpdateSpellNote(); UpdateSpace(); }));
        }

        private void UpdateSpellNote()
        {
            PartyData.Member m = party.Members[current];
            List<string> cant = new List<string>();
            foreach (int s in m.Spells) if (!CanLearn(s, current)) cant.Add(spellNames[s].Trim());
            labelSpellNote.Text = "Spells this character knows from the start (each costs 1 byte of setup space)." +
                (cant.Count > 0 ? "\n\nNote: normally can't learn " + string.Join(", ", cant.ToArray()) + " (see Spell Effects)." : "");
        }

        private bool CanLearn(int spell, int c)
        {
            try
            {
                int rec = int.Parse(rom.getSpellOffset(spell), System.Globalization.NumberStyles.HexNumber);
                return ((chars.RawData[rec + 0x0A] >> c) & 1) != 0;
            }
            catch (Exception) { return true; }
        }

        private Rom rom;

        private void UpdateSpace()
        {
            labelSpace.Text = "Setup space (spells + inventory): " + party.Used + " of " + party.Capacity + " bytes used, " + party.Free + " free";
            buttonInvAdd.Enabled = party.Free >= 2;
        }

        // ---------------- inventory ----------------

        private void FillInventory(int select)
        {
            bool was = loading;
            loading = true;
            listInv.Items.Clear();
            foreach (int w in party.Inventory)
            {
                int id = w & 0x1FF, qty = w >> 9;
                listInv.Items.Add((id < ItemData.Count ? items.GetName(id).Trim() : "#" + id) + "  x" + qty);
            }
            if (party.Inventory.Count > 0) listInv.SelectedIndex = Math.Max(0, Math.Min(select, party.Inventory.Count - 1));
            loading = was;
            SyncInv();
        }

        private void SyncInv()
        {
            int i = listInv.SelectedIndex;
            if (i >= 0 && i < party.Inventory.Count)
            {
                bool was = loading; loading = true;
                comboInvItem.SelectedIndex = party.Inventory[i] & 0x1FF;
                UiHelpers.SetValueClamped(numInvQty, Math.Max(1, party.Inventory[i] >> 9));
                loading = was;
            }
            buttonInvSet.Enabled = buttonInvRemove.Enabled = i >= 0;
            if (current >= 0) UpdateSpace();
        }

        private int InvWord() { return (comboInvItem.SelectedIndex & 0x1FF) | ((int)numInvQty.Value << 9); }

        private void InvAdd()
        {
            if (comboInvItem.SelectedIndex <= 0 || party.Free < 2) return;
            party.Inventory.Add(InvWord());
            dirty = true;
            FillInventory(party.Inventory.Count - 1);
        }

        private void InvSet()
        {
            int i = listInv.SelectedIndex;
            if (i < 0 || comboInvItem.SelectedIndex <= 0) return;
            party.Inventory[i] = InvWord();
            dirty = true;
            FillInventory(i);
        }

        private void InvRemove()
        {
            int i = listInv.SelectedIndex;
            if (i < 0) return;
            party.Inventory.RemoveAt(i);
            dirty = true;
            FillInventory(i);
        }

        // ---------------- Ancient Cave ----------------

        private void numCaveLevel_ValueChanged(object sender, EventArgs e)
        {
            if (loading || current < 0 || !chars.HasCave) return;
            chars.SetCaveLevel(current, (int)numCaveLevel.Value);
            UpdateCave();
        }

        private void allCave_Click(object sender, EventArgs e)
        {
            if (current < 0 || !chars.HasCave) return;
            for (int c = 0; c < PartyData.Characters; c++) chars.SetCaveLevel(c, (int)numCaveLevel.Value);
            UpdateCave();
        }

        private void UpdateCave()
        {
            if (!chars.HasCave) { labelCave.Text = "Ancient Cave records not found."; return; }
            labelCave.Text = "Everyone starts the cave at this level with no equipment; stats come from the growth " +
                "curve plus the cave bonus (Characters tab). EXP is set to match: " + chars.GetCaveExp(current).ToString("N0") + ".";
        }

        // ---------------- experience curve ----------------

        private void CurveChanged(object sender, EventArgs e)
        {
            if (loading || current < 0) return;
            int from = checkSameCurve.Checked ? 0 : current, to = checkSameCurve.Checked ? PartyData.Characters - 1 : current;
            for (int c = from; c <= to; c++)
            {
                chars.SetExpBase(c, (int)numExpBase.Value);
                for (int r = 0; r < 13; r++) chars.SetExpRate(c, r, (int)numRate[r].Value);
                // Keep EXP that matched its level matched under the new curve.
                if (chars.HasCave) chars.SetCaveLevel(c, chars.GetCaveLevel(c));
            }
            if (checkMatch.Checked) LevelOrExpChanged(null, EventArgs.Empty);
            UpdateCurve();
            UpdateExpInfo();
            UpdateCave();
        }

        private void UpdateCurve()
        {
            for (int r = 0; r < 13; r++)
                labelRate[r].Text = "+" + ((numRate[r].Value + 1) * 100 / 256).ToString("0.0") + "% per level";
            long[] t = chars.ExpTable(current);
            CharacterData cr = Reference.Characters;
            long[] o = cr != null ? cr.ExpTable(current) : null;
            listExp.BeginUpdate();
            listExp.Items.Clear();
            for (int lv = 1; lv < CharacterData.MaxLevel; lv++)
            {
                string line = "Lv " + lv.ToString().PadLeft(2) + " -> " + (lv + 1).ToString().PadLeft(2) + ": " + t[lv].ToString("N0").PadLeft(9);
                if (o != null && o[lv] != t[lv]) line += "  (was " + o[lv].ToString("N0") + ")";
                listExp.Items.Add(line);
            }
            listExp.EndUpdate();
            labelExpTotal.Text = "Total EXP to reach level 99: " + CharacterData.ExpCap.ToString("N0") + " (the game's cap from level 98)";
        }
    }
}
