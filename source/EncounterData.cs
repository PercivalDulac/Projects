using System;
using System.Collections.Generic;

namespace com.lufiaIIEditor.RomInfo
{
    /// <summary>
    /// Random encounters and fixed battles. All tables are fixed-size and edited in place.
    ///
    /// Monster groups  $97:BE93, 192 x 8 bytes (enemy IDs, FF = empty). Built into a fight by $81:839F:
    ///   byte 0 is the leader (if byte 1 is set too, one of the two, 50/50); the other bytes are
    ///   shuffled and the first two found join as extra kinds; enemies are added at random until
    ///   their total size reaches the limit. Byte 0 = FF with byte 1 set means a fixed line-up:
    ///   byte 1 is a raw value (item-drop bits) and bytes 2-7 are the six enemies exactly.
    /// World zones     $97:C493 + zone * 10: 5 x (group, weight). The zone of each 4x4-tile block of
    ///   the world map comes from resource 193 (a 64 x 64 grid), read by $86:9CF3.
    /// Sea             $86:9DC7, 15 groups: entry = clamp($0BBB - 30, 0, 18) / 2 + random 0-5.
    /// Fixed battles   $97:C53D: 39 pointers, 30-byte records: 6 enemy IDs, 6 x (x, y), 12 more bytes.
    /// Dungeon maps    $97:B9AC: 248 pointers (by map) to lists of groups, one per monster on the map.
    /// Ancient Cave    $94:D95C: floor n uses entries 2(n-1) .. 2(n-1)+5 (random), read by $83:9A30.
    /// </summary>
    class EncounterData
    {
        public const int GroupCount = 192, GroupSize = 8;
        public const int ZoneCount = 17, ZoneSlots = 5;      // zone 0 = no encounters
        public const int SeaCount = 15;
        public const int BattleSize = 30;
        public const int MapCount = 248;
        public const int CaveEntries = 202;
        public const int ZoneGridResource = 0x193;

        private readonly byte[] data;
        private readonly int adjust;
        public readonly int Groups, Zones, Sea, Battles, Maps, Cave, CaveSprites;
        private int battleCount;
        private int[] mapStart = new int[MapCount], mapEnd = new int[MapCount];

        private static int File(int bank, int addr) { return 0x200 + (bank & 0x7F) * 0x8000 + (addr - 0x8000); }

        public EncounterData(byte[] romData, bool hasHeader)
        {
            data = romData;
            adjust = hasHeader ? 0 : -0x200;
            Groups = File(0x97, 0xBE93) + adjust;
            Zones = File(0x97, 0xC493) + adjust;
            Sea = File(0x86, 0x9DC7) + adjust;
            Battles = File(0x97, 0xC53D) + adjust;
            Maps = File(0x97, 0xB9AC) + adjust;
            Cave = File(0x94, 0xD95C) + adjust;
            CaveSprites = File(0x94, 0xDDF6) + adjust;   // map sprite per group in the cave ($80:AB86)
        }

        public byte[] RawData { get { return data; } }
        private int Word(int o) { return data[o] | (data[o + 1] << 8); }

        private bool Code(int bank, int addr, params byte[] bytes)
        {
            int o = File(bank, addr) + adjust;
            for (int i = 0; i < bytes.Length; i++) if (data[o + i] != bytes[i]) return false;
            return true;
        }

        public string Validate()
        {
            if (data.Length < Groups + GroupCount * GroupSize) return "ROM is too small.";
            if (!Code(0x81, 0x83B2, 0xB9, 0x93, 0xBE) || !Code(0x86, 0x9D22, 0xBF, 0x94, 0xC4, 0x97) ||
                !Code(0x81, 0x82C6, 0xBF, 0x3D, 0xC5, 0x97) || !Code(0x86, 0x9CE3, 0xBD, 0xC7, 0x9D) ||
                !Code(0x81, 0x837D, 0xBD, 0xAC, 0xB9) || !Code(0x83, 0x9A42, 0xBF, 0x5C, 0xD9, 0x94) ||
                !Code(0x80, 0xAB86, 0xBF, 0xF6, 0xDD, 0x94))
                return "The encounter code isn't where expected.";
            // Fixed battles: the pointer table ends where the first record starts (the smallest pointer).
            int n = 0, minPtr = Word(Battles);
            while (2 * n < minPtr && n < 200)
            {
                int p = Word(Battles + 2 * n);
                if (p >= 2 * (n + 1) && p < minPtr) minPtr = p;
                n++;
            }
            if (n < 1 || n > 100 || Battles + minPtr + n * BattleSize > data.Length) return "The fixed battle table looks unusual.";
            battleCount = n;
            // Dungeon map lists: each runs to the next list start (the last one to the group table).
            List<int> starts = new List<int>();
            for (int m = 0; m < MapCount; m++)
            {
                int s = Maps + Word(Maps + 2 * m);
                if (s < Maps + MapCount * 2 || s > Groups) return "The map encounter lists look unusual.";
                mapStart[m] = s;
                if (!starts.Contains(s)) starts.Add(s);
            }
            starts.Sort();
            for (int m = 0; m < MapCount; m++)
            {
                int k = starts.IndexOf(mapStart[m]);
                mapEnd[m] = k + 1 < starts.Count ? starts[k + 1] : Groups;
            }
            return null;
        }

