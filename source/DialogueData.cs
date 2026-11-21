using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

using com.lufiaIIEditor.UI;

namespace com.lufiaIIEditor.RomInfo
{
    /// <summary>
    /// Story dialogue, edited in place. Dialogue sits inside the map event scripts (interpreter
    /// $80:9D00, opcode table $80:CA14). Text starts after one of the "show text" commands
    /// (08, 13 xx, 61-67, 9E xx) and runs until 00 (end), 01 (next box) or 0B xx. In text mode:
    ///   20-7F letter, 80-FF word 512 + (b - 80), 05 nn / 06 nn word (b - 5) * 256 + nn
    ///   (general word list $8E:EA00), 03 new line, 0C-0F "?" "!" "," "." + new line,
    ///   09 hero name, 04 xx pause (xx >= 80) or voice, 02 xx / 07 xx kept as tags,
    ///   0A xx yy = copy of earlier text bytes (shown as plain text; see ParseRange).
    /// Text lengths never change: a line is re-encoded to exactly its original byte count
    /// (dictionary words can be spelled out to grow, padded with zero pauses "04 80" if needed).
    /// The scripts aren't fully decoded, so lines are found by a strict scan: anything that
    /// doesn't decode as plausible English is left alone.
    /// </summary>
    class DialogueData
    {
        public class Line
        {
            public int Start;      // first text byte
            public int Term;       // terminator byte (kept as it is)
            public int Opcode;     // the command that started the text
            public int Param = -1; // its parameter (13 / 9E), or -1
            public int Length { get { return Term - Start; } }
        }

        public const int MaxLineLetters = 28, MaxLines = 4;
        public const int HeroLength = 5;   // "Maxim" for previews and width checks

        private readonly byte[] data;
        private readonly int adjust, scanStart, scanEnd, words;
        private List<string> dict = new List<string>();
        public readonly List<Line> Lines = new List<Line>();
        private HashSet<string> vocab;
        private List<int[]> others = new List<int[]>();   // text-like runs the editor doesn't offer (start, term)

        private static readonly Dictionary<int, int> Starts = new Dictionary<int, int> {
            { 0x08, 0 }, { 0x13, 1 }, { 0x9E, 1 }, { 0x61, 0 }, { 0x62, 0 }, { 0x63, 0 }, { 0x64, 0 }, { 0x65, 0 }, { 0x66, 0 }, { 0x67, 0 } };
        // Extra bytes after each control code 00-0F in text mode ($80:C744, with 0A taking 2).
        private static readonly int[] CtrlParams = { 0, 0, 1, 0, 1, 1, 1, 1, 0, 0, 2, 1, 0, 0, 0, 0 };

        private static int File(int bank, int addr) { return 0x200 + (bank & 0x7F) * 0x8000 + (addr - 0x8000); }

        public DialogueData(byte[] romData, bool hasHeader)
        {
            data = romData;
            adjust = hasHeader ? 0 : -0x200;
            scanStart = File(0x87, 0x8000) + adjust;
            scanEnd = File(0x8E, 0xFFFF) + 1 + adjust;
            words = File(0x8E, 0xEA00) + adjust;
        }

        public byte[] RawData { get { return data; } }
        public List<string> Words { get { return dict; } }

        private int Word(int o) { return data[o] | (data[o + 1] << 8); }

        // ---------------- reading ----------------

        private class Tok { public int Kind; public string Text; public int Code; public byte[] Bytes; }   // 0 text, 1 control

        /// <summary>Text from p to its terminator; null if anything in it can't be text.</summary>
        private static List<Tok> Parse(byte[] d, List<string> dict, int p, int limit, out int term)
        {
            return ParseRange(d, dict, p, limit, true, 0, out term);
        }

