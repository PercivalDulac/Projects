using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

using com.lufiaIIEditor.RomInfo;

namespace com.lufiaIIEditor.UI
{
    /// <summary>
    /// "What did I change?": compares the opened ROM with the clean ROM and lists every
    /// difference in plain words (old -> new), grouped by what it belongs to. Bytes that
    /// aren't part of anything the editor knows are listed at the end as raw ranges.
    /// </summary>
    class ChangeReport
    {
        private readonly byte[] cur;
        private readonly bool curHdr;
        private readonly byte[] refd;
        private readonly bool refHdr;
        private readonly Rom rom;
        private readonly bool[] covered;
        private readonly StringBuilder sb = new StringBuilder();
        private List<string> section;
        private string[] spellNames;
        private string[] enemySkillNames;
        private string[] capsuleEffectLabels;

        private ChangeReport(Rom romData)
        {
            rom = romData;
            cur = romData.RawData;
            curHdr = romData.HasHeader;
            refd = Reference.Data;
            refHdr = Reference.HasHeader;
            covered = new bool[cur.Length];
            spellNames = romData.SpellNames;
            enemySkillNames = romData.skillNames;
        }

        public static string Build(Rom romData)
        {
            if (!Reference.Available) return "No ROM to compare against.";
            ChangeReport r = new ChangeReport(romData);
            return r.Run();
        }

        // ---------------- helpers ----------------

        private int R(int curOffset) { return Reference.Shift(curOffset); }
        private bool InRef(int o, int len) { int r = R(o); return r >= 0 && r + len <= refd.Length; }
        private int CurB(int o) { return cur[o]; }
        private int RefB(int o) { return refd[R(o)]; }
        private int CurW(int o) { return cur[o] | (cur[o + 1] << 8); }
        private int RefW(int o) { int r = R(o); return refd[r] | (refd[r + 1] << 8); }

        private void Cover(int start, int end)
        {
            for (int i = Math.Max(0, start); i < end && i < covered.Length; i++) covered[i] = true;
        }

        private bool Differs(int start, int end)
        {
            if (!InRef(start, end - start)) return false;
            int r = R(start);
            for (int i = 0; i < end - start; i++) if (cur[start + i] != refd[r + i]) return true;
            return false;
        }

        private void Begin(string title) { section = new List<string>(); section.Add(title); }

        private void End()
        {
            if (section.Count > 1)
            {
                sb.AppendLine(section[0]);
                sb.AppendLine(new string('-', section[0].Length));
                for (int i = 1; i < section.Count; i++) sb.AppendLine(section[i]);
                sb.AppendLine();
            }
            section = null;
        }

        private void Add(string line) { section.Add("  " + line); }

        private void Num(string what, int o, int len)
        {
            if (!InRef(o, len)) return;
            int a = len == 1 ? RefB(o) : RefW(o), b = len == 1 ? CurB(o) : CurW(o);
            if (a != b) Add(what + ": " + a + " -> " + b);
        }

        private string Str(byte[] d, int o, int max)
        {
            StringBuilder s = new StringBuilder();
            for (int i = 0; i < max && o + i < d.Length && d[o + i] != 0; i++) s.Append((char)d[o + i]);
            return s.ToString().TrimEnd();
        }

        private void Text(string what, int o, int max)
        {
            if (!InRef(o, max)) return;
            string a = Str(refd, R(o), max), b = Str(cur, o, max);
            if (a != b) Add(what + ": \"" + a + "\" -> \"" + b + "\"");
        }

        private static string Name(string[] names, int i)
        {
            return (names != null && i >= 0 && i < names.Length && names[i] != null) ? names[i].Trim() : "#" + i;
        }

        private string DropItemName(int id)
        {
            ItemData items = new ItemData(cur, curHdr);
            return items.Validate() == null ? ItemName(items, id) : "#" + id.ToString("X3");
        }

        private string ItemName(ItemData items, int id)
        {
            return id > 0 && id < ItemData.Count ? items.GetName(id).Trim() : id == 0 ? "(none)" : "#" + id;
        }

        /// <summary>Compares the values of a script in both ROMs (same layout) and lists the changed ones.</summary>
        private void Script(string owner, int start, int end, string[] skillNames, string[] capsuleNames)
        {
            if (end <= start || !InRef(start, end - start)) return;
            Cover(start, end);
            if (!Differs(start, end)) return;
            List<ScriptValue> now = ScriptTools.GetScriptValues(cur, start, end, skillNames, capsuleNames);
            int rs = R(start), re = R(end);
            List<ScriptValue> was = ScriptTools.GetScriptValues(refd, rs, re, skillNames, capsuleNames);
            bool explained = false;
            if (now != null && was != null && now.Count == was.Count)
            {
                for (int k = 0; k < now.Count; k++)
                {
                    ScriptValue v = now[k], w = was[k];
                    if (v.Kind == ScriptValue.KIND_INFO) continue;
                    int a = ScriptTools.GetScriptValue(refd, w), b = ScriptTools.GetScriptValue(cur, v);
                    if (a == b) continue;
                    Add(owner + " - " + v.Label + ": " + Show(v, a, skillNames, capsuleNames) + " -> " + Show(v, b, skillNames, capsuleNames));
                    explained = true;
                }
                // Status commands.
                List<StatusEffect> sn = ScriptTools.FindStatusEffects(cur, start, end);
                foreach (StatusEffect e in sn)
                {
                    int p = e.Offset;
                    string a = StatusText(refd, R(p)), b = StatusText(cur, p);
                    if (a != b) { Add(owner + " - status: " + a + " -> " + b); explained = true; }
                }
                // Elemental weaknesses / resistances (same positions in both, edits are in place).
                List<EnemyElementEntry> en = EnemyElements.Find(cur, start, end), ew = EnemyElements.Find(refd, rs, re);
                if (en.Count == ew.Count)
                    for (int k = 0; k < en.Count; k++)
                    {
                        string a = EnemyElements.Describe(refd, ew[k]), b = EnemyElements.Describe(cur, en[k]);
                        if (a != b) { Add(owner + " - element: " + a + " -> " + b); explained = true; }
                    }
            }
            if (!explained) Add(owner + ": script bytes changed (" + start.ToString("X") + "-" + (end - 1).ToString("X") + ")");
        }

