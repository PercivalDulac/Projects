using System;
using System.Collections.Generic;

namespace com.lufiaIIEditor.RomInfo
{
    /// <summary>
    /// Treasure chests on the normal maps (the Ancient Cave's are random: see CaveLootData).
    ///
    /// Table at $91:B8B5 (file 0x8BAB5 headered), uncompressed:
    ///   +000  248 words, one per map ($05AC), relative to the table: that map's list.
    ///         FFFF = no list. Map 0's word points inside the pointer table and is ignored.
    ///   lists 3-byte entries ending in FF:
    ///     b0  bits 0-4 object number on the map, bit 5 unknown (kept), bit 6 item bit 8,
    ///         bit 7 opened flag in the second flag set
    ///     b1  opened-flag number
    ///     b2  item, low 8 bits
    /// Read by $80:E844 (map load: $7FD1A0 = the map's word) and $8E:C05F / $8E:C36D (opening).
    /// Only the item is edited, in place; the list sizes and flags never change.
    /// </summary>
    class ChestData
    {
        public const int Maps = 248;
        private const int TableBank = 0x91, TableAddr = 0xB8B5;

        public class Chest
        {
            public int Map;
            public int Index;      // position in the map's list (0-based)
            public int Offset;     // file offset of the 3-byte entry
            public int Object;     // object number on the map (b0 & 1F)
            public int Flag;       // opened flag: b1, + 256 for the second set
        }

        private readonly byte[] data;
        private readonly int adjust, table;
        private readonly int[] listStart = new int[Maps];   // file offset, or -1 for no list
        public readonly List<Chest> Chests = new List<Chest>();
        private readonly Dictionary<int, List<Chest>> byMap = new Dictionary<int, List<Chest>>();
        private readonly Dictionary<int, List<int>> sharers = new Dictionary<int, List<int>>();  // list start -> maps

        private static int File(int bank, int addr) { return 0x200 + (bank & 0x7F) * 0x8000 + (addr - 0x8000); }

        public ChestData(byte[] romData, bool hasHeader)
        {
            data = romData;
            adjust = hasHeader ? 0 : -0x200;
            table = File(TableBank, TableAddr) + adjust;
        }

        public byte[] RawData { get { return data; } }

        private bool LongRead(int bank, int addr, int target)
        {
            int o = File(bank, addr) + adjust;
            if (o < 0 || o + 4 > data.Length) return false;
            return data[o] == 0xBF && data[o + 1] == (target & 0xFF) && data[o + 2] == ((target >> 8) & 0xFF) && data[o + 3] == (target >> 16);
        }

        private int Word(int o) { return data[o] | (data[o + 1] << 8); }

        public string Load()
        {
            Chests.Clear(); byMap.Clear(); sharers.Clear();
            int bankEnd = table - (TableAddr - 0x8000) + 0x8000;   // end of bank $91 in the file
            if (data.Length < bankEnd) return "ROM is too small.";
            // the map loader and the chest code both read LDA $91B8B5,X
            if (!LongRead(0x80, 0xE85D, 0x91B8B5) || !LongRead(0x8E, 0xC095, 0x91B8B5) ||
                !LongRead(0x8E, 0xC36D, 0x91B8B5) || !LongRead(0x8E, 0xC378, 0x91B8B7))
                return "The treasure chest code isn't where expected.";
            for (int m = 0; m < Maps; m++)
            {
                int p = Word(table + 2 * m);
                listStart[m] = -1;
                if (p == 0xFFFF || p < Maps * 2) continue;     // no list (map 0's word points into the table)
                int o = table + p;
                if (o >= bankEnd) return "Map " + m.ToString("X2") + "'s chest list points outside its bank.";
                listStart[m] = o;
                List<int> who;
                if (!sharers.TryGetValue(o, out who)) { who = new List<int>(); sharers[o] = who; }
                who.Add(m);
                List<Chest> list = new List<Chest>();
                int k = 0;
                while (data[o] != 0xFF)
                {
                    if (o + 3 >= bankEnd || k >= 64) return "Map " + m.ToString("X2") + "'s chest list doesn't end.";
                    Chest c = new Chest();
                    c.Map = m; c.Index = k; c.Offset = o;
                    c.Object = data[o] & 0x1F;
                    c.Flag = data[o + 1] + ((data[o] & 0x80) != 0 ? 256 : 0);
                    list.Add(c);
                    if (who.Count == 1) Chests.Add(c);     // a shared list is listed once, under its first map
                    o += 3; k++;
                }
                byMap[m] = list;
            }
            return null;
        }

        public List<Chest> ForMap(int m)
        {
            List<Chest> l;
            return byMap.TryGetValue(m, out l) ? l : new List<Chest>();
        }

        /// <summary>Maps that have at least one chest, in map order.</summary>
        public List<int> MapsWithChests()
        {
            List<int> r = new List<int>();
            for (int m = 0; m < Maps; m++) if (ForMap(m).Count > 0) r.Add(m);
            return r;
        }

        /// <summary>Other maps that use the same list as this one (edits change them all).</summary>
        public List<int> SharedWith(int m)
        {
            List<int> r = new List<int>();
            if (listStart[m] < 0) return r;
            foreach (int x in sharers[listStart[m]]) if (x != m) r.Add(x);
            return r;
        }

        public int GetItem(Chest c) { return data[c.Offset + 2] | ((data[c.Offset] & 0x40) != 0 ? 0x100 : 0); }

        public void SetItem(Chest c, int item)
        {
            item = Math.Max(0, Math.Min(0x1FF, item));
            data[c.Offset + 2] = (byte)item;
            data[c.Offset] = (byte)((data[c.Offset] & ~0x40) | ((item & 0x100) != 0 ? 0x40 : 0));
        }

        /// <summary>Item in the same chest in another ROM's data (same layout), or -1.</summary>
        public static int ItemIn(byte[] d, int offset)
        {
            if (offset < 0 || offset + 3 > d.Length) return -1;
            return d[offset + 2] | ((d[offset] & 0x40) != 0 ? 0x100 : 0);
        }

        public int TableOffset { get { return table; } }
    }
}
