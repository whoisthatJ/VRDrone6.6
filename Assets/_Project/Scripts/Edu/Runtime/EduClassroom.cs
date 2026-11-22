using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using TMPro;
using UnityEngine;
using Ushqyr.Core;
using Ushqyr.Vehicle;

namespace Ushqyr.Edu
{
    /// <summary>
    /// The class link inside the program (EDU_PLAN F6, §9). On the teacher's PC it opens the class, stores the
    /// attempts the seats send (graded again here from the recording, never taken on the seat's word), keeps their
    /// latest status and picture, and sends signed commands. On a seat it joins the class, reports status and a
    /// small picture of the simulator's own view a few times a second, sends each finished attempt until the
    /// teacher's PC confirms it, and obeys the teacher's commands. It lives across scenes.
    /// </summary>
    public sealed class EduClassroom : MonoBehaviour
    {
        private static EduClassroom _instance;

        public static EduClassroom Instance
        {
            get
            {
                if (_instance != null) return _instance;
                var go = new GameObject("Edu Classroom");
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<EduClassroom>();
                return _instance;
            }
        }

        /// <summary>True when the class link exists and is in use (it is not made just by asking).</summary>
        public static bool Active => _instance != null && (_instance.Host != null || _instance.Joined || _instance.JoinState == "joining");

        // ================================================================ the teacher's PC

        public ClassHost Host { get; private set; }
        /// <summary>The code of this lesson: seats type it to join. A new one each time the class is opened.</summary>
        public string Code { get; private set; } = "";
        public readonly Dictionary<string, Texture2D> Pictures = new Dictionary<string, Texture2D>();
        /// <summary>Seats that have their hand up.</summary>
        public readonly HashSet<string> Hands = new HashSet<string>();
        /// <summary>Attempts stored from seats since the class was opened.</summary>
        public int Stored { get; private set; }
        /// <summary>Raised when a seat joined, left or changed (for the teacher's screen to draw itself again).</summary>
        public event Action SeatsChanged;
        public string LastProblem { get; private set; } = "";
        /// <summary>The activity last started on every seat; empty when none was (or everyone was sent to the menu).</summary>
        public string StartedOnAll { get; private set; } = "";
        private readonly Dictionary<string, KeyValuePair<double, float>> _progress = new Dictionary<string, KeyValuePair<double, float>>();
        private readonly HashSet<string> _present = new HashSet<string>();

        /// <summary>Seconds a flying seat has gone without finishing a step (0 when it is not flying a guided exercise).</summary>
        public float NoProgressFor(string seat) => _progress.TryGetValue(seat, out KeyValuePair<double, float> p) ? Time.unscaledTime - p.Value : 0f;

        /// <summary>Opens the class on this PC. The class key is made the first time and kept in the class database.</summary>
        public void OpenClass(int port = ClassWire.DefaultPort, int beaconPort = ClassWire.DefaultBeaconPort, bool thisPcOnly = false)
        {
            if (Host != null) return;
            EduStore store = EduSession.Store;
            string saved = store.ClassKey;
            ClassKey key;
            if (string.IsNullOrEmpty(saved))
            {
                key = ClassKey.Create();
                store.ClassKey = key.ExportPrivate();
            }
            else key = ClassKey.Import(saved);
            Code = UnityEngine.Random.Range(100000, 1000000).ToString();
            string name = store.SchoolName.Length > 0 ? store.SchoolName : EduSession.SeatName;
            Host = new ClassHost(name, Code, key);
            Host.Start(port, beaconPort, thisPcOnly);
            Stored = 0;
        }

        public void CloseClass()
        {
            Host?.Dispose();
            Host = null;
            Code = "";
            Hands.Clear();
            foreach (Texture2D t in Pictures.Values) Destroy(t);
            Pictures.Clear();
        }

        /// <summary>Starts an activity on every seat (or one): the activity itself travels with the command.</summary>
        public int StartOn(string seat, ActivityDefinition activity, int attemptLimit = 0, bool locked = false)
        {
            var c = new Dictionary<string, object>
            {
                ["do"] = "start", ["activity"] = Json.Write(activity.Raw), ["attempts"] = (double)attemptLimit, ["lock"] = locked,
            };
            if (string.IsNullOrEmpty(seat)) StartedOnAll = activity.Id;
            return Send(seat, c);
        }