        private string Show(ScriptValue v, int x, string[] skillNames, string[] capsuleNames)
        {
            switch (v.Kind)
            {
                case ScriptValue.KIND_SPELL: return Name(spellNames, x);
                case ScriptValue.KIND_ELEMENT: return ScriptTools.DescribeElement(x);
                case ScriptValue.KIND_SKILL: return Name(skillNames, x - 1);
                case ScriptValue.KIND_CAPSKILL: return Name(capsuleNames, x);
            }
            if (v.Hint == ScriptValue.HINT_SKIP_CHANCE) return x + " (runs " + ((255 - x) * 100 / 255) + "%)";
            return x.ToString();
        }

        private static string StatusText(byte[] d, int p)
        {
            int op = d[p];
            if (op == 0x0C)
            {
                int reg = d[p + 1], idx = (reg - ScriptTools.FirstProtectRegister) / 2;
                string st = idx >= 0 && idx < ScriptTools.StatusNames.Length ? ScriptTools.StatusNames[idx] : "status";
                int v = d[p + 2] | (d[p + 3] << 8);
                return (v & 0x8000) != 0 ? st + " protection (computed)" : st + " protection " + v + "%";
            }
            string name = d[p + 1] < ScriptTools.StatusNames.Length ? ScriptTools.StatusNames[d[p + 1]] : "status " + d[p + 1];
            return (op == 0x27 ? "inflict " : "cure ") + name + " " + d[p + 2] + "%";
        }

        // ---------------- the report ----------------

        private string Run()
        {
            string header = "Changes compared with " +
                (Reference.IsCleanRom ? "the clean ROM (" + System.IO.Path.GetFileName(Reference.CleanPath) + ")" : "the ROM as it was when opened") +
                Environment.NewLine + Environment.NewLine;

            {
                int was = refd.Length - (refHdr ? 0x200 : 0), now = cur.Length - (curHdr ? 0x200 : 0);
                if (was != now)
                {
                    Begin("ROM SIZE");
                    Add("ROM size: " + (was / 1048576.0).ToString("0.#") + " MB -> " + (now / 1048576.0).ToString("0.#") + " MB");
                    End();
                }
            }
            try { Characters(); } catch (Exception) { }
            try { Capsules(); } catch (Exception) { }
            try { Items(); } catch (Exception) { }
            try { IPMoves(); } catch (Exception) { }
            try { Spells(); } catch (Exception) { }
            try { Enemies(); } catch (Exception) { }
            try { Shops(); } catch (Exception) { }
            try { CaveLoot(); } catch (Exception) { }
            try { Chests(); } catch (Exception) { }
            try { Feeding(); } catch (Exception) { }
            try { Party(); } catch (Exception) { }
            try { Descriptions(); } catch (Exception) { }
            try { Encounters(); } catch (Exception) { }
            try { Dialogue(); } catch (Exception) { }
            try { Fixes(); } catch (Exception) { }
            Other();

            int total = 0;
            int len = Math.Min(cur.Length, refd.Length + (curHdr ? 0x200 : 0) - (refHdr ? 0x200 : 0));
            for (int i = curHdr ? 0x200 : 0; i < len; i++) if (InRef(i, 1) && cur[i] != RefB(i)) total++;
            if (total == 0) return header + "No changes.";
            return header + total + " bytes differ." + Environment.NewLine + Environment.NewLine + sb.ToString();
        }

        private void Chests()
        {
            ChestData now = new ChestData(cur, curHdr);
            if (now.Load() != null) return;
            ItemData items = new ItemData(cur, curHdr);
            bool named = items.Validate() == null;
            Begin("TREASURE CHESTS");
            foreach (int m in now.MapsWithChests())
            {
                foreach (ChestData.Chest c in now.ForMap(m))
                {
                    Cover(c.Offset, c.Offset + 3);
                    if (!InRef(c.Offset, 3)) continue;
                    int a = ChestData.ItemIn(refd, R(c.Offset)), b = now.GetItem(c);
                    if (a != b)
                        Add("Map " + MapNames.Label(m) + " chest " + (c.Index + 1) + ": " +
                            (named ? ItemName(items, a) : "#" + a.ToString("X3")) + " -> " +
                            (named ? ItemName(items, b) : "#" + b.ToString("X3")));
                }
            }
            End();
        }

        private void Fixes()
        {
            GameFixes now = GameFixes.For(cur, curHdr), was = GameFixes.For(refd, refHdr);
            Begin("GAME FIXES");
            for (int i = 0; i < now.Fixes.Count && i < was.Fixes.Count; i++)
            {
                GameFixes.Fix f = now.Fixes[i];
                int a = was.State(was.Fixes[i]), b = now.State(f);
                if (a != b) Add(f.Name + ": " + FixState(a) + " -> " + FixState(b));
                if (f.Problem == null)
                    foreach (GameFixes.Change c in f.Changes) Cover(c.Offset, c.Offset + c.Fixed.Length);
            }
            End();
        }

