using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

using com.lufiaIIEditor.RomInfo;

namespace com.lufiaIIEditor.UI
{
    /// <summary>
    /// Shows and edits one effect script: its numbers (damage, element, healing, stat changes,
    /// critical hits, cast spell, formula numbers) on top and its statuses below.
    /// Used by the IP Moves and Spell Effects tabs. Everything is edited in place.
    /// </summary>
    class EffectEditorPanel : Panel
    {
        private byte[] data;
        private bool active = false;
        private Panel panelValues;
        private StatusEffectsPanel statusPanel;
        private List<ScriptValue> values = new List<ScriptValue>();
        private List<Control> valueControls = new List<Control>();

        // Where the value boxes start: 340 normally, closer on narrow panels so the boxes stay
        // inside without scrolling sideways; moved again when the panel is resized.
        private int ControlX = 340;
        private int ValueX() { return Math.Max(250, Math.Min(340, panelValues.Width - 200)); }
        private readonly double valuesShare;   // part of the height used by the values list
        private Label labelStatuses;

        /// <summary>Enemy skill names (for "uses skill" choices in enemy AI). Optional.</summary>
        public string[] SkillNames;

        /// <summary>Capsule skill effect names (0-based) for capsule AI "uses skill" choices. Optional.</summary>
        public string[] CapsuleSkillNames;

        public EffectEditorPanel(int width, int height) : this(width, height, height * 3 / 5) { }

        public EffectEditorPanel(int width, int height, int valuesHeight)
        {
            this.Size = new Size(width, height);
            valuesShare = (double)valuesHeight / height;

            panelValues = new Panel();
            panelValues.Location = new Point(0, 0);
            panelValues.Size = new Size(width, valuesHeight);
            panelValues.AutoScroll = true;
            Controls.Add(panelValues);

            labelStatuses = UiHelpers.MakeLabel("Statuses", 2, valuesHeight + 6);
            Controls.Add(labelStatuses);
            statusPanel = new StatusEffectsPanel();
            statusPanel.Location = new Point(0, valuesHeight + 24);
            statusPanel.Size = new Size(width, height - valuesHeight - 24);
            Controls.Add(statusPanel);
        }

        /// <summary>When the window is resized, the values and statuses lists share the new height.</summary>
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (panelValues == null || statusPanel == null || labelStatuses == null) return;
            int vh = Math.Max(60, (int)(Height * valuesShare));
            panelValues.Size = new Size(Width, vh);
            labelStatuses.Location = new Point(2, vh + 6);
            // Shift the value boxes (and their hints) when the space for them changes.
            int want = ValueX();
            if (want != ControlX)
            {
                int dx = want - ControlX;
                foreach (Control c in panelValues.Controls)
                    if (c.Left >= ControlX - 1) c.Left += dx;
                ControlX = want;
            }
            statusPanel.Location = new Point(0, vh + 24);
            statusPanel.Size = new Size(Width, Math.Max(40, Height - vh - 24));
        }

        /// <summary>Shows a message instead of values (nothing can be edited).</summary>
        public void ShowNothing(string message)
        {
            active = false;
            values = new List<ScriptValue>();
            valueControls.Clear();
            UiHelpers.ClearAndDispose(panelValues);
            panelValues.Controls.Add(UiHelpers.MakeNote(message, 3, 8));
            statusPanel.Clear();
            statusPanel.Build();
        }