        /// <summary>
        /// Text tokens from p. With untilEnd, runs to the terminator (term = its offset); otherwise
        /// covers exactly the bytes up to limit (used for 0A copies). Null if it can't be text.
        /// 0A xx yy copies (yy &gt;&gt; 4) + 2 bytes from earlier in the ROM: start = (offset after the
        /// code) - 2 - (xx | (yy &amp; 0F) &lt;&lt; 8) ($80:9E83). The copied text is shown as plain text.
        /// </summary>
        private static List<Tok> ParseRange(byte[] d, List<string> dict, int p, int limit, bool untilEnd, int depth, out int term)
        {
            term = -1;
            List<Tok> toks = new List<Tok>();
            int q = p;
            while (q < limit && q < p + 600)
            {
                int b = d[q];
                if (b >= 0x80)
                {
                    int k = 512 + b - 0x80;
                    if (k >= dict.Count) return null;
                    Tok t = new Tok(); t.Kind = 0; t.Text = dict[k]; toks.Add(t); q++;
                }
                else if (b >= 0x20) { Tok t = new Tok(); t.Kind = 0; t.Text = ((char)b).ToString(); toks.Add(t); q++; }
                else if (b >= 0x10 || b == 0x08) return null;
                else if (b == 0x05 || b == 0x06)
                {
                    if (q + 1 >= limit) return null;
                    int k = (b - 5) * 256 + d[q + 1];
                    if (k >= dict.Count) return null;
                    Tok t = new Tok(); t.Kind = 0; t.Text = dict[k]; toks.Add(t); q += 2;
                }
                else if (b == 0x0A)
                {
                    if (q + 2 >= limit || depth > 0) return null;
                    int off = d[q + 1] | ((d[q + 2] & 0x0F) << 8), len = (d[q + 2] >> 4) + 2;
                    int src = q + 3 - 2 - off, t2;
                    if (src < 0 || src + len > d.Length) return null;
                    List<Tok> copied = ParseRange(d, dict, src, src + len, false, depth + 1, out t2);
                    if (copied == null) return null;
                    toks.AddRange(copied);
                    q += 3;
                }
                else
                {
                    int n = CtrlParams[b];
                    if (q + n >= limit && untilEnd) return null;
                    if (b == 0x00 || b == 0x01 || b == 0x0B)
                    {
                        if (!untilEnd) return null;
                        term = q; return toks;
                    }
                    Tok t = new Tok(); t.Kind = 1; t.Code = b;
                    t.Bytes = new byte[n + 1];
                    Array.Copy(d, q, t.Bytes, 0, Math.Min(n + 1, d.Length - q));
                    toks.Add(t);
                    q += 1 + n;
                }
            }
            if (!untilEnd && q == limit) return toks;
            return null;
        }

        /// <summary>Byte ranges [from, to) that 0A codes between start and term copy from.</summary>
        private static List<int[]> CopyRanges(byte[] d, int start, int term)
        {
            List<int[]> r = new List<int[]>();
            int q = start;
            while (q < term)
            {
                int b = d[q];
                if (b >= 0x20) q++;
                else if (b == 0x05 || b == 0x06) q += 2;
                else if (b == 0x0A)
                {
                    int off = d[q + 1] | ((d[q + 2] & 0x0F) << 8), len = (d[q + 2] >> 4) + 2;
                    int src = q + 3 - 2 - off;
                    r.Add(new int[] { src, src + len });
                    q += 3;
                }
                else if (b < 0x10) q += 1 + CtrlParams[b];
                else q++;
            }
            return r;
        }

        private static readonly string Common =
            "the a an and or but if of to in on at by for with from up out about into over after before under again then once here there when where why how all any both each few more most other some such no nor not only own same so than too very can will just don't should now i you he she it we they me him her us them my your his its our their what which who whom this that these those am is are was were be been being have has had having do does did doing would could i'm you're he's she's it's we're they're i've you've we've i'd you'd he'd she'd we'd i'll you'll he'll she'll we'll isn't aren't wasn't weren't hasn't haven't doesn't didn't won't wouldn't can't couldn't let's that's who's what's here's there's oh yes yeah well hey ok please thank thanks sorry go get come see know think take make want look find need let give tell say said ask feel try leave call keep help talk turn show hear hold bring sit stand pay meet stop speak read win open walk remember love buy wait die send stay fall reach kill good new first last long great little old big high small large next young bad right left sure real best better free true whole one two three king queen princess prince castle town village cave tower shrine ship sea boat island door key world people man woman child father mother son brother sister friend way day time year thing place name life hand eye heart power monster enemy sword crown gold money item inn room house shop already still never always ever really maybe also even back down off away much many something nothing everything someone";