        /// <summary>The names of the teacher's commands.</summary>
        public const string DoPause = "pause", DoResume = "resume", DoRestart = "restart", DoLeave = "leave", DoAttention = "attention", DoMessage = "message",
            DoWeather = "weather", DoFailure = "failure";

        /// <summary>"pause", "resume", "restart", "attention" (with on), "message" (with text), "weather" or "failure" (with name and value), "leave".</summary>
        public int Command(string seat, string what, string name = null, double value = 0, string text = null)
        {
            var c = new Dictionary<string, object> { ["do"] = what, ["value"] = value };
            if (what == DoLeave && string.IsNullOrEmpty(seat)) StartedOnAll = "";
            if (name != null) c["name"] = name;
            if (text != null) c["text"] = text;
            return Send(seat, c);
        }

        private int Send(string seat, Dictionary<string, object> c)
        {
            if (Host == null) return 0;
            return string.IsNullOrEmpty(seat) ? Host.Broadcast(c) : Host.Send(seat, c) ? 1 : 0;
        }

        private void HostUpdate()
        {
            bool changed = false;
            int budget = 200;
            while (budget-- > 0 && Host.Poll(out ClassEvent e))
            {
                if (e.Kind == "picture")
                {
                    if (!Pictures.TryGetValue(e.Seat, out Texture2D t)) Pictures[e.Seat] = t = new Texture2D(2, 2, TextureFormat.RGB24, false);
                    t.LoadImage(e.Bytes, false);
                    continue;
                }
                changed = true;
                if (e.Kind == "hand")
                {
                    if (Json.Flag(e.Data, "up")) Hands.Add(e.Seat);
                    else Hands.Remove(e.Seat);
                }
                else if (e.Kind == "left")
                {
                    Hands.Remove(e.Seat);
                    _progress.Remove(e.Seat);
                }
                else if (e.Kind == "status") NoteStatus(e);
                else if (e.Kind == "joined") SendRoster(e.Seat);
                else if (e.Kind == "attempt") StoreAttempt(e);
            }
            if (changed) SeatsChanged?.Invoke();
        }

        /// <summary>Follows a seat's progress (for the "no progress" alert) and notes the student as present today.</summary>
        private void NoteStatus(ClassEvent e)
        {
            bool flying = Json.Str(e.Data, "state", "") == "flying" && e.Data.ContainsKey("steps");
            double steps = Json.Num(e.Data, "steps", 0);
            if (!flying) _progress.Remove(e.Seat);
            else if (!_progress.TryGetValue(e.Seat, out KeyValuePair<double, float> p) || p.Key != steps)
                _progress[e.Seat] = new KeyValuePair<double, float>(steps, Time.unscaledTime);

            string user = Json.Str(e.Data, "user", "").Trim();
            string day = DateTime.Now.ToString("yyyy-MM-dd");
            if (user.Length == 0 || !_present.Add(day + "|" + user)) return;
            try
            {
                EduStore store = EduSession.Store;
                foreach (EduUser u in store.Users())
                    if (u.Role == "student" && string.Equals(u.Name, user, StringComparison.OrdinalIgnoreCase)) store.MarkPresent(u.Id, day, e.Seat);
            }
            catch (Exception x) { LastProblem = x.Message; }
        }

        /// <summary>The names of the class (no PINs, nothing else), so a seat's "Who is flying?" shows the same list.</summary>
        private void SendRoster(string seat)
        {
            try
            {
                EduStore store = EduSession.Store;
                var groups = new List<object>();
                foreach (EduGroup g in store.Groups())
                {
                    var names = new List<object>();
                    foreach (EduUser u in store.Users(g.Id))
                        if (u.Role == "student") names.Add(u.Name);
                    groups.Add(new Dictionary<string, object> { ["name"] = g.Name, ["students"] = names });
                }
                // What the class has been given, so "My tasks" on a seat shows it.
                var groupNames = new Dictionary<long, string>();
                foreach (EduGroup g in store.Groups()) groupNames[g.Id] = g.Name;
                var tasks = new List<object>();
                foreach (Assignment a in store.Assignments())
                {
                    var t = new Dictionary<string, object>
                    {
                        ["activity"] = a.ActivityId, ["attempts"] = (double)a.AttemptLimit, ["lesson"] = a.Lesson, ["position"] = (double)a.Position, ["group"] = "", ["student"] = "",
                    };
                    if (a.GroupId != null)
                    {
                        if (!groupNames.TryGetValue(a.GroupId.Value, out string group)) continue;
                        t["group"] = group;
                    }
                    else if (a.UserId != null)
                    {
                        EduUser u = store.User(a.UserId.Value);
                        if (u == null || u.Role != "student") continue;
                        t["student"] = u.Name;
                    }
                    tasks.Add(t);
                }
                Host.Send(seat, new Dictionary<string, object> { ["do"] = "roster", ["groups"] = groups, ["tasks"] = tasks });
            }
            catch (Exception e) { LastProblem = e.Message; }
        }

