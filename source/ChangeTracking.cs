using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

using com.lufiaIIEditor.RomInfo;

namespace com.lufiaIIEditor.UI
{
    /// <summary>
    /// The ROM that edits are compared against: a clean (unmodified) ROM chosen once by the
    /// user and remembered in LufiaEditorCleanRom.txt next to the exe, or - when none is set -
    /// a copy of the ROM as it was when it was opened.
    /// </summary>
    static class Reference
    {
        private const string SettingsFile = "LufiaEditorCleanRom.txt";
        private const string InternalName = "Lufia II(Estpolis II)";

        private static byte[] data;
        private static bool hasHeader;
        private static bool currentHasHeader;
        private static string cleanPath;       // null when comparing against the opened ROM
        private static ItemData items;
        private static IPData ip;
        private static CapsuleMonsterData capsules;
        private static CharacterData characters;
        private static ShopData shops;

        public static bool Available { get { return data != null; } }
        public static bool IsCleanRom { get { return cleanPath != null; } }
        public static string CleanPath { get { return cleanPath; } }
        public static byte[] Data { get { return data; } }
        public static bool HasHeader { get { return hasHeader; } }

        private static string SettingsPath
        {
            get { return Path.Combine(Application.StartupPath, SettingsFile); }
        }

        /// <summary>Call after a ROM is opened. Loads the remembered clean ROM, or snapshots the opened one.</summary>
        public static void Init(byte[] currentData, bool currentHeader)
        {
            currentHasHeader = currentHeader;
            string remembered = null;
            try
            {
                if (File.Exists(SettingsPath)) remembered = File.ReadAllText(SettingsPath).Trim();
            }
            catch (Exception) { remembered = null; }

            if (!string.IsNullOrEmpty(remembered) && TryLoad(remembered) == null) return;
            UseSnapshot(currentData);
        }

        private static void UseSnapshot(byte[] currentData)
        {
            data = (byte[])currentData.Clone();
            hasHeader = currentHasHeader;
            cleanPath = null;
            ResetCaches();
        }

        /// <summary>Loads a clean ROM. Returns null on success, otherwise the reason it was rejected.</summary>
        public static string TryLoad(string path)
        {
            byte[] bytes;
            try { bytes = File.ReadAllBytes(path); }
            catch (Exception ex) { return "Couldn't read the file: " + ex.Message; }

            bool header = (bytes.Length & 0x7FFF) == 0x200;
            int nameOfs = (header ? 0x200 : 0) + 0x7FC0;
            if (bytes.Length < nameOfs + InternalName.Length) return "The file is too small to be a Lufia II ROM.";
            string name = Encoding.ASCII.GetString(bytes, nameOfs, InternalName.Length);
            if (name != InternalName) return "This doesn't look like a Lufia II ROM (internal name is \"" + name.Trim() + "\").";

            data = bytes;
            hasHeader = header;
            cleanPath = path;
            ResetCaches();
            return null;
        }

        /// <summary>Remembers (or forgets, with null) the clean ROM path for next time.</summary>
        public static void Remember(string path)
        {
            try
            {
                if (path == null) { if (File.Exists(SettingsPath)) File.Delete(SettingsPath); }
                else File.WriteAllText(SettingsPath, path);
            }
            catch (Exception) { /* not fatal: it just won't be remembered */ }
        }

        public static void Forget(byte[] currentData)
        {
            Remember(null);
            UseSnapshot(currentData);
        }

        private static void ResetCaches() { items = null; ip = null; capsules = null; characters = null; shops = null; }

        /// <summary>Converts a file offset in the opened ROM to the same place in the reference ROM.</summary>
        public static int Shift(int offset)
        {
            return offset + (hasHeader ? 0x200 : 0) - (currentHasHeader ? 0x200 : 0);
        }

        public static bool InRange(int offset, int size)
        {
            return data != null && offset >= 0 && offset + size <= data.Length;
        }

        public static int Byte(int currentOffset)
        {
            int o = Shift(currentOffset);
            return InRange(o, 1) ? data[o] : -1;
        }

        public static ItemData Items
        {
            get
            {
                if (items == null && data != null)
                {
                    try { ItemData d = new ItemData(data, hasHeader); if (d.Validate() == null) items = d; }
                    catch (Exception) { items = null; }
                }
                return items;
            }
        }

        public static IPData IP
        {
            get
            {
                if (ip == null && data != null)
                {
                    try { IPData d = new IPData(data, hasHeader); if (d.Validate() == null) ip = d; }
                    catch (Exception) { ip = null; }
                }
                return ip;
            }
        }

        public static CapsuleMonsterData Capsules
        {
            get
            {
                if (capsules == null && data != null)
                {
                    try { CapsuleMonsterData d = new CapsuleMonsterData(data, hasHeader); if (d.Validate() == null) capsules = d; }
                    catch (Exception) { capsules = null; }
                }
                return capsules;
            }
        }

        public static CharacterData Characters
        {
            get
            {
                if (characters == null && data != null)
                {
                    try { CharacterData d = new CharacterData(data, hasHeader); if (d.Validate() == null) characters = d; }
                    catch (Exception) { characters = null; }
                }
                return characters;
            }
        }

        public static ShopData Shops
        {
            get
            {
                if (shops == null && data != null)
                {
                    try { ShopData d = new ShopData(data, hasHeader); if (d.Load() == null) shops = d; }
                    catch (Exception) { shops = null; }
                }
                return shops;
            }
        }

        /// <summary>Runs a lookup in the reference ROM; any failure just means "original unknown".</summary>
        public static object Try(Func<object> lookup)
        {
            if (data == null) return null;
            try { return lookup(); }
            catch (Exception) { return null; }
        }
    }

