using System;
using System.Collections.Generic;

namespace com.lufiaIIEditor.RomInfo
{
    /// <summary>
    /// Fixes for bugs in the original game, taken from Relnqshd's "Lufia II Fixxxer Deluxe"
    /// (v1.0 beta, never finished or released by him) and his Artea name patch, and applied here
    /// one at a time instead of as one big patch.
    ///
    /// Each fix is a list of byte changes: the original game's bytes and the fixed bytes. A fix is
    /// "on" when every change matches the fixed bytes, "off" when every change matches the original,
    /// and left alone when anything else is there (another tool or an edit on another tab changed it).
    /// Code fixes use the same places in the ROM as Fixxxer, including its small routines in unused
    /// space ($80:FC1A, $82:FFB7). Enemy and item fixes are found through the game's own tables, so
    /// they stay correct if those records have moved within their blocks.
    ///
    /// Not taken from Fixxxer: the changes that make enemy scripts longer (Nosferato's Contact,
    /// Copper Dragon's and Ninja's elements, Erim's spells) - Fixxxer moves the whole enemy table into
    /// expanded space for those - and the map, graphics and text changes.
    /// </summary>
    class GameFixes
    {
        public class Change
        {
            public int Offset;
            public byte[] Original;
            public byte[] Fixed;
        }

        public class Fix
        {
            public string Group;
            public string Name;
            public string Detail;
            public string Source;
            public string Problem;      // why it can't be used on this ROM, or null
            public bool BalanceChange;  // a design change rather than a bug fix (not reported by Check ROM)
            public readonly List<Change> Changes = new List<Change>();
        }

        public const int STATE_OFF = 0, STATE_ON = 1, STATE_OTHER = -1;

        public const string FixxxerCredit = "Relnqshd (Lufia II Fixxxer Deluxe), and those who helped him";
        public const string ArteaCredit = "Relnqshd (Artea name patch), and those who helped him";

        private readonly byte[] data;
        private readonly int adjust;
        public readonly List<Fix> Fixes = new List<Fix>();

        private static int File(int bank, int addr) { return 0x200 + (bank & 0x7F) * 0x8000 + (addr - 0x8000); }

        /// <summary>items / enemies / party may be null (or not loaded) - the fixes that need them are then unavailable.</summary>
        public GameFixes(byte[] romData, bool hasHeader, ItemData items, EnemyData enemies, PartyData party)
        {
            data = romData;
            adjust = hasHeader ? 0 : -0x200;
            Build(items, enemies, party);
        }

        public byte[] RawData { get { return data; } }

        // ---------------- the fixes ----------------

