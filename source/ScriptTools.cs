using System;
using System.Collections.Generic;

namespace com.lufiaIIEditor.RomInfo
{
    /// <summary>One status-related command found in a script, editable in place (same size).</summary>
    class StatusEffect
    {
        public const int KIND_PROTECT = 0;   // 0C reg value : overrides the incoming chance for a status
        public const int KIND_INFLICT = 1;   // 27 status chance
        public const int KIND_CURE = 2;      // 26 status chance

        public int Kind;
        public int Offset;    // file offset of the opcode
        public bool Editable; // false when the value is computed at run time (not a plain number)
    }

    /// <summary>One editable number / choice inside a script (always edited in place).</summary>
    class ScriptValue
    {
        public const int KIND_NUMBER = 0;
        public const int KIND_SPELL = 1;    // byte: spell number
        public const int KIND_ELEMENT = 2;  // two bytes, high byte first: element flag
        public const int KIND_INFO = 3;     // nothing to edit, just a line of explanation
        public const int KIND_SIGNED = 4;   // one byte, -128..127
        public const int KIND_SKILL = 5;    // byte: enemy skill name number (1-based)
        public const int KIND_CAPSKILL = 6; // byte: capsule skill effect number (0-based, opcode 3E)

        public const int HINT_NONE = 0;
        public const int HINT_SKIP_CHANCE = 1; // value n = chance out of 255 to SKIP the next part
        public const int HINT_STAT_PERCENT = 2; // value n = stat change in % ($85:D33A: stat x n / 100)

        public int Kind;
        public int Hint;
        public int Offset;
        public int Size;    // 1 or 2 (numbers are little-endian)
        public int Max;
        public string Label;
        public string Subject;   // e.g. the stat name, for labels built while editing
    }

    /// <summary>
    /// Helpers for the game's effect/AI script language (interpreter at $85:B452, opcode
    /// table at $85:B483). Opcode sizes below were taken from the game's own handlers; with
    /// them every item script (202) and every monster script (224) decodes to its exact end.
    /// </summary>
    static class ScriptTools
    {
        // Size in bytes including the opcode. -1 = not a valid opcode, -2 = depends on next byte (32).
        private static readonly int[] OpSize = {
             1,  1,  1,  3,  3,  4,  6,  6,  6,  6,  6,  6,  4,  4,  4,  4,  // 00-0F
             4,  2,  3,  3,  3,  3,  4,  4,  4,  2,  2,  2,  2,  1,  2,  3,  // 10-1F
             3,  6,  6,  3,  5,  3,  3,  3,  1,  1,  1,  3,  2,  2,  1,  2,  // 20-2F
             2, -1, -2,  2,  1,  2,  2,  1,  1,  1,  1,  1,  1,  2,  2,  4,  // 30-3F
             2,  1,  3,  1,  3,  3,  4,  3,  1,  1,  2,  1,  2,  1,  3,  1,  // 40-4F
             1,  1,  1,  2,  2,  4,  3,  4,  1,  1,  2,  1,  1, -1, -1, -1,  // 50-5F
        };
        // Opcode 32 (targeting): size by its first operand.
        private static readonly int[] Op32Size = { 2, 2, 5, 5, 2, 2, 2, 2, 2, 3 };

        // Status order used by 26/27 and by the protection registers 4E, 50, 52, 54, 56, 58.
        public static readonly string[] StatusNames = {
            "Poison", "Silence", "Death", "Paralysis", "Confusion", "Sleep", "Status 6 (?)", "Mirror" };
        public const int ProtectableCount = 6;
        public const int FirstProtectRegister = 0x4E;

        public static int InstructionSize(byte[] data, int pos)
        {
            int op = data[pos];
            if (op >= OpSize.Length) return -1;
            int n = OpSize[op];
            if (n == -2)
            {
                int mode = pos + 1 < data.Length ? data[pos + 1] : 0xFF;
                n = mode < Op32Size.Length ? Op32Size[mode] : -1;
            }
            return n;
        }

        /// <summary>Start offsets of every instruction from start to end, or null if they don't line up exactly.</summary>
        public static List<int> Walk(byte[] data, int start, int end)
        {
            List<int> list = new List<int>();
            int p = start;
            while (p < end)
            {
                int n = InstructionSize(data, p);
                if (n <= 0) return null;
                list.Add(p);
                p += n;
            }
            return p == end ? list : null;
        }

