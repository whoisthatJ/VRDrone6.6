using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using Ushqyr.Core;

namespace Ushqyr.Edu
{
    /// <summary>
    /// What the teacher's console does with files (EDU_PLAN §9): writes the reports and the backups into the class
    /// data folder on this PC, and puts a backup back. Nothing leaves the PC.
    /// </summary>
    public static class EduTeacher
    {
        public static string ReportsFolder => Path.Combine(EduSession.Folder, "reports");
        public static string BackupFolder => Path.Combine(EduSession.Folder, "backups");

        /// <summary>The journal of a group, or of everyone, over the exercises of the course.</summary>
        public static Journal ReadJournal(long? groupId) => Journal.Build(EduSession.Store, EduSession.Activities.Values, groupId);

        private static byte[] Font() => File.ReadAllBytes(Path.Combine(Application.streamingAssetsPath, "Edu/LiberationSans-Regular.ttf"));

        private static string Stamp => DateTime.Now.ToString("yyyyMMdd-HHmm");

        /// <summary>A name made safe for a file: letters and digits kept, everything else a dash, at most 40 characters.</summary>
        public static string Safe(string name)
        {
            var b = new StringBuilder();
            foreach (char c in name ?? "")
                if (b.Length < 40) b.Append(char.IsLetterOrDigit(c) ? c : '-');
            return b.Length > 0 ? b.ToString() : "all";
        }

        /// <summary>The path itself when nothing is there yet, otherwise the same name with -2, -3 …</summary>
        private static string Free(string path)
        {
            if (!File.Exists(path)) return path;
            string stem = Path.Combine(Path.GetDirectoryName(path) ?? "", Path.GetFileNameWithoutExtension(path)), ext = Path.GetExtension(path);
            for (int i = 2; ; i++)
                if (!File.Exists(stem + "-" + i + ext)) return stem + "-" + i + ext;
        }

        /// <summary>Writes the group's gradebook as XLSX or CSV and returns the file.</summary>
        public static string SaveGradebook(Journal j, bool csv)
        {
            Directory.CreateDirectory(ReportsFolder);
            string file = Free(Path.Combine(ReportsFolder, "gradebook-" + Safe(j.Group) + "-" + Stamp + (csv ? ".csv" : ".xlsx")));
            // UTF-8 with a byte-order mark: a spreadsheet then reads the Cyrillic letters correctly.
            if (csv) File.WriteAllText(file, Reports.GradebookCsv(j, L.T), new UTF8Encoding(true));
            else Reports.Gradebook(j, L.T, EduSession.Store.SchoolName, DateTime.Now, EduSession.Store).Save(file);
            return file;
        }

        /// <summary>Writes one student's progress report as a PDF and returns the file.</summary>
        public static string SaveProgress(Journal j, JournalRow row)
        {
            Directory.CreateDirectory(ReportsFolder);
            string file = Free(Path.Combine(ReportsFolder, "progress-" + Safe(row.User.Name) + "-" + Stamp + ".pdf"));
            Reports.StudentProgress(j, row, Font(), L.T, EduSession.Store.SchoolName, DateTime.Now).Save(file);
            return file;
        }

        /// <summary>Writes a progress report for every student of the journal into one new folder; returns how many.</summary>
        public static int SaveAllProgress(Journal j, out string folder)
        {
            folder = Path.Combine(ReportsFolder, "progress-" + Safe(j.Group) + "-" + Stamp);
            Directory.CreateDirectory(folder);
            byte[] font = Font();
            string school = EduSession.Store.SchoolName;
            int n = 0;
            foreach (JournalRow row in j.Rows)
            {
                Reports.StudentProgress(j, row, font, L.T, school, DateTime.Now).Save(Free(Path.Combine(folder, Safe(row.User.Name) + ".pdf")));
                n++;
            }
            return n;
        }

        /// <summary>Writes a tournament's result sheet and returns the file: 0 the PDF with the bracket, 1 the XLSX, 2 the CSV.</summary>
        public static string SaveRaceSheet(Tournament t, int kind)
        {
            Directory.CreateDirectory(ReportsFolder);
            string stem = Path.Combine(ReportsFolder, "tournament-" + Safe(t.Name) + "-" + Stamp);
            string school = EduSession.Store.SchoolName;
            if (kind == 0)
            {
                string pdf = Free(stem + ".pdf");
                RaceReports.Pdf(t, Font(), L.T, school, DateTime.Now).Save(pdf);
                return pdf;
            }
            if (kind == 1)
            {
                string xlsx = Free(stem + ".xlsx");
                RaceReports.Workbook(t, L.T, school, DateTime.Now).Save(xlsx);
                return xlsx;
            }
            string csv = Free(stem + ".csv");
            File.WriteAllText(csv, RaceReports.Csv(t, L.T), new UTF8Encoding(true));
            return csv;
        }

        /// <summary>Copies the class database into the backups folder and returns the file.</summary>
        public static string Backup(string prefix = "class")
        {
            Directory.CreateDirectory(BackupFolder);
            string file = Free(Path.Combine(BackupFolder, prefix + "-" + Stamp + ".db"));
            EduSession.Store.Backup(file);
            return file;
        }

        /// <summary>The backups on this PC, newest first.</summary>
        public static List<string> Backups()
        {
            var list = new List<string>();
            if (Directory.Exists(BackupFolder)) list.AddRange(Directory.GetFiles(BackupFolder, "*.db"));
            list.Sort((a, b) => File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a)));
            return list;
        }

        /// <summary>
        /// Puts a backup in place of the class database. The file is checked first, and the database being replaced is
        /// itself saved as a backup ("before-restore-…"), so the step can be undone. Recordings of flights are files
        /// beside the database and are not touched. Returns the backup made of the replaced database.
        /// </summary>
        public static string Restore(string file)
        {
            EduStore.CheckBackup(file);
            string kept = Backup("before-restore");
            EduSession.Close();
            File.Copy(file, EduSession.DatabaseFile, true);
            foreach (string side in new[] { "-wal", "-shm", "-journal" })
                if (File.Exists(EduSession.DatabaseFile + side)) File.Delete(EduSession.DatabaseFile + side);
            // The profiles of the restored class are other rows: nobody is chosen until someone picks a name again.
            EduSession.CurrentUserId = 0;
            return kept;
        }

        /// <summary>Opens a file or a folder with whatever this PC uses for it.</summary>
        public static void Open(string path) => Application.OpenURL(new Uri(path).AbsoluteUri);
    }
}