        private void Build(ItemData items, EnemyData enemies, PartyData party)
        {
            Fix f;

            // Equip menu: the name is drawn over the old one without clearing the rest of the line,
            // so "Knife" over "No equip" shows "Knifeuip". $80:8BA0 now jumps to a few lines at
            // $80:FC1A that blank the next 12 tiles first, then carries on.
            f = Add("Menus", "Equip menu: leftover letters after an item name",
                "Switching characters on the Equip screen could leave the end of the previous name showing " +
                "(e.g. \"Knifeuip\"). The rest of the line is now cleared.", FixxxerCredit);
            Code(f, 0x80, 0x8BA0, new byte[] { 0x8E, 0x75, 0x05 }, new byte[] { 0x4C, 0x1A, 0xFC });
            Code(f, 0x80, 0xFC1A,
                new byte[] { 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0x8E, 0xAA, 0xAA, 0xAA, 0xAA, 0xA8,
                             0xAA, 0xAA, 0xAA, 0xAA, 0x2A, 0xA2, 0xA8, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA },
                new byte[] { 0x8E, 0x75, 0x05,             // STX $0575 (the instruction replaced above)
                             0x5A, 0xA0, 0x0B, 0x00,       // PHY, LDY #$000B
                             0xA9, 0x20, 0x9D, 0x40, 0x00, // loop: LDA #$20, STA $0040,X
                             0x9E, 0x41, 0x00,             //       STZ $0041,X
                             0xE8, 0xE8, 0x88, 0x10, 0xF5, //       INX, INX, DEY, BPL loop
                             0x7A, 0x4C, 0xA3, 0x8B });    // PLY, JMP $8BA3

            // Settings menu: Left and Right each go through a jump table indexed by the option
            // ($14B3, 0-3). The Left table has only three entries, so Left on the fourth option jumps to
            // whatever the next bytes spell ($B3AD). The fix adds the missing entry (the same toggle
            // Right uses) and moves the two instructions it displaces to $82:FFB7.
            f = Add("Menus", "Settings menu: pressing Left on the last option",
                "The Left jump table was one entry short, so Left on the last option ran random code " +
                "(the \"Level 0\" / music menu bug). It now does the same as Right.", FixxxerCredit);
            Code(f, 0x82, 0xBF32, new byte[] { 0x33 }, new byte[] { 0x35 });   // BEQ $BF66 -> $BF68
            Code(f, 0x82, 0xBF49, new byte[] { 0x0E }, new byte[] { 0x10 });   // BNE $BF58 -> $BF5A
            Code(f, 0x82, 0xBF59,
                new byte[] { 0xB3, 0x14, 0x20, 0x28, 0x80, 0x88, 0xBF, 0x99, 0xBF, 0xA3, 0xBF, 0xAD,
                             0xBF, 0xA9, 0x02, 0x22, 0x3B, 0x95, 0x80, 0x80, 0xEA },
                new byte[] { 0xBF, 0xAD,                   // 4th Left entry: $BFAD
                             0xB3, 0x14, 0x20, 0x28, 0x80, // LDA $14B3, JSR $8028 (Right)
                             0x88, 0xBF, 0x99, 0xBF, 0xA3, 0xBF, 0xAD, 0xBF,
                             0x4C, 0xB7, 0xFF, 0xEA,       // JMP $FFB7, NOP
                             0x80, 0xEC });                // BRA $BF5A
            Code(f, 0x82, 0xFFB7,
                new byte[] { 0xAA, 0xAA, 0xAA, 0xAB, 0xAA, 0xAA, 0xAA, 0xA8, 0xAA },
                new byte[] { 0xA9, 0x02, 0x22, 0x3B, 0x95, 0x80, 0x4C, 0x6C, 0xBF }); // LDA #$02, JSL $80953B, JMP $BF6C

            // ---- items ----
            const string needItems = "The item data isn't where expected in this ROM.";

            // Statue: item record +0 bit 0x40 = can be used from the field menu. Statue's menu use
            // freezes or resets the game; it's meant for battle ("Restores HP ... during battle").
            f = Add("Items", "Statue: no longer usable from the menu",
                "Using the Statue from the main menu froze or reset the game. It's a battle item, so it can " +
                "now only be used in battle.", FixxxerCredit);
            if (items == null) f.Problem = needItems;
            else Record(f, items.RecordOffset(0x191), 0, new byte[] { 0xC1 }, new byte[] { 0x81 });

            // Sea ring: weapon script 17 27 hi lo ORs an element into the attack. The original sets
            // 0x0020 (Anti-sea), which no enemy script ever checks, so the ring did nothing.
            f = Add("Items", "Sea ring: give its attacks an element that works",
                "The Sea ring adds Anti-sea to attacks, which no enemy reacts to, so it does nothing. " +
                "Fixxxer makes it Fire instead. (Any element can be chosen on the Equipment tab.)", FixxxerCredit);
            if (items == null) f.Problem = needItems;
            else Record(f, items.RecordOffset(0x165), 0x1B, new byte[] { 0x00, 0x20 }, new byte[] { 0x02, 0x00 });

            // Lucky blade: two items share the name (05C and 05D). 05D could also be equipped by Tia.
            f = Add("Items", "Lucky blade (item 05D): same characters as the other one",
                "The two Lucky blades differ in who can equip them: item 05D also allows Tia. " +
                "It now matches item 05C.", FixxxerCredit);
            if (items == null) f.Problem = needItems;
            else Record(f, items.RecordOffset(0x05D), 8, new byte[] { 0x75 }, new byte[] { 0x65 });

            // ---- enemies ----
            const string needEnemies = "The enemy data isn't where expected in this ROM.";

            // "15 80 14 / 16 80 80 00 / 06 80 00 00 jj 00": take the user's status bits, keep the
            // Mirror bit, then "runs only if it is NOT zero" - so they cast Mirror only when it is
            // already up. Fixxxer flips it to "equals" (07).
            f = Add("Enemies", "Mirror check (Archfiend, Sphinx, Demise, Leech, Nosferato, Sly Fox)",
                "These enemies only cast Mirror when they already had it, so it never helped them. " +
                "They now cast it when they don't have it.", FixxxerCredit);
            if (enemies == null) f.Problem = needEnemies;
            else
            {
                int[,] mirror = { { 0x17, 0x64 }, { 0x29, 0x32 }, { 0x78, 0x46 }, { 0x79, 0x32 }, { 0x81, 0x7E }, { 0xB7, 0x32 } };
                for (int k = 0; k < mirror.GetLength(0); k++)
                    Record(f, enemies.RecordOffset(mirror[k, 0]), mirror[k, 1], new byte[] { 0x06 }, new byte[] { 0x07 });
            }

            // Bolt Fish AI (record +0x28): the block at +0x3B is "05 c4 3B 00 / 2C 15 / 04 3B 00",
            // a random skip and a jump that both go back to +0x3B itself, so it can loop forever.
            // +0x45 is the normal attack after it.
            f = Add("Enemies", "Bolt Fish: no freeze when it runs out of MP",
                "Part of its battle script jumped back to its own start, so it could loop forever and lock " +
                "up the game. It now goes on to a normal attack.", FixxxerCredit);
            if (enemies == null) f.Problem = needEnemies;
            else
            {
                Record(f, enemies.RecordOffset(0x55), 0x3D, new byte[] { 0x3B }, new byte[] { 0x45 });
                Record(f, enemies.RecordOffset(0x55), 0x42, new byte[] { 0x3B }, new byte[] { 0x45 });
            }

            f = Add("Enemies", "Dark Warrior: correct palette",
                "Dark Warrior used the wrong colour palette (record +10, also on the Enemy Overview tab).", FixxxerCredit);
            if (enemies == null) f.Problem = needEnemies;
            else Record(f, enemies.RecordOffset(0xB5), EnemyData.OFS_PATTERN, new byte[] { 0x00 }, new byte[] { 0x01 });

            // Fixed battles 31 and 33 ($97:C53D table): Gades C6 (7,500 HP) -> Gades DE (10,000 HP).
            f = Add("Enemies", "\"Frue\" Gades in Shuman Tower and the Fortress of Doom",
                "These two fights use the weaker Gades (7,500 HP). This switches them to the strongest " +
                "version (10,000 HP). A balance change rather than a bug fix. Fixed battles 31 and 33 on the Encounters tab.",
                FixxxerCredit);
            f.BalanceChange = true;
            Code(f, 0x97, 0xC92D, new byte[] { 0xC6 }, new byte[] { 0xDE });
            Code(f, 0x97, 0xC969, new byte[] { 0xC6 }, new byte[] { 0xDE });

            // ---- names ----
            f = Add("Names", "Artea's name in menus (\"Arty\" -> \"Artea\")",
                "The story calls him Artea, but the menus and his bow said \"Arty\". This changes his " +
                "starting name and \"Arty's bow\" to \"Artea's bow\".", ArteaCredit);
            if (party == null) f.Problem = "The new-game setup data isn't where expected in this ROM.";
            else if (items == null) f.Problem = needItems;
            else
            {
                Record(f, party.NameOffsets[3], 0,
                    new byte[] { 0x41, 0x72, 0x74, 0x79, 0x00, 0x00 },          // "Arty"
                    new byte[] { 0x41, 0x72, 0x74, 0x65, 0x61, 0x00 });         // "Artea"
                int bow = FindItem(items, "Arty's bow");
                if (bow < 0) bow = FindItem(items, "Artea's bow");
                if (bow < 0) f.Problem = "Artea's bow wasn't found by name (it may have been renamed).";
                else Record(f, items.NameOffset(bow), 0, Ascii("Arty's bow  "), Ascii("Artea's bow "));
            }

            foreach (Fix x in Fixes)
            {
                if (x.Problem != null) continue;
                foreach (Change c in x.Changes)
                    if (c.Offset < 0 || c.Offset + c.Original.Length > data.Length)
                    { x.Problem = "This ROM is too small for this fix."; break; }
            }
        }

