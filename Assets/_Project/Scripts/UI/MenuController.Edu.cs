using System;
using System.Collections.Generic;
using Ushqyr.Core;
using Ushqyr.Edu;
using Ushqyr.Game;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ushqyr.UI
{
    /// <summary>
    /// "Who is flying?" (EDU_PLAN F5): the student picks their name from the class list, types a PIN on a keypad if
    /// they set one, adds themself, or flies as the seat with no person named. Flights are stored under the chosen
    /// profile. Everything stays in the class database on this PC.
    /// </summary>
    public sealed partial class MenuController
    {
        private const int StudentsPerPage = 10;

        private int _eduGroup, _eduPage;
        private EduUser _pinUser;
        private string _pin = "", _pinMessage = "", _newName = "", _newPin = "", _newMessage = "";
        private bool _pinForNew;
        private TMP_Text _pinDots, _pinNote;

        /// <summary>The class data, or null (with the reason in <paramref name="error"/>) when it cannot be opened.</summary>
        private static EduStore Students(out string error)
        {
            error = null;
            try { return EduSession.Store; }
            catch (Exception e)
            {
                error = e.Message;
                Debug.LogError("[Edu] class data: " + e);
                return null;
            }
        }

        /// <summary>"Student: name" for the main menu's button.</summary>
        private static string StudentLabel()
        {
            EduUser user = null;
            try { user = EduSession.CurrentUser; } catch (Exception) { /* shown as not chosen; the page itself reports the reason */ }
            string who = user == null ? L.T("not chosen") : user.Role == "seat" ? L.F("Seat {0}", user.Name) : user.Name;
            return L.T("Student") + ":  " + who;
        }

        // ---------- the training hub ----------

        private string _trainingBlock = "A";
        private string _trainingNotice;

        /// <summary>
        /// The training hub (EDU_PLAN §4): the exercises in their blocks, what the student has reached on each, and
        /// the way into an exercise. A block opens when the one before it is passed.
        /// </summary>
        private void ShowTraining()
        {
            RectTransform p = NewPage(L.T("Training"));
            BackButton(p);
            EduStore store = Students(out string error);
            long user = 0;
            if (store != null)
            {
                EduUser who = EduSession.CurrentUser;
                if (who == null)
                {
                    EduSession.UseSeat();
                    who = EduSession.CurrentUser;
                }
                user = who != null ? who.Id : 0;
            }
            var course = new Course(EduSession.Activities.Values, store, user);
            course.Progress(out int passed, out int total);
            UiKit.Button(p, StudentLabel(), new Vector2(Width - Margin - 560f, -35f), new Vector2(560f, 70f), ShowStudents, 26f);
            UiKit.Label(p, error != null ? L.F("The class data could not be opened: {0}", error) : L.F("Passed: {0} of {1}", passed, total),
                new Vector2(Margin + 420f, -40f), new Vector2(error != null ? 500f : 250f, 60f), 28f, TextAlignmentOptions.MidlineLeft, UiKit.DimTextColor);
            // The check ride passed: the certificate can be saved and printed.
            if (store != null && Certificate.Earned(course))
                UiKit.Button(p, L.T("Certificate"), new Vector2(Margin + 680f, -35f), new Vector2(220f, 70f), () =>
                {
                    EduUser who = EduSession.CurrentUser;
                    try
                    {
                        string file = EduCertificates.Save(course, who);
                        _trainingNotice = L.F("Certificate saved: {0}", file);
                        EduCertificates.Open(file);
                    }
                    catch (Exception e)
                    {
                        _trainingNotice = L.F("The certificate was not saved: {0}", e.Message);
                    }
                    ShowTraining();
                }, 26f);

            if (store != null) UiKit.Button(p, L.T("My flights"), new Vector2(Width - Margin - 300f, -(Height - 110f)), new Vector2(300f, 70f), ShowFlights, 28f);
            // What the teacher has given this student (or everyone): a way straight to those exercises.
            if (store != null && user != 0)
            {
                List<Assignment> given = store.AssignmentsFor(user);
                int left = 0;
                foreach (Assignment a in given)
                {
                    CourseEntry entry = course.Find(a.ActivityId);
                    if (entry != null && !entry.Passed) left++;
                }
                if (given.Count > 0)
                {
                    Button tasks = UiKit.Button(p, L.F("My tasks: {0} to do", left), new Vector2(Margin + 270f, -(Height - 110f)), new Vector2(520f, 70f), ShowMyTasks, 28f);
                    if (left > 0) UiKit.SetButtonColor(tasks, UiKit.SelectedColor);
                }
            }

            // The blocks.
            float tab = (ContentWidth - 4f * 12f) / 5f;
            for (int i = 0; i < Course.Blocks.Length; i++)
            {
                string block = Course.Blocks[i];
                bool open = course.Open(block, EduSession.EverythingUnlocked);
                Button b = UiKit.Button(p, block, new Vector2(Margin + i * (tab + 12f), -125f), new Vector2(tab, 64f), () =>
                {
                    _trainingBlock = block;
                    ShowTraining();
                }, 32f);
                // A finished block is green, a locked one dark; the chosen one has a bar under it.
                if (course.Done(block) && course.In(block).Count > 0) UiKit.SetButtonColor(b, new Color(0.16f, 0.36f, 0.22f, 1f));
                else if (!open) UiKit.SetButtonColor(b, new Color(0.12f, 0.13f, 0.16f, 1f));
                if (block == _trainingBlock)
                    UiKit.Image(UiKit.Rect(p, "Chosen", new Vector2(Margin + i * (tab + 12f), -191f), new Vector2(tab, 6f)), UiKit.AccentColor);
            }
            int index = Mathf.Max(0, Array.IndexOf(Course.Blocks, _trainingBlock));
            bool blockOpen = course.Open(_trainingBlock, EduSession.EverythingUnlocked);
            // What just happened (the certificate was saved, and where) is shown once in place of the block's name.
            string heading = _trainingNotice ?? _trainingBlock + ".  " + L.T(Course.BlockNames[index]) + (blockOpen ? "" : "     " + L.T("Opens when the block before it is passed"));
            UiKit.Label(p, heading, new Vector2(Margin, -200f), new Vector2(ContentWidth, 56f), _trainingNotice != null ? 24f : 34f, TextAlignmentOptions.MidlineLeft,
                blockOpen || _trainingNotice != null ? UiKit.AccentColor : UiKit.DimTextColor);
            _trainingNotice = null;

            IReadOnlyList<CourseEntry> entries = course.In(_trainingBlock);
            if (entries.Count == 0)
                UiKit.Label(p, L.T("The exercises of this block are not in this version yet."), new Vector2(Margin, -280f), new Vector2(ContentWidth, 60f), 30f,
                    TextAlignmentOptions.MidlineLeft, UiKit.DimTextColor);
            float y = -268f;
            for (int i = 0; i < entries.Count && i < 7; i++)
            {
                CourseEntry e = entries[i];
                ActivityDefinition activity = e.Activity;
                Button row = UiKit.Button(p, "", new Vector2(Margin, y), new Vector2(ContentWidth, 76f), () => StartExercise(activity, blockOpen), 30f);
                if (!blockOpen) UiKit.SetButtonColor(row, new Color(0.12f, 0.13f, 0.16f, 1f));
                UiKit.Label(row.transform, activity.Number + "   " + L.T(activity.Title), new Vector2(24f, 0f), new Vector2(ContentWidth - 420f, 76f), 32f,
                    TextAlignmentOptions.MidlineLeft, blockOpen ? UiKit.TextColor : UiKit.DimTextColor);
                string best = e.Best == null ? L.T("not flown") : e.Best.Value.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
                UiKit.Label(row.transform, best, new Vector2(ContentWidth - 390f, 0f), new Vector2(200f, 76f), 32f, TextAlignmentOptions.MidlineRight,
                    e.Passed ? new Color(0.4f, 0.95f, 0.5f) : UiKit.DimTextColor);
                for (int s = 0; s < 3; s++)
                {
                    RectTransform star = UiKit.Rect(row.transform, "Star", new Vector2(ContentWidth - 160f + s * 44f, -21f), new Vector2(34f, 34f));
                    UiKit.Image(star, s < e.Stars ? new Color(1f, 0.8f, 0.15f) : new Color(1f, 1f, 1f, 0.12f));
                }
                y -= 86f;
            }
        }

        /// <summary>The exercises the teacher has given this student, in the order given, with what is left of each.</summary>
        private void ShowMyTasks()
        {
            RectTransform p = NewPage(L.T("My tasks"));
            BackButton(p, ShowTraining);
            EduStore store = Students(out _);
            EduUser who = null;
            try { who = EduSession.CurrentUser; } catch (Exception) { /* nothing to show */ }
            if (store == null || who == null) return;
            UiKit.Label(p, StudentLabel(), new Vector2(Width - Margin - 700f, -40f), new Vector2(700f, 60f), 28f, TextAlignmentOptions.MidlineRight, UiKit.DimTextColor);
            var course = new Course(EduSession.Activities.Values, store, who.Id);
            List<Assignment> given = store.AssignmentsFor(who.Id);
            float y = -130f;
            for (int i = 0; i < given.Count && i < 8; i++)
            {
                Assignment a = given[i];
                CourseEntry entry = course.Find(a.ActivityId);
                if (entry == null) continue;
                int used = store.AttemptsUsed(a, who.Id);
                bool may = a.AttemptLimit <= 0 || used < a.AttemptLimit;
                ActivityDefinition activity = entry.Activity;
                Button row = UiKit.Button(p, "", new Vector2(Margin, y), new Vector2(ContentWidth, 76f), () =>
                {
                    if (!may) return;
                    EduActivityRunner.SetPendingAssignment(a);
                    StartExercise(activity, true);
                }, 30f);
                if (!may) UiKit.SetButtonColor(row, new Color(0.12f, 0.13f, 0.16f, 1f));
                UiKit.Label(row.transform, (a.Lesson.Length > 0 ? a.Lesson + "  ·  " : "") + activity.Number + "   " + L.T(activity.Title), new Vector2(24f, 0f),
                    new Vector2(ContentWidth - 640f, 76f), 30f, TextAlignmentOptions.MidlineLeft, may ? UiKit.TextColor : UiKit.DimTextColor);
                string attempts = a.AttemptLimit > 0 ? L.F("Attempts: {0} of {1}", used, a.AttemptLimit) : L.F("Attempts: {0}", used);
                UiKit.Label(row.transform, attempts, new Vector2(ContentWidth - 600f, 0f), new Vector2(300f, 76f), 24f, TextAlignmentOptions.MidlineLeft, UiKit.DimTextColor);
                string best = entry.Best == null ? L.T("not flown") : entry.Best.Value.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
                UiKit.Label(row.transform, entry.Passed ? L.T("Passed") + "   " + best : best, new Vector2(ContentWidth - 300f, 0f), new Vector2(280f, 76f), 28f,
                    TextAlignmentOptions.MidlineRight, entry.Passed ? new Color(0.4f, 0.95f, 0.5f) : UiKit.DimTextColor);
                y -= 86f;
            }
        }

        private int _flightsPage;

        /// <summary>The replay library: the student's own attempts, newest first, each to be watched again.</summary>
        private void ShowFlights()
        {
            RectTransform p = NewPage(L.T("My flights"));
            BackButton(p, ShowTraining);
            EduStore store = Students(out string error);
            EduUser who = null;
            try { who = EduSession.CurrentUser; } catch (Exception) { /* reported just below */ }
            if (store == null || who == null)
            {
                UiKit.Label(p, error != null ? L.F("The class data could not be opened: {0}", error) : L.T("No flights recorded yet."), new Vector2(Margin, -140f),
                    new Vector2(ContentWidth, 200f), 30f, TextAlignmentOptions.TopLeft, UiKit.DimTextColor);
                return;
            }
            UiKit.Label(p, StudentLabel(), new Vector2(Width - Margin - 700f, -40f), new Vector2(700f, 60f), 28f, TextAlignmentOptions.MidlineRight, UiKit.DimTextColor);
            List<AttemptRecord> attempts = store.Attempts(who.Id);
            if (attempts.Count == 0)
                UiKit.Label(p, L.T("No flights recorded yet."), new Vector2(Margin, -140f), new Vector2(ContentWidth, 60f), 30f, TextAlignmentOptions.MidlineLeft, UiKit.DimTextColor);
            const int rows = 8;
            int pages = Mathf.Max(1, (attempts.Count + rows - 1) / rows);
            _flightsPage = Mathf.Clamp(_flightsPage, 0, pages - 1);
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            float y = -130f;
            for (int i = _flightsPage * rows; i < attempts.Count && i < (_flightsPage + 1) * rows; i++)
            {
                AttemptRecord attempt = attempts[i];
                EduSession.Activities.TryGetValue(attempt.ActivityId, out ActivityDefinition activity);
                // It can be watched when its exercise is still shipped and its recording is still on the disk.
                bool there = activity != null && !string.IsNullOrEmpty(attempt.ReplayPath) && System.IO.File.Exists(store.ReplayFile(attempt.ReplayPath));
                Button row = UiKit.Button(p, "", new Vector2(Margin, y), new Vector2(ContentWidth, 76f), () => WatchFlight(attempt, activity, there), 30f);
                if (!there) UiKit.SetButtonColor(row, new Color(0.12f, 0.13f, 0.16f, 1f));
                string when = DateTime.TryParse(attempt.Started, inv, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out DateTime at)
                    ? at.ToLocalTime().ToString("dd.MM.yyyy   HH:mm", inv) : attempt.Started;
                UiKit.Label(row.transform, when, new Vector2(24f, 0f), new Vector2(330f, 76f), 28f, TextAlignmentOptions.MidlineLeft, UiKit.DimTextColor);
                UiKit.Label(row.transform, activity != null ? activity.Number + "   " + L.T(activity.Title) : attempt.ActivityId, new Vector2(370f, 0f),
                    new Vector2(ContentWidth - 620f, 76f), 30f, TextAlignmentOptions.MidlineLeft, there ? UiKit.TextColor : UiKit.DimTextColor);
                UiKit.Label(row.transform, attempt.Final.ToString("0", inv), new Vector2(ContentWidth - 240f, 0f), new Vector2(120f, 76f), 32f, TextAlignmentOptions.MidlineRight,
                    attempt.Passed ? new Color(0.4f, 0.95f, 0.5f) : UiKit.DimTextColor);
                UiKit.Label(row.transform, EduUi.Clock((float)attempt.Duration), new Vector2(ContentWidth - 110f, 0f), new Vector2(90f, 76f), 26f, TextAlignmentOptions.MidlineRight, UiKit.DimTextColor);
                y -= 86f;
            }
            if (pages > 1)
            {
                UiKit.Button(p, "<", new Vector2(Width - Margin - 400f, -(Height - 110f)), new Vector2(90f, 70f), () => { _flightsPage--; ShowFlights(); });
                UiKit.Label(p, (_flightsPage + 1) + " / " + pages, new Vector2(Width - Margin - 300f, -(Height - 110f)), new Vector2(200f, 70f), 30f, TextAlignmentOptions.Midline, UiKit.DimTextColor);
                UiKit.Button(p, ">", new Vector2(Width - Margin - 90f, -(Height - 110f)), new Vector2(90f, 70f), () => { _flightsPage++; ShowFlights(); });
            }
        }

        private void WatchFlight(AttemptRecord attempt, ActivityDefinition activity, bool there)
        {
            if (!there) return;
            CommitIfDirty();
            EduActivityRunner.SetPending(activity);
            EduActivityRunner.SetPendingReplay(attempt);
            if (GameFlow.Activity(tracks, activity.Setup.Place)) return;
            EduActivityRunner.SetPending(null);
            EduActivityRunner.SetPendingReplay(null);
        }

        private void StartExercise(ActivityDefinition activity, bool open)
        {
            if (!open) return;
            CommitIfDirty();
            EduActivityRunner.SetPending(activity);
            if (!GameFlow.Activity(tracks, activity.Setup.Place)) EduActivityRunner.SetPending(null);
        }

        private void ShowStudents()
        {
            RectTransform p = NewPage(L.T("Who is flying?"));
            BackButton(p);
            EduStore store = Students(out string error);
            if (store == null)
            {
                UiKit.Label(p, L.F("The class data could not be opened: {0}", error), new Vector2(Margin, -140f), new Vector2(ContentWidth, 200f), 30f,
                    TextAlignmentOptions.TopLeft, UiKit.AccentColor);
                return;
            }

            // The class link: this seat joins the teacher's class over the school's network.
            Button join = UiKit.Button(p, EduClassroom.Active && EduClassroom.Instance.Joined ? L.F("Joined: {0}", EduClassroom.Instance.ClassName) : L.T("Join the class"),
                new Vector2(Width - Margin - 300f - 20f - 420f, -35f), new Vector2(420f, 70f), () =>
                {
                    _tMessage = "";
                    ShowJoinClass();
                }, 26f);
            if (EduClassroom.Active && EduClassroom.Instance.Joined) UiKit.SetButtonColor(join, UiKit.SelectedColor);

            // The teacher's console, behind the teacher's password.
            UiKit.Button(p, L.T("Teacher"), new Vector2(Width - Margin - 300f, -35f), new Vector2(300f, 70f), () =>
            {
                _tMessage = "";
                ShowTeacher();
            }, 28f);

            List<EduGroup> groups = store.Groups();
            if (_eduGroup > groups.Count) _eduGroup = 0;
            float y = -130f;
            if (groups.Count > 0)
            {
                var names = new string[groups.Count + 1];
                names[0] = L.T("Everyone");
                for (int i = 0; i < groups.Count; i++) names[i + 1] = groups[i].Name;
                RectTransform row = UiKit.Rect(p, "Group", new Vector2(Margin, y), new Vector2(ContentWidth, 60f));
                UiKit.StepperRow(row, L.T("Group"), 0f, names, _eduGroup, i =>
                {
                    _eduGroup = i;
                    _eduPage = 0;
                    ShowStudents();
                }, ContentWidth);
                y -= 90f;
            }

            List<EduUser> students = store.Users(_eduGroup > 0 ? groups[_eduGroup - 1].Id : (long?)null);
            students.RemoveAll(u => u.Role != "student");
            int pages = Mathf.Max(1, (students.Count + StudentsPerPage - 1) / StudentsPerPage);
            _eduPage = Mathf.Clamp(_eduPage, 0, pages - 1);
            long current = EduSession.CurrentUserId;
            float half = (ContentWidth - 30f) * 0.5f;
            if (students.Count == 0)
                UiKit.Label(p, L.T("No students yet. Add yourself, or fly without a name."), new Vector2(Margin, y - 20f), new Vector2(ContentWidth, 120f), 32f,
                    TextAlignmentOptions.TopLeft, UiKit.DimTextColor);
            for (int i = 0; i < StudentsPerPage; i++)
            {
                int index = _eduPage * StudentsPerPage + i;
                if (index >= students.Count) break;
                EduUser user = students[index];
                var pos = new Vector2(Margin + (i % 2) * (half + 30f), y - (i / 2) * 84f);
                Button b = UiKit.Button(p, user.Name + (user.HasPin ? "   · PIN" : ""), pos, new Vector2(half, 72f), () => ChooseStudent(user), 32f);
                if (user.Id == current) UiKit.SetButtonColor(b, UiKit.SelectedColor);
            }
            if (pages > 1)
            {
                float py = y - 5 * 84f - 4f;
                UiKit.Button(p, "<", new Vector2(Margin + ContentWidth * 0.5f - 200f, py), new Vector2(90f, 60f), () => { _eduPage--; ShowStudents(); });
                UiKit.Label(p, (_eduPage + 1) + " / " + pages, new Vector2(Margin + ContentWidth * 0.5f - 100f, py), new Vector2(200f, 60f), 30f, TextAlignmentOptions.Center);
                UiKit.Button(p, ">", new Vector2(Margin + ContentWidth * 0.5f + 110f, py), new Vector2(90f, 60f), () => { _eduPage++; ShowStudents(); });
            }

            float by = -(Height - 110f);
            UiKit.Button(p, L.T("New student"), new Vector2(Margin + 270f, by), new Vector2(420f, 70f), () =>
            {
                _newName = _newPin = _newMessage = "";
                ShowNewStudent();
            }, 30f);
            Button seat = UiKit.Button(p, L.T("Without a name (this seat)"), new Vector2(Margin + 720f, by), new Vector2(ContentWidth - 720f, 70f), () =>
            {
                EduSession.UseSeat();
                ShowMain();
            }, 30f);
            EduUser now = EduSession.CurrentUser;
            if (now != null && now.Role == "seat") UiKit.SetButtonColor(seat, UiKit.SelectedColor);
        }

        private void ChooseStudent(EduUser user)
        {
            if (!user.HasPin)
            {
                EduSession.CurrentUserId = user.Id;
                ShowMain();
                return;
            }
            _pinUser = user;
            _pinForNew = false;
            _pin = _pinMessage = "";
            ShowPinPad();
        }

        /// <summary>A keypad for the PIN: no keyboard is needed, so it also works with a mouse alone or in a headset.</summary>
        private void ShowPinPad()
        {
            RectTransform p = NewPage("PIN");
            BackButton(p, _pinForNew ? ShowNewStudent : (UnityEngine.Events.UnityAction)ShowStudents);
            string ask = _pinForNew ? L.T("Choose a PIN: 4 to 12 digits") : L.F("Enter the PIN of {0}", _pinUser != null ? _pinUser.Name : "…");
            UiKit.Label(p, ask, new Vector2(Margin, -130f), new Vector2(ContentWidth, 60f), 34f, TextAlignmentOptions.Center);
            _pinDots = UiKit.Label(p, "", new Vector2(Margin, -200f), new Vector2(ContentWidth, 80f), 60f, TextAlignmentOptions.Center, UiKit.AccentColor);
            _pinNote = UiKit.Label(p, _pinMessage, new Vector2(Margin, -280f), new Vector2(ContentWidth, 50f), 30f, TextAlignmentOptions.Center, UiKit.AccentColor);
            string[] keys = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "<", "0", "OK" };
            float x0 = Margin + (ContentWidth - (3 * 190f - 20f)) * 0.5f;
            for (int i = 0; i < keys.Length; i++)
            {
                string key = keys[i];
                UiKit.Button(p, key, new Vector2(x0 + (i % 3) * 190f, -350f - (i / 3) * 110f), new Vector2(170f, 96f), () => PinKey(key), 44f);
            }
            if (_pinForNew)
                UiKit.Label(p, L.T("A PIN keeps classmates out of your results. The teacher can reset it."), new Vector2(Margin + 270f, -(Height - 110f)),
                    new Vector2(ContentWidth - 270f, 70f), 26f, TextAlignmentOptions.MidlineLeft, UiKit.DimTextColor);
            ShowPinDots();
        }

        private void ShowPinDots()
        {
            if (_pinDots != null) _pinDots.text = _pin.Length == 0 ? "·" : new string('•', _pin.Length);
            if (_pinNote != null) _pinNote.text = _pinMessage;
        }

        private void PinKey(string key)
        {
            _pinMessage = "";
            if (key == "<") _pin = _pin.Length > 0 ? _pin.Substring(0, _pin.Length - 1) : "";
            else if (key != "OK") { if (_pin.Length < 12) _pin += key; }
            else if (_pinForNew)
            {
                if (_pin.Length != 0 && _pin.Length < 4) _pinMessage = L.T("Choose a PIN: 4 to 12 digits");
                else
                {
                    _newPin = _pin; // empty: no PIN
                    ShowNewStudent();
                    return;
                }
            }
            else
            {
                EduStore store = Students(out _);
                if (store != null && _pinUser != null && store.CheckPin(_pinUser.Id, _pin))
                {
                    EduSession.CurrentUserId = _pinUser.Id;
                    _pin = "";
                    ShowMain();
                    return;
                }
                _pin = "";
                _pinMessage = L.T("Wrong PIN");
            }
            ShowPinDots();
        }

        private void ShowNewStudent()
        {
            RectTransform p = NewPage(L.T("New student"));
            BackButton(p, ShowStudents);
            UiKit.Label(p, L.T("Type your name"), new Vector2(Margin, -150f), new Vector2(ContentWidth, 60f), 32f);
            RectTransform box = UiKit.Rect(p, "Name", new Vector2(Margin, -220f), new Vector2(ContentWidth, 84f));
            UiKit.Image(box, new Color(1f, 1f, 1f, 0.1f), true);
            TextMeshProUGUI text = UiKit.Label(box, "", new Vector2(20f, 0f), new Vector2(ContentWidth - 40f, 84f), 38f);
            var field = box.gameObject.AddComponent<TMP_InputField>();
            field.textComponent = text;
            field.textViewport = box;
            field.characterLimit = 60;
            field.targetGraphic = box.GetComponent<Image>();
            field.text = _newName;
            field.onValueChanged.AddListener(v => _newName = v);

            UiKit.Button(p, _newPin.Length > 0 ? L.T("PIN: set") : L.T("PIN: none"), new Vector2(Margin, -340f), new Vector2(520f, 72f), () =>
            {
                _pinForNew = true;
                _pin = _pinMessage = "";
                ShowPinPad();
            }, 30f);
            UiKit.Label(p, L.T("A PIN keeps classmates out of your results. The teacher can reset it."), new Vector2(Margin + 550f, -340f),
                new Vector2(ContentWidth - 550f, 72f), 26f, TextAlignmentOptions.MidlineLeft, UiKit.DimTextColor);
            UiKit.Label(p, _newMessage, new Vector2(Margin, -440f), new Vector2(ContentWidth, 60f), 30f, TextAlignmentOptions.MidlineLeft, UiKit.AccentColor);
            UiKit.Button(p, L.T("Add"), new Vector2(Width - Margin - 420f, -(Height - 110f)), new Vector2(420f, 70f), AddStudent, 32f);
        }

        private void AddStudent()
        {
            EduStore store = Students(out string error);
            string name = (_newName ?? "").Trim();
            if (store == null) _newMessage = L.F("The class data could not be opened: {0}", error);
            else if (name.Length == 0) _newMessage = L.T("A name is needed");
            else
            {
                long id = store.AddUser(name, "student", _newPin.Length > 0 ? _newPin : null);
                List<EduGroup> groups = store.Groups();
                if (_eduGroup > 0 && _eduGroup <= groups.Count) store.AddToGroup(groups[_eduGroup - 1].Id, id);
                EduSession.CurrentUserId = id;
                _newName = _newPin = _newMessage = "";
                ShowMain();
                return;
            }
            ShowNewStudent();
        }
    }
}
