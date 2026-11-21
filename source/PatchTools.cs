using System;
using System.Collections.Generic;

namespace com.lufiaIIEditor.RomInfo
{
    /// <summary>
    /// Builds IPS and BPS patches that turn a clean ROM (source) into the edited ROM (target).
    /// BPS also stores checksums of both ROMs, so patchers can refuse the wrong base ROM.
    /// </summary>
    static class PatchTools
    {
        // ---------------- IPS ----------------

        public static byte[] MakeIps(byte[] source, byte[] target)
        {
            List<byte> o = new List<byte>();
            AddAscii(o, "PATCH");
            int n = target.Length;
            int i = 0;
            while (i < n)
            {
                if (i < source.Length && source[i] == target[i]) { i++; continue; }
                // Past the end of the source, zeros needn't be written (patchers fill the gap with
                // zeros), except the very last byte, which makes the file the right size.
                if (i >= source.Length && target[i] == 0 && i != n - 1) { i++; continue; }
                // A run of changes; small gaps (< 6 equal bytes) are cheaper to include than a new record.
                int start = i, end = i + 1, gap = 0;
                for (int j = i + 1; j < n && j - start < 0xFFFF; j++)
                {
                    bool diff = j >= source.Length ? (target[j] != 0 || j == n - 1) : source[j] != target[j];
                    if (diff) { end = j + 1; gap = 0; }
                    else if (++gap >= 6) break;
                }
                // 0x454F46 spells "EOF": a record can't start there, so start one byte earlier.
                if (start == 0x454F46) start--;
                int len = end - start;
                o.Add((byte)(start >> 16)); o.Add((byte)(start >> 8)); o.Add((byte)start);
                o.Add((byte)(len >> 8)); o.Add((byte)len);
                for (int k = start; k < end; k++) o.Add(target[k]);
                i = end;
            }
            AddAscii(o, "EOF");
            if (target.Length < source.Length)
            {
                // Truncation extension understood by Lunar IPS / Floating IPS.
                o.Add((byte)(target.Length >> 16)); o.Add((byte)(target.Length >> 8)); o.Add((byte)target.Length);
            }
            return o.ToArray();
        }

        // ---------------- BPS ----------------

        /// <summary>How many bytes from p repeat the byte at p - 1 (0 at the start).</summary>
        private static int RunAt(byte[] t, int p)
        {
            if (p <= 0 || p >= t.Length) return 0;
            byte b = t[p - 1];
            int k = 0;
            while (p + k < t.Length && t[p + k] == b) k++;
            return k;
        }

        public static byte[] MakeBps(byte[] source, byte[] target)
        {
            List<byte> o = new List<byte>();
            AddAscii(o, "BPS1");
            Number(o, (ulong)source.Length);
            Number(o, (ulong)target.Length);
            Number(o, 0);   // no metadata

            int pos = 0, n = target.Length;
            long targetRel = 0;   // TargetCopy's running offset
            while (pos < n)
            {
                int run = RunAt(target, pos);
                if (run >= 32 && !(pos < source.Length && source[pos] == target[pos]))
                {
                    // TargetCopy: repeat the byte just written (long runs, e.g. the zero-filled expansion).
                    long from = pos - 1;
                    long delta = from - targetRel;
                    Number(o, ((ulong)(run - 1) << 2) | 3);
                    Number(o, ((ulong)Math.Abs(delta) << 1) | (delta < 0 ? 1UL : 0UL));
                    targetRel = from + run;
                    pos += run;
                    continue;
                }
                if (pos < source.Length && source[pos] == target[pos])
                {
                    // SourceRead: copy matching bytes from the same place in the source.
                    int len = 0;
                    while (pos + len < n && pos + len < source.Length && source[pos + len] == target[pos + len]) len++;
                    Number(o, ((ulong)(len - 1) << 2) | 0);
                    pos += len;
                }
                else
                {
                    // TargetRead: the new bytes, until 8 matching bytes in a row (worth a SourceRead).
                    int len = 0, same = 0;
                    while (pos + len < n)
                    {
                        bool match = pos + len < source.Length && source[pos + len] == target[pos + len];
                        if (match) { if (++same >= 8) { len -= 7; break; } }
                        else same = 0;
                        if (len > 0 && same == 0 && RunAt(target, pos + len) >= 32) break;
                        len++;
                    }
                    Number(o, ((ulong)(len - 1) << 2) | 1);
                    for (int k = 0; k < len; k++) o.Add(target[pos + k]);
                    pos += len;
                }
            }
            AddUInt(o, Crc32(source, 0, source.Length));
            AddUInt(o, Crc32(target, 0, target.Length));
            byte[] soFar = o.ToArray();
            AddUInt(o, Crc32(soFar, 0, soFar.Length));
            return o.ToArray();
        }

        private static void Number(List<byte> o, ulong v)
        {
            while (true)
            {
                byte x = (byte)(v & 0x7F);
                v >>= 7;
                if (v == 0) { o.Add((byte)(0x80 | x)); break; }
                o.Add(x);
                v--;
            }
        }

        private static void AddUInt(List<byte> o, uint v)
        {
            o.Add((byte)v); o.Add((byte)(v >> 8)); o.Add((byte)(v >> 16)); o.Add((byte)(v >> 24));
        }

        private static void AddAscii(List<byte> o, string s) { foreach (char c in s) o.Add((byte)c); }

        private static uint[] crcTable;

        public static uint Crc32(byte[] data, int start, int length)
        {
            if (crcTable == null)
            {
                crcTable = new uint[256];
                for (uint i = 0; i < 256; i++)
                {
                    uint c = i;
                    for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                    crcTable[i] = c;
                }
            }
            uint crc = 0xFFFFFFFFu;
            for (int i = start; i < start + length; i++) crc = crcTable[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFFu;
        }

        /// <summary>The edited ROM laid out like the clean ROM (adds or drops the 512-byte copier header).</summary>
        public static byte[] MatchHeader(byte[] edited, bool editedHasHeader, byte[] clean, bool cleanHasHeader)
        {
            if (editedHasHeader == cleanHasHeader) return edited;
            if (editedHasHeader)
            {
                byte[] t = new byte[edited.Length - 0x200];
                Array.Copy(edited, 0x200, t, 0, t.Length);
                return t;
            }
            byte[] h = new byte[edited.Length + 0x200];
            Array.Copy(clean, 0, h, 0, 0x200);
            Array.Copy(edited, 0, h, 0x200, edited.Length);
            return h;
        }
    }
}
