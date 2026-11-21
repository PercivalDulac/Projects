using System;
using System.Collections.Generic;

namespace com.lufiaIIEditor.RomInfo
{
    /// <summary>
    /// Shop inventories (item shops, magic shops, casino prize counters).
    ///
    ///   Table $97:EE9F (file 0xBF09F headered): 256 two-byte pointers, relative to the table
    ///     start, indexed by shop ID (read at $82:E729). 0 = no shop with that ID.
    ///   Record: [type byte] [flags16] then one list per set flag bit, in bit order:
    ///     bit 0  list built at run time (no data in the ROM)
    ///     bit 1  casino coins    - 16-bit item IDs, 0-terminated (sold for gold; the
    ///                             shop's item list is then the prizes, cost = price x 200 coins)
    ///     bit 2  items           - 16-bit item IDs, 0-terminated
    ///     bit 3  weapons         - 16-bit item IDs, 0-terminated
    ///     bit 4  armor           - 16-bit item IDs, 0-terminated
    ///     bit 5  spells          - 1-byte spell IDs, FF-terminated
    ///     bit 7  extra menu entry (no data)
    ///   Prices come from the item / spell records, not from the shop.
    ///
    ///   The records sit back to back from table + 0x200 up to the next table (file 0xBF83B),
    ///   with no free space in the original game. Shops are kept as lists in memory and the
    ///   whole block is rewritten on save, so one shop can grow when another shrinks.
    /// </summary>
    class ShopData
    {
        public const int TableSize = 256;
        public const int ListCount = 6;   // bits 0..5
        public static readonly int[] ListBits = { 1, 2, 3, 4, 5 };
        public static readonly string[] ListNames = { "", "Coins", "Items", "Weapons", "Armor", "Spells" };

        /// <summary>
        /// Where each shop is (by shop ID), matched by comparing the inventories with town shop
        /// guides. The game itself doesn't name shops: the town's map script just opens a shop ID.
        /// "Unused" shops are never opened by the game (checked in play), so their space is free to reuse.
        /// </summary>
        public static readonly string[] Locations = {
            "",             "Elcid",          "Elcid",          "Sundletan",      // 00-03
            "Sundletan",    "Sundletan tavern",       "",               "",               // 04-07
            "Alunze",       "Tanbel",         "Tanbel",         "Tanbel",         // 08-0B
            "Clamento",     "Clamento",       "Clamento jewelry shop",    "Parcelyte",      // 0C-0F
            "Parcelyte",    "",               "Merix",          "Merix",          // 10-13
            "Bound Kingdom","Bound Kingdom",  "Aleyn",          "Aleyn",          // 14-17
            "Aleyn tavern", "Gruberik",       "Unused tavern",       "Narcyssus",      // 18-1B
            "Narcyssus",    "Karloon",        "Karloon",        "Treadool",       // 1C-1F
            "Treadool",     "Unused tavern",       "Forfeit",        "Forfeit",        // 20-23
            "Forfeit",      "Forfeit pawn shop", "Forfeit tavern", "Unused tavern",       // 24-27
            "Forfeit casino", "Auralio",      "Dankirk",        "Dankirk",        // 28-2B
            "Dankirk",      "Ferim",          "Ferim",          "Treble",         // 2C-2F
            "Treble",       "Portravia",      "Portravia",      "Eserikto",       // 30-33
            "Eserikto",     "Barnan",         "Barnan",         "Durale",         // 34-37
            "Durale",       "Unused tavern",       "Chaed",          "Chaed",          // 38-3B
            "Preamarl",     "Narvick",        "Alunze casino"                          // 3C-3E
        };

        public static string LocationOf(int id)
        {
            return (id >= 0 && id < Locations.Length && Locations[id].Length > 0) ? Locations[id] : "Unknown";
        }

        private const int TableHeadered = 0xBF09F;
        private const int BlockEndHeadered = 0xBF83B;
        private const int CodeCheckHeadered = 0x200 + 0x02 * 0x8000 + (0xE729 - 0x8000);

        public class Shop
        {
            public int Id;
            public int Type;
            public int Flags;
            public List<int>[] Lists = new List<int>[ListCount];   // null when the flag is off

            public bool Has(int bit) { return (Flags & (1 << bit)) != 0 && bit < ListCount && Lists[bit] != null; }

            public int Size
            {
                get
                {
                    int n = 3;
                    for (int b = 1; b < ListCount; b++)
                        if (Lists[b] != null) n += b == 5 ? Lists[b].Count + 1 : (Lists[b].Count + 1) * 2;
                    return n;
                }
            }