        /// <summary>
        /// Stores an attempt a seat sent. The recording is graded again here; the student is found by name (a name
        /// the class does not have is kept under the seat). A recording already stored is not stored twice.
        /// </summary>
        private void StoreAttempt(ClassEvent e)
        {
            try
            {
                EduStore store = EduSession.Store;
                FlightRecording flight;
                using (var ms = new MemoryStream(e.Bytes)) flight = FlightRecording.Load(ms);
                if (store.HasReplay(flight.Hash())) return;
                string activityId = Json.Str(e.Data, "activity", "");
                if (!EduSession.Activities.TryGetValue(activityId, out ActivityDefinition activity))
                {
                    LastProblem = "unknown activity from " + e.Seat + ": " + activityId;
                    return;
                }
                string user = Json.Str(e.Data, "user", "").Trim();
                long id = 0;
                if (user.Length > 0)
                    foreach (EduUser u in store.Users())
                        if (u.Role == "student" && string.Equals(u.Name, user, StringComparison.OrdinalIgnoreCase)) { id = u.Id; break; }
                if (id == 0) id = store.SeatUser(e.Seat);
                var flags = new Dictionary<string, object> { ["seat"] = e.Seat };
                string ended = Json.Str(e.Data, "ended_by", "");
                if (ended.Length > 0) flags["ended_by"] = ended;
                ActivityAttempt.Finish(store, EduSession.ReplayFolder, activity, flight, id, Json.Str(e.Data, "build", ""), flags);
                Stored++;
            }
            catch (Exception x)
            {
                LastProblem = x.Message;
                Debug.LogWarning("[Edu] an attempt from " + e.Seat + " could not be stored: " + x.Message);
            }
        }

        // ================================================================ a seat

        public ClassSeat Link { get; private set; }
        /// <summary>"" (not joined), "joining", "joined", or why joining failed: "code", "key", "network".</summary>
        public string JoinState { get; private set; } = "";
        public bool Joined => Link != null && Link.Connected;
        public string ClassName { get; private set; } = "";
        public bool Attention { get; private set; }
        public bool Paused { get; private set; }
        public bool HandUp { get; private set; }
        /// <summary>The teacher asked for this activity to be started; the game's own code loads its scene.</summary>
        public static event Action<ActivityDefinition, Assignment, bool> StartRequested;
        /// <summary>The teacher sent everyone back to the menu.</summary>
        public static event Action LeaveRequested;

        private string _address = "", _code = "";
        private int _port;
        private volatile string _joinResult;
        private ClassSeat _joining;
        private float _retryAt, _statusAt;
        private string _message = "";
        private float _messageUntil;
        private Canvas _canvas;
        private RectTransform _overlay;
        private string _overlayShown = "";
        private RenderTexture _full, _small;
        private Coroutine _pictures;

        private static string Outbox => Path.Combine(EduSession.Folder, "outbox");

        /// <summary>Joins a class in the background; watch <see cref="JoinState"/>.</summary>
        public void Join(string address, int port, string code)
        {
            LeaveClass();
            _address = address;
            _port = port;
            _code = code;
            StartJoin();
        }

