using System;
using System.Collections.Generic;
using System.Text;

namespace com.lufiaIIEditor.RomInfo
{
    /// <summary>
    /// Item / spell / IP move descriptions. Decoder: $85:C4F1.
    ///   Table $97:8000 (file 0xB8200 headered): 730 words, offsets from the table start.
    ///   Index: items 000-1D2 (item ID), spells 200 + spell, IP moves 230 + IP ID.
    ///   Text bytes: 00 end, 0A new line, 20-7F a letter, 80-FF description word (b - 80),
    ///   05-09 nn description word 80 + (b - 5) * 255 + nn, 10-1F nn general word (b - 10) * 255 + nn.
    ///   Description words: $9E:DDCC (624, offsets from the table start).
    ///   General words: $8E:EA00 (640, shared with dialogue; includes fragments like "of ").
    /// The box shows 26 letters x 4 lines and doesn't wrap by itself.
    /// The text block is repacked on save; identical texts are stored once.
    /// </summary>
    class DescriptionData
    {
        public const int Count = 730;
        public const int LineLength = 26;
        public const int MaxLines = 4;
        public const int SpellBase = 0x200, IPBase = 0x230, ItemCount = 0x1D3;

        private readonly byte[] data;
        private readonly int adjust, descWords, genWords, codeCheck;
        private int table, blockEnd, bank;

        public const int OriginalBank = 0x97, ExpandedBank = 0xD0;
        // The four LDA $97:8000,X in the decoder; the bank byte is the 4th byte of each.
        private static readonly int[] BankOperands = { 0xC508, 0xC516, 0xC53A, 0xC56F };
        private List<string> descDict = new List<string>(), genDict = new List<string>();
        private string[] texts = new string[Count];
        private HashSet<char> allowed = new HashSet<char>();

        public DescriptionData(byte[] romData, bool hasHeader)
        {
            data = romData;
            adjust = hasHeader ? 0 : -0x200;
            codeCheck = File(0x85, 0xC508) + adjust;
            bank = codeCheck + 3 < data.Length ? data[codeCheck + 3] : OriginalBank;
            SetPlace(bank);
            descWords = File(0x9E, 0xDDCC) + adjust;
            genWords = File(0x8E, 0xEA00) + adjust;
        }

        private void SetPlace(int b)
        {
            bank = b;
            table = File(b, 0x8000) + adjust;
            // Original block ends at 0xBB2A1; a moved block may use its whole bank.
            blockEnd = b == OriginalBank ? 0xBB2A1 + adjust : table + 0x8000;
        }

        /// <summary>File offsets of the four decoder instructions that hold the bank.</summary>
        public int[] BankInstructionOffsets()
        {
            int[] r = new int[BankOperands.Length];
            for (int i = 0; i < r.Length; i++) r[i] = File(0x85, BankOperands[i]) + adjust;
            return r;
        }

        public int Bank { get { return bank; } }
        public bool IsMoved { get { return bank != OriginalBank; } }

        /// <summary>
        /// Points the decoder at another bank and rewrites every description there on the next Write.
        /// The old copy is left as it was.
        /// </summary>
        public void Relocate(int newBank)
        {
            foreach (int a in BankOperands)
            {
                int o = File(0x85, a) + adjust;
                data[o + 3] = (byte)newBank;
            }
            SetPlace(newBank);
            for (int k = 0; k < Count; k++) written[k] = null;   // force a full write
        }

        private static int File(int bank, int addr) { return 0x200 + (bank & 0x7F) * 0x8000 + (addr - 0x8000); }
        private int Word(int o) { return data[o] | (data[o + 1] << 8); }

        public int Capacity { get { return blockEnd - (table + Count * 2); } }
        public int TableOffset { get { return table; } }
        public int BlockEnd { get { return blockEnd; } }
        public List<string> DescriptionWords { get { return descDict; } }
        public List<string> GeneralWords { get { return genDict; } }

        private static List<string> ReadDict(byte[] d, int t, int count)
        {
            List<string> l = new List<string>();
            for (int k = 0; k < count; k++)
            {
                int s = t + (d[t + 2 * k] | (d[t + 2 * k + 1] << 8));
                StringBuilder sb = new StringBuilder();
                while (s < d.Length && d[s] != 0 && sb.Length < 40) sb.Append((char)d[s++]);
                l.Add(sb.ToString());
            }
            return l;
        }

