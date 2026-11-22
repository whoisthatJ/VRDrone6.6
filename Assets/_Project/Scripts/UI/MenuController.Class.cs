using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using Ushqyr.Core;
using Ushqyr.Edu;
using Ushqyr.Game;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ushqyr.UI
{
    /// <summary>
    /// The class link on the screen (EDU_PLAN §9). For the teacher: opening the class, the grid of seats with their
    /// small live pictures, starting an exercise on every seat, pausing, EduClassroom.DoAttention, messages, weather and failures.
    /// For a seat: finding the class on the network (or typing its address), typing the code, and raising a hand.
    /// </summary>
    public sealed partial class MenuController
    {
        private sealed class SeatTile
        {
            public string Seat;
            public TMP_Text Who, State;
            public Image Frame;
            public RawImage Picture;
        }

        private readonly List<SeatTile> _tiles = new List<SeatTile>();
        private string _classSeat = "", _classTarget = "";
        private bool _classLock;
        private int _classWind;
        private static readonly int[] WindChoices = { 0, 3, 6, 9, 12 };

        private static string LocalAddresses()
        {
            var list = new List<string>();
            try
            {
                foreach (IPAddress a in Dns.GetHostAddresses(Dns.GetHostName()))
                    if (a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a)) list.Add(a.ToString());
            }
            catch (SocketException) { /* no network: the list stays empty */ }
            return list.Count > 0 ? string.Join(", ", list) : "127.0.0.1";
        }

        /// <summary>What a seat is doing, in words and a colour, from the status it last sent.</summary>
        private static string SeatState(ClassSeatInfo seat, out Color colour)
        {
            colour = UiKit.DimTextColor;
            if (!seat.Connected) return L.T("not connected");
            string state = Json.Str(seat.Status, "state", "menu");
            string activity = Json.Str(seat.Status, "activity", "");
            string number = EduSession.Activities.TryGetValue(activity, out ActivityDefinition a) && a.Number > 0 ? a.Number + ".  " : "";
            CultureInfo inv = CultureInfo.InvariantCulture;
            switch (state)
            {
                case "flying":
                    colour = new Color(0.45f, 0.8f, 1f);
                    return number + L.T("flying") + (seat.Status.ContainsKey("steps") ? "  " + Json.Num(seat.Status, "steps", 0).ToString("0", inv) + "/" + Json.Num(seat.Status, "of", 0).ToString("0", inv) : "") +
                           "   " + EduUi.Clock((float)Json.Num(seat.Status, "clock", 0));
                case "crashed":
                    colour = new Color(1f, 0.5f, 0.4f);
                    return number + L.T("crashed");
                case "finished":
                    colour = Json.Flag(seat.Status, "passed") ? new Color(0.4f, 0.95f, 0.5f) : new Color(1f, 0.8f, 0.3f);
                    return number + L.F("finished: {0}", Json.Num(seat.Status, "score", 0).ToString("0", inv));
                case "briefing":
                    colour = UiKit.TextColor;
                    return number + L.T("reading the task");
                default: return L.T("in the menu");
            }
        }

        // ---------- the teacher's Class tab ----------

        private void ShowTeacherClass()
        {
            RectTransform p = TeacherPage();
            _tiles.Clear();
            EduStore store = Students(out string error);
            if (store == null) { _tMessage = L.F("The class data could not be opened: {0}", error); TeacherNote(p); return; }
            GameFlow.Catalog = tracks;
            EduClassroom room = EduClassroom.Instance;
            if (room.Host == null)
            {
                UiKit.Label(p, L.T("Open the class to see the seats, start an exercise on all of them at once and collect their results here."), new Vector2(Margin, -140f),
                    new Vector2(ContentWidth, 90f), 28f, TextAlignmentOptions.TopLeft, UiKit.DimTextColor);
                UiKit.Button(p, L.T("Open the class"), new Vector2(Margin, -250f), new Vector2(520f, 80f), () =>
                {
                    try { room.OpenClass(); }
                    catch (Exception e) { _tMessage = L.F("The class could not be opened: {0}", e.Message); }
                    ShowTeacherClass();
                }, 32f);
                UiKit.Label(p, L.T("Seats join over the school's network with the code shown here. Windows may ask once to let the program use the network: allow it for private networks."),
                    new Vector2(Margin, -360f), new Vector2(ContentWidth, 90f), 24f, TextAlignmentOptions.TopLeft, UiKit.DimTextColor);
                TeacherNote(p);
                return;
            }

            List<ClassSeatInfo> seats = room.Host.Seats();
            int connected = seats.FindAll(s => s.Connected).Count;
            string code = room.Code.Length == 6 ? room.Code.Substring(0, 3) + " " + room.Code.Substring(3) : room.Code;
            UiKit.Label(p, L.F("Code: {0}", code), new Vector2(Margin, -120f), new Vector2(360f, 60f), 40f, TextAlignmentOptions.MidlineLeft, UiKit.AccentColor);
            UiKit.Label(p, L.F("Address: {0}", LocalAddresses() + " : " + room.Host.Port) + "      " + L.F("Seats: {0} of {1}", connected, seats.Count) + "      " +
                           L.F("Results received: {0}", room.Stored), new Vector2(Margin + 370f, -120f), new Vector2(ContentWidth - 370f, 60f), 22f, TextAlignmentOptions.MidlineLeft,
                UiKit.DimTextColor);

            float bw = (ContentWidth - 4f * 10f) / 5f, by = -190f;
            UiKit.Button(p, L.T("Start an exercise"), new Vector2(Margin, by), new Vector2(bw, 60f), () => { _classTarget = ""; ShowClassStart(); }, 22f);
            UiKit.Button(p, L.T("Pause all"), new Vector2(Margin + (bw + 10f), by), new Vector2(bw / 2f - 5f, 60f), () => room.Command(null, EduClassroom.DoPause), 22f);
            UiKit.Button(p, L.T("Resume all"), new Vector2(Margin + (bw + 10f) + bw / 2f + 5f, by), new Vector2(bw / 2f - 5f, 60f), () => room.Command(null, EduClassroom.DoResume), 22f);
            UiKit.Button(p, L.T("Attention: on"), new Vector2(Margin + 2f * (bw + 10f), by), new Vector2(bw / 2f - 5f, 60f), () => room.Command(null, EduClassroom.DoAttention, null, 1), 20f);
            UiKit.Button(p, L.T("Attention: off"), new Vector2(Margin + 2f * (bw + 10f) + bw / 2f + 5f, by), new Vector2(bw / 2f - 5f, 60f), () => room.Command(null, EduClassroom.DoAttention, null, 0), 20f);
            UiKit.Button(p, L.T("Everyone to the menu"), new Vector2(Margin + 3f * (bw + 10f), by), new Vector2(bw, 60f), () => room.Command(null, EduClassroom.DoLeave), 22f);
            Button close = UiKit.Button(p, _tArmed == -1 ? L.T("Press again") : L.T("Close the class"), new Vector2(Margin + 4f * (bw + 10f), by), new Vector2(bw, 60f), () =>
            {
                if (_tArmed != -1) _tArmed = -1;
                else
                {
                    _tArmed = 0;
                    room.CloseClass();
                }
                ShowTeacherClass();
            }, 22f);
            if (_tArmed == -1) UiKit.SetButtonColor(close, new Color(0.42f, 0.2f, 0.16f, 1f));

            // The seats: six across, three down.
            const int cols = 6, perPage = 18;
            float tw = (ContentWidth - (cols - 1) * 10f) / cols, ph = tw * 9f / 16f, th = ph + 58f;
            int pages = Mathf.Max(1, (seats.Count + perPage - 1) / perPage);
            _tPage = Mathf.Clamp(_tPage, 0, pages - 1);
            if (seats.Count == 0)
                UiKit.Label(p, L.T("No seat has joined yet. On each seat: Student, then Join the class."), new Vector2(Margin, -290f), new Vector2(ContentWidth, 60f), 28f,
                    TextAlignmentOptions.MidlineLeft, UiKit.DimTextColor);
            for (int i = _tPage * perPage; i < seats.Count && i < (_tPage + 1) * perPage; i++)
            {
                ClassSeatInfo seat = seats[i];
                int k = i - _tPage * perPage;
                var pos = new Vector2(Margin + (k % cols) * (tw + 10f), -262f - (k / cols) * (th + 8f));
                Button tile = UiKit.Button(p, "", pos, new Vector2(tw, th), () => { _classSeat = seat.Name; _tText = ""; ShowClassSeat(); }, 20f);
                UiKit.SetButtonColor(tile, new Color(0.1f, 0.11f, 0.14f, 1f));
                RectTransform pictureBox = UiKit.Rect(tile.transform, "Picture", new Vector2(3f, -3f), new Vector2(tw - 6f, ph - 3f));
                var raw = pictureBox.gameObject.AddComponent<RawImage>();
                raw.raycastTarget = false;
                raw.color = Color.black;
                var t = new SeatTile
                {
                    Seat = seat.Name, Picture = raw, Frame = (Image)tile.targetGraphic,
                    Who = UiKit.Label(tile.transform, "", new Vector2(8f, -ph), new Vector2(tw - 16f, 28f), 19f, TextAlignmentOptions.MidlineLeft),
                    State = UiKit.Label(tile.transform, "", new Vector2(8f, -ph - 27f), new Vector2(tw - 16f, 28f), 18f, TextAlignmentOptions.MidlineLeft),
                };
                t.Who.raycastTarget = t.State.raycastTarget = false;
                _tiles.Add(t);
            }
            RefreshTiles();
            TeacherNote(p);
            TeacherPager(p, pages, ShowTeacherClass);
            StartCoroutine(ClassWatch(p, seats.Count));
        }

        private void RefreshTiles()
        {
            EduClassroom room = EduClassroom.Instance;
            if (room.Host == null) return;
            var byName = new Dictionary<string, ClassSeatInfo>();
            foreach (ClassSeatInfo s in room.Host.Seats()) byName[s.Name] = s;
            foreach (SeatTile t in _tiles)
            {
                if (t.Who == null || !byName.TryGetValue(t.Seat, out ClassSeatInfo seat)) continue;
                t.Who.text = seat.User.Length > 0 ? seat.User : seat.Name;
                t.State.text = (room.Hands.Contains(seat.Name) ? L.T("HAND UP") + "   " : "") + SeatState(seat, out Color colour);
                t.State.color = room.Hands.Contains(seat.Name) ? UiKit.AccentColor : colour;
                t.Frame.color = room.Hands.Contains(seat.Name) ? new Color(0.5f, 0.3f, 0.05f, 1f) : new Color(0.1f, 0.11f, 0.14f, 1f);
                if (seat.Connected && room.Pictures.TryGetValue(seat.Name, out Texture2D picture))
                {
                    t.Picture.texture = picture;
                    t.Picture.color = Color.white;
                }
                else t.Picture.color = Color.black;
            }
        }

        /// <summary>Keeps the Class tab current while it is on the screen: the words twice a second, the whole page when a seat joins.</summary>
        private IEnumerator ClassWatch(RectTransform page, int seatCount)
        {
            var wait = new WaitForSecondsRealtime(0.5f);
            while (page != null && _page == page)
            {
                yield return wait;
                if (page == null || _page != page) yield break;
                EduClassroom room = EduClassroom.Instance;
                if (room.Host == null || room.Host.Seats().Count != seatCount)
                {
                    ShowTeacherClass();
                    yield break;
                }
                RefreshTiles();
            }
        }

        /// <summary>Choosing the exercise to start now, on every seat or on the one chosen.</summary>
        private void ShowClassStart()
        {
            RectTransform p = TeacherPage(ShowTeacherClass);
            var blocks = new string[Course.Blocks.Length];
            for (int i = 0; i < blocks.Length; i++) blocks[i] = Course.Blocks[i] + ".  " + L.T(Course.BlockNames[i]);
            Stepper(p, Margin, 600f, blocks, _tBlock, i => { _tBlock = i; ShowClassStart(); });
            Stepper(p, Margin + 620f, 480f, AttemptNames(), _tLimit, i => { _tLimit = i; ShowClassStart(); });
            Button lockButton = UiKit.Button(p, _classLock ? L.T("Leaving: not allowed") : L.T("Leaving: allowed"), new Vector2(Margin + 1120f, -125f), new Vector2(ContentWidth - 1120f, 60f), () =>
            {
                _classLock = !_classLock;
                ShowClassStart();
            }, 22f);
            if (_classLock) UiKit.SetButtonColor(lockButton, UiKit.SelectedColor);
            UiKit.Label(p, _classTarget.Length > 0 ? L.F("Press an exercise to start it on the seat {0}", _classTarget) : L.T("Press an exercise to start it on every seat"), new Vector2(Margin, -200f),
                new Vector2(ContentWidth, 50f), 26f, TextAlignmentOptions.MidlineLeft, UiKit.DimTextColor);
            var course = new Course(EduSession.Activities.Values);
            float y = -260f;
            foreach (CourseEntry e in course.In(Course.Blocks[_tBlock]))
            {
                ActivityDefinition activity = e.Activity;
                UiKit.Button(p, activity.Number + ".  " + L.T(activity.Title), new Vector2(Margin, y), new Vector2(ContentWidth, 68f), () =>
                {
                    int n = EduClassroom.Instance.StartOn(_classTarget.Length > 0 ? _classTarget : null, activity, AttemptChoices[_tLimit], _classLock);
                    _tMessage = L.F("Started on {0} seats: {1}", n, activity.Number + ". " + L.T(activity.Title));
                    if (_classTarget.Length > 0) ShowClassSeat();
                    else ShowTeacherClass();
                }, 28f);
                y -= 76f;
                if (y < -(Height - 190f)) break;
            }
            TeacherNote(p);
        }

        /// <summary>One seat: its picture, what it is doing, and what the teacher can do to it.</summary>
        private void ShowClassSeat()
        {
            RectTransform p = TeacherPage(ShowTeacherClass);
            _tiles.Clear();
            EduClassroom room = EduClassroom.Instance;
            ClassSeatInfo seat = room.Host != null ? room.Host.Seats().Find(s => s.Name == _classSeat) : null;
            if (seat == null) { ShowTeacherClass(); return; }
            string seatName = seat.Name;
            const float pw = 720f, ph = 405f;
            RectTransform box = UiKit.Rect(p, "Picture", new Vector2(Margin, -125f), new Vector2(pw, ph));
            UiKit.Image(box, Color.black);
            var raw = UiKit.Rect(box, "Live", Vector2.zero, new Vector2(pw, ph)).gameObject.AddComponent<RawImage>();
            raw.raycastTarget = false;
            var tile = new SeatTile
            {
                Seat = seatName, Picture = raw, Frame = box.GetComponent<Image>(),
                Who = UiKit.Label(p, "", new Vector2(Margin, -125f - ph - 8f), new Vector2(pw, 44f), 30f, TextAlignmentOptions.MidlineLeft, UiKit.AccentColor),
                State = UiKit.Label(p, "", new Vector2(Margin, -125f - ph - 52f), new Vector2(pw, 40f), 26f, TextAlignmentOptions.MidlineLeft),
            };
            _tiles.Add(tile);
            RefreshTiles();
            tile.Frame.color = Color.black;
            UiKit.Label(p, seat.Name + "      " + seat.Address, new Vector2(Margin, -125f - ph - 94f), new Vector2(pw, 36f), 20f, TextAlignmentOptions.MidlineLeft, UiKit.DimTextColor);

            float x = Margin + pw + 30f, w = ContentWidth - pw - 30f, half = (w - 10f) / 2f, y = -125f;
            void Two(string a, Action doA, string b, Action doB)
            {
                UiKit.Button(p, a, new Vector2(x, y), new Vector2(half, 60f), () => doA(), 22f);
                if (b != null) UiKit.Button(p, b, new Vector2(x + half + 10f, y), new Vector2(half, 60f), () => doB(), 22f);
                y -= 68f;
            }
            Two(L.T("Start an exercise"), () => { _classTarget = seatName; ShowClassStart(); }, L.T("Start again"), () => room.Command(seatName, EduClassroom.DoRestart));
            Two(L.T("Pause"), () => room.Command(seatName, EduClassroom.DoPause), L.T("Resume"), () => room.Command(seatName, EduClassroom.DoResume));
            Two(L.T("To the menu"), () => room.Command(seatName, EduClassroom.DoLeave), room.Hands.Contains(seatName) ? L.T("Hand seen") : null, () =>
            {
                room.Hands.Remove(seatName);
                ShowClassSeat();
            });
            // Weather and failures, into the flight as it is flown.
            var winds = new string[WindChoices.Length];
            for (int i = 0; i < winds.Length; i++) winds[i] = L.F("Wind: {0} m/s", WindChoices[i]);
            UiKit.Button(p, "<", new Vector2(x, y), new Vector2(60f, 60f), () => SetClassWind(seatName, _classWind - 1));
            UiKit.Label(p, winds[Mathf.Clamp(_classWind, 0, winds.Length - 1)], new Vector2(x + 66f, y), new Vector2(w - 132f, 60f), 24f, TextAlignmentOptions.Midline);
            UiKit.Button(p, ">", new Vector2(x + w - 60f, y), new Vector2(60f, 60f), () => SetClassWind(seatName, _classWind + 1));
            y -= 68f;
            Two(L.T("Failure: positioning lost"), () => room.Command(seatName, EduClassroom.DoFailure, "gps_lost", 1), L.T("Failure: one motor weak"), () => room.Command(seatName, EduClassroom.DoFailure, "motor0", 0.5));
            Two(L.T("Failure: video lost"), () => room.Command(seatName, EduClassroom.DoFailure, "video_lost", 1), L.T("Clear the failures"), () =>
            {
                foreach (string name in new[] { "gps_lost", "motor0", "video_lost" }) room.Command(seatName, EduClassroom.DoFailure, name, 0);
            });
            // A short message or hint.
            InputBox(p, new Vector2(x, y - 4f), new Vector2(w - 170f, 64f), _tText, v => _tText = v, false, false, 120);
            UiKit.Button(p, L.T("Send"), new Vector2(x + w - 160f, y - 4f), new Vector2(160f, 64f), () =>
            {
                if (!string.IsNullOrWhiteSpace(_tText)) room.Command(seatName, EduClassroom.DoMessage, null, 0, _tText.Trim());
                _tText = "";
                ShowClassSeat();
            }, 24f);
            TeacherNote(p);
            StartCoroutine(SeatWatch(p));
        }

        private void SetClassWind(string seat, int index)
        {
            _classWind = Mathf.Clamp(index, 0, WindChoices.Length - 1);
            EduClassroom.Instance.Command(seat, EduClassroom.DoWeather, "wind", WindChoices[_classWind]);
            ShowClassSeat();
        }

        private IEnumerator SeatWatch(RectTransform page)
        {
            var wait = new WaitForSecondsRealtime(0.5f);
            while (page != null && _page == page)
            {
                yield return wait;
                if (page == null || _page != page) yield break;
                RefreshTiles();
                if (_tiles.Count > 0 && _tiles[0].Frame != null) _tiles[0].Frame.color = Color.black;
            }
        }

        // ---------- a seat joins the class ----------

        private ClassFinder _finder;
        private string _joinAddress = "", _joinCode = "", _joinTyped = "";
        private int _joinPort = ClassWire.DefaultPort;

        private void ShowJoinClass()
        {
            RectTransform p = NewPage(L.T("Join the class"));
            BackButton(p, () => { StopFinder(); ShowStudents(); });
            GameFlow.Catalog = tracks;
            EduClassroom room = EduClassroom.Instance;
            if (room.Joined || room.JoinState == "joining" || room.JoinState == "network")
            {
                StopFinder();
                string state = room.Joined ? L.F("Joined: {0}", room.ClassName) : room.JoinState == "joining" ? L.T("Joining…") : L.T("The teacher's PC does not answer. Trying again…");
                UiKit.Label(p, state, new Vector2(Margin, -150f), new Vector2(ContentWidth, 70f), 40f, TextAlignmentOptions.MidlineLeft, UiKit.AccentColor);
                UiKit.Label(p, L.T("The teacher can now start exercises on this seat and sees a small picture of the simulator, never of anything else on this PC. F2 raises your hand."),
                    new Vector2(Margin, -240f), new Vector2(ContentWidth, 100f), 26f, TextAlignmentOptions.TopLeft, UiKit.DimTextColor);
                if (room.Joined)
                    UiKit.Button(p, room.HandUp ? L.T("Lower my hand") : L.T("Raise my hand"), new Vector2(Margin, -370f), new Vector2(520f, 76f), () =>
                    {
                        room.RaiseHand(!room.HandUp);
                        ShowJoinClass();
                    }, 30f);
                UiKit.Button(p, L.T("Leave the class"), new Vector2(Margin, -460f), new Vector2(520f, 76f), () =>
                {
                    room.LeaveClass();
                    ShowJoinClass();
                }, 30f);
                StartCoroutine(JoinWatch(p, room.JoinState));
                return;
            }

            if (room.JoinState == "code") _tMessage = L.T("Wrong code");
            else if (room.JoinState == "key") _tMessage = L.T("This is not the class this seat joined before. Ask the teacher.");
            if (_finder == null)
            {
                try { _finder = new ClassFinder(); }
                catch (SocketException) { /* the list stays empty; the address can still be typed */ }
            }
            List<FoundClass> found = _finder != null ? _finder.Classes() : new List<FoundClass>();
            UiKit.Label(p, found.Count > 0 ? L.T("Classes found on the network: press yours") : L.T("Looking for classes on the network…"), new Vector2(Margin, -130f),
                new Vector2(ContentWidth, 50f), 28f, TextAlignmentOptions.MidlineLeft, UiKit.DimTextColor);
            float y = -190f;
            for (int i = 0; i < found.Count && i < 4; i++)
            {
                FoundClass c = found[i];
                UiKit.Button(p, c.Name + "      " + c.Address, new Vector2(Margin, y), new Vector2(ContentWidth, 72f), () =>
                {
                    _joinAddress = c.Address;
                    _joinPort = c.Port;
                    _joinCode = "";
                    ShowJoinCode();
                }, 30f);
                y -= 82f;
            }
            y = Mathf.Min(y, -530f);
            UiKit.Label(p, L.T("Or type the address the teacher's screen shows"), new Vector2(Margin, y), new Vector2(ContentWidth, 50f), 26f, TextAlignmentOptions.MidlineLeft, UiKit.DimTextColor);
            InputBox(p, new Vector2(Margin, y - 60f), new Vector2(600f, 76f), _joinTyped, v => _joinTyped = v, false, false, 60);
            UiKit.Button(p, L.T("Next"), new Vector2(Margin + 620f, y - 60f), new Vector2(240f, 76f), () =>
            {
                string typed = (_joinTyped ?? "").Trim().Replace(" ", "");
                int port = ClassWire.DefaultPort, colon = typed.LastIndexOf(':');
                if (colon > 0 && int.TryParse(typed.Substring(colon + 1), out int given) && given > 0 && given < 65536)
                {
                    port = given;
                    typed = typed.Substring(0, colon);
                }
                if (typed.Length == 0) return;
                _joinAddress = typed;
                _joinPort = port;
                _joinCode = "";
                ShowJoinCode();
            }, 30f);
            UiKit.Label(p, _tMessage, new Vector2(Margin + 270f, -(Height - 110f)), new Vector2(ContentWidth - 270f, 70f), 26f, TextAlignmentOptions.MidlineLeft, UiKit.AccentColor);
            _tMessage = "";
            StartCoroutine(JoinWatch(p, found.Count.ToString(CultureInfo.InvariantCulture)));
        }

        private void StopFinder()
        {
            _finder?.Dispose();
            _finder = null;
        }

        /// <summary>Draws the page again when what it shows has changed: a class found, or joining finished.</summary>
        private IEnumerator JoinWatch(RectTransform page, string shown)
        {
            var wait = new WaitForSecondsRealtime(1f);
            while (page != null && _page == page)
            {
                yield return wait;
                if (page == null || _page != page) break;
                EduClassroom room = EduClassroom.Instance;
                bool busy = room.Joined || room.JoinState == "joining" || room.JoinState == "network";
                string now = busy || room.JoinState.Length > 0 ? room.JoinState : (_finder != null ? _finder.Classes().Count : 0).ToString(CultureInfo.InvariantCulture);
                if (now == shown) continue;
                ShowJoinClass();
                yield break;
            }
            if (_page != page && (_page == null || _page.name != "Page " + L.T("Join the class"))) StopFinder();
        }

        /// <summary>The class code, on a keypad.</summary>
        private void ShowJoinCode()
        {
            RectTransform p = NewPage(L.T("Join the class"));
            BackButton(p, ShowJoinClass);
            UiKit.Label(p, L.T("Type the code on the teacher's screen"), new Vector2(Margin, -130f), new Vector2(ContentWidth, 60f), 34f, TextAlignmentOptions.Center);
            UiKit.Label(p, _joinCode.Length == 0 ? "·" : _joinCode, new Vector2(Margin, -200f), new Vector2(ContentWidth, 80f), 60f, TextAlignmentOptions.Center, UiKit.AccentColor);
            string[] keys = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "<", "0", "OK" };
            float x0 = Margin + (ContentWidth - (3 * 190f - 20f)) * 0.5f;
            for (int i = 0; i < keys.Length; i++)
            {
                string key = keys[i];
                UiKit.Button(p, key, new Vector2(x0 + (i % 3) * 190f, -350f - (i / 3) * 110f), new Vector2(170f, 96f), () =>
                {
                    if (key == "<") _joinCode = _joinCode.Length > 0 ? _joinCode.Substring(0, _joinCode.Length - 1) : "";
                    else if (key != "OK") { if (_joinCode.Length < 8) _joinCode += key; }
                    else if (_joinCode.Length >= 4)
                    {
                        StopFinder();
                        EduClassroom.Instance.Join(_joinAddress, _joinPort, _joinCode);
                        _joinCode = "";
                        ShowJoinClass();
                        return;
                    }
                    ShowJoinCode();
                }, 44f);
            }
        }
    }
}
