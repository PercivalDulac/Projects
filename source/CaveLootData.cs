using System;
using System.Collections.Generic;

namespace com.lufiaIIEditor.RomInfo
{
    /// <summary>
    /// Ancient Cave chest contents, filled per floor by $83:90C5 (up to 8 chests).
    ///
    /// A chest's item word: bits 0-8 item, bit 9 = quantity 1, bit 14 = blue chest
    /// ($8E:B954, $83:9986), bit 15 = spell (learned by everyone, $8E:C0DC).
    ///
    /// Per floor:
    ///   Iris  - rand(256) &lt; IrisChance: one of the 9 items in the Iris table ($91:FFCA),
    ///           red chest, once per run, and never again once taken out of the cave
    ///           (event flag 0xC8 + slot, set on leaving by $84:8959).
    ///   Providence - from floor ProvidenceFloor, rand(60) &lt; floor: the Providence item
    ///           (once per run, red chest).
    /// Every chest then rolls rand(256) against five thresholds (code immediates):
    ///   >= T1  weapon  from the run-time list (price &lt; limit, slot bit 0)
    ///   >= T2  other equipment from the run-time list
    ///   >= T3  spell 0..34 (if someone already knows it: the potion table)
    ///   >= T4  rare table ($94:EEA0, 41 items) in a BLUE chest
    ///   >= T5  potion table ($91:FFDC, 9 items)
    ///   else   common table ($94:F13D, 31 items)
    /// Run-time lists ($83:9074): every item whose record +0 has bit 0x02 but not 0x20,
    /// +1 lacks 0x20, and price &lt; PriceStep x (floor + 1) - no limit from floor NoLimitFloor.
    /// </summary>
    class CaveLootData
    {
        public class Table
        {
            public string Name;
            public string Note;
            public int Offset;
            public int Count;
        }

        public const int Sources = 6;
        public static readonly string[] SourceNames = {
            "Weapon (price-limited list)", "Other equipment (price-limited list)", "Spell",
            "Blue chest", "Potion table", "Common table" };

        private readonly byte[] data;
        private readonly int adjust;
        public readonly List<Table> Tables = new List<Table>();

        // code operand addresses (headered file offsets computed in the constructor)
        private readonly int[] threshold = new int[5];
        private readonly int irisChance, provFloor, provItem, priceLo, priceHi, noLimitFloor;

        private static int File(int bank, int addr) { return 0x200 + (bank & 0x7F) * 0x8000 + (addr - 0x8000); }

        public CaveLootData(byte[] romData, bool hasHeader)
        {
            data = romData;
            adjust = hasHeader ? 0 : -0x200;
            AddTable("Blue chests", "Every blue chest takes one of these at random (equipment you keep when you leave the cave).", File(0x94, 0xEEA0), 41);
            AddTable("Potion table", "Also used when a spell chest rolls a spell the party already knows.", File(0x91, 0xFFDC), 9);
            AddTable("Common table", "", File(0x94, 0xF13D), 31);
            AddTable("Iris treasures", "One may appear per floor (red chest). Whatever is listed here is kept when you leave the cave and never appears again in that save.", File(0x91, 0xFFCA), 9);
            int[] t = { 0x9147, 0x914B, 0x914F, 0x9153, 0x9157 };
            for (int i = 0; i < 5; i++) threshold[i] = File(0x83, t[i]) + adjust;
            irisChance = File(0x83, 0x90D5) + adjust;
            provFloor = File(0x83, 0x9126) + adjust;
            provItem = File(0x83, 0x9135) + adjust;
            priceLo = File(0x83, 0x9059) + adjust;
            priceHi = File(0x83, 0x905E) + adjust;
            noLimitFloor = File(0x83, 0x904D) + adjust;
        }

        private void AddTable(string name, string note, int headered, int count)
        {
            Table t = new Table();
            t.Name = name; t.Note = note; t.Offset = headered + adjust; t.Count = count;
            Tables.Add(t);
        }

        public byte[] RawData { get { return data; } }

