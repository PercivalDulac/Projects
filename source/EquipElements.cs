using System;
using System.Collections.Generic;

namespace com.lufiaIIEditor.RomInfo
{
    /// <summary>
    /// Elements on equipment, found inside the weapon and armor effect scripts.
    ///
    /// Weapon (field 2):  1F hi lo                 the attack gets this element.
    ///                    17 27 hi lo              same, by OR-ing it into register 27 (Undead ring).
    /// Armor  (field 3):  46 f tt 00               if battle flag f of the wearer is 0, jump to record + tt
    ///                    0C 81 hi lo              element(s) to guard against
    ///                    42 ss 00                 shared script ss: 18 halves, 19 blocks, 1A reflects
    ///                    00                       ...the jump lands after this
    ///                    42 hh 00                 always-on shared script: halves / doubles one element
    /// Flag 2 is set for 5 turns by the guard IP moves (Flame guard ... Holy wall, Fury force,
    /// Miracle care, Ancient power), flag 3 by the mirror IP moves (Flame return ... Brilliance).
    /// The shared scripts are the table at $96:FADD. A few items use their own small check
    /// (Pearl brace: 0C 80 27 80 / 06 80 hi lo tt 00 ... 10 2A 02 00 = halves one element).
    /// Only values that are already in an item's script can be changed; nothing is added or moved.
    /// </summary>
    class EquipElements
    {
        public int AttackOffset = -1;       // weapon 1F: element bytes (hi, lo)
        public int AttackModeOffset = -1;   // weapon script start when it is "23 lo hi 00" or "37 00 00 00"
        public int GuardFlagOffset = -1;    // 46: flag number byte
        public int GuardElementOffset = -1; // 0C 81: element bytes
        public int GuardActionOffset = -1;  // 42: shared script number (18/19/1A)
        public int AlwaysOffset = -1;       // 42: shared script number (halve/double list)
        public int ExactElementOffset = -1; // 06 80 hi lo: halves exactly this element
        public int TriggerFlag = -1;        // first 46 2/3 in the armor script, whatever follows it

        public bool Any
        {
            get
            {
                return AttackOffset >= 0 || GuardElementOffset >= 0 || AlwaysOffset >= 0 || ExactElementOffset >= 0;
            }
        }

        // Guard actions (shared scripts 18, 19, 1A).
        public static readonly int[] GuardActions = { 0x18, 0x19, 0x1A };
        public static readonly string[] GuardActionNames = { "halves", "blocks", "reflects" };

        // What triggers the guard (battle flag 2 or 3).
        public static readonly int[] GuardFlags = { 2, 3 };
        public static readonly string[] GuardFlagNames = {
            "Guard IPs (Flame guard...)", "Mirror IPs (Flame return...)" };

        // Always-on shared scripts. All are 3 bytes (42 nn 00), so any can replace any other.
        public static readonly int[] AlwaysScripts = {
            0x06, 0x07, 0x08, 0x09, 0x0A, 0x0E, 0x14, 0x0B,
            0x00, 0x01, 0x02, 0x03, 0x05, 0x0D };
        public static readonly string[] AlwaysNames = {
            "Halves Fire damage", "Halves Thunder damage", "Halves Water damage", "Halves Ice damage",
            "Halves Light damage", "Halves Shadow damage", "Halves all elemental damage", "Halves non-elemental damage",
            "Takes double Fire damage", "Takes double Thunder damage", "Takes double Water damage",
            "Takes double Ice damage", "Takes double Light damage", "Takes double Shadow damage" };

        private static int ElementAt(byte[] d, int o) { return (d[o] << 8) | d[o + 1]; }