        private void StartJoin()
        {
            if (JoinState == "joining") return;
            JoinState = "joining";
            _joinResult = null;
            string seatName = EduSession.SeatName, user = CurrentUserName(), address = _address, code = _code;
            int port = _port;
            // The key remembered for a class at this address: a different key there is refused.
            string pinned = null;
            try { pinned = EduSession.Store.Setting("class_key:" + address); } catch (Exception) { /* joined without a pin */ }
            var seat = new ClassSeat();
            _joining = seat;
            new Thread(() =>
            {
                try
                {
                    seat.Connect(address, port, seatName, code, user, pinned);
                    _joinResult = "joined";
                }
                catch (UnauthorizedAccessException e) { _joinResult = e.Message; }
                catch (Exception) { _joinResult = "network"; }
            }) { IsBackground = true, Name = "Class link: join" }.Start();
        }

        public void LeaveClass()
        {
            Link?.Dispose();
            Link = null;
            _joining = null;
            JoinState = "";
            _address = "";
            ClassName = "";
            SetAttention(false);
            SetPaused(false);
            HandUp = false;
            if (_pictures != null) StopCoroutine(_pictures);
            _pictures = null;
        }

        public void RaiseHand(bool up)
        {
            HandUp = up;
            if (Joined) Link.RaiseHand(up);
        }

        private static string CurrentUserName()
        {
            try
            {
                EduUser u = EduSession.CurrentUser;
                return u != null && u.Role == "student" ? u.Name : "";
            }
            catch (Exception) { return ""; }
        }

        /// <summary>
        /// Called when an attempt was stored on this PC: a copy waits in the outbox until the teacher's PC confirms it
        /// has the recording. With no class joined in this run of the program nothing is queued.
        /// </summary>
        public static void AttemptFinished(AttemptRecord attempt, ActivityDefinition activity, string endedBy)
        {
            if (_instance == null || _instance._address.Length == 0 || attempt == null || string.IsNullOrEmpty(attempt.ReplayPath)) return;
            try
            {
                Directory.CreateDirectory(Outbox);
                var record = new Dictionary<string, object>
                {
                    ["activity"] = activity.Id, ["user"] = CurrentUserName(), ["score"] = attempt.Score, ["build"] = attempt.Build ?? "",
                    ["ended_by"] = endedBy ?? "", ["replay"] = attempt.ReplayPath, ["hash"] = "",
                };
                byte[] replay = File.ReadAllBytes(EduSession.Store.ReplayFile(attempt.ReplayPath));
                record["hash"] = ClassWire.Sha256(replay);
                File.WriteAllText(Path.Combine(Outbox, (string)record["hash"] + ".json"), Json.Write(record), new UTF8Encoding(false));
                _instance.SendOutbox();
            }
            catch (Exception e) { Debug.LogWarning("[Edu] the attempt was not queued for the teacher's PC: " + e.Message); }
        }

        /// <summary>Sends everything still waiting in the outbox (after an attempt, and whenever the link comes back).</summary>
        private void SendOutbox()
        {
            if (!Joined || !Directory.Exists(Outbox)) return;
            foreach (string file in Directory.GetFiles(Outbox, "*.json"))
            {
                try
                {
                    string json = File.ReadAllText(file);
                    Dictionary<string, object> record = Json.ParseObject(json);
                    string replay = EduSession.Store.ReplayFile(Json.Str(record, "replay", ""));
                    if (replay == null || !File.Exists(replay))
                    {
                        File.Delete(file); // the recording is gone: nothing to send
                        continue;
                    }
                    if (!Link.SendAttempt(json, File.ReadAllBytes(replay))) return;
                }
                catch (Exception e) { Debug.LogWarning("[Edu] outbox: " + e.Message); }
            }
        }