        private void BuildVocab(List<List<Tok>> candidates)
        {
            Dictionary<string, int> df = new Dictionary<string, int>();
            Regex re = new Regex("[A-Za-z']{3,}");
            foreach (List<Tok> toks in candidates)
            {
                HashSet<string> seen = new HashSet<string>();
                foreach (Match m in re.Matches(Joined(toks))) seen.Add(m.Value.ToLowerInvariant());
                foreach (string w in seen) { int c; df.TryGetValue(w, out c); df[w] = c + 1; }
            }
            vocab = new HashSet<string>(Common.Split(' '));
            foreach (KeyValuePair<string, int> kv in df) if (kv.Value >= 3) vocab.Add(kv.Key);
            foreach (string w in dict) if (w.Trim().Length >= 4) vocab.Add(w.Trim().ToLowerInvariant());
        }

        private static string Joined(List<Tok> toks)
        {
            StringBuilder sb = new StringBuilder();
            foreach (Tok t in toks) sb.Append(t.Kind == 0 ? t.Text : (t.Code == 9 ? " Maxim " : " "));
            return sb.ToString();
        }

        private static readonly string Bad = "`@_~{}|^\x7f#$=<>*+&;\\";

        private bool LooksReal(List<Tok> toks)
        {
            string s = Joined(toks);
            foreach (char c in s) if (Bad.IndexOf(c) >= 0) return false;
            string st = s.TrimStart(' ');
            if (st.Length == 0) return false;
            char f = st[0];
            if (!(char.IsUpper(f) || "\"'.(![".IndexOf(f) >= 0 || char.IsDigit(f))) return false;
            int good = 0, n = 0, letters = 0;
            foreach (char c in s) if (char.IsLetter(c)) letters++;
            foreach (Match m in Regex.Matches(s, "[A-Za-z']+"))
            {
                string w = m.Value.ToLowerInvariant();
                if (w.Length > 20) return false;                     // script bytes, not a word
                if (w.Length < 2) continue;
                bool known = vocab.Contains(w);
                if (n == 0 && !known) return false;                  // the first word must be a real one
                n++;
                if (known) good++;
            }
            if (n < 1 || good < 0.75 * n) return false;
            if (n < 2 && letters < 4) return false;
            return true;
        }

        public string Load()
        {
            if (data.Length < scanEnd) return "ROM is too small.";
            if (data[File(0x80, 0x9D3B) + adjust] != 0x7C || data[File(0x80, 0x9D3C) + adjust] != 0x14 || data[File(0x80, 0x9D3D) + adjust] != 0xCA)
                return "The event script code isn't where expected.";
            int n = Word(words) / 2;
            if (n < 600 || n > 700) return "The word list doesn't look like the original.";
            for (int k = 0; k < n; k++)
            {
                int s = words + Word(words + 2 * k);
                StringBuilder sb = new StringBuilder();
                while (s < data.Length && data[s] != 0 && sb.Length < 40) sb.Append((char)data[s++]);
                dict.Add(sb.ToString());
            }
            // Pass 1: every candidate, for the word statistics.
            List<List<Tok>> cands = new List<List<Tok>>();
            for (int p = scanStart; p < scanEnd; p++)
            {
                int pc;
                if (!Starts.TryGetValue(data[p], out pc)) continue;
                int term;
                List<Tok> t = Parse(data, dict, p + 1 + pc, scanEnd, out term);
                if (t != null) cands.Add(t);
            }
            BuildVocab(cands);
            Scan(data, adjust, Lines, true);
            FindOthers();
            return null;
        }

        /// <summary>Finds the lines in a ROM image (this one, or the reference for comparison).</summary>
        private void Scan(byte[] d, int adj, List<Line> into, bool useReference)
        {
            into.Clear();
            int lo = File(0x87, 0x8000) + adj, hi = File(0x8E, 0xFFFF) + 1 + adj;
            byte[] r = useReference ? Reference.Data : null;
            for (int p = lo; p < hi; p++)
            {
                int pc;
                if (!Starts.TryGetValue(d[p], out pc)) continue;
                int start = p + 1 + pc, term;
                List<Tok> t = Parse(d, dict, start, hi, out term);
                if (t == null) continue;
                bool ok = LooksReal(t);
                if (!ok && r != null)
                {
                    // An edited line may no longer "look" like the original kind of text:
                    // accept it if the reference ROM has a real line at the same place.
                    int rp = Reference.Shift(p), rterm;
                    if (Reference.InRange(rp, 2) && r[rp] == d[p])
                    {
                        List<Tok> rt = Parse(r, dict, Reference.Shift(start), Math.Min(r.Length, Reference.Shift(hi)), out rterm);
                        ok = rt != null && rterm == Reference.Shift(term) && LooksReal(rt);
                    }
                }
                if (!ok) continue;
                Line l = new Line();
                l.Start = start; l.Term = term; l.Opcode = d[p]; l.Param = pc > 0 ? d[p + 1] : -1;
                into.Add(l);
                p = term;   // continue after this line
            }
        }

