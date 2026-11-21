using System;
using System.Collections.Generic;
using Ushqyr.Core;
using Ushqyr.Edu;
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