        /// <summary>All status protections / inflictions / cures in a script range. Empty if the range doesn't decode.</summary>
        public static List<StatusEffect> FindStatusEffects(byte[] data, int start, int end)
        {
            List<StatusEffect> result = new List<StatusEffect>();
            List<int> ins = Walk(data, start, end);
            if (ins == null) return result;
            foreach (int p in ins)
            {
                int op = data[p];
                if (op == 0x0C)
                {
                    int reg = data[p + 1];
                    int idx = ProtectIndex(reg);
                    if (idx < 0) continue;
                    StatusEffect e = new StatusEffect();
                    e.Kind = StatusEffect.KIND_PROTECT;
                    e.Offset = p;
                    e.Editable = (data[p + 3] & 0x80) == 0; // bit 15 set = value read from a register
                    result.Add(e);
                }
                else if (op == 0x26 || op == 0x27)
                {
                    if (data[p + 1] >= StatusNames.Length) continue;
                    StatusEffect e = new StatusEffect();
                    e.Kind = op == 0x27 ? StatusEffect.KIND_INFLICT : StatusEffect.KIND_CURE;
                    e.Offset = p;
                    e.Editable = true;
                    result.Add(e);
                }
            }
            return result;
        }

        // Element flags as used by damage commands 21/22 and 1F (names from the original editor).
        public static readonly int[] ElementValues = {
            0x0000, 0x0200, 0x0400, 0x0004, 0x0008, 0x0100, 0x0800, 0x0010, 0x1000, 0x2000,
            0x0001, 0x0002, 0x0020, 0x4000, 0x8000 };
        public static readonly string[] ElementNames = {
            "Neutral", "Fire", "Water", "Ice", "Thunder", "Wind", "Soil", "Earth", "Light", "Shadow",
            "Anti-dragon", "Anti-insect", "Anti-sea", "Anti-hard", "Anti-flying" };

        private static readonly string[] StatTypeNames = {
            "current HP", "current MP", "max HP", "max MP", "ATP", "DFP", "STR", "AGL", "INT", "GUT", "MGR",
            "IP", "Level", "temp ATP", "temp DFP", "temp STR", "temp AGL", "temp INT", "temp GUT", "temp MGR" };

        private static string StatType(int b) { return b < StatTypeNames.Length ? StatTypeNames[b] : "value " + b.ToString("X2"); }

        private static string RegName(int r)
        {
            return (r & 0x80) != 0 ? "work value " + (r & 0x7F) : "register " + r.ToString("X2");
        }

        private static ScriptValue Val(int kind, int offset, int size, int max, string label)
        {
            ScriptValue v = new ScriptValue();
            v.Kind = kind; v.Offset = offset; v.Size = size; v.Max = max; v.Label = label;
            return v;
        }

        /// <summary>
        /// Editable numbers in a script that decodes exactly from start to end (null otherwise):
        /// damage and its element, heal amounts, stat changes, critical hits, cast spell,
        /// and plain numbers in formula steps. Status commands are handled separately.
        /// </summary>
        public static List<ScriptValue> GetScriptValues(byte[] data, int start, int end)
        {
            return GetScriptValues(data, start, end, null);
        }

        // 06..0B jump past the next part when the comparison holds ($85:B598...B652), so the
        // next part runs when it does NOT hold. These are the "runs if" conditions:
        private static readonly string[] RunsIfNames = { "is not", "equals", "<=", ">=", "<", ">" };

        /// <summary>
        /// As above; with skillNames (enemy AI), values that follow a "show skill name" command
        /// are labelled with that skill, e.g. "[Voltage bolt] Magic damage: base amount".
        /// </summary>
        public static List<ScriptValue> GetScriptValues(byte[] data, int start, int end, string[] skillNames)
        {
            return GetScriptValues(data, start, end, skillNames, null);
        }