        // ---------------- text <-> tags ----------------

        /// <summary>The editable text of a line (in this ROM, or the reference when offsets are shifted).</summary>
        public string GetText(Line l) { return Decode(data, l.Start, l.Term); }

        public string GetReferenceText(Line l)
        {
            if (Reference.Data == null) return null;
            int s = Reference.Shift(l.Start), e = Reference.Shift(l.Term);
            if (!Reference.InRange(s, e - s + 1)) return null;
            int term;
            List<Tok> t = Parse(Reference.Data, dict, s, e + 1, out term);
            if (t == null || term != e) return null;
            return ToText(t);
        }

        public string Decode(byte[] d, int start, int termAt)
        {
            int term;
            List<Tok> t = Parse(d, dict, start, termAt + 1, out term);
            return t == null ? null : ToText(t);
        }

        private static string ToText(List<Tok> toks)
        {
            StringBuilder sb = new StringBuilder();
            foreach (Tok t in toks)
            {
                if (t.Kind == 0) { sb.Append(t.Text); continue; }
                switch (t.Code)
                {
                    case 0x03: sb.Append('\n'); break;
                    case 0x0C: sb.Append("?\n"); break;
                    case 0x0D: sb.Append("!\n"); break;
                    case 0x0E: sb.Append(",\n"); break;
                    case 0x0F: sb.Append(".\n"); break;
                    case 0x09: sb.Append("[Hero]"); break;
                    case 0x04:
                        sb.Append(t.Bytes[1] >= 0x80 ? "[pause " + (t.Bytes[1] & 0x7F) + "]" : "[voice " + t.Bytes[1] + "]");
                        break;
                    default:
                        sb.Append("[" + t.Code.ToString("X2") + ":");
                        for (int i = 1; i < t.Bytes.Length; i++) sb.Append(t.Bytes[i].ToString("X2"));
                        sb.Append("]");
                        break;
                }
            }
            return sb.ToString();
        }

