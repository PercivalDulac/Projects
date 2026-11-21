using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace com.lufiaIIEditor.RomInfo
{
    /// <summary>
    /// Keeps copies of the ROM as it was before each save, in a "Lufia2Editor backups" folder next
    /// to it. Saving overwrites the ROM in place, so without these there is no way back from a bad
    /// edit. The newest Keep backups of each ROM are kept; older ones are deleted.
    /// </summary>
    static class BackupTools
    {
        public const int Keep = 10;
        public const string FolderName = "Lufia2Editor backups";

        public static string FolderFor(string romPath)
        {
            return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(romPath)), FolderName);
        }

        /// <summary>Does this file name look like "&lt;rom&gt; yyyy-MM-dd ..."?</summary>
        private static bool IsBackupOf(string fileName, string romBase)
        {
            if (fileName.Length < romBase.Length + 6) return false;
            if (!fileName.StartsWith(romBase + " ", StringComparison.OrdinalIgnoreCase)) return false;
            int at = romBase.Length + 1;
            for (int k = 0; k < 4; k++) if (!char.IsDigit(fileName[at + k])) return false;
            return fileName[at + 4] == '-';
        }

        /// <summary>Backups of this ROM, newest first.</summary>
        public static List<FileInfo> List(string romPath)
        {
            List<FileInfo> l = new List<FileInfo>();
            try
            {
                string folder = FolderFor(romPath);
                if (!Directory.Exists(folder)) return l;
                string romBase = Path.GetFileNameWithoutExtension(romPath);
                foreach (string f in Directory.GetFiles(folder))
                {
                    FileInfo fi = new FileInfo(f);
                    if (IsBackupOf(fi.Name, romBase)) l.Add(fi);
                }
                l.Sort(delegate(FileInfo a, FileInfo b) { return b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc); });
            }
            catch (Exception) { }
            return l;
        }

        /// <summary>
        /// Copies the ROM as it is on disk into the backups folder. Returns the copy's path, or null
        /// (error stays null when there is simply nothing to copy yet).
        /// </summary>
        public static string Make(string romPath, string label, out string error)
        {
            if (string.IsNullOrEmpty(romPath) || !File.Exists(romPath)) { error = null; return null; }
            return Write(romPath, null, label, out error);
        }

        /// <summary>The same, but storing bytes the editor holds (including unsaved edits).</summary>
        public static string MakeFrom(string romPath, byte[] data, string label, out string error)
        {
            return Write(romPath, data, label, out error);
        }

        private static string Write(string romPath, byte[] data, string label, out string error)
        {
            error = null;
            try
            {
                string folder = FolderFor(romPath);
                Directory.CreateDirectory(folder);
                string stamp = DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss", CultureInfo.InvariantCulture);
                string ext = Path.GetExtension(romPath);
                string name = Path.GetFileNameWithoutExtension(romPath) + " " + stamp +
                    (string.IsNullOrEmpty(label) ? "" : " (" + label + ")");
                string path = Path.Combine(folder, name + ext);
                for (int k = 2; File.Exists(path) && k < 100; k++) path = Path.Combine(folder, name + " " + k + ext);
                if (data == null) File.Copy(romPath, path); else File.WriteAllBytes(path, data);
                Prune(romPath);
                return path;
            }
            catch (Exception x) { error = x.Message; return null; }
        }

        private static void Prune(string romPath)
        {
            List<FileInfo> l = List(romPath);
            for (int i = Keep; i < l.Count; i++)
            {
                try { l[i].Delete(); }
                catch (Exception) { }
            }
        }

        /// <summary>"2026-10-28 15:23:45   3.0 MB   (before expanding)" for the restore list.</summary>
        public static string Describe(FileInfo fi)
        {
            string name = Path.GetFileNameWithoutExtension(fi.Name);
            string label = "";
            int open = name.LastIndexOf('(');
            if (open > 0 && name.EndsWith(")")) label = name.Substring(open);
            string when = fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            return when + "    " + (fi.Length / 1048576.0).ToString("0.0") + " MB" + (label.Length > 0 ? "    " + label : "");
        }
    }
}
