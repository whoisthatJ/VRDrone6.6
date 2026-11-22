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

        private static string _folder;

        public static string Folder => _folder ?? Path.Combine(Application.persistentDataPath, "edu");

        /// <summary>
        /// Keeps the data somewhere else from now on (a check that must not touch the real class data; later, a
        /// folder the school chose). Null goes back to the user's data folder. Nobody is chosen afterwards.
        /// </summary>
        public static void UseFolder(string folder)
        {
            Close();
            _folder = string.IsNullOrEmpty(folder) ? null : folder;
            CurrentUserId = 0;
        }
        public static string DatabaseFile => Path.Combine(Folder, "class.db");
        public static string ReplayFolder => Path.Combine(Folder, "replays");

        /// <summary>The student at this seat; 0 until someone is chosen.</summary>
        public static long CurrentUserId { get; set; }

        /// <summary>The profile that is flying, or null when nobody was chosen (or the chosen one was deleted).</summary>
        public static EduUser CurrentUser
        {
            get
            {
                if (CurrentUserId == 0) return null;
                EduUser user = Store.User(CurrentUserId);
                if (user == null) CurrentUserId = 0;
                return user;
            }
        }

        /// <summary>
        /// The teacher opened every block of the course (EDU_PLAN §4). Kept in the class data, so it holds for the
        /// lesson and after a restart.
        /// </summary>
        public static bool EverythingUnlocked
        {
            get => Store != null && _db.Setting("unlock_all") == "1";
            set { if (Store != null) _db.SetSetting("unlock_all", value ? "1" : "0"); }
        }

        /// <summary>The name of this place when nobody is named: the computer's own name.</summary>
        public static string SeatName => string.IsNullOrEmpty(SeatNameOverride) ? SystemInfo.deviceName : SeatNameOverride;

        /// <summary>Another name for this seat than the computer's (several copies of the program on one PC, each a seat).</summary>
        public static string SeatNameOverride { get; set; }

        /// <summary>Flies as the seat, with no person named.</summary>
        public static void UseSeat() => CurrentUserId = Store.SeatUser(SeatName);

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

        /// <summary>Reads the shipped activities again the next time they are asked for (their files were changed).</summary>
        public static void ReloadActivities() => _activities = null;

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
        /// The reference flight shipped with an activity (the demo before it, the flight to compare with after it):
        /// the file with the activity's id beside its definition. Null when the activity has none or it cannot be read.
        /// </summary>
        public static FlightRecording Reference(ActivityDefinition activity)
        {
            if (activity == null) return null;
            string file = Path.Combine(Application.streamingAssetsPath, "Edu/Activities", activity.Id + ".grreplay");
            if (!File.Exists(file)) return null;
            try { return FlightRecording.Load(file); }
            catch (InvalidDataException e)
            {
                Debug.LogError("[Edu] reference flight of " + activity.Id + ": " + e.Message);
                return null;
            }
        }

        /// <summary>
        /// Ends an attempt: grades the recorder's flight against the activity and stores it for the current student.
        /// With nobody chosen yet, the seat's own profile is used, so a first flight is never lost.
        /// </summary>
        public static AttemptRecord Finish(EduRecorder recorder, ActivityDefinition activity, IDictionary<string, object> flags = null, string program = null)
        {
            if (recorder == null || recorder.Recording == null) throw new ArgumentException("nothing was recorded");
            EduStore store = Store;
            // Nobody chosen: the flight is kept under the seat's name, never under some student's.
            if (CurrentUser == null) UseSeat();
            return ActivityAttempt.Finish(store, ReplayFolder, activity, recorder.Recording, CurrentUserId, Application.version, flags, program);
        }
    }
}