        private static string FixState(int s)
        {
            return s == GameFixes.STATE_ON ? "fixed" : s == GameFixes.STATE_OFF ? "original" : "changed by something else";
        }

        private void Characters()
        {
            CharacterData c = new CharacterData(cur, curHdr);
            if (c.Validate() != null) return;
            Begin("CHARACTERS");
            string[] names = ItemData.CharacterNames;
            for (int ch = 0; ch < CharacterData.Count; ch++)
            {
                for (int s = 0; s < CharacterData.Stats; s++)
                {
                    int bo = BaseOffset(ch, s, curHdr);
                    Num(names[ch] + " base " + CharacterData.StatNames[s], bo, 1);
                    Cover(bo, bo + 2);
                    for (int row = 0; row < 13; row++)
                    {
                        int go = GrowthOffset(ch, row, curHdr) + s;
                        int from = row == 0 ? 1 : row * 8, to = Math.Min(row * 8 + 8, 99);
                        Num(names[ch] + " " + CharacterData.StatNames[s] + " growth, levels " + from + "-" + to + " (x16)", go, 1);
                    }
                }
                Cover(GrowthOffset(ch, 0, curHdr), GrowthOffset(ch, 14, curHdr));
                if (c.HasCave)
                {
                    for (int s = 0; s < CharacterData.Stats; s++)
                    {
                        int before = c.GetCaveBonus(ch, s);
                        CharacterData r = Reference.Characters;
                        if (r != null && r.HasCave && r.GetCaveBonus(ch, s) != before)
                            Add(names[ch] + " Ancient Cave bonus " + CharacterData.StatNames[s] + ": " + r.GetCaveBonus(ch, s) + " -> " + before);
                    }
                    Cover(c.CaveOffset(ch), c.CaveOffset(ch) + 27);
                }
            }
            End();
        }

        private static int GrowthOffset(int ch, int row, bool hdr) { return 0xBB82C - (hdr ? 0 : 0x200) + ch * 0x70 + row * 8; }
        private static int BaseOffset(int ch, int s, bool hdr) { return 0xBBB3C - (hdr ? 0 : 0x200) + (ch * 7 + s) * 2; }

        private void Capsules()
        {
            CapsuleMonsterData caps = new CapsuleMonsterData(cur, curHdr);
            if (caps.Validate() != null) return;
            CapsuleSkillData sk = new CapsuleSkillData(cur, curHdr, caps);
            bool skillsOk = sk.Validate() == null;
            string[] skillNames = skillsOk ? sk.AllNames() : null;
            capsuleEffectLabels = skillsOk ? sk.EffectLabels() : null;
            string[] stat = { "HP", "ATP bonus", "DFP bonus", "STR", "AGL", "INT", "GUT", "MGR" };
            string[] grow = { "HP", "STR", "AGL", "INT", "GUT", "MGR" };

            Begin("CAPSULE MONSTERS");
            for (int i = 0; i < CapsuleMonsterData.Count; i++)
            {
                int rec = caps.RecordOffset(i);
                string who = caps.GetName(i).Trim();
                Text(who + " name", rec, CapsuleMonsterData.NameLength);
                Num(who + " element", rec + CapsuleMonsterData.OFS_ELEMENT, 1);
                for (int k = 0; k < 6; k++)
                {
                    int o = rec + CapsuleMonsterData.OFS_SKILLS + k;
                    if (InRef(o, 1) && CurB(o) != RefB(o))
                        Add(who + (k < 3 ? " skill slot " + (k + 1) + " (starts with)" : " skill slot " + (k - 2) + " (can learn)") + ": " +
                            Name(skillNames, RefB(o)) + " -> " + Name(skillNames, CurB(o)));
                }
                for (int k = 0; k < stat.Length; k++) Num(who + " " + stat[k], rec + CapsuleMonsterData.OFS_HP + k, 1);
                for (int k = 0; k < grow.Length; k++) Num(who + " " + grow[k] + " growth curve row", rec + CapsuleMonsterData.OFS_GROWTH + k, 1);
                Cover(rec, rec + CapsuleSkillData.AiStart);
                if (skillsOk) Script(who + " AI", sk.AiScriptStart(i), sk.AiScriptEnd(i), null, capsuleEffectLabels);
            }
            End();

            if (!skillsOk) return;
            Begin("CAPSULE SKILLS");
            for (int n = 0; n < CapsuleSkillData.NameCount; n++)
            {
                int o = sk.NameOffset(n), cap = sk.NameCapacity(n);
                Text("Skill name " + n.ToString("X2"), o, cap);
                Cover(o, o + cap + 1);
            }
            for (int e = 0; e < CapsuleSkillData.EffectCount; e++)
            {
                int o = sk.EffectOffset(e);
                string label = Name(capsuleEffectLabels, e) + " (effect " + e.ToString("X2") + ")";
                if (InRef(o, 1) && CurB(o) != RefB(o)) Add(label + " name in battle: " + Name(skillNames, RefB(o)) + " -> " + Name(skillNames, CurB(o)));
                Cover(o, o + 2);
                int end = sk.EffectScriptEnd(e);
                if (end > 0) Script(label, sk.EffectScriptStart(e), end, null, null);
            }
            if (sk.HasLearnChance) Num("Chance to learn a skill on level-up (1 in N)", sk.LearnChanceOffset, 1);
            End();
        }