        public string Load()
        {
            if (data.Length < codeCheck + 4) return "ROM is too small.";
            foreach (int a in BankOperands)
            {
                int o = File(0x85, a) + adjust;
                if (data[o] != 0xBF || data[o + 1] != 0x00 || data[o + 2] != 0x80 || data[o + 3] != bank)
                    return "The description code isn't where expected.";
            }
            if (bank != OriginalBank && bank < ExpandedBank) return "The descriptions are in an unexpected bank.";
            if (data.Length < blockEnd) return "The descriptions point past the end of the ROM.";
            if (Word(table) != Count * 2) return "The description table doesn't look like the original.";
            int nd = Word(descWords) / 2, ng = Word(genWords) / 2;
            if (nd < 100 || nd > 1000 || ng < 100 || ng > 1000) return "The word lists don't look like the original.";
            descDict = ReadDict(data, descWords, nd);
            genDict = ReadDict(data, genWords, ng);
            foreach (string w in descDict) foreach (char c in w) allowed.Add(c);
            foreach (string w in genDict) foreach (char c in w) allowed.Add(c);
            for (int k = 0; k < Count; k++)
            {
                int o = table + Word(table + 2 * k);
                if (o < table + Count * 2 || o >= blockEnd) return "Description " + k + " points outside the text block.";
                string t = Decode(o);
                if (t == null) return "Description " + k + " couldn't be read.";
                texts[k] = t;
                written[k] = t;
                foreach (char c in t) if (c != '\n') allowed.Add(c);
            }
            for (char c = 'A'; c <= 'Z'; c++) allowed.Add(c);
            for (char c = 'a'; c <= 'z'; c++) allowed.Add(c);
            for (char c = '0'; c <= '9'; c++) allowed.Add(c);
            return null;
        }

        private string Decode(int o)
        {
            StringBuilder sb = new StringBuilder();
            while (o < blockEnd)
            {
                int b = data[o];
                if (b == 0) return sb.ToString();
                if (b >= 0x80) { if ((b & 0x7F) >= descDict.Count) return null; sb.Append(descDict[b & 0x7F]); o++; }
                else if (b == 0x0A) { sb.Append('\n'); o++; }
                else if (b >= 0x20) { sb.Append((char)b); o++; }
                else if (b >= 0x05 && b <= 0x09)
                {
                    int i = 0x80 + (b - 5) * 255 + data[o + 1];
                    if (i >= descDict.Count) return null;
                    sb.Append(descDict[i]); o += 2;
                }
                else if (b >= 0x10)
                {
                    int i = (b - 0x10) * 255 + data[o + 1];
                    if (i >= genDict.Count) return null;
                    sb.Append(genDict[i]); o += 2;
                }
                else return null;
            }
            return null;
        }

        public string GetText(int k) { return texts[k]; }
        private string[] written = new string[Count];   // what the ROM bytes currently hold
        public void SetText(int k, string t) { texts[k] = Normalize(t); }

        public static string Normalize(string t)
        {
            if (t == null) return "";
            return t.Replace("\r\n", "\n").Replace('\r', '\n');
        }

        /// <summary>Letters the game's font can show (everything used in the original texts and word lists).</summary>
        public bool IsAllowed(char c) { return c == '\n' || allowed.Contains(c); }

        /// <summary>Problems with a text: too many lines, too-long lines, letters the font lacks. Null if fine.</summary>
        public string Problem(string t)
        {
            t = Normalize(t);
            string[] lines = t.Split('\n');
            if (lines.Length > MaxLines) return "more than " + MaxLines + " lines";
            for (int i = 0; i < lines.Length; i++)
                if (lines[i].Length > LineLength) return "line " + (i + 1) + " is " + lines[i].Length + " letters (max " + LineLength + ")";
            foreach (char c in t) if (!IsAllowed(c)) return "the game has no letter '" + c + "'";
            return null;
        }

        // ---------------- encoding ----------------

        private class Token { public string Text; public byte[] Code; }
        private List<Token> tokens;
        private Dictionary<char, List<Token>> byFirst;

        private void BuildTokens()
        {
            if (tokens != null) return;
            tokens = new List<Token>();
            for (int i = 0; i < descDict.Count; i++)
            {
                if (descDict[i].Length < 2) continue;
                Token t = new Token(); t.Text = descDict[i];
                if (i < 0x80) t.Code = new byte[] { (byte)(0x80 | i) };
                else { int j = i - 0x80; t.Code = new byte[] { (byte)(5 + j / 255), (byte)(j % 255) }; }
                tokens.Add(t);
            }
            for (int i = 0; i < genDict.Count; i++)
            {
                if (genDict[i].Length < 3) continue;   // 2-letter fragments save nothing over 2 letters
                Token t = new Token(); t.Text = genDict[i];
                t.Code = new byte[] { (byte)(0x10 + i / 255), (byte)(i % 255) };
                tokens.Add(t);
            }
            byFirst = new Dictionary<char, List<Token>>();
            foreach (Token t in tokens)
            {
                List<Token> l;
                if (!byFirst.TryGetValue(t.Text[0], out l)) { l = new List<Token>(); byFirst[t.Text[0]] = l; }
                l.Add(t);
            }
        }

        /// <summary>Smallest encoding (dynamic programming over the text), ending in 00.</summary>
        private Dictionary<string, byte[]> encoded = new Dictionary<string, byte[]>();

        public byte[] Encode(string text)
        {
            text = Normalize(text);
            byte[] cached;
            if (encoded.TryGetValue(text, out cached)) return cached;
            byte[] r = EncodeNow(text);
            encoded[text] = r;
            return r;
        }

