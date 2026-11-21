using System;
using System.Collections.Generic;

namespace com.lufiaIIEditor.RomInfo
{
    /// <summary>
    /// Elemental weaknesses / resistances in an enemy's script (the part that runs when it's hit).
    ///   42 nn 00                      shared script nn ($96:FADD): halves / doubles one element
    ///   0C 81 hi lo [17 81 hi lo] 42 15 00   immune to these element(s) (17 = OR in another one)
    /// Every entry is edited in place, so the enemy's data never changes size.
    /// The same shared scripts are used by armor (see EquipElements).
    /// </summary>
    class EnemyElementEntry
    {
        public const int KIND_SHARED = 0;
        public const int KIND_IMMUNE = 1;

        public int Kind;
        public int ScriptOffset = -1;                       // KIND_SHARED: the nn byte
        public List<int> ElementOffsets = new List<int>();  // KIND_IMMUNE: element bytes (hi, lo)
        public bool Conditional;                            // a condition right before decides whether it runs
    }

    static class EnemyElements
    {
        public static readonly int[] SharedScripts = {
            0x00, 0x01, 0x02, 0x03, 0x05, 0x0D, 0x0C, 0x10, 0x0F, 0x04,
            0x06, 0x07, 0x08, 0x09, 0x0A, 0x0E, 0x14, 0x0B, 0x17 };
        public static readonly string[] SharedNames = {
            "Weak: double Fire damage", "Weak: double Thunder damage", "Weak: double Water damage",
            "Weak: double Ice damage", "Weak: double Light damage", "Weak: double Shadow damage",
            "Weak: double from Anti-dragon", "Weak: double from Anti-insect", "Weak: double from Anti-hard",
            "Flying: double from Anti-flying, immune to Earth",
            "Resists: halves Fire damage", "Resists: halves Thunder damage", "Resists: halves Water damage",
            "Resists: halves Ice damage", "Resists: halves Light damage", "Resists: halves Shadow damage",
            "Resists: halves all elemental damage", "Resists: halves non-elemental damage",
            "Resists: halves Anti-hard damage" };

        public static string SharedName(int nn)
        {
            int k = Array.IndexOf(SharedScripts, nn);
            return k >= 0 ? SharedNames[k] : "shared script " + nn.ToString("X2");
        }

        public static List<EnemyElementEntry> Find(byte[] d, int start, int end)
        {
            List<EnemyElementEntry> list = new List<EnemyElementEntry>();
            List<int> ins = ScriptTools.Walk(d, start, end);
            if (ins == null) return list;
            for (int k = 0; k < ins.Count; k++)
            {
                int p = ins[k];
                if (d[p] != 0x42 || d[p + 2] != 0x00) continue;
                bool cond = k > 0 && d[ins[k - 1]] >= 0x05 && d[ins[k - 1]] <= 0x0B;
                if (Array.IndexOf(SharedScripts, (int)d[p + 1]) >= 0)
                {
                    EnemyElementEntry e = new EnemyElementEntry();
                    e.Kind = EnemyElementEntry.KIND_SHARED;
                    e.ScriptOffset = p + 1;
                    e.Conditional = cond;
                    list.Add(e);
                }
                else if (d[p + 1] == 0x15)
                {
                    // Walk back over 17 81 hi lo (OR) to the 0C 81 hi lo that starts the element set.
                    List<int> els = new List<int>();
                    int j = k - 1;
                    while (j >= 0 && d[ins[j]] == 0x17 && d[ins[j] + 1] == 0x81) { els.Insert(0, ins[j] + 2); j--; }
                    if (j < 0 || d[ins[j]] != 0x0C || d[ins[j] + 1] != 0x81) continue;
                    if ((d[ins[j] + 3] & 0x80) != 0) continue;   // a register, not an element
                    els.Insert(0, ins[j] + 2);
                    EnemyElementEntry e = new EnemyElementEntry();
                    e.Kind = EnemyElementEntry.KIND_IMMUNE;
                    e.ElementOffsets = els;
                    e.Conditional = j > 0 && d[ins[j - 1]] >= 0x05 && d[ins[j - 1]] <= 0x0B;
                    list.Add(e);
                }
            }
            return list;
        }

        public static int GetElement(byte[] d, int o) { return (d[o] << 8) | d[o + 1]; }

        public static void SetElement(byte[] d, int o, int v)
        {
            if (v < 0) return;
            d[o] = (byte)((v >> 8) & 0xFF);
            d[o + 1] = (byte)(v & 0xFF);
        }

        public static void SetShared(byte[] d, EnemyElementEntry e, int nn)
        {
            if (e.Kind == EnemyElementEntry.KIND_SHARED && Array.IndexOf(SharedScripts, nn) >= 0) d[e.ScriptOffset] = (byte)nn;
        }

        private static readonly int[] WeakScripts = { 0x00, 0x01, 0x02, 0x03, 0x05, 0x0D, 0x0C, 0x10, 0x0F };
        private static readonly string[] WeakNames = { "Fire", "Thunder", "Water", "Ice", "Light", "Shadow", "Anti-dragon", "Anti-insect", "Anti-hard" };
        private static readonly int[] HalfScripts = { 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0E, 0x14, 0x0B, 0x17 };
        private static readonly string[] HalfNames = { "Fire", "Thunder", "Water", "Ice", "Light", "Shadow", "all elements", "non-elemental", "Anti-hard" };

        /// <summary>One short line for overviews: "Weak: Light | Resists: Shadow, Ice | Immune: Fire | Flying".</summary>
        public static string Summary(byte[] d, List<EnemyElementEntry> list)
        {
            List<string> weak = new List<string>(), half = new List<string>(), immune = new List<string>();
            bool flying = false;
            foreach (EnemyElementEntry e in list)
            {
                string mark = e.Conditional ? "*" : "";
                if (e.Kind == EnemyElementEntry.KIND_IMMUNE)
                {
                    foreach (int o in e.ElementOffsets) immune.Add(ScriptTools.DescribeElement(GetElement(d, o)) + mark);
                    continue;
                }
                int nn = d[e.ScriptOffset];
                int k = Array.IndexOf(WeakScripts, nn);
                if (k >= 0) { weak.Add(WeakNames[k] + mark); continue; }
                k = Array.IndexOf(HalfScripts, nn);
                if (k >= 0) { half.Add(HalfNames[k] + mark); continue; }
                if (nn == 0x04) flying = true;
            }
            List<string> parts = new List<string>();
            if (weak.Count > 0) parts.Add("Weak: " + string.Join(", ", weak.ToArray()));
            if (half.Count > 0) parts.Add("Resists: " + string.Join(", ", half.ToArray()));
            if (immune.Count > 0) parts.Add("Immune: " + string.Join(", ", immune.ToArray()));
            if (flying) parts.Add("Flying");
            return string.Join(" | ", parts.ToArray());
        }

        public static string Describe(byte[] d, EnemyElementEntry e)
        {
            string s;
            if (e.Kind == EnemyElementEntry.KIND_SHARED) s = SharedName(d[e.ScriptOffset]);
            else
            {
                List<string> parts = new List<string>();
                foreach (int o in e.ElementOffsets) parts.Add(ScriptTools.DescribeElement(GetElement(d, o)));
                s = "Immune to " + string.Join(" + ", parts.ToArray());
            }
            return e.Conditional ? s + " (only sometimes)" : s;
        }
    }
}