        private void Items()
        {
            ItemData items = new ItemData(cur, curHdr);
            ItemData r = Reference.Items;
            if (items.Validate() != null || r == null) return;
            Begin("ITEMS AND EQUIPMENT");
            string[] chars = ItemData.CharacterNames;
            for (int id = 1; id < ItemData.Count; id++)
            {
                string who = r.GetName(id).Trim();
                int nameOfs = NameOffset(id);
                Text(who + " name", nameOfs, ItemData.NameLength);
                Cover(nameOfs, nameOfs + ItemData.NameLength);
                int rec = items.RecordOffset(id), end = items.RecordEnd(id);
                if (!Differs(rec, end)) { Cover(rec, end); continue; }
                bool said = false;
                if (r.GetPrice(id) != items.GetPrice(id)) { Add(who + " price: " + r.GetPrice(id) + " -> " + items.GetPrice(id)); said = true; }
                int ma = r.GetEquipMask(id), mb = items.GetEquipMask(id);
                for (int c = 0; c < 7; c++)
                    if (((ma ^ mb) >> c & 1) != 0) { Add(who + ": " + chars[c] + (((mb >> c) & 1) != 0 ? " can now equip it" : " can no longer equip it")); said = true; }
                if (r.CaveCapable(id) && r.GetInCaveLists(id) != items.GetInCaveLists(id))
                { Add(who + ": " + (items.GetInCaveLists(id) ? "now found in Ancient Cave red chests" : "no longer found in Ancient Cave red chests")); said = true; }
                SortedDictionary<int, int> fa = r.GetFields(id), fb = items.GetFields(id);
                for (int b = 0; b < 7; b++)
                {
                    int a, c;
                    fa.TryGetValue(ItemData.FIELD_ATP + b, out a); fb.TryGetValue(ItemData.FIELD_ATP + b, out c);
                    if (a != c) { Add(who + " " + ItemData.BonusNames[b] + " bonus: " + a + " -> " + c); said = true; }
                }
                int ia, ib;
                fa.TryGetValue(ItemData.FIELD_IP, out ia); fb.TryGetValue(ItemData.FIELD_IP, out ib);
                if (ia != ib)
                {
                    IPData ip = Reference.IP;
                    Add(who + " IP move: " + IPName(ip, ia) + " -> " + IPName(ip, ib)); said = true;
                }
                // Effect numbers inside the item's scripts.
                bool c1, c2;
                List<ScriptParam> pa = r.GetEffectParams(id, out c1), pb = items.GetEffectParams(id, out c2);
                if (pa.Count == pb.Count)
                    for (int k = 0; k < pb.Count; k++)
                    {
                        int a = r.GetParam(pa[k]), c = items.GetParam(pb[k]);
                        if (a != c) { Add(who + " - " + pb[k].Label + ": " + a + " -> " + c); said = true; }
                    }
                foreach (int field in new int[] { ItemData.FIELD_WEAPON_SCRIPT, ItemData.FIELD_ARMOR_SCRIPT })
                {
                    List<StatusEffect> se = items.GetStatusEffects(id, field);
                    foreach (StatusEffect e in se)
                    {
                        string a = StatusText(refd, R(e.Offset)), b = StatusText(cur, e.Offset);
                        if (a != b) { Add(who + " status: " + a + " -> " + b); said = true; }
                    }
                }
                // Elements (attack element, guard, always-on halving).
                List<string> ea = EquipElements.Find(r, id).Describe(r.RawData), eb = EquipElements.Find(items, id).Describe(items.RawData);
                if (ea.Count == eb.Count)
                    for (int k = 0; k < eb.Count; k++)
                        if (ea[k] != eb[k]) { Add(who + " " + ea[k] + " -> " + eb[k]); said = true; }
                if (!said) Add(who + ": other record bytes changed (" + rec.ToString("X") + ")");
                Cover(rec, end);
            }
            End();
        }

        private int NameOffset(int id) { return 0xF49E8 - (curHdr ? 0 : 0x200) + id * ItemData.NameLength; }

        private static string IPName(IPData ip, int id)
        {
            if (id == 0) return "(none)";
            return ip != null && id >= 1 && id <= ip.Count ? ip.GetName(id) : "#" + id;
        }

        private void IPMoves()
        {
            IPData ip = new IPData(cur, curHdr);
            if (ip.Validate() != null) return;
            Begin("IP MOVES");
            for (int id = 1; id <= ip.Count; id++)
            {
                int rec = ip.RecordOffset(id);
                string who = IPName(Reference.IP, id);
                Text(who + " name", rec + IPData.OFS_NAME, 20);
                Num(who + " IP cost", rec + IPData.OFS_COST, 1);
                Num(who + " effect number", rec + IPData.OFS_EFFECT, 2);
                Num(who + " animation", rec + IPData.OFS_ANIM, 1);
                Num(who + " type", rec + IPData.OFS_TYPE, 1);
                Num(who + " targeting", rec + IPData.OFS_TARGET, 1);
                int end = id < ip.Count ? ip.RecordOffset(id + 1) : rec + IPData.OFS_NAME + ip.GetName(id).Length + 1;
                Cover(rec, end);
            }
            for (int e = 0; e < ip.EffectCount; e++)
            {
                int s, en;
                if (!ip.GetEffectRange(e, out s, out en)) continue;
                Cover(s - 1, s);
                Script("IP effect " + e, s, en, null, null);
            }
            End();
        }

