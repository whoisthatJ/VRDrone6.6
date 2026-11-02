using GateRush.Game;
using GateRush.Track;
using TMPro;
using UnityEngine;

namespace GateRush.UI
{
    /// <summary>Track editor home: new track per environment, copy a built-in, and your saved tracks.</summary>
    public sealed partial class MenuController
    {
        private const int UserTracksPerPage = 4;
        private int _userPage;
        private string _confirmDelete;

        private static string EnvironmentName(string env) =>
            env == "hangar" ? "Hangar" : env == "industrial" ? "Dockyard" : "Open Field";

        private void ShowEditorHome()
        {
            var p = NewPage("Track editor");
            var library = TrackLibrary.Default;

            UiKit.Label(p, "New track in:", new Vector2(Margin, -115f), new Vector2(400f, 60f), 30f);
            float w = (ContentWidth - 400f - 2f * 20f) / 3f;
            string[] envs = { "field", "hangar", "industrial" };
            for (int i = 0; i < envs.Length; i++)
            {
                string env = envs[i];
                UiKit.Button(p, EnvironmentName(env), new Vector2(Margin + 400f + i * (w + 20f), -110f), new Vector2(w, 70f), () =>
                {
                    CommitIfDirty();
                    var track = TrackEditModel.NewTrack(env, NewTrackName(library, EnvironmentName(env)));
                    GameFlow.Edit(tracks, track);
                }, 28f);
            }

            UiKit.Label(p, "Copy and edit:", new Vector2(Margin, -200f), new Vector2(400f, 60f), 30f);
            for (int i = 0; i < tracks.Tracks.Count && i < 3; i++)
            {
                var entry = tracks.Tracks[i];
                UiKit.Button(p, entry.DisplayName, new Vector2(Margin + 400f + i * (w + 20f), -195f), new Vector2(w, 70f), () =>
                {
                    CommitIfDirty();
                    var source = tracks.Load(entry);
                    if (source != null) GameFlow.Edit(tracks, library.Duplicate(source, entry.DisplayName + " (copy)"));
                }, 26f);
            }

            UiKit.Label(p, "Your tracks:", new Vector2(Margin, -290f), new Vector2(400f, 50f), 30f, TextAlignmentOptions.MidlineLeft, UiKit.AccentColor);
            var mine = library.LoadAll();
            int pages = Mathf.Max(1, (mine.Count + UserTracksPerPage - 1) / UserTracksPerPage);
            _userPage = Mathf.Clamp(_userPage, 0, pages - 1);
            float y = -350f;
            if (mine.Count == 0)
                UiKit.Label(p, "None yet. Start a new one above.", new Vector2(Margin, y), new Vector2(ContentWidth, 60f), 28f, TextAlignmentOptions.MidlineLeft, UiKit.DimTextColor);
            for (int i = _userPage * UserTracksPerPage; i < mine.Count && i < (_userPage + 1) * UserTracksPerPage; i++)
            {
                var t = mine[i];
                UiKit.Label(p, $"<b>{t.Name}</b>   <color=#bbb>{EnvironmentName(t.Environment)}, {t.Gates.Count} gates, {t.Props.Count} props</color>",
                    new Vector2(Margin, y), new Vector2(ContentWidth - 640f, 80f), 28f);
                UiKit.Button(p, "Edit", new Vector2(Width - Margin - 620f, y), new Vector2(190f, 75f), () => { CommitIfDirty(); GameFlow.Edit(tracks, t); }, 28f);
                UiKit.Button(p, "Fly", new Vector2(Width - Margin - 410f, y), new Vector2(190f, 75f), () => { CommitIfDirty(); GameFlow.Race(tracks, t.Id); }, 28f);
                bool confirming = _confirmDelete == t.Id;
                var del = UiKit.Button(p, confirming ? "Sure?" : "Delete", new Vector2(Width - Margin - 200f, y), new Vector2(200f, 75f), () =>
                {
                    if (_confirmDelete == t.Id)
                    {
                        library.Delete(t.Id);
                        _confirmDelete = null;
                    }
                    else _confirmDelete = t.Id;
                    ShowEditorHome();
                }, 28f);
                if (confirming) UiKit.SetButtonColor(del, new Color(0.6f, 0.15f, 0.12f, 1f));
                y -= 90f;
            }
            if (pages > 1)
            {
                float py = -(Height - 110f);
                UiKit.Button(p, "<", new Vector2(Width - Margin - 420f, py), new Vector2(90f, 70f), () => { _userPage = (_userPage - 1 + pages) % pages; ShowEditorHome(); });
                UiKit.Label(p, $"{_userPage + 1}/{pages}", new Vector2(Width - Margin - 320f, py), new Vector2(120f, 70f), 30f, TextAlignmentOptions.Center);
                UiKit.Button(p, ">", new Vector2(Width - Margin - 190f, py), new Vector2(90f, 70f), () => { _userPage = (_userPage + 1) % pages; ShowEditorHome(); });
            }

            UiKit.Label(p,
                // Forward slashes: TMP would turn "\t" in a Windows path into a tab.
                $"<size=80%>Share tracks by copying the .json files in <b>{library.Folder.Replace('\\', '/')}</b>. " +
                "Files you copy in show up here.</size>",
                new Vector2(Margin, -(Height - 210f)), new Vector2(ContentWidth, 90f), 26f, TextAlignmentOptions.TopLeft, UiKit.DimTextColor);
            BackButton(p);
        }

        private static string NewTrackName(TrackLibrary library, string env)
        {
            int n = library.LoadAll().Count + 1;
            return $"My {env} track {n}";
        }
    }
}