        /// <summary>
        /// Capsule monster AI: capsuleSkills (effect names, 0-based) turns opcode 3E "use skill
        /// effect n" into a choice, and 3F (learned-skill check) into a line of explanation.
        /// </summary>
        public static List<ScriptValue> GetScriptValues(byte[] data, int start, int end, string[] skillNames, string[] capsuleSkills)
        {
            List<int> ins = Walk(data, start, end);
            if (ins == null) return null;
            List<ScriptValue> list = new List<ScriptValue>();
            // Values are grouped in blocks between decision points (end, jump, random choice,
            // condition). A block that shows a skill name is that skill's attack, whether the
            // name comes before or after the damage command.
            List<int> valueBlock = new List<int>();
            Dictionary<int, string> blockSkill = new Dictionary<int, string>();
            int block = 0;
            // A normal attack (28) that a 1F element belongs to: shown as one line with the element.
            Dictionary<int, bool> attackWithElement = new Dictionary<int, bool>();
            foreach (int p in ins)
            {
                int op = data[p];
                bool boundary = op == 0x00 || op == 0x03 || op == 0x04 || (op >= 0x05 && op <= 0x0B);
                if (boundary) block++;
                int before = list.Count;
                switch (op)
                {
                    case 0x1E:
                        {
                            int id = data[p + 1];
                            string nm = (skillNames != null && id >= 1 && id <= skillNames.Length) ? skillNames[id - 1].Trim() : "skill " + id.ToString("X2");
                            list.Add(Val(ScriptValue.KIND_SKILL, p + 1, 1, 255, "Uses skill (name shown)"));
                            if (!blockSkill.ContainsKey(block)) blockSkill[block] = nm;
                        }
                        break;
                    case 0x3E:
                        if (capsuleSkills != null)
                        {
                            int id = data[p + 1];
                            string nm = id < capsuleSkills.Length ? capsuleSkills[id].Trim() : "skill " + id.ToString("X2");
                            list.Add(Val(ScriptValue.KIND_CAPSKILL, p + 1, 1, 255, "Uses skill"));
                            if (!blockSkill.ContainsKey(block)) blockSkill[block] = nm;
                        }
                        break;
                    case 0x3F:
                        if (capsuleSkills != null)
                            list.Add(Val(ScriptValue.KIND_INFO, p, 0, 0, "Checks the capsule's learned-skill flag " + data[p + 1] + " for this form (jumps if set)"));
                        break;
                    case 0x2C:
                        list.Add(Val(ScriptValue.KIND_SPELL, p + 1, 1, 255, "Casts spell"));
                        break;
                    case 0x28:
                        if (!attackWithElement.ContainsKey(p))
                            list.Add(Val(ScriptValue.KIND_INFO, p, 0, 0, "Normal attack"));
                        break;
                    case 0x29:
                        list.Add(Val(ScriptValue.KIND_INFO, p, 0, 0, "Defends"));
                        break;
                    case 0x2A:
                        list.Add(Val(ScriptValue.KIND_INFO, p, 0, 0, "Runs away"));
                        break;
                    case 0x2D:
                        list.Add(Val(ScriptValue.KIND_INFO, p, 0, 0, "Calls a companion (" + data[p + 1].ToString("X2") + ")"));
                        break;
                    case 0x05:
                        {
                            // $85:B582: roll 0-254; if it's below n, jump past the next part.
                            ScriptValue r = Val(ScriptValue.KIND_NUMBER, p + 1, 1, 255, "Random: chance to SKIP the next part (n/255)");
                            r.Hint = ScriptValue.HINT_SKIP_CHANCE;
                            list.Add(r);
                        }
                        break;
                    case 0x06:
                    case 0x07:
                    case 0x08:
                    case 0x09:
                    case 0x0A:
                    case 0x0B:
                        if ((data[p + 3] & 0x80) != 0) break; // compares two registers: nothing to edit
                        list.Add(Val(ScriptValue.KIND_NUMBER, p + 2, 2, 0x7FFF, "Next part runs only if " + RegName(data[p + 1]) + " " + RunsIfNames[op - 0x06]));
                        break;
                    case 0x21:
                    case 0x22:
                        string kind = op == 0x21 ? "Physical damage" : "Magic damage";
                        if ((data[p + 4] & 0x80) == 0)
                            list.Add(Val(ScriptValue.KIND_NUMBER, p + 3, 2, 0x7FFF, kind + ": base amount"));
                        else
                            list.Add(Val(ScriptValue.KIND_INFO, p + 3, 0, 0, kind + ": amount comes from the formula steps"));
                        list.Add(Val(ScriptValue.KIND_ELEMENT, p + 1, 2, 0, kind + ": element"));
                        break;
                    case 0x1F:
                        {
                            // Enemy AI: "1F element ... 28" = a normal attack with that element.
                            int attack = -1;
                            for (int k = ins.IndexOf(p) + 1; k < ins.Count; k++)
                            {
                                int o2 = data[ins[k]];
                                if (o2 == 0x28) { attack = ins[k]; break; }
                                if (o2 == 0x00 || o2 == 0x03 || o2 == 0x04 || (o2 >= 0x05 && o2 <= 0x0B) || o2 == 0x1F) break;
                            }
                            if (attack >= 0)
                            {
                                attackWithElement[attack] = true;
                                list.Add(Val(ScriptValue.KIND_ELEMENT, p + 1, 2, 0, "Normal attack - element"));
                            }
                            else
                                list.Add(Val(ScriptValue.KIND_ELEMENT, p + 1, 2, 0, "Adds element to the attack"));
                        }
                        break;
                    case 0x24:
                        if ((data[p + 3] & 0x80) == 0)
                            list.Add(Val(ScriptValue.KIND_NUMBER, p + 2, 2, 0x7FFF, "Restore " + StatType(data[p + 1]) + ": amount"));
                        else
                            list.Add(Val(ScriptValue.KIND_INFO, p + 2, 0, 0, "Restore " + StatType(data[p + 1]) + ": amount comes from the formula steps"));
                        list.Add(Val(ScriptValue.KIND_NUMBER, p + 4, 1, 255, "Restore " + StatType(data[p + 1]) + ": random extra"));
                        break;
                    case 0x25:
                        {
                            // Battle code $85:D33A: target = stat x n / 100. Raising moves the boost half
                            // the way to +n% per use; lowering takes off half of n% of the current value.
                            ScriptValue sv = Val(ScriptValue.KIND_SIGNED, p + 2, 1, 127, StatType(data[p + 1]) + " change %");
                            sv.Hint = ScriptValue.HINT_STAT_PERCENT;
                            sv.Subject = StatType(data[p + 1]);
                            list.Add(sv);
                        }
                        break;
                    case 0x20:
                        list.Add(Val(ScriptValue.KIND_NUMBER, p + 1, 1, 255, "Critical hit: rate"));
                        list.Add(Val(ScriptValue.KIND_NUMBER, p + 2, 1, 255, "Critical hit: damage x (value / 10)"));
                        break;
                    case 0x42:
                        if (data[p + 1] == 0x21) list.Add(Val(ScriptValue.KIND_INFO, p, 0, 0, "Attack: 1.5x a normal attack (built in)"));
                        else if (data[p + 1] == 0x23) list.Add(Val(ScriptValue.KIND_INFO, p, 0, 0, "Attack: starts from normal attack power (scaled below)"));
                        else if (data[p + 1] == 0x22) list.Add(Val(ScriptValue.KIND_INFO, p, 0, 0, "Damage: the formula result is dealt as fixed damage"));
                        break;
                    case 0x44:
                        // 44 f n: set the user's battle flag f to n; it counts down once per turn ($85:ED45).
                        // Equipment armor scripts check flag 2 (shields, gloves) and 3 (helmets).
                        if (data[p + 1] == 2 || data[p + 1] == 3)
                            list.Add(Val(ScriptValue.KIND_NUMBER, p + 2, 1, 255, data[p + 1] == 2
                                ? "Switches on the user's shield/glove element guard: turns"
                                : "Switches on the user's helmet element mirror: turns"));
                        break;
                    case 0x54:
                        list.Add(Val(ScriptValue.KIND_SPELL, p + 1, 1, 255, "Casts spell"));
                        break;
                    case 0x13:
                        list.Add(Val(ScriptValue.KIND_INFO, p, 0, 0, "Formula: take the target's " + StatType(data[p + 2]) + " (into " + RegName(data[p + 1]) + ")"));
                        break;
                    case 0x15:
                        list.Add(Val(ScriptValue.KIND_INFO, p, 0, 0, "Formula: take the user's " + StatType(data[p + 2]) + " (into " + RegName(data[p + 1]) + ")"));
                        break;
                    case 0x55:
                        if ((data[p + 3] & 0x80) != 0) break;
                        list.Add(Val(ScriptValue.KIND_NUMBER, p + 2, 2, 0x7FFF, "Formula: divide " + RegName(data[p + 1]) + " (whole number)"));
                        break;
                    case 0x0D:
                    case 0x0E:
                    case 0x0F:
                    case 0x10:
                        if ((data[p + 3] & 0x80) != 0) break; // operand is another register, not a number
                        string verb = op == 0x0D ? "add to" : op == 0x0E ? "subtract from" : op == 0x0F ? "multiply" : "divide";
                        list.Add(Val(ScriptValue.KIND_NUMBER, p + 2, 2, 0x7FFF, "Formula: " + verb + " " + RegName(data[p + 1])));
                        break;
                }
                for (int k = before; k < list.Count; k++) valueBlock.Add(block);
                if (boundary) block++;
            }
            // Label values with the skill of their block (not the skill line itself).
            for (int k = 0; k < list.Count; k++)
            {
                string nm;
                if (list[k].Kind != ScriptValue.KIND_SKILL && list[k].Kind != ScriptValue.KIND_CAPSKILL && blockSkill.TryGetValue(valueBlock[k], out nm))
                    list[k].Label = "[" + nm + "] " + list[k].Label;
            }
            return list;
        }