        private void Spells()
        {
            if (!curHdr) return;   // the original editor's offset files assume a headered ROM
            Begin("SPELLS");
            int n = spellNames.Length;
            for (int i = 0; i < n; i++)
            {
                int rec = int.Parse(rom.getSpellOffset(i), NumberStyles.HexNumber);
                int next = i + 1 < n ? int.Parse(rom.getSpellOffset(i + 1), NumberStyles.HexNumber) : 0xB0115;
                string who = Name(spellNames, i);
                Text(who + " name", rec, 8);
                Num(who + " MP cost", rec + 0xC, 2);
                Num(who + " gold cost", rec + 0xF, 2);
                if (InRef(rec + 0xA, 1) && ((CurB(rec + 0xA) ^ RefB(rec + 0xA)) & 0x7F) != 0)
                    for (int c = 0; c < 7; c++)
                        if ((((CurB(rec + 0xA) ^ RefB(rec + 0xA)) >> c) & 1) != 0)
                            Add(who + ": " + ItemData.CharacterNames[c] + (((CurB(rec + 0xA) >> c) & 1) != 0 ? " can now learn it" : " can no longer learn it"));
                Cover(rec, rec + 0x13);
                int rel = cur[rec + 0x11] | (cur[rec + 0x12] << 8);
                if (rel >= 0x13 && rec + rel < next) Script(who, rec + rel, next, null, null);
                else Cover(rec, next);
            }
            End();
        }

        private void Enemies()
        {
            if (!curHdr) return;
            if (new EnemyData(cur, curHdr).Validate() != null) return;   // moved by another patch
            Begin("ENEMIES");
            const int count = 224;
            string[,] fields = {
                { "level", "D", "1" }, { "sprite", "F", "1" }, { "pattern", "10", "1" }, { "max HP", "11", "2" },
                { "max MP", "13", "2" }, { "ATP", "15", "2" }, { "DFP", "17", "2" }, { "AGL", "19", "1" },
                { "INT", "1A", "1" }, { "GUT", "1B", "1" }, { "MGR", "1C", "1" }, { "EXP", "1D", "2" }, { "gold", "1F", "2" } };
            for (int i = 0; i < count; i++)
            {
                int rec = int.Parse(rom.getEnemyOffset(i), NumberStyles.HexNumber);
                int next = i + 1 < count ? int.Parse(rom.getEnemyOffset(i + 1), NumberStyles.HexNumber) : 0xB5168;
                string who = Str(refd, R(rec), 13) + " (" + i.ToString("X2") + ")";
                Text(who + " name", rec, 13);
                for (int f = 0; f < fields.GetLength(0); f++)
                    Num(who + " " + fields[f, 0], rec + int.Parse(fields[f, 1], NumberStyles.HexNumber), int.Parse(fields[f, 2]));
                Cover(rec, rec + 0x21);
                // Item drop (tag 03 at +21): item | chance << 9 (see EnemyData).
                int drop = -1;
                for (int t = rec + 0x21; t + 2 < next && cur[t] != 0; t += 3)
                    if (cur[t] == 0x03) { drop = t + 1; break; }
                if (drop >= 0 && InRef(drop - 1, 3) && refd[R(drop - 1)] == 0x03)
                {
                    int wa = refd[R(drop)] | (refd[R(drop) + 1] << 8), wb = cur[drop] | (cur[drop + 1] << 8);
                    if ((wa & 0x1FF) != (wb & 0x1FF)) Add(who + " drop: " + DropItemName(wa & 0x1FF) + " -> " + DropItemName(wb & 0x1FF));
                    if ((wa >> 9) != (wb >> 9)) Add(who + " drop chance: " + (wa >> 9) + " -> " + (wb >> 9) + " (real " +
                        EnemyData.RealDropPercent(wb >> 9).ToString("0.##") + "%)");
                    if (!Differs(rec + 0x21, drop) && !Differs(drop + 2, next)) { Cover(rec + 0x21, next); continue; }
                }
                Script(who + " AI", rec + 0x21, next, enemySkillNames, null);
            }
            End();

            Begin("ENEMY SKILL NAMES");
            if (enemySkillNames != null && rom.skillNamesLength != null)
                for (int k = 0; k < enemySkillNames.Length; k++)
                {
                    int o = int.Parse(rom.getSkillOffset(k), NumberStyles.HexNumber);
                    int len = rom.skillNamesLength[k];
                    Text("Skill " + (k + 1).ToString("X2"), o, len);
                    Cover(o - 1, o + len + 1);
                }
            End();
        }

        private void Shops()
        {
            ShopData now = new ShopData(cur, curHdr);
            ShopData was = Reference.Shops;
            if (now.Load() != null || was == null) return;
            ItemData items = new ItemData(cur, curHdr);
            Begin("SHOPS");
            foreach (ShopData.Shop s in now.Shops)
            {
                ShopData.Shop o = was.Find(s.Id);
                if (o == null) continue;
                string who = s.Id.ToString("X2") + " " + ShopData.LocationOf(s.Id);
                for (int b = 1; b < ShopData.ListCount; b++)
                {
                    if (s.Lists[b] == null || o.Lists[b] == null) continue;
                    if (string.Join(",", s.Lists[b]) == string.Join(",", o.Lists[b])) continue;
                    List<string> added = new List<string>(), removed = new List<string>();
                    foreach (int x in s.Lists[b]) if (!o.Lists[b].Contains(x)) added.Add(Entry(items, b, x));
                    foreach (int x in o.Lists[b]) if (!s.Lists[b].Contains(x)) removed.Add(Entry(items, b, x));
                    string line = who + " " + ShopData.ListNames[b].ToLower() + ":";
                    if (added.Count > 0) line += " added " + string.Join(", ", added.ToArray()) + ";";
                    if (removed.Count > 0) line += " removed " + string.Join(", ", removed.ToArray()) + ";";
                    if (added.Count == 0 && removed.Count == 0) line += " reordered";
                    Add(line.TrimEnd(';'));
                }
            }
            Cover(now.TableOffset, now.TableOffset + ShopData.TableSize * 2 + now.Capacity);
            End();
        }

        private string Entry(ItemData items, int bit, int id)
        {
            return bit == 5 ? Name(spellNames, id) : ItemName(items, id);
        }

