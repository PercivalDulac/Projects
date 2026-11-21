using System;
using System.Collections.Generic;
using System.Text;

namespace com.lufiaIIEditor.RomInfo
{
    /// <summary>One editable number found inside an item's effect script.</summary>
    class ScriptParam
    {
        public int Offset;   // absolute file offset of the value
        public int Size;     // 1 or 2 bytes (little-endian)
        public int Max;
        public string Label;
    }

    /// <summary>
    /// Reads and writes item records (467 items).
    ///
    /// Pointer table: $96:CF69 (file 0xB5169 headered), 16-bit pointers relative to the table.
    /// Looked up by $81:F291, unpacked by $81:F1C5.
    ///
    /// Record:
    ///   +0..+4  flags / icon (not edited)
    ///   +5..+6  price
    ///   +7      equip slot: 01 weapon, 02 armor, 04 shield, 08 helmet, 10 ring, 20 jewel,
    ///           40 dungeon tool, 00 not equippable
    ///   +8      who can equip: bit0 Maxim, 1 Selan, 2 Guy, 3 Artea, 4 Tia, 5 Dekar, 6 Lexis
    ///   +9..+C  32-bit field mask
    ///   +D..    one 16-bit value per set mask bit, in bit order, then effect scripts
    ///
    /// Mask bits: 0 menu-use script offset, 1 battle-use script offset, 2 weapon effect
    /// script offset, 3 armor effect script offset (offsets relative to record start),
    /// 4..10 ATP, DFP, STR, AGL, INT, GUT, MGR bonuses (summed by $82:F722), 15 IP move ID.
    ///
    /// Names: separate fixed table of 12-character names at file 0xF49E8.
    /// </summary>
    class ItemData
    {
        public const int Count = 467;
        public const int NameLength = 12;

        private const int PointerTableHeadered = 0xB5169;
        private const int NameTableHeadered = 0xF49E8;

        public const int OFS_PRICE = 5;
        public const int OFS_SLOT = 7;
        public const int OFS_EQUIP = 8;
        public const int OFS_MASK = 9;
        public const int OFS_FIELDS = 13;

        public const int FIELD_MENU_SCRIPT = 0;
        public const int FIELD_BATTLE_SCRIPT = 1;
        public const int FIELD_ATP = 4;      // ATP..MGR = 4..10
        public const int FIELD_IP = 15;

        public static readonly string[] BonusNames = { "ATP", "DFP", "STR", "AGL", "INT", "GUT", "MGR" };
        public static readonly string[] CharacterNames = { "Maxim", "Selan", "Guy", "Artea", "Tia", "Dekar", "Lexis" };

        // Fields that share the "bonus slot" pool: the 7 stat bonuses and the IP move.
        public static readonly int[] PoolFields = { 4, 5, 6, 7, 8, 9, 10, 15 };

        private readonly byte[] data;
        private readonly int pointerTable;
        private readonly int nameTable;

        public ItemData(byte[] romData, bool hasHeader)
        {
            data = romData;
            int adjust = hasHeader ? 0 : -0x200;
            pointerTable = PointerTableHeadered + adjust;
            nameTable = NameTableHeadered + adjust;
        }

        public string Validate()
        {
            if (data.Length < nameTable + Count * NameLength) return "ROM is too small.";
            int prev = -1;
            for (int i = 0; i < Count; i++)
            {
                int o = RecordOffset(i);
                if (o <= prev || o + OFS_FIELDS > data.Length) return "Item pointer " + i + " looks wrong.";
                int slot = data[o + OFS_SLOT];
                if (slot != 0 && slot != 1 && slot != 2 && slot != 4 && slot != 8 && slot != 0x10 && slot != 0x20 && slot != 0x40)
                    return "Item " + i + " has an unexpected equip slot.";
                prev = o;
            }
            if (GetName(0) != "No equip") return "Item name table not found.";
            return null;
        }

        public int RecordOffset(int i)
        {
            return pointerTable + (data[pointerTable + i * 2] | (data[pointerTable + i * 2 + 1] << 8));
        }

        /// <summary>First byte after this record (start of the next one).</summary>
        public int RecordEnd(int i)
        {
            if (i + 1 < Count) return RecordOffset(i + 1);
            return FieldsEnd(i); // last record: don't trust anything past its fields
        }

        // ---------------- names ----------------

        /// <summary>File offset of an item's 12-letter name.</summary>
        public int NameOffset(int i) { return nameTable + i * NameLength; }

        public string GetName(int i)
        {
            StringBuilder sb = new StringBuilder();
            int o = nameTable + i * NameLength;
            for (int k = 0; k < NameLength; k++) sb.Append((char)data[o + k]);
            return sb.ToString().TrimEnd(' ');
        }

