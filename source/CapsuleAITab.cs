using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

using com.lufiaIIEditor.RomInfo;

namespace com.lufiaIIEditor.UI
{
    /// <summary>
    /// "Capsule AI" tab. Left: a capsule form's menu skill list and its AI script (which skill
    /// effects it uses, random odds, conditions). Right: the 84 skill effects themselves
    /// (name shown in battle, damage, element, statuses...). Everything is edited in place.
    /// </summary>
    class CapsuleAITab : TabPage
    {
        private CapsuleSkillData skills;
        private string[] spellNames;
        private int currentCapsule = -1, currentEffect = -1;
        private bool loading = false;

        private ComboBox comboCapsule;
        private Label labelCapsuleInfo;
        private ComboBox[] comboSlot = new ComboBox[6];   // 0-2 starting skills, 3-5 their upgrades
        private Label[] labelSlotAI = new Label[3];
        private NumericUpDown numLearn;
        private Label labelLearn;
        private EffectEditorPanel aiPanel;

        private ComboBox comboEffect;
        private ComboBox comboEffectName;
        private TextBox textName;
        private Label labelNameLimit, labelUsedBy;
        private EffectEditorPanel effectPanel;

        public CapsuleAITab()
        {
            this.Text = "Capsule AI";
            this.UseVisualStyleBackColor = true;
            Build();
            this.Enabled = false;
        }

        private void Build()
        {
            Controls.Add(UiHelpers.MakeLabel("Capsule:", 12, 15));
            comboCapsule = UiHelpers.MakeDropDown(70, 12, 230);
            comboCapsule.SelectedIndexChanged += new EventHandler(comboCapsule_SelectedIndexChanged);
            Controls.Add(comboCapsule);
            labelCapsuleInfo = UiHelpers.MakeNote("", 310, 15);
            Controls.Add(labelCapsuleInfo);

            GroupBox gSlots = UiHelpers.MakeGroup("Skills and what they can be replaced by (capsule menu)", 12, 40, 470, 128);
            Controls.Add(gSlots);
            gSlots.Controls.Add(UiHelpers.MakeNote("Starts with", 52, 16));
            gSlots.Controls.Add(UiHelpers.MakeNote("can learn (replaces it)", 215, 16));
            for (int k = 0; k < 6; k++)
            {
                int row = k % 3, y = 32 + row * 25;
                if (k < 3) gSlots.Controls.Add(UiHelpers.MakeLabel("Slot " + (row + 1), 8, y + 3));
                comboSlot[k] = UiHelpers.MakeDropDown(k < 3 ? 52 : 215, y, 140);
                comboSlot[k].SelectedIndexChanged += delegate(object o, EventArgs e) { if (!loading) UpdateSlotLabels(); };
                gSlots.Controls.Add(comboSlot[k]);
                if (k < 3)
                {
                    gSlots.Controls.Add(UiHelpers.MakeLabel("->", 197, y + 3));
                    labelSlotAI[k] = UiHelpers.MakeNote("", 362, y + 3);
                    gSlots.Controls.Add(labelSlotAI[k]);
                }
            }

            gSlots.Controls.Add(UiHelpers.MakeLabel("Chance to learn on each level-up: 1 in", 8, 107));
            numLearn = UiHelpers.MakeNumeric(215, 104, 1, 255);
            numLearn.Size = new Size(50, 20);
            numLearn.ValueChanged += new EventHandler(numLearn_ValueChanged);
            gSlots.Controls.Add(numLearn);
            labelLearn = UiHelpers.MakeNote("", 270, 107);
            gSlots.Controls.Add(labelLearn);

            GroupBox gAI = UiHelpers.AnchorShare(this, UiHelpers.MakeGroup("What the capsule's AI does (in script order)", 12, 172, 470, 336), 0, 0.5);
            Controls.Add(gAI);
            aiPanel = UiHelpers.Anchor(this, new EffectEditorPanel(452, 310, 220), AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right);
            aiPanel.Location = new Point(9, 20);
            gAI.Controls.Add(aiPanel);

            GroupBox gEffect = UiHelpers.AnchorShare(this, UiHelpers.MakeGroup("Skill effects (shared: used by any capsule whose AI picks them)", 492, 8, 486, 500), 0.5, 0.5);
            Controls.Add(gEffect);
            gEffect.Controls.Add(UiHelpers.MakeLabel("Effect:", 10, 23));
            comboEffect = UiHelpers.MakeDropDown(70, 20, 220);
            comboEffect.SelectedIndexChanged += new EventHandler(comboEffect_SelectedIndexChanged);
            gEffect.Controls.Add(comboEffect);
            labelUsedBy = UiHelpers.MakeNote("", 10, 46);
            labelUsedBy.AutoSize = false;
            labelUsedBy.Size = new Size(466, 30);
            gEffect.Controls.Add(labelUsedBy);

            gEffect.Controls.Add(UiHelpers.MakeLabel("Name in battle:", 10, 81));
            comboEffectName = UiHelpers.MakeDropDown(110, 78, 180);
            comboEffectName.SelectedIndexChanged += new EventHandler(comboEffectName_SelectedIndexChanged);
            gEffect.Controls.Add(comboEffectName);
            gEffect.Controls.Add(UiHelpers.MakeLabel("Rename:", 300, 81));
            textName = UiHelpers.MakeNameBox(355, 78, 12);
            textName.Size = new Size(120, 20);
            gEffect.Controls.Add(textName);
            labelNameLimit = UiHelpers.MakeNote("", 300, 100);
            gEffect.Controls.Add(labelNameLimit);

            effectPanel = UiHelpers.Anchor(this, new EffectEditorPanel(466, 330, 220), AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right);
            effectPanel.Location = new Point(10, 120);
            gEffect.Controls.Add(effectPanel);
            gEffect.Controls.Add(UiHelpers.Anchor(this, UiHelpers.MakeNote(
                "Renaming changes that name everywhere it's shown (menu and battle). Pick a capsule's\n" +
                "\"Uses skill\" line on the left, then edit what that skill does here.", 10, 458), AnchorStyles.Bottom | AnchorStyles.Left));
        }

