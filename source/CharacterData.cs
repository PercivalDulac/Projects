using System;

namespace com.lufiaIIEditor.RomInfo
{
    /// <summary>
    /// Party character growth (routine $81:F87F, used on level-up by $85:CBDC).
    ///
    ///   Growth table $97:B62C (file 0xBB82C headered): 0x70 bytes per character
    ///     (Maxim, Selan, Guy, Artea, Tia, Dekar, Lexis) = 14 rows of 8 bytes, one row per
    ///     8 levels. Bytes 0-6 = gain for HP, MP, STR, AGL, INT, GUT, MGR; byte 7 is other data
    ///     (left untouched).
    ///   Base table $97:B93C (file 0xBBB3C): 7 two-byte slots per character, same stat order;
    ///     the game reads only the low byte (8-bit add at $81:F95E), so bases are 0-255.
    ///
    ///   Stat at level L = base + (sum over levels l = 1 .. L-1 of row[l / 8]) / 16.
    /// </summary>
    class CharacterData
    {
        public const int Count = 7;
        public const int Stats = 7;
        public const int Rows = 14;
        public const int RowSize = 8;
        public const int MaxLevel = 99;
        public static readonly string[] StatNames = { "HP", "MP", "STR", "AGL", "INT", "GUT", "MGR" };

        private const int GrowthHeadered = 0xBB82C;
        private const int BaseHeadered = 0xBBB3C;

        // Ancient Cave party setup ($81:ED35 reads 7 records from $85:B395, file 0x2B595 headered):
        //   +0 level (word), +2 word, spell list ending in FF (empty in the original, so at +4),
        //   +5 EXP (3 bytes), +8 nine words copied to the character's permanent bonuses
        //   (+74 HP, +76 MP, +78 ATP override, +7A DFP override, +7C STR, +7E AGL, +80 INT,
        //   +82 GUT, +84 MGR), +26 one byte. The bonuses are added on top of base + growth
        //   ($81:F4ED), so they last the whole cave run.
        private const int CaveHeadered = 0x2B595;
        private const int CaveRecord = 27;
        private const int CaveCodeHeadered = 0x200 + 0x01 * 0x8000 + (0xED4C - 0x8000);
        /// <summary>Which bonus word each stat (HP MP STR AGL INT GUT MGR) uses.</summary>
        private static readonly int[] CaveWord = { 0, 1, 4, 5, 6, 7, 8 };
        public const int CaveBonusMax = 999;

        private readonly byte[] data;
        private readonly int growth;
        private readonly int bases;
        private readonly int cave;
        private readonly int caveCode;

        public CharacterData(byte[] romData, bool hasHeader)
        {
            data = romData;
            int adjust = hasHeader ? 0 : -0x200;
            growth = GrowthHeadered + adjust;
            bases = BaseHeadered + adjust;
            cave = CaveHeadered + adjust;
            caveCode = CaveCodeHeadered + adjust;
        }

        /// <summary>True when the Ancient Cave setup records are where this editor expects them.</summary>
        public bool HasCave
        {
            get
            {
                if (data.Length < cave + Count * CaveRecord) return false;
                // LDX #$B395 in the cave setup routine.
                if (data[caveCode] != 0xA2 || data[caveCode + 1] != 0x95 || data[caveCode + 2] != 0xB3) return false;
                for (int c = 0; c < Count; c++)
                    if (data[cave + c * CaveRecord + 4] != 0xFF) return false;   // spell lists must be empty
                return true;
            }
        }

        public int CaveOffset(int c) { return cave + c * CaveRecord; }

        private int CaveBonusOffset(int c, int stat) { return cave + c * CaveRecord + 8 + CaveWord[stat] * 2; }

        public int GetCaveBonus(int c, int stat)
        {
            int o = CaveBonusOffset(c, stat);
            return data[o] | (data[o + 1] << 8);
        }

        public void SetCaveBonus(int c, int stat, int v)
        {
            if (v < 0) v = 0; if (v > CaveBonusMax) v = CaveBonusMax;
            int o = CaveBonusOffset(c, stat);
            data[o] = (byte)v; data[o + 1] = (byte)(v >> 8);
        }

        public int GetCaveLevel(int c) { int o = cave + c * CaveRecord; return data[o] | (data[o + 1] << 8); }

        public byte[] RawData { get { return data; } }

        public string Validate()
        {
            if (data.Length < bases + Count * Stats * 2) return "ROM is too small.";
            for (int c = 0; c < Count; c++)
            {
                int hp = GetBase(c, 0);
                if (hp < 1 || hp > 999) return "Character base stats not found where expected.";
            }
            // The code that reads the tables: LDA #$B62C (growth) at $81:F8B2, LDA $B93C,Y (bases) at
            // $81:F95E. (Byte 7 of each growth row is the EXP rate, which the Party & EXP tab can
            // now make different per character, so it isn't used as a check any more.)
            int adj = growth - GrowthHeadered;
            int g = 0x200 + 0x01 * 0x8000 + (0xF8B2 - 0x8000) + adj, b = 0x200 + 0x01 * 0x8000 + (0xF95E - 0x8000) + adj;
            if (data[g] != 0xA9 || data[g + 1] != 0x2C || data[g + 2] != 0xB6 || data[b] != 0xB9 || data[b + 1] != 0x3C || data[b + 2] != 0xB9)
                return "Character growth table not found where expected.";
            return null;
        }