        public void SetName(int i, string name)
        {
            int o = nameTable + i * NameLength;
            if (name == null) name = "";
            for (int k = 0; k < NameLength; k++)
            {
                char c = k < name.Length ? name[k] : ' ';
                if (c < 0x20 || c > 0x7E) c = ' ';
                data[o + k] = (byte)c;
            }
        }

        // ---------------- fixed bytes ----------------

        public int GetPrice(int i) { int o = RecordOffset(i); return data[o + OFS_PRICE] | (data[o + OFS_PRICE + 1] << 8); }

        public void SetPrice(int i, int v)
        {
            if (v < 0) v = 0; if (v > 65535) v = 65535;
            int o = RecordOffset(i);
            data[o + OFS_PRICE] = (byte)(v & 0xFF);
            data[o + OFS_PRICE + 1] = (byte)(v >> 8);
        }

        public int GetSlot(int i) { return data[RecordOffset(i) + OFS_SLOT]; }

        /// <summary>
        /// Can this item ever be in the Ancient Cave's random equipment lists ($83:907F)?
        /// Needs record +0 bit 02 and not bit 20 (key items, Iris treasures); not changed here.
        /// </summary>
        public bool CaveCapable(int i) { int o = RecordOffset(i); return (data[o] & 0x02) != 0 && (data[o] & 0x20) == 0; }

        /// <summary>Record +1 bit 20 keeps an item out of the cave's random lists (the game sets it on
        /// rare gear, which only comes from the blue-chest table). Only the cave code reads this bit.</summary>
        public bool GetInCaveLists(int i) { return (data[RecordOffset(i) + 1] & 0x20) == 0; }

        public void SetInCaveLists(int i, bool on)
        {
            int o = RecordOffset(i) + 1;
            data[o] = (byte)(on ? (data[o] & ~0x20) : (data[o] | 0x20));
        }
        public int GetEquipMask(int i) { return data[RecordOffset(i) + OFS_EQUIP]; }
        public void SetEquipMask(int i, int v) { data[RecordOffset(i) + OFS_EQUIP] = (byte)(v & 0x7F); }

        public bool IsEquipment(int i)
        {
            int s = GetSlot(i);
            return s != 0 && s != 0x40;
        }

        public static string SlotName(int slot)
        {
            switch (slot)
            {
                case 0x01: return "Weapon";
                case 0x02: return "Armor";
                case 0x04: return "Shield";
                case 0x08: return "Helmet";
                case 0x10: return "Ring";
                case 0x20: return "Jewel";
                case 0x40: return "Dungeon tool";
                case 0x00: return "Item";
                default: return "Slot " + slot.ToString("X2");
            }
        }

        public string FixedBytesHex(int i)
        {
            int o = RecordOffset(i);
            StringBuilder sb = new StringBuilder();
            for (int k = 0; k < 5; k++) sb.Append(data[o + k].ToString("X2")).Append(' ');
            return sb.ToString().Trim();
        }

        // ---------------- mask fields ----------------

        public uint GetMask(int i)
        {
            int o = RecordOffset(i) + OFS_MASK;
            return (uint)(data[o] | (data[o + 1] << 8) | (data[o + 2] << 16) | (data[o + 3] << 24));
        }

        private void SetMask(int i, uint m)
        {
            int o = RecordOffset(i) + OFS_MASK;
            data[o] = (byte)(m & 0xFF);
            data[o + 1] = (byte)((m >> 8) & 0xFF);
            data[o + 2] = (byte)((m >> 16) & 0xFF);
            data[o + 3] = (byte)((m >> 24) & 0xFF);
        }

        private static int PopCount(uint m)
        {
            int c = 0;
            while (m != 0) { c += (int)(m & 1); m >>= 1; }
            return c;
        }

        public int FieldsEnd(int i)
        {
            return RecordOffset(i) + OFS_FIELDS + 2 * PopCount(GetMask(i));
        }

        /// <summary>All present fields: bit -> value.</summary>
        public SortedDictionary<int, int> GetFields(int i)
        {
            SortedDictionary<int, int> result = new SortedDictionary<int, int>();
            uint m = GetMask(i);
            int p = RecordOffset(i) + OFS_FIELDS;
            for (int bit = 0; bit < 32; bit++)
            {
                if (((m >> bit) & 1) != 0)
                {
                    result[bit] = data[p] | (data[p + 1] << 8);
                    p += 2;
                }
            }
            return result;
        }

