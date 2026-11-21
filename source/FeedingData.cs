using System;
using System.Collections.Generic;

namespace com.lufiaIIEditor.RomInfo
{
    /// <summary>
    /// Capsule monster feeding (menu code $82:C683-$82:CC3B).
    ///
    ///   Price brackets  $8E:E466 (file 0x76666): 16 words. An item belongs to the first bracket
    ///                   whose limit is >= its price ($82:C6E0). Items under 200G take a
    ///                   separate path (they lower the appetite level by one).
    ///   Appetite level  per capsule ($11A6+c): the bracket of the best food eaten. Better food
    ///                   raises it; cheaper food lowers it by one.
    ///   Wanted items    $95:FF16 (file 0xB0116): 16 headers [offset, count] (offsets from the
    ///                   table start), then the lists, back to back up to +0xD8. The capsule asks
    ///                   for a random item from the list of its appetite level ($82:C554). A
    ///                   list with 0 items borrows the next list's first item.
    ///   Growth gained   $8E:E48A (file 0x7668A): 4 forms x 4 bracket groups (brackets 1-4,
    ///                   5-8, 9-12, 13-16); x3 when the item is the wanted one ($82:CC16).
    ///                   The feeding bar is full at 32.
    ///   Fruits          $8E:E4A8 (file 0x766A8): each family's own fruit (7 words);
    ///                   Magic fruit = the word at $82:C64A.
    /// </summary>
    class FeedingData
    {
        public const int Brackets = 16;
        public const int ListArea = 0xD8;    // header (0x40) + lists end here
        public const int HeaderSize = Brackets * 4;
        public const int Capacity = (ListArea - HeaderSize) / 2;   // entries
        public const int Forms = 4, Groups = 4;
        public static readonly string[] Families = { "Jelze", "Flash", "Gusto", "Zeppy", "Darbi", "Sully", "Blaze" };

        private readonly byte[] data;
        private readonly int lists, prices, gains, fruits, magicFruit;
        private readonly int adjust;
        public readonly List<int>[] Wanted = new List<int>[Brackets];

        private static int File(int bank, int addr) { return 0x200 + (bank & 0x7F) * 0x8000 + (addr - 0x8000); }

        public FeedingData(byte[] romData, bool hasHeader)
        {
            data = romData;
            adjust = hasHeader ? 0 : -0x200;
            lists = File(0x95, 0xFF16) + adjust;
            prices = File(0x8E, 0xE466) + adjust;
            gains = File(0x8E, 0xE48A) + adjust;
            fruits = File(0x8E, 0xE4A8) + adjust;
            magicFruit = File(0x82, 0xC64A) + adjust;
        }

        public byte[] RawData { get { return data; } }
        private int Word(int o) { return data[o] | (data[o + 1] << 8); }
        private void SetWord(int o, int v) { data[o] = (byte)v; data[o + 1] = (byte)(v >> 8); }

        private bool Code(int bank, int addr, params byte[] bytes)
        {
            int o = File(bank, addr) + adjust;
            if (o < 0 || o + bytes.Length > data.Length) return false;
            for (int i = 0; i < bytes.Length; i++) if (data[o + i] != bytes[i]) return false;
            return true;
        }

        /// <summary>Reads the wanted lists. Returns null if everything is where expected.</summary>
        public string Load()
        {
            if (!Code(0x82, 0xC55A, 0xBF, 0x16, 0xFF, 0x95) || !Code(0x82, 0xC6E0, 0xBF, 0x66, 0xE4, 0x8E) ||
                !Code(0x82, 0xCC0C, 0xBF, 0x8A, 0xE4, 0x8E) || !Code(0x82, 0xC659, 0xBF, 0xA8, 0xE4, 0x8E) ||
                !Code(0x82, 0xC649, 0xA9))
                return "The feeding code isn't where expected (changed by another tool?).";
            int expected = HeaderSize;
            for (int g = 0; g < Brackets; g++)
            {
                int off = Word(lists + g * 4), count = Word(lists + g * 4 + 2);
                if (off != expected || off + count * 2 > ListArea) return "Wanted-item list " + (g + 1) + " isn't where expected.";
                Wanted[g] = new List<int>();
                for (int k = 0; k < count; k++) Wanted[g].Add(Word(lists + off + k * 2) & 0x1FF);
                expected = off + count * 2;
            }
            return null;
        }

        public int Used { get { int n = 0; foreach (List<int> l in Wanted) n += l.Count; return n; } }
        public int Free { get { return Capacity - Used; } }

        /// <summary>Writes the lists back to back (headers rebuilt); leftover space is zeroed.</summary>
        public bool WriteLists()
        {
            if (Used > Capacity) return false;
            int off = HeaderSize;
            for (int g = 0; g < Brackets; g++)
            {
                SetWord(lists + g * 4, off);
                SetWord(lists + g * 4 + 2, Wanted[g].Count);
                foreach (int id in Wanted[g]) { SetWord(lists + off, id); off += 2; }
            }
            while (off < ListArea) { data[lists + off] = 0; off++; }
            return true;
        }

        public int ListsOffset { get { return lists; } }

        // ---- brackets, gains, fruits ----

        public int PriceOffset(int g) { return prices + g * 2; }
        public int GetPriceLimit(int g) { return Word(PriceOffset(g)); }
        public void SetPriceLimit(int g, int v) { SetWord(PriceOffset(g), Math.Max(0, Math.Min(65535, v))); }

        public int GainOffset(int form, int group) { return gains + form * Groups + group; }
        public int GetGain(int form, int group) { return data[GainOffset(form, group)]; }
        public void SetGain(int form, int group, int v) { data[GainOffset(form, group)] = (byte)Math.Max(0, Math.Min(255, v)); }

        public int FruitOffset(int family) { return fruits + family * 2; }
        public int GetFruit(int family) { return Word(FruitOffset(family)) & 0x1FF; }
        public void SetFruit(int family, int id) { SetWord(FruitOffset(family), id & 0x1FF); }

        public int MagicFruitOffset { get { return magicFruit; } }
        public int MagicFruit { get { return Word(magicFruit) & 0x1FF; } set { SetWord(magicFruit, value & 0x1FF); } }

        /// <summary>Price range of a bracket as text, e.g. "500-599G".</summary>
        public string BracketRange(int g)
        {
            int lo = g == 0 ? 200 : GetPriceLimit(g - 1) + 1;
            int hi = GetPriceLimit(g);
            if (g == Brackets - 1 && hi >= 65535) return lo + "G and up";
            return lo + "-" + hi + "G";
        }
    }
}