        /// <summary>Readable name for an element value, including combinations ("Fire + Ice").</summary>
        public static string DescribeElement(int value)
        {
            int idx = Array.IndexOf(ElementValues, value);
            if (idx >= 0) return ElementNames[idx];
            List<string> parts = new List<string>();
            int rest = value;
            for (int k = 1; k < ElementValues.Length; k++)
            {
                if ((value & ElementValues[k]) != 0) { parts.Add(ElementNames[k]); rest &= ~ElementValues[k]; }
            }
            if (rest != 0) parts.Add(rest.ToString("X4"));
            return string.Join(" + ", parts.ToArray());
        }

        public static int GetScriptValue(byte[] data, ScriptValue v)
        {
            if (v.Kind == ScriptValue.KIND_SIGNED) return (sbyte)data[v.Offset];
            if (v.Kind == ScriptValue.KIND_ELEMENT) return (data[v.Offset] << 8) | data[v.Offset + 1];
            if (v.Size == 1) return data[v.Offset];
            return data[v.Offset] | (data[v.Offset + 1] << 8);
        }

        public static void SetScriptValue(byte[] data, ScriptValue v, int value)
        {
            if (v.Kind == ScriptValue.KIND_ELEMENT)
            {
                data[v.Offset] = (byte)((value >> 8) & 0xFF);
                data[v.Offset + 1] = (byte)(value & 0xFF);
                return;
            }
            if (v.Kind == ScriptValue.KIND_SIGNED)
            {
                if (value < -128) value = -128;
                if (value > 127) value = 127;
                data[v.Offset] = (byte)(value & 0xFF);
                return;
            }
            if (value < 0) value = 0;
            if (value > v.Max) value = v.Max;
            data[v.Offset] = (byte)(value & 0xFF);
            if (v.Size == 2) data[v.Offset + 1] = (byte)((value >> 8) & 0xFF);
        }

