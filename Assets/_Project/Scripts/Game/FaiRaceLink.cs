using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TMPro;
using UnityEngine;
using Ushqyr.Core;
using Ushqyr.Edu;
using Ushqyr.Net;
using Ushqyr.Race;

namespace Ushqyr.Game
{
    /// <summary>
    /// A LAN race timed by the FAI rules (EDU_PLAN §5, Docs/reference/fai/f9u_values.md). Every pilot's PC measures
    /// its own gate passes in simulator time from the start tone, to a thousandth of a second, and sends each one to
    /// the host. The host keeps the one timing of the race from those passes, decides the result (false start,
    /// missed gates, three minutes of flight) and sends it to everyone, so every PC shows the same result. When the
    /// pilots are exactly those of the next race of a tournament in the host's class data, the result is recorded
    /// into that tournament.
    /// </summary>
    public sealed class FaiRaceLink : MonoBehaviour
    {
        public const string MsgGate = "fai.gate", MsgLeft = "fai.left", MsgResult = "fai.result";

        public static FaiRaceLink Current { get; private set; }
        /// <summary>The last result the host sent, as JSON; null before any.</summary>
        public static string LastResultJson { get; private set; }
        public static event Action<string> ResultReceived;

        /// <summary>The host's timing of the race; null on the other PCs and before the tone.</summary>
        public RaceTiming Timing { get; private set; }
        /// <summary>The tournament race this result went into ("" when it matched none).</summary>
        public string RecordedInto { get; private set; } = "";

        private NetRace _net;
        private RaceSession _race;
        private double _go;
        private bool _toneHeard, _sent;
        private readonly Dictionary<string, string> _names = new Dictionary<string, string>();
        private long _tournamentId;
        private string _heatId = "";
        private Canvas _canvas, _towerCanvas;
        private TextMeshProUGUI _tower;
        private float _towerAt;

        public void Configure(RaceSession race) => _race = race;

        private void Start()
        {
            _net = NetRace.Instance;
            if (_net == null || _race == null)
            {
                enabled = false;
                return;
            }
            Current = this;
            _race.FaiStart = true;
            _race.StartSignal += OnTone;
            _race.GateCrossed += OnGate;
            _net.RegisterExtra(MsgResult, OnResult);
            if (!_net.IsHost) return;
            _net.RegisterExtra(MsgGate, HostGate);
            _net.RegisterExtra(MsgLeft, HostLeft);
            _net.GoReceived += HostGo;
            _net.RosterChanged += HostRoster;
            if (_net.Phase == OnlinePhase.Racing) HostGo(_net.GoServerTime);
        }

        private void OnDestroy()
        {
            if (Current == this) Current = null;
            if (_race != null)
            {
                _race.StartSignal -= OnTone;
                _race.GateCrossed -= OnGate;
            }
            if (_net == null) return;
            _net.UnregisterExtra(MsgResult);
            _net.UnregisterExtra(MsgGate);
            _net.UnregisterExtra(MsgLeft);
            _net.GoReceived -= HostGo;
            _net.RosterChanged -= HostRoster;
        }

        // ---------- every pilot ----------

        private void OnTone(double at)
        {
            _go = at;
            _toneHeard = true;
            // Not on the start position at the tone: the host must know, in case this model now goes through the first gate.
            if (_race.LeftStartEarly) _net.SendExtraToHost(MsgLeft, new byte[] { 1 });
        }

        private void OnGate(int gate, double at)
        {
            if (!_toneHeard || gate < 0 || gate > 255) return;
            int ms = (int)Math.Round((at - _go) * 1000.0);
            if (ms < 0) return;
            _net.SendExtraToHost(MsgGate, new[] { (byte)gate, (byte)ms, (byte)(ms >> 8), (byte)(ms >> 16), (byte)(ms >> 24) });
        }

        // ---------- the host ----------

        private static string Key(ulong clientId) => clientId.ToString(CultureInfo.InvariantCulture);

