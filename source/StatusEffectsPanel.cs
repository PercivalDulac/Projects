using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

using com.lufiaIIEditor.RomInfo;

namespace com.lufiaIIEditor.UI
{
    /// <summary>
    /// Lists the status commands of one script (or several) and lets you change which status
    /// each one uses and its number. Edits are written back by Commit(). Same size, always.
    /// </summary>
    class StatusEffectsPanel : Panel
    {
        private byte[] data;
        private List<StatusEffect> effects = new List<StatusEffect>();
        private List<string> prefixes = new List<string>();
        private List<ComboBox> statusBoxes = new List<ComboBox>();
        private List<NumericUpDown> valueBoxes = new List<NumericUpDown>();

        public StatusEffectsPanel()
        {
            this.AutoScroll = true;
        }

        public void Clear()
        {
            effects.Clear(); prefixes.Clear(); statusBoxes.Clear(); valueBoxes.Clear();
            UiHelpers.ClearAndDispose(this);
        }

        /// <summary>Adds the effects found in one script; prefix labels where they come from ("Weapon", "AI"...).</summary>
        public void AddEffects(byte[] romData, List<StatusEffect> list, string prefix)
        {
            data = romData;
            foreach (StatusEffect e in list) { effects.Add(e); prefixes.Add(prefix); }
        }

        public void Build()
        {
            this.SuspendLayout();
            UiHelpers.ClearAndDispose(this);
            statusBoxes.Clear(); valueBoxes.Clear();
            if (effects.Count == 0)
            {
                this.Controls.Add(UiHelpers.MakeNote("No status effects.", 5, 8));
                this.ResumeLayout();
                return;
            }
            for (int k = 0; k < effects.Count; k++)
            {
                StatusEffect e = effects[k];
                int y = 5 + k * 30;
                string what = e.Kind == StatusEffect.KIND_PROTECT ? "protects vs" :
                              e.Kind == StatusEffect.KIND_INFLICT ? "inflicts" : "cures";
                this.Controls.Add(UiHelpers.MakeLabel(prefixes[k] + " " + what, 3, y + 3));

                ComboBox c = UiHelpers.MakeDropDown(112, y, 90);
                int count = e.Kind == StatusEffect.KIND_PROTECT ? ScriptTools.ProtectableCount : ScriptTools.StatusNames.Length;
                for (int s = 0; s < count; s++) c.Items.Add(ScriptTools.StatusNames[s]);
                int cur = ScriptTools.GetStatus(data, e);
                if (cur >= 0 && cur < count) c.SelectedIndex = cur;
                this.Controls.Add(c);
                statusBoxes.Add(c);

                NumericUpDown n;
                if (e.Kind == StatusEffect.KIND_PROTECT)
                {
                    n = UiHelpers.MakeNumeric(206, y, 0, 0x7FFF);
                    n.Size = new Size(55, 20);
                    if (e.Editable)
                    {
                        UiHelpers.SetValueClamped(n, ScriptTools.GetValue(data, e));
                        this.Controls.Add(UiHelpers.MakeNote("% (0 = immune)", 264, y + 3));
                    }
                    else
                    {
                        n.Enabled = false;
                        this.Controls.Add(UiHelpers.MakeNote("computed", 264, y + 3));
                    }
                }
                else
                {
                    n = UiHelpers.MakeNumeric(206, y, 0, 255);
                    n.Size = new Size(55, 20);
                    UiHelpers.SetValueClamped(n, ScriptTools.GetValue(data, e));
                    this.Controls.Add(UiHelpers.MakeNote("% chance", 264, y + 3));
                }
                this.Controls.Add(n);
                valueBoxes.Add(n);

                // Highlight changes against the reference ROM (same command at the same place).
                object origStatus = null, origValue = null;
                int ro = Reference.Shift(e.Offset);
                if (Reference.InRange(ro, 4) && Reference.Data[ro] == data[e.Offset])
                {
                    StatusEffect re = new StatusEffect();
                    re.Kind = e.Kind; re.Offset = ro; re.Editable = e.Editable;
                    int rs = ScriptTools.GetStatus(Reference.Data, re);
                    if (rs >= 0 && rs < c.Items.Count) origStatus = rs;
                    if (e.Editable) origValue = ScriptTools.GetValue(Reference.Data, re);
                }
                ChangeHighlighter.Set(c, origStatus);
                ChangeHighlighter.Set(n, origValue);
            }
            this.ResumeLayout();
        }

        public void Commit()
        {
            if (data == null) return;
            for (int k = 0; k < effects.Count && k < statusBoxes.Count; k++)
            {
                StatusEffect e = effects[k];
                if (statusBoxes[k].SelectedIndex >= 0) ScriptTools.SetStatus(data, e, statusBoxes[k].SelectedIndex);
                if (e.Editable) ScriptTools.SetValue(data, e, (int)valueBoxes[k].Value);
            }
        }
    }
}
