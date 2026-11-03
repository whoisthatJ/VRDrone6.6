using System.Collections.Generic;
using System.Globalization;
using GateRush.Core;
using GateRush.Game;
using GateRush.Net;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GateRush.UI
{
    /// <summary>
    /// Online pages: find or host a race (quick race, open races, join with a code), the race lobby, and the online
    /// leaderboards. Opt-in: nothing connects until the player opens the Online page.
    /// </summary>
    public sealed partial class MenuController
    {
        private enum OnlinePage { None, Home, Lobby, Leaderboards }

        private OnlinePage _onlinePage;
        private TMP_Text _onlineStatus;
        private TMP_InputField _codeField;
        private bool _onlineBusy;
        private bool _onlineHooked;
        private List<OpenRace> _openRaces = new List<OpenRace>();
        private int _boardTrack;

        /// <summary>Built-in tracks only: every pilot has them (user tracks live on one PC).</summary>
        private List<TrackCatalog.Entry> OnlineTracks()
        {
            var list = new List<TrackCatalog.Entry>();
            foreach (var t in tracks.Tracks)
                if (t != null && !string.IsNullOrEmpty(t.Id)) list.Add(t);
            return list;
        }

        private void StartOnline()
        {
            GameFlow.Catalog = tracks;
            if (OnlineLobby.InSession) ShowOnlineLobby();
            else if (!string.IsNullOrEmpty(OnlineFlow.Notice)) ShowOnline();
        }

        private void HookOnline()
        {
            if (_onlineHooked) return;
            _onlineHooked = true;
            OnlineLobby.Changed += OnOnlineChanged;
            OnlineLobby.Ended += OnOnlineEnded;
            if (NetRace.Instance != null) NetRace.Instance.RosterChanged += OnOnlineChanged;
        }

        private void UnhookOnline()
        {
            if (!_onlineHooked) return;
            _onlineHooked = false;
            OnlineLobby.Changed -= OnOnlineChanged;
            OnlineLobby.Ended -= OnOnlineEnded;
            if (NetRace.Instance != null) NetRace.Instance.RosterChanged -= OnOnlineChanged;
        }

        private void OnOnlineChanged()
        {
            if (this != null && _onlinePage == OnlinePage.Lobby && !_onlineBusy) ShowOnlineLobby();
        }

        private void OnOnlineEnded(string reason)
        {
            if (this == null) return;
            OnlineFlow.Notice = reason;
            if (_onlinePage == OnlinePage.Lobby) ShowOnline();
        }

        private string CurrentTrackName()
        {
            var t = tracks.Find(S.TrackId);
            return t != null ? t.DisplayName : "";
        }

        /// <summary>The selected track if it is a built-in one, else the first built-in track.</summary>
        private TrackCatalog.Entry OnlineTrack()
        {
            var t = tracks.Find(S.TrackId);
            return t ?? tracks.Tracks[0];
        }

        // ---------- find or host ----------

        private void ShowOnline()
        {
            var p = NewPage(L.T("ONLINE"));
            _onlinePage = OnlinePage.Home;
            HookOnline();
            float half = (ContentWidth - 30f) * 0.5f;

            string notice = OnlineFlow.Notice;
            OnlineFlow.Notice = "";
            _onlineStatus = UiKit.Label(p, notice, new Vector2(Margin, -110f), new Vector2(ContentWidth, 60f), 28f, TextAlignmentOptions.MidlineLeft, UiKit.AccentColor);

            float y = -190f;
            var track = OnlineTrack();
            UiKit.Button(p, L.T("Quick race"), new Vector2(Margin, y), new Vector2(half, 90f), () => Go(OnlineLobby.QuickRaceAsync(track.Id, track.DisplayName), L.T("Finding a race...")));
            UiKit.Button(p, L.T("Host a race"), new Vector2(Margin + half + 30f, y), new Vector2(half, 90f), () => Go(OnlineLobby.CreateAsync(track.Id, track.DisplayName, false), L.T("Opening your race...")));
            y -= 110f;
            UiKit.Button(p, L.T("Host a private race"), new Vector2(Margin, y), new Vector2(half, 90f), () => Go(OnlineLobby.CreateAsync(track.Id, track.DisplayName, true), L.T("Opening your race...")));
            float codeW = half - 230f;
            var codeBox = UiKit.Rect(p, "Code", new Vector2(Margin + half + 30f, y), new Vector2(codeW, 90f));
            _codeField = InputField(codeBox, L.T("Race code"));
            UiKit.Button(p, L.T("Join"), new Vector2(Margin + half + 30f + codeW + 20f, y), new Vector2(210f, 90f), () => Go(OnlineLobby.JoinByCodeAsync(_codeField.text), L.T("Joining...")));
            y -= 120f;

            UiKit.Label(p, L.T("Open races"), new Vector2(Margin, y), new Vector2(half, 60f), 34f, TextAlignmentOptions.MidlineLeft, UiKit.AccentColor);
            UiKit.Button(p, L.T("Refresh"), new Vector2(Width - Margin - 240f, y), new Vector2(240f, 60f), RefreshOpenRaces, 28f);
            y -= 75f;
            if (_openRaces.Count == 0)
                UiKit.Label(p, L.T("No open races listed. Refresh, start a quick race, or host one."), new Vector2(Margin, y), new Vector2(ContentWidth, 60f), 28f, TextAlignmentOptions.MidlineLeft, UiKit.DimTextColor);
            for (int i = 0; i < _openRaces.Count && i < 4; i++)
            {
                var r = _openRaces[i];
                UiKit.Label(p, $"{r.Name}   <color=#aab>{L.T(r.TrackName)}</color>   {r.Pilots}/{r.MaxPilots}", new Vector2(Margin, y), new Vector2(ContentWidth - 260f, 60f), 30f);
                string id = r.Id;
                UiKit.Button(p, L.T("Join"), new Vector2(Width - Margin - 240f, y), new Vector2(240f, 60f), () => Go(OnlineLobby.JoinAsync(id), L.T("Joining...")), 28f);
                y -= 70f;
            }

            UiKit.Label(p, L.F("Hosted races use your selected track ({0}); built-in tracks only. Up to {1} pilots.", L.T(track.DisplayName), OnlineRules.MaxPilots),
                new Vector2(Margin, -(Height - 185f)), new Vector2(ContentWidth, 50f), 24f, TextAlignmentOptions.MidlineLeft, UiKit.DimTextColor);
            BackButton(p, () =>
            {
                _onlinePage = OnlinePage.None;
                ShowMain();
            });
            UiKit.Button(p, L.T("Online leaderboards"), new Vector2(Width - Margin - 420f, -(Height - 110f)), new Vector2(420f, 70f), ShowOnlineBoards, 28f);
        }

        private async void RefreshOpenRaces()
        {
            if (_onlineBusy) return;
            _onlineBusy = true;
            SetOnlineStatus(L.T("Looking for races..."));
            var list = await OnlineLobby.ListAsync();
            _onlineBusy = false;
            if (this == null || _onlinePage != OnlinePage.Home) return;
            _openRaces = list;
            ShowOnline();
            SetOnlineStatus(OnlineLobby.LastError);
        }

        private async void Go(System.Threading.Tasks.Task<bool> join, string busyText)
        {
            if (_onlineBusy) return;
            _onlineBusy = true;
            SetOnlineStatus(busyText);
            bool ok;
            try
            {
                ok = await join;
            }
            finally
            {
                _onlineBusy = false;
            }
            if (this == null) return;
            if (ok)
            {
                OnlineFlow.PrepareLocalPilot();
                if (NetRace.Instance != null)
                {
                    NetRace.Instance.RosterChanged -= OnOnlineChanged;
                    NetRace.Instance.RosterChanged += OnOnlineChanged;
                }
                ShowOnlineLobby();
            }
            else SetOnlineStatus(OnlineLobby.LastError);
        }

        private void SetOnlineStatus(string text)
        {
            if (_onlineStatus != null) _onlineStatus.text = text ?? "";
        }

        // ---------- lobby ----------

        private void ShowOnlineLobby()
        {
            var p = NewPage(L.T("ONLINE RACE"));
            _onlinePage = OnlinePage.Lobby;
            HookOnline();
            if (!OnlineLobby.InSession)
            {
                ShowOnline();
                return;
            }
            bool host = OnlineLobby.IsHost;
            var net = NetRace.Instance;

            string code = string.IsNullOrEmpty(OnlineLobby.Code) ? "" : "   " + L.F("Code {0} (share it to invite)", $"<color=#fff>{OnlineLobby.Code}</color>");
            UiKit.Label(p, OnlineLobby.Name + code, new Vector2(Margin, -110f), new Vector2(ContentWidth, 60f), 30f, TextAlignmentOptions.MidlineLeft, UiKit.DimTextColor);
            _onlineStatus = UiKit.Label(p, "", new Vector2(Margin, -165f), new Vector2(ContentWidth, 50f), 26f, TextAlignmentOptions.MidlineLeft, UiKit.AccentColor);

            // Track: the host picks, everyone sees it.
            var list = OnlineTracks();
            string trackId = OnlineLobby.TrackId;
            int index = Mathf.Max(0, list.FindIndex(t => t.Id == trackId));
            if (host)
            {
                var names = list.ConvertAll(t => L.T(t.DisplayName)).ToArray();
                var row = UiKit.Rect(p, "Track row", new Vector2(Margin, -230f), new Vector2(ContentWidth, 60f));
                UiKit.StepperRow(row, L.T("Track"), 0f, names, index, i => _ = OnlineLobby.SetTrackAsync(list[i].Id, list[i].DisplayName), ContentWidth);
                var lapsRow = UiKit.Rect(p, "Laps row", new Vector2(Margin, -300f), new Vector2(ContentWidth, 60f));
                var lapNames = new[] { L.T("Track default"), "1", "2", "3", "4", "5" };
                UiKit.StepperRow(lapsRow, L.T("Laps"), 0f, lapNames, Mathf.Clamp(OnlineFlow.Laps, 0, 5), i => OnlineFlow.Laps = i, ContentWidth);
            }
            else
            {
                UiKit.Label(p, L.T("Track"), new Vector2(Margin, -230f), new Vector2(420f, 60f), 30f);
                UiKit.Label(p, L.T(OnlineLobby.TrackName), new Vector2(Margin + 440f, -230f), new Vector2(ContentWidth - 440f, 60f), 30f);
            }

            // Pilots: the race network's roster (with quads), or the session's names while it connects.
            UiKit.Label(p, L.F("Pilots  {0}/{1}", OnlineLobby.PlayerCount, OnlineLobby.MaxPlayers), new Vector2(Margin, -385f), new Vector2(ContentWidth, 60f), 34f, TextAlignmentOptions.MidlineLeft, UiKit.AccentColor);
            float y = -445f;
            var rows = new List<string>();
            if (net != null && net.Pilots.Count > 0)
            {
                var pilots = new List<NetPilot>(net.Pilots.Values);
                pilots.Sort((a, b) => a.Slot.CompareTo(b.Slot));
                foreach (var pilot in pilots)
                {
                    var build = RemotePilotView.Parse(pilot.BuildJson);
                    string me = pilot.ClientId == net.LocalClientId ? "  <color=#fa3>" + L.T("(you)") + "</color>" : "";
                    rows.Add($"{pilot.Slot + 1}.  {pilot.Name}{me}   <color=#aab>{L.T(build.Name)}</color>");
                }
            }
            else rows.AddRange(OnlineLobby.PlayerNames());
            foreach (string row in rows)
            {
                UiKit.Label(p, row, new Vector2(Margin + 20f, y), new Vector2(ContentWidth - 560f, 46f), 30f);
                y -= 46f;
            }

            if (host)
                UiKit.Button(p, L.T("START RACE"), new Vector2(Width - Margin - 520f, -(Height - 220f)), new Vector2(520f, 100f), () =>
                {
                    CommitIfDirty();
                    OnlineFlow.PrepareLocalPilot();
                    OnlineFlow.HostStart(OnlineLobby.TrackId);
                }, 44f);
            else
                UiKit.Label(p, L.T("Waiting for the host to start the race."), new Vector2(Margin, -(Height - 220f)), new Vector2(ContentWidth, 60f), 30f, TextAlignmentOptions.MidlineLeft, UiKit.DimTextColor);

            var leave = BackButton(p, async () =>
            {
                _onlinePage = OnlinePage.Home;
                await OnlineLobby.LeaveAsync();
                if (this != null) ShowOnline();
            });
            UiKit.SetButtonText(leave, L.T("Leave race"));
        }

        // ---------- leaderboards ----------

        private void ShowOnlineBoards()
        {
            var p = NewPage(L.T("ONLINE LEADERBOARDS"));
            _onlinePage = OnlinePage.Leaderboards;
            var ids = OnlineLeaderboards.Tracks;
            _boardTrack = Mathf.Clamp(_boardTrack, 0, ids.Length - 1);
            var names = new string[ids.Length];
            for (int i = 0; i < ids.Length; i++)
            {
                var t = tracks.Find(ids[i]);
                names[i] = t != null ? L.T(t.DisplayName) : ids[i];
            }
            var row = UiKit.Rect(p, "Track row", new Vector2(Margin, -120f), new Vector2(ContentWidth, 60f));
            UiKit.StepperRow(row, L.T("Track"), 0f, names, _boardTrack, i =>
            {
                _boardTrack = i;
                ShowOnlineBoards();
            }, ContentWidth);
            _onlineStatus = UiKit.Label(p, L.T("Loading..."), new Vector2(Margin, -200f), new Vector2(ContentWidth, 50f), 26f, TextAlignmentOptions.MidlineLeft, UiKit.AccentColor);
            var body = UiKit.Rect(p, "Scores", new Vector2(Margin, -260f), new Vector2(ContentWidth, 600f));
            string posting = S.Online.PostLapTimes ? L.T("Your best laps are posted (Settings > Pilot).") : L.T("Posting your laps is off: turn it on in Settings > Pilot.");
            UiKit.Label(p, posting, new Vector2(Margin, -(Height - 185f)), new Vector2(ContentWidth, 50f), 24f, TextAlignmentOptions.MidlineLeft, UiKit.DimTextColor);
            BackButton(p, ShowOnline);
            LoadBoard(ids[_boardTrack], body);
        }

        private async void LoadBoard(string trackId, RectTransform body)
        {
            var rows = await OnlineLeaderboards.TopAsync(trackId);
            if (this == null || body == null || _onlinePage != OnlinePage.Leaderboards) return;
            SetOnlineStatus(OnlineLeaderboards.LastError.Length > 0 ? OnlineLeaderboards.LastError : rows.Count == 0 ? L.T("No laps posted yet.") : "");
            float y = 0f;
            foreach (var r in rows)
            {
                string colour = r.IsMe ? "#fa3" : "#fff";
                UiKit.Label(body, $"<color={colour}>{r.Rank,3}.  {r.Pilot}</color>", new Vector2(0f, y), new Vector2(ContentWidth - 300f, 50f), 30f);
                UiKit.Label(body, $"<color={colour}>{r.LapTime.ToString("0.000", CultureInfo.InvariantCulture)} s</color>", new Vector2(ContentWidth - 300f, y), new Vector2(300f, 50f), 30f, TextAlignmentOptions.MidlineRight);
                y -= 46f;
            }
        }

        // ---------- helpers ----------

        private static TMP_InputField InputField(RectTransform box, string placeholder)
        {
            var bg = UiKit.Image(box, new Color(1f, 1f, 1f, 0.1f), true);
            var text = UiKit.Label(box, "", new Vector2(20f, 0f), new Vector2(box.sizeDelta.x - 40f, box.sizeDelta.y), 36f, TextAlignmentOptions.MidlineLeft);
            var hint = UiKit.Label(box, placeholder, new Vector2(20f, 0f), new Vector2(box.sizeDelta.x - 40f, box.sizeDelta.y), 32f, TextAlignmentOptions.MidlineLeft, UiKit.DimTextColor);
            var field = box.gameObject.AddComponent<TMP_InputField>();
            field.textComponent = text;
            field.placeholder = hint;
            field.textViewport = box;
            field.targetGraphic = bg;
            field.characterLimit = 12;
            field.characterValidation = TMP_InputField.CharacterValidation.Alphanumeric;
            return field;
        }
    }
}
