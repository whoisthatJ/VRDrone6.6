using System;
using System.Collections.Generic;
using System.Globalization;
using Ushqyr.Core;
using Ushqyr.Edu;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ushqyr.UI
{
    /// <summary>
    /// The teacher's console on this PC (EDU_PLAN §9), behind the teacher's password: the journal (students down,
    /// exercises across, every attempt with its replay and the teacher's own mark), the class lists, the reports and
    /// the settings with backup and restore. Everything is read from and written to the class database on this PC.
    /// </summary>
    public sealed partial class MenuController
    {
        private static readonly string[] TeacherTabs = { "Journal", "Students", "Tasks", "Reports", "Settings" };

        private bool _teacherIn;
        private int _tTab, _tGroup, _tBlock, _tPage;
        private string _tMessage = "", _tText = "", _tText2 = "";
        private long _tArmed; // the thing a second press will delete or restore (0: nothing)

        // ---------- the way in ----------

        private void ShowTeacher()
        {
            if (!_teacherIn)
            {
                _tText = "";
                ShowTeacherPassword();
                return;
            }
            if (_tTab == 1) ShowTeacherStudents();
            else if (_tTab == 2) ShowTeacherTasks();
            else if (_tTab == 3) ShowTeacherReports();
            else if (_tTab == 4) ShowTeacherSettings();
            else ShowTeacherJournal();
        }

        private void ShowTeacherPassword()
        {
            RectTransform p = NewPage(L.T("Teacher"));
            BackButton(p, ShowStudents);
            EduStore store = Students(out string error);
            if (store == null)
            {
                UiKit.Label(p, L.F("The class data could not be opened: {0}", error), new Vector2(Margin, -140f), new Vector2(ContentWidth, 200f), 30f,
                    TextAlignmentOptions.TopLeft, UiKit.AccentColor);
                return;
            }
            UiKit.Label(p, store.HasTeacherPassword ? L.T("Type the teacher's password") : L.T("Choose the teacher's password: 4 characters or more"),
                new Vector2(Margin, -170f), new Vector2(ContentWidth, 60f), 34f);
            TMP_InputField field = InputBox(p, new Vector2(Margin, -250f), new Vector2(700f, 84f), _tText, v => _tText = v, true);
            field.onSubmit.AddListener(_ => TeacherEnter());
            field.ActivateInputField();
            UiKit.Button(p, "OK", new Vector2(Margin + 730f, -250f), new Vector2(200f, 84f), TeacherEnter, 34f);
            UiKit.Label(p, _tMessage, new Vector2(Margin, -360f), new Vector2(ContentWidth, 60f), 30f, TextAlignmentOptions.MidlineLeft, UiKit.AccentColor);
            if (!store.HasTeacherPassword)
                UiKit.Label(p, L.T("The password keeps students out of the journal on this PC. Write it down: it cannot be read back."), new Vector2(Margin, -440f),
                    new Vector2(ContentWidth, 80f), 26f, TextAlignmentOptions.TopLeft, UiKit.DimTextColor);
            _tMessage = "";
        }

        private void TeacherEnter()
        {
            EduStore store = Students(out _);
            if (store == null) return;
            if (!store.HasTeacherPassword)
            {
                if (_tText.Length < 4 || _tText.Length > 64) _tMessage = L.T("Choose the teacher's password: 4 characters or more");
                else
                {
                    store.SetTeacherPassword(_tText);
                    _teacherIn = true;
                }
            }
            else if (store.CheckTeacherPassword(_tText)) _teacherIn = true;
            else _tMessage = L.T("Wrong password");
            _tText = "";
            if (_teacherIn) ShowTeacher();
            else ShowTeacherPassword();
        }

        // ---------- shared pieces ----------

        /// <summary>A box to type in. <paramref name="changed"/> is told every change.</summary>
        private static TMP_InputField InputBox(RectTransform p, Vector2 pos, Vector2 size, string value, Action<string> changed, bool password = false,
            bool lines = false, int limit = 80)
        {
            RectTransform box = UiKit.Rect(p, "Input", pos, size);
            UiKit.Image(box, new Color(1f, 1f, 1f, 0.1f), true);
            TextMeshProUGUI text = UiKit.Label(box, "", new Vector2(16f, lines ? -10f : 0f), new Vector2(size.x - 32f, size.y - (lines ? 20f : 0f)), lines ? 26f : 34f,
                lines ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.MidlineLeft);
            var field = box.gameObject.AddComponent<TMP_InputField>();
            field.textComponent = text;
            field.textViewport = box;
            field.characterLimit = limit;
            field.targetGraphic = box.GetComponent<Image>();
            if (password) field.contentType = TMP_InputField.ContentType.Password;
            if (lines) field.lineType = TMP_InputField.LineType.MultiLineNewline;
            field.text = value ?? "";
            field.onValueChanged.AddListener(v => changed(v));
            return field;
        }

        /// <summary>A page of the console: its title, the four tabs, and "Back" to the main menu.</summary>
        private RectTransform TeacherPage(UnityEngine.Events.UnityAction back = null)
        {
            RectTransform p = NewPage(L.T("Teacher"));
            BackButton(p, back ?? ShowMain);
            const float w = 218f;
            for (int i = 0; i < TeacherTabs.Length; i++)
            {
                int tab = i;
                Button b = UiKit.Button(p, L.T(TeacherTabs[i]), new Vector2(Width - Margin - (TeacherTabs.Length - i) * (w + 10f) + 10f, -35f), new Vector2(w, 70f), () =>
                {
                    _tTab = tab;
                    _tPage = 0;
                    _tArmed = 0;
                    _tMessage = "";
                    ShowTeacher();
                }, 26f);
                if (back == null && i == _tTab) UiKit.SetButtonColor(b, UiKit.SelectedColor);
            }
            return p;
        }

        /// <summary>The "Group" chooser; returns the chosen group's id, or null for everyone.</summary>
        private long? TeacherGroup(RectTransform p, EduStore store, float x, float width, Action redraw, out EduGroup chosen)
        {
            List<EduGroup> groups = store.Groups();
            if (_tGroup > groups.Count) _tGroup = 0;
            var names = new string[groups.Count + 1];
            names[0] = L.T("Everyone");
            for (int i = 0; i < groups.Count; i++) names[i + 1] = groups[i].Name;
            for (int i = 0; i < names.Length; i++) names[i] = L.T("Group") + ":  " + names[i];
            Stepper(p, x, width, names, _tGroup, i =>
            {
                _tGroup = i;
                _tPage = 0;
                redraw();
            });
            chosen = _tGroup > 0 ? groups[_tGroup - 1] : null;
            return chosen != null ? chosen.Id : (long?)null;
        }

        /// <summary>"&lt;  value  &gt;" in a row of any width, under the tabs.</summary>
        private static void Stepper(RectTransform p, float x, float width, string[] options, int index, Action<int> changed)
        {
            int current = Mathf.Clamp(index, 0, options.Length - 1);
            UiKit.Button(p, "<", new Vector2(x, -125f), new Vector2(70f, 60f), () => changed((current - 1 + options.Length) % options.Length));
            RectTransform box = UiKit.Rect(p, "Value", new Vector2(x + 76f, -125f), new Vector2(width - 152f, 60f));
            UiKit.Image(box, new Color(1f, 1f, 1f, 0.05f));
            UiKit.Fit(UiKit.Label(box, options[current], new Vector2(10f, 0f), new Vector2(width - 172f, 60f), 28f, TextAlignmentOptions.Midline));
            UiKit.Button(p, ">", new Vector2(x + width - 70f, -125f), new Vector2(70f, 60f), () => changed((current + 1) % options.Length));
        }

        private void TeacherNote(RectTransform p)
        {
            UiKit.Label(p, _tMessage, new Vector2(Margin + 270f, -(Height - 110f)), new Vector2(ContentWidth - 270f - 320f, 70f), 22f, TextAlignmentOptions.MidlineLeft,
                UiKit.AccentColor);
            _tMessage = "";
        }

        private void TeacherPager(RectTransform p, int pages, Action redraw)
        {
            if (pages <= 1) return;
            float y = -(Height - 110f);
            UiKit.Button(p, "<", new Vector2(Width - Margin - 300f, y), new Vector2(80f, 70f), () => { _tPage--; redraw(); });
            UiKit.Label(p, (_tPage + 1) + " / " + pages, new Vector2(Width - Margin - 220f, y), new Vector2(140f, 70f), 28f, TextAlignmentOptions.Midline, UiKit.DimTextColor);
            UiKit.Button(p, ">", new Vector2(Width - Margin - 80f, y), new Vector2(80f, 70f), () => { _tPage++; redraw(); });
        }

        private static string StudentName(EduUser user) => user.Role == "seat" ? L.F("Seat {0}", user.Name) : user.Name;

        private static string When(string started) =>
            DateTime.TryParse(started, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime at)
                ? at.ToLocalTime().ToString("dd.MM.yyyy   HH:mm", CultureInfo.InvariantCulture) : started;

        // ---------- the journal ----------

        private void ShowTeacherJournal()
        {
            RectTransform p = TeacherPage();
            EduStore store = Students(out string error);
            if (store == null) { _tMessage = L.F("The class data could not be opened: {0}", error); TeacherNote(p); return; }
            long? group = TeacherGroup(p, store, Margin, 760f, ShowTeacherJournal, out _);
            var blocks = new string[Course.Blocks.Length];
            for (int i = 0; i < blocks.Length; i++) blocks[i] = Course.Blocks[i] + ".  " + L.T(Course.BlockNames[i]);
            Stepper(p, Margin + 790f, ContentWidth - 790f, blocks, _tBlock, i => { _tBlock = i; ShowTeacherJournal(); });

            Journal j = EduTeacher.ReadJournal(group);
            var columns = new List<int>();
            for (int i = 0; i < j.Activities.Count; i++)
                if (j.Activities[i].Block == Course.Blocks[_tBlock] && columns.Count < 7) columns.Add(i);
            const float nameW = 400f, totalW = 150f, rowH = 60f;
            float cw = (ContentWidth - nameW - 2f * totalW) / 7f;
            float y = -205f;
            UiKit.Label(p, L.T("Student"), new Vector2(Margin + 16f, y), new Vector2(nameW, 44f), 24f, TextAlignmentOptions.MidlineLeft, UiKit.DimTextColor);
            for (int c = 0; c < columns.Count; c++)
                UiKit.Label(p, j.Activities[columns[c]].Number.ToString(CultureInfo.InvariantCulture), new Vector2(Margin + nameW + c * cw, y), new Vector2(cw - 8f, 44f), 26f,
                    TextAlignmentOptions.Midline, UiKit.DimTextColor);
            UiKit.Label(p, L.T("Passed"), new Vector2(Margin + nameW + 7f * cw, y), new Vector2(totalW, 44f), 22f, TextAlignmentOptions.Midline, UiKit.DimTextColor);
            UiKit.Label(p, L.T("Average score"), new Vector2(Margin + nameW + 7f * cw + totalW, y), new Vector2(totalW, 44f), 22f, TextAlignmentOptions.Midline, UiKit.DimTextColor);

            const int rows = 9;
            int pages = Mathf.Max(1, (j.Rows.Count + rows - 1) / rows);
            _tPage = Mathf.Clamp(_tPage, 0, pages - 1);
            y -= 52f;
            if (j.Rows.Count == 0)
                UiKit.Label(p, L.T("No students yet. Add them on the Students tab."), new Vector2(Margin, y - 10f), new Vector2(ContentWidth, 60f), 30f,
                    TextAlignmentOptions.MidlineLeft, UiKit.DimTextColor);
            for (int r = _tPage * rows; r < j.Rows.Count && r < (_tPage + 1) * rows; r++)
            {
                JournalRow row = j.Rows[r];
                UiKit.Button(p, StudentName(row.User), new Vector2(Margin, y), new Vector2(nameW - 10f, rowH - 6f), () => { _tArmed = 0; ShowTeacherStudent(row.User.Id); }, 26f);
                for (int c = 0; c < columns.Count; c++)
                {
                    JournalCell cell = row.Cells[columns[c]];
                    ActivityDefinition activity = j.Activities[columns[c]];
                    string text = cell.Best == null ? "·" : cell.Best.Value.ToString("0", CultureInfo.InvariantCulture);
                    Button b = UiKit.Button(p, text, new Vector2(Margin + nameW + c * cw, y), new Vector2(cw - 8f, rowH - 6f),
                        () => { _tPage = 0; ShowTeacherAttempts(row.User.Id, activity.Id); }, 28f);
                    UiKit.SetButtonColor(b, cell.Passed ? new Color(0.16f, 0.36f, 0.22f, 1f) : cell.Best != null ? new Color(0.42f, 0.2f, 0.16f, 1f) : new Color(0.12f, 0.13f, 0.16f, 1f));
                }
                UiKit.Label(p, row.Passed + " / " + j.Activities.Count, new Vector2(Margin + nameW + 7f * cw, y), new Vector2(totalW, rowH - 6f), 26f, TextAlignmentOptions.Midline);
                UiKit.Label(p, row.Flown > 0 ? row.Average.ToString("0", CultureInfo.InvariantCulture) : "—", new Vector2(Margin + nameW + 7f * cw + totalW, y),
                    new Vector2(totalW, rowH - 6f), 26f, TextAlignmentOptions.Midline);
                y -= rowH;
            }
            TeacherNote(p);
            TeacherPager(p, pages, ShowTeacherJournal);
        }

        /// <summary>Every attempt of one student on one exercise: watch it, or give it the teacher's own mark.</summary>
        private void ShowTeacherAttempts(long userId, string activityId)
        {
            RectTransform p = TeacherPage(ShowTeacherJournal);
            EduStore store = Students(out _);
            EduUser user = store != null ? store.User(userId) : null;
            if (user == null) { ShowTeacherJournal(); return; }
            EduSession.Activities.TryGetValue(activityId, out ActivityDefinition activity);
            UiKit.Label(p, StudentName(user) + "     " + (activity != null ? activity.Number + ". " + L.T(activity.Title) : activityId), new Vector2(Margin, -125f),
                new Vector2(ContentWidth, 60f), 34f, TextAlignmentOptions.MidlineLeft, UiKit.AccentColor);
            List<AttemptRecord> attempts = store.Attempts(userId, activityId);
            if (attempts.Count == 0)
                UiKit.Label(p, L.T("No flights recorded yet."), new Vector2(Margin, -210f), new Vector2(ContentWidth, 60f), 30f, TextAlignmentOptions.MidlineLeft, UiKit.DimTextColor);
            const int rows = 7;
            int pages = Mathf.Max(1, (attempts.Count + rows - 1) / rows);
            _tPage = Mathf.Clamp(_tPage, 0, pages - 1);
            CultureInfo inv = CultureInfo.InvariantCulture;
            float y = -205f;
            for (int i = _tPage * rows; i < attempts.Count && i < (_tPage + 1) * rows; i++)
            {
                AttemptRecord a = attempts[i];
                RectTransform row = UiKit.Rect(p, "Attempt", new Vector2(Margin, y), new Vector2(ContentWidth, 80f));
                UiKit.Image(row, new Color(1f, 1f, 1f, 0.05f));
                UiKit.Label(row, When(a.Started), new Vector2(20f, 0f), new Vector2(330f, 44f), 26f, TextAlignmentOptions.MidlineLeft);
                string mark = a.ManualScore != null ? L.F("Score {0}, teacher's mark {1}", a.Score.ToString("0", inv), a.ManualScore.Value.ToString("0", inv))
                    : L.F("Score {0}", a.Score.ToString("0", inv));
                UiKit.Label(row, mark + "     " + EduUi.Clock((float)a.Duration), new Vector2(360f, 0f), new Vector2(620f, 44f), 26f, TextAlignmentOptions.MidlineLeft,
                    a.Final >= (activity != null ? activity.PassMark : 60) ? new Color(0.4f, 0.95f, 0.5f) : UiKit.TextColor);
                if (!string.IsNullOrEmpty(a.Comment))
                    UiKit.Label(row, a.Comment, new Vector2(20f, -42f), new Vector2(960f, 36f), 22f, TextAlignmentOptions.MidlineLeft, UiKit.DimTextColor);
                bool there = activity != null && !string.IsNullOrEmpty(a.ReplayPath) && System.IO.File.Exists(store.ReplayFile(a.ReplayPath));
                Button watch = UiKit.Button(row, L.T("Replay"), new Vector2(ContentWidth - 470f, -8f), new Vector2(220f, 64f), () => WatchFlight(a, activity, there), 26f);
                if (!there) UiKit.SetButtonColor(watch, new Color(0.12f, 0.13f, 0.16f, 1f));
                UiKit.Button(row, L.T("Mark"), new Vector2(ContentWidth - 235f, -8f), new Vector2(220f, 64f), () =>
                {
                    _tText = a.ManualScore != null ? a.ManualScore.Value.ToString("0", inv) : "";
                    _tText2 = a.Comment ?? "";
                    ShowTeacherMark(a, userId, activityId);
                }, 26f);
                y -= 88f;
            }
            TeacherNote(p);
            TeacherPager(p, pages, () => ShowTeacherAttempts(userId, activityId));
        }

        private void ShowTeacherMark(AttemptRecord attempt, long userId, string activityId)
        {
            RectTransform p = TeacherPage(() => ShowTeacherAttempts(userId, activityId));
            UiKit.Label(p, L.F("Score {0}", attempt.Score.ToString("0", CultureInfo.InvariantCulture)) + "     " + When(attempt.Started), new Vector2(Margin, -125f),
                new Vector2(ContentWidth, 60f), 34f, TextAlignmentOptions.MidlineLeft, UiKit.AccentColor);
            UiKit.Label(p, L.T("The teacher's mark, 0 to 100 (leave empty to keep the computed score)"), new Vector2(Margin, -210f), new Vector2(ContentWidth, 50f), 28f);
            InputBox(p, new Vector2(Margin, -270f), new Vector2(260f, 80f), _tText, v => _tText = v, false, false, 5);
            UiKit.Label(p, L.T("Comment for the student"), new Vector2(Margin, -380f), new Vector2(ContentWidth, 50f), 28f);
            InputBox(p, new Vector2(Margin, -440f), new Vector2(ContentWidth, 80f), _tText2, v => _tText2 = v, false, false, 200);
            UiKit.Button(p, L.T("Save"), new Vector2(Width - Margin - 300f, -(Height - 110f)), new Vector2(300f, 70f), () =>
            {
                string typed = (_tText ?? "").Trim().Replace(',', '.');
                double? mark = null;
                if (typed.Length > 0)
                {
                    if (!double.TryParse(typed, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) || v < 0 || v > 100)
                    {
                        _tMessage = L.T("The mark is a number from 0 to 100");
                        ShowTeacherMark(attempt, userId, activityId);
                        return;
                    }
                    mark = v;
                }
                Students(out _).Grade(attempt.Id, mark, string.IsNullOrWhiteSpace(_tText2) ? null : _tText2.Trim());
                ShowTeacherAttempts(userId, activityId);
            }, 30f);
            TeacherNote(p);
        }

        // ---------- one student ----------

        private void ShowTeacherStudent(long userId)
        {
            RectTransform p = TeacherPage(ShowTeacher);
            EduStore store = Students(out _);
            EduUser user = store != null ? store.User(userId) : null;
            if (user == null) { ShowTeacher(); return; }
            List<EduGroup> groups = store.Groups();
            EduGroup group = _tGroup > 0 && _tGroup <= groups.Count ? groups[_tGroup - 1] : null;
            Journal j = EduTeacher.ReadJournal(null);
            JournalRow row = j.Rows.Find(r => r.User.Id == userId);
            UiKit.Label(p, StudentName(user), new Vector2(Margin, -125f), new Vector2(ContentWidth, 60f), 40f, TextAlignmentOptions.MidlineLeft, UiKit.AccentColor);
            if (row != null)
                UiKit.Label(p, L.T("Passed") + ": " + row.Passed + " / " + j.Activities.Count + "        " + L.T("Average score") + ": " +
                               (row.Flown > 0 ? row.Average.ToString("0", CultureInfo.InvariantCulture) : "—") + "        " + L.T("Attempts") + ": " + row.Attempts +
                               "        " + L.T("Time, min") + ": " + (row.Seconds / 60.0).ToString("0", CultureInfo.InvariantCulture),
                    new Vector2(Margin, -195f), new Vector2(ContentWidth, 50f), 28f);
            float y = -290f;
            void Row(string text, UnityEngine.Events.UnityAction act, bool danger = false)
            {
                Button b = UiKit.Button(p, text, new Vector2(Margin, y), new Vector2(900f, 72f), act, 28f);
                if (danger) UiKit.SetButtonColor(b, new Color(0.42f, 0.2f, 0.16f, 1f));
                y -= 86f;
            }
            if (row != null)
                Row(L.T("Progress report (PDF)"), () =>
                {
                    try
                    {
                        string file = EduTeacher.SaveProgress(j, row);
                        _tMessage = L.F("Saved: {0}", file);
                        EduTeacher.Open(file);
                    }
                    catch (Exception e) { _tMessage = L.F("Not saved: {0}", e.Message); }
                    ShowTeacherStudent(userId);
                });
            if (user.HasPin)
                Row(L.T("Remove the PIN"), () =>
                {
                    store.SetPin(userId, null);
                    _tMessage = L.T("The PIN is removed. The student can choose a new one.");
                    ShowTeacherStudent(userId);
                });
            if (group != null && user.Role == "student")
                Row(L.F("Take out of the group {0}", group.Name), () =>
                {
                    store.RemoveFromGroup(group.Id, userId);
                    ShowTeacher();
                });
            // Deleting is asked for twice: everything recorded about the student goes, and it cannot be undone.
            bool armed = _tArmed == userId;
            Row(armed ? L.T("Press again: the profile, the results and the recordings will be deleted for good") : L.T("Delete this student's data"), () =>
            {
                if (_tArmed != userId)
                {
                    _tArmed = userId;
                    ShowTeacherStudent(userId);
                    return;
                }
                _tArmed = 0;
                if (EduSession.CurrentUserId == userId) EduSession.CurrentUserId = 0;
                int files = store.DeleteUser(userId);
                _tMessage = L.F("Deleted, with {0} recordings.", files);
                ShowTeacher();
            }, true);
            TeacherNote(p);
        }

        // ---------- the class lists ----------

        private void ShowTeacherStudents()
        {
            RectTransform p = TeacherPage();
            EduStore store = Students(out string error);
            if (store == null) { _tMessage = L.F("The class data could not be opened: {0}", error); TeacherNote(p); return; }
            long? groupId = TeacherGroup(p, store, Margin, 760f, ShowTeacherStudents, out EduGroup group);
            float bw = (ContentWidth - 790f - 20f) / 3f;
            UiKit.Button(p, L.T("New group"), new Vector2(Margin + 790f, -125f), new Vector2(bw, 60f), () => { _tText = ""; ShowTeacherType(0); }, 24f);
            UiKit.Button(p, L.T("New student"), new Vector2(Margin + 790f + bw + 10f, -125f), new Vector2(bw, 60f), () => { _tText = ""; ShowTeacherType(1); }, 24f);
            UiKit.Button(p, L.T("Import a list"), new Vector2(Margin + 790f + 2f * (bw + 10f), -125f), new Vector2(bw, 60f), () => { _tText = ""; ShowTeacherType(2); }, 24f);

            List<EduUser> students = store.Users(groupId);
            students.RemoveAll(u => u.Role != "student");
            const int perPage = 14;
            int pages = Mathf.Max(1, (students.Count + perPage - 1) / perPage);
            _tPage = Mathf.Clamp(_tPage, 0, pages - 1);
            float half = (ContentWidth - 30f) * 0.5f;
            if (students.Count == 0)
                UiKit.Label(p, L.T("Nobody here yet. Add students one by one, or import the class list."), new Vector2(Margin, -230f), new Vector2(ContentWidth, 60f), 30f,
                    TextAlignmentOptions.MidlineLeft, UiKit.DimTextColor);
            for (int i = 0; i < perPage; i++)
            {
                int index = _tPage * perPage + i;
                if (index >= students.Count) break;
                EduUser user = students[index];
                UiKit.Button(p, user.Name + (user.HasPin ? "   · PIN" : ""), new Vector2(Margin + (i % 2) * (half + 30f), -215f - (i / 2) * 80f), new Vector2(half, 70f),
                    () => { _tArmed = 0; ShowTeacherStudent(user.Id); }, 28f);
            }
            TeacherNote(p);
            TeacherPager(p, pages, ShowTeacherStudents);
        }

        /// <summary>Typing a new group's name, a new student's name, or pasting a class list.</summary>
        private void ShowTeacherType(int what)
        {
            RectTransform p = TeacherPage(ShowTeacher);
            EduStore store = Students(out _);
            List<EduGroup> groups = store.Groups();
            EduGroup group = _tGroup > 0 && _tGroup <= groups.Count ? groups[_tGroup - 1] : null;
            bool list = what == 2;
            string ask = what == 0 ? L.T("Name of the new group")
                : what == 1 ? (group != null ? L.F("Name of the new student of the group {0}", group.Name) : L.T("Name of the new student"))
                : group != null ? L.F("Paste the list of the group {0}: one name on a line", group.Name) : L.T("Choose a group on the Students tab first");
            UiKit.Label(p, ask, new Vector2(Margin, -130f), new Vector2(ContentWidth, 60f), 32f);
            if (list && group == null) return;
            TMP_InputField field = InputBox(p, new Vector2(Margin, -200f), new Vector2(ContentWidth, list ? 560f : 84f), _tText, v => _tText = v, false, list, list ? 20000 : 80);
            field.ActivateInputField();
            if (list)
                UiKit.Label(p, L.T("Copy the names from a spreadsheet or a document and press Ctrl+V. Names already in the group are skipped."), new Vector2(Margin + 270f, -770f),
                    new Vector2(ContentWidth - 270f, 50f), 22f, TextAlignmentOptions.MidlineLeft, UiKit.DimTextColor);
            UnityEngine.Events.UnityAction add = () =>
            {
                try
                {
                    if (what == 0)
                    {
                        long id = store.AddGroup(_tText);
                        _tGroup = store.Groups().FindIndex(g => g.Id == id) + 1;
                        _tMessage = L.F("Group {0} is added.", _tText.Trim());
                    }
                    else if (what == 1)
                    {
                        long id = store.AddUser(_tText);
                        if (group != null) store.AddToGroup(group.Id, id);
                        _tMessage = L.F("{0} is added.", _tText.Trim());
                    }
                    else _tMessage = L.F("Students added: {0}", store.ImportList(group.Id, _tText));
                    _tText = "";
                    _tTab = 1;
                    ShowTeacher();
                }
                catch (ArgumentException e)
                {
                    _tMessage = e.Message.Contains("already exists") ? L.T("A group with this name already exists") : L.T("A name is needed");
                    ShowTeacherType(what);
                }
            };
            if (!list) field.onSubmit.AddListener(_ => add());
            UiKit.Button(p, L.T("Add"), new Vector2(Width - Margin - 300f, -(Height - 110f)), new Vector2(300f, 70f), add, 30f);
            TeacherNote(p);
        }

        // ---------- what the class has been given ----------

        private static readonly int[] AttemptChoices = { 0, 1, 2, 3, 5, 10 };
        private int _tLimit;
        private readonly List<string> _tPlan = new List<string>();

        private string[] AttemptNames()
        {
            var names = new string[AttemptChoices.Length];
            for (int i = 0; i < names.Length; i++)
                names[i] = AttemptChoices[i] == 0 ? L.T("Attempts: no limit") : L.F("Attempts: {0}", AttemptChoices[i]);
            return names;
        }

        private static string ExerciseName(string activityId)
        {
            return EduSession.Activities.TryGetValue(activityId, out ActivityDefinition a) ? a.Number + ". " + L.T(a.Title) : activityId;
        }

        private void ShowTeacherTasks()
        {
            RectTransform p = TeacherPage();
            EduStore store = Students(out string error);
            if (store == null) { _tMessage = L.F("The class data could not be opened: {0}", error); TeacherNote(p); return; }
            long? groupId = TeacherGroup(p, store, Margin, 760f, ShowTeacherTasks, out _);
            float bw = (ContentWidth - 790f - 10f) / 2f;
            UiKit.Button(p, L.T("Give an exercise"), new Vector2(Margin + 790f, -125f), new Vector2(bw, 60f), () => { _tPage = 0; ShowTeacherGive(); }, 24f);
            UiKit.Button(p, L.T("Lesson plans"), new Vector2(Margin + 790f + bw + 10f, -125f), new Vector2(bw, 60f), () => { _tPage = 0; _tArmed = 0; ShowTeacherLessons(); }, 24f);

            List<Assignment> given = store.AssignmentsOf(groupId);
            Journal j = EduTeacher.ReadJournal(groupId);
            j.Rows.RemoveAll(r => r.User.Role != "student");
            if (given.Count == 0)
                UiKit.Label(p, groupId != null ? L.T("Nothing has been given to this group yet.") : L.T("Nothing has been given to everyone yet. Choose a group, or give an exercise to all."),
                    new Vector2(Margin, -215f), new Vector2(ContentWidth, 60f), 28f, TextAlignmentOptions.MidlineLeft, UiKit.DimTextColor);
            const int rows = 8;
            int pages = Mathf.Max(1, (given.Count + rows - 1) / rows);
            _tPage = Mathf.Clamp(_tPage, 0, pages - 1);
            float y = -205f;
            for (int i = _tPage * rows; i < given.Count && i < (_tPage + 1) * rows; i++)
            {
                Assignment a = given[i];
                int column = j.Activities.FindIndex(x => x.Id == a.ActivityId), done = 0;
                if (column >= 0)
                    foreach (JournalRow r in j.Rows)
                        if (r.Cells[column].Passed) done++;
                RectTransform row = UiKit.Rect(p, "Task", new Vector2(Margin, y), new Vector2(ContentWidth, 70f));
                UiKit.Image(row, new Color(1f, 1f, 1f, 0.05f));
                UiKit.Label(row, (a.Lesson.Length > 0 ? a.Lesson + "  ·  " : "") + ExerciseName(a.ActivityId), new Vector2(20f, 0f), new Vector2(760f, 70f), 26f, TextAlignmentOptions.MidlineLeft);
                UiKit.Label(row, a.AttemptLimit == 0 ? L.T("Attempts: no limit") : L.F("Attempts: {0}", a.AttemptLimit), new Vector2(790f, 0f), new Vector2(250f, 70f), 22f,
                    TextAlignmentOptions.MidlineLeft, UiKit.DimTextColor);
                UiKit.Label(row, L.F("Passed by {0} of {1}", done, j.Rows.Count), new Vector2(1040f, 0f), new Vector2(230f, 70f), 22f, TextAlignmentOptions.MidlineLeft,
                    j.Rows.Count > 0 && done == j.Rows.Count ? new Color(0.4f, 0.95f, 0.5f) : UiKit.DimTextColor);
                UiKit.Button(row, L.T("Take back"), new Vector2(ContentWidth - 200f, -6f), new Vector2(190f, 58f), () =>
                {
                    store.Unassign(a.Id);
                    ShowTeacherTasks();
                }, 22f);
                y -= 78f;
            }
            TeacherNote(p);
            TeacherPager(p, pages, ShowTeacherTasks);
        }

        /// <summary>Choosing an exercise to give to the chosen group (or to everyone), with how many attempts it gets.</summary>
        private void ShowTeacherGive()
        {
            RectTransform p = TeacherPage(ShowTeacherTasks);
            EduStore store = Students(out _);
            List<EduGroup> groups = store.Groups();
            EduGroup group = _tGroup > 0 && _tGroup <= groups.Count ? groups[_tGroup - 1] : null;
            var blocks = new string[Course.Blocks.Length];
            for (int i = 0; i < blocks.Length; i++) blocks[i] = Course.Blocks[i] + ".  " + L.T(Course.BlockNames[i]);
            Stepper(p, Margin, 760f, blocks, _tBlock, i => { _tBlock = i; ShowTeacherGive(); });
            Stepper(p, Margin + 790f, ContentWidth - 790f, AttemptNames(), _tLimit, i => { _tLimit = i; ShowTeacherGive(); });
            UiKit.Label(p, group != null ? L.F("Press an exercise to give it to the group {0}", group.Name) : L.T("Press an exercise to give it to everyone"), new Vector2(Margin, -200f),
                new Vector2(ContentWidth, 50f), 26f, TextAlignmentOptions.MidlineLeft, UiKit.DimTextColor);
            var course = new Course(EduSession.Activities.Values);
            float y = -260f;
            foreach (CourseEntry e in course.In(Course.Blocks[_tBlock]))
            {
                ActivityDefinition activity = e.Activity;
                UiKit.Button(p, activity.Number + ".  " + L.T(activity.Title), new Vector2(Margin, y), new Vector2(ContentWidth, 68f), () =>
                {
                    store.Assign(activity, group != null ? group.Id : (long?)null, null, AttemptChoices[_tLimit]);
                    _tMessage = L.F("Given: {0}", activity.Number + ". " + L.T(activity.Title));
                    ShowTeacherTasks();
                }, 28f);
                y -= 76f;
                if (y < -(Height - 190f)) break;
            }
            TeacherNote(p);
        }

        private void ShowTeacherLessons()
        {
            RectTransform p = TeacherPage(ShowTeacherTasks);
            EduStore store = Students(out _);
            List<EduGroup> groups = store.Groups();
            EduGroup group = _tGroup > 0 && _tGroup <= groups.Count ? groups[_tGroup - 1] : null;
            UiKit.Label(p, L.T("Lesson plans"), new Vector2(Margin, -125f), new Vector2(500f, 60f), 34f, TextAlignmentOptions.MidlineLeft, UiKit.AccentColor);
            Stepper(p, Margin + 520f, 520f, AttemptNames(), _tLimit, i => { _tLimit = i; ShowTeacherLessons(); });
            UiKit.Button(p, L.T("New lesson plan"), new Vector2(Width - Margin - 400f, -125f), new Vector2(400f, 60f), () =>
            {
                _tText = "";
                _tPlan.Clear();
                ShowTeacherLessonEdit();
            }, 24f);
            List<LessonPlan> plans = store.Lessons();
            if (plans.Count == 0)
                UiKit.Label(p, L.T("No lesson plans yet. A plan is a list of exercises in the order they are to be done."), new Vector2(Margin, -215f), new Vector2(ContentWidth, 60f), 28f,
                    TextAlignmentOptions.MidlineLeft, UiKit.DimTextColor);
            const int rows = 7;
            int pages = Mathf.Max(1, (plans.Count + rows - 1) / rows);
            _tPage = Mathf.Clamp(_tPage, 0, pages - 1);
            float y = -205f;
            for (int i = _tPage * rows; i < plans.Count && i < (_tPage + 1) * rows; i++)
            {
                LessonPlan plan = plans[i];
                var numbers = new List<string>();
                foreach (string id in plan.Activities)
                    numbers.Add(EduSession.Activities.TryGetValue(id, out ActivityDefinition a) ? a.Number.ToString(CultureInfo.InvariantCulture) : "?");
                RectTransform row = UiKit.Rect(p, "Plan", new Vector2(Margin, y), new Vector2(ContentWidth, 76f));
                UiKit.Image(row, new Color(1f, 1f, 1f, 0.05f));
                UiKit.Label(row, plan.Name, new Vector2(20f, 0f), new Vector2(520f, 76f), 28f, TextAlignmentOptions.MidlineLeft);
                UiKit.Label(row, string.Join(", ", numbers), new Vector2(550f, 0f), new Vector2(330f, 76f), 22f, TextAlignmentOptions.MidlineLeft, UiKit.DimTextColor);
                UiKit.Button(row, group != null ? L.F("Give to {0}", group.Name) : L.T("Give to everyone"), new Vector2(ContentWidth - 590f, -8f), new Vector2(330f, 60f), () =>
                {
                    int n = store.AssignLesson(plan, EduSession.Activities, group != null ? group.Id : (long?)null, null, AttemptChoices[_tLimit]);
                    _tMessage = L.F("Exercises given: {0}", n);
                    ShowTeacherTasks();
                }, 22f);
                bool armed = _tArmed == plan.Id;
                Button del = UiKit.Button(row, armed ? L.T("Press again") : L.T("Delete"), new Vector2(ContentWidth - 250f, -8f), new Vector2(240f, 60f), () =>
                {
                    if (_tArmed != plan.Id) _tArmed = plan.Id;
                    else
                    {
                        _tArmed = 0;
                        store.DeleteLesson(plan.Id);
                    }
                    ShowTeacherLessons();
                }, 22f);
                if (armed) UiKit.SetButtonColor(del, new Color(0.42f, 0.2f, 0.16f, 1f));
                y -= 86f;
            }
            TeacherNote(p);
            TeacherPager(p, pages, ShowTeacherLessons);
        }

        /// <summary>A new lesson plan: its name, and the exercises pressed in the order they are to be done.</summary>
        private void ShowTeacherLessonEdit()
        {
            RectTransform p = TeacherPage(ShowTeacherLessons);
            InputBox(p, new Vector2(Margin, -125f), new Vector2(760f, 60f), _tText, v => _tText = v, false, false, 60);
            var blocks = new string[Course.Blocks.Length];
            for (int i = 0; i < blocks.Length; i++) blocks[i] = Course.Blocks[i] + ".  " + L.T(Course.BlockNames[i]);
            Stepper(p, Margin + 790f, ContentWidth - 790f, blocks, _tBlock, i => { _tBlock = i; ShowTeacherLessonEdit(); });
            UiKit.Label(p, L.T("Type the plan's name above, then press the exercises in the order of the lesson. Press one again to take it out."), new Vector2(Margin, -195f),
                new Vector2(ContentWidth, 50f), 22f, TextAlignmentOptions.MidlineLeft, UiKit.DimTextColor);
            var course = new Course(EduSession.Activities.Values);
            float y = -250f;
            foreach (CourseEntry e in course.In(Course.Blocks[_tBlock]))
            {
                string id = e.Activity.Id;
                int place = _tPlan.IndexOf(id);
                Button b = UiKit.Button(p, (place >= 0 ? "[" + (place + 1) + "]   " : "") + e.Activity.Number + ".  " + L.T(e.Activity.Title), new Vector2(Margin, y),
                    new Vector2(ContentWidth, 64f), () =>
                    {
                        if (!_tPlan.Remove(id)) _tPlan.Add(id);
                        ShowTeacherLessonEdit();
                    }, 26f);
                if (place >= 0) UiKit.SetButtonColor(b, UiKit.SelectedColor);
                y -= 72f;
                if (y < -(Height - 190f)) break;
            }
            UiKit.Button(p, L.F("Save the plan ({0})", _tPlan.Count), new Vector2(Width - Margin - 400f, -(Height - 110f)), new Vector2(400f, 70f), () =>
            {
                try
                {
                    Students(out _).SaveLesson(_tText, _tPlan);
                    _tText = "";
                    _tPlan.Clear();
                    ShowTeacherLessons();
                }
                catch (ArgumentException)
                {
                    _tMessage = L.T("A plan needs a name and at least one exercise");
                    ShowTeacherLessonEdit();
                }
            }, 26f);
            TeacherNote(p);
        }

        // ---------- the reports ----------

        private void ShowTeacherReports()
        {
            RectTransform p = TeacherPage();
            EduStore store = Students(out string error);
            if (store == null) { _tMessage = L.F("The class data could not be opened: {0}", error); TeacherNote(p); return; }
            long? groupId = TeacherGroup(p, store, Margin, 760f, ShowTeacherReports, out _);
            UiKit.Label(p, L.T("School (printed at the top of the reports)"), new Vector2(Margin, -215f), new Vector2(ContentWidth, 44f), 26f, TextAlignmentOptions.MidlineLeft,
                UiKit.DimTextColor);
            InputBox(p, new Vector2(Margin, -265f), new Vector2(ContentWidth, 76f), store.SchoolName, v => store.SchoolName = v, false, false, 120);
            float y = -380f;
            void Row(string text, Func<string> make, bool open)
            {
                UiKit.Button(p, text, new Vector2(Margin, y), new Vector2(900f, 72f), () =>
                {
                    try
                    {
                        string made = make();
                        _tMessage = L.F("Saved: {0}", made);
                        if (open) EduTeacher.Open(made);
                    }
                    catch (Exception e) { _tMessage = L.F("Not saved: {0}", e.Message); }
                    ShowTeacherReports();
                }, 28f);
                y -= 86f;
            }
            Row(L.T("Gradebook of the group (XLSX)"), () => EduTeacher.SaveGradebook(EduTeacher.ReadJournal(groupId), false), true);
            Row(L.T("Gradebook of the group (CSV)"), () => EduTeacher.SaveGradebook(EduTeacher.ReadJournal(groupId), true), false);
            Row(L.T("Progress report for every student (PDF)"), () =>
            {
                EduTeacher.SaveAllProgress(EduTeacher.ReadJournal(groupId), out string folder);
                return folder;
            }, true);
            UiKit.Button(p, L.T("Open the reports folder"), new Vector2(Margin, y), new Vector2(900f, 72f), () =>
            {
                System.IO.Directory.CreateDirectory(EduTeacher.ReportsFolder);
                EduTeacher.Open(EduTeacher.ReportsFolder);
            }, 28f);
            TeacherNote(p);
        }

        // ---------- settings, backup and restore ----------

        private void ShowTeacherSettings()
        {
            RectTransform p = TeacherPage();
            EduStore store = Students(out string error);
            if (store == null) { _tMessage = L.F("The class data could not be opened: {0}", error); TeacherNote(p); return; }
            float y = -140f;
            void Row(string text, UnityEngine.Events.UnityAction act, string note = null)
            {
                UiKit.Button(p, text, new Vector2(Margin, y), new Vector2(760f, 72f), act, 28f);
                if (note != null)
                    UiKit.Label(p, note, new Vector2(Margin + 790f, y), new Vector2(ContentWidth - 790f, 72f), 22f, TextAlignmentOptions.MidlineLeft, UiKit.DimTextColor);
                y -= 86f;
            }
            Row(EduSession.EverythingUnlocked ? L.T("All exercises open: yes") : L.T("All exercises open: no"), () =>
            {
                EduSession.EverythingUnlocked = !EduSession.EverythingUnlocked;
                ShowTeacherSettings();
            }, L.T("Otherwise a block opens when the block before it is passed."));
            Row(L.T("Back up the class data"), () =>
            {
                try { _tMessage = L.F("Saved: {0}", EduTeacher.Backup()); }
                catch (Exception e) { _tMessage = L.F("Not saved: {0}", e.Message); }
                ShowTeacherSettings();
            }, L.T("A copy of the students, groups and results, kept on this PC."));
            Row(L.T("Restore from a backup"), () => { _tArmed = 0; _tPage = 0; ShowTeacherRestore(); }, L.T("The class data as it is now is saved first."));
            Row(L.T("Change the teacher's password"), () => { _tText = ""; ShowTeacherNewPassword(); });
            Row(L.T("Leave the teacher's console"), () =>
            {
                _teacherIn = false;
                ShowMain();
            }, L.T("The password is asked for again next time."));
            UiKit.Label(p, EduSession.Folder, new Vector2(Margin, y - 10f), new Vector2(ContentWidth, 44f), 20f, TextAlignmentOptions.MidlineLeft, UiKit.DimTextColor);
            TeacherNote(p);
        }

        private void ShowTeacherNewPassword()
        {
            RectTransform p = TeacherPage(ShowTeacherSettings);
            UiKit.Label(p, L.T("Choose the teacher's password: 4 characters or more"), new Vector2(Margin, -170f), new Vector2(ContentWidth, 60f), 34f);
            TMP_InputField field = InputBox(p, new Vector2(Margin, -250f), new Vector2(700f, 84f), _tText, v => _tText = v, true);
            field.ActivateInputField();
            UiKit.Button(p, L.T("Save"), new Vector2(Margin + 730f, -250f), new Vector2(240f, 84f), () =>
            {
                if (_tText.Length < 4 || _tText.Length > 64)
                {
                    _tMessage = L.T("Choose the teacher's password: 4 characters or more");
                    ShowTeacherNewPassword();
                    return;
                }
                Students(out _).SetTeacherPassword(_tText);
                _tText = "";
                _tMessage = L.T("The password is changed.");
                ShowTeacherSettings();
            }, 30f);
            TeacherNote(p);
        }

        private void ShowTeacherRestore()
        {
            RectTransform p = TeacherPage(ShowTeacherSettings);
            List<string> files = EduTeacher.Backups();
            UiKit.Label(p, files.Count == 0 ? L.T("There are no backups on this PC yet.") : L.T("Choose the backup to put back. Press it twice."), new Vector2(Margin, -125f),
                new Vector2(ContentWidth, 60f), 30f, TextAlignmentOptions.MidlineLeft, UiKit.DimTextColor);
            const int rows = 7;
            int pages = Mathf.Max(1, (files.Count + rows - 1) / rows);
            _tPage = Mathf.Clamp(_tPage, 0, pages - 1);
            float y = -205f;
            for (int i = _tPage * rows; i < files.Count && i < (_tPage + 1) * rows; i++)
            {
                string file = files[i];
                long mark = i + 1;
                string label = System.IO.File.GetLastWriteTime(file).ToString("dd.MM.yyyy   HH:mm", CultureInfo.InvariantCulture) + "      " + System.IO.Path.GetFileName(file);
                Button b = UiKit.Button(p, _tArmed == mark ? L.T("Press again: today's class data will be replaced by this backup") : label, new Vector2(Margin, y),
                    new Vector2(ContentWidth, 72f), () =>
                    {
                        if (_tArmed != mark)
                        {
                            _tArmed = mark;
                            ShowTeacherRestore();
                            return;
                        }
                        _tArmed = 0;
                        try
                        {
                            string kept = EduTeacher.Restore(file);
                            _tMessage = L.F("Restored. The data it replaced is kept as {0}", System.IO.Path.GetFileName(kept));
                        }
                        catch (Exception e) { _tMessage = L.F("Not restored: {0}", e.Message); }
                        ShowTeacherSettings();
                    }, 26f);
                if (_tArmed == mark) UiKit.SetButtonColor(b, new Color(0.42f, 0.2f, 0.16f, 1f));
                y -= 86f;
            }
            TeacherNote(p);
            TeacherPager(p, pages, ShowTeacherRestore);
        }
    }
}