        private Fix Add(string group, string name, string detail, string source)
        {
            Fix f = new Fix();
            f.Group = group; f.Name = name; f.Detail = detail; f.Source = source;
            Fixes.Add(f);
            return f;
        }

        private void Code(Fix f, int bank, int addr, byte[] original, byte[] fixedBytes)
        {
            Record(f, File(bank, addr) + adjust, 0, original, fixedBytes);
        }

        private static void Record(Fix f, int offset, int rel, byte[] original, byte[] fixedBytes)
        {
            Change c = new Change();
            c.Offset = offset + rel; c.Original = original; c.Fixed = fixedBytes;
            f.Changes.Add(c);
        }

        private static byte[] Ascii(string s)
        {
            byte[] b = new byte[s.Length];
            for (int i = 0; i < s.Length; i++) b[i] = (byte)s[i];
            return b;
        }

        private static int FindItem(ItemData items, string name)
        {
            for (int i = 0; i < ItemData.Count; i++) if (items.GetName(i) == name) return i;
            return -1;
        }

        // ---------------- state ----------------

        private bool Matches(int o, byte[] b)
        {
            if (o < 0 || o + b.Length > data.Length) return false;
            for (int i = 0; i < b.Length; i++) if (data[o + i] != b[i]) return false;
            return true;
        }

