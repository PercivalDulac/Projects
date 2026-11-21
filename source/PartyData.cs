using System;
using System.Collections.Generic;
using System.Text;

namespace com.lufiaIIEditor.RomInfo
{
    /// <summary>
    /// New-game setup ($81:EC80 reads it from $85:B2A1, file 0x2B4A1 headered; it ends where the
    /// Ancient Cave records start, 0x2B595):
    ///   5 bytes   party slots ($0A7A)            - kept as they are
    ///   2 words   ($0A8A, $0B55)                  - kept as they are
    ///   words     starting inventory, item | quantity &lt;&lt; 9, ending in 0000
    ///   7 records name (6), level (word), word, spells known (bytes, FF-terminated), EXP (3 bytes),
    ///             equipment (7 words: weapon, armor, shield, helmet, ring, jewel, tool), 1 byte.
    /// Stats aren't stored: the game computes them from the level ($81:F87F).
    /// The whole block is rewritten on save, so spells and inventory can trade space.
    /// </summary>
    class PartyData
    {
        public const int Characters = 7;
        public static readonly string[] SlotNames = { "Weapon", "Armor", "Shield", "Helmet", "Ring", "Jewel", "Tool" };

        public class Member
        {
            public byte[] Name = new byte[6];
            public int Level;
            public int Word94;
            public List<int> Spells = new List<int>();
            public int Exp;
            public int[] Equip = new int[7];
            public int Last;
        }

        private readonly byte[] data;
        private readonly int start, end, codeCheck;
        private byte[] header = new byte[9];
        public readonly List<int> Inventory = new List<int>();   // raw words (item | qty << 9)
        public readonly Member[] Members = new Member[Characters];
        /// <summary>Where each character's 6-byte name was found by Load (it moves when spells or inventory change).</summary>
        public readonly int[] NameOffsets = new int[Characters];

        public PartyData(byte[] romData, bool hasHeader)
        {
            data = romData;
            int adjust = hasHeader ? 0 : -0x200;
            start = 0x2B4A1 + adjust;
            end = 0x2B595 + adjust;
            codeCheck = 0x200 + 0x01 * 0x8000 + (0xEC8A - 0x8000) + adjust;
        }

        public int Capacity { get { return end - start; } }

        private int Word(int o) { return data[o] | (data[o + 1] << 8); }

        public string Load()
        {
            if (data.Length < end) return "ROM is too small.";
            if (data[codeCheck] != 0xA2 || data[codeCheck + 1] != 0xA1 || data[codeCheck + 2] != 0xB2)
                return "The new-game setup data isn't where expected.";
            int o = start;
            Array.Copy(data, o, header, 0, 9); o += 9;
            Inventory.Clear();
            while (o + 1 < end && Word(o) != 0) { Inventory.Add(Word(o)); o += 2; }
            o += 2;
            for (int c = 0; c < Characters; c++)
            {
                Member m = new Member();
                if (o + 30 > end) return "The new-game setup data looks unusual.";
                NameOffsets[c] = o;
                Array.Copy(data, o, m.Name, 0, 6); o += 6;
                m.Level = Word(o); o += 2;
                m.Word94 = Word(o); o += 2;
                while (o < end && data[o] != 0xFF) { m.Spells.Add(data[o]); o++; }
                o++;
                m.Exp = data[o] | (data[o + 1] << 8) | (data[o + 2] << 16); o += 3;
                for (int k = 0; k < 7; k++) { m.Equip[k] = Word(o); o += 2; }
                m.Last = data[o]; o++;
                if (o > end || m.Level < 1 || m.Level > 99) return "The new-game setup data looks unusual.";
                Members[c] = m;
            }
            return null;
        }

        public string NameOf(int c) { return Encoding.ASCII.GetString(Members[c].Name).TrimEnd('\0', ' '); }

        public int Used
        {
            get
            {
                int n = 9 + Inventory.Count * 2 + 2;
                foreach (Member m in Members) n += 6 + 2 + 2 + m.Spells.Count + 1 + 3 + 14 + 1;
                return n;
            }
        }

        public int Free { get { return Capacity - Used; } }

        public bool Write()
        {
            if (Used > Capacity) return false;
            int o = start;
            Array.Copy(header, 0, data, o, 9); o += 9;
            foreach (int w in Inventory) { data[o++] = (byte)w; data[o++] = (byte)(w >> 8); }
            data[o++] = 0; data[o++] = 0;
            foreach (Member m in Members)
            {
                Array.Copy(m.Name, 0, data, o, 6); o += 6;
                data[o++] = (byte)m.Level; data[o++] = (byte)(m.Level >> 8);
                data[o++] = (byte)m.Word94; data[o++] = (byte)(m.Word94 >> 8);
                foreach (int s in m.Spells) data[o++] = (byte)s;
                data[o++] = 0xFF;
                data[o++] = (byte)m.Exp; data[o++] = (byte)(m.Exp >> 8); data[o++] = (byte)(m.Exp >> 16);
                foreach (int e in m.Equip) { data[o++] = (byte)e; data[o++] = (byte)(e >> 8); }
                data[o++] = (byte)m.Last;
            }
            while (o < end) data[o++] = 0;   // freed space
            return true;
        }

        public int StartOffset { get { return start; } }
        public int EndOffset { get { return end; } }
    }
}
