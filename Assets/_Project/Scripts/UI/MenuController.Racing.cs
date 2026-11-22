using System;
using System.Collections.Generic;
using System.Globalization;
using Ushqyr.Core;
using Ushqyr.Edu;
using Ushqyr.Game;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ushqyr.UI
{
    /// <summary>
    /// Racing tournaments in the teacher's console (EDU_PLAN §5): making one from a group, the next race, entering or
    /// correcting a result, flying a race again, and the result sheets. The tournament is saved in the class
    /// database after every change.
    /// </summary>
    public sealed partial class MenuController
    {
        private long _rId;
        private Tournament _rT;
        private int _rFormat, _rOption, _rRounds = 3;
        private bool _rDouble, _rSuccessive;
        private string _rHeat = "";
        private readonly Dictionary<string, string> _rLaps = new Dictionary<string, string>();
        private readonly HashSet<string> _rDsq = new HashSet<string>(), _rDns = new HashSet<string>();
        private static readonly double[] PenaltyChoices = { 0, 1, 2, 5, 10 };

        private Func<string, string> Words => L.T;

        private void SaveRacing()
        {
            EduStore store = Students(out _);
            if (store != null && _rT != null) _rId = store.SaveTournament(_rT, _rId);
        }

        // ---------- the list ----------

        private void ShowTeacherRacing()
        {
            RectTransform p = TeacherPage();
            EduStore store = Students(out string error);
            if (store == null) { _tMessage = L.F("The class data could not be opened: {0}", error); TeacherNote(p); return; }
            UiKit.Button(p, L.T("New tournament"), new Vector2(Width - Margin - 420f, -125f), new Vector2(420f, 60f), () =>
            {
                _tText = "";
                ShowRacingNew();
            }, 26f);
            List<TournamentInfo> list = store.Tournaments();
            UiKit.Label(p, list.Count == 0 ? L.T("No tournaments yet.") : L.T("Tournaments"), new Vector2(Margin, -125f), new Vector2(700f, 60f), 30f, TextAlignmentOptions.MidlineLeft,
                UiKit.DimTextColor);
            const int rows = 7;
            int pages = Mathf.Max(1, (list.Count + rows - 1) / rows);
            _tPage = Mathf.Clamp(_tPage, 0, pages - 1);
            float y = -205f;
            for (int i = _tPage * rows; i < list.Count && i < (_tPage + 1) * rows; i++)
            {
                TournamentInfo info = list[i];
                Button row = UiKit.Button(p, "", new Vector2(Margin, y), new Vector2(ContentWidth, 76f), () =>
                {
                    _rId = info.Id;
                    _rT = store.LoadTournament(info.Id);
                    _tArmed = 0;
                    ShowRacingEvent();
                }, 28f);
                UiKit.Label(row.transform, info.Name, new Vector2(24f, 0f), new Vector2(ContentWidth - 620f, 76f), 30f, TextAlignmentOptions.MidlineLeft);
                UiKit.Label(row.transform, When(info.Updated), new Vector2(ContentWidth - 580f, 0f), new Vector2(330f, 76f), 24f, TextAlignmentOptions.MidlineLeft, UiKit.DimTextColor);
                UiKit.Label(row.transform, info.Finished ? L.T("finished") : L.T("under way"), new Vector2(ContentWidth - 240f, 0f), new Vector2(220f, 76f), 26f,
                    TextAlignmentOptions.MidlineRight, info.Finished ? new Color(0.4f, 0.95f, 0.5f) : UiKit.AccentColor);
                y -= 86f;
            }
            TeacherNote(p);
            TeacherPager(p, pages, ShowTeacherRacing);
        }

        // ---------- a new tournament ----------

        private void ShowRacingNew()
        {
            RectTransform p = TeacherPage(ShowTeacherRacing);
            EduStore store = Students(out _);
            long? groupId = TeacherGroup(p, store, Margin, 760f, ShowRacingNew, out EduGroup group);
            List<EduUser> pilots = store.Users(groupId);
            pilots.RemoveAll(u => u.Role != "student");
            UiKit.Label(p, L.F("Pilots: {0}", pilots.Count), new Vector2(Margin + 790f, -125f), new Vector2(400f, 60f), 28f, TextAlignmentOptions.MidlineLeft,
                pilots.Count >= 2 ? UiKit.TextColor : UiKit.AccentColor);
            UiKit.Label(p, L.T("Name of the tournament"), new Vector2(Margin, -200f), new Vector2(ContentWidth, 44f), 24f, TextAlignmentOptions.MidlineLeft, UiKit.DimTextColor);
            InputBox(p, new Vector2(Margin, -246f), new Vector2(ContentWidth, 72f), _tText, v => _tText = v, false, false, 60);

            float y = -340f;
            void Choice(string label, Action flip, bool selected = false)
            {
                Button b = UiKit.Button(p, label, new Vector2(Margin, y), new Vector2(980f, 64f), () => { flip(); ShowRacingNew(); }, 26f);
                if (selected) UiKit.SetButtonColor(b, UiKit.SelectedColor);
                y -= 74f;
            }
            bool points = _rFormat == 1;
            Choice(points ? L.T("Format: points over a fixed number of rounds") : L.T("Format: FAI (qualification, elimination, final)"), () => _rFormat = 1 - _rFormat);
            Choice(L.F(points ? "Rounds: {0}" : "Qualifying rounds: {0}", _rRounds), () => _rRounds = _rRounds >= 6 ? 3 : _rRounds + 1);
            if (!points)
            {
                Choice(_rOption == 0 ? L.T("Ranking: average of the three best laps") : L.T("Ranking: best time for the three laps"), () => _rOption = 1 - _rOption);
                Choice(_rDouble ? L.T("Double elimination: yes") : L.T("Double elimination: no"), () => _rDouble = !_rDouble, _rDouble);
                Choice(_rSuccessive ? L.T("Final: races until someone has won two") : L.T("Final: one race"), () => _rSuccessive = !_rSuccessive, _rSuccessive);
            }
            if (pilots.Count < 16 && !points)
                UiKit.Label(p, L.T("With fewer than 16 pilots the FAI rules suggest the points format."), new Vector2(Margin + 1000f, -340f), new Vector2(ContentWidth - 1000f, 138f), 22f,
                    TextAlignmentOptions.TopLeft, UiKit.DimTextColor);

            UiKit.Button(p, L.T("Create"), new Vector2(Width - Margin - 300f, -(Height - 110f)), new Vector2(300f, 70f), () =>
            {
                string name = (_tText ?? "").Trim();
                if (name.Length == 0 || pilots.Count < 2)
                {
                    _tMessage = L.T("A tournament needs a name and at least two pilots. Choose a group with students.");
                    ShowRacingNew();
                    return;
                }
                var names = new List<string>();
                foreach (EduUser u in pilots)
                    if (!names.Contains(u.Name)) names.Add(u.Name);
                var settings = new TournamentSettings
                {
                    Format = points ? "points" : "fai", QualificationRounds = _rRounds, PointsRounds = _rRounds, QualificationOption = _rOption == 0 ? 2 : 1,
                    DoubleElimination = _rDouble, SuccessiveFinals = _rSuccessive, Seed = Environment.TickCount & 0xFFFF,
                };
                _rT = Tournament.Create(name, names, settings);
                _rId = 0;
                SaveRacing();
                _tText = "";
                ShowRacingEvent();
            }, 30f);
            TeacherNote(p);
        }

        // ---------- one tournament ----------

        private void ShowRacingEvent()
        {
            RectTransform p = TeacherPage(ShowTeacherRacing);
            if (_rT == null) { ShowTeacherRacing(); return; }
            Tournament t = _rT;
            RaceHeat next = t.Next;
            UiKit.Label(p, t.Name, new Vector2(Margin, -120f), new Vector2(ContentWidth, 60f), 38f, TextAlignmentOptions.MidlineLeft, UiKit.AccentColor);

            // Left: the race to fly now.
            const float lw = 560f;
            UiKit.Label(p, next != null ? L.T("Next race") + ":  " + RaceReports.HeatName(next, Words) : t.Finished ? L.T("The tournament is finished") : "", new Vector2(Margin, -190f),
                new Vector2(lw, 50f), 28f, TextAlignmentOptions.MidlineLeft);
            float y = -246f;
            if (next != null)
            {
                for (int i = 0; i < next.Pilots.Count; i++)
                {
                    UiKit.Label(p, (i + 1) + ".   " + next.Pilots[i], new Vector2(Margin + 20f, y), new Vector2(lw - 20f, 44f), 28f, TextAlignmentOptions.MidlineLeft);
                    y -= 46f;
                }
                // With the class open: call this race's pilots, on their seats, into a LAN race this PC directs.
                if (EduClassroom.Active && EduClassroom.Instance.Host != null)
                {
                    UiKit.Button(p, L.T("Fly it on the seats"), new Vector2(Margin, y - 14f), new Vector2(lw, 70f), () => FlyOnSeats(next), 28f);
                    y -= 80f;
                }
                UiKit.Button(p, L.T("Enter the result"), new Vector2(Margin, y - 14f), new Vector2(lw, 70f), () =>
                {
                    _rHeat = next.Id;
                    _rLaps.Clear();
                    _rDsq.Clear();
                    _rDns.Clear();
                    ShowRacingEntry();
                }, 28f);
            }

            // Middle: the classification as far as it is known.
            float mx = Margin + lw + 30f;
            const float mw = 460f;
            UiKit.Label(p, L.T("Classification"), new Vector2(mx, -190f), new Vector2(mw, 50f), 28f, TextAlignmentOptions.MidlineLeft, UiKit.DimTextColor);
            List<string> order = t.Classification();
            Dictionary<string, int> sum = t.Settings.Format == "points" ? t.Points() : null;
            y = -244f;
            for (int i = 0; i < order.Count && i < 14; i++)
            {
                bool out_ = t.Excluded.Contains(order[i]);
                UiKit.Label(p, (out_ ? Tournament.DisqMark : (i + 1).ToString(CultureInfo.InvariantCulture)) + "   " + order[i] + (sum != null ? "   (" + sum[order[i]] + ")" : ""), new Vector2(mx, y),
                    new Vector2(mw, 38f), 24f, TextAlignmentOptions.MidlineLeft, t.Finished && i < 3 && !out_ ? new Color(1f, 0.85f, 0.3f) : UiKit.TextColor);
                y -= 38f;
            }

            // Right: the sheets and the races flown.
            float rx = mx + mw + 30f, rw = Width - Margin - rx;
            y = -190f;
            void Side(string label, UnityEngine.Events.UnityAction act, bool danger = false)
            {
                Button b = UiKit.Button(p, label, new Vector2(rx, y), new Vector2(rw, 64f), act, 24f);
                if (danger) UiKit.SetButtonColor(b, new Color(0.42f, 0.2f, 0.16f, 1f));
                y -= 74f;
            }
            void Sheet(string label, int kind) => Side(label, () =>
            {
                try
                {
                    string file = EduTeacher.SaveRaceSheet(t, kind);
                    _tMessage = L.F("Saved: {0}", file);
                    if (kind != 2) EduTeacher.Open(file);
                }
                catch (Exception e) { _tMessage = L.F("Not saved: {0}", e.Message); }
                ShowRacingEvent();
            });
            Side(L.T("Races flown"), () => { _tPage = 0; _tArmed = 0; ShowRacingRaces(); });
            Sheet(L.T("Results and bracket (PDF)"), 0);
            Sheet(L.T("Results (XLSX)"), 1);
            Sheet(L.T("Results (CSV)"), 2);
            Side(_tArmed == -2 ? L.T("Press again") : L.T("Delete the tournament"), () =>
            {
                if (_tArmed != -2)
                {
                    _tArmed = -2;
                    ShowRacingEvent();
                    return;
                }
                _tArmed = 0;
                Students(out _).DeleteTournament(_rId);
                _rT = null;
                ShowTeacherRacing();
            }, _tArmed == -2);
            TeacherNote(p);
        }

        /// <summary>
        /// Hosts a LAN race on this PC as its director (FAI start, the tournament's laps), calls the seats of the
        /// race's pilots into it, and shows the lobby: the teacher picks the track and starts when everyone is in.
        /// The result goes into the tournament by itself.
        /// </summary>
        private void FlyOnSeats(RaceHeat heat)
        {
            EduClassroom room = EduClassroom.Instance;
            var seatOf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (ClassSeatInfo seat in room.Host.Seats())
                if (seat.Connected && seat.User.Length > 0) seatOf[seat.User] = seat.Name;
            var missing = new List<string>();
            foreach (string pilot in heat.Pilots)
                if (!seatOf.ContainsKey(pilot)) missing.Add(pilot);
            if (missing.Count > 0)
            {
                _tMessage = L.F("Not on a seat of the class: {0}", string.Join(", ", missing));
                ShowRacingEvent();
                return;
            }
            GameFlow.Catalog = tracks;
            Ushqyr.Net.NetRace.Ensure().Director = true;
            OnlineFlow.Fai = true;
            OnlineFlow.Laps = Mathf.Clamp(_rT.Settings.Laps, 1, 5);
            var list = OnlineTracks();
            if (list.Count == 0 || !Ushqyr.Net.OnlineLobby.HostDirect(list[0].Id, list[0].DisplayName))
            {
                _tMessage = Ushqyr.Net.OnlineLobby.LastError;
                ShowRacingEvent();
                return;
            }
            foreach (string pilot in heat.Pilots) room.CallToRace(seatOf[pilot], Ushqyr.Net.NetRace.DefaultPort);
            StartOnline();
            ShowOnlineLobby();
        }

        // ---------- entering a result by hand ----------

        /// <summary>"20.5 21 19,8" or "1:02.345 1:01.9": the lap times in seconds; null when a piece cannot be read.</summary>
        private static List<double> ParseLaps(string typed)
        {
            var laps = new List<double>();
            foreach (string piece in (typed ?? "").Split(new[] { ' ', ';', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string s = piece.Replace(',', '.');
                double minutes = 0;
                int colon = s.IndexOf(':');
                if (colon > 0)
                {
                    if (!double.TryParse(s.Substring(0, colon), NumberStyles.Float, CultureInfo.InvariantCulture, out minutes)) return null;
                    s = s.Substring(colon + 1);
                }
                if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds) || seconds < 0 || minutes < 0) return null;
                double lap = minutes * 60 + seconds;
                if (lap <= 0 || lap > 3600) return null;
                laps.Add(lap);
            }
            return laps;
        }

        private void ShowRacingEntry()
        {
            RectTransform p = TeacherPage(ShowRacingEvent);
            RaceHeat heat = _rT != null ? _rT.Heat(_rHeat) : null;
            if (heat == null || heat.Done) { ShowRacingEvent(); return; }
            UiKit.Label(p, RaceReports.HeatName(heat, Words), new Vector2(Margin, -120f), new Vector2(ContentWidth, 60f), 34f, TextAlignmentOptions.MidlineLeft, UiKit.AccentColor);
            UiKit.Label(p, L.T("For each pilot type the lap times, with spaces between them: 20.5 21.3 19.8 or 1:02.3 1:01.9 1:00.4"), new Vector2(Margin, -180f),
                new Vector2(ContentWidth, 44f), 22f, TextAlignmentOptions.MidlineLeft, UiKit.DimTextColor);
            float y = -240f;
            foreach (string pilot in heat.Pilots)
            {
                string who = pilot;
                _rLaps.TryGetValue(who, out string typed);
                UiKit.Label(p, who, new Vector2(Margin, y), new Vector2(420f, 68f), 28f, TextAlignmentOptions.MidlineLeft);
                InputBox(p, new Vector2(Margin + 430f, y), new Vector2(540f, 68f), typed ?? "", v => _rLaps[who] = v, false, false, 80);
                Button dsq = UiKit.Button(p, L.T("disqualified"), new Vector2(Margin + 990f, y), new Vector2(240f, 68f), () =>
                {
                    if (!_rDsq.Remove(who)) _rDsq.Add(who);
                    ShowRacingEntry();
                }, 22f);
                if (_rDsq.Contains(who)) UiKit.SetButtonColor(dsq, new Color(0.42f, 0.2f, 0.16f, 1f));
                Button dns = UiKit.Button(p, L.T("did not fly"), new Vector2(Margin + 1240f, y), new Vector2(240f, 68f), () =>
                {
                    if (!_rDns.Remove(who)) _rDns.Add(who);
                    ShowRacingEntry();
                }, 22f);
                if (_rDns.Contains(who)) UiKit.SetButtonColor(dns, UiKit.SelectedColor);
                y -= 78f;
            }
            UiKit.Button(p, L.T("Save"), new Vector2(Width - Margin - 300f, -(Height - 110f)), new Vector2(300f, 70f), () =>
            {
                var results = new List<RaceEntry>();
                foreach (string pilot in heat.Pilots)
                {
                    var e = new RaceEntry { Pilot = pilot, Disqualified = _rDsq.Contains(pilot), DidNotFly = _rDns.Contains(pilot) };
                    _rLaps.TryGetValue(pilot, out string typed);
                    List<double> laps = ParseLaps(typed);
                    if (laps == null)
                    {
                        _tMessage = L.F("The lap times of {0} could not be read", pilot);
                        ShowRacingEntry();
                        return;
                    }
                    for (int i = 0; i < laps.Count && i < _rT.Settings.Laps; i++)
                    {
                        e.Laps.Add(laps[i]);
                        e.Time += laps[i];
                    }
                    results.Add(e);
                }
                _rT.Record(heat.Id, results);
                SaveRacing();
                ShowRacingEvent();
            }, 30f);
            TeacherNote(p);
        }

        // ---------- the races flown: fly again, correct ----------

        private void ShowRacingRaces()
        {
            RectTransform p = TeacherPage(ShowRacingEvent);
            if (_rT == null) { ShowTeacherRacing(); return; }
            List<RaceHeat> flown = _rT.Heats.FindAll(h => h.Done);
            flown.Reverse();
            UiKit.Label(p, flown.Count == 0 ? L.T("No race has been flown yet.") : L.T("Races flown"), new Vector2(Margin, -120f), new Vector2(ContentWidth, 60f), 30f,
                TextAlignmentOptions.MidlineLeft, UiKit.DimTextColor);
            const int rows = 7;
            int pages = Mathf.Max(1, (flown.Count + rows - 1) / rows);
            _tPage = Mathf.Clamp(_tPage, 0, pages - 1);
            float y = -196f;
            for (int i = _tPage * rows; i < flown.Count && i < (_tPage + 1) * rows; i++)
            {
                RaceHeat heat = flown[i];
                RectTransform row = UiKit.Rect(p, "Race", new Vector2(Margin, y), new Vector2(ContentWidth, 76f));
                UiKit.Image(row, new Color(1f, 1f, 1f, 0.05f));
                UiKit.Label(row, RaceReports.HeatName(heat, Words), new Vector2(20f, 0f), new Vector2(480f, 76f), 26f, TextAlignmentOptions.MidlineLeft);
                var places = new List<string>();
                for (int n = 1; n <= heat.Results.Count; n++) places.Add(n + ". " + heat.Placed(n));
                UiKit.Fit(UiKit.Label(row, string.Join("     ", places), new Vector2(510f, 0f), new Vector2(ContentWidth - 1010f, 76f), 22f, TextAlignmentOptions.MidlineLeft, UiKit.DimTextColor));
                long mark = 1000 + i;
                Button again = UiKit.Button(row, _tArmed == mark ? L.T("Press again") : L.T("Fly again"), new Vector2(ContentWidth - 480f, -8f), new Vector2(230f, 60f), () =>
                {
                    if (_tArmed != mark)
                    {
                        _tArmed = mark;
                        ShowRacingRaces();
                        return;
                    }
                    _tArmed = 0;
                    _rT.Rerun(heat.Id);
                    SaveRacing();
                    ShowRacingEvent();
                }, 22f);
                if (_tArmed == mark) UiKit.SetButtonColor(again, new Color(0.42f, 0.2f, 0.16f, 1f));
                UiKit.Button(row, L.T("Correct"), new Vector2(ContentWidth - 240f, -8f), new Vector2(230f, 60f), () =>
                {
                    _rHeat = heat.Id;
                    ShowRacingCorrect();
                }, 22f);
                y -= 86f;
            }
            TeacherNote(p);
            TeacherPager(p, pages, ShowRacingRaces);
        }

        /// <summary>A recorded race: disqualify a pilot from it, or set a penalty. What followed from the race is laid out again.</summary>
        private void ShowRacingCorrect()
        {
            RectTransform p = TeacherPage(ShowRacingRaces);
            RaceHeat heat = _rT != null ? _rT.Heat(_rHeat) : null;
            if (heat == null || !heat.Done) { ShowRacingRaces(); return; }
            UiKit.Label(p, RaceReports.HeatName(heat, Words), new Vector2(Margin, -120f), new Vector2(ContentWidth, 60f), 34f, TextAlignmentOptions.MidlineLeft, UiKit.AccentColor);
            UiKit.Label(p, L.T("A change here places the race again; the races that followed from it are flown again."), new Vector2(Margin, -180f), new Vector2(ContentWidth, 44f), 22f,
                TextAlignmentOptions.MidlineLeft, UiKit.DimTextColor);
            var order = new List<RaceEntry>(heat.Results);
            order.Sort((a, b) => a.Place.CompareTo(b.Place));
            float y = -240f;
            foreach (RaceEntry entry in order)
            {
                RaceEntry e = entry;
                UiKit.Label(p, e.Place + ".   " + e.Pilot, new Vector2(Margin, y), new Vector2(520f, 68f), 28f, TextAlignmentOptions.MidlineLeft);
                UiKit.Label(p, e.DidNotFly ? L.T("did not fly") : e.Laps.Count > 0 ? RaceReports.Clock(e.Total) + "   (" + e.Laps.Count + ")" : "—", new Vector2(Margin + 530f, y),
                    new Vector2(300f, 68f), 26f, TextAlignmentOptions.MidlineLeft, UiKit.DimTextColor);
                Button dsq = UiKit.Button(p, L.T("disqualified"), new Vector2(Margin + 850f, y), new Vector2(260f, 68f), () =>
                {
                    _rT.Correct(heat.Id, e.Pilot, !e.Disqualified, e.Penalty);
                    SaveRacing();
                    ShowRacingCorrect();
                }, 22f);
                if (e.Disqualified) UiKit.SetButtonColor(dsq, new Color(0.42f, 0.2f, 0.16f, 1f));
                int now = Mathf.Max(0, Array.IndexOf(PenaltyChoices, e.Penalty));
                UiKit.Button(p, L.F("Penalty: {0} s", PenaltyChoices[now].ToString("0", CultureInfo.InvariantCulture)), new Vector2(Margin + 1120f, y), new Vector2(360f, 68f), () =>
                {
                    _rT.Correct(heat.Id, e.Pilot, e.Disqualified, PenaltyChoices[(now + 1) % PenaltyChoices.Length]);
                    SaveRacing();
                    ShowRacingCorrect();
                }, 22f);
                y -= 78f;
            }
            TeacherNote(p);
        }
    }
}