        /// <summary>Loads a script. spellNames is used for "casts spell" choices.</summary>
        public void ShowScript(byte[] romData, int start, int end, string[] spellNames, string statusPrefix)
        {
            data = romData;
            active = false;
            values = new List<ScriptValue>();
            valueControls.Clear();
            panelValues.SuspendLayout();
            UiHelpers.ClearAndDispose(panelValues);
            statusPanel.Clear();
            if (spellNames == null) spellNames = new string[0];

            ControlX = ValueX();
            List<ScriptValue> found = ScriptTools.GetScriptValues(data, start, end, SkillNames, CapsuleSkillNames);
            if (found == null)
            {
                panelValues.ResumeLayout();
                ShowNothing("This script can't be decoded, so it isn't editable here.");
                return;
            }
            values = found;

            for (int k = 0; k < values.Count; k++)
            {
                ScriptValue v = values[k];
                int y = 5 + k * 28;
                Label label = UiHelpers.MakeLabel(v.Label, 3, y + 3);
                panelValues.Controls.Add(label);
                Control c;
                if (v.Kind == ScriptValue.KIND_INFO)
                {
                    label.ForeColor = SystemColors.GrayText;
                    c = new Label(); // placeholder so indexes line up; never written
                    c.Visible = false;
                }
                else if (v.Kind == ScriptValue.KIND_SKILL)
                {
                    ComboBox cb = UiHelpers.MakeDropDown(ControlX, y, 170);
                    string[] names = SkillNames ?? new string[0];
                    for (int s = 0; s < names.Length; s++) cb.Items.Add((names[s] ?? "").Trim() + "  (" + (s + 1).ToString("X2") + ")");
                    int cur = ScriptTools.GetScriptValue(data, v);
                    while (cur - 1 >= cb.Items.Count && cb.Items.Count < 256) cb.Items.Add("(unknown)  (" + (cb.Items.Count + 1).ToString("X2") + ")");
                    if (cur >= 1) cb.SelectedIndex = cur - 1;
                    c = cb;
                }
                else if (v.Kind == ScriptValue.KIND_CAPSKILL)
                {
                    ComboBox cb = UiHelpers.MakeDropDown(ControlX, y, 170);
                    string[] names = CapsuleSkillNames ?? new string[0];
                    for (int s = 0; s < names.Length; s++) cb.Items.Add((names[s] ?? "").Trim() + "  (" + s.ToString("X2") + ")");
                    int cur = ScriptTools.GetScriptValue(data, v);
                    while (cur >= cb.Items.Count && cb.Items.Count < 256) cb.Items.Add("(unknown)  (" + cb.Items.Count.ToString("X2") + ")");
                    cb.SelectedIndex = cur;
                    c = cb;
                }
                else if (v.Kind == ScriptValue.KIND_SPELL)
                {
                    ComboBox cb = UiHelpers.MakeDropDown(ControlX, y, 150);
                    for (int s = 0; s < spellNames.Length; s++) cb.Items.Add((spellNames[s] ?? "").Trim() + "  (" + s + ")");
                    int cur = ScriptTools.GetScriptValue(data, v);
                    while (cur >= cb.Items.Count && cb.Items.Count < 256) cb.Items.Add("(unknown)  (" + cb.Items.Count + ")");
                    cb.SelectedIndex = cur;
                    c = cb;
                }
                else if (v.Kind == ScriptValue.KIND_ELEMENT)
                {
                    ComboBox cb = UiHelpers.MakeDropDown(ControlX, y, 150);
                    foreach (string nm in ScriptTools.ElementNames) cb.Items.Add(nm);
                    int cur = ScriptTools.GetScriptValue(data, v);
                    int idx = Array.IndexOf(ScriptTools.ElementValues, cur);
                    if (idx < 0)
                    {
                        cb.Items.Add(ScriptTools.DescribeElement(cur) + " (keep)");
                        idx = cb.Items.Count - 1;
                    }
                    cb.SelectedIndex = idx;
                    c = cb;
                }
                else
                {
                    NumericUpDown n = v.Kind == ScriptValue.KIND_SIGNED
                        ? UiHelpers.MakeNumeric(ControlX, y, -128, 127)
                        : UiHelpers.MakeNumeric(ControlX, y, 0, v.Max);
                    UiHelpers.SetValueClamped(n, ScriptTools.GetScriptValue(data, v));
                    c = n;
                    if (v.Hint == ScriptValue.HINT_STAT_PERCENT)
                    {
                        // The label itself says what the number does (there's more room there).
                        string subject = v.Subject ?? "stat";
                        string tail = subject + " change %";
                        string prefix = v.Label.EndsWith(tail) ? v.Label.Substring(0, v.Label.Length - tail.Length) : "";
                        NumericUpDown box = n;
                        Label text = label;
                        EventHandler update = delegate(object hintSender, EventArgs hintArgs)
                        {
                            int pct = (int)box.Value;
                            if (pct > 0) text.Text = prefix + "Raises " + subject + ": +" + (pct + 1) / 2 + "% first use, up to +" + pct + "%";
                            else if (pct < 0) text.Text = prefix + "Lowers " + subject + ": about -" + (-pct + 1) / 2 + "% each use";
                            else text.Text = prefix + subject + ": no change";
                        };
                        n.ValueChanged += update;
                        update(null, EventArgs.Empty);
                        new ToolTip().SetToolTip(n, "Percent of the normal stat. Raising: each use closes half the gap to +n% (big first boost, smaller ones after, never past +n%). Lowering: each use takes off about n/2 % of the current value.");
                    }
                    if (v.Hint == ScriptValue.HINT_SKIP_CHANCE)
                    {
                        Label hint = UiHelpers.MakeNote("", ControlX + 75, y + 3);
                        panelValues.Controls.Add(hint);
                        NumericUpDown box = n;
                        EventHandler update = delegate(object hintSender, EventArgs hintArgs)
                        {
                            int runs = (255 - (int)box.Value) * 100 / 255;
                            hint.Text = "-> runs " + runs + "%";
                        };
                        n.ValueChanged += update;
                        update(null, EventArgs.Empty);
                    }
                }
                panelValues.Controls.Add(c);
                valueControls.Add(c);
                if (v.Kind != ScriptValue.KIND_INFO) ChangeHighlighter.Set(c, OriginalOf(v, c));
            }
            if (values.Count == 0)
                panelValues.Controls.Add(UiHelpers.MakeNote("No editable numbers in this effect.", 3, 8));
            panelValues.ResumeLayout();

            statusPanel.AddEffects(data, ScriptTools.FindStatusEffects(data, start, end), statusPrefix);
            statusPanel.Build();
            active = true;
        }

