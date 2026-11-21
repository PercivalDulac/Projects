using System;
using System.Collections.Generic;
using System.Text;

namespace com.lufiaIIEditor.RomInfo
{
    /// <summary>
    /// Enemy records, found through the game's own pointer table at $96:85C0 (file 0xB07C0
    /// headered; 224 words, relative to the table start). Read by $81:FC2A when a battle starts.
    ///   +00  name (13)                 +0D level (as the original editor)   +0E ?   +0F sprite
    ///   +10  pattern   +11 max HP   +13 max MP   +15 ATP   +17 DFP   (words)
    ///   +19  AGL  +1A INT  +1B GUT  +1C MGR   (bytes; the game doubles them: $81:FC81)
    ///   +1D  EXP (word)   +1F gold (word)
    ///   +21  tagged words, ending in 00:  03 = item drop, 07 = AI script offset, 08 = reaction script
    /// Item drop ($81:FB32): word = item (9 bits) | chance &lt;&lt; 9. The item drops if a roll of
    /// 0-99 is below the chance AND a second 1-in-4 roll succeeds, so the real rate is chance / 4 %.
    /// </summary>
    class EnemyData
    {
        public const int Count = 224;
        public const int NameLength = 13;
        public const int OFS_LEVEL = 0x0D, OFS_SPRITE = 0x0F, OFS_PATTERN = 0x10, OFS_HP = 0x11, OFS_MP = 0x13,
            OFS_ATP = 0x15, OFS_DFP = 0x17, OFS_AGL = 0x19, OFS_INT = 0x1A, OFS_GUT = 0x1B, OFS_MGR = 0x1C,
            OFS_EXP = 0x1D, OFS_GOLD = 0x1F, OFS_TAGS = 0x21;

        private readonly byte[] data;
        private readonly int table, endOfLast;
        private int[] offsets;

        public EnemyData(byte[] romData, bool hasHeader)
        {
            data = romData;
            int adjust = hasHeader ? 0 : -0x200;
            table = 0xB07C0 + adjust;
            endOfLast = 0xB5168 + adjust;
        }

        public byte[] RawData { get { return data; } }

        public string Validate()
        {
            if (data.Length < endOfLast) return "ROM is too small.";
            offsets = new int[Count + 1];
            for (int i = 0; i < Count; i++)
            {
                int o = table + (data[table + 2 * i] | (data[table + 2 * i + 1] << 8));
                if (o < table + Count * 2 || o >= endOfLast) return "The enemy table isn't where expected.";
                if (i > 0 && o <= offsets[i - 1]) return "The enemy table isn't in order.";
                offsets[i] = o;
            }
            offsets[Count] = endOfLast;
            return null;
        }

        public int RecordOffset(int i) { return offsets[i]; }
        public int RecordEnd(int i) { return offsets[i + 1]; }

        public string GetName(int i)
        {
            StringBuilder sb = new StringBuilder();
            for (int k = 0; k < NameLength; k++) sb.Append((char)data[offsets[i] + k]);
            return sb.ToString().TrimEnd(' ', '\0');
        }

        public void SetName(int i, string name)
        {
            if (name == null) name = "";
            if (name.Length > NameLength) name = name.Substring(0, NameLength);
            for (int k = 0; k < NameLength; k++)
            {
                char c = k < name.Length ? name[k] : ' ';
                data[offsets[i] + k] = (byte)(c < 0x20 || c > 0x7E ? ' ' : c);
            }
        }

        public int GetByte(int i, int ofs) { return data[offsets[i] + ofs]; }
        public void SetByte(int i, int ofs, int v) { data[offsets[i] + ofs] = (byte)Math.Max(0, Math.Min(255, v)); }
        public int GetWord(int i, int ofs) { int o = offsets[i] + ofs; return data[o] | (data[o + 1] << 8); }
        public void SetWord(int i, int ofs, int v)
        {
            v = Math.Max(0, Math.Min(0xFFFF, v));
            int o = offsets[i] + ofs;
            data[o] = (byte)v; data[o + 1] = (byte)(v >> 8);
        }

        /// <summary>File offset of the drop word (after tag 03), or -1 if this enemy has no drop entry.</summary>
        public int DropOffset(int i)
        {
            int o = offsets[i] + OFS_TAGS, end = offsets[i + 1];
            while (o < end && data[o] != 0)
            {
                if (o + 2 >= end) return -1;
                if (data[o] == 0x03) return o + 1;
                o += 3;
            }
            return -1;
        }

        public bool HasDrop(int i) { return DropOffset(i) >= 0; }
        public int GetDropItem(int i) { int o = DropOffset(i); return o < 0 ? 0 : (data[o] | (data[o + 1] << 8)) & 0x1FF; }
        public int GetDropChance(int i) { int o = DropOffset(i); return o < 0 ? 0 : data[o + 1] >> 1; }

        public void SetDrop(int i, int item, int chance)
        {
            int o = DropOffset(i);
            if (o < 0) return;
            item = Math.Max(0, Math.Min(0x1FF, item));
            chance = Math.Max(0, Math.Min(127, chance));
            int w = item | (chance << 9);
            data[o] = (byte)w; data[o + 1] = (byte)(w >> 8);
        }

        /// <summary>The real drop rate in %: the 0-99 roll, then 1 in 4.</summary>
        public static double RealDropPercent(int chance) { return Math.Min(chance, 100) / 4.0; }

        /// <summary>Start / end of the scripts (the tagged part and both scripts), for element lookups.</summary>
        public int ScriptStart(int i) { return offsets[i] + OFS_TAGS; }

        /// <summary>Elements the enemy's normal attacks carry (1F before 28 in its AI).</summary>
        public List<int> AttackElements(int i)
        {
            List<int> list = new List<int>();
            List<int> ins = ScriptTools.Walk(data, ScriptStart(i), RecordEnd(i));
            if (ins == null) return list;
            for (int k = 0; k < ins.Count; k++)
            {
                int p = ins[k];
                if (data[p] != 0x1F) continue;
                int v = (data[p + 1] << 8) | data[p + 2];
                if (!list.Contains(v)) list.Add(v);
            }
            return list;
        }
    }
}
