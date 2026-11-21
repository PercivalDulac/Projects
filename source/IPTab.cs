using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

using com.lufiaIIEditor.RomInfo;

namespace com.lufiaIIEditor.UI
{
    /// <summary>
    /// "IP Moves" tab. Left: name (never longer than its space), IP cost, and - behind an
    /// "advanced" checkbox - effect script number, animation, type and target bytes.
    /// Right: what the move's effect script does (damage, element, healing, stat changes,
    /// spell cast, formula numbers, statuses). Effect scripts can be shared between moves;
    /// the tab lists which moves an edit will affect.
    /// </summary>
    class IPTab : TabPage
    {
        private IPData ip;
        private string[] spellNames = new string[0];
        private int currentId = -1;
        private int currentEffect = -1;
        private bool loading = false;

        private ComboBox comboIP;
        private Label labelInfo;
        private TextBox textName;
        private Label labelNameLimit;
        private NumericUpDown numCost;
        private Label labelCostPct;
        private CheckBox checkAdvanced;
        private NumericUpDown numEffect;
        private NumericUpDown numAnim;
        private NumericUpDown numType;
        private NumericUpDown numTarget;

        private Label labelShared;
        private EffectEditorPanel effectPanel;
        private ItemData items;
        private Label labelGuard;

        public IPTab()
        {
            this.Text = "IP Moves";
            this.UseVisualStyleBackColor = true;
            Build();
            this.Enabled = false;
        }

        private void Build()
        {
            Controls.Add(UiHelpers.MakeLabel("IP move:", 12, 15));
            comboIP = UiHelpers.MakeDropDown(80, 12, 230);
            comboIP.SelectedIndexChanged += new EventHandler(comboIP_SelectedIndexChanged);
            Controls.Add(comboIP);

            labelInfo = UiHelpers.MakeLabel("", 320, 15);
            Controls.Add(labelInfo);

            Controls.Add(UiHelpers.MakeLabel("Name:", 12, 47));
            textName = UiHelpers.MakeNameBox(80, 44, 1);
            textName.Size = new Size(130, 20);
            Controls.Add(textName);
            labelNameLimit = UiHelpers.MakeNote("", 215, 47);
            Controls.Add(labelNameLimit);

            Controls.Add(UiHelpers.MakeLabel("IP cost:", 12, 80));
            numCost = UiHelpers.MakeNumeric(80, 77, 0, 255);
            numCost.ValueChanged += new EventHandler(numCost_ValueChanged);
            Controls.Add(numCost);
            labelCostPct = UiHelpers.MakeNote("", 155, 80);
            Controls.Add(labelCostPct);

            GroupBox gAdv = UiHelpers.MakeGroup("Advanced", 12, 112, 400, 200);
            Controls.Add(gAdv);
            checkAdvanced = new CheckBox();
            checkAdvanced.Text = "Unlock (wrong values can break the move)";
            checkAdvanced.Location = new Point(12, 22);
            checkAdvanced.AutoSize = true;
            checkAdvanced.CheckedChanged += new EventHandler(checkAdvanced_CheckedChanged);
            gAdv.Controls.Add(checkAdvanced);

            gAdv.Controls.Add(UiHelpers.MakeLabel("Effect script", 12, 58));
            numEffect = UiHelpers.MakeNumeric(110, 55, 0, 1023);
            numEffect.ValueChanged += new EventHandler(numEffect_ValueChanged);
            gAdv.Controls.Add(numEffect);
            gAdv.Controls.Add(UiHelpers.MakeNote("which effect the move runs", 185, 58));

            gAdv.Controls.Add(UiHelpers.MakeLabel("Animation", 12, 90));
            numAnim = UiHelpers.MakeNumeric(110, 87, 0, 255);
            numAnim.Hexadecimal = true;
            gAdv.Controls.Add(numAnim);
            gAdv.Controls.Add(UiHelpers.MakeNote("hex; attack type / animation", 185, 90));

            gAdv.Controls.Add(UiHelpers.MakeLabel("Type", 12, 122));
            numType = UiHelpers.MakeNumeric(110, 119, 0, 255);
            numType.Hexadecimal = true;
            gAdv.Controls.Add(numType);
            gAdv.Controls.Add(UiHelpers.MakeNote("hex; 01 attack-style, 02 spell-style", 185, 122));

            gAdv.Controls.Add(UiHelpers.MakeLabel("Target", 12, 154));
            numTarget = UiHelpers.MakeNumeric(110, 151, 0, 255);
            numTarget.Hexadecimal = true;
            gAdv.Controls.Add(numTarget);
            gAdv.Controls.Add(UiHelpers.MakeNote("hex; raw targeting flags", 185, 154));

            SetAdvancedEnabled(false);

            Controls.Add(UiHelpers.MakeNote(
                "Names are packed back to back, so each name can only\n" +
                "use the letters it already has (shorter is fine).\n" +
                "Which item carries which IP move: Equipment tab.", 12, 325));

            labelGuard = UiHelpers.MakeNote("", 12, 375);
            labelGuard.AutoSize = false;
            labelGuard.Size = new Size(400, 130);
            Controls.Add(UiHelpers.Anchor(this, labelGuard, AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left));

            // Effect side
            GroupBox gFx = UiHelpers.MakeGroup("What the move does (effect script)", 425, 40, 550, 465);
            Controls.Add(UiHelpers.Anchor(this, gFx, AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right));
            labelShared = UiHelpers.MakeLabel("", 10, 20);
            labelShared.AutoSize = false;
            labelShared.Size = new Size(530, 30);
            gFx.Controls.Add(labelShared);

            effectPanel = UiHelpers.Anchor(this, new EffectEditorPanel(535, 405), AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right);
            effectPanel.Location = new Point(8, 52);
            gFx.Controls.Add(effectPanel);
        }