        /// <summary>Checks that the code still looks like the original routine.</summary>
        public string Validate()
        {
            if (data.Length < File(0x94, 0xF13D) + adjust + 62) return "ROM is too small.";
            int[] cmpOps = { threshold[0], threshold[1], threshold[2], threshold[3], threshold[4], irisChance, provFloor, noLimitFloor };
            foreach (int o in cmpOps) if (data[o - 1] != 0xC9) return "The Ancient Cave chest code isn't where expected.";
            if (data[provItem - 1] != 0xA2 || data[priceLo - 1] != 0xA9 || data[priceHi - 1] != 0xA9)
                return "The Ancient Cave chest code isn't where expected.";
            // the table reads: LDA $91FFCA,X / LDA $91FFDC,X / LDA $94EEA0,X / LDA $94F13D,X
            if (!Long(File(0x83, 0x9103), 0x91FFCA) || !Long(File(0x83, 0x91B8), 0x91FFDC) ||
                !Long(File(0x83, 0x91CD), 0x94EEA0) || !Long(File(0x83, 0x915F), 0x94F13D))
                return "The Ancient Cave loot tables have been moved by another tool.";
            int prev = 256;
            for (int i = 0; i < 5; i++) { int v = data[threshold[i]]; if (v > prev) return "The chest odds look unusual."; prev = v; }
            return null;
        }

        private bool Long(int headered, int addr)
        {
            int o = headered + adjust;
            return data[o] == 0xBF && data[o + 1] == (addr & 0xFF) && data[o + 2] == ((addr >> 8) & 0xFF) && data[o + 3] == (addr >> 16);
        }

        private int Word(int o) { return data[o] | (data[o + 1] << 8); }

        // ---- floor 10+ equipment bug ----
        //
        // The "other equipment" pick at $83:9176 reads only the low byte of the list's end
        // offset (0x1000 + 2 x count) and the index maths uses a signed 8-bit multiply, so once
        // a list holds 128+ items (floor 10 in the original) only the first (count - 128) can be
        // picked - all body armor, since the list is in item order. The fix reads the full count
        // (up to 255 per list) for both equipment lists and makes the shared random-range
        // routine $83:9E1B use the unsigned multiplier (same results for every other caller).

        private static readonly byte[] PickOriginal = {
            0xA2, 0x00, 0x00, 0xA5, 0x5D, 0x80, 0x05, 0xA2, 0x00, 0x10, 0xA5, 0x60, 0x86, 0x54,
            0x4A, 0x20, 0x11, 0x9E, 0xC2, 0x20, 0x8A, 0x18, 0x65, 0x54, 0xAA };
        private static readonly byte[] PickFixed = {
            0xC2, 0x20, 0xA5, 0x5D, 0x80, 0x05, 0xEA,         // weapons: REP #$20, LDA $5D, BRA
            0xC2, 0x20, 0xA5, 0x60,                           // others ($9176): REP #$20, LDA $60
            0x4A, 0xE2, 0x20, 0x20, 0x1B, 0x9E,               // LSR (count, base/2 in B), SEP, JSR $9E1B
            0xC2, 0x20, 0x0A, 0xAA,                           // REP, ASL, TAX = base + 2 x index
            0xEA, 0xEA, 0xEA, 0xEA };
        private static readonly byte[] RangeOriginal = {
            0x8F, 0x1C, 0x21, 0x00, 0x22, 0xC7, 0x82, 0x80, 0x8F, 0x1B, 0x21, 0x00, 0x7B,
            0x8F, 0x1B, 0x21, 0x00, 0xAF, 0x35, 0x21, 0x00, 0x60 };
        private static readonly byte[] RangeFixed = {
            0x8F, 0x02, 0x42, 0x00,                           // STA $004202 (range)
            0x22, 0xC7, 0x82, 0x80,                           // JSL $8082C7 (random 0-255)
            0x8F, 0x03, 0x42, 0x00,                           // STA $004203
            0xEA, 0xEA, 0xEA,                                 // wait for the multiplier
            0xAF, 0x17, 0x42, 0x00,                           // LDA $004217 = range x random / 256
            0x60, 0xEA, 0xEA };

        private int PickOffset { get { return File(0x83, 0x916F) + adjust; } }
        private int RangeOffset { get { return File(0x83, 0x9E1B) + adjust; } }

        private bool Matches(int o, byte[] b)
        {
            if (o < 0 || o + b.Length > data.Length) return false;
            for (int i = 0; i < b.Length; i++) if (data[o + i] != b[i]) return false;
            return true;
        }

