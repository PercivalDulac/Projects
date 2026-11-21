using System;
using System.Collections.Generic;
using System.Text;


namespace com.lufiaIIEditor.RomInfo
{
    /// <summary>
    /// Capsule monster skills and AI.
    ///
    ///   Skill names: table $A5:DF00 (file 0x12E100 headered), 78 relative pointers to
    ///     00-terminated names (0 = "Nothing"). A capsule record's six bytes at +0F are name
    ///     numbers - the skills listed in the capsule menu.
    ///   Skill effects: table $97:F63B (file 0xBF83B), 84 relative pointers ($81:F476). Record:
    ///     +0 name number shown in battle, +1 animation, +2 effect script (same language as
    ///     items, spells and IP moves).
    ///   AI: each capsule record's script from +23 to the next record (the last one ends where
    ///     the shop table starts, 0xBF09F). Opcode 3E n = use skill effect n ($85:BD5E).
    /// </summary>
    class CapsuleSkillData
    {
        public const int NameCount = 78;
        public const int EffectCount = 84;
        public const int AiStart = 0x23;

        private const int NamesHeadered = 0x12E100;
        private const int EffectsHeadered = 0xBF83B;
        private const int AfterLastCapsuleHeadered = 0xBF09F;

        private readonly byte[] data;
        private readonly int names, effects, afterLast;
        private readonly CapsuleMonsterData capsules;
        private readonly int learnChance;   // operand of LDA #$08 at $82:CD1F (1-in-N per level-up)

        public CapsuleSkillData(byte[] romData, bool hasHeader, CapsuleMonsterData capsuleData)
        {
            data = romData;
            int adjust = hasHeader ? 0 : -0x200;
            names = NamesHeadered + adjust;
            effects = EffectsHeadered + adjust;
            afterLast = AfterLastCapsuleHeadered + adjust;
            capsules = capsuleData;
            learnChance = 0x200 + 0x02 * 0x8000 + (0xCD20 - 0x8000) + adjust;
        }

        // ---- learning upgrades ----
        //
        // Record +0F..+11 = the three starting skills (slots 1-3), +12..+14 = what each slot
        // can turn into. On each capsule level-up ($82:CD1F, from $81:DCBE) there is a 1 in N
        // chance; then one slot is picked at random and, if it has an upgrade not learned yet,
        // the upgrade is learned: the menu shows it instead of the starting skill (an empty
        // starting slot simply gains it). Learned flags: 3 bits per form ($11CA). The AI's
        // "3F slot" command checks the same flag to start using the upgrade.

        public bool HasLearnChance
        {
            get
            {
                int o = learnChance - 1;
                return o >= 0 && o + 10 <= data.Length && data[o] == 0xA9 && data[o + 2] == 0x22 &&
                       data[o + 3] == 0x99 && data[o + 4] == 0x82 && data[o + 5] == 0x80 && data[o + 6] == 0xC9;
            }
        }
        public int LearnChanceOffset { get { return learnChance; } }
        public int LearnChance { get { return data[learnChance]; } set { data[learnChance] = (byte)Math.Max(1, Math.Min(255, value)); } }

        /// <summary>Does this capsule's AI check slot k's learned flag (opcode 3F k+1)?</summary>
        public bool AiChecksSlot(int capsule, int slot)
        {
            List<int> ins = ScriptTools.Walk(data, AiScriptStart(capsule), AiScriptEnd(capsule));
            if (ins == null) return false;
            foreach (int p in ins) if (data[p] == 0x3F && data[p + 1] == slot + 1) return true;
            return false;
        }

        public byte[] RawData { get { return data; } }
        public CapsuleMonsterData Capsules { get { return capsules; } }

        private int Word(int o) { return data[o] | (data[o + 1] << 8); }

        public string Validate()
        {
            if (data.Length < names + NameCount * 2 + 16) return "ROM is too small.";
            if (Word(names) != NameCount * 2) return "Capsule skill names not found where expected.";
            if (Word(effects) != EffectCount * 2) return "Capsule skill effects not found where expected.";
            for (int i = 1; i < NameCount; i++) if (Word(names + i * 2) <= Word(names + i * 2 - 2)) return "Capsule skill name table looks unusual.";
            for (int i = 1; i < EffectCount; i++) if (Word(effects + i * 2) <= Word(effects + i * 2 - 2)) return "Capsule skill effect table looks unusual.";
            return null;
        }

        // ---- names ----

        public int NameOffset(int i) { return names + Word(names + i * 2); }

        public string GetName(int i)
        {
            int o = NameOffset(i), e = o;
            while (e < data.Length && data[e] != 0) e++;
            return Encoding.ASCII.GetString(data, o, e - o);
        }

        /// <summary>Letters that fit: up to the next name (the last keeps its own length).</summary>
        public int NameCapacity(int i)
        {
            if (i + 1 < NameCount) return NameOffset(i + 1) - NameOffset(i) - 1;
            return GetName(i).Length;
        }

        public void SetName(int i, string name)
        {
            int cap = NameCapacity(i);
            if (name.Length > cap) name = name.Substring(0, cap);
            int o = NameOffset(i);
            for (int k = 0; k < cap; k++) data[o + k] = (byte)(k < name.Length ? name[k] : 0);
            data[o + cap] = 0;
        }

        public string[] AllNames()
        {
            string[] a = new string[NameCount];
            for (int i = 0; i < NameCount; i++) a[i] = GetName(i);
            return a;
        }

        // ---- effects ----

        public int EffectOffset(int i) { return effects + Word(effects + i * 2); }
        public int GetEffectName(int i) { return data[EffectOffset(i)]; }
        public void SetEffectName(int i, int nameId) { data[EffectOffset(i)] = (byte)nameId; }
        public int GetEffectAnim(int i) { return data[EffectOffset(i) + 1]; }
        public int EffectScriptStart(int i) { return EffectOffset(i) + 2; }

        public int EffectScriptEnd(int i)
        {
            if (i + 1 < EffectCount) return EffectOffset(i + 1);
            // The last effect is followed by other data: it ends after its first end command.
            int p = EffectScriptStart(i);
            for (int guard = 0; guard < 256 && p < data.Length; guard++)
            {
                int n = ScriptTools.InstructionSize(data, p);
                if (n <= 0) return -1;
                if (data[p] == 0x00) return p + 1;
                p += n;
            }
            return -1;
        }

        /// <summary>Effect labels like "Foomy punch" (from each effect's name number).</summary>
        public string[] EffectLabels()
        {
            string[] a = new string[EffectCount];
            for (int i = 0; i < EffectCount; i++)
            {
                int n = GetEffectName(i);
                a[i] = n < NameCount ? GetName(n) : "name " + n;
            }
            return a;
        }

        // ---- AI ----

        public int AiScriptStart(int capsule) { return capsules.RecordOffset(capsule) + AiStart; }

        public int AiScriptEnd(int capsule)
        {
            return capsule + 1 < CapsuleMonsterData.Count ? capsules.RecordOffset(capsule + 1) : afterLast;
        }

        /// <summary>Effects that a capsule's AI uses (opcode 3E).</summary>
        public List<int> EffectsUsedBy(int capsule)
        {
            List<int> r = new List<int>();
            List<int> ins = ScriptTools.Walk(data, AiScriptStart(capsule), AiScriptEnd(capsule));
            if (ins == null) return r;
            foreach (int p in ins) if (data[p] == 0x3E && !r.Contains(data[p + 1])) r.Add(data[p + 1]);
            return r;
        }
    }
}
