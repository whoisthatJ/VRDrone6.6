using System;
using System.IO;
using UnityEngine;
using Ushqyr.Core;

namespace Ushqyr.Edu
{
    /// <summary>
    /// Saves a student's course certificate as a PDF in the class data folder on this PC and opens it in whatever the
    /// PC reads PDFs with, to be printed from there. Nothing is sent anywhere.
    /// </summary>
    public static class EduCertificates
    {
        public static string Folder => Path.Combine(EduSession.Folder, "certificates");

        /// <summary>Writes the certificate of the course's student and returns the file; throws when it is not earned.</summary>
        public static string Save(Course course, EduUser user)
        {
            // A seat profile names a PC, not a person: its certificate has an empty line for the name.
            string name = user == null || user.Role == "seat" ? "" : user.Name;
            Certificate c = Certificate.From(course, name, DateTime.Now);
            if (c == null) throw new InvalidOperationException("the check ride is not passed yet");
            byte[] font = File.ReadAllBytes(Path.Combine(Application.streamingAssetsPath, "Edu/LiberationSans-Regular.ttf"));
            Directory.CreateDirectory(Folder);
            string file = Path.Combine(Folder, "certificate-" + (user != null ? user.Id : 0) + "-" + c.Date.ToString("yyyyMMdd") + ".pdf");
            c.Write(font, L.T).Save(file);
            return file;
        }

        /// <summary>Opens the saved file with the PC's own PDF reader (or browser).</summary>
        public static void Open(string file) => Application.OpenURL(new Uri(file).AbsoluteUri);
    }
}