        private static int ProtectIndex(int reg)
        {
            int d = reg - FirstProtectRegister;
            if (d < 0 || (d & 1) != 0) return -1;
            d >>= 1;
            return d < ProtectableCount ? d : -1;
        }

        public static int GetStatus(byte[] data, StatusEffect e)
        {
            if (e.Kind == StatusEffect.KIND_PROTECT) return ProtectIndex(data[e.Offset + 1]);
            return data[e.Offset + 1];
        }

        public static int GetValue(byte[] data, StatusEffect e)
        {
            if (e.Kind == StatusEffect.KIND_PROTECT) return data[e.Offset + 2] | (data[e.Offset + 3] << 8);
            return data[e.Offset + 2];
        }

        public static void SetStatus(byte[] data, StatusEffect e, int status)
        {
            if (e.Kind == StatusEffect.KIND_PROTECT)
            {
                if (status < 0 || status >= ProtectableCount) return;
                data[e.Offset + 1] = (byte)(FirstProtectRegister + status * 2);
            }
            else
            {
                if (status < 0 || status >= StatusNames.Length) return;
                data[e.Offset + 1] = (byte)status;
            }
        }

        public static void SetValue(byte[] data, StatusEffect e, int value)
        {
            if (!e.Editable) return;
            if (value < 0) value = 0;
            if (e.Kind == StatusEffect.KIND_PROTECT)
            {
                if (value > 0x7FFF) value = 0x7FFF;
                data[e.Offset + 2] = (byte)(value & 0xFF);
                data[e.Offset + 3] = (byte)(value >> 8);
            }
            else
            {
                if (value > 255) value = 255;
                data[e.Offset + 2] = (byte)value;
            }
        }
    }
}