        private void HostGo(double goServerTime)
        {
            var ids = new List<string>();
            _names.Clear();
            var order = new List<NetPilot>(_net.Pilots.Values);
            order.Sort((a, b) => a.Slot.CompareTo(b.Slot));
            foreach (NetPilot p in order)
            {
                ids.Add(Key(p.ClientId));
                _names[Key(p.ClientId)] = p.Name;
            }
            // A qualifying race of a tournament is timed from the timing gate; every other race from the tone (F9U.6.1).
            bool fromTone = true;
            FindHeat(out Tournament t, out RaceHeat heat);
            if (heat != null) fromTone = heat.Stage != "qualification";
            int laps = _race.Timer != null && _race.Timer.TotalLaps > 0 ? _race.Timer.TotalLaps : 3;
            Timing = new RaceTiming(ids, Mathf.Max(1, _race.GateCount), laps, fromTone);
            Timing.Start(0);
            _sent = false;
            Debug.Log($"[Ushqyr] FAI race: {ids.Count} pilots, {laps} laps, {_race.GateCount} gates, timed from the {(fromTone ? "tone" : "timing gate")}" +
                      (heat != null ? ", " + heat.Id + " of \"" + t.Name + "\"" : ""));
        }

        /// <summary>The tournament race, in the host's class data, whose pilots are exactly the pilots of this race.</summary>
        private void FindHeat(out Tournament tournament, out RaceHeat heat)
        {
            tournament = null;
            heat = null;
            _tournamentId = 0;
            _heatId = "";
            try
            {
                var names = new HashSet<string>(_names.Values);
                if (names.Count != _names.Count) return; // two pilots with one name: nothing can be matched
                EduStore store = EduSession.Store;
                foreach (TournamentInfo info in store.Tournaments())
                {
                    if (info.Finished) continue;
                    Tournament t = store.LoadTournament(info.Id);
                    RaceHeat next = t != null ? t.Next : null;
                    if (next == null || next.Pilots.Count != names.Count || !names.SetEquals(next.Pilots)) continue;
                    tournament = t;
                    heat = next;
                    _tournamentId = info.Id;
                    _heatId = next.Id;
                    return;
                }
            }
            catch (Exception e) { Debug.LogWarning("[Ushqyr] FAI race: the class data could not be read: " + e.Message); }
        }

        private void HostLeft(ulong sender, byte[] data)
        {
            if (Timing != null) Timing.LeftStart(Key(sender), -0.001);
        }

        private void HostGate(ulong sender, byte[] data)
        {
            if (Timing == null || data == null || data.Length < 5) return;
            double t = (data[1] | data[2] << 8 | data[3] << 16 | data[4] << 24) / 1000.0;
            string pilot = Key(sender);
            PilotTiming before = Timing.Of(pilot);
            bool wasOut = before != null && before.Disqualified;
            Timing.LeftStart(pilot, t);
            Timing.Gate(pilot, data[0], t);
            if (before != null && before.FalseStart && !wasOut) Debug.Log($"[Ushqyr] FAI race: false start, {_names[pilot]} is disqualified from the race");
        }

        /// <summary>A pilot who left the session can fly no more in this race.</summary>
        private void HostRoster()
        {
            if (Timing == null) return;
            foreach (PilotTiming p in Timing.Pilots)
            {
                bool here = false;
                foreach (NetPilot n in _net.Pilots.Values) here |= Key(n.ClientId) == p.Pilot;
                if (!here) Timing.Retire(p.Pilot);
            }
        }

        private void Update()
        {
            if (_net == null || !_net.IsHost || Timing == null) return;
            if (_net.Director && Time.unscaledTime >= _towerAt)
            {
                _towerAt = Time.unscaledTime + 0.25f;
                ShowTower();
            }
            if (_sent) return;
            Timing.Tick(_net.ServerTime - _net.GoServerTime);
            if (!Timing.Over) return;
            _sent = true;
            SendResult();
        }