        private void CaveLoot()
        {
            CaveLootData now = new CaveLootData(cur, curHdr);
            if (now.Validate() != null || !Reference.Available) return;
            CaveLootData was = new CaveLootData(refd, refHdr);
            if (was.Validate() != null) return;
            ItemData items = new ItemData(cur, curHdr);
            Begin("ANCIENT CAVE LOOT");
            int[] a = was.GetShares(), b = now.GetShares();
            for (int i = 0; i < CaveLootData.Sources; i++)
                if (a[i] != b[i]) Add("Chest odds, " + CaveLootData.SourceNames[i] + ": " + a[i] + "/256 -> " + b[i] + "/256");
            if (was.IrisChance != now.IrisChance) Add("Iris chance per floor: " + was.IrisChance + "/256 -> " + now.IrisChance + "/256");
            if (was.ProvidenceItem != now.ProvidenceItem) Add("Providence item: " + ItemName(items, was.ProvidenceItem) + " -> " + ItemName(items, now.ProvidenceItem));
            if (was.ProvidenceFloor != now.ProvidenceFloor) Add("Providence from floor: " + was.ProvidenceFloor + " -> " + now.ProvidenceFloor);
            if (was.PriceStep != now.PriceStep) Add("Equipment price limit per floor: " + was.PriceStep + " -> " + now.PriceStep);
            if (was.NoLimitFloor != now.NoLimitFloor) Add("No price limit from floor: " + was.NoLimitFloor + " -> " + now.NoLimitFloor);
            if (was.BugFixState != now.BugFixState)
                Add("Floor 10+ equipment bug: " + (now.BugFixState == 1 ? "fixed" : now.BugFixState == 0 ? "original (not fixed)" : "code changed by another tool"));
            for (int t = 0; t < now.Tables.Count; t++)
            {
                for (int k = 0; k < now.Tables[t].Count; k++)
                {
                    int x = was.GetEntry(t, k), y = now.GetEntry(t, k);
                    if (x != y) Add(now.Tables[t].Name + " entry " + (k + 1) + ": " + ItemName(items, x) + " -> " + ItemName(items, y));
                }
                Cover(now.Tables[t].Offset, now.Tables[t].Offset + now.Tables[t].Count * 2);
            }
            // The fix and the odds live in the cave code; mark those bytes as explained.
            int f = 0x200 + 0x03 * 0x8000 - (curHdr ? 0 : 0x200);
            Cover(f + (0x9040 - 0x8000), f + (0x91DE - 0x8000));
            Cover(f + (0x9E1B - 0x8000), f + (0x9E31 - 0x8000));
            End();
        }

        private void Dialogue()
        {
            DialogueData dd = new DialogueData(cur, curHdr);
            if (dd.Load() != null) return;
            Begin("DIALOGUE");
            for (int i = 0; i < dd.Lines.Count; i++)
            {
                DialogueData.Line l = dd.Lines[i];
                string a = dd.GetReferenceText(l), b = dd.GetText(l);
                if (a != null && b != null && a != b)
                    Add("Line #" + (i + 1).ToString("D4") + " (" + dd.Where(l) + "): \"" + a.Replace("\n", " / ") + "\" -> \"" + b.Replace("\n", " / ") + "\"");
                Cover(l.Start, l.Term);
            }
            End();
        }