        // ---------------- groups ----------------

        public int GroupOffset(int g) { return Groups + g * GroupSize; }
        public int GetGroupSlot(int g, int k) { return data[GroupOffset(g) + k]; }
        public void SetGroupSlot(int g, int k, int v) { data[GroupOffset(g) + k] = (byte)v; }

        /// <summary>Fixed line-up: byte 0 empty, byte 1 set (a raw value), bytes 2-7 the enemies.</summary>
        public bool IsFixedLineup(int g) { return GetGroupSlot(g, 0) == 0xFF && GetGroupSlot(g, 1) != 0xFF; }

        // ---------------- world zones ----------------

        public int ZoneOffset(int z, int k) { return Zones + z * 10 + k * 2; }
        public int GetZoneGroup(int z, int k) { return data[ZoneOffset(z, k)]; }
        public int GetZoneWeight(int z, int k) { return data[ZoneOffset(z, k) + 1]; }
        public void SetZoneGroup(int z, int k, int g) { data[ZoneOffset(z, k)] = (byte)g; }
        public void SetZoneWeight(int z, int k, int w) { data[ZoneOffset(z, k) + 1] = (byte)Math.Max(0, Math.Min(255, w)); }

        // ---------------- sea ----------------

        public int GetSea(int i) { return data[Sea + i]; }
        public void SetSea(int i, int g) { data[Sea + i] = (byte)g; }

        // ---------------- fixed battles ----------------

        public int BattleCount { get { return battleCount; } }
        public int BattleOffset(int b) { return Battles + Word(Battles + 2 * b); }
        public int GetBattleEnemy(int b, int k) { return data[BattleOffset(b) + k]; }
        public void SetBattleEnemy(int b, int k, int e) { data[BattleOffset(b) + k] = (byte)e; }
        public int GetBattlePos(int b, int k, int xy) { return data[BattleOffset(b) + 6 + k * 2 + xy]; }
        public void SetBattlePos(int b, int k, int xy, int v) { data[BattleOffset(b) + 6 + k * 2 + xy] = (byte)Math.Max(0, Math.Min(255, v)); }

        /// <summary>Other battles that share this record (the table has one duplicate pointer).</summary>
        public List<int> BattlesSharing(int b)
        {
            List<int> l = new List<int>();
            for (int i = 0; i < battleCount; i++) if (i != b && BattleOffset(i) == BattleOffset(b)) l.Add(i);
            return l;
        }

        // ---------------- dungeon maps / cave (read for "used by"; edited in stage 2) ----------------

        // ---------------- world map encounter rate ($86:9C71) ----------------
        // After each step the counter $11E3 goes up. Below SafeSteps nothing happens; then the game
        // rolls 0..(min(steps + Ramp, 255) - 1) and a fight starts if the roll is Threshold or more.
        // At sea, a fight that passes this check also needs a 0-99 roll below SeaChance.
        public int SafeStepsOffset { get { return File(0x86, 0x9C88) + adjust; } }
        public int RampOffset { get { return File(0x86, 0x9C92) + adjust; } }
        public int ThresholdOffset { get { return File(0x86, 0x9C9C) + adjust; } }
        public int SeaChanceOffset { get { return File(0x86, 0x9CC0) + adjust; } }

        public bool HasRate
        {
            get
            {
                return data[SafeStepsOffset - 1] == 0xC9 && data[RampOffset - 1] == 0x69 &&
                       data[ThresholdOffset - 1] == 0xC9 && data[SeaChanceOffset - 1] == 0xC9;
            }
        }