        private void SendResult()
        {
            List<RaceEntry> results = Timing.Results();
            var ranking = new List<string>();
            foreach (RaceEntry e in results) ranking.Add(e.Pilot);
            Tournament.Place(results, Timing.Laps, ranking);
            var rows = new List<object>();
            foreach (RaceEntry e in results)
            {
                PilotTiming p = Timing.Of(e.Pilot);
                rows.Add(new Dictionary<string, object>
                {
                    ["name"] = _names[e.Pilot], ["place"] = (double)e.Place, ["laps"] = e.Laps.ConvertAll(x => (object)x), ["time"] = e.Time,
                    ["dsq"] = e.Disqualified, ["dns"] = e.DidNotFly, ["false_start"] = p.FalseStart, ["holeshot"] = double.IsNaN(p.Holeshot) ? -1.0 : p.Holeshot,
                    ["void_laps"] = (double)p.VoidLaps,
                });
            }
            string json = Json.Write(new Dictionary<string, object> { ["laps"] = (double)Timing.Laps, ["results"] = rows });
            _net.SendExtraToAll(MsgResult, Encoding.UTF8.GetBytes(json));

            // Into the tournament, when this was its next race.
            if (_tournamentId == 0) return;
            try
            {
                EduStore store = EduSession.Store;
                Tournament t = store.LoadTournament(_tournamentId);
                RaceHeat heat = t != null ? t.Heat(_heatId) : null;
                if (heat == null || heat.Done) return;
                foreach (RaceEntry e in results) e.Pilot = _names[e.Pilot];
                t.Record(heat.Id, results);
                store.SaveTournament(t, _tournamentId);
                RecordedInto = heat.Id;
                Debug.Log($"[Ushqyr] FAI race: recorded as {heat.Id} of \"{t.Name}\"; next: {(t.Next != null ? t.Next.Id : "none")}");
            }
            catch (Exception e) { Debug.LogWarning("[Ushqyr] FAI race: the result was not recorded into the tournament: " + e.Message); }
        }

        /// <summary>The timing tower of the race director: position, pilot, laps, the gap to the leader, the last lap.</summary>
        private void ShowTower()
        {
            if (_tower == null)
            {
                _towerCanvas = EduUi.Canvas("Timing Tower", 650);
                _towerCanvas.transform.SetParent(transform, false);
                RectTransform p = EduUi.Panel(_towerCanvas.transform, "Tower", 0f, 0f, 520f, 60f + Timing.Pilots.Count * 40f, new Color(0.05f, 0.06f, 0.08f, 0.85f));
                p.anchorMin = p.anchorMax = p.pivot = new Vector2(0f, 1f);
                p.anchoredPosition = new Vector2(30f, -30f);
                _tower = EduUi.Text(p, "", 20f, 12f, 480f, 40f + Timing.Pilots.Count * 40f, 26f, Color.white, TextAlignmentOptions.TopLeft);
            }
            var b = new StringBuilder();
            double clock = Math.Max(0, _net.ServerTime - _net.GoServerTime);
            b.Append(RaceReports.Clock(Math.Min(clock, Timing.FlightTime))).Append('\n');
            foreach (PilotTiming p in Timing.Order())
            {
                double gap = Timing.Gap(p.Pilot);
                string note = p.Disqualified ? Tournament.DisqMark : p.Finished ? RaceReports.Clock(p.RaceTime)
                    // No figure for a pilot who was ahead at their last gate and has been passed since: it would be negative.
                    : p.Position == 1 || double.IsNaN(gap) || gap < 0 ? "" : "+" + gap.ToString("0.000", CultureInfo.InvariantCulture);
                b.Append(p.Position).Append("  ").Append(_names.TryGetValue(p.Pilot, out string name) ? name : p.Pilot).Append("   ")
                    .Append(Math.Min(p.Laps.Count, Timing.Laps)).Append('/').Append(Timing.Laps).Append("   ").Append(note).Append('\n');
            }
            _tower.text = b.ToString();
        }

        // ---------- everyone: the result ----------

