using System;
using UnityEngine;

namespace Ushqyr.Edu
{
    /// <summary>
    /// A seat that runs itself, for trying the class link without a room full of PCs: started with command-line
    /// options, the program keeps its class data in a folder of its own, calls itself by a seat name, joins a class,
    /// picks a student, and flies whatever the teacher starts with the exercise's own reference flight.
    ///
    /// <para><c>-edu-folder PATH</c> the class data folder of this copy; <c>-edu-seat NAME</c> its seat name;
    /// <c>-edu-join HOST:PORT:CODE</c> the class to join; <c>-edu-user NAME</c> the student to fly as (once the
    /// class list has arrived); <c>-edu-autofly</c> fly every exercise the teacher starts. Without any of these the
    /// program is untouched.</para>
    /// </summary>
    public sealed class EduAutomation : MonoBehaviour
    {
        private string _join, _user;
        private bool _autofly, _joined, _userChosen;
        private float _joinAt = 4f, _lookAt;
        private EduActivityRunner _flown;
        private float _briefingSince;

        private static string Arg(string[] args, string name)
        {
            int i = Array.FindIndex(args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        /// <summary>The folder and the seat name must be set before anything opens the class data.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Early()
        {
            string[] args = Environment.GetCommandLineArgs();
            string folder = Arg(args, "-edu-folder"), seat = Arg(args, "-edu-seat");
            if (!string.IsNullOrEmpty(folder)) EduSession.UseFolder(folder);
            if (!string.IsNullOrEmpty(seat)) EduSession.SeatNameOverride = seat;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init()
        {
            string[] args = Environment.GetCommandLineArgs();
            string join = Arg(args, "-edu-join"), user = Arg(args, "-edu-user");
            bool autofly = Array.Exists(args, a => string.Equals(a, "-edu-autofly", StringComparison.OrdinalIgnoreCase));
            if (join == null && user == null && !autofly) return;
            var go = new GameObject("Edu Automation");
            DontDestroyOnLoad(go);
            var auto = go.AddComponent<EduAutomation>();
            auto._join = join;
            auto._user = user;
            auto._autofly = autofly;
            // A seat that nobody sits at must keep running when its window is not in front.
            Application.runInBackground = true;
            Debug.Log("[EduAuto] seat " + EduSession.SeatName + ", data in " + EduSession.Folder + (join != null ? ", joining " + join.Substring(0, join.LastIndexOf(':') + 1) + "…" : ""));
        }

        private void Update()
        {
            if (_join != null && !_joined && Time.realtimeSinceStartup >= _joinAt)
            {
                _joined = true;
                string[] parts = _join.Split(':');
                if (parts.Length == 3 && int.TryParse(parts[1], out int port)) EduClassroom.Instance.Join(parts[0], port, parts[2]);
                else Debug.LogWarning("[EduAuto] -edu-join wants HOST:PORT:CODE");
            }

            if (Time.unscaledTime < _lookAt) return;
            _lookAt = Time.unscaledTime + 0.5f;

            // The student to fly as: found by name once the class list is here.
            if (_user != null && !_userChosen)
            {
                try
                {
                    foreach (EduUser u in EduSession.Store.Users())
                        if (u.Role == "student" && string.Equals(u.Name, _user, StringComparison.OrdinalIgnoreCase))
                        {
                            EduSession.CurrentUserId = u.Id;
                            _userChosen = true;
                            Debug.Log("[EduAuto] flying as " + u.Name);
                        }
                }
                catch (Exception e) { Debug.LogWarning("[EduAuto] " + e.Message); }
            }

            if (!_autofly) return;
            EduActivityRunner r = EduActivityRunner.Current;
            if (r == null || r == _flown || r.Watching) { _briefingSince = 0f; return; }
            if (r.State != EduActivityRunner.Phase.Briefing) return;
            // A moment on the briefing first, as a person would take (and for the scene to settle).
            if (_briefingSince == 0f) _briefingSince = Time.unscaledTime;
            if (Time.unscaledTime - _briefingSince < 2f || Time.timeScale == 0f) return;
            _flown = r;
            EduReferencePilot pilot = EduReferencePilot.Fly(r, r.Vehicle);
            Debug.Log("[EduAuto] " + (pilot != null ? "flying " : "no reference flight for ") + r.Activity.Id);
        }
    }
}