        /// <summary>The same value in the reference ROM, in the form the control uses (null = unknown).</summary>
        private object OriginalOf(ScriptValue v, Control c)
        {
            int ro = Reference.Shift(v.Offset);
            if (!Reference.InRange(ro, 2)) return null;
            ScriptValue rv = new ScriptValue();
            rv.Kind = v.Kind; rv.Offset = ro; rv.Size = v.Size; rv.Max = v.Max;
            int refVal = ScriptTools.GetScriptValue(Reference.Data, rv);
            if (v.Kind == ScriptValue.KIND_ELEMENT)
            {
                int idx = Array.IndexOf(ScriptTools.ElementValues, refVal);
                if (idx >= 0) return idx;
                // A combination: matches the "(keep)" entry only if it's still the same combination.
                // A different combination than now: changed, but there's no single entry to restore.
                return refVal == ScriptTools.GetScriptValue(data, v) ? (object)(((ComboBox)c).Items.Count - 1) : (object)(-2);
            }
            if (v.Kind == ScriptValue.KIND_SPELL || v.Kind == ScriptValue.KIND_CAPSKILL)
                return refVal < ((ComboBox)c).Items.Count ? (object)refVal : null;
            if (v.Kind == ScriptValue.KIND_SKILL)
                return (refVal >= 1 && refVal - 1 < ((ComboBox)c).Items.Count) ? (object)(refVal - 1) : null;
            return refVal;
        }

        public void Commit()
        {
            if (!active || data == null) return;
            for (int k = 0; k < values.Count && k < valueControls.Count; k++)
            {
                ScriptValue v = values[k];
                Control c = valueControls[k];
                if (v.Kind == ScriptValue.KIND_NUMBER || v.Kind == ScriptValue.KIND_SIGNED)
                {
                    ScriptTools.SetScriptValue(data, v, (int)((NumericUpDown)c).Value);
                }
                else if (v.Kind == ScriptValue.KIND_SPELL || v.Kind == ScriptValue.KIND_CAPSKILL)
                {
                    ComboBox cb = (ComboBox)c;
                    if (cb.SelectedIndex >= 0) ScriptTools.SetScriptValue(data, v, cb.SelectedIndex);
                }
                else if (v.Kind == ScriptValue.KIND_SKILL)
                {
                    ComboBox cb = (ComboBox)c;
                    if (cb.SelectedIndex >= 0) ScriptTools.SetScriptValue(data, v, cb.SelectedIndex + 1);
                }
                else if (v.Kind == ScriptValue.KIND_ELEMENT)
                {
                    ComboBox cb = (ComboBox)c;
                    // The extra last entry means "keep the current combination".
                    if (cb.SelectedIndex >= 0 && cb.SelectedIndex < ScriptTools.ElementValues.Length)
                        ScriptTools.SetScriptValue(data, v, ScriptTools.ElementValues[cb.SelectedIndex]);
                }
            }
            statusPanel.Commit();
        }
    }
}