        private void SeatUpdate()
        {
            if (_joinResult != null)
            {
                string result = _joinResult;
                _joinResult = null;
                if (result == "joined" && _joining != null)
                {
                    Link = _joining;
                    ClassName = Link.ClassName;
                    JoinState = "joined";
                    try { EduSession.Store.SetSetting("class_key:" + _address, Link.KeyFingerprint); } catch (Exception) { /* joined all the same */ }
                    SendOutbox();
                    if (_pictures == null) _pictures = StartCoroutine(PictureLoop());
                }
                else
                {
                    _joining?.Dispose();
                    JoinState = result;
                    // A wrong code or a wrong key is not tried again; a network fault is, below.
                    if (result != "network") _address = "";
                }
                _joining = null;
            }
            if (_address.Length == 0) return;

            // The link dropped (or never came up): try again every few seconds, quietly.
            if (!Joined && JoinState != "joining" && Time.unscaledTime >= _retryAt)
            {
                _retryAt = Time.unscaledTime + 5f;
                Link?.Dispose();
                Link = null;
                StartJoin();
            }
            if (!Joined) return;

            while (Link.Poll(out Dictionary<string, object> c)) Obey(c);
            while (Link.PollStored(out string hash))
            {
                string file = Path.Combine(Outbox, hash + ".json");
                if (File.Exists(file)) File.Delete(file);
            }
            if (Time.unscaledTime >= _statusAt)
            {
                _statusAt = Time.unscaledTime + 0.5f;
                Link.SendStatus(Status());
            }
            if (_message.Length > 0 && Time.unscaledTime > _messageUntil) _message = "";
            ShowOverlay();
        }

        /// <summary>What this seat is doing, for the teacher's grid.</summary>
        public static Dictionary<string, object> Status()
        {
            var s = new Dictionary<string, object> { ["state"] = "menu", ["user"] = CurrentUserName() };
            EduActivityRunner r = EduActivityRunner.Current;
            if (r == null) return s;
            s["activity"] = r.Activity.Id;
            s["state"] = r.State == EduActivityRunner.Phase.Flying ? (r.Crashed ? "crashed" : "flying") : r.State == EduActivityRunner.Phase.Result ? "finished" : "briefing";
            s["clock"] = Math.Round(r.Clock, 1);
            if (r.Steps != null)
            {
                s["steps"] = (double)r.Steps.Done;
                s["of"] = (double)r.Steps.Count;
            }
            if (r.State == EduActivityRunner.Phase.Result && r.Result != null)
            {
                s["score"] = Math.Round(r.Result.Score);
                s["passed"] = r.Result.Passed;
            }
            QuadVehicle v = r.Vehicle;
            if (v != null)
            {
                s["height"] = Math.Round(v.State.Position.y - r.Frame.Y, 1);
                s["speed"] = Math.Round(v.State.Velocity.Magnitude, 1);
            }
            return s;
        }