        // ---- experience ----
        //
        // There's no EXP table: $81:F9E9 computes it. v starts at the character's base ($97:B99E,
        // word, read byte-swapped: 20 -> 20.0 in 8.8 fixed point). For each level, the EXP
        // needed grows by (rate + 1) / 256, where rate is byte 7 of the growth row for that
        // 8-level band ($97:B633 = row + 7). Total to reach level L+1 = sum / 256 - 10.
        // Level 98 and up: 9,999,999.

        private const int ExpBaseHeadered = 0xBBB9E;

        private int ExpBaseOffset(int c) { return ExpBaseHeadered - GrowthHeadered + growth + c * 2; }
        public int GetExpBase(int c) { return data[ExpBaseOffset(c)]; }
        public void SetExpBase(int c, int v) { data[ExpBaseOffset(c)] = (byte)Math.Max(1, Math.Min(255, v)); }
        public int ExpBaseFileOffset(int c) { return ExpBaseOffset(c); }

        public int GetExpRate(int c, int row) { return data[RowOffset(c, row) + 7]; }
        public void SetExpRate(int c, int row, int v) { data[RowOffset(c, row) + 7] = (byte)Math.Max(0, Math.Min(255, v)); }
        public int ExpRateOffset(int c, int row) { return RowOffset(c, row) + 7; }

        public const int ExpCap = 9999999;

        /// <summary>
        /// Total EXP needed to reach each level: result[L] = EXP to go from level L to L+1
        /// (index 1..98). Mirror of $81:F9E9.
        /// </summary>
        public long[] ExpTable(int c)
        {
            long[] t = new long[MaxLevel + 1];
            int lo = data[ExpBaseOffset(c)], hi = data[ExpBaseOffset(c) + 1];
            for (int level = 1; level < MaxLevel; level++)
            {
                if (level >= 98) { t[level] = ExpCap; continue; }
                long v = (lo << 8) | hi;          // XBA: low byte becomes the whole part
                long sum = v;
                int row = 0;
                long m = GetExpRate(c, 0) + 1;
                for (int x = 1; x != level; x++)
                {
                    if (((x + 1) & 7) == 0) { row = Math.Min((x + 1) >> 3, Rows - 1); m = GetExpRate(c, row) + 1; }
                    v = (v + ((v * m) >> 8)) & 0xFFFFFF;
                    sum = (sum + v) & 0xFFFFFFFF;
                }
                t[level] = (((sum >> 8) - 10) & 0xFFFFFF);
            }
            return t;
        }

        /// <summary>The least EXP a character at this level has (what a new game gives them).</summary>
        public long MinExpForLevel(int c, int level)
        {
            if (level <= 1) return 0;
            return ExpTable(c)[level - 1];
        }

        // ---- Ancient Cave start level ----
        public void SetCaveLevel(int c, int level)
        {
            int o = cave + c * CaveRecord;
            data[o] = (byte)level; data[o + 1] = 0;
            long exp = MinExpForLevel(c, level);
            data[o + 5] = (byte)exp; data[o + 6] = (byte)(exp >> 8); data[o + 7] = (byte)(exp >> 16);
        }

        public long GetCaveExp(int c) { int o = cave + c * CaveRecord + 5; return data[o] | (data[o + 1] << 8) | (data[o + 2] << 16); }

        private int RowOffset(int c, int row) { return growth + c * Rows * RowSize + row * RowSize; }

        public int GetGain(int c, int row, int stat) { return data[RowOffset(c, row) + stat]; }

        public void SetGain(int c, int row, int stat, int v)
        {
            if (v < 0) v = 0; if (v > 255) v = 255;
            data[RowOffset(c, row) + stat] = (byte)v;
        }

        public int GetBase(int c, int stat)
        {
            return data[bases + (c * Stats + stat) * 2];
        }

        public void SetBase(int c, int stat, int v)
        {
            if (v < 0) v = 0; if (v > 255) v = 255;
            int o = bases + (c * Stats + stat) * 2;
            data[o] = (byte)v;   // only the low byte is used; the second byte is left as it is
        }

        /// <summary>All gains of one stat, one per row.</summary>
        public int[] GetCurve(int c, int stat)
        {
            int[] g = new int[Rows];
            for (int r = 0; r < Rows; r++) g[r] = GetGain(c, r, stat);
            return g;
        }

        public void SetCurve(int c, int stat, int[] g)
        {
            for (int r = 0; r < Rows; r++) SetGain(c, r, stat, g[r]);
        }

        /// <summary>Mirror of $81:F87F for one stat, from a base value and a curve.</summary>
        public static int StatAt(int baseValue, int[] curve, int level)
        {
            int sum = 0;
            for (int l = 1; l < level; l++) sum += curve[Math.Min(l >> 3, Rows - 1)];
            return baseValue + (sum >> 4);
        }
    }
}