        // ---------------- load / commit ----------------

        public void LoadRom(CapsuleSkillData data, string[] spells)
        {
            int keepC = comboCapsule.SelectedIndex, keepE = comboEffect.SelectedIndex;
            skills = data;
            spellNames = spells;
            currentCapsule = -1; currentEffect = -1;
            loading = true;
            comboCapsule.Items.Clear();
            for (int i = 0; i < CapsuleMonsterData.Count; i++) comboCapsule.Items.Add(CapsuleLabel(i));
            FillNameLists();
            loading = false;
            this.Enabled = true;
            comboEffect.SelectedIndex = (keepE >= 0 && keepE < CapsuleSkillData.EffectCount) ? keepE : 0;
            comboCapsule.SelectedIndex = (keepC >= 0 && keepC < CapsuleMonsterData.Count) ? keepC : 0;
        }

        public void Disable()
        {
            skills = null; currentCapsule = -1; currentEffect = -1;
            comboCapsule.Items.Clear();
            this.Enabled = false;
        }

        private string CapsuleLabel(int i)
        {
            return skills.Capsules.GetName(i).Trim() + "  (" + CapsuleMonsterData.FamilyOf(i) + " " + CapsuleMonsterData.FormOf(i) + ")";
        }

        private void FillNameLists()
        {
            string[] names = skills.AllNames();
            foreach (ComboBox cb in comboSlot)
            {
                cb.Items.Clear();
                for (int n = 0; n < names.Length; n++) cb.Items.Add(n == 0 ? "(empty)" : names[n] + "  (" + n.ToString("X2") + ")");
            }
            comboEffectName.Items.Clear();
            for (int n = 0; n < names.Length; n++) comboEffectName.Items.Add(names[n] + "  (" + n.ToString("X2") + ")");
            string[] labels = skills.EffectLabels();
            comboEffect.Items.Clear();
            for (int e = 0; e < labels.Length; e++) comboEffect.Items.Add(labels[e] + "  (" + e.ToString("X2") + ")");
            aiPanel.CapsuleSkillNames = labels;
        }

        public void CommitCurrent()
        {
            if (skills == null) return;
            if (currentCapsule >= 0)
            {
                int o = skills.Capsules.RecordOffset(currentCapsule) + CapsuleMonsterData.OFS_SKILLS;
                for (int k = 0; k < 6; k++)
                    if (comboSlot[k].SelectedIndex >= 0) skills.RawData[o + k] = (byte)comboSlot[k].SelectedIndex;
                aiPanel.Commit();
            }
            if (currentEffect >= 0)
            {
                if (comboEffectName.SelectedIndex >= 0) skills.SetEffectName(currentEffect, comboEffectName.SelectedIndex);
                int n = skills.GetEffectName(currentEffect);
                if (n > 0 && n < CapsuleSkillData.NameCount && textName.Text.Trim().Length > 0 && textName.Text != skills.GetName(n))
                    skills.SetName(n, textName.Text);
                effectPanel.Commit();
            }
        }