        private static bool IsPoolField(int bit)
        {
            return Array.IndexOf(PoolFields, bit) >= 0;
        }

        /// <summary>How many bonus/IP values this item can hold (fixed by its record size).</summary>
        public int PoolCapacity(int i)
        {
            int n = 0;
            foreach (int bit in GetFields(i).Keys) if (IsPoolField(bit)) n++;
            return n;
        }

        /// <summary>
        /// Writes stat bonuses and IP move. values[bit] for bits in PoolFields; zero = not used.
        /// The number of fields stays the same, so the record never changes size and its
        /// scripts stay where they are. Returns false (and writes nothing) if more non-zero
        /// values are requested than the item has slots for.
        /// </summary>
        public bool SetPoolValues(int i, Dictionary<int, int> values)
        {
            SortedDictionary<int, int> fields = GetFields(i);
            int capacity = 0;
            foreach (int bit in fields.Keys) if (IsPoolField(bit)) capacity++;

            List<int> used = new List<int>();
            foreach (int bit in PoolFields)
            {
                int v;
                if (values.TryGetValue(bit, out v) && v != 0) used.Add(bit);
            }
            if (used.Count > capacity) return false;

            // Rebuild: keep every non-pool field; pool = the used ones, padded with
            // zero-valued fields (prefer bits the item already had, then stat bits) so the
            // total count is unchanged.
            SortedDictionary<int, int> rebuilt = new SortedDictionary<int, int>();
            foreach (KeyValuePair<int, int> kv in fields)
                if (!IsPoolField(kv.Key)) rebuilt[kv.Key] = kv.Value;
            foreach (int bit in used) rebuilt[bit] = values[bit] & 0xFFFF;

            int needed = capacity - used.Count;
            foreach (int bit in fields.Keys)
            {
                if (needed == 0) break;
                if (IsPoolField(bit) && !rebuilt.ContainsKey(bit)) { rebuilt[bit] = 0; needed--; }
            }
            foreach (int bit in PoolFields)
            {
                if (needed == 0) break;
                if (!rebuilt.ContainsKey(bit)) { rebuilt[bit] = 0; needed--; }
            }

            uint mask = 0;
            foreach (int bit in rebuilt.Keys) mask |= (1u << bit);
            if (PopCount(mask) != PopCount(GetMask(i))) return false; // safety net

            SetMask(i, mask);
            int p = RecordOffset(i) + OFS_FIELDS;
            foreach (KeyValuePair<int, int> kv in rebuilt)
            {
                data[p] = (byte)(kv.Value & 0xFF);
                data[p + 1] = (byte)((kv.Value >> 8) & 0xFF);
                p += 2;
            }
            return true;
        }

        // ---------------- effect scripts ----------------

        private static readonly string[] StatNames = {
            "current HP", "current MP", "max HP", "max MP", "ATP", "DFP", "STR", "AGL", "INT", "GUT", "MGR",
            "IP", "Level", "temp ATP", "temp DFP", "temp STR", "temp AGL", "temp INT", "temp GUT", "temp MGR" };
        private static readonly string[] AilmentNames = {
            "Poison", "Silence", "Death", "Paralysis", "Confusion", "Sleep", "Status 6", "Mirror" };

        private static string StatName(int b) { return b < StatNames.Length ? StatNames[b] : "stat " + b.ToString("X2"); }
        private static string AilmentName(int b) { return b < AilmentNames.Length ? AilmentNames[b] : "status " + b.ToString("X2"); }

        /// <summary>Opcode lengths (in bytes, including the opcode). Same script language as enemy AI.</summary>
        private static int OpLength(int op)
        {
            switch (op)
            {
                case 0x00: case 0x01: case 0x28: case 0x29: case 0x2A: case 0x37: return 1;
                case 0x50: return 1; // only in Regain/Miracle battle use: lets them target fallen allies (inferred)
                case 0x35: return 2;
                case 0x03: case 0x04: case 0x23: case 0x25: case 0x26: case 0x27: case 0x42: return 3;
                case 0x0C: case 0x17: return 4;
                case 0x24: return 5;
                case 0x21: return 6;
                default: return -1; // unknown: stop decoding
            }
        }

