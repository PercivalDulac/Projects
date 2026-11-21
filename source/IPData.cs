using System;
using System.Collections.Generic;
using System.Text;

namespace com.lufiaIIEditor.RomInfo
{
    /// <summary>
    /// IP moves (168). Pointer table at $84:8F10 (file 0x21110 headered), absolute 16-bit
    /// addresses in bank $84, indexed by (IP ID - 1). Items reference IP IDs (1-based) in mask field 15.
    ///
    /// Record:
    ///   +0..+1  effect ID
    ///   +2..+3  action / animation ID (meaning not fully confirmed)
    ///   +4      targeting flags (raw)
    ///   +5      IP cost on the 0-255 gauge ($81:C217 compares the gauge against it)
    ///   +6..    name, ASCII, ended by 00 (battle menu copy at $81:C1D6 shows at most 13 letters)
    ///
    /// Names are packed back to back, so a name can never grow past the space it has.
    /// </summary>
    class IPData
    {
        public const int MaxCount = 255;
        public const int MaxNameLetters = 20; // longest name we accept when scanning; real limit is the space each name has

        private const int PointerTableHeadered = 0x21110;
        private const int BankFileStartHeadered = 0x20200; // file offset of $84:8000

        public const int OFS_EFFECT = 0;   // 16-bit: index into the IP effect script table
        public const int OFS_ANIM = 2;     // byte copied to $1262 when the move is used (attack type / animation)
        public const int OFS_TYPE = 3;     // byte read by the menu with +4
        public const int OFS_TARGET = 4;   // targeting flags
        public const int OFS_COST = 5;
        public const int OFS_NAME = 6;

        // IP effect scripts: relative pointer table at $95:F1EF (file 0xAF3EF headered),
        // looked up by $81:F46B. Each script starts with an FF byte; the game runs it from +1.
        private const int EffectTableHeadered = 0xAF3EF;

        private readonly byte[] data;
        private readonly int pointerTable;
        private readonly int bankStart;
        private readonly int effectTable;
        private int count;
        private int effectCount;
        private int[] capacity;  // letters available for each name

        public int Count { get { return count; } }
        public int EffectCount { get { return effectCount; } }
        public byte[] RawData { get { return data; } }

        public IPData(byte[] romData, bool hasHeader)
        {
            data = romData;
            int adjust = hasHeader ? 0 : -0x200;
            pointerTable = PointerTableHeadered + adjust;
            bankStart = BankFileStartHeadered + adjust;
            effectTable = EffectTableHeadered + adjust;
            Scan();
            ScanEffects();
        }

        private int EffectPointer(int effect)
        {
            int p = effectTable + effect * 2;
            return effectTable + (data[p] | (data[p + 1] << 8));
        }

        private void ScanEffects()
        {
            effectCount = 0;
            if (effectTable + 2 > data.Length) return;
            int n = (data[effectTable] | (data[effectTable + 1] << 8)) / 2; // first script starts right after the table
            if (n <= 0 || n > 1024) return;
            for (int k = 0; k < n; k++)
            {
                int o = EffectPointer(k);
                if (o >= data.Length || data[o] != 0xFF) return;
                if (k > 0 && o < EffectPointer(k - 1)) return;
            }
            effectCount = n;
        }

        /// <summary>Range of an effect script (after its FF byte). False if it can't be decoded exactly.</summary>
        public bool GetEffectRange(int effect, out int start, out int end)
        {
            start = 0; end = 0;
            if (effect < 0 || effect >= effectCount) return false;
            start = EffectPointer(effect) + 1;
            if (effect + 1 < effectCount)
            {
                end = EffectPointer(effect + 1);
            }
            else
            {
                // Last script: run to its first End command.
                int p = start;
                while (p < data.Length)
                {
                    int n = ScriptTools.InstructionSize(data, p);
                    if (n <= 0) return false;
                    bool isEnd = data[p] == 0x00;
                    p += n;
                    if (isEnd) break;
                }
                end = p;
            }
            return end > start && ScriptTools.Walk(data, start, end) != null;
        }

        private bool LooksLikeRecord(int o)
        {
            if (o < bankStart || o + OFS_NAME + 2 > data.Length) return false;
            int len = 0;
            while (len <= MaxNameLetters && o + OFS_NAME + len < data.Length && data[o + OFS_NAME + len] != 0)
            {
                byte c = data[o + OFS_NAME + len];
                if (c < 0x20 || c > 0x7E) return false;
                len++;
            }
            return len >= 1 && len <= MaxNameLetters;
        }

        private void Scan()
        {
            count = 0;
            while (count < MaxCount && pointerTable + count * 2 + 1 < data.Length)
            {
                int o = RecordOffset(count + 1);
                if (!LooksLikeRecord(o)) break;
                if (count > 0 && o <= RecordOffset(count)) break; // records are stored in order
                count++;
            }

            capacity = new int[count + 1];
            for (int id = 1; id <= count; id++)
            {
                int nameStart = RecordOffset(id) + OFS_NAME;
                int cap;
                if (id < count)
                {
                    // Everything up to the next record is this name's space (minus the end marker).
                    cap = RecordOffset(id + 1) - nameStart - 1;
                }
                else
                {
                    cap = 0;
                    while (data[nameStart + cap] != 0) cap++;
                }
                if (cap > MaxNameLetters) cap = MaxNameLetters;
                if (cap < 0) cap = 0;
                capacity[id] = cap;
            }
        }

        public string Validate()
        {
            if (count < 100) return "IP move table not found (only " + count + " entries looked valid).";
            return null;
        }

        /// <summary>File offset of the record for a 1-based IP ID.</summary>
        public int RecordOffset(int id)
        {
            int p = pointerTable + (id - 1) * 2;
            int addr = data[p] | (data[p + 1] << 8);
            return bankStart + (addr - 0x8000);
        }

        public int NameCapacity(int id) { return capacity[id]; }

        public string GetName(int id)
        {
            int o = RecordOffset(id) + OFS_NAME;
            StringBuilder sb = new StringBuilder();
            for (int k = 0; k < MaxNameLetters && data[o + k] != 0; k++) sb.Append((char)data[o + k]);
            return sb.ToString();
        }

        /// <summary>Writes the name, cut to the available space; unused space is filled with 00.</summary>
        public void SetName(int id, string name)
        {
            if (name == null) name = "";
            name = name.TrimEnd(' ');
            int cap = capacity[id];
            if (name.Length > cap) name = name.Substring(0, cap);
            if (name.Length == 0) name = "-";
            int o = RecordOffset(id) + OFS_NAME;
            for (int k = 0; k <= cap; k++)
            {
                char c = k < name.Length ? name[k] : '\0';
                if (c != '\0' && (c < 0x20 || c > 0x7E)) c = ' ';
                data[o + k] = (byte)c;
            }
        }

        public int GetByte(int id, int ofs) { return data[RecordOffset(id) + ofs]; }
        public void SetByte(int id, int ofs, int v) { data[RecordOffset(id) + ofs] = (byte)(v & 0xFF); }

        public int GetWord(int id, int ofs)
        {
            int o = RecordOffset(id) + ofs;
            return data[o] | (data[o + 1] << 8);
        }

        public void SetWord(int id, int ofs, int v)
        {
            int o = RecordOffset(id) + ofs;
            data[o] = (byte)(v & 0xFF);
            data[o + 1] = (byte)((v >> 8) & 0xFF);
        }
    }
}
