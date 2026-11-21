using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Ushqyr.Edu
{
    /// <summary>
    /// The Education Suite's local data on this PC (EDU_PLAN F5): the class database, the folder of recordings, the
    /// shipped activities and the student who is flying. Everything lives under the user's data folder; nothing here
    /// opens a network connection.
    /// </summary>
    public static class EduSession
    {
        private static EduDatabase _db;
        private static EduStore _store;
        private static Dictionary<string, ActivityDefinition> _activities;

        public static string Folder => Path.Combine(Application.persistentDataPath, "edu");
        public static string DatabaseFile => Path.Combine(Folder, "class.db");
        public static string ReplayFolder => Path.Combine(Folder, "replays");

        /// <summary>The student at this seat; 0 until someone is chosen.</summary>
        public static long CurrentUserId { get; set; }

        public static EduStore Store
        {
            get
            {
                if (_store != null) return _store;
                Directory.CreateDirectory(Folder);
                _db = new EduDatabase(DatabaseFile);
                _store = new EduStore(_db, ReplayFolder);
                Application.quitting += Close;
                return _store;
            }
        }

        /// <summary>Closes the database file (before a backup is restored, and when the program ends).</summary>
        public static void Close()
        {
            _db?.Dispose();
            _db = null;
            _store = null;
        }

        /// <summary>Every activity shipped with the program, by id. A file that does not pass its checks is reported and left out.</summary>
        public static IReadOnlyDictionary<string, ActivityDefinition> Activities
        {
            get
            {
                if (_activities != null) return _activities;
                _activities = new Dictionary<string, ActivityDefinition>();
                string folder = Path.Combine(Application.streamingAssetsPath, "Edu/Activities");
                if (!Directory.Exists(folder)) return _activities;
                foreach (string file in Directory.GetFiles(folder, "*.json"))
                {
                    try
                    {
                        ActivityDefinition a = ActivityDefinition.Parse(File.ReadAllText(file));
                        _activities[a.Id] = a;
                    }
                    catch (FormatException e)
                    {
                        Debug.LogError("[Edu] " + Path.GetFileName(file) + " was left out: " + e.Message);
                    }
                }
                return _activities;
            }
        }

        /// <summary>
        /// Ends an attempt: grades the recorder's flight against the activity and stores it for the current student.
        /// With nobody chosen yet, a local student named by the seat's computer is used, so a first flight is never lost.
        /// </summary>
        public static AttemptRecord Finish(EduRecorder recorder, ActivityDefinition activity, IDictionary<string, object> flags = null, string program = null)
        {
            if (recorder == null || recorder.Recording == null) throw new ArgumentException("nothing was recorded");
            EduStore store = Store;
            if (CurrentUserId == 0)
            {
                List<EduUser> users = store.Users();
                CurrentUserId = users.Count > 0 ? users[0].Id : store.AddUser(SystemInfo.deviceName);
            }
            return ActivityAttempt.Finish(store, ReplayFolder, activity, recorder.Recording, CurrentUserId, Application.version, flags, program);
        }
    }
}
