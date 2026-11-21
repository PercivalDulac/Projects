using System;
using System.IO;
using System.Windows.Forms;
using System.Text.RegularExpressions;

using System.Collections.Generic;
using System.Linq;
using System.Text;



namespace com.lufiaIIEditor.RomInfo
{
    public enum ByteOrder : int
    {
        HighByteFirst = 0,
        LowByteFirst = 1
    }

    class Rom
    {
        private byte[] Data;
        private string[] enemyOffsets;
        private string[] spellOffsets;
        private RomDataParser parser;

        private bool _header;

        // Used by the capsule monster editor.
        public byte[] RawData { get { return Data; } }
        public bool HasHeader { get { return _header; } }
        public string[] SpellNames { get { return spellNames; } }

        private static int maxEnemies = 224;
        private static int maxItems = 467;
        private static int maxSpells = 40;
        private string[] enemyNames = new string[maxEnemies];
        private string[] itemNames = new string[maxItems];
        private string[] spellNames = new string[maxSpells];
        public string[] skillNames;
        public int[] skillNamesLength;
        private string[] skillOffsets;

        private void loadEnemyNames()
        {
            string offset;
            for (var i = 0; i < maxEnemies; i += 1)
            {   
                offset = getEnemyOffset(i);
                enemyNames[i] = readDataString(offset, 13);
            }
        }

        private void loadSpellNames()
        {
            string offset;
            for (var i = 0; i < maxSpells; i += 1)
            {
                offset = getSpellOffset(i);
                spellNames[i] = readDataString(offset, 8);
            }
        }

        public string getEnemyOffset(int v)
        {
            return enemyOffsets[v].TrimEnd('\r', '\n');
        }

        public string getSpellOffset(int v)
        {
            return spellOffsets[v].TrimEnd('\r', '\n');
        }

        public string getSkillOffset(int v)
        {
            return skillOffsets[v].TrimEnd('\r', '\n');
        }

        public Rom(string path)
        {
            this.Data = File.ReadAllBytes(path);

            int remainder = this.Data.Length & 0x7FFF;
            this._header = remainder == 0x200;
            using (StreamReader sr = new StreamReader(AppInfo.FileBesideExe("LufiaMonsterOffsets.txt")))
            {
                string text = sr.ReadToEnd();
                enemyOffsets = text.Split('\n');
            }
            using (StreamReader sr = new StreamReader(AppInfo.FileBesideExe("LufiaSpellOffsets.txt")))
            {
                string text = sr.ReadToEnd();
                spellOffsets = text.Split('\n');
            }
            //2f5fb - 2fcc4 (each 00)
            
            parser = new RomDataParser(Data);
            loadEnemyNames();
            loadSpellNames();
            loadSkillNames();
            parser.setNames(enemyNames, spellNames);
        }



        /// <summary>
        /// Enemy skill names, read the way the game reads them ($85:9532): a pointer table at
        /// $85:F2D7 (file 0x2F4D7 with header) indexed by skill ID - 1. Each record is one type
        /// byte (E0) followed by the name and a 00. skillNames[i] is skill ID i + 1 and
        /// skillOffsets[i] points at the first letter of its name.
        /// (The original loader started two names late and had its offsets one slot off,
        /// so editing a name changed the previous one.)
        /// </summary>
        public void loadSkillNames()
        {
            int hdr = _header ? 0x200 : 0;
            int table = 0x2F2D7 + hdr;
            List<string> names = new List<string>();
            List<int> lengths = new List<int>();
            List<string> offs = new List<string>();
            int first = SkillRecordOffset(table, 0, hdr);
            for (int k = 0; table + 2 * k + 1 < Data.Length && table + 2 * k < first && k < 512; k++)
            {
                int rec = SkillRecordOffset(table, k, hdr);
                if (rec < 0 || rec + 1 >= Data.Length) break;
                int start = rec + 1;
                StringBuilder sb = new StringBuilder();
                int len = 0;
                while (start + len < Data.Length && Data[start + len] != 0 && len < 40)
                {
                    sb.Append((char)Data[start + len]);
                    len++;
                }
                names.Add(sb.ToString());
                lengths.Add(len);
                offs.Add(start.ToString("X"));
            }
            if (names.Count < 50)
            {
                loadSkillNamesLegacy(); // table not where expected: keep the old behavior
                return;
            }
            skillNames = names.ToArray();
            skillNamesLength = lengths.ToArray();
            skillOffsets = offs.ToArray();
        }

        private int SkillRecordOffset(int table, int k, int hdr)
        {
            int addr = Data[table + 2 * k] | (Data[table + 2 * k + 1] << 8);
            if (addr < 0x8000) return -1;
            return hdr + 0x05 * 0x8000 + (addr - 0x8000);
        }

        public void loadSkillNamesLegacy()
        {
            List<string> list1 = new List<string>();
            List<int> list2 = new List<int>();
            List<string> list3 = new List<string>();
            string startOffset = "2F5Fb";
            string dif = parser.hexAdd(parser.hexSub("2FCC4", startOffset), "1");
            int index = int.Parse(startOffset, System.Globalization.NumberStyles.HexNumber);
            int length = int.Parse(dif, System.Globalization.NumberStyles.HexNumber);

            byte[] names = new byte[length];

            Array.Copy(Data, index, names, 0, length);

            string nameAux = "";
            string testChar;
            int l = 0;
            int i = 0;
            
            list3.Add(startOffset);
            foreach (byte _byte in names)
            {
                
                if (i == length)
                {
                    break;
                }
                
                testChar = names[i].ToString("X2");
                if (testChar == "00")
                {

                    list1.Add(nameAux);
                    list2.Add(l);
                    l = 0;
                    nameAux = "";
                }
                else if (testChar != "E0")
                {
                    nameAux += new string((char)names[i], 1);
                    l++;
                }
                else
                {
                    list3.Add(parser.hexAdd(startOffset, "01"));
                }
                i++;
                startOffset = parser.hexAdd(startOffset, "01");
                
            }

            skillNames = list1.ToArray();
            skillNamesLength = list2.ToArray();
            skillOffsets = list3.ToArray();
        }

