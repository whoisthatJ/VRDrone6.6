using Ushqyr.Edu;
using UnityEngine;

namespace Ushqyr.Game
{
    /// <summary>
    /// Joins the class link to the game's scenes: when the teacher starts an activity on this seat, the place it is
    /// flown in is loaded and the activity begins there; when the teacher sends everyone back, the menu is loaded.
    /// </summary>
    public static class ClassroomBridge
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Listen()
        {
            EduClassroom.StartRequested -= Start;
            EduClassroom.StartRequested += Start;
            EduClassroom.LeaveRequested -= Leave;
            EduClassroom.LeaveRequested += Leave;
            EduClassroom.TrackReceived -= TakeTrack;
            EduClassroom.TrackReceived += TakeTrack;
            EduClassroom.RaceRequested -= JoinRace;
            EduClassroom.RaceRequested += JoinRace;
        }

        /// <summary>Joins the LAN race the teacher's PC hosts, under the student's own name.</summary>
        private static void JoinRace(string address)
        {
            Time.timeScale = 1f;
            EduActivityRunner runner = EduActivityRunner.Current;
            if (runner != null) runner.Leave();
            if (Ushqyr.Net.OnlineLobby.JoinDirect(address)) OnlineFlow.PrepareLocalPilot();
            else Debug.LogWarning("[Edu] could not join the teacher's race at " + address + ": " + Ushqyr.Net.OnlineLobby.LastError);
        }

        /// <summary>
        /// Stores a track the teacher sent among this seat's own tracks (the same name again replaces the earlier
        /// one) and, when asked, starts a race on it. Returns the track's id here, or null when it could not be read.
        /// </summary>
        private static string TakeTrack(string name, string json, bool race)
        {
            try
            {
                Ushqyr.Track.TrackData track = Ushqyr.Track.TrackData.FromJson(json);
                if (!string.IsNullOrWhiteSpace(name)) track.Name = name;
                var safe = new System.Text.StringBuilder();
                foreach (char c in track.Name ?? "")
                    if (safe.Length < 40) safe.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-');
                track.Id = Ushqyr.Track.TrackLibrary.UserPrefix + "class-" + (safe.Length > 0 ? safe.ToString() : "track");
                Ushqyr.Track.TrackLibrary.Default.Save(track);
                if (race && GameFlow.Catalog != null)
                {
                    Time.timeScale = 1f;
                    EduActivityRunner.LeaveBlocked = false;
                    GameFlow.Race(GameFlow.Catalog, track.Id);
                }
                return track.Id;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[Edu] the track the teacher sent could not be stored: " + e.Message);
                return null;
            }
        }

        private static void Start(ActivityDefinition activity, Assignment given, bool locked)
        {
            if (GameFlow.Catalog == null)
            {
                Debug.LogWarning("[Edu] the teacher started " + activity.Id + " before this seat knew its places; it was not started");
                return;
            }
            Time.timeScale = 1f;
            EduActivityRunner.LeaveBlocked = false;
            EduActivityRunner.SetPending(activity);
            EduActivityRunner.SetPendingAssignment(given);
            if (GameFlow.Activity(GameFlow.Catalog, activity.Setup.Place)) EduActivityRunner.LeaveBlocked = locked;
            else
            {
                EduActivityRunner.SetPending(null);
                EduActivityRunner.SetPendingAssignment(null);
            }
        }

        private static void Leave()
        {
            Time.timeScale = 1f;
            EduActivityRunner runner = EduActivityRunner.Current;
            if (runner != null) runner.Leave();
        }
    }
}