        public void RefreshView()
        {
            if (skills == null) return;
            CommitCurrent();
            int c = currentCapsule, e = currentEffect;
            loading = true;
            int sc = comboCapsule.SelectedIndex, se = comboEffect.SelectedIndex;
            for (int i = 0; i < CapsuleMonsterData.Count; i++) UiHelpers.RenameItem(comboCapsule, i, CapsuleLabel(i));
            FillNameLists();
            comboEffect.SelectedIndex = se;
            loading = false;
            if (e >= 0) ShowEffect(e);
            if (c >= 0) ShowCapsule(c);
        }

        // ---------------- capsule side ----------------

        private void comboCapsule_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (loading || skills == null) return;
            int i = comboCapsule.SelectedIndex;
            if (i < 0 || i == currentCapsule) return;
            CommitCurrent();
            ShowCapsule(i);
            if (currentEffect >= 0) ShowEffect(currentEffect);   // "used by" may have changed
        }

        private void ShowCapsule(int i)
        {
            currentCapsule = i;
            byte[] d = skills.RawData;
            int rec = skills.Capsules.RecordOffset(i);
            loading = true;
            for (int k = 0; k < 6; k++)
            {
                int v = d[rec + CapsuleMonsterData.OFS_SKILLS + k];
                while (v >= comboSlot[k].Items.Count) comboSlot[k].Items.Add("(unknown)  (" + comboSlot[k].Items.Count.ToString("X2") + ")");
                comboSlot[k].SelectedIndex = v;
                int ro = Reference.Shift(rec + CapsuleMonsterData.OFS_SKILLS + k);
                ChangeHighlighter.Set(comboSlot[k], Reference.InRange(ro, 1) ? (object)(int)Reference.Data[ro] : null);
            }
            UpdateSlotLabels();
            numLearn.Enabled = skills.HasLearnChance;
            if (skills.HasLearnChance)
            {
                UiHelpers.SetValueClamped(numLearn, skills.LearnChance);
                int rl = Reference.Shift(skills.LearnChanceOffset);
                ChangeHighlighter.Set(numLearn, Reference.InRange(rl, 1) ? (object)(int)Reference.Data[rl] : null);
            }
            UpdateLearnLabel();
            loading = false;

            int start = skills.AiScriptStart(i), end = skills.AiScriptEnd(i);
            labelCapsuleInfo.Text = "Record " + rec.ToString("X") + "   AI " + start.ToString("X") + "-" + (end - 1).ToString("X");
            if (end <= start || ScriptTools.Walk(d, start, end) == null)
            {
                aiPanel.ShowNothing("This capsule's AI script couldn't be decoded, so it isn't editable here.");
                return;
            }
            aiPanel.ShowScript(d, start, end, spellNames, "AI");
        }

        /// <summary>
        /// Locks slots that can never do anything for this form (the AI can't gain new lines):
        /// a "can learn" field only works if the AI checks that slot, and an empty starting slot
        /// has no AI line behind it. A field that already holds a skill stays open so it can be
        /// cleared or restored. Also shows whether the AI switches to each upgrade.
        /// </summary>
        private void UpdateSlotLabels()
        {
            if (skills == null || currentCapsule < 0) return;
            int rec = skills.Capsules.RecordOffset(currentCapsule) + CapsuleMonsterData.OFS_SKILLS;
            for (int k = 0; k < 3; k++)
            {
                bool checks = skills.AiChecksSlot(currentCapsule, k);
                ComboBox start = comboSlot[k], learn = comboSlot[3 + k];
                start.Enabled = start.SelectedIndex > 0 || OriginalSlot(rec + k) > 0;
                learn.Enabled = checks || learn.SelectedIndex > 0 || OriginalSlot(rec + 3 + k) > 0;
                bool upgrade = learn.SelectedIndex > 0;
                if (upgrade) labelSlotAI[k].Text = checks ? "AI switches to it" : "AI never uses it";
                else if (!start.Enabled && !learn.Enabled) labelSlotAI[k].Text = "not used by this form";
                else if (!learn.Enabled) labelSlotAI[k].Text = "nothing to learn";
                else labelSlotAI[k].Text = "";
                labelSlotAI[k].ForeColor = upgrade && !checks ? Color.DarkRed : SystemColors.GrayText;
            }
        }