        private void SetAdvancedEnabled(bool on)
        {
            numEffect.Enabled = on;
            numAnim.Enabled = on;
            numType.Enabled = on;
            numTarget.Enabled = on;
        }

        private void checkAdvanced_CheckedChanged(object sender, EventArgs e)
        {
            SetAdvancedEnabled(checkAdvanced.Checked);
        }

        private void numCost_ValueChanged(object sender, EventArgs e)
        {
            labelCostPct.Text = "= " + ((int)numCost.Value * 100 / 255) + "% of the IP gauge";
        }

        private void numEffect_ValueChanged(object sender, EventArgs e)
        {
            if (loading || ip == null || currentId < 1) return;
            // Save the old effect's values, then show the newly chosen effect.
            CommitEffect();
            ShowEffect((int)numEffect.Value);
        }

        public void LoadRom(IPData ipData, string[] spells)
        {
            int keep = comboIP.SelectedIndex;
            ip = ipData;
            spellNames = spells != null ? spells : new string[0];
            currentId = -1;
            currentEffect = -1;
            numEffect.Maximum = Math.Max(0, ip.EffectCount - 1);
            loading = true;
            comboIP.Items.Clear();
            for (int id = 1; id <= ip.Count; id++) comboIP.Items.Add(Describe(id));
            loading = false;
            this.Enabled = true;
            comboIP.SelectedIndex = (keep >= 0 && keep < comboIP.Items.Count) ? keep : 0;
        }

        /// <summary>Equipment data, to list what a guard / mirror IP switches on.</summary>
        public void SetItems(ItemData itemData) { items = itemData; }

        /// <summary>Flag 2 or 3 if the script switches on equipment guards / mirrors, else -1.</summary>
        private int GuardFlagIn(int start, int end)
        {
            List<int> ins = ScriptTools.Walk(ip.RawData, start, end);
            if (ins == null) return -1;
            foreach (int p in ins)
                if (ip.RawData[p] == 0x44 && (ip.RawData[p + 1] == 2 || ip.RawData[p + 1] == 3)) return ip.RawData[p + 1];
            return -1;
        }

        private string GuardText(int flag)
        {
            if (flag < 0 || items == null) return "";
            List<string> l = new List<string>();
            for (int i = 0; i < ItemData.Count; i++)
            {
                if (!items.IsEquipment(i)) continue;
                EquipElements e = EquipElements.Find(items, i);
                if (e.TriggerFlag == flag) l.Add(items.GetName(i).Trim() + ": " + e.GuardSummary(items.RawData));
            }
            string kind = flag == 2 ? "guard" : "mirror";
            return "This is a " + kind + " IP: it only switches on the " + kind + " of whatever " +
                (flag == 2 ? "shield or gloves" : "helmet") + " the user is wearing (the turn count is on the right). " +
                "The element and the effect belong to the item - edit them on the Equipment tab:\n" +
                (l.Count == 0 ? "(no equipment has one)" : string.Join(";  ", l.ToArray()));
        }

        public void Disable()
        {
            ip = null; currentId = -1; currentEffect = -1;
            comboIP.Items.Clear();
            this.Enabled = false;
        }

        private string Describe(int id) { return ip.GetName(id) + "  (" + id + ")"; }

        public void CommitCurrent()
        {
            if (ip == null || currentId < 1) return;
            int id = currentId;
            ip.SetName(id, textName.Text);
            ip.SetByte(id, IPData.OFS_COST, (int)numCost.Value);
            ip.SetWord(id, IPData.OFS_EFFECT, (int)numEffect.Value);
            ip.SetByte(id, IPData.OFS_ANIM, (int)numAnim.Value);
            ip.SetByte(id, IPData.OFS_TYPE, (int)numType.Value);
            ip.SetByte(id, IPData.OFS_TARGET, (int)numTarget.Value);
            CommitEffect();

            loading = true;
            UiHelpers.RenameItem(comboIP, id - 1, Describe(id));
            loading = false;
        }