        /// <summary>STATE_ON, STATE_OFF, or STATE_OTHER (something else is there; left alone).</summary>
        public int State(Fix f)
        {
            if (f.Problem != null || f.Changes.Count == 0) return STATE_OTHER;
            bool on = true, off = true;
            foreach (Change c in f.Changes)
            {
                if (!Matches(c.Offset, c.Fixed)) on = false;
                if (!Matches(c.Offset, c.Original)) off = false;
            }
            return on ? STATE_ON : off ? STATE_OFF : STATE_OTHER;
        }

        /// <summary>Turns a fix on or off. Does nothing unless it's cleanly on or off now.</summary>
        public bool Set(Fix f, bool on)
        {
            if (State(f) == STATE_OTHER) return false;
            foreach (Change c in f.Changes)
            {
                byte[] b = on ? c.Fixed : c.Original;
                Array.Copy(b, 0, data, c.Offset, b.Length);
            }
            return true;
        }

        /// <summary>Same fixes on another ROM (for comparing with the clean ROM), or null if that ROM won't load.</summary>
        public static GameFixes For(byte[] romData, bool hasHeader)
        {
            ItemData items = new ItemData(romData, hasHeader);
            if (items.Validate() != null) items = null;
            EnemyData enemies = new EnemyData(romData, hasHeader);
            if (enemies.Validate() != null) enemies = null;
            PartyData party = new PartyData(romData, hasHeader);
            if (party.Load() != null) party = null;
            return new GameFixes(romData, hasHeader, items, enemies, party);
        }

        // ---------------- other patches ----------------

        /// <summary>
        /// Recognises Fixxxer Deluxe (or anything like it) having been applied to this ROM: it moves the
        /// enemy records to $D0:A000 and the enemy skill names to $D0:8000, which this editor doesn't
        /// read. Returns a message to show, or null.
        /// </summary>
        public static string RelocationWarning(byte[] d, bool hasHeader)
        {
            int adj = hasHeader ? 0 : -0x200;
            // the battle loader's enemy-table read ($81:FB28: LDA $9685C0,X) and the skill-name routine call ($81:ADC2: JSL $859532)
            int enemyOp = File(0x81, 0xFB28) + adj, skillCall = File(0x81, 0xADC2) + adj;
            if (skillCall < 0 || enemyOp + 3 > d.Length) return null;
            bool enemyMoved = !(d[enemyOp] == 0xC0 && d[enemyOp + 1] == 0x85 && d[enemyOp + 2] == 0x96);
            bool skillsMoved = !(d[skillCall] == 0x22 && d[skillCall + 1] == 0x32 && d[skillCall + 2] == 0x95 && d[skillCall + 3] == 0x85);
            if (!enemyMoved && !skillsMoved) return null;
            bool fixxxer = d[enemyOp] == 0x00 && d[enemyOp + 1] == 0xA0 && d[enemyOp + 2] == 0xD0;
            return (fixxxer ? "This ROM looks like it has Lufia II Fixxxer Deluxe applied." : "Another patch has changed how this ROM finds its enemies.") +
                "\nIt moves the enemy data" + (skillsMoved ? " and enemy skill names" : "") +
                " to places this editor doesn't read, so Enemy Overview, Enemy AI and Encounters are switched off for this ROM." +
                "\n\nTo use those tabs, start from a clean ROM. The Game Fixes tab has most of Fixxxer's fixes as separate tick boxes.";
        }
    }
}
