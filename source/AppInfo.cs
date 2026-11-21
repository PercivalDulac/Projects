using System;
using System.IO;
using System.Windows.Forms;

namespace com.lufiaIIEditor.RomInfo
{
    /// <summary>Name, version, credits, and the files that live next to the exe.</summary>
    static class AppInfo
    {
        public const string Name = "Dual Blade Editor";
        public const string Version = "0.9";
        public const string OriginalBy = "zanto";
        public const string OriginalUrl = "https://www.romhacking.net/utilities/1066/";
        public const string AddedBy = "Percival";
        public const string FixesBy = "Relnqshd's Lufia II Fixxxer Deluxe and Artea name patch, and those who helped him";

        public static string Title { get { return Name + " " + Version; } }

        /// <summary>
        /// A file that ships beside the exe: the three offset lists and the working ROM copy.
        /// These used to be opened by bare name, which Windows resolves against the working
        /// directory, not the program's folder. That breaks for a shortcut with a different
        /// "Start in" folder, and for Run as administrator, which can set the working directory
        /// to System32.
        /// </summary>
        public static string FileBesideExe(string fileName)
        {
            return Path.Combine(Application.StartupPath, fileName);
        }
    }
}