        public int SafeSteps { get { return data[SafeStepsOffset]; } set { data[SafeStepsOffset] = (byte)Math.Max(0, Math.Min(255, value)); } }
        public int Ramp { get { return data[RampOffset]; } set { data[RampOffset] = (byte)Math.Max(0, Math.Min(255, value)); } }
        public int Threshold { get { return data[ThresholdOffset]; } set { data[ThresholdOffset] = (byte)Math.Max(0, Math.Min(255, value)); } }
        public int SeaChance { get { return data[SeaChanceOffset]; } set { data[SeaChanceOffset] = (byte)Math.Max(0, Math.Min(100, value)); } }

        /// <summary>Chance of a fight on the step that brings the counter to s (0..1).</summary>
        public static double StepChance(int s, int safe, int ramp, int threshold)
        {
            if (s < safe) return 0;
            int range = s + ramp;
            if (range > 255) range = 255;
            if (range <= 0) return 0;
            return Math.Max(0, range - threshold) / (double)range;
        }

        /// <summary>Average number of steps from one fight to the next (land), or -1 if fights never happen.</summary>
        public static double AverageSteps(int safe, int ramp, int threshold)
        {
            double alive = 1, expected = 0;
            for (int s = 1; s < 2000; s++)
            {
                double p = StepChance(s, safe, ramp, threshold);
                expected += alive * p * s;
                alive *= 1 - p;
                if (alive < 1e-9) break;
            }
            return alive > 0.01 ? -1 : expected;
        }

        public int MapListStart(int m) { return mapStart[m]; }
        public int MapListLength(int m) { return mapEnd[m] - mapStart[m]; }
        public int GetCave(int i) { return data[Cave + i]; }
        public void SetCave(int i, int g) { data[Cave + i] = (byte)g; }
        public int GetCaveSprite(int g) { return data[CaveSprites + g]; }
        public void SetCaveSprite(int g, int v) { data[CaveSprites + g] = (byte)v; }

        /// <summary>The 6 cave entries a floor picks from at random.</summary>
        public static int CaveFirstEntry(int floor) { return 2 * (floor - 1); }

        /// <summary>Floors that use a cave entry.</summary>
        public static void FloorsOfEntry(int i, out int lo, out int hi)
        {
            lo = Math.Max(1, (i - 5 + 1) / 2 + 1);
            while (CaveFirstEntry(lo) + 5 < i) lo++;
            hi = Math.Min(99, i / 2 + 1);
            while (hi > 1 && CaveFirstEntry(hi) > i) hi--;
        }

        public int GetMapEntry(int m, int k) { return data[mapStart[m] + k]; }
        public void SetMapEntry(int m, int k, int g) { data[mapStart[m] + k] = (byte)g; }

        /// <summary>Other maps that share this map's list.</summary>
        public List<int> MapsSharing(int m)
        {
            List<int> l = new List<int>();
            for (int i = 0; i < MapCount; i++) if (i != m && mapStart[i] == mapStart[m]) l.Add(i);
            return l;
        }

        /// <summary>Groups (index) and fixed battles (-1 - index) that contain an enemy.</summary>
        public List<int> GroupsWithEnemy(int enemy)
        {
            List<int> l = new List<int>();
            for (int g = 0; g < GroupCount; g++)
                for (int k = 0; k < GroupSize; k++)
                    if (GetGroupSlot(g, k) == enemy && !(k == 1 && IsFixedLineup(g))) { l.Add(g); break; }
            return l;
        }

        public List<int> BattlesWithEnemy(int enemy)
        {
            List<int> l = new List<int>();
            for (int b = 0; b < battleCount; b++)
                for (int k = 0; k < 6; k++)
                    if (GetBattleEnemy(b, k) == enemy) { l.Add(b); break; }
            return l;
        }

        /// <summary>Where a group is used, in a few words per place.</summary>
        public List<string> GroupUsage(int g)
        {
            List<string> l = new List<string>();
            for (int z = 1; z < ZoneCount; z++)
                for (int k = 0; k < ZoneSlots; k++)
                    if (GetZoneGroup(z, k) == g && GetZoneWeight(z, k) > 0) { l.Add("World zone " + z); break; }
            for (int i = 0; i < SeaCount; i++) if (GetSea(i) == g) { l.Add("Sea"); break; }
            List<int> maps = new List<int>();
            for (int m = 0; m < MapCount; m++)
                for (int k = 0; k < MapListLength(m); k++)
                    if (data[mapStart[m] + k] == g && !maps.Contains(m)) maps.Add(m);
            if (maps.Count > 0)
            {
                List<string> s = new List<string>();
                foreach (int m in maps) s.Add(MapNames.Label(m));
                l.Add((maps.Count == 1 ? "Map " : "Maps ") + string.Join(", ", s.ToArray()));
            }
            int lo = -1, hi = -1;
            for (int f = 1; f <= 99; f++)
            {
                bool on = false;
                for (int k = 0; k < 6; k++) if (2 * (f - 1) + k < CaveEntries && GetCave(2 * (f - 1) + k) == g) on = true;
                if (on) { if (lo < 0) lo = f; hi = f; }
            }
            if (lo > 0) l.Add("Ancient Cave floors " + (lo == hi ? lo.ToString() : lo + "-" + hi));
            return l;
        }