        private static int OriginalSlot(int offset)
        {
            int r = Reference.Shift(offset);
            return Reference.InRange(r, 1) ? Reference.Data[r] : 0;
        }

        private void numLearn_ValueChanged(object sender, EventArgs e)
        {
            if (loading || skills == null || !skills.HasLearnChance) return;
            skills.LearnChance = (int)numLearn.Value;
            UpdateLearnLabel();
        }

        private void UpdateLearnLabel()
        {
            int n = (int)numLearn.Value;
            labelLearn.Text = "= " + (100.0 / n / 3).ToString("0.#") + "% per slot (all capsules)";
        }

        // ---------------- effect side ----------------

        private void comboEffect_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (loading || skills == null) return;
            int i = comboEffect.SelectedIndex;
            if (i < 0 || i == currentEffect) return;
            CommitCurrent();
            ShowEffect(i);
            // Labels in the AI list come from effect names; refresh in case one was renamed.
            if (currentCapsule >= 0) ShowCapsule(currentCapsule);
        }

        private void ShowEffect(int i)
        {
            currentEffect = i;
            loading = true;
            int n = skills.GetEffectName(i);
            while (n >= comboEffectName.Items.Count) comboEffectName.Items.Add("(unknown)  (" + comboEffectName.Items.Count.ToString("X2") + ")");
            comboEffectName.SelectedIndex = n;
            int ro = Reference.Shift(skills.EffectOffset(i));
            ChangeHighlighter.Set(comboEffectName, Reference.InRange(ro, 1) ? (object)(int)Reference.Data[ro] : null);

            bool canRename = n > 0 && n < CapsuleSkillData.NameCount;
            textName.Enabled = canRename;
            if (canRename)
            {
                int cap = skills.NameCapacity(n);
                textName.MaxLength = cap;
                textName.Text = skills.GetName(n);
                labelNameLimit.Text = "max " + cap + " letters";
                int rno = Reference.Shift(skills.NameOffset(n));
                ChangeHighlighter.Set(textName, Reference.InRange(rno, cap + 1) ? (object)RefString(rno, cap) : null);
            }
            else { textName.Text = ""; labelNameLimit.Text = ""; }
            loading = false;

            List<string> users = new List<string>();
            for (int c = 0; c < CapsuleMonsterData.Count; c++)
                if (skills.EffectsUsedBy(c).Contains(i)) users.Add(skills.Capsules.GetName(c).Trim());
            labelUsedBy.Text = "Animation " + skills.GetEffectAnim(i).ToString("X2") + "   Used by: " +
                (users.Count == 0 ? "no capsule AI (unused)" : string.Join(", ", users.ToArray()));

            int start = skills.EffectScriptStart(i), end = skills.EffectScriptEnd(i);
            if (end <= start || ScriptTools.Walk(skills.RawData, start, end) == null)
            {
                effectPanel.ShowNothing("This skill's effect script couldn't be decoded, so it isn't editable here.");
                return;
            }
            effectPanel.ShowScript(skills.RawData, start, end, spellNames, "Effect");
        }

        /// <summary>Picking a different name: the rename box now edits that name.</summary>
        private void comboEffectName_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (loading || skills == null || currentEffect < 0) return;
            int n = comboEffectName.SelectedIndex;
            loading = true;
            bool canRename = n > 0 && n < CapsuleSkillData.NameCount;
            textName.Enabled = canRename;
            if (canRename)
            {
                int cap = skills.NameCapacity(n);
                textName.MaxLength = cap;
                textName.Text = skills.GetName(n);
                labelNameLimit.Text = "max " + cap + " letters";
                int rno = Reference.Shift(skills.NameOffset(n));
                ChangeHighlighter.Set(textName, Reference.InRange(rno, cap + 1) ? (object)RefString(rno, cap) : null);
            }
            else { textName.Text = ""; labelNameLimit.Text = ""; }
            loading = false;
        }

        private static string RefString(int o, int max)
        {
            int e = o;
            while (e < o + max && Reference.Data[e] != 0) e++;
            return System.Text.Encoding.ASCII.GetString(Reference.Data, o, e - o);
        }
    }
}
