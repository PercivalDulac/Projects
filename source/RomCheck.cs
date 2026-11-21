using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

using com.lufiaIIEditor.UI;

namespace com.lufiaIIEditor.RomInfo
{
    /// <summary>
    /// File > Check ROM: looks for likely mistakes. Every check runs on the edited ROM and on the
    /// reference ROM; problems the original game already has are listed separately, so new
    /// ones stand out.
    /// </summary>
    static class RomCheck
    {
        private class Issue
        {
            public string Area, Text;
            public Issue(string a, string t) { Area = a; Text = t; }
            public string Key { get { return Area + "|" + Text; } }
        }

        // Order the areas are shown in.
        private static readonly string[] Areas = {
            "Equipment", "Starting party", "Shops", "Spells", "IP moves", "Enemies", "Encounters", "Capsule feeding", "Ancient Cave", "Items" };

        public static string Build(Rom rom)
        {
            byte[] cur = rom.RawData;
            List<Issue> now = Run(cur, rom.HasHeader, rom);
            HashSet<string> before = new HashSet<string>();
            bool haveRef = Reference.Data != null;
            if (haveRef)
            {
                try { foreach (Issue i in Run(Reference.Data, Reference.HasHeader, rom)) before.Add(i.Key); }
                catch (Exception) { haveRef = false; }
            }

            List<Issue> fresh = new List<Issue>(), old = new List<Issue>();
            foreach (Issue i in now) (before.Contains(i.Key) ? old : fresh).Add(i);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("ROM CHECK");
            sb.AppendLine("Things that are probably mistakes, or that the game handles in a surprising way.");
            sb.AppendLine("Nothing here is changed automatically.");
            sb.AppendLine();
            if (!haveRef)
            {
                sb.AppendLine("(No reference ROM, so problems the original game already has can't be told apart.)");
                sb.AppendLine();
            }
            sb.AppendLine(haveRef ? "NEW IN THIS ROM (" + fresh.Count + ")" : "FOUND (" + fresh.Count + ")");
            Write(sb, fresh, "  Nothing found.");
            if (haveRef)
            {
                sb.AppendLine();
                sb.AppendLine("ALSO IN THE " + (Reference.IsCleanRom ? "ORIGINAL GAME" : "SAVED FILE") + " (" + old.Count + ")");
                if (!Reference.IsCleanRom)
                    sb.AppendLine("  (Compared with the file on disk. Set the clean ROM to compare with the original game.)");
                Write(sb, old, "  None.");
            }
            return sb.ToString();
        }

        private static void Write(StringBuilder sb, List<Issue> list, string empty)
        {
            if (list.Count == 0) { sb.AppendLine(empty); return; }
            foreach (string area in Areas)
            {
                bool head = false;
                foreach (Issue i in list)
                {
                    if (i.Area != area) continue;
                    if (!head) { sb.AppendLine("  " + area); head = true; }
                    sb.AppendLine("    - " + i.Text);
                }
            }
        }

        private static string Id(int id) { return id.ToString("X3"); }

