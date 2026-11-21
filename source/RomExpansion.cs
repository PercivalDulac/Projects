using System;

namespace com.lufiaIIEditor.RomInfo
{
    /// <summary>
    /// Expands the ROM from 2.5 MB to 3 MB and moves the item / spell / IP descriptions into the new
    /// space ($D0:8000, file 0x280000 + header), or undoes it. The header already says 4 MB, so it
    /// isn't touched. The new space is zero-filled; patches only need to carry what's written there.
    /// </summary>
    static class RomExpansion
    {
        public const int OriginalSize = 0x280000;
        public const int ExpandedSize = 0x300000;
        public const int MaxSize = 0x400000;     // the most a LoROM cartridge can map (and what the header says)

        public static int Size(byte[] d, bool hdr) { return d.Length - (hdr ? 0x200 : 0); }

        public static bool IsExpanded(byte[] d, bool hdr)
        {
            return d.Length - (hdr ? 0x200 : 0) >= ExpandedSize;
        }

        /// <summary>
        /// Grows the ROM to target (3 or 4 MB). From 2.5 MB the descriptions are moved into the new
        /// space too; a ROM that's already expanded is just made bigger. Null with the reason in error.
        /// </summary>
        public static byte[] Expand(byte[] d, bool hdr, int target, out string error)
        {
            error = null;
            int h = hdr ? 0x200 : 0;
            int size = Size(d, hdr);
            if (target != ExpandedSize && target != MaxSize) { error = "Unsupported size."; return null; }
            if (size >= target) { error = "The ROM is already that big."; return null; }
            if (size != OriginalSize)
            {
                if (size != ExpandedSize) { error = "The ROM isn't one of the sizes this editor makes (2.5, 3 or 4 MB)."; return null; }
                byte[] grown = new byte[target + h];
                Array.Copy(d, grown, d.Length);
                return grown;
            }
            DescriptionData before = new DescriptionData(d, hdr);
            string p = before.Load();
            if (p != null) { error = p; return null; }
            if (before.IsMoved) { error = "The descriptions have already been moved."; return null; }

            byte[] n = new byte[target + h];
            Array.Copy(d, n, d.Length);
            DescriptionData moved = new DescriptionData(n, hdr);
            p = moved.Load();
            if (p != null) { error = p; return null; }
            moved.Relocate(DescriptionData.ExpandedBank);
            if (!moved.Write()) { error = "The descriptions didn't fit in the new space."; return null; }
            error = Verify(before, n, hdr);
            return error == null ? n : null;
        }

        /// <summary>Moves the descriptions back and trims the ROM to 2.5 MB, or null with the reason in error.</summary>
        public static byte[] Shrink(byte[] d, bool hdr, out string error)
        {
            error = null;
            int h = hdr ? 0x200 : 0;
            if (Size(d, hdr) != ExpandedSize && Size(d, hdr) != MaxSize) { error = "The ROM isn't an expanded (3 or 4 MB) ROM."; return null; }
            DescriptionData cur = new DescriptionData(d, hdr);
            string p = cur.Load();
            if (p != null) { error = p; return null; }
            if (!cur.IsMoved) { error = "The descriptions are already in the original space."; return null; }

            // Anything else in the expanded area (outside the description bank) would be lost.
            int bankStart = cur.TableOffset, bankEnd = cur.TableOffset + 0x8000;
            for (int o = OriginalSize + h; o < d.Length; o++)
            {
                if (o >= bankStart && o < bankEnd) continue;
                if (d[o] != 0) { error = "Other data has been put in the expanded area, so it can't be removed."; return null; }
            }

            byte[] work = (byte[])d.Clone();
            DescriptionData back = new DescriptionData(work, hdr);
            back.Load();
            back.Relocate(DescriptionData.OriginalBank);
            int over = back.Used - back.Capacity;
            if (over > 0)
            {
                error = "The descriptions are " + over + " bytes too long for the original space.\n" +
                        "Shorten some of them first (the Descriptions tab shows what each one costs).";
                return null;
            }
            if (!back.Write()) { error = "The descriptions didn't fit in the original space."; return null; }
            byte[] n = new byte[OriginalSize + h];
            Array.Copy(work, n, n.Length);
            error = Verify(cur, n, hdr);
            return error == null ? n : null;
        }

        /// <summary>Reads the descriptions back from the new ROM and compares them with the old ones.</summary>
        private static string Verify(DescriptionData was, byte[] n, bool hdr)
        {
            DescriptionData now = new DescriptionData(n, hdr);
            string p = now.Load();
            if (p != null) return "Check after moving failed: " + p;
            for (int k = 0; k < DescriptionData.Count; k++)
                if (now.GetText(k) != was.GetText(k)) return "Check after moving failed at description " + k + ".";
            return null;
        }
    }
}