        /// <summary>Splits text into units: tags (bytes given) or single characters (bytes null). Null + error if a tag is wrong.</summary>
        private static List<KeyValuePair<string, byte[]>> Units(string text, out string error)
        {
            error = null;
            List<KeyValuePair<string, byte[]>> u = new List<KeyValuePair<string, byte[]>>();
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == ']') { error = "a ']' without '['"; return null; }
                if (c != '[') { u.Add(new KeyValuePair<string, byte[]>(c.ToString(), null)); continue; }
                int e = text.IndexOf(']', i);
                if (e < 0) { error = "a '[' without ']'"; return null; }
                string tag = text.Substring(i + 1, e - i - 1).Trim();
                byte[] b = TagBytes(tag);
                if (b == null) { error = "unknown tag [" + tag + "]"; return null; }
                u.Add(new KeyValuePair<string, byte[]>("[" + tag + "]", b));
                i = e;
            }
            return u;
        }

        private static byte[] TagBytes(string tag)
        {
            string t = tag.ToLowerInvariant();
            int v;
            if (t == "hero") return new byte[] { 0x09 };
            if (t.StartsWith("pause ") && int.TryParse(t.Substring(6).Trim(), out v) && v >= 0 && v <= 127) return new byte[] { 0x04, (byte)(0x80 | v) };
            if (t.StartsWith("voice ") && int.TryParse(t.Substring(6).Trim(), out v) && v >= 0 && v <= 127) return new byte[] { 0x04, (byte)v };
            Match m = Regex.Match(tag, "^([0-9A-Fa-f]{2}):([0-9A-Fa-f]*)$");
            if (!m.Success) return null;
            int code = Convert.ToInt32(m.Groups[1].Value, 16);
            if (code != 0x02 && code != 0x07) return null;
            string hex = m.Groups[2].Value;
            if (hex.Length != CtrlParams[code] * 2) return null;
            byte[] b = new byte[1 + hex.Length / 2];
            b[0] = (byte)code;
            for (int i = 0; i < hex.Length / 2; i++) b[1 + i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            return b;
        }

        /// <summary>Letters the game uses in text (and word list); others are refused.</summary>
        private HashSet<char> allowed;
        public bool IsAllowed(char c)
        {
            if (allowed == null)
            {
                allowed = new HashSet<char>();
                foreach (string w in dict) foreach (char ch in w) if (ch >= 0x20 && ch < 0x7F) allowed.Add(ch);
                foreach (Line l in Lines) { string t = GetText(l); if (t != null) foreach (char ch in t) if (ch >= 0x20 && ch < 0x7F) allowed.Add(ch); }
                for (char ch = 'A'; ch <= 'Z'; ch++) allowed.Add(ch);
                for (char ch = 'a'; ch <= 'z'; ch++) allowed.Add(ch);
                for (char ch = '0'; ch <= '9'; ch++) allowed.Add(ch);
                allowed.Remove('['); allowed.Remove(']');
            }
            return c == '\n' || allowed.Contains(c);
        }

        /// <summary>Width problems etc. Null if fine. widest = the original's widest line (old lines may be wider).</summary>
        public string Problem(string text, int widest, int oldLines)
        {
            string err;
            List<KeyValuePair<string, byte[]>> u = Units(text, out err);
            if (u == null) return err;
            foreach (KeyValuePair<string, byte[]> x in u) if (x.Value == null && !IsAllowed(x.Key[0])) return "the game has no letter '" + x.Key + "'";
            string[] lines = Preview(text).Split('\n');
            if (lines.Length > Math.Max(MaxLines, oldLines)) return "more than " + Math.Max(MaxLines, oldLines) + " lines in one box";
            int limit = Math.Max(MaxLineLetters, widest);
            for (int i = 0; i < lines.Length; i++)
                if (lines[i].Length > limit) return "line " + (i + 1) + " is " + lines[i].Length + " letters (max " + limit + ")";
            return null;
        }

        /// <summary>What the box shows: tags removed, [Hero] as Maxim.</summary>
        public static string Preview(string text)
        {
            string s = Regex.Replace(text, @"\[hero\]", "Maxim", RegexOptions.IgnoreCase);
            return Regex.Replace(s, @"\[[^\]]*\]", "");
        }

        public static int Widest(string text)
        {
            int w = 0;
            foreach (string l in Preview(text).Split('\n')) w = Math.Max(w, l.Length);
            return w;
        }

        public static int LineCount(string text) { return Preview(text).Split('\n').Length; }

        // ---------------- encoding ----------------

        private class Choice { public int Next; public byte[] Bytes; }
        private Dictionary<char, List<KeyValuePair<string, byte[]>>> byFirst;

        private void BuildWordCodes()
        {
            if (byFirst != null) return;
            byFirst = new Dictionary<char, List<KeyValuePair<string, byte[]>>>();
            for (int i = 0; i < dict.Count && i < 640; i++)
            {
                string w = dict[i];
                if (w.Length < 2 || w.IndexOf('[') >= 0 || w.IndexOf(']') >= 0) continue;
                byte[] code = i < 512 ? new byte[] { (byte)(5 + i / 256), (byte)(i % 256) } : new byte[] { (byte)(0x80 + i - 512) };
                List<KeyValuePair<string, byte[]>> l;
                if (!byFirst.TryGetValue(w[0], out l)) { l = new List<KeyValuePair<string, byte[]>>(); byFirst[w[0]] = l; }
                l.Add(new KeyValuePair<string, byte[]>(w, code));
            }
        }

        /// <summary>
        /// Encodes text to exactly 'length' bytes (no terminator). Returns null with a reason when it
        /// can't fit. Uses spelled-out words to grow; zero pauses (04 80) as a last resort.
        /// </summary>
        public byte[] Encode(string text, int length, out string error, out int minimum)
        {
            byte[] r = EncodeExact(text, length, out error, out minimum);
            if (r == null && minimum <= length && error != null && !error.StartsWith("unknown") && !error.StartsWith("the game") && !error.StartsWith("a "))
            {
                // Odd byte left over: one trailing space (invisible) fixes the count.
                int m2;
                string e2;
                byte[] r2 = EncodeExact(text + " ", length, out e2, out m2);
                if (r2 != null) { error = null; return r2; }
            }
            return r;
        }

        private byte[] EncodeExact(string text, int length, out string error, out int minimum)
        {
            minimum = 0;
            List<KeyValuePair<string, byte[]>> u = Units(text, out error);
            if (u == null) return null;
            BuildWordCodes();
            // Plain string with one char per unit (tags as private-use chars) for word matching.
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < u.Count; i++) sb.Append(u[i].Value == null ? u[i].Key[0] : (char)(0xE000 + i));
            string s = sb.ToString();
            int n = s.Length;
            List<Choice>[] opts = new List<Choice>[n];
            for (int i = 0; i < n; i++)
            {
                List<Choice> o = new List<Choice>();
                char c = s[i];
                if (c >= 0xE000) { Choice ch = new Choice(); ch.Next = i + 1; ch.Bytes = u[c - 0xE000].Value; o.Add(ch); }
                else if (c == '\n') { Choice ch = new Choice(); ch.Next = i + 1; ch.Bytes = new byte[] { 0x03 }; o.Add(ch); }
                else
                {
                    if (c < 0x20 || c >= 0x7F) { error = "the game has no letter '" + c + "'"; return null; }
                    Choice lit = new Choice(); lit.Next = i + 1; lit.Bytes = new byte[] { (byte)c }; o.Add(lit);
                    int p = "?!,.".IndexOf(c);
                    if (p >= 0 && i + 1 < n && s[i + 1] == '\n') { Choice pc = new Choice(); pc.Next = i + 2; pc.Bytes = new byte[] { (byte)(0x0C + p) }; o.Add(pc); }
                    List<KeyValuePair<string, byte[]>> ws;
                    if (byFirst.TryGetValue(c, out ws))
                        foreach (KeyValuePair<string, byte[]> w in ws)
                            if (w.Key.Length <= n - i && string.CompareOrdinal(s, i, w.Key, 0, w.Key.Length) == 0)
                            { Choice wc = new Choice(); wc.Next = i + w.Key.Length; wc.Bytes = w.Value; o.Add(wc); }
                }
                opts[i] = o;
            }
            // reach[i][c]: the rest from i can be encoded in exactly c bytes.
            int max = length;
            bool[][] reach = new bool[n + 1][];
            for (int i = 0; i <= n; i++) reach[i] = new bool[max + 1];
            reach[n][0] = true;
            int[] minc = new int[n + 1];
            minc[n] = 0;
            for (int i = n - 1; i >= 0; i--)
            {
                minc[i] = int.MaxValue;
                foreach (Choice ch in opts[i])
                {
                    if (minc[ch.Next] != int.MaxValue) minc[i] = Math.Min(minc[i], ch.Bytes.Length + minc[ch.Next]);
                    for (int c = 0; c + ch.Bytes.Length <= max; c++)
                        if (reach[ch.Next][c]) reach[i][c + ch.Bytes.Length] = true;
                }
            }
            minimum = minc[0];
            // Exact, or exact minus some zero pauses (2 bytes each).
            for (int pads = 0; 2 * pads <= max; pads++)
            {
                int target = max - 2 * pads;
                if (!reach[0][target]) continue;
                List<byte> outb = new List<byte>();
                int i = 0, left = target;
                while (i < n)
                {
                    bool done = false;
                    foreach (Choice ch in opts[i])
                    {
                        int rest = left - ch.Bytes.Length;
                        if (rest >= 0 && reach[ch.Next][rest]) { outb.AddRange(ch.Bytes); left = rest; i = ch.Next; done = true; break; }
                    }
                    if (!done) { error = "internal encoding error"; return null; }
                }
                for (int k = 0; k < pads; k++) { outb.Add(0x04); outb.Add(0x80); }
                return outb.ToArray();
            }
            error = minimum > length ? (minimum - length) + " bytes too long" : "can't be fitted to exactly " + length + " bytes";
            return null;
        }

        /// <summary>
        /// Text-like runs that aren't offered for editing but might copy (0A) from a line: they're
        /// checked so an edit never silently changes text the editor doesn't show.
        /// </summary>
        private void FindOthers()
        {
            others.Clear();
            List<int> inLine = new List<int>();
            int li = 0;
            for (int p = scanStart; p < scanEnd; p++)
            {
                int pc;
                if (!Starts.TryGetValue(data[p], out pc)) continue;
                int start = p + 1 + pc;
                while (li < Lines.Count && Lines[li].Term < start) li++;
                if (li < Lines.Count && Lines[li].Start - 1 - 1 <= p && p <= Lines[li].Term) continue;   // part of a line
                int term;
                List<Tok> t = Parse(data, dict, start, scanEnd, out term);
                if (t == null) continue;
                int letters = 0, total = 0;
                foreach (char c in Joined(t)) { total++; if (char.IsLetter(c)) letters++; }
                if (total == 0 || letters < 0.5 * total) continue;
                if (CopyRanges(data, start, term).Count > 0) others.Add(new int[] { start, term });
            }
        }

        /// <summary>
        /// Writes a line in place. Other lines that copy (0A) bytes from it are re-encoded with their
        /// old text so they don't change; if one can't be, or text the editor doesn't show copies from
        /// it, nothing is written. False with the reason otherwise.
        /// </summary>
        public bool SetText(Line l, string text, out string error)
        {
            int min;
            byte[] b = Encode(text, l.Length, out error, out min);
            if (b == null) return false;
            foreach (int[] o in others)
                foreach (int[] r in CopyRanges(data, o[0], o[1]))
                    if (r[0] < l.Term && r[1] > l.Start)
                    {
                        error = "part of this line is reused by other text at " + WhereOf(o[0]) +
                                " that the editor doesn't offer, so it can't be changed safely";
                        return false;
                    }
            // Snapshot every line's text and the bytes, then write and repair lines that changed.
            string[] before = new string[Lines.Count];
            for (int i = 0; i < Lines.Count; i++) before[i] = GetText(Lines[i]);
            byte[] backup = new byte[scanEnd - scanStart];
            Array.Copy(data, scanStart, backup, 0, backup.Length);
            Array.Copy(b, 0, data, l.Start, b.Length);
            int self = Lines.IndexOf(l);
            for (int pass = 0; pass < 10; pass++)
            {
                bool any = false;
                for (int i = 0; i < Lines.Count; i++)
                {
                    if (i == self || before[i] == null) continue;
                    if (GetText(Lines[i]) == before[i]) continue;
                    string e2; int m2;
                    byte[] fix = Encode(before[i], Lines[i].Length, out e2, out m2);
                    if (fix == null)
                    {
                        Array.Copy(backup, 0, data, scanStart, backup.Length);
                        error = "line #" + (i + 1).ToString("D4") + " reuses part of this line and wouldn't fit once it changes";
                        return false;
                    }
                    Array.Copy(fix, 0, data, Lines[i].Start, fix.Length);
                    any = true;
                }
                if (!any) { error = null; return true; }
            }
            Array.Copy(backup, 0, data, scanStart, backup.Length);
            error = "the lines that reuse this one couldn't be settled";
            return false;
        }

        private string WhereOf(int fileOffset)
        {
            int o = fileOffset - adjust - 0x200;
            return ((o / 0x8000) | 0x80).ToString("X2") + ":" + (0x8000 + o % 0x8000).ToString("X4");
        }

        /// <summary>Lines (indexes) whose text copies bytes from this line.</summary>
        public List<int> ReusedBy(Line l)
        {
            List<int> r = new List<int>();
            for (int i = 0; i < Lines.Count; i++)
            {
                if (Lines[i] == l) continue;
                foreach (int[] c in CopyRanges(data, Lines[i].Start, Lines[i].Term))
                    if (c[0] < l.Term && c[1] > l.Start) { r.Add(i); break; }
            }
            return r;
        }

        /// <summary>Smallest byte count for a text (for the meter). -1 if it can't be encoded.</summary>
        public int MinimumBytes(string text)
        {
            string err; int min;
            Encode(text, 0, out err, out min);
            return min == int.MaxValue ? -1 : min;
        }

        /// <summary>Word finder over the dialogue word list, with each word's cost.</summary>
        public List<string> FindWords(string part, int max)
        {
            List<string> r = new List<string>();
            if (part == null || part.Trim().Length == 0) return r;
            part = part.Trim();
            for (int i = 0; i < dict.Count && r.Count < max; i++)
            {
                string w = dict[i];
                if (w.Length < 2 || w.IndexOf(part, StringComparison.OrdinalIgnoreCase) < 0) continue;
                int bytes = i < 512 ? 2 : 1;
                r.Add(w.Replace(" ", "·") + "  -  " + bytes + (bytes == 1 ? " byte" : " bytes") + " (saves " + Math.Max(0, w.Length - bytes) + ")");
            }
            return r;
        }

        public string Where(Line l)
        {
            int o = l.Start - adjust - 0x200;
            return ((o / 0x8000) | 0x80).ToString("X2") + ":" + (0x8000 + o % 0x8000).ToString("X4");
        }
    }
}