        private void CommitEffect()
        {
            if (ip == null || currentEffect < 0) return;
            effectPanel.Commit();
        }

        public void RefreshView() { if (ip != null && currentId >= 1) ShowMove(currentId); }

        private void comboIP_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (loading || ip == null) return;
            int newIndex = comboIP.SelectedIndex;
            if (newIndex < 0 || newIndex + 1 == currentId) return;
            CommitCurrent();
            if (comboIP.SelectedIndex != newIndex)
            {
                loading = true; comboIP.SelectedIndex = newIndex; loading = false;
            }
            ShowMove(newIndex + 1);
        }

        private void ShowMove(int id)
        {
            loading = true;
            currentId = id;

            int cap = ip.NameCapacity(id);
            textName.MaxLength = Math.Max(cap, 1);
            textName.Text = ip.GetName(id);
            labelNameLimit.Text = "(max " + cap + " letters)";

            UiHelpers.SetValueClamped(numCost, ip.GetByte(id, IPData.OFS_COST));
            numCost_ValueChanged(null, EventArgs.Empty);
            UiHelpers.SetValueClamped(numEffect, ip.GetWord(id, IPData.OFS_EFFECT));
            UiHelpers.SetValueClamped(numAnim, ip.GetByte(id, IPData.OFS_ANIM));
            UiHelpers.SetValueClamped(numType, ip.GetByte(id, IPData.OFS_TYPE));
            UiHelpers.SetValueClamped(numTarget, ip.GetByte(id, IPData.OFS_TARGET));

            labelInfo.Text = "IP ID: " + id + " (" + id.ToString("X2") + ")    Offset: " + ip.RecordOffset(id).ToString("X");

            // Highlight what differs from the reference ROM.
            IPData r = Reference.IP;
            bool known = r != null && id <= r.Count;
            ChangeHighlighter.Set(textName, known ? Reference.Try(delegate { return r.GetName(id); }) : null);
            ChangeHighlighter.Set(numCost, known ? Reference.Try(delegate { return r.GetByte(id, IPData.OFS_COST); }) : null);
            ChangeHighlighter.Set(numEffect, known ? Reference.Try(delegate { return r.GetWord(id, IPData.OFS_EFFECT); }) : null);
            ChangeHighlighter.Set(numAnim, known ? Reference.Try(delegate { return r.GetByte(id, IPData.OFS_ANIM); }) : null);
            ChangeHighlighter.Set(numType, known ? Reference.Try(delegate { return r.GetByte(id, IPData.OFS_TYPE); }) : null);
            ChangeHighlighter.Set(numTarget, known ? Reference.Try(delegate { return r.GetByte(id, IPData.OFS_TARGET); }) : null);
            loading = false;

            ShowEffect(ip.GetWord(id, IPData.OFS_EFFECT));
        }

        private void ShowEffect(int effect)
        {
            currentEffect = -1;
            int start, end;
            if (!ip.GetEffectRange(effect, out start, out end))
            {
                labelShared.Text = "Effect script " + effect + " can't be decoded, so it isn't editable here.";
                effectPanel.ShowNothing("Nothing to edit.");
                labelGuard.Text = "";
                return;
            }

            // Which moves use this effect?
            List<string> users = new List<string>();
            for (int id = 1; id <= ip.Count; id++)
            {
                int eff = (id == currentId) ? effect : ip.GetWord(id, IPData.OFS_EFFECT);
                if (eff == effect) users.Add(ip.GetName(id));
            }
            labelShared.Text = "Effect script " + effect + (users.Count > 1
                ? " - shared by " + users.Count + " moves, edits change all of them: " + string.Join(", ", users.ToArray())
                : " - used only by this move.");

            // Multi-hit moves repeat the normal attack command (37): Octo-strike 8, Repeat attack 4,
            // Triple attack 3, Scissor slash / Double slash 2. The count is fixed by the script's size.
            int hits = 0;
            List<int> ins = ScriptTools.Walk(ip.RawData, start, end);
            if (ins != null) foreach (int p in ins) if (ip.RawData[p] == 0x37) hits++;
            if (hits > 1) labelShared.Text += "  Attacks " + hits + " times in a row (each a normal attack).";

            labelGuard.Text = GuardText(GuardFlagIn(start, end));
            effectPanel.ShowScript(ip.RawData, start, end, spellNames, "Move");
            currentEffect = effect;
        }
    }
}