        private void OnResult(ulong sender, byte[] data)
        {
            if (data == null) return;
            string json = Encoding.UTF8.GetString(data);
            LastResultJson = json;
            try
            {
                Dictionary<string, object> o = Json.ParseObject(json);
                List<object> rows = Json.Arr(o, "results") ?? new List<object>();
                rows.Sort((a, b) => Json.Num((Dictionary<string, object>)a, "place", 0).CompareTo(Json.Num((Dictionary<string, object>)b, "place", 0)));
                var line = new StringBuilder("[Ushqyr] FAI result:");
                foreach (object r in rows)
                {
                    var m = (Dictionary<string, object>)r;
                    List<object> laps = Json.Arr(m, "laps") ?? new List<object>();
                    line.Append(" | ").Append(Json.Num(m, "place", 0).ToString("0", CultureInfo.InvariantCulture)).Append(". ").Append(Json.Str(m, "name", "")).Append(' ');
                    if (Json.Flag(m, "dsq")) line.Append(Json.Flag(m, "false_start") ? "DSQ false start" : "DSQ");
                    else if (Json.Flag(m, "dns")) line.Append("DNS");
                    else line.Append(laps.Count).Append(" laps ").Append(Json.Num(m, "time", 0).ToString("0.000", CultureInfo.InvariantCulture));
                    foreach (object lap in laps) line.Append(' ').Append(((double)lap).ToString("0.000", CultureInfo.InvariantCulture));
                }
                Debug.Log(line.ToString());
                ShowResult(rows);
            }
            catch (Exception e) { Debug.LogWarning("[Ushqyr] FAI result could not be read: " + e.Message); }
            ResultReceived?.Invoke(json);
        }

        /// <summary>The result table, the same on every PC.</summary>
        private void ShowResult(List<object> rows)
        {
            if (_canvas != null) Destroy(_canvas.gameObject);
            _canvas = EduUi.Canvas("FAI Result", 700);
            _canvas.transform.SetParent(transform, false);
            float h = 130f + rows.Count * 44f;
            RectTransform p = EduUi.Panel(_canvas.transform, "Panel", 0f, 0f, 900f, h, new Color(0.05f, 0.06f, 0.08f, 0.94f));
            p.anchorMin = p.anchorMax = p.pivot = new Vector2(0.5f, 1f);
            p.anchoredPosition = new Vector2(0f, -150f);
            EduUi.Text(p, L.T("RACE RESULT"), 30f, 14f, 840f, 46f, 32f, EduUi.Accent);
            EduUi.Text(p, L.T("Pilot"), 100f, 66f, 380f, 30f, 20f, EduUi.Dim);
            EduUi.Text(p, L.T("Time"), 480f, 66f, 180f, 30f, 20f, EduUi.Dim, TextAlignmentOptions.MidlineRight);
            EduUi.Text(p, L.T("Best lap"), 680f, 66f, 190f, 30f, 20f, EduUi.Dim, TextAlignmentOptions.MidlineRight);
            float y = 100f;
            foreach (object r in rows)
            {
                var m = (Dictionary<string, object>)r;
                List<object> laps = Json.Arr(m, "laps") ?? new List<object>();
                double best = double.NaN;
                foreach (object lap in laps) best = double.IsNaN(best) ? (double)lap : Math.Min(best, (double)lap);
                bool dsq = Json.Flag(m, "dsq"), dns = Json.Flag(m, "dns");
                string time = dsq ? L.T("disqualified") : dns ? L.T("did not fly") : laps.Count > 0 ? RaceReports.Clock(Json.Num(m, "time", 0)) + "  (" + laps.Count + ")" : "";
                string place = Json.Num(m, "place", 0).ToString("0", CultureInfo.InvariantCulture);
                string pilot = Json.Str(m, "name", "");
                EduUi.Text(p, place, 30f, y, 60f, 40f, 28f);
                EduUi.Text(p, pilot, 100f, y, 380f, 40f, 28f);
                EduUi.Text(p, time, 440f, y, 220f, 40f, 26f, dsq ? new Color(1f, 0.5f, 0.4f) : Color.white, TextAlignmentOptions.MidlineRight);
                EduUi.Text(p, RaceReports.Clock(best), 680f, y, 190f, 40f, 26f, EduUi.Dim, TextAlignmentOptions.MidlineRight);
                y += 44f;
            }
        }
    }
}