        public static EquipElements Find(ItemData items, int id)
        {
            EquipElements e = new EquipElements();
            byte[] d = items.RawData;
            int s, end;
            if (items.GetScriptRange(id, ItemData.FIELD_WEAPON_SCRIPT, out s, out end))
            {
                // 37 = normal attack (power = wielder's ATP total, $85:BCED); 23 lo hi = fixed power,
                // ignores ATP (Bunny sword: 120). Both fit in 3 bytes + the script's end 00.
                if (s + 3 < end && d[s + 3] == 0x00 &&
                    (d[s] == 0x23 || (d[s] == 0x37 && d[s + 1] == 0x00 && d[s + 2] == 0x00)))
                    e.AttackModeOffset = s;
                for (int p = s; p < end; )
                {
                    int n = ScriptTools.InstructionSize(d, p);
                    if (n <= 0 || p + n > end) break;
                    if (d[p] == 0x1F && e.AttackOffset < 0) e.AttackOffset = p + 1;
                    // 17 27 hi lo: OR the element into the attack's element register (Undead ring: Light).
                    if (d[p] == 0x17 && d[p + 1] == 0x27 && e.AttackOffset < 0) e.AttackOffset = p + 2;
                    if (d[p] == 0x00) break;   // weapon scripts end at their first 00
                    p += n;
                }
            }
            if (items.GetScriptRange(id, ItemData.FIELD_ARMOR_SCRIPT, out s, out end))
            {
                List<int> ops = new List<int>();
                for (int p = s; p < end; )
                {
                    int n = ScriptTools.InstructionSize(d, p);
                    if (n <= 0 || p + n > end) break;
                    ops.Add(p);
                    p += n;
                }
                for (int k = 0; k < ops.Count; k++)
                {
                    int p = ops[k];
                    if (d[p] == 0x46 && (d[p + 1] == 2 || d[p + 1] == 3) && TriggerUnset(e)) e.TriggerFlag = d[p + 1];
                    // 46 f tt 00 | 0C 81 hi lo | 42 ss 00
                    if (d[p] == 0x46 && (d[p + 1] == 2 || d[p + 1] == 3) && k + 2 < ops.Count &&
                        d[ops[k + 1]] == 0x0C && d[ops[k + 1] + 1] == 0x81 &&
                        d[ops[k + 2]] == 0x42 && Array.IndexOf(GuardActions, (int)d[ops[k + 2] + 1]) >= 0 && d[ops[k + 2] + 2] == 0 &&
                        e.GuardElementOffset < 0)
                    {
                        e.GuardFlagOffset = p + 1;
                        e.GuardElementOffset = ops[k + 1] + 2;
                        e.GuardActionOffset = ops[k + 2] + 1;
                        k += 2;
                        continue;
                    }
                    // 0C 80 27 80 | 06 80 hi lo tt 00 | 00 | 10 2A 02 00
                    if (d[p] == 0x0C && d[p + 1] == 0x80 && d[p + 2] == 0x27 && d[p + 3] == 0x80 && k + 3 < ops.Count &&
                        d[ops[k + 1]] == 0x06 && d[ops[k + 1] + 1] == 0x80 &&
                        d[ops[k + 2]] == 0x00 &&
                        d[ops[k + 3]] == 0x10 && d[ops[k + 3] + 1] == 0x2A && d[ops[k + 3] + 2] == 0x02 &&
                        e.ExactElementOffset < 0)
                    {
                        e.ExactElementOffset = ops[k + 1] + 2;
                        k += 3;
                    }
                }
                FindAlways(items, id, e, ops, s);
            }
            return e;
        }

        private static bool TriggerUnset(EquipElements e) { return e.TriggerFlag < 0; }

        /// <summary>What an IP move that sets this item's flag switches on, in a few words.</summary>
        public string GuardSummary(byte[] d)
        {
            if (GuardElementOffset >= 0)
                return GuardActionName(GetGuardAction(d)) + " " + ScriptTools.DescribeElement(GetGuardElement(d));
            return "its own special effect";
        }

        /// <summary>
        /// The always-on part: where the script continues when the guard flag is off (record + tt),
        /// or the script start when there's no guard. Only a 42 nn 00 reached without any condition
        /// counts (Mega shield's runs only sometimes, so it's left alone).
        /// </summary>
        private static void FindAlways(ItemData items, int id, EquipElements e, List<int> ops, int s)
        {
            byte[] d = items.RawData;
            int from = s;
            if (e.GuardFlagOffset >= 0)
            {
                int p46 = e.GuardFlagOffset - 1;
                from = items.RecordOffset(id) + (d[p46 + 2] | (d[p46 + 3] << 8));
            }
            int k = ops.IndexOf(from);
            if (k < 0) return;
            for (; k < ops.Count; k++)
            {
                int p = ops[k], op = d[p];
                if (op == 0x00 || (op >= 0x06 && op <= 0x0B) || op == 0x46) return;
                if (op == 0x42 && d[p + 2] == 0 && Array.IndexOf(AlwaysScripts, (int)d[p + 1]) >= 0)
                {
                    e.AlwaysOffset = p + 1;
                    return;
                }
            }
        }

