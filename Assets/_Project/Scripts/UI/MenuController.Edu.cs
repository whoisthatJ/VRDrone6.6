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