        private void Obey(Dictionary<string, object> c)
        {
            string what = Json.Str(c, "do", "");
            EduActivityRunner r = EduActivityRunner.Current;
            try
            {
                switch (what)
                {
                    case "start":
                    {
                        ActivityDefinition activity = ActivityDefinition.Parse(Json.Str(c, "activity", ""));
                        int limit = (int)Json.Num(c, "attempts", 0);
                        Assignment given = limit > 0
                            ? new Assignment { ActivityId = activity.Id, AttemptLimit = limit, Created = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ") }
                            : null;
                        SetPaused(false);
                        StartRequested?.Invoke(activity, given, Json.Flag(c, "lock"));
                        break;
                    }
                    case "pause": SetPaused(true); break;
                    case "resume": SetPaused(false); break;
                    case "restart":
                        SetPaused(false);
                        if (r != null) r.TeacherRestart();
                        break;
                    case "leave":
                        SetPaused(false);
                        EduActivityRunner.LeaveBlocked = false;
                        LeaveRequested?.Invoke();
                        break;
                    case "attention": SetAttention(Json.Num(c, "value", 0) > 0); break;
                    case "message":
                        _message = Json.Str(c, "text", "");
                        _messageUntil = Time.unscaledTime + 15f;
                        break;
                    case "weather":
                        if (r != null) r.TeacherWeather(Json.Str(c, "name", ""), (float)Json.Num(c, "value", 0));
                        break;
                    case "failure":
                        if (r != null) r.TeacherFailure(Json.Str(c, "name", ""), (float)Json.Num(c, "value", 0));
                        break;
                    // A PC that is itself holding a class keeps its own lists and tasks.
                    case "roster":
                        if (Host == null) TakeRoster(c);
                        break;
                }
            }
            catch (Exception e) { Debug.LogWarning("[Edu] the teacher's command \"" + what + "\" could not be done: " + e.Message); }
        }

        /// <summary>Adds the class's names to this seat's own list, so a student finds their name here.</summary>
        private static void TakeRoster(Dictionary<string, object> c)
        {
            EduStore store = EduSession.Store;
            List<object> groups = Json.Arr(c, "groups");
            if (groups == null) return;
            foreach (object g in groups)
            {
                if (!(g is Dictionary<string, object> group)) continue;
                string name = Json.Str(group, "name", "").Trim();
                List<object> students = Json.Arr(group, "students");
                if (name.Length == 0 || students == null) continue;
                long id = 0;
                foreach (EduGroup have in store.Groups())
                    if (string.Equals(have.Name, name, StringComparison.OrdinalIgnoreCase)) id = have.Id;
                if (id == 0) id = store.AddGroup(name);
                var lines = new StringBuilder();
                foreach (object s in students)
                    if (s is string student && student.IndexOf('\n') < 0) lines.Append(student).Append('\n');
                store.ImportList(id, lines.ToString());
            }

            // The class's tasks take the place of whatever this seat held before: a seat follows its class.
            List<object> tasks = Json.Arr(c, "tasks");
            if (tasks == null) return;
            foreach (Assignment old in store.Assignments()) store.Unassign(old.Id);
            var groupIds = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            foreach (EduGroup g in store.Groups()) groupIds[g.Name] = g.Id;
            List<EduUser> users = store.Users();
            foreach (object o in tasks)
            {
                if (!(o is Dictionary<string, object> t)) continue;
                if (!EduSession.Activities.TryGetValue(Json.Str(t, "activity", ""), out ActivityDefinition activity)) continue;
                string group = Json.Str(t, "group", ""), student = Json.Str(t, "student", "");
                long? groupId = null, userId = null;
                if (group.Length > 0)
                {
                    if (!groupIds.TryGetValue(group, out long id)) continue;
                    groupId = id;
                }
                else if (student.Length > 0)
                {
                    EduUser u = users.Find(x => x.Role == "student" && string.Equals(x.Name, student, StringComparison.OrdinalIgnoreCase));
                    if (u == null) continue;
                    userId = u.Id;
                }
                int limit = Mathf.Clamp((int)Json.Num(t, "attempts", 0), 0, 99);
                store.Assign(activity, groupId, userId, limit, Json.Str(t, "lesson", ""), (int)Json.Num(t, "position", 0));
            }
        }

        private void SetPaused(bool on)
        {
            if (Paused == on) return;
            Paused = on;
            // A replay being watched has stopped the clock itself; it is left alone.
            if (!Attention && EduReplayViewer.Current == null) Time.timeScale = on ? 0f : 1f;
        }

        private void SetAttention(bool on)
        {
            if (Attention == on) return;
            Attention = on;
            if (EduReplayViewer.Current == null) Time.timeScale = on || Paused ? 0f : 1f;
        }

        // ---------- what the seat shows on top of everything ----------

        private void ShowOverlay()
        {
            string want = Attention ? "attention" : Paused ? "paused" : _message.Length > 0 ? "message:" + _message : HandUp ? "hand" : "";
            if (want == _overlayShown) return;
            _overlayShown = want;
            if (_overlay != null) Destroy(_overlay.gameObject);
            _overlay = null;
            if (want.Length == 0) return;
            if (_canvas == null)
            {
                _canvas = EduUi.Canvas("Class Overlay", 950);
                _canvas.transform.SetParent(transform, false);
            }
            if (Attention)
            {
                // The whole screen: nothing of the simulator shows while the teacher is talking.
                _overlay = EduUi.Panel(_canvas.transform, "Attention", 0f, 0f, 1920f, 1080f, Color.black);
                _overlay.anchorMin = Vector2.zero;
                _overlay.anchorMax = Vector2.one;
                _overlay.offsetMin = _overlay.offsetMax = Vector2.zero;
                TextMeshProUGUI t = EduUi.Text(_overlay, L.T("Look at the teacher"), 0f, 0f, 1920f, 200f, 72f, Color.white, TextAlignmentOptions.Center);
                t.rectTransform.anchorMin = new Vector2(0f, 0.4f);
                t.rectTransform.anchorMax = new Vector2(1f, 0.6f);
                t.rectTransform.offsetMin = t.rectTransform.offsetMax = Vector2.zero;
                return;
            }
            string text = Paused ? L.T("Paused by the teacher") : _message.Length > 0 ? L.T("Teacher") + ":  " + _message : L.T("Hand raised (F2 to lower)");
            _overlay = EduUi.Panel(_canvas.transform, "Note", 0f, 0f, 1100f, 90f, new Color(0.05f, 0.06f, 0.08f, 0.92f));
            _overlay.anchorMin = _overlay.anchorMax = _overlay.pivot = new Vector2(0.5f, 1f);
            _overlay.anchoredPosition = new Vector2(0f, -230f);
            TextMeshProUGUI label = EduUi.Text(_overlay, text, 20f, 0f, 1060f, 90f, 32f, EduUi.Accent, TextAlignmentOptions.Center);
            label.textWrappingMode = TextWrappingModes.Normal;
            label.enableAutoSizing = true;
            label.fontSizeMin = 18f;
            label.fontSizeMax = 32f;
        }

        // ---------- the small picture ----------

        /// <summary>Width of the picture sent to the teacher's grid; the height follows the screen's shape.</summary>
        public const int PictureWidth = 192;
        public const float PicturesPerSecond = 3f;
        /// <summary>What making and sending the pictures costs this seat: milliseconds for the last one, and the average.</summary>
        public float PictureMs { get; private set; }
        public float PictureMsAverage { get; private set; }
        public int PicturesSent { get; private set; }

        /// <summary>
        /// Three times a second: the simulator's own picture as it was drawn (nothing of the desktop can be in it),
        /// made small and sent as a JPEG of a few kilobytes.
        /// </summary>
        private IEnumerator PictureLoop()
        {
            var endOfFrame = new WaitForEndOfFrame();
            float next = 0f;
            while (true)
            {
                yield return endOfFrame;
                if (!Joined || Time.unscaledTime < next || Screen.width < 16 || Screen.height < 16) continue;
                next = Time.unscaledTime + 1f / PicturesPerSecond;
                int h = Mathf.Max(16, PictureWidth * Screen.height / Screen.width);
                if (_full == null || _full.width != Screen.width || _full.height != Screen.height)
                {
                    if (_full != null) _full.Release();
                    _full = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB32);
                }
                if (_small == null || _small.height != h)
                {
                    if (_small != null) _small.Release();
                    _small = new RenderTexture(PictureWidth, h, 0, RenderTextureFormat.ARGB32);
                }
                var cost = System.Diagnostics.Stopwatch.StartNew();
                ScreenCapture.CaptureScreenshotIntoRenderTexture(_full);
                // Where the graphics system keeps pictures upside down, turn it over while making it small.
                if (SystemInfo.graphicsUVStartsAtTop) Graphics.Blit(_full, _small, new Vector2(1f, -1f), new Vector2(0f, 1f));
                else Graphics.Blit(_full, _small);
                // The small picture is fetched from the graphics card without making the frame wait for it.
                int width = PictureWidth, height = h;
                float asked = (float)cost.Elapsed.TotalMilliseconds;
                UnityEngine.Rendering.AsyncGPUReadback.Request(_small, 0, TextureFormat.RGBA32, request =>
                {
                    if (request.hasError || !Joined) return;
                    var encode = System.Diagnostics.Stopwatch.StartNew();
                    using (Unity.Collections.NativeArray<byte> jpg = ImageConversion.EncodeNativeArrayToJPG(request.GetData<byte>(),
                               UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm, (uint)width, (uint)height, 0, 55))
                        Link.SendPicture(jpg.ToArray());
                    PictureMs = asked + (float)encode.Elapsed.TotalMilliseconds;
                    PicturesSent++;
                    PictureMsAverage += (PictureMs - PictureMsAverage) / Mathf.Min(PicturesSent, 100);
                });
            }
        }

        // ================================================================

        private void Update()
        {
            if (Host != null) HostUpdate();
            SeatUpdate();
            if (Joined && UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.f2Key.wasPressedThisFrame) RaiseHand(!HandUp);
        }

        private void OnDestroy()
        {
            CloseClass();
            LeaveClass();
            if (_full != null) _full.Release();
            if (_small != null) _small.Release();
            if (_instance == this) _instance = null;
        }

        private void OnApplicationQuit()
        {
            Host?.Dispose();
            Link?.Dispose();
        }
    }
}