        /// <summary>For each letter: true if it's stored as a plain letter (costs a byte each), false if part of a word code.</summary>
        public bool[] LiteralMask(string text)
        {
            text = Normalize(text);
            byte[][] choice; int[] next;
            Parse(text, out choice, out next);
            bool[] lit = new bool[text.Length];
            int p = 0;
            while (p < text.Length && choice[p] != null)
            {
                bool isLit = next[p] == p + 1 && choice[p].Length == 1 && choice[p][0] < 0x80;
                for (int i = p; i < next[p]; i++) lit[i] = isLit;
                p = next[p];
            }
            return lit;
        }

        private byte[] EncodeNow(string text)
        {
            byte[][] choice; int[] next;
            Parse(text, out choice, out next);
            List<byte> outp = new List<byte>();
            int p = 0;
            while (p < text.Length && choice[p] != null) { outp.AddRange(choice[p]); p = next[p]; }
            outp.Add(0);
            return outp.ToArray();
        }

        private void Parse(string text, out byte[][] choice, out int[] next)
        {
            BuildTokens();
            int n = text.Length;
            int[] cost = new int[n + 1];
            choice = new byte[n + 1][];
            next = new int[n + 1];
            cost[n] = 0;
            for (int i = n - 1; i >= 0; i--)
            {
                char c = text[i];
                cost[i] = int.MaxValue;
                if ((c == '\n' || (c >= 0x20 && c < 0x7F)) && cost[i + 1] != int.MaxValue)
                {
                    cost[i] = 1 + cost[i + 1];
                    choice[i] = new byte[] { (byte)(c == '\n' ? 0x0A : c) };
                    next[i] = i + 1;
                }
                List<Token> cands;
                if (!byFirst.TryGetValue(c, out cands)) continue;
                foreach (Token t in cands)
                {
                    int L = t.Text.Length;
                    if (L > n - i || cost[i + L] == int.MaxValue) continue;
                    if (string.CompareOrdinal(text, i, t.Text, 0, L) != 0) continue;
                    int cst = t.Code.Length + cost[i + L];
                    if (cst < cost[i]) { cost[i] = cst; choice[i] = t.Code; next[i] = i + L; }
                }
            }
        }

        /// <summary>Bytes the whole block needs with the current texts (identical texts stored once).</summary>
        public int Used
        {
            get
            {
                Dictionary<string, int> seen = new Dictionary<string, int>();
                int total = 0;
                foreach (string t in texts)
                {
                    if (seen.ContainsKey(t)) continue;
                    seen[t] = 1;
                    total += Encode(t).Length;
                }
                return total;
            }
        }

        public int Free { get { return Capacity - Used; } }

        /// <summary>Repacks the block. Returns false (and writes nothing) if the texts don't fit.</summary>
        public bool Write()
        {
            bool changed = false;
            for (int k = 0; k < Count; k++) if (texts[k] != written[k]) { changed = true; break; }
            if (!changed) return true;   // untouched: keep the bytes exactly as they are
            Dictionary<string, int> placed = new Dictionary<string, int>();
            List<byte> block = new List<byte>();
            int start = table + Count * 2;
            int[] ptr = new int[Count];
            for (int k = 0; k < Count; k++)
            {
                int at;
                if (!placed.TryGetValue(texts[k], out at))
                {
                    at = start + block.Count;
                    placed[texts[k]] = at;
                    block.AddRange(Encode(texts[k]));
                }
                ptr[k] = at - table;
            }
            if (start + block.Count > blockEnd) return false;
            for (int k = 0; k < Count; k++) { data[table + 2 * k] = (byte)ptr[k]; data[table + 2 * k + 1] = (byte)(ptr[k] >> 8); }
            for (int i = 0; i < block.Count; i++) data[start + i] = block[i];
            for (int o = start + block.Count; o < blockEnd; o++) data[o] = 0;
            Array.Copy(texts, written, Count);
            return true;
        }

        /// <summary>Word finder: description and general words containing the text, with their cost.</summary>
        public List<string> FindWords(string part, int max)
        {
            List<string> r = new List<string>();
            if (part == null || part.Trim().Length == 0) return r;
            part = part.Trim();
            HashSet<string> seen = new HashSet<string>();
            for (int pass = 0; pass < 2; pass++)
            {
                List<string> dict = pass == 0 ? descDict : genDict;
                for (int i = 0; i < dict.Count && r.Count < max; i++)
                {
                    string w = dict[i];
                    if (w.Length < 2 || seen.Contains(w)) continue;
                    if (w.IndexOf(part, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    seen.Add(w);
                    int bytes = pass == 0 ? (i < 0x80 ? 1 : 2) : 2;
                    r.Add(w.Replace(" ", "·") + "  -  " + bytes + (bytes == 1 ? " byte" : " bytes") + " (saves " + Math.Max(0, w.Length - bytes) + ")");
                }
            }
            return r;
        }

        /// <summary>Is this word (letters only) in a word list? For the preview's cost shading.</summary>
        public bool IsDictionaryWord(string word)
        {
            return descDict.Contains(word) || genDict.Contains(word);
        }
    }
}