        private void Encounters()
        {
            EncounterData now = new EncounterData(cur, curHdr), was = new EncounterData(refd, refHdr);
            if (now.Validate() != null || was.Validate() != null) return;
            EnemyData en = new EnemyData(cur, curHdr);
            bool enOk = en.Validate() == null;
            Func<int, string> E = delegate(int id)
            {
                if (id == 0xFF) return "-";
                return enOk && id < EnemyData.Count ? en.GetName(id) : "#" + id.ToString("X2");
            };
            Begin("ENCOUNTERS");
            for (int g = 0; g < EncounterData.GroupCount; g++)
            {
                List<string> a = new List<string>(), b = new List<string>();
                bool diff = false;
                for (int k = 0; k < EncounterData.GroupSize; k++)
                {
                    int x = was.GetGroupSlot(g, k), y = now.GetGroupSlot(g, k);
                    if (x != y) diff = true;
                    a.Add(E(x)); b.Add(E(y));
                }
                if (diff) Add("Group " + g + ": " + string.Join(", ", a.ToArray()) + " -> " + string.Join(", ", b.ToArray()));
                Cover(now.GroupOffset(g), now.GroupOffset(g) + EncounterData.GroupSize);
            }
            for (int z = 1; z < EncounterData.ZoneCount; z++)
                for (int k = 0; k < EncounterData.ZoneSlots; k++)
                {
                    int ga = was.GetZoneGroup(z, k), gb = now.GetZoneGroup(z, k), wa = was.GetZoneWeight(z, k), wb = now.GetZoneWeight(z, k);
                    if (ga != gb || wa != wb) Add("World zone " + z + " slot " + (k + 1) + ": group " + ga + " weight " + wa + " -> group " + gb + " weight " + wb);
                    Cover(now.ZoneOffset(z, k), now.ZoneOffset(z, k) + 2);
                }
            for (int i = 0; i < EncounterData.SeaCount; i++)
            {
                if (was.GetSea(i) != now.GetSea(i)) Add("Sea entry " + (i + 1) + ": group " + was.GetSea(i) + " -> " + now.GetSea(i));
                Cover(now.Sea + i, now.Sea + i + 1);
            }
            for (int bt = 0; bt < now.BattleCount && bt < was.BattleCount; bt++)
            {
                for (int k = 0; k < 6; k++)
                {
                    int x = was.GetBattleEnemy(bt, k), y = now.GetBattleEnemy(bt, k);
                    if (x != y) Add("Battle " + bt + " enemy " + (k + 1) + ": " + E(x) + " -> " + E(y));
                    int xa = was.GetBattlePos(bt, k, 0), xb = now.GetBattlePos(bt, k, 0), ya = was.GetBattlePos(bt, k, 1), yb = now.GetBattlePos(bt, k, 1);
                    if (xa != xb || ya != yb) Add("Battle " + bt + " enemy " + (k + 1) + " position: " + xa + "," + ya + " -> " + xb + "," + yb);
                }
                Cover(now.BattleOffset(bt), now.BattleOffset(bt) + 18);
            }
            for (int m = 0; m < EncounterData.MapCount; m++)
            {
                if (now.MapListLength(m) != was.MapListLength(m)) continue;
                bool firstOfList = true;
                for (int o = 0; o < m; o++) if (now.MapListStart(o) == now.MapListStart(m)) firstOfList = false;
                if (!firstOfList) continue;
                for (int k = 0; k < now.MapListLength(m); k++)
                    if (was.GetMapEntry(m, k) != now.GetMapEntry(m, k))
                        Add("Map " + MapNames.Label(m) + " monster " + k + ": group " + was.GetMapEntry(m, k) + " -> " + now.GetMapEntry(m, k));
                Cover(now.MapListStart(m), now.MapListStart(m) + now.MapListLength(m));
            }
            if (now.HasRate && was.HasRate)
            {
                if (now.SafeSteps != was.SafeSteps) Add("World map safe steps: " + was.SafeSteps + " -> " + now.SafeSteps);
                if (now.Ramp != was.Ramp) Add("World map encounter ramp: " + was.Ramp + " -> " + now.Ramp);
                if (now.Threshold != was.Threshold) Add("World map encounter threshold: " + was.Threshold + " -> " + now.Threshold);
                if (now.SeaChance != was.SeaChance) Add("Sea fight chance: " + was.SeaChance + "% -> " + now.SeaChance + "%");
                foreach (int o in new int[] { now.SafeStepsOffset, now.RampOffset, now.ThresholdOffset, now.SeaChanceOffset }) Cover(o, o + 1);
            }
            for (int i = 0; i < EncounterData.CaveEntries; i++)
            {
                if (was.GetCave(i) != now.GetCave(i))
                {
                    int lo, hi;
                    EncounterData.FloorsOfEntry(i, out lo, out hi);
                    Add("Ancient Cave entry " + (i + 1) + " (floors " + lo + "-" + hi + "): group " + was.GetCave(i) + " -> " + now.GetCave(i));
                }
                Cover(now.Cave + i, now.Cave + i + 1);
            }
            for (int g = 0; g < EncounterData.GroupCount; g++)
            {
                if (was.GetCaveSprite(g) != now.GetCaveSprite(g))
                    Add("Group " + g + " cave map sprite: " + was.GetCaveSprite(g) + " -> " + now.GetCaveSprite(g));
                Cover(now.CaveSprites + g, now.CaveSprites + g + 1);
            }
            End();
        }

        private void Descriptions()
        {
            DescriptionData now = new DescriptionData(cur, curHdr), was = new DescriptionData(refd, refHdr);
            if (now.Load() != null || was.Load() != null) return;
            Begin("DESCRIPTIONS");
            if (now.IsMoved != was.IsMoved)
                Add(now.IsMoved ? "ROM expanded to 3 MB; descriptions moved to the new space ($" + now.Bank.ToString("X2") + ":8000)."
                                : "Descriptions moved back into the original space.");
            foreach (int o in now.BankInstructionOffsets()) Cover(o, o + 4);
            ItemData items = new ItemData(cur, curHdr);
            bool itemsOk = items.Validate() == null;
            IPData ip = new IPData(cur, curHdr);
            bool ipOk = ip.Validate() == null;
            for (int k = 0; k < DescriptionData.Count; k++)
            {
                string a = was.GetText(k), b = now.GetText(k);
                if (a == b) continue;
                string who;
                if (k < DescriptionData.SpellBase) who = itemsOk && k < ItemData.Count ? ItemName(items, k) : "Item " + k.ToString("X3");
                else if (k < DescriptionData.IPBase) who = "Spell " + Name(spellNames, k - DescriptionData.SpellBase);
                else who = "IP " + (ipOk ? IPName(ip, k - DescriptionData.IPBase) : (k - DescriptionData.IPBase).ToString());
                Add(who + ": \"" + a.Replace("\n", " / ") + "\" -> \"" + b.Replace("\n", " / ") + "\"");
            }
            End();
            Cover(now.TableOffset, now.BlockEnd);
        }