    /// <summary>
    /// Colors a field blue while its value differs from the reference ROM, and gives it a
    /// right-click "Restore original" item. Call Set(control, original) whenever a field is
    /// filled in; null means "original unknown" (no highlight).
    /// </summary>
    static class ChangeHighlighter
    {
        public static readonly Color ChangedColor = Color.FromArgb(185, 215, 255);

        private static Dictionary<Control, object> originals = new Dictionary<Control, object>();
        private static Dictionary<Control, Color> normalColors = new Dictionary<Control, Color>();

        public static void Set(Control c, object original)
        {
            if (c == null) return;
            if (!normalColors.ContainsKey(c))
            {
                normalColors[c] = c.BackColor;
                Hook(c);
            }
            if (original == null) originals.Remove(c);
            else originals[c] = Normalize(c, original);
            Refresh(c);
        }

        private static void Hook(Control c)
        {
            EventHandler h = new EventHandler(ValueChanged);
            NumericUpDown n = c as NumericUpDown;
            ComboBox cb = c as ComboBox;
            CheckBox chk = c as CheckBox;
            TextBox t = c as TextBox;
            if (n != null) n.ValueChanged += h;
            else if (cb != null) cb.SelectedIndexChanged += h;
            else if (chk != null) chk.CheckedChanged += h;
            else if (t != null) t.TextChanged += h;

            ContextMenuStrip menu = new ContextMenuStrip();
            ToolStripMenuItem restore = new ToolStripMenuItem("Restore original");
            restore.Tag = c;
            restore.Click += new EventHandler(Restore_Click);
            menu.Items.Add(restore);
            menu.Opening += new System.ComponentModel.CancelEventHandler(Menu_Opening);
            menu.Tag = restore;
            if (t != null)
            {
                // Keep the usual text commands too.
                AddTextCommand(menu, "Cut", t, 0);
                AddTextCommand(menu, "Copy", t, 1);
                AddTextCommand(menu, "Paste", t, 2);
            }
            c.ContextMenuStrip = menu;
            c.Disposed += new EventHandler(Control_Disposed);
        }

        private static void AddTextCommand(ContextMenuStrip menu, string text, TextBox t, int which)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text);
            item.Click += delegate(object s, EventArgs e)
            {
                if (which == 0) t.Cut(); else if (which == 1) t.Copy(); else t.Paste();
            };
            if (menu.Items.Count == 1) menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(item);
        }

        private static void Menu_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            ContextMenuStrip menu = (ContextMenuStrip)sender;
            ToolStripMenuItem restore = (ToolStripMenuItem)menu.Tag;
            Control c = (Control)restore.Tag;
            object orig;
            bool known = originals.TryGetValue(c, out orig);
            restore.Enabled = known && IsChanged(c) && c.Enabled;
            restore.Text = known ? "Restore original (" + Show(c, orig) + ")" : "Restore original (unknown)";
        }

        private static void Restore_Click(object sender, EventArgs e)
        {
            Control c = (Control)((ToolStripMenuItem)sender).Tag;
            object orig;
            if (!originals.TryGetValue(c, out orig)) return;
            NumericUpDown n = c as NumericUpDown;
            ComboBox cb = c as ComboBox;
            CheckBox chk = c as CheckBox;
            TextBox t = c as TextBox;
            if (n != null) UiHelpers.SetValueClamped(n, (int)orig);
            else if (cb != null) { int i = (int)orig; if (i >= 0 && i < cb.Items.Count) cb.SelectedIndex = i; }
            else if (chk != null) chk.Checked = (bool)orig;
            else if (t != null) t.Text = (string)orig;
        }

        private static void Control_Disposed(object sender, EventArgs e)
        {
            Control c = (Control)sender;
            originals.Remove(c);
            normalColors.Remove(c);
        }

        private static void ValueChanged(object sender, EventArgs e)
        {
            Refresh((Control)sender);
        }

        private static object Normalize(Control c, object v)
        {
            if (c is TextBox) return ((string)v).Trim();
            return v;
        }

        private static object Current(Control c)
        {
            NumericUpDown n = c as NumericUpDown;
            if (n != null) return (int)n.Value;
            ComboBox cb = c as ComboBox;
            if (cb != null) return cb.SelectedIndex;
            CheckBox chk = c as CheckBox;
            if (chk != null) return chk.Checked;
            TextBox t = c as TextBox;
            if (t != null) return t.Text.Trim();
            return null;
        }

        private static string Show(Control c, object v)
        {
            ComboBox cb = c as ComboBox;
            if (cb != null)
            {
                int i = (int)v;
                return (i >= 0 && i < cb.Items.Count) ? cb.Items[i].ToString().Trim() : "?";
            }
            if (v is bool) return (bool)v ? "on" : "off";
            return v.ToString();
        }

        public static bool IsChanged(Control c)
        {
            object orig;
            if (!originals.TryGetValue(c, out orig)) return false;
            object cur = Current(c);
            return cur != null && !cur.Equals(orig);
        }

        private static void Refresh(Control c)
        {
            bool changed = IsChanged(c);
            Color normal;
            if (!normalColors.TryGetValue(c, out normal)) normal = c.BackColor;
            ComboBox cb = c as ComboBox;
            if (cb != null)
            {
                // A themed drop-down list ignores BackColor, so switch to the flat style while highlighted.
                cb.FlatStyle = changed ? FlatStyle.Flat : FlatStyle.Standard;
            }
            c.BackColor = changed ? ChangedColor : normal;
        }
    }
}