            public Shop Clone()
            {
                Shop s = new Shop();
                s.Id = Id; s.Type = Type; s.Flags = Flags;
                for (int b = 0; b < ListCount; b++) if (Lists[b] != null) s.Lists[b] = new List<int>(Lists[b]);
                return s;
            }
        }

        private readonly byte[] data;
        private readonly int table;
        private readonly int blockEnd;
        private readonly int codeCheck;
        private List<Shop> shops = new List<Shop>();

        public ShopData(byte[] romData, bool hasHeader)
        {
            data = romData;
            int adjust = hasHeader ? 0 : -0x200;
            table = TableHeadered + adjust;
            blockEnd = BlockEndHeadered + adjust;
            codeCheck = CodeCheckHeadered + adjust;
        }

        public List<Shop> Shops { get { return shops; } }
        public int Capacity { get { return blockEnd - (table + TableSize * 2); } }
        public int TableOffset { get { return table; } }

        public int Used
        {
            get { int n = 0; foreach (Shop s in shops) n += s.Size; return n; }
        }

        public int Free { get { return Capacity - Used; } }

        public Shop Find(int id)
        {
            foreach (Shop s in shops) if (s.Id == id) return s;
            return null;
        }

        private int Word(int o) { return data[o] | (data[o + 1] << 8); }

        /// <summary>Reads every shop. Returns null on success, otherwise why shop editing can't be used.</summary>
        public string Load()
        {
            shops.Clear();
            if (data.Length < blockEnd) return "ROM is too small.";
            // The game's lookup: LDX #$EE9F / STX $08 / LDA #$97.
            if (data[codeCheck] != 0xA2 || data[codeCheck + 1] != 0x9F || data[codeCheck + 2] != 0xEE ||
                data[codeCheck + 5] != 0xA9 || data[codeCheck + 6] != 0x97)
                return "The shop table has been moved by another tool (the game no longer reads it from $97:EE9F).";

            int expected = TableSize * 2;
            for (int id = 0; id < TableSize; id++)
            {
                int p = Word(table + id * 2);
                if (p == 0) continue;
                if (p != expected) return "Shop " + id.ToString("X2") + " isn't where expected (records out of order).";
                int o = table + p;
                Shop s = new Shop();
                s.Id = id;
                s.Type = data[o];
                s.Flags = Word(o + 1);
                o += 3;
                if ((s.Flags & 0x40) != 0 || (s.Flags & 0xFF00) != 0)
                    return "Shop " + id.ToString("X2") + " uses list types this editor doesn't know.";
                for (int b = 1; b < ListCount; b++)
                {
                    if ((s.Flags & (1 << b)) == 0) continue;
                    List<int> list = new List<int>();
                    if (b == 5)
                    {
                        while (o < blockEnd && data[o] != 0xFF) { list.Add(data[o]); o++; }
                        o++;
                    }
                    else
                    {
                        while (o + 1 < blockEnd && Word(o) != 0)
                        {
                            int v = Word(o);
                            if (v > 0x1FF) return "Shop " + id.ToString("X2") + " has an entry this editor doesn't understand.";
                            list.Add(v); o += 2;
                        }
                        o += 2;
                    }
                    if (o > blockEnd) return "Shop " + id.ToString("X2") + " runs past the end of the shop data.";
                    s.Lists[b] = list;
                }
                expected = o - table;
                shops.Add(s);
            }
            if (shops.Count == 0) return "No shops found.";
            return null;
        }

        /// <summary>Writes the table and every record back, packed from the start of the block.</summary>
        public bool Write()
        {
            if (Used > Capacity) return false;
            int o = table + TableSize * 2;
            for (int id = 0; id < TableSize; id++) { data[table + id * 2] = 0; data[table + id * 2 + 1] = 0; }
            foreach (Shop s in shops)
            {
                int p = o - table;
                data[table + s.Id * 2] = (byte)p;
                data[table + s.Id * 2 + 1] = (byte)(p >> 8);
                data[o++] = (byte)s.Type;
                data[o++] = (byte)s.Flags;
                data[o++] = (byte)(s.Flags >> 8);
                for (int b = 1; b < ListCount; b++)
                {
                    if (s.Lists[b] == null) continue;
                    if (b == 5)
                    {
                        foreach (int v in s.Lists[b]) data[o++] = (byte)v;
                        data[o++] = 0xFF;
                    }
                    else
                    {
                        foreach (int v in s.Lists[b]) { data[o++] = (byte)v; data[o++] = (byte)(v >> 8); }
                        data[o++] = 0; data[o++] = 0;
                    }
                }
            }
            while (o < blockEnd) data[o++] = 0;   // space freed by shorter lists
            return true;
        }

        /// <summary>Byte cost of one entry in a list.</summary>
        public static int EntrySize(int bit) { return bit == 5 ? 1 : 2; }
    }
}