        public bool IsFixedPower(byte[] d) { return AttackModeOffset >= 0 && d[AttackModeOffset] == 0x23; }
        public int GetFixedPower(byte[] d)
        {
            return IsFixedPower(d) ? d[AttackModeOffset + 1] | (d[AttackModeOffset + 2] << 8) : 0;
        }

        /// <summary>Fixed power (23 lo hi) or the normal ATP-based attack (37 00 00).</summary>
        public void SetAttackMode(byte[] d, bool fixedPower, int power)
        {
            if (AttackModeOffset < 0) return;
            int o = AttackModeOffset;
            if (fixedPower)
            {
                if (power < 0) power = 0;
                if (power > 0x7FFF) power = 0x7FFF;
                d[o] = 0x23; d[o + 1] = (byte)power; d[o + 2] = (byte)(power >> 8);
            }
            else
            {
                d[o] = 0x37; d[o + 1] = 0; d[o + 2] = 0;
            }
        }

        public int GetAttack(byte[] d) { return AttackOffset < 0 ? -1 : ElementAt(d, AttackOffset); }
        public int GetGuardElement(byte[] d) { return GuardElementOffset < 0 ? -1 : ElementAt(d, GuardElementOffset); }
        public int GetGuardAction(byte[] d) { return GuardActionOffset < 0 ? -1 : d[GuardActionOffset]; }
        public int GetGuardFlag(byte[] d) { return GuardFlagOffset < 0 ? -1 : d[GuardFlagOffset]; }
        public int GetAlways(byte[] d) { return AlwaysOffset < 0 ? -1 : d[AlwaysOffset]; }
        public int GetExact(byte[] d) { return ExactElementOffset < 0 ? -1 : ElementAt(d, ExactElementOffset); }

        private static void PutElement(byte[] d, int o, int v)
        {
            if (o < 0 || v < 0) return;
            d[o] = (byte)((v >> 8) & 0xFF);
            d[o + 1] = (byte)(v & 0xFF);
        }

        public void SetAttack(byte[] d, int v) { PutElement(d, AttackOffset, v); }
        public void SetGuardElement(byte[] d, int v) { PutElement(d, GuardElementOffset, v); }
        public void SetExact(byte[] d, int v) { PutElement(d, ExactElementOffset, v); }
        public void SetGuardAction(byte[] d, int v)
        {
            if (GuardActionOffset >= 0 && Array.IndexOf(GuardActions, v) >= 0) d[GuardActionOffset] = (byte)v;
        }
        public void SetGuardFlag(byte[] d, int v)
        {
            if (GuardFlagOffset >= 0 && (v == 2 || v == 3)) d[GuardFlagOffset] = (byte)v;
        }
        public void SetAlways(byte[] d, int v)
        {
            if (AlwaysOffset >= 0 && Array.IndexOf(AlwaysScripts, v) >= 0) d[AlwaysOffset] = (byte)v;
        }

        public static string AlwaysName(int v)
        {
            int k = Array.IndexOf(AlwaysScripts, v);
            return k >= 0 ? AlwaysNames[k] : "shared script " + v.ToString("X2");
        }

        public static string GuardActionName(int v)
        {
            int k = Array.IndexOf(GuardActions, v);
            return k >= 0 ? GuardActionNames[k] : "script " + v.ToString("X2");
        }

        /// <summary>One line per element setting, for the changes report.</summary>
        public List<string> Describe(byte[] d)
        {
            List<string> l = new List<string>();
            if (AttackModeOffset >= 0) l.Add(IsFixedPower(d) ? "attacks with fixed power " + GetFixedPower(d) : "attacks with the wielder's ATP");
            if (AttackOffset >= 0) l.Add("attack element " + ScriptTools.DescribeElement(GetAttack(d)));
            if (GuardElementOffset >= 0)
                l.Add("guard: " + ScriptTools.DescribeElement(GetGuardElement(d)) + " - " + GuardActionName(GetGuardAction(d)) +
                      (GetGuardFlag(d) == 3 ? " (mirror IP)" : " (guard IP)"));
            if (AlwaysOffset >= 0) l.Add("always: " + AlwaysName(GetAlways(d)));
            if (ExactElementOffset >= 0) l.Add("always halves " + ScriptTools.DescribeElement(GetExact(d)));
            return l;
        }
    }
}