        /// <summary>
        /// Walks a script and collects the numbers that are safe to edit. Stops at the end
        /// marker or at the first opcode it doesn't know; 'complete' tells which.
        /// </summary>
        public List<ScriptParam> ReadScriptParams(int pos, int end, string section, out bool complete)
        {
            List<ScriptParam> list = new List<ScriptParam>();
            complete = false;
            while (pos < end)
            {
                int op = data[pos];
                int n;
                if (op == 0x32)
                    n = (pos + 3 < end && data[pos + 2] == 0x32 && data[pos + 3] == 0x05) ? 4 : 2;
                else
                    n = OpLength(op);
                if (n < 0 || pos + n > end) return list;

                if (op == 0x24)
                {
                    string s = StatName(data[pos + 1]);
                    list.Add(MakeParam(pos + 2, 2, 65535, section + ": restore " + s + " (amount)"));
                    list.Add(MakeParam(pos + 4, 1, 255, section + ": restore " + s + " (random extra)"));
                }
                else if (op == 0x25)
                {
                    list.Add(MakeParam(pos + 2, 1, 255, section + ": raise " + StatName(data[pos + 1]) + " by % (raw byte; 128+ lowers)"));
                }
                else if (op == 0x26)
                {
                    list.Add(MakeParam(pos + 2, 1, 255, section + ": cure " + AilmentName(data[pos + 1]) + " (% chance)"));
                }
                else if (op == 0x27)
                {
                    list.Add(MakeParam(pos + 2, 1, 255, section + ": inflict " + AilmentName(data[pos + 1]) + " (% chance)"));
                }
                else if (op == 0x21)
                {
                    list.Add(MakeParam(pos + 3, 2, 65535, section + ": damage (base amount)"));
                }

                if (op == 0x00) { complete = true; return list; }
                pos += n;
            }
            return list;
        }

        private static ScriptParam MakeParam(int offset, int size, int max, string label)
        {
            ScriptParam p = new ScriptParam();
            p.Offset = offset; p.Size = size; p.Max = max; p.Label = label;
            return p;
        }

        /// <summary>Editable values of the menu-use and battle-use scripts.</summary>
        public List<ScriptParam> GetEffectParams(int i, out bool complete)
        {
            List<ScriptParam> all = new List<ScriptParam>();
            complete = true;
            SortedDictionary<int, int> f = GetFields(i);
            int start = RecordOffset(i);
            int end = RecordEnd(i);
            int fieldsEnd = FieldsEnd(i);
            Dictionary<int, bool> seen = new Dictionary<int, bool>();

            int[] scriptFields = { FIELD_MENU_SCRIPT, FIELD_BATTLE_SCRIPT };
            string[] sectionNames = { "Menu use", "Battle use" };
            for (int k = 0; k < 2; k++)
            {
                int rel;
                if (!f.TryGetValue(scriptFields[k], out rel)) continue;
                int pos = start + rel;
                if (pos < fieldsEnd || pos >= end) { complete = false; continue; }
                bool c;
                foreach (ScriptParam p in ReadScriptParams(pos, end, sectionNames[k], out c))
                {
                    if (!seen.ContainsKey(p.Offset)) { seen[p.Offset] = true; all.Add(p); }
                }
                if (!c) complete = false;
            }
            return all;
        }

        public const int FIELD_WEAPON_SCRIPT = 2;
        public const int FIELD_ARMOR_SCRIPT = 3;

        /// <summary>
        /// Where one of the item's scripts (field 0-3) starts and ends: it runs until the next
        /// script of the same item begins, or to the end of the record.
        /// </summary>
        public bool GetScriptRange(int i, int field, out int start, out int end)
        {
            start = 0; end = 0;
            SortedDictionary<int, int> f = GetFields(i);
            int rel;
            if (!f.TryGetValue(field, out rel)) return false;
            int rec = RecordOffset(i);
            start = rec + rel;
            end = RecordEnd(i);
            if (start < FieldsEnd(i) || start >= end) return false;
            for (int k = 0; k < 4; k++)
            {
                int other;
                if (k == field || !f.TryGetValue(k, out other)) continue;
                int o = rec + other;
                if (o > start && o < end) end = o;
            }
            return true;
        }

        /// <summary>Status protections / on-hit statuses in the weapon and armor scripts.</summary>
        public List<StatusEffect> GetStatusEffects(int i, int field)
        {
            int s, e;
            if (!GetScriptRange(i, field, out s, out e)) return new List<StatusEffect>();
            return ScriptTools.FindStatusEffects(data, s, e);
        }

        public byte[] RawData { get { return data; } }

        public int GetParam(ScriptParam p)
        {
            return p.Size == 1 ? data[p.Offset] : (data[p.Offset] | (data[p.Offset + 1] << 8));
        }

        public void SetParam(ScriptParam p, int v)
        {
            if (v < 0) v = 0;
            if (v > p.Max) v = p.Max;
            data[p.Offset] = (byte)(v & 0xFF);
            if (p.Size == 2) data[p.Offset + 1] = (byte)((v >> 8) & 0xFF);
        }
    }
}
