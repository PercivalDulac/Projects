using System;
using System.Text;

namespace com.lufiaIIEditor.RomInfo
{
    /// <summary>
    /// Reads and writes the capsule monster table (7 monsters x 5 forms).
    ///
    /// Table: pointer list at $97:DCB8 (file offset 0xBDEB8 with header). Each 16-bit
    /// pointer is relative to the start of the table. Index = monster * 5 + (form - 1).
    /// Loaded by the game at $82:C38B, stats applied at $82:D283.
    ///
    /// Record layout (offsets from record start):
    ///   00-0B  name (12 ASCII chars, space padded)
    ///   0C     00 - end of the name (the capsule menu crashes without it)
    ///   0D     form (1-5)
    ///   0E     element bit (00 Neutral, 01 Light, 02 Wind, 04 Water, 08 Dark, 10 Soil, 20 Fire)
    ///   0F-14  skill IDs (1-based, 0 = empty slot)
    ///   15     HP base
    ///   16     ATP bonus (added to STR, like a weapon)
    ///   17     DFP bonus (added to (STR+AGL)/2, like armor)
    ///   18-1C  STR, AGL, INT, GUT, MGR base
    ///   1D-22  growth curve rows for HP, STR, AGL, INT, GUT, MGR (0-30)
    ///   23-    AI script (not edited here)
    ///
    /// Growth (routine $82:D31C): for each level L from 1 to (level-1), take
    /// curve[row][L/16] from the table at $A6:F420; halve it when L/16 equals the form
    /// number, quarter it when L/16 is greater; sum, divide by 16, add to the base.
    /// </summary>
    class CapsuleMonsterData
    {
        public const int Count = 35;
        public const int FormsPerMonster = 5;
        public const int MaxCurveRow = 30;
        public const int NameLength = 12;   // +0C is a 00 end marker the menu relies on
        public const int OFS_NAME_END = 0x0C;

        // File offsets for a headered ROM (same convention as the rest of the editor).
        private const int TableOffsetHeadered = 0xBDEB8;
        private const int CurveOffsetHeadered = 0x137620;

        public const int OFS_FORM = 0x0D;
        public const int OFS_ELEMENT = 0x0E;
        public const int OFS_SKILLS = 0x0F;
        public const int OFS_HP = 0x15;
        public const int OFS_ATP_BONUS = 0x16;
        public const int OFS_DFP_BONUS = 0x17;
        public const int OFS_STR = 0x18;   // STR, AGL, INT, GUT, MGR follow in order
        public const int OFS_GROWTH = 0x1D; // HP, STR, AGL, INT, GUT, MGR

        public static readonly string[] FamilyNames = { "Jelze", "Flash", "Gusto", "Zeppy", "Darbi", "Sully", "Blaze" };

        private readonly byte[] data;
        private readonly int tableOffset;
        private readonly int curveOffset;

        public CapsuleMonsterData(byte[] romData, bool hasHeader)
        {
            data = romData;
            int adjust = hasHeader ? 0 : -0x200;
            tableOffset = TableOffsetHeadered + adjust;
            curveOffset = CurveOffsetHeadered + adjust;
        }

        /// <summary>Returns null if the table looks right, otherwise a description of the problem.</summary>
        public string Validate()
        {
            if (data.Length < curveOffset + (MaxCurveRow + 1) * 8)
                return "ROM is too small.";
            for (int i = 0; i < Count; i++)
            {
                int o = RecordOffset(i);
                if (o < 0 || o + 0x23 > data.Length)
                    return "Capsule monster pointer " + i + " is out of range.";
                if (data[o + OFS_FORM] != (i % FormsPerMonster) + 1)
                    return "Capsule monster record " + i + " has an unexpected form number.";
            }
            return null;
        }

        public int RecordOffset(int index)
        {
            int p = data[tableOffset + index * 2] | (data[tableOffset + index * 2 + 1] << 8);
            return tableOffset + p;
        }

        public static string FamilyOf(int index) { return FamilyNames[index / FormsPerMonster]; }
        public static int FormOf(int index) { return (index % FormsPerMonster) + 1; }

        public string GetName(int index)
        {
            int o = RecordOffset(index);
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < NameLength; i++) sb.Append((char)data[o + i]);
            return sb.ToString().TrimEnd(' ');
        }

        public void SetName(int index, string name)
        {
            int o = RecordOffset(index);
            if (name == null) name = "";
            for (int i = 0; i < NameLength; i++)
            {
                char c = i < name.Length ? name[i] : ' ';
                if (c < 0x20 || c > 0x7E) c = ' ';
                data[o + i] = (byte)c;
            }
        }

        /// <summary>
        /// Puts back the 00 that ends each capsule name. An earlier version of this editor
        /// saved names as 13 characters and overwrote it with a space, which makes the game
        /// reset when the capsule menu opens. Returns how many records were fixed.
        /// </summary>
        public int RepairNameEnds()
        {
            int fixedCount = 0;
            for (int i = 0; i < Count; i++)
            {
                int o = RecordOffset(i) + OFS_NAME_END;
                if (data[o] != 0) { data[o] = 0; fixedCount++; }
            }
            return fixedCount;
        }

        public byte GetByte(int index, int fieldOffset) { return data[RecordOffset(index) + fieldOffset]; }

        public void SetByte(int index, int fieldOffset, int value)
        {
            if (value < 0) value = 0;
            if (value > 255) value = 255;
            data[RecordOffset(index) + fieldOffset] = (byte)value;
        }

        public string ElementName(int index)
        {
            switch (GetByte(index, OFS_ELEMENT))
            {
                case 0x00: return "Neutral";
                case 0x01: return "Light";
                case 0x02: return "Wind";
                case 0x04: return "Water";
                case 0x08: return "Dark";
                case 0x10: return "Soil";
                case 0x20: return "Fire";
                default: return "Unknown (" + GetByte(index, OFS_ELEMENT).ToString("X2") + ")";
            }
        }

        public int CurveValue(int row, int bracket)
        {
            if (row < 0) row = 0;
            if (row > MaxCurveRow) row = MaxCurveRow;
            if (bracket < 0) bracket = 0;
            if (bracket > 7) bracket = 7;
            return data[curveOffset + row * 8 + bracket];
        }

        /// <summary>Mirror of the game's growth routine at $82:D31C.</summary>
        public int GrownStat(int baseValue, int curveRow, int level, int form)
        {
            int sum = 0;
            for (int lv = 1; lv < level; lv++)
            {
                int bracket = lv >> 4;
                int gain = CurveValue(curveRow, bracket);
                if (bracket == form) gain >>= 1;
                else if (bracket > form) gain >>= 2;
                sum += gain;
            }
            return baseValue + (sum >> 4);
        }
    }
}