        public void writeDataInt(int value, string offset, int length, bool lEndian)
        {
            length *= 2;
            //System.Diagnostics.Debug.WriteLine("value: " + value.ToString() + "  offset: " + offset + "  length: " + length.ToString());
            String hexString = value.ToString("X" + (length).ToString());

            int index = int.Parse(offset, System.Globalization.NumberStyles.HexNumber);
            //index -= 1024;
            //index += 512;

            if (!lEndian)
            {
                for (int i = 0; i < length; i += 2)
                {
                    String substring = hexString.Substring(i, 2);
                    byte b = Convert.ToByte(substring, 16);
                    Data[index + (i / 2)] = b;
                }
            }
            else
            {
                StringBuilder sb = new StringBuilder("0000");
                for (int i = 0; i < length; i += 2)
                {
                    String substring = hexString.Substring(i, 2);
                    byte b = Convert.ToByte(substring, 16);
                    Data[index + ((length - i - 2) / 2)] = b;
                    sb[((length - i - 2) / 2)] = substring[0];
                    sb[((length - i - 2) / 2) + 1] = substring[1];
                    
                }
            }
        }

        public void writeDataString(string value, string offset, int length)
        {
            //length *= 2;
            //System.Diagnostics.Debug.WriteLine("value: " + value + "  offset: " + offset + "  length: " + length.ToString());

            int index = int.Parse(offset, System.Globalization.NumberStyles.HexNumber);
            //index -= 1024;
            //index += 512;

            char[] chars = value.ToCharArray();
            int length2 = chars.Length;
            //byte[] bytes = new byte[length];
            int i = 0;
            for (; i < length2; i++)
            {
                Data[index + i] = (byte)chars[i];
            }
            char space = ' ';
            for (; i < length; i++)
            {
                Data[index + i] = (byte)space;
            }
        }

        public void SaveData(string ROMname)
        {
            File.WriteAllBytes(ROMname, Data);
        }

        public string readDataString(string offset, int length)
        {
            return parser.readDataString(offset, length);
        }

        public int readDataInt(string offset, int length, bool lEndian)
        {
            return parser.readDataInt(offset, length, lEndian);
        }

        public string readDataHex(string offset)
        {
            return parser.readDataHex(offset);
        }

        public string readAttScript(string enemyOffset, string endOffset)
        {
            string offset = parser.hexAdd(enemyOffset, "24");
            string extraOffset = "00";
            string dif;
            if (readDataInt(offset, 1, false) == 8)
            {
                var v = readDataHex(parser.hexAdd(offset, "01"));
                if (v == "28")
                {
                    return "";
                }
                dif = parser.hexSub(v, "24");
                endOffset = parser.hexAdd(offset, dif);
                extraOffset = parser.hexAdd(extraOffset, "04");
            }
            else if (readDataInt(offset, 1, false) == 7)
            {
                string v = readDataHex(parser.hexAdd(offset, "03"));
                if (v == "08")
                {
                    endOffset = parser.hexAdd(enemyOffset, readDataHex(parser.hexAdd(offset, "04")));
                }
                extraOffset = parser.hexAdd(extraOffset, "01");
                dif = parser.hexSub(readDataHex(parser.hexAdd(offset, extraOffset)), parser.hexAdd("24", extraOffset));
                extraOffset = parser.hexAdd(extraOffset, dif);
            }
            else if (readDataInt(offset, 1, false) == 0 && readDataHex(parser.hexAdd(offset, "01")) != "42")
            {
                return parser.readScript(parser.hexAdd(offset, "01"), endOffset, "01");
            } 
            else
            {
                return "";
            }
            return parser.readScript(parser.hexAdd(offset, extraOffset), endOffset, extraOffset);
        }


        public string readRctScript(string enemyOffset, string endOffset)
        {
            string offset = parser.hexAdd(enemyOffset, "24");
            string extraOffset = "00";
            string dif;
            if (readDataInt(offset, 1, false) == 7)
            {
                var v = readDataHex(parser.hexAdd(offset, "01"));
                if (v == "28")
                {
                    return "";
                }

                v = readDataHex(parser.hexAdd(offset, "03"));
                if (v == "08") {
                    return parser.readScript(parser.hexAdd(enemyOffset, readDataHex(parser.hexAdd(offset, "04"))), endOffset, readDataHex(parser.hexAdd(offset, "04")));
                } 
                else
                {
                    return "";
                }
                
            }
            else if (readDataInt(offset, 1, false) == 8)
            {

                extraOffset = parser.hexAdd(extraOffset, "01");
                dif = parser.hexSub(readDataHex(parser.hexAdd(offset, extraOffset)), parser.hexAdd("24", extraOffset));
                extraOffset = parser.hexAdd(extraOffset, dif);
            }
            else if (readDataInt(offset, 1, false) == 0 && readDataHex(parser.hexAdd(offset, "01")) == "42")
            {
                return parser.readScript(parser.hexAdd(offset, "01"), endOffset, "01");
            } 
            else
            {
                return "";
            }
            return parser.readScript(parser.hexAdd(offset, extraOffset), endOffset, extraOffset);
        }

        public string readSpellBehavior(string startOffset, string endOffset)
        {
            return parser.readScript(startOffset, endOffset, "00");
        }
    }
}