        private static List<Issue> Run(byte[] d, bool hdr, Rom rom)
        {
            List<Issue> list = new List<Issue>();
            ItemData items = new ItemData(d, hdr);
            if (items.Validate() != null) return list;
            IPData ip = new IPData(d, hdr);
            bool ipOk = ip.Validate() == null;
            Func<int, string> Item = delegate(int id)
            {
                return id > 0 && id < ItemData.Count ? items.GetName(id).Trim() + " (" + Id(id) + ")" : "#" + Id(id);
            };

            // Spells: offsets file is for a headered ROM.
            int spellCount = rom.SpellNames != null ? rom.SpellNames.Length : 0;
            int[] spellOfs = new int[spellCount];
            for (int s = 0; s < spellCount; s++)
                spellOfs[s] = int.Parse(rom.getSpellOffset(s), NumberStyles.HexNumber) - (hdr ? 0 : 0x200);
            Func<int, string> Spell = delegate(int s)
            {
                if (s < 0 || s >= spellCount) return "spell #" + s;
                StringBuilder n = new StringBuilder();
                for (int k = 0; k < 8; k++) n.Append((char)d[spellOfs[s] + k]);
                return n.ToString().Trim();
            };
            Func<int, int> LearnMask = delegate(int s) { return s >= 0 && s < spellCount ? d[spellOfs[s] + 0x0A] : 0x7F; };

            // ---- equipment ----
            for (int i = 1; i < ItemData.Count; i++)
            {
                if (!items.IsEquipment(i)) continue;
                if ((items.GetSlot(i) & 0x40) != 0) continue;   // dungeon tools (Hook, Bomb...) aren't worn by anyone
                if ((items.GetEquipMask(i) & 0x7F) == 0)
                    list.Add(new Issue("Equipment", Item(i) + ": nobody can equip it."));
            }

            // ---- items ----
            for (int i = 1; i < ItemData.Count; i++)
                if (items.GetName(i).Trim().Length == 0)
                    list.Add(new Issue("Items", "Item " + Id(i) + " has no name."));

            // ---- IP moves ----
            if (ipOk)
            {
                bool[] used = new bool[ip.Count + 1];
                for (int i = 1; i < ItemData.Count; i++)
                {
                    int v;
                    if (items.GetFields(i).TryGetValue(ItemData.FIELD_IP, out v) && v >= 1 && v <= ip.Count) used[v] = true;
                }
                for (int id = 1; id <= ip.Count; id++)
                    if (!used[id]) list.Add(new Issue("IP moves", ip.GetName(id).Trim() + " (" + id + ") isn't on any item, so nobody can use it."));
            }

            // ---- shops ----
            ShopData shops = new ShopData(d, hdr);
            if (shops.Load() == null)
            {
                foreach (ShopData.Shop s in shops.Shops)
                {
                    string where = "Shop " + s.Id.ToString("X2") + " (" + ShopData.LocationOf(s.Id) + ")";
                    for (int b = 2; b <= 4; b++)
                    {
                        if (!s.Has(b)) continue;
                        foreach (int id in s.Lists[b])
                        {
                            if (id <= 0 || id >= ItemData.Count) continue;
                            if (items.GetPrice(id) == 0) list.Add(new Issue("Shops", where + " sells " + Item(id) + " for 0 G."));
                            if (b == 3 && items.GetSlot(id) != 0x01 && items.IsEquipment(id))
                                list.Add(new Issue("Shops", where + " lists " + Item(id) + " with the weapons, but it isn't a weapon."));
                        }
                    }
                    if (s.Has(1))
                        foreach (int id in s.Lists[1])
                            if (id > 0 && id < ItemData.Count && items.GetPrice(id) == 0)
                                list.Add(new Issue("Shops", where + " offers " + Item(id) + " as a casino prize for 0 coins."));
                    if (s.Has(5))
                        foreach (int sp in s.Lists[5])
                            if ((LearnMask(sp) & 0x7F) == 0)
                                list.Add(new Issue("Shops", where + " sells " + Spell(sp) + ", but nobody can learn it."));
                }
            }

            // ---- spells nobody can learn (only worth saying when nothing else gives them) ----
            // (covered by the shop check above; the party check below covers starting spells)

            // ---- starting party ----
            PartyData party = new PartyData(d, hdr);
            if (party.Load() == null)
            {
                for (int c = 0; c < PartyData.Characters; c++)
                {
                    PartyData.Member m = party.Members[c];
                    string who = party.NameOf(c);
                    for (int k = 0; k < 7; k++)
                    {
                        int id = m.Equip[k] & 0x1FF;
                        if (id <= 0 || id >= ItemData.Count) continue;
                        if ((items.GetSlot(id) & (1 << k)) == 0)
                            list.Add(new Issue("Starting party", who + " starts with " + Item(id) + " as " +
                                PartyData.SlotNames[k].ToLower() + ", but it isn't one."));
                        else if (k < 6 && ((items.GetEquipMask(id) >> c) & 1) == 0)
                            list.Add(new Issue("Starting party", who + " starts with " + Item(id) + " equipped, but can't equip it."));
                    }
                    foreach (int sp in m.Spells)
                        if (((LearnMask(sp) >> c) & 1) == 0)
                            list.Add(new Issue("Spells", who + " starts knowing " + Spell(sp) + ", which " + who + " can't normally learn."));
                }
            }

            // ---- enemies ----
            EnemyData enemies = new EnemyData(d, hdr);
            if (enemies.Validate() == null)
            {
                for (int i = 0; i < EnemyData.Count; i++)
                {
                    string who = enemies.GetName(i) + " (" + i.ToString("X2") + ")";
                    if (enemies.GetWord(i, EnemyData.OFS_HP) == 0)
                        list.Add(new Issue("Enemies", who + " has 0 HP."));
                    if (enemies.HasDrop(i))
                    {
                        int item = enemies.GetDropItem(i), ch = enemies.GetDropChance(i);
                        if (item == 0 && ch > 0) list.Add(new Issue("Enemies", who + " has a drop chance but no drop item."));
                        if (item != 0 && ch == 0) list.Add(new Issue("Enemies", who + " drops " + Item(item) + " with chance 0, so never."));
                        if (ch > 100) list.Add(new Issue("Enemies", who + " drop chance is over 100 (anything above 100 works like 100)."));
                    }
                }
            }

            // ---- encounters ----
            EncounterData enc = new EncounterData(d, hdr);
            if (enc.Validate() == null)
            {
                for (int g = 0; g < EncounterData.GroupCount; g++)
                {
                    bool any = false;
                    for (int k = 0; k < EncounterData.GroupSize; k++) if (enc.GetGroupSlot(g, k) != 0xFF) any = true;
                    if (!any && enc.GroupUsage(g).Count > 0) list.Add(new Issue("Encounters", "Group " + g + " is empty but is used (" + string.Join(", ", enc.GroupUsage(g).ToArray()) + ")."));
                    for (int k = 0; k < EncounterData.GroupSize; k++)
                    {
                        int v = enc.GetGroupSlot(g, k);
                        if (v != 0xFF && v >= EnemyData.Count && !(k == 1 && enc.IsFixedLineup(g)))
                            list.Add(new Issue("Encounters", "Group " + g + " slot " + (k + 1) + " holds " + v.ToString("X2") + ", which isn't an enemy."));
                    }
                }
                for (int z = 1; z < EncounterData.ZoneCount; z++)
                {
                    bool groups = false; int total = 0;
                    for (int k = 0; k < EncounterData.ZoneSlots; k++) { total += enc.GetZoneWeight(z, k); if (enc.GetZoneGroup(z, k) != 0) groups = true; }
                    if (groups && total == 0) list.Add(new Issue("Encounters", "World zone " + z + " lists groups but every weight is 0, so it has no fights."));
                    for (int k = 0; k < EncounterData.ZoneSlots; k++)
                        if (enc.GetZoneWeight(z, k) > 0 && enc.GetZoneGroup(z, k) >= EncounterData.GroupCount)
                            list.Add(new Issue("Encounters", "World zone " + z + " slot " + (k + 1) + " uses group " + enc.GetZoneGroup(z, k) + ", which doesn't exist."));
                }
            }

            // ---- capsule feeding ----
            FeedingData feed = new FeedingData(d, hdr);
            if (feed.Load() == null)
            {
                for (int g = 0; g < FeedingData.Brackets; g++)
                {
                    int bad = 0;
                    foreach (int id in feed.Wanted[g])
                    {
                        if (id <= 0 || id >= ItemData.Count) continue;
                        bool fruit = id == feed.MagicFruit;
                        for (int k = 0; k < 7; k++) if (id == feed.GetFruit(k)) fruit = true;
                        int price = items.GetPrice(id), bracket = -1;
                        if (price >= 200)
                        {
                            bracket = FeedingData.Brackets - 1;
                            for (int h = 0; h < FeedingData.Brackets; h++) if (feed.GetPriceLimit(h) >= price) { bracket = h; break; }
                        }
                        if (fruit || bracket < g) bad++;
                    }
                    if (bad > 0)
                        list.Add(new Issue("Capsule feeding", "Request list " + g + ": " + bad + " of " + feed.Wanted[g].Count +
                            " requests can never be accepted (see the Capsule Feeding tab)."));
                }
            }

            // ---- Ancient Cave ----
            CaveLootData cave = new CaveLootData(d, hdr);
            if (cave.Validate() == null)
            {
                if (cave.BugFixState == 0)
                    list.Add(new Issue("Ancient Cave", "The floor 10+ equipment bug is still there (only weapons and armor in red chests from about floor 10). The Cave Loot tab can fix it."));
                for (int t = 0; t < cave.Tables.Count; t++)
                {
                    if (!cave.Tables[t].Name.StartsWith("Blue", StringComparison.OrdinalIgnoreCase)) continue;
                    for (int k = 0; k < cave.Tables[t].Count; k++)
                    {
                        int id = cave.GetEntry(t, k);
                        if (id > 0 && !items.IsEquipment(id))
                            list.Add(new Issue("Ancient Cave", "Blue chest entry " + (k + 1) + " is " + Item(id) + ", which isn't equipment."));
                        if (id == 0)
                            list.Add(new Issue("Ancient Cave", "Blue chest entry " + (k + 1) + " is empty."));
                    }
                }
            }

            // ---- treasure chests ----
            ChestData chests = new ChestData(d, hdr);
            if (chests.Load() == null)
            {
                foreach (int m in chests.MapsWithChests())
                    foreach (ChestData.Chest c in chests.ForMap(m))
                    {
                        int id = chests.GetItem(c);
                        string where = "Map " + MapNames.Label(m) + " chest " + (c.Index + 1);
                        if (id == 0) list.Add(new Issue("Chests", where + " holds item 000 (\"No equip\"), which isn't a real item."));
                        else if (id >= ItemData.Count) list.Add(new Issue("Chests", where + " holds item " + id.ToString("X3") + ", which doesn't exist."));
                        else if (items.GetName(id).Trim().Length == 0) list.Add(new Issue("Chests", where + " holds item " + id.ToString("X3") + ", which has no name."));
                    }
            }

            // ---- Game Fixes tab ----
            GameFixes fixes = GameFixes.For(d, hdr);
            foreach (GameFixes.Fix f in fixes.Fixes)
                if (!f.BalanceChange && fixes.State(f) == GameFixes.STATE_OFF)
                    list.Add(new Issue("Game bugs", "Not fixed: " + f.Name + " (the Game Fixes tab can fix it)."));
            return list;
        }
    }
}
