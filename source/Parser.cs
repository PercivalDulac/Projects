using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace com.lufiaIIEditor.RomInfo
{
    public class RomDataParser
    {
        private byte[] Data;
        private static int maxEnemies = 224;
        private static int maxItems = 467;
        private static int maxSpells = 40;
        private string[] enemyNames = new string[maxEnemies];
        private string[] itemNames = new string[maxItems];
        private string[] spellNames = new string[maxSpells];


        public RomDataParser(byte[] _data)
        {
            this.Data = _data;
        }

        public void setNames (string[] _enemyNames, string[] _spellNames) 
        {   
            this.enemyNames = _enemyNames;
            this.spellNames = _spellNames;
        }

        public int readDataInt(string offset, int length, bool lEndian)
        {
            byte[] buffer = new byte[length];

            buffer = SubData(offset, length);
            System.Text.StringBuilder hexString = new System.Text.StringBuilder(length * 2);

            for (int index = 0; index < length; index++)
            {
                byte byteValue = buffer[index];
                string hexValue = byteValue.ToString("X");
                if (hexValue.Length < 2) { hexValue = "0" + hexValue; }
                if (!lEndian)
                {
                    hexString.Append(hexValue);
                }
                else
                {
                    hexString.Insert(0, hexValue);
                }
            }
            return (int.Parse(hexString.ToString(), System.Globalization.NumberStyles.HexNumber));
        }

        public string readDataString(string offset, int length)
        {
            byte[] buffer = new byte[length];
            //MessageBox.Show(System.Text.Encoding.UTF8.GetString(Data));
            buffer = SubData(offset, length);
            char[] chars = new char[length];
            int index = 0;
            while (index < length)
            {
                chars[index] = (char)buffer[index];
                index++;
            }


            return (new String(chars, 0, index).Trim());
        }

        public string readDataHex(string offset)
        {
            byte[] buffer = new byte[1];

            buffer = SubData(offset, 1);

            byte byteValue = buffer[0];
            return (byteValue.ToString("X2"));
        }

        public byte[] SubData(string offset, int length)
        {

            int index = int.Parse(offset, System.Globalization.NumberStyles.HexNumber);
            //index -= 1024;
            //index += 512;

            byte[] result = new byte[length];
            Array.Copy(Data, index, result, 0, length);
            return result;
        }

        public string hexAdd(string value1, string value2)
        {
            string r;
            int v1 = int.Parse(value1, System.Globalization.NumberStyles.HexNumber);
            int v2 = int.Parse(value2, System.Globalization.NumberStyles.HexNumber);
            r = (v1 + v2).ToString("X");
            return r;
        }

        public string hexSub(string value1, string value2)
        {
            string r;
            int v1 = int.Parse(value1, System.Globalization.NumberStyles.HexNumber);
            int v2 = int.Parse(value2, System.Globalization.NumberStyles.HexNumber);
            r = (v1 - v2).ToString("X");
            return r;
        }

        
        public string readScript(string startOffset, string endOffset, string extra)
        {

            string result = "";
            string offset = startOffset;
            string extraOffset = "00";
            string byteValue1;
            bool ok = false;
            //System.Diagnostics.Debug.WriteLine("===============================");

            string endb, enda, reg;
            int stat, prob, value1, value2, value3;

            while (!ok)
            {
                
                if (hexAdd(offset, extraOffset) == endOffset)
                {
                    break;
                }

                
                byteValue1 = readDataHex(hexAdd(offset, extraOffset));
                result += "offset: " + hexAdd(offset, extraOffset) + "  rel: " + hexAdd(hexAdd(extra, extraOffset), "24") + "   byte: " + byteValue1 + "  >  ";

                //System.Diagnostics.Debug.WriteLine("offset: " + hexAdd(offset, extraOffset) + "  rel: " + hexAdd("24", extraOffset) + "   byte: " + byteValue1 + "  >  ");
                switch (byteValue1)
                {
                    case "00":
                        result += "End\r\n\r\n";
                        extraOffset = hexAdd(extraOffset, "01");
                        //ok = true;
                        break;

                    case "01":
                        result += "Continue\r\n\r\n";
                        extraOffset = hexAdd(extraOffset, "01");
                        break;

                    case "03":
                        extraOffset = hexAdd(extraOffset, "01");
                        endb = readDataHex(hexAdd(offset, extraOffset));
                        extraOffset = hexAdd(extraOffset, "01");
                        enda = readDataHex(hexAdd(offset, extraOffset));
                        result += "Go to: " + enda + " " + endb + "\r\n";
                        extraOffset = hexAdd(extraOffset, "01");
                        break;

                    case "04":
                        extraOffset = hexAdd(extraOffset, "01");
                        endb = readDataHex(hexAdd(offset, extraOffset));
                        extraOffset = hexAdd(extraOffset, "01");
                        enda = readDataHex(hexAdd(offset, extraOffset));
                        result += "On error, go to: " + enda + " " + endb + "\r\n";
                        extraOffset = hexAdd(extraOffset, "01");
                        break;

                    case "05":
                        extraOffset = hexAdd(extraOffset, "01");
                        prob = readDataInt(hexAdd(offset, extraOffset), 1, false);
                        prob = (prob * 100) / 255;
                        extraOffset = hexAdd(extraOffset, "01");
                        endb = readDataHex(hexAdd(offset, extraOffset));
                        extraOffset = hexAdd(extraOffset, "01");
                        enda = readDataHex(hexAdd(offset, extraOffset));
                        result += prob.ToString() + "% chance, go to: " + enda + " " + endb + "\r\n";
                        extraOffset = hexAdd(extraOffset, "01");
                        break;

                    case "06":
                        extraOffset = hexAdd(extraOffset, "01");
                        reg = readDataHex(hexAdd(offset, extraOffset));
                        extraOffset = hexAdd(extraOffset, "01");
                        stat = readDataInt(hexAdd(offset, extraOffset), 2, true);
                        extraOffset = hexAdd(extraOffset, "02");
                        endb = readDataHex(hexAdd(offset, extraOffset));
                        extraOffset = hexAdd(extraOffset, "01");
                        enda = readDataHex(hexAdd(offset, extraOffset));
                        result += "If reg($" + reg + ") != " + stat.ToString() + ", Jump to " + enda + " " + endb + "\r\n";
                        extraOffset = hexAdd(extraOffset, "01");
                        break;

                    case "07":
                        extraOffset = hexAdd(extraOffset, "01");
                        reg = readDataHex(hexAdd(offset, extraOffset));
                        extraOffset = hexAdd(extraOffset, "01");
                        stat = readDataInt(hexAdd(offset, extraOffset), 2, true);
                        extraOffset = hexAdd(extraOffset, "02");
                        endb = readDataHex(hexAdd(offset, extraOffset));
                        extraOffset = hexAdd(extraOffset, "01");
                        enda = readDataHex(hexAdd(offset, extraOffset));
                        result += "If reg($" + reg + ") == " + stat.ToString() + ", Jump to " + enda + " " + endb + "\r\n";
                        extraOffset = hexAdd(extraOffset, "01");
                        break;

                    case "08":
                        extraOffset = hexAdd(extraOffset, "01");
                        reg = readDataHex(hexAdd(offset, extraOffset));
                        extraOffset = hexAdd(extraOffset, "01");
                        stat = readDataInt(hexAdd(offset, extraOffset), 2, true);
                        extraOffset = hexAdd(extraOffset, "02");
                        endb = readDataHex(hexAdd(offset, extraOffset));
                        extraOffset = hexAdd(extraOffset, "01");
                        enda = readDataHex(hexAdd(offset, extraOffset));
                        result += "If reg($" + reg + ") > " + stat.ToString() + ", Jump to " + enda + " " + endb + "\r\n";
                        extraOffset = hexAdd(extraOffset, "01");
                        break;

                    case "09":
                        extraOffset = hexAdd(extraOffset, "01");
                        reg = readDataHex(hexAdd(offset, extraOffset));
                        extraOffset = hexAdd(extraOffset, "01");
                        stat = readDataInt(hexAdd(offset, extraOffset), 2, true);
                        extraOffset = hexAdd(extraOffset, "02");
                        endb = readDataHex(hexAdd(offset, extraOffset));
                        extraOffset = hexAdd(extraOffset, "01");
                        enda = readDataHex(hexAdd(offset, extraOffset));
                        result += "If reg($" + reg + ") < " + stat.ToString() + ", Jump to " + enda + " " + endb + "\r\n";
                        extraOffset = hexAdd(extraOffset, "01");
                        break;

                    case "0A":
                        extraOffset = hexAdd(extraOffset, "01");
                        reg = readDataHex(hexAdd(offset, extraOffset));
                        extraOffset = hexAdd(extraOffset, "01");
                        stat = readDataInt(hexAdd(offset, extraOffset), 2, true);
                        extraOffset = hexAdd(extraOffset, "02");
                        endb = readDataHex(hexAdd(offset, extraOffset));
                        extraOffset = hexAdd(extraOffset, "01");
                        enda = readDataHex(hexAdd(offset, extraOffset));
                        result += "If reg($" + reg + ") >= " + stat.ToString() + ", Jump to " + enda + " " + endb + "\r\n";
                        extraOffset = hexAdd(extraOffset, "01");
                        break;

                    case "0B":
                        extraOffset = hexAdd(extraOffset, "01");
                        reg = readDataHex(hexAdd(offset, extraOffset));
                        extraOffset = hexAdd(extraOffset, "01");
                        stat = readDataInt(hexAdd(offset, extraOffset), 2, true);
                        extraOffset = hexAdd(extraOffset, "02");
                        endb = readDataHex(hexAdd(offset, extraOffset));
                        extraOffset = hexAdd(extraOffset, "01");
                        enda = readDataHex(hexAdd(offset, extraOffset));
                        result += "If reg($" + reg + ") <= " + stat.ToString() + ", Jump to " + enda + " " + endb + "\r\n";
                        extraOffset = hexAdd(extraOffset, "01");
                        break;

                    case "0C":
                        extraOffset = hexAdd(extraOffset, "01");
                        reg = readDataHex(hexAdd(offset, extraOffset));
                        extraOffset = hexAdd(extraOffset, "01");
                        endb = readDataHex(hexAdd(offset, extraOffset));
                        extraOffset = hexAdd(extraOffset, "01");
                        enda = readDataHex(hexAdd(offset, extraOffset));
                        extraOffset = hexAdd(extraOffset, "01");
                        if (enda == "00" && endb == "00")
                        {
                            switch (reg)
                            {
                                case "4E":
                                    result += "Protection against poison\r\n";
                                    break;
                                case "50":
                                    result += "Protection against silence\r\n";
                                    break;
                                case "52":
                                    result += "Protection against death\r\n";
                                    break;
                                case "54":
                                    result += "Protection against paralysis\r\n";
                                    break;
                                case "56":
                                    result += "Protection against confusion\r\n";
                                    break;
                                case "58":
                                    result += "Protection against sleep\r\n";
                                    break;
                                default:
                                    result += "Load value " + enda + " " + endb + " to reg ($" + reg + ")\r\n";
                                    break;
                            }
                        }
                        else
                        {
                            result += "Load value " + enda + " " + endb + " to reg ($" + reg + ")\r\n";
                        }
                        break;

                    case "0D":
                        extraOffset = hexAdd(extraOffset, "01");
                        reg = readDataHex(hexAdd(offset, extraOffset));
                        extraOffset = hexAdd(extraOffset, "01");
                        endb = readDataHex(hexAdd(offset, extraOffset));
                        extraOffset = hexAdd(extraOffset, "01");
                        enda = readDataHex(hexAdd(offset, extraOffset));
                        result += "Add value " + enda + " " + endb + " to reg ($" + reg + ")\r\n";
                        extraOffset = hexAdd(extraOffset, "01");
                        break;

                    case "0E":
                        extraOffset = hexAdd(extraOffset, "01");
                        reg = readDataHex(hexAdd(offset, extraOffset));
                        extraOffset = hexAdd(extraOffset, "01");
                        endb = readDataHex(hexAdd(offset, extraOffset));
                        extraOffset = hexAdd(extraOffset, "01");
                        enda = readDataHex(hexAdd(offset, extraOffset));
                        result += "Sub value " + enda + " " + endb + " to reg ($" + reg + ")\r\n";
                        extraOffset = hexAdd(extraOffset, "01");
                        break;

                    case "0F":
                        extraOffset = hexAdd(extraOffset, "01");
                        reg = readDataHex(hexAdd(offset, extraOffset));
                        extraOffset = hexAdd(extraOffset, "01");
                        endb = readDataHex(hexAdd(offset, extraOffset));
                        extraOffset = hexAdd(extraOffset, "01");
                        enda = readDataHex(hexAdd(offset, extraOffset));
                        result += "Multiply reg ($" + reg + ") by " + enda + " " + endb + "\r\n";
                        extraOffset = hexAdd(extraOffset, "01");
                        break;

                    case "10":
                        extraOffset = hexAdd(extraOffset, "01");
                        reg = readDataHex(hexAdd(offset, extraOffset));
                        extraOffset = hexAdd(extraOffset, "01");
                        endb = readDataHex(hexAdd(offset, extraOffset));
                        extraOffset = hexAdd(extraOffset, "01");
                        enda = readDataHex(hexAdd(offset, extraOffset));
                        result += "Divide reg ($" + reg + ") by " + enda + " " + endb + "\r\n";
                        extraOffset = hexAdd(extraOffset, "01");
                        break;

                    case "11":
                        extraOffset = hexAdd(extraOffset, "01");
                        reg = readDataHex(hexAdd(offset, extraOffset));
                        result += "Get random from reg ($" + reg + ")\r\n";
                        extraOffset = hexAdd(extraOffset, "01");
                        break;

                    case "12":
                        extraOffset = hexAdd(extraOffset, "01");
                        endb = readDataHex(hexAdd(offset, extraOffset));
                        extraOffset = hexAdd(extraOffset, "01");
                        enda = readDataHex(hexAdd(offset, extraOffset));
                        result += "Byte 12 ??? : " + enda + " " + endb + "\r\n";
                        extraOffset = hexAdd(extraOffset, "01");
                        break;

                    case "13":
                        extraOffset = hexAdd(extraOffset, "01");
                        reg = readDataHex(hexAdd(offset, extraOffset));
                        extraOffset = hexAdd(extraOffset, "01");
                        result += "Load at reg($" + reg + ") " + readDataHex(hexAdd(offset, extraOffset)) + "\r\n";
                        extraOffset = hexAdd(extraOffset, "01");
                        break;

                    case "14":
                        extraOffset = hexAdd(extraOffset, "01");
                        reg = readDataHex(hexAdd(offset, extraOffset));
                        extraOffset = hexAdd(extraOffset, "01");
                        result += "Load at ";
                        result += readStat(readDataHex(hexAdd(offset, extraOffset))) + "  ";
                        if (readDataHex(hexAdd(offset, extraOffset)) == "14")
                            extraOffset = hexAdd(extraOffset, "01");
                        extraOffset = hexAdd(extraOffset, "01");
                        result += "  reg ($" + reg + ")\r\n";
                        extraOffset = hexAdd(extraOffset, "01");
                        break;

                    case "15":
                        extraOffset = hexAdd(extraOffset, "01");
                        reg = readDataHex(hexAdd(offset, extraOffset));
                        extraOffset = hexAdd(extraOffset, "01");
                        result += "Load at reg($" + reg + ")  SELF ";
                        result += readStat(readDataHex(hexAdd(offset, extraOffset))) + "\r\n";
                        if (readDataHex(hexAdd(offset, extraOffset)) == "14")
                            extraOffset = hexAdd(extraOffset, "01");
                        extraOffset = hexAdd(extraOffset, "01");
                        break;

                    case "16":
                        extraOffset = hexAdd(extraOffset, "01");
                        reg = readDataHex(hexAdd(offset, extraOffset));
                        extraOffset = hexAdd(extraOffset, "01");
                        endb = readDataHex(hexAdd(offset, extraOffset));
                        extraOffset = hexAdd(extraOffset, "01");
                        enda = readDataHex(hexAdd(offset, extraOffset));
                        result += "AND ($" + reg + ") by " + enda + " " + endb + "\r\n";
                        extraOffset = hexAdd(extraOffset, "01");
                        break;

                    case "17":
                        extraOffset = hexAdd(extraOffset, "01");
                        reg = readDataHex(hexAdd(offset, extraOffset));
                        extraOffset = hexAdd(extraOffset, "01");
                        endb = readDataHex(hexAdd(offset, extraOffset));
                        extraOffset = hexAdd(extraOffset, "01");
                        enda = readDataHex(hexAdd(offset, extraOffset));
                        result += "OR ($" + reg + ") by " + enda + " " + endb + "\r\n";
                        extraOffset = hexAdd(extraOffset, "01");
                        break;

                    case "18":
                        extraOffset = hexAdd(extraOffset, "01");
                        reg = readDataHex(hexAdd(offset, extraOffset));
                        extraOffset = hexAdd(extraOffset, "01");
                        endb = readDataHex(hexAdd(offset, extraOffset));
                        extraOffset = hexAdd(extraOffset, "01");
                        enda = readDataHex(hexAdd(offset, extraOffset));
                        result += "EOR ($" + reg + ") by " + enda + " " + endb + "\r\n";
                        extraOffset = hexAdd(extraOffset, "01");
                        break;

                    case "19":
                        extraOffset = hexAdd(extraOffset, "01");
                        reg = readDataHex(hexAdd(offset, extraOffset));
                        result += "Byte 19 ???? : " + reg + "\r\n";
                        extraOffset = hexAdd(extraOffset, "01");
                        break;

                    case "1A":
                        extraOffset = hexAdd(extraOffset, "01");
                        reg = readDataHex(hexAdd(offset, extraOffset));
                        result += "Neg reg ($" + reg + ")\r\n";
                        extraOffset = hexAdd(extraOffset, "01");
                        break;

                    case "1B":
                        extraOffset = hexAdd(extraOffset, "01");
                        reg = readDataHex(hexAdd(offset, extraOffset));
                        result += "Byte 1B ???? : " + reg + "\r\n";
                        extraOffset = hexAdd(extraOffset, "01");
                        break;

                    case "1C":
                        extraOffset = hexAdd(extraOffset, "01");
                        reg = readDataHex(hexAdd(offset, extraOffset));
                        result += "Byte 1C ???? : " + reg + "\r\n";
                        extraOffset = hexAdd(extraOffset, "01");
                        break;

                    case "1D":
                        extraOffset = hexAdd(extraOffset, "01");
                        result += "Cancel and continue?\r\n";
                        break;

                    case "1E":
                        extraOffset = hexAdd(extraOffset, "01");
                        reg = readDataHex(hexAdd(offset, extraOffset));
                        result += "Display monster attack name : " + reg + "\r\n";
                        extraOffset = hexAdd(extraOffset, "01");
                        break;

                    case "1F":
                        extraOffset = hexAdd(extraOffset, "01");
                        enda = readDataHex(hexAdd(offset, extraOffset));
                        extraOffset = hexAdd(extraOffset, "01");
                        endb = readDataHex(hexAdd(offset, extraOffset));
                        result += "Add elemental power to attack: ";
                        result += readElement(enda, endb) + "\r\n";
                        extraOffset = hexAdd(extraOffset, "01");
                        break;

                    case "20":
                        extraOffset = hexAdd(extraOffset, "01");
                        value1 = readDataInt(hexAdd(offset, extraOffset), 1, false);
                        extraOffset = hexAdd(extraOffset, "01");
                        value2 = readDataInt(hexAdd(offset, extraOffset), 1, false);
                        result += "Enhanced critical: rate " + value1.ToString() + "   damage multiplier: " + (value2 / 10).ToString() + ")\r\n";
                        extraOffset = hexAdd(extraOffset, "01");
                        break;

                    case "21":
                        extraOffset = hexAdd(extraOffset, "01");
                        enda = readDataHex(hexAdd(offset, extraOffset));
                        extraOffset = hexAdd(extraOffset, "01");
                        endb = readDataHex(hexAdd(offset, extraOffset));
                        extraOffset = hexAdd(extraOffset, "01");
                        value1 = readDataInt(hexAdd(offset, extraOffset), 2, true);
                        extraOffset = hexAdd(extraOffset, "02");
                        reg = readDataHex(hexAdd(offset, extraOffset));
                        result += "Physical damage > element:" + readElement(enda, endb) + "   base damage: " + value1.ToString() + "   Z: " + reg + "\r\n";
                        extraOffset = hexAdd(extraOffset, "01");
                        break;

                    case "22":
                        extraOffset = hexAdd(extraOffset, "01");
                        enda = readDataHex(hexAdd(offset, extraOffset));
                        extraOffset = hexAdd(extraOffset, "01");
                        endb = readDataHex(hexAdd(offset, extraOffset));
                        extraOffset = hexAdd(extraOffset, "01");
                        value1 = readDataInt(hexAdd(offset, extraOffset), 2, true);
                        extraOffset = hexAdd(extraOffset, "02");
                        reg = readDataHex(hexAdd(offset, extraOffset));
                        result += "Magic damage > element:" + readElement(enda, endb) + "   base damage: " + value1.ToString() + "   Z: " + reg + "\r\n";
                        extraOffset = hexAdd(extraOffset, "01");
                        break;

                    case "23":
                        extraOffset = hexAdd(extraOffset, "01");
                        endb = readDataHex(hexAdd(offset, extraOffset));
                        extraOffset = hexAdd(extraOffset, "01");
                        enda = readDataHex(hexAdd(offset, extraOffset));
                        extraOffset = hexAdd(extraOffset, "01");
                        result += "Byte 23 ???  :" + endb + " " + enda + "\r\n";
                        break;

                    case "24":
                        extraOffset = hexAdd(extraOffset, "01");
                        endb = readStat(readDataHex(hexAdd(offset, extraOffset)));
                        extraOffset = hexAdd(extraOffset, "01");
                        value1 = readDataInt(hexAdd(offset, extraOffset), 2, true);
                        extraOffset = hexAdd(extraOffset, "02");
                        enda = readDataHex(hexAdd(offset, extraOffset));
                        extraOffset = hexAdd(extraOffset, "01");
                        result += "Restoring (or modifying) effect > type: " + endb + "   base value: " + value1.ToString() + "   fluctuation: " + enda + "\r\n";
                        break;

                    case "25":
                        extraOffset = hexAdd(extraOffset, "01");
                        endb = readStat(readDataHex(hexAdd(offset, extraOffset)));
                        extraOffset = hexAdd(extraOffset, "01");
                        value1 = readDataInt(hexAdd(offset, extraOffset), 1, false);
                        extraOffset = hexAdd(extraOffset, "01");
                        result += "Stat up/down: " + endb + "   intensity: " + value1.ToString() + "\r\n";
                        break;

                    case "26":
                        extraOffset = hexAdd(extraOffset, "01");
                        endb = readAil(readDataHex(hexAdd(offset, extraOffset)));
                        extraOffset = hexAdd(extraOffset, "01");
                        value1 = readDataInt(hexAdd(offset, extraOffset), 1, false);
                        extraOffset = hexAdd(extraOffset, "01");
                        result += "Recover status: " + endb + "   chance: " + value1.ToString() + "\r\n";
                        break;

                    case "27":
                        extraOffset = hexAdd(extraOffset, "01");
                        endb = readAil(readDataHex(hexAdd(offset, extraOffset)));
                        extraOffset = hexAdd(extraOffset, "01");
                        value1 = readDataInt(hexAdd(offset, extraOffset), 1, false);
                        extraOffset = hexAdd(extraOffset, "01");
                        result += "Cause status: " + endb + "   chance: " + value1.ToString() + "\r\n";
                        break;

                    case "28":
                        result += "Physical attack\r\n";
                        extraOffset = hexAdd(extraOffset, "01");
                        break;

                    case "29":
                        result += "Defends\r\n";
                        extraOffset = hexAdd(extraOffset, "01");
                        break;

                    case "2A":
                        result += "Escape\r\n";
                        extraOffset = hexAdd(extraOffset, "01");
                        break;

                    case "2C":
                        extraOffset = hexAdd(extraOffset, "01");
                        System.Diagnostics.Debug.WriteLine("====");
                        System.Diagnostics.Debug.WriteLine("offset: " + offset);
                        System.Diagnostics.Debug.WriteLine("extraOffset " + extraOffset + "    GETTING SPELL FROM INDEX: " + readDataInt(hexAdd(offset, extraOffset), 1, false).ToString());
                        result += "Casting spell: " + spellNames[readDataInt(hexAdd(offset, extraOffset), 1, false)] + " (" + readDataHex(hexAdd(offset, extraOffset)) + ")\r\n";
                        extraOffset = hexAdd(extraOffset, "01");
                        break;

                    case "2D":
                        extraOffset = hexAdd(extraOffset, "01");
                        result += "Calls companion: " + readDataHex(hexAdd(offset, extraOffset)) + "\r\n";
                        extraOffset = hexAdd(extraOffset, "01");
                        break;

                    case "2E":
                        extraOffset = hexAdd(extraOffset, "01");
                        result += "Byte 2E ??? \r\n";
                        break;

                    case "2F":
                        extraOffset = hexAdd(extraOffset, "01");
                        endb = readDataHex(hexAdd(offset, extraOffset));
                        extraOffset = hexAdd(extraOffset, "01");
                        result += "Byte 2F ??? : " + endb + "\r\n";
                        break;

                    case "30":
                        extraOffset = hexAdd(extraOffset, "01");
                        endb = readDataHex(hexAdd(offset, extraOffset));
                        extraOffset = hexAdd(extraOffset, "01");
                        result += "Byte 30 ??? : " + endb + "\r\n";
                        break;

                    case "32":
                        extraOffset = hexAdd(extraOffset, "01");
                        if (readDataHex(hexAdd(offset, hexAdd(extraOffset, "01"))) == "32" && readDataHex(hexAdd(offset, hexAdd(extraOffset, "02"))) == "05")
                        {
                            if (readDataHex(hexAdd(offset, extraOffset)) == "01")
                            {
                                result += "Target All Enemies\r\n";
                            }
                            else
                            {
                                result += "Target All Allies\r\n";
                            }
                            extraOffset = hexAdd(extraOffset, "02");
                        }
                        else
                        {
                            if (readDataHex(hexAdd(offset, extraOffset)) == "01")
                            {
                                result += "Target One Enemy\r\n";
                            }
                            else if (readDataHex(hexAdd(offset, extraOffset)) == "06")
                            {
                                result += "Target Self\r\n";
                            }
                            else
                            {
                                result += "Target One Ally\r\n";
                            }
                        }
                        extraOffset = hexAdd(extraOffset, "01");

                        break;

                    case "35":
                        extraOffset = hexAdd(extraOffset, "01");
                        endb = readDataHex(hexAdd(offset, extraOffset));
                        extraOffset = hexAdd(extraOffset, "01");
                        result += "Byte 35 ??? : " + endb + "\r\n";
                        break;

                    case "37":
                        extraOffset = hexAdd(extraOffset, "01");
                        result += "Physical attack (for weapons)\r\n";
                        break;

                    case "3E":
                        extraOffset = hexAdd(extraOffset, "01");
                        endb = readDataHex(hexAdd(offset, extraOffset));
                        extraOffset = hexAdd(extraOffset, "01");
                        result += "Display capsule monster attack name??? : " + endb + "\r\n";
                        break;

                    case "3F":
                        extraOffset = hexAdd(extraOffset, "01");
                        result += "For capsule monsters...\r\n";
                        break;

                    case "40":
                        extraOffset = hexAdd(extraOffset, "01");
                        endb = readDataHex(hexAdd(offset, extraOffset));
                        extraOffset = hexAdd(extraOffset, "01");
                        result += "Byte 40 ??? : " + endb + "\r\n";
                        break;

                    case "41":
                        extraOffset = hexAdd(extraOffset, "01");
                        result += "Cheking situation...\r\n";
                        break;

                    case "42":
                        extraOffset = hexAdd(extraOffset, "01");
                        endb = readProtection(readDataHex(hexAdd(offset, extraOffset)));
                        extraOffset = hexAdd(extraOffset, "01");
                        enda = readDataHex(hexAdd(offset, extraOffset));
                        extraOffset = hexAdd(extraOffset, "01");
                        result += "Attribute: " + endb + "   byte: " + enda + "\r\n";
                        break;

                    case "47":
                        extraOffset = hexAdd(extraOffset, "01");
                        enda = readDataHex(hexAdd(offset, extraOffset));
                        extraOffset = hexAdd(extraOffset, "01");
                        if (enda == "78")
                        {

                            extraOffset = hexAdd(extraOffset, "01");
                            extraOffset = hexAdd(extraOffset, "01");
                            endb = readDataHex(hexAdd(offset, extraOffset));
                            extraOffset = hexAdd(extraOffset, "01");
                            extraOffset = hexAdd(extraOffset, "01");
                            result += "Cast spell for IPs " + endb + "\r\n";
                        }
                        else
                        {
                            endb = readDataHex(hexAdd(offset, extraOffset));
                            result += "Byte 47 ??? : " + enda + " " + endb + "\r\n";
                        }
                        extraOffset = hexAdd(extraOffset, "01");
                        break;

                    case "4F":
                        extraOffset = hexAdd(extraOffset, "01");
                        result += "Exit without executing code\r\n";
                        break;

                    case "51":
                        extraOffset = hexAdd(extraOffset, "01");
                        result += "Dark reflector effect\r\n";
                        break;

                    case "54":
                        extraOffset = hexAdd(extraOffset, "01");
                        result += "Casting spell (?) for IPs (?): " + readDataHex(hexAdd(offset, extraOffset)) + "\r\n";
                        extraOffset = hexAdd(extraOffset, "01");
                        break;

                    case "55":
                        extraOffset = hexAdd(extraOffset, "01");
                        extraOffset = hexAdd(extraOffset, "01");
                        extraOffset = hexAdd(extraOffset, "01");
                        extraOffset = hexAdd(extraOffset, "01");
                        result += "Unsigned division\r\n";
                        break;

                    case "56":
                        extraOffset = hexAdd(extraOffset, "01");
                        value1 = readDataInt(hexAdd(offset, extraOffset), 2, true);
                        extraOffset = hexAdd(extraOffset, "02");
                        result += "Duration of attack name display " + value1.ToString() + "\r\n";
                        break;

                    case "57":
                        extraOffset = hexAdd(extraOffset, "01");
                        extraOffset = hexAdd(extraOffset, "01");
                        extraOffset = hexAdd(extraOffset, "01");
                        extraOffset = hexAdd(extraOffset, "01");
                        result += "Spell effect augment\r\n";
                        break;

                    case "58":
                        extraOffset = hexAdd(extraOffset, "01");
                        result += "Eerie light effect (cancels temp stat_reg on target)\r\n";
                        break;

                    case "5A":
                        extraOffset = hexAdd(extraOffset, "01");
                        endb = readDataHex(hexAdd(offset, extraOffset));
                        extraOffset = hexAdd(extraOffset, "01");
                        result += "Show spell effect: " + endb + "\r\n";
                        break;

                    case "5B":
                        extraOffset = hexAdd(extraOffset, "01");
                        result += "Lose battle if enemy is killed (used by Master)\r\n";
                        break;

                    case "5C":
                        extraOffset = hexAdd(extraOffset, "01");
                        result += "Don't show damage numbers\r\n";
                        break;


                    default:
                        result += "I dunno...!!\r\n";
                        extraOffset = hexAdd(extraOffset, "01");
                        break;

                }
            }

            return result;
        }

        private string readStat(string statByte)
        {
            switch (statByte)
            {
                case "00": return "current HP"; break;
                case "01": return "current MP"; break;
                case "02": return "max HP"; break;
                case "03": return "max MP"; break;
                case "04": return "ATP"; break;
                case "05": return "DFP"; break;
                case "06": return "STR"; break;
                case "07": return "AGL"; break;
                case "08": return "INT"; break;
                case "09": return "GUT"; break;
                case "0A": return "MGR"; break;
                case "0B": return "IP"; break;
                case "0C": return "Level"; break;
                case "0D": return "temp ATP+"; break;
                case "0E": return "temp DFP+"; break;
                case "0F": return "temp STR+"; break;
                case "10": return "temp AGL+"; break;
                case "11": return "temp INT+"; break;
                case "12": return "temp GUT+"; break;
                case "13": return "temp MGR+"; break;
                case "14": return "Status "; break;
            }
            return "none";
        }

        private string readElement(string a, string b)
        {
            // THIS IS NOT LITTLE ENDIAN!!!
            switch (a + b)
            {
                case "0000": return "Neutral"; break;
                case "0001": return "Anti-dragon"; break;
                case "0002": return "Anti-insect"; break;
                case "0004": return "Ice"; break;
                case "0008": return "Thunder"; break;
                case "0010": return "Earth"; break;
                case "0020": return "Anti-sea"; break;
                case "0100": return "Wind"; break;
                case "0200": return "Fire"; break;
                case "0400": return "Water"; break;
                case "0800": return "Soil"; break;
                case "1000": return "Light"; break;
                case "2000": return "Shadow"; break;
                case "4000": return "Anti-hard"; break;
                case "8000": return "Anti-flying"; break;
            }
            return "none";
        }

        private string readAil(string a)
        {
            switch (a)
            {
                case "00": return "Poison"; break;
                case "01": return "Silence"; break;
                case "02": return "Death"; break;
                case "03": return "Paralysis"; break;
                case "04": return "Confusion"; break;
                case "05": return "Sleep"; break;
                case "06": return "Status 6 (?)"; break;
                case "07": return "Mirror"; break;
            }
            return "none";
        }

        private string readProtection(string a)
        {
            switch (a)
            {
                case "00": return "Weak to Fire"; break;
                case "01": return "Weak to Thunder"; break;
                case "02": return "Weak to Water"; break;
                case "03": return "Weak to Ice"; break;
                case "04": return "Weak to Anti-flying + null earth"; break;
                case "05": return "Weak to Light"; break;
                case "06": return "Strong to Fire"; break;
                case "07": return "Strong to Thunder"; break;
                case "08": return "Strong to Water"; break;
                case "09": return "Strong to Ice"; break;
                case "0A": return "Strong to Light"; break;
                case "0B": return "Strong to Neutral"; break;
                case "0C": return "Weak to Anti-Dragon"; break;
                case "0D": return "Weak to Shadow"; break;
                case "0E": return "Strong against Shadow"; break;
                case "0F": return "Weak to Anti-hard (?)"; break;
                case "10": return "Weak to Anti-insect (?)"; break;
                case "11": return "Protection against status (cept Death, Mirror, Status 6)"; break;
                case "12": return "Becomes undead"; break;
                case "13": return "Core monsters (DMG = DMG/10) (?)"; break;
                case "14": return "Golem monsters (Protection to all 'cept neutral)"; break;
                case "15": return "Protection byte 15 ???"; break;
                case "16": return "Protection byte 16 ???"; break;
                case "17": return "Strong against Anti-hard"; break;
                case "18": return "Protection against certain element (for items)"; break;
                case "19": return "Null certain element (for items)"; break;
                case "1A": return "Mirror certain element (for items)"; break;
                case "1B": return "Protection byte 1B ???"; break;
                case "23": return "Protection byte 23 ???"; break;
                case "24": return "Null all damage"; break;
            }
            return "none";
        }

    }
}