        private void Party()
        {
            PartyData now = new PartyData(cur, curHdr), was = new PartyData(refd, refHdr);
            if (now.Load() != null || was.Load() != null) return;
            ItemData items = new ItemData(cur, curHdr);
            CharacterData cn = new CharacterData(cur, curHdr), cw = new CharacterData(refd, refHdr);
            Begin("STARTING PARTY AND EXPERIENCE");
            for (int c = 0; c < PartyData.Characters; c++)
            {
                PartyData.Member a = was.Members[c], b = now.Members[c];
                string who = was.NameOf(c);
                if (a.Level != b.Level) Add(who + " joins at level: " + a.Level + " -> " + b.Level);
                if (a.Exp != b.Exp) Add(who + " starting EXP: " + a.Exp + " -> " + b.Exp);
                for (int k = 0; k < 7; k++)
                    if ((a.Equip[k] & 0x1FF) != (b.Equip[k] & 0x1FF))
                        Add(who + " starting " + PartyData.SlotNames[k].ToLower() + ": " + ItemName(items, a.Equip[k] & 0x1FF) + " -> " + ItemName(items, b.Equip[k] & 0x1FF));
                foreach (int s in b.Spells) if (!a.Spells.Contains(s)) Add(who + " now starts knowing " + Name(spellNames, s));
                foreach (int s in a.Spells) if (!b.Spells.Contains(s)) Add(who + " no longer starts knowing " + Name(spellNames, s));
                if (cn.GetExpBase(c) != cw.GetExpBase(c)) Add(who + " EXP curve start value: " + cw.GetExpBase(c) + " -> " + cn.GetExpBase(c));
                for (int r = 0; r < 13; r++)
                    if (cn.GetExpRate(c, r) != cw.GetExpRate(c, r))
                        Add(who + " EXP growth, levels " + (r == 0 ? 1 : r * 8) + "-" + Math.Min(r * 8 + 7, 98) + ": " + cw.GetExpRate(c, r) + " -> " + cn.GetExpRate(c, r));
                if (cn.HasCave && cw.HasCave && cn.GetCaveLevel(c) != cw.GetCaveLevel(c))
                    Add(who + " Ancient Cave start level: " + cw.GetCaveLevel(c) + " -> " + cn.GetCaveLevel(c));
                Cover(cn.ExpBaseFileOffset(c), cn.ExpBaseFileOffset(c) + 2);
            }
            if (string.Join(",", was.Inventory) != string.Join(",", now.Inventory))
            {
                List<string> l = new List<string>();
                foreach (int w in now.Inventory) l.Add(ItemName(items, w & 0x1FF) + " x" + (w >> 9));
                Add("Starting inventory is now: " + (l.Count == 0 ? "(nothing)" : string.Join(", ", l.ToArray())));
            }
            Cover(now.StartOffset, now.EndOffset);
            End();
        }

        private void Feeding()
        {
            FeedingData now = new FeedingData(cur, curHdr), was = new FeedingData(refd, refHdr);
            if (now.Load() != null || was.Load() != null) return;
            ItemData items = new ItemData(cur, curHdr);
            Begin("CAPSULE FEEDING");
            for (int g = 0; g < FeedingData.Brackets; g++)
            {
                if (was.GetPriceLimit(g) != now.GetPriceLimit(g)) Add("Price bracket " + (g + 1) + " limit: " + was.GetPriceLimit(g) + " -> " + now.GetPriceLimit(g));
                List<int> a = was.Wanted[g], b = now.Wanted[g];
                if (string.Join(",", a) == string.Join(",", b)) continue;
                List<string> added = new List<string>(), removed = new List<string>();
                foreach (int x in b) if (!a.Contains(x)) added.Add(ItemName(items, x) + " (" + x.ToString("X3") + ")");
                foreach (int x in a) if (!b.Contains(x)) removed.Add(ItemName(items, x) + " (" + x.ToString("X3") + ")");
                string line = "Wanted list " + (g + 1) + ":";
                if (added.Count > 0) line += " added " + string.Join(", ", added.ToArray()) + ";";
                if (removed.Count > 0) line += " removed " + string.Join(", ", removed.ToArray()) + ";";
                if (added.Count == 0 && removed.Count == 0) line += " reordered";
                Add(line.TrimEnd(';'));
            }
            for (int f = 0; f < FeedingData.Forms; f++)
                for (int c = 0; c < FeedingData.Groups; c++)
                    if (was.GetGain(f, c) != now.GetGain(f, c))
                        Add("Growth per feeding, form " + (f + 1) + ", brackets " + (c * 4 + 1) + "-" + (c * 4 + 4) + ": " + was.GetGain(f, c) + " -> " + now.GetGain(f, c));
            for (int k = 0; k < 7; k++)
                if (was.GetFruit(k) != now.GetFruit(k)) Add(FeedingData.Families[k] + " fruit: " + ItemName(items, was.GetFruit(k)) + " -> " + ItemName(items, now.GetFruit(k)));
            if (was.MagicFruit != now.MagicFruit) Add("Magic fruit: " + ItemName(items, was.MagicFruit) + " -> " + ItemName(items, now.MagicFruit));
            Cover(now.ListsOffset, now.ListsOffset + FeedingData.ListArea);
            Cover(now.PriceOffset(0), now.PriceOffset(0) + 32);
            Cover(now.GainOffset(0, 0), now.GainOffset(0, 0) + 16);
            Cover(now.FruitOffset(0), now.FruitOffset(0) + 14);
            Cover(now.MagicFruitOffset, now.MagicFruitOffset + 2);
            End();
        }

        /// <summary>Anything that differs but isn't part of what the editor knows about.</summary>
        private void Other()
        {
            Begin("OTHER CHANGES (raw bytes)");
            int start = curHdr ? 0x200 : 0;
            int i = start;
            while (i < cur.Length)
            {
                if (covered[i] || !InRef(i, 1) || cur[i] == RefB(i)) { i++; continue; }
                int s = i, e = i + 1, gap = 0;
                for (int j = i + 1; j < cur.Length; j++)
                {
                    bool d = !covered[j] && InRef(j, 1) && cur[j] != RefB(j);
                    if (d) { e = j + 1; gap = 0; } else if (++gap > 16) break;
                }
                int o = s - (curHdr ? 0x200 : 0);
                string snes = "$" + (0x80 + o / 0x8000).ToString("X2") + ":" + (0x8000 + o % 0x8000).ToString("X4");
                Add("File offset " + s.ToString("X") + "-" + (e - 1).ToString("X") + " (" + snes + "), " + (e - s) + " bytes");
                i = e;
            }
            End();
        }
    }
}