        /// <summary>1 = fixed, 0 = original game code, -1 = changed by something else (left alone).</summary>
        public int BugFixState
        {
            get
            {
                if (Matches(PickOffset, PickFixed) && Matches(RangeOffset, RangeFixed)) return 1;
                if (Matches(PickOffset, PickOriginal) && Matches(RangeOffset, RangeOriginal)) return 0;
                return -1;
            }
        }

        public void SetBugFix(bool on)
        {
            if (BugFixState < 0) return;
            Array.Copy(on ? PickFixed : PickOriginal, 0, data, PickOffset, PickOriginal.Length);
            Array.Copy(on ? RangeFixed : RangeOriginal, 0, data, RangeOffset, RangeOriginal.Length);
        }

        /// <summary>How many entries of a list the game can actually pick from.</summary>
        public int Pickable(int count)
        {
            if (BugFixState == 1) return Math.Min(count, 255);
            return count & 0x7F;
        }

        // ---- tables ----

        public int GetEntry(int table, int k) { return Word(Tables[table].Offset + k * 2) & 0x1FF; }

        public void SetEntry(int table, int k, int item)
        {
            int o = Tables[table].Offset + k * 2;
            int v = (Word(o) & ~0x1FF) | (item & 0x1FF);   // keep any flag bits
            data[o] = (byte)v; data[o + 1] = (byte)(v >> 8);
        }

        public int EntryOffset(int table, int k) { return Tables[table].Offset + k * 2; }

        // ---- odds ----

        /// <summary>Chests (out of 256) that come from each source.</summary>
        public int[] GetShares()
        {
            int[] s = new int[Sources];
            int prev = 256;
            for (int i = 0; i < 5; i++) { s[i] = prev - data[threshold[i]]; prev = data[threshold[i]]; }
            s[5] = prev;
            return s;
        }

        /// <summary>Sets the shares (first five; the common table gets the rest). The weapon share is at least 1.</summary>
        public void SetShares(int[] s)
        {
            int t = 256;
            for (int i = 0; i < 5; i++)
            {
                t -= s[i];
                if (i == 0 && t > 255) t = 255;
                if (t < 0) t = 0;
                data[threshold[i]] = (byte)t;
            }
        }

        public int ThresholdOffset(int i) { return threshold[i]; }

        public int IrisChance { get { return data[irisChance]; } set { data[irisChance] = (byte)Math.Max(0, Math.Min(255, value)); } }
        public int IrisChanceOffset { get { return irisChance; } }
        public int ProvidenceFloor { get { return data[provFloor]; } set { data[provFloor] = (byte)Math.Max(1, Math.Min(255, value)); } }
        public int ProvidenceFloorOffset { get { return provFloor; } }

        public int ProvidenceItem
        {
            get { return Word(provItem) & 0x1FF; }
            set { int v = (Word(provItem) & ~0x1FF) | (value & 0x1FF); data[provItem] = (byte)v; data[provItem + 1] = (byte)(v >> 8); }
        }
        public int ProvidenceItemOffset { get { return provItem; } }

        public int PriceStep
        {
            get { return data[priceLo] | (data[priceHi] << 8); }
            set { int v = Math.Max(1, Math.Min(65535, value)); data[priceLo] = (byte)v; data[priceHi] = (byte)(v >> 8); }
        }
        public int PriceLoOffset { get { return priceLo; } }
        public int PriceHiOffset { get { return priceHi; } }

        public int NoLimitFloor { get { return data[noLimitFloor]; } set { data[noLimitFloor] = (byte)Math.Max(1, Math.Min(255, value)); } }
        public int NoLimitFloorOffset { get { return noLimitFloor; } }

        /// <summary>Price limit on a floor (items must cost less), or -1 for no limit. Mirrors $83:9048.</summary>
        public int PriceLimit(int floor)
        {
            if (floor >= NoLimitFloor) return -1;
            long v = (long)PriceStep * ((floor + 1) & 0xFF);
            return (int)(v & 0xFFFF);
        }

        /// <summary>Is this item in the run-time equipment lists at all (ignoring price)? Mirrors $83:907F.</summary>
        public static bool CaveEligible(ItemData items, int id)
        {
            byte[] d = items.RawData;
            int o = items.RecordOffset(id);
            return (d[o] & 0x02) != 0 && (d[o] & 0x20) == 0 && (d[o + 1] & 0x20) == 0;
        }
    }
}
