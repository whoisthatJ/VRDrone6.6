using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using Ushqyr.Core;
using Ushqyr.Flight;
using Ushqyr.Vehicle;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Ushqyr.Edu
{
    /// <summary>
    /// Runs one activity from briefing to result (EDU_PLAN §4): shows what to do, lays the activity's objects out in
    /// the scene, starts the recording when the pilot is ready, applies the weather and the failures of its timeline,
    /// ends the attempt (landing, crash, time limit or the pilot's own "finish"), grades the recording and shows the
    /// score with its breakdown and tips. The grade comes from the recording alone.
    /// <para>Space or Enter starts; Enter finishes early; after the result R flies again, L replays (with the
    /// reference flight beside it), Esc leaves.</para>
    /// </summary>
    public sealed class EduActivityRunner : MonoBehaviour
    {
        public enum Phase { Briefing, Flying, Result }

        private static ActivityDefinition _pending;

        /// <summary>The activity the next flying scene should run (set by the training hub before the scene loads).</summary>
        public static void SetPending(ActivityDefinition activity) => _pending = activity;

        public static ActivityDefinition TakePending()
        {
            ActivityDefinition a = _pending;
            _pending = null;
            return a;
        }

        private static AttemptRecord _pendingReplay;
        private static Assignment _pendingAssignment;
        private Assignment _assignment;

        /// <summary>The assignment the next exercise is flown for (set with its activity): its attempt limit is kept.</summary>
        public static void SetPendingAssignment(Assignment assignment) => _pendingAssignment = assignment;

        /// <summary>
        /// Attempts left on the assignment this exercise is flown for; -1 when there is no limit (or no assignment).
        /// </summary>
        public int AttemptsLeft
        {
            get
            {
                if (_assignment == null || _assignment.AttemptLimit <= 0) return -1;
                try { return Mathf.Max(0, _assignment.AttemptLimit - EduSession.Store.AttemptsUsed(_assignment, EduSession.CurrentUserId)); }
                catch (Exception) { return -1; }
            }
        }

        /// <summary>
        /// A stored attempt to show instead of flying (set by the replay library together with its activity): the
        /// scene is laid out as for the exercise, the replay plays, and closing it leaves.
        /// </summary>
        public static void SetPendingReplay(AttemptRecord attempt) => _pendingReplay = attempt;

        public static EduActivityRunner Current { get; private set; }

        /// <summary>True while a replay or the demonstration is on the screen.</summary>
        public bool Watching { get; private set; }

        private QuadVehicle _vehicle;
        private EduFlight _flight;
        private EduRecorder _recorder;
        private EduFreeFlight _free;
        private Action _leave;
        private Transform _start, _objects, _oldSpawn;
        private FlightFrame _frame;
        private WeatherSettings _weather;
        private int _timelineNext;
        private float _clock, _groundedFor, _highest;
        private bool _crashed;
        private Canvas _canvas;
        private RectTransform _panel;
        private TextMeshProUGUI _timer, _prompt;
        private RectTransform _promptBar;
        private StepTracker _steps;
        private int _fed, _fedEvents, _stepsSeen;
        private float _stepDoneAt;
        private readonly HashSet<TimedChange> _applied = new HashSet<TimedChange>();
        private float _stepsDoneFor;
        private readonly List<UnityEngine.Object> _made = new List<UnityEngine.Object>();

        public ActivityDefinition Activity { get; private set; }
        public Phase State { get; private set; }
        public ScoreResult Result { get; private set; }
        public AttemptRecord Attempt { get; private set; }
        public FlightFrame Frame => _frame;
        /// <summary>Why the last attempt ended: "landed", "steps", "crash", "time" or "pilot".</summary>
        public string EndedBy { get; private set; } = "";
        public float Clock => _clock;
        public QuadVehicle Vehicle => _vehicle;
        /// <summary>True once the drone has crashed in the attempt being flown.</summary>
        public bool Crashed => _crashed;
        /// <summary>
        /// Lock mode of the class link: while set, the pilot cannot leave the activity the teacher started (inside
        /// this program only; nothing outside it is touched).
        /// </summary>
        public static bool LeaveBlocked { get; set; }

        /// <summary>The teacher starts the attempt again from the beginning.</summary>
        public void TeacherRestart()
        {
            if (Watching) return;
            if (State == Phase.Flying)
            {
                _vehicle.Crashed -= OnCrashed;
                State = Phase.Briefing;
            }
            ShowBriefing();
            StartFlying();
        }

        /// <summary>The teacher changes the weather in the middle of a flight ("wind", "gusts", "rain", "fog" …).</summary>
        public void TeacherWeather(string name, float value)
        {
            if (Array.IndexOf(ActivitySetup.WeatherNames, name) < 0) return;
            SetWeatherValue(name, value);
            Flight().SetWeather(_weather);
            _recorder.Event("teacher", name + "=" + value.ToString("0.##", CultureInfo.InvariantCulture));
        }

        /// <summary>The teacher sets or clears a failure ("gps_lost", "prop1", "video" …; 0 clears it).</summary>
        public void TeacherFailure(string name, float value)
        {
            Flight().SetFailure(name, value);
            _recorder.Event("teacher", name + "=" + value.ToString("0.##", CultureInfo.InvariantCulture));
        }
        /// <summary>The steps of a guided exercise as the attempt goes; null when the activity has none.</summary>
        public StepTracker Steps => _steps;

        /// <summary>
        /// Starts <paramref name="activity"/> on the drone of the scene. The drone, its model and the view were already
        /// set by the scene; this places the start point, the objects, the assist and the weather, and shows the
        /// briefing. <paramref name="leave"/> is called when the pilot leaves (back to the menu).
        /// </summary>
        public static EduActivityRunner Begin(QuadVehicle vehicle, ActivityDefinition activity, Action leave)
        {
            if (vehicle == null) throw new ArgumentNullException(nameof(vehicle));
            if (activity == null) throw new ArgumentNullException(nameof(activity));
            if (Current != null) Destroy(Current.gameObject);
            var runner = new GameObject("Edu Activity: " + activity.Id).AddComponent<EduActivityRunner>();
            Current = runner;
            runner._vehicle = vehicle;
            runner._leave = leave;
            runner.Activity = activity;
            runner.Prepare();
            return runner;
        }

        private void Prepare()
        {
            ActivitySetup setup = Activity.Setup;
            // The activity's frame: the scene's own start, moved and turned as the activity asks, on the ground there.
            _oldSpawn = _vehicle.SpawnPoint;
            Transform spawn = _oldSpawn != null ? _oldSpawn : _vehicle.transform;
            float heading = spawn.eulerAngles.y + (float)setup.StartHeadingDeg;
            Vector3 at = spawn.position + Quaternion.Euler(0f, spawn.eulerAngles.y, 0f) * new Vector3((float)setup.StartX, 0f, (float)setup.StartZ);
            float above = 0.15f;
            if (Physics.Raycast(at + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 100f, ~0, QueryTriggerInteraction.Ignore))
            {
                above = Mathf.Clamp(spawn.position.y - hit.point.y, 0.02f, 0.5f);
                at.y = hit.point.y;
            }
            else at.y = spawn.position.y - above;
            _frame = new FlightFrame { X = at.x, Y = at.y, Z = at.z, HeadingDeg = heading };
            _start = new GameObject("Activity Start").transform;
            _start.SetParent(transform, false);
            _start.SetPositionAndRotation(at + Vector3.up * above, Quaternion.Euler(0f, heading, 0f));
            _vehicle.SpawnPoint = _start;

            ApplyFlightSetup();
            _recorder = _vehicle.GetComponent<EduRecorder>();
            _objects = EduActivityObjects.Build(setup.Objects, _frame, transform, _vehicle.Collision.TrackGeometry, _made);
            _canvas = EduUi.Canvas("Activity Screen", 600);
            _canvas.transform.SetParent(transform, false);
            L.Changed += Redraw;
            AttemptRecord stored = _pendingReplay;
            _pendingReplay = null;
            _assignment = _pendingAssignment != null && _pendingAssignment.ActivityId == Activity.Id ? _pendingAssignment : null;
            _pendingAssignment = null;
            if (stored != null && stored.ActivityId == Activity.Id && WatchStored(stored)) return;
            ShowBriefing();
        }

        /// <summary>
        /// Puts the activity's assist and flight mode on the drone. Called at the start, and again whenever the scene
        /// has re-applied the pilot's settings (a radio plugged in, a setting changed), which must not undo them.
        /// </summary>
        public void ApplyFlightSetup()
        {
            ActivitySetup setup = Activity.Setup;
            _free = EduFreeFlight.Attach(_vehicle, 0, true);
            if (setup.Assist.Length > 0)
                _free.Set(setup.Assist == "position" ? AssistMode.Position : setup.Assist == "altitude" ? AssistMode.AltitudeHold : AssistMode.Off, false);
            // The mode the exercise is flown in, whatever the pilot's switch says.
            if (setup.Mode.Length > 0)
                Flight().ForcedMode = setup.Mode == "acro" ? FlightMode.Acro : setup.Mode == "horizon" ? FlightMode.Horizon : FlightMode.Angle;
        }

        private void OnDestroy()
        {
            L.Changed -= Redraw;
            foreach (UnityEngine.Object o in _made) if (o != null) Destroy(o);
            if (_vehicle != null)
            {
                _vehicle.Crashed -= OnCrashed;
                _vehicle.HoldAtSpawn = false;
                if (_oldSpawn != null) _vehicle.SpawnPoint = _oldSpawn;
                if (_flight != null)
                {
                    _flight.ClearFailures();
                    _flight.ForcedMode = null;
                }
            }
            if (Current == this) Current = null;
        }

        private void Redraw()
        {
            if (Watching) return;
            if (State == Phase.Briefing) ShowBriefing();
            else if (State == Phase.Result) ShowResult();
        }

        // ---------- the three phases ----------

        private void ShowBriefing()
        {
            State = Phase.Briefing;
            _vehicle.HoldAtSpawn = true;
            _vehicle.Respawn();
            RectTransform p = Panel(900f, 560f);
            EduUi.Text(p, L.T(Activity.Title), 30f, 20f, 840f, 50f, 36f, EduUi.Accent);
            TextMeshProUGUI brief = EduUi.Text(p, L.T(Activity.Briefing), 30f, 80f, 840f, 150f, 24f, Color.white, TextAlignmentOptions.TopLeft);
            brief.textWrappingMode = TextWrappingModes.Normal;
            EduUi.Text(p, L.T("You are scored on"), 30f, 240f, 840f, 30f, 21f, EduUi.Dim);
            float y = 274f;
            foreach (Criterion c in Activity.Criteria)
            {
                if (y > 430f) break;
                EduUi.Text(p, "·  " + L.T(c.Label) + (c.Required ? "  (" + L.T("required") + ")" : ""), 40f, y, 820f, 28f, 22f);
                y += 30f;
            }
            if (Activity.TimeLimit > 0)
                EduUi.Text(p, L.F("Time limit: {0}", EduUi.Clock((float)Activity.TimeLimit)), 30f, 452f, 400f, 28f, 21f, EduUi.Dim);
            int left = AttemptsLeft;
            if (left >= 0) EduUi.Text(p, L.F("Attempts left: {0}", left), 440f, 452f, 430f, 28f, 21f, left > 0 ? EduUi.Dim : EduUi.Accent, TextAlignmentOptions.MidlineRight);
            EduUi.Button(p, L.T("Start") + "  (Space)", 30f, 492f, 270f, 50f, StartFlying, 23f);
            // The exercise flown as it should be, to watch before trying.
            if (EduSession.Reference(Activity) != null) EduUi.Button(p, L.T("Show me") + "  (D)", 315f, 492f, 270f, 50f, WatchDemonstration, 23f);
            EduUi.Button(p, L.T("Leave") + "  (Esc)", 600f, 492f, 270f, 50f, Leave, 23f);
        }

        /// <summary>The pilot is ready: the recording starts and the drone is free.</summary>
        public void StartFlying()
        {
            if (State == Phase.Flying) return;
            // An assignment with no attempts left is not flown again; the briefing says so.
            if (AttemptsLeft == 0) return;
            ClearPanel();
            State = Phase.Flying;
            Result = null;
            Attempt = null;
            EndedBy = "";
            _clock = _groundedFor = _highest = 0f;
            _crashed = false;
            _timelineNext = 0;
            _vehicle.Respawn();
            _vehicle.HoldAtSpawn = Activity.Setup.OnTheGround;
            _steps = Activity.Steps != null ? new StepTracker(Activity.Steps) : null;
            _fed = _fedEvents = _stepsSeen = 0;
            _stepDoneAt = 0f;
            _applied.Clear();
            _stepsDoneFor = 0f;
            _vehicle.Crashed -= OnCrashed;
            _vehicle.Crashed += OnCrashed;
            _flight = _vehicle.GetComponent<EduFlight>();
            if (_flight != null) _flight.ClearFailures();
            ApplyWeather(true);
            _recorder.Begin(true);
            _recorder.Note("activity", Activity.Id);
            _recorder.Note("activity_hash", Activity.Hash);
            if (!_frame.IsIdentity) _recorder.Note("frame", _frame.ToString());

            RectTransform strip = EduUi.Panel(_canvas.transform, "Strip", 0f, 0f, 760f, 40f, new Color(0.05f, 0.06f, 0.08f, 0.7f), false);
            strip.anchorMin = strip.anchorMax = strip.pivot = new Vector2(0.5f, 1f);
            strip.anchoredPosition = new Vector2(0f, -150f);
            _panel = strip;
            _timer = EduUi.Text(strip, "", 0f, 0f, 760f, 40f, 22f, Color.white, TextAlignmentOptions.Center);
            if (_steps == null) return;
            // What to do now, large, with a bar that fills while a step's condition holds.
            RectTransform prompt = EduUi.Panel(strip, "Prompt", -170f, 48f, 1100f, 64f, new Color(0.05f, 0.06f, 0.08f, 0.8f), false);
            _prompt = EduUi.Text(prompt, "", 16f, 0f, 1068f, 58f, 30f, EduUi.Accent, TextAlignmentOptions.Center);
            _promptBar = EduUi.Panel(prompt, "Progress", 0f, 58f, 0f, 6f, EduUi.Accent, false);
        }

        /// <summary>A photo's place and heading as the activity's frame sees them.</summary>
        private FlightEvent LocalEvent(FlightEvent e)
        {
            if (e.Type != "photo" || _frame.IsIdentity) return e;
            string[] parts = (e.Data ?? "").Split(';');
            CultureInfo inv = CultureInfo.InvariantCulture;
            if (parts.Length < 4 || !float.TryParse(parts[0], NumberStyles.Float, inv, out float x) || !float.TryParse(parts[1], NumberStyles.Float, inv, out float y)
                || !float.TryParse(parts[2], NumberStyles.Float, inv, out float z) || !float.TryParse(parts[3], NumberStyles.Float, inv, out float heading)) return e;
            _frame.ToLocal(x, y, z, out x, out y, out z);
            parts[0] = x.ToString("0.00", inv);
            parts[1] = y.ToString("0.00", inv);
            parts[2] = z.ToString("0.00", inv);
            parts[3] = (((heading - _frame.HeadingDeg) % 360f + 360f) % 360f).ToString("0.0", inv);
            e.Data = string.Join(";", parts);
            return e;
        }

        /// <summary>Gives the step tracker the moments recorded since last time, in the activity's own frame.</summary>
        private void FeedSteps()
        {
            if (_steps == null || _recorder.Recording == null) return;
            List<FlightSample> samples = _recorder.Recording.Samples;
            double dt = 1.0 / _recorder.Recording.Rate;
            List<FlightEvent> events = _recorder.Recording.Events;
            for (; _fed < samples.Count && !_steps.Finished; _fed++)
            {
                // Events (a photo) at their own moments, in the activity's frame like the samples.
                for (; _fedEvents < events.Count && events[_fedEvents].T <= samples[_fed].T; _fedEvents++) _steps.FeedEvent(LocalEvent(events[_fedEvents]));
                _steps.Feed(_frame.Local(samples[_fed]), dt);
            }
            for (; _fedEvents < events.Count && !_steps.Finished; _fedEvents++) _steps.FeedEvent(LocalEvent(events[_fedEvents]));
            if (_steps.Done != _stepsSeen)
            {
                _stepsSeen = _steps.Done;
                _stepDoneAt = _clock;
            }
            if (_prompt == null) return;
            StepSpec now = _steps.Current;
            _prompt.text = now != null ? (_steps.Done + 1) + " / " + _steps.Count + "     " + L.T(now.Say) : L.T("All steps done");
            _promptBar.sizeDelta = new Vector2(1100f * _steps.Progress, 6f);
        }

        private void OnCrashed() => _crashed = true;

        /// <summary>The flight layer is needed for weather and failures even when the pilot flies with no assist.</summary>
        private EduFlight Flight()
        {
            if (_flight == null) _flight = _vehicle.GetComponent<EduFlight>();
            if (_flight == null) _flight = _vehicle.gameObject.AddComponent<EduFlight>();
            _flight.enabled = true;
            return _flight;
        }

        private void ApplyWeather(bool atOnce)
        {
            ActivitySetup setup = Activity.Setup;
            _weather = WeatherSettings.Calm;
            foreach (KeyValuePair<string, double> kv in setup.Weather) SetWeatherValue(kv.Key, (float)kv.Value);
            if (setup.Weather.Count > 0) Flight().SetWeather(_weather, atOnce);
        }

        private void SetWeatherValue(string name, float value)
        {
            switch (name)
            {
                case "wind": _weather.Wind = value; break;
                case "wind_from": _weather.WindFromDeg = value; break;
                case "gusts": _weather.Gusts = value; break;
                case "rain": _weather.Rain = value; break;
                case "visibility": _weather.Visibility = value; break;
                case "temperature": _weather.TemperatureC = value; break;
            }
        }

        private void Update()
        {
            Keyboard kb = Keyboard.current;
            // A gamepad works the two screens too: A goes on, B leaves, X shows the demonstration, Y the replay.
            // (In flight its buttons belong to the drone.)
            Gamepad pad = Gamepad.current;
            if (Watching || EduReplayViewer.Current != null) return;
            if (State == Phase.Briefing)
            {
                if ((kb != null && (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame)) || (pad != null && pad.buttonSouth.wasPressedThisFrame)) StartFlying();
                else if ((kb != null && kb.dKey.wasPressedThisFrame) || (pad != null && pad.buttonWest.wasPressedThisFrame)) WatchDemonstration();
                else if ((kb != null && kb.escapeKey.wasPressedThisFrame) || (pad != null && pad.buttonEast.wasPressedThisFrame)) Leave();
                return;
            }
            if (State == Phase.Result)
            {
                if ((kb != null && kb.rKey.wasPressedThisFrame) || (pad != null && pad.buttonSouth.wasPressedThisFrame)) ShowBriefing();
                else if ((kb != null && kb.lKey.wasPressedThisFrame) || (pad != null && pad.buttonNorth.wasPressedThisFrame)) WatchReplay();
                else if ((kb != null && kb.escapeKey.wasPressedThisFrame) || (pad != null && pad.buttonEast.wasPressedThisFrame)) Leave();
                return;
            }

            _clock += Time.deltaTime;
            ActivitySetup setup = Activity.Setup;
            foreach (TimedChange change in setup.Timeline)
            {
                if (_applied.Contains(change)) continue;
                // By the clock, or so many seconds after the pilot finished a step.
                bool due = change.AfterStep < 0 ? change.T <= _clock : _steps != null && _steps.Done >= change.AfterStep && _clock - _stepDoneAt >= change.T;
                if (!due) continue;
                _applied.Add(change);
                if (Array.IndexOf(ActivitySetup.WeatherNames, change.Name) >= 0)
                {
                    SetWeatherValue(change.Name, (float)change.Value);
                    Flight().SetWeather(_weather);
                }
                else Flight().SetFailure(change.Name, (float)change.Value);
            }

            ref readonly QuadState s = ref _vehicle.State;
            float height = s.Position.y - _frame.Y;
            _highest = Mathf.Max(_highest, height);
            bool down = !s.Armed && height < 0.4f && s.Velocity.Magnitude < 0.3f;
            _groundedFor = down ? _groundedFor + Time.deltaTime : 0f;
            string left = Activity.TimeLimit > 0 ? EduUi.Clock(Mathf.Max(0f, (float)Activity.TimeLimit - _clock)) : EduUi.Clock(_clock);
            if (_timer != null) _timer.text = L.T(Activity.Title) + "     " + left + "     " + L.T("Enter: finish");

            FeedSteps();
            _stepsDoneFor = _steps != null && _steps.Finished ? _stepsDoneFor + Time.deltaTime : 0f;

            if (_crashed && setup.EndOnCrash) Finish("crash");
            else if (setup.EndOnSteps && _stepsDoneFor > 1f) Finish("steps");
            else if (setup.EndOnLanding && _highest > 0.5f && _groundedFor > 1.5f) Finish("landed");
            else if (Activity.TimeLimit > 0 && _clock >= Activity.TimeLimit) Finish("time");
            else if (kb != null && kb.enterKey.wasPressedThisFrame && _clock > 1f) Finish("pilot");
            else if (kb != null && kb.escapeKey.wasPressedThisFrame) Leave();
        }

        /// <summary>Ends the attempt, grades the recording, stores it and shows the result.</summary>
        public void Finish(string endedBy)
        {
            if (State != Phase.Flying) return;
            EndedBy = endedBy;
            _vehicle.Crashed -= OnCrashed;
            _recorder.Event("end", endedBy);
            var flags = new Dictionary<string, object> { ["ended_by"] = endedBy };
            if (_free != null) flags["assist"] = _free.Assist.ToString();
            try
            {
                Attempt = EduSession.Finish(_recorder, Activity, flags);
                Result = Rubric.Score(Activity, _recorder.Recording);
                // In a class, the attempt also goes to the teacher's PC (and waits in the outbox until it has arrived).
                EduClassroom.AttemptFinished(Attempt, Activity, endedBy);
            }
            catch (Exception e)
            {
                Debug.LogError("[Edu] the attempt could not be stored: " + e);
                Result = Rubric.Score(Activity, _recorder.Recording);
            }
            _recorder.Paused = true;
            _vehicle.HoldAtSpawn = true;
            _vehicle.Respawn();
            _recorder.Paused = false;
            if (_flight != null) _flight.ClearFailures();
            ShowResult();
        }

        private void ShowResult()
        {
            State = Phase.Result;
            CultureInfo inv = CultureInfo.InvariantCulture;
            RectTransform p = Panel(940f, 640f);
            EduUi.Text(p, L.T(Activity.Title), 30f, 16f, 880f, 44f, 30f, EduUi.Accent);
            EduUi.Text(p, Result.Score.ToString("0", inv), 30f, 62f, 200f, 96f, 84f);
            EduUi.Text(p, "/ 100", 210f, 108f, 120f, 40f, 26f, EduUi.Dim);
            for (int i = 0; i < 3; i++)
                EduUi.Panel(p, "Star " + (i + 1), 350f + i * 56f, 88f, 44f, 44f, i < Result.Stars ? new Color(1f, 0.8f, 0.15f) : new Color(1f, 1f, 1f, 0.12f), false);
            EduUi.Text(p, L.T(Result.Passed ? "Passed" : "Not passed yet"), 540f, 86f, 370f, 48f, 30f,
                Result.Passed ? new Color(0.4f, 0.95f, 0.5f) : new Color(1f, 0.5f, 0.4f), TextAlignmentOptions.MidlineRight);

            float y = 176f;
            foreach (CriterionResult line in Result.Lines)
            {
                if (y > 420f) break;
                Criterion c = Activity.Criteria.Find(k => k.Id == line.Id);
                string value = double.IsNaN(line.Value) ? "—" : string.Format(inv, L.T(Metrics.Unit(c != null ? c.Metric : "")), line.Value.ToString("0.##", inv));
                EduUi.Text(p, L.T(line.Label), 30f, y, 520f, 30f, 22f);
                EduUi.Text(p, value, 550f, y, 170f, 30f, 22f, EduUi.Dim, TextAlignmentOptions.MidlineRight);
                EduUi.Text(p, (line.Weight * line.Fraction).ToString("0.#", inv) + " / " + line.Weight.ToString("0.#", inv), 730f, y, 180f, 30f, 22f,
                    line.Fraction >= 0.9 ? new Color(0.4f, 0.95f, 0.5f) : line.Fraction <= 0 ? new Color(1f, 0.5f, 0.4f) : Color.white, TextAlignmentOptions.MidlineRight);
                y += 34f;
            }
            y = Mathf.Max(y + 8f, 400f);
            foreach (string tip in Result.Tips)
            {
                TextMeshProUGUI t = EduUi.Text(p, "→  " + L.T(tip), 30f, y, 880f, 56f, 21f, EduUi.Accent, TextAlignmentOptions.TopLeft);
                t.textWrappingMode = TextWrappingModes.Normal;
                y += 60f;
            }
            EduUi.Button(p, L.T("Fly again") + "  (R)", 30f, 572f, 280f, 50f, ShowBriefing, 23f);
            EduUi.Button(p, L.T("Replay") + "  (L)", 330f, 572f, 280f, 50f, WatchReplay, 23f);
            EduUi.Button(p, L.T("Leave") + "  (Esc)", 630f, 572f, 280f, 50f, Leave, 23f);
        }

        /// <summary>The replay of the attempt, with the activity's reference flight beside it when it has one.</summary>
        public void WatchReplay()
        {
            if (_recorder == null || _recorder.Recording == null) return;
            Watch(_recorder.Recording, true, null);
        }

        /// <summary>The exercise flown as it should be (its reference flight), to watch before trying.</summary>
        public void WatchDemonstration()
        {
            FlightRecording reference = EduSession.Reference(Activity);
            if (reference != null) Watch(reference.PlacedAt(_frame), false, null);
        }

        /// <summary>A stored attempt from the replay library; closing it leaves the scene.</summary>
        private bool WatchStored(AttemptRecord attempt)
        {
            try
            {
                FlightRecording flight = FlightRecording.Load(EduSession.Store.ReplayFile(attempt.ReplayPath));
                return Watch(flight.PlacedAt(_frame), true, Leave);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Edu] the stored replay could not be opened: " + e.Message);
                return false;
            }
        }

        private bool _pendingReplayShown;

        private bool Watch(FlightRecording flight, bool withReference, Action closed)
        {
            // Shown from the library (closing it leaves): leaving is then always allowed.
            _pendingReplayShown = closed != null;
            if (flight == null || flight.Samples.Count < 2) return false;
            FlightRecording reference = withReference ? EduSession.Reference(Activity) : null;
            // The reference was flown somewhere of its own: bring it to where this activity is laid out.
            if (reference != null) reference = reference.PlacedAt(_frame);
            _vehicle.HoldAtSpawn = true;
            _canvas.enabled = false;
            Watching = true;
            EduReplayViewer viewer = EduReplayViewer.Open(_vehicle, flight, reference, L.T(Activity.Title));
            viewer.Closed += () =>
            {
                Watching = false;
                if (_canvas != null) _canvas.enabled = true;
                closed?.Invoke();
            };
            return true;
        }

        public void Leave()
        {
            // Lock mode: the teacher's activity is not left (a stored replay being watched always can be).
            if (LeaveBlocked && _pendingReplayShown == false) return;
            Action leave = _leave;
            Destroy(gameObject);
            leave?.Invoke();
        }

        // ---------- screen ----------

        private RectTransform Panel(float w, float h)
        {
            ClearPanel();
            _panel = EduUi.Panel(_canvas.transform, "Panel", 0f, 0f, w, h, new Color(0.05f, 0.06f, 0.08f, 0.94f));
            _panel.anchorMin = _panel.anchorMax = _panel.pivot = new Vector2(0.5f, 0.5f);
            _panel.anchoredPosition = Vector2.zero;
            return _panel;
        }

        private void ClearPanel()
        {
            if (_panel != null) Destroy(_panel.gameObject);
            _panel = null;
            _timer = null;
            _prompt = null;
            _promptBar = null;
        }
    }
}