        // ---------------- world zone grid (resource 193) ----------------

        /// <summary>Unpacks one of the game's compressed resources (loader $80:8E9D). Null if it fails.</summary>
        public byte[] Resource(int index)
        {
            try
            {
                int table = File(0xA7, 0x8000) + adjust;
                int lin = data[table + 3 * index] | (data[table + 3 * index + 1] << 8) | (data[table + 3 * index + 2] << 16);
                int p = table + lin;
                int size = data[p] | (data[p + 1] << 8);
                p += 2;
                byte[] outb = new byte[size];
                int o = 0;
                while (o < size)
                {
                    int flag = data[p++];
                    int bits = 8;
                    while (bits > 0 && o < size)
                    {
                        int b = data[p];
                        if (b < 0x80) { outb[o++] = (byte)b; p++; continue; }
                        bool copy = (flag & 0x80) != 0;
                        flag = (flag << 1) & 0xFF;
                        bits--;
                        if (!copy) { outb[o++] = (byte)b; p++; continue; }
                        int n = data[p + 1], count, off;
                        if ((n & 0x0F) != 0)
                        {
                            count = (n & 0x0F) + 2;
                            off = (((b << 8) | n) >> 4) | 0xF000;
                            p += 2;
                        }
                        else
                        {
                            int m = data[p + 2];
                            count = (m & 0x3F) + 3;
                            off = ((((((b << 8) | n) >> 4) | 0xF000) << 2) | (m >> 6)) & 0xFFFF;
                            p += 3;
                        }
                        int src = o + off - 0x10000;
                        if (src < 0) return null;
                        for (int k = 0; k < count && o < size; k++) outb[o++] = outb[src + k];
                    }
                }
                return outb;
            }
            catch (Exception) { return null; }
        }
    }

    /// <summary>
    /// Names for the game's maps, which the ROM doesn't have. Kept in MapNames.txt next to the
    /// editor ("0B=Gruberik Cave", one per line) so they can be shared between modders.
    /// </summary>
    static class MapNames
    {
        private static Dictionary<int, string> names;
        public static string FilePath
        {
            get { return System.IO.Path.Combine(System.Windows.Forms.Application.StartupPath, "MapNames.txt"); }
        }

        private static void Load()
        {
            if (names != null) return;
            names = new Dictionary<int, string>();
            try
            {
                if (!System.IO.File.Exists(FilePath)) return;
                foreach (string line in System.IO.File.ReadAllLines(FilePath))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    int m;
                    if (int.TryParse(line.Substring(0, eq).Trim(), System.Globalization.NumberStyles.HexNumber, null, out m))
                        names[m] = line.Substring(eq + 1).Trim();
                }
            }
            catch (Exception) { }
        }

        public static string Get(int m)
        {
            Load();
            string n;
            return names.TryGetValue(m, out n) ? n : "";
        }

        /// <summary>Saves a name (empty removes it). Returns false if the file couldn't be written.</summary>
        public static bool Set(int m, string name)
        {
            Load();
            name = (name ?? "").Replace("=", "-").Trim();
            if (name.Length == 0) names.Remove(m); else names[m] = name;
            try
            {
                List<int> keys = new List<int>(names.Keys);
                keys.Sort();
                List<string> lines = new List<string>();
                lines.Add("# Lufia II Editor map names: map number (hex) = name");
                foreach (int k in keys) lines.Add(k.ToString("X2") + "=" + names[k]);
                System.IO.File.WriteAllLines(FilePath, lines.ToArray());
                return true;
            }
            catch (Exception) { return false; }
        }

        /// <summary>"0B" or "0B Gruberik Cave".</summary>
        public static string Label(int m)
        {
            string n = Get(m);
            return m.ToString("X2") + (n.Length > 0 ? " " + n : "");
        }
    }
}
