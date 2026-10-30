using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace GateRush.Track
{
    /// <summary>
    /// User tracks on disk: one JSON file per track in persistentDataPath/tracks. Sharing = copying files:
    /// any valid track JSON dropped into that folder shows up in the game (ids are made unique on load).
    /// On Quest: /sdcard/Android/data/com.gaterush.vr/files/tracks (adb push/pull or SideQuest).
    /// </summary>
    public sealed class TrackLibrary
    {
        public const string UserPrefix = "user-";

        public TrackLibrary(string folder) => Folder = folder;

        public static TrackLibrary Default => new TrackLibrary(Path.Combine(Application.persistentDataPath, "tracks"));

        public string Folder { get; }

        public static bool IsUserId(string id) => !string.IsNullOrEmpty(id) && id.StartsWith(UserPrefix, StringComparison.Ordinal);

        public string PathFor(string id) => Path.Combine(Folder, Sanitize(id) + ".json");

        /// <summary>All readable user tracks, newest file first. Unreadable files are skipped with a warning.</summary>
        public List<TrackData> LoadAll()
        {
            var result = new List<TrackData>();
            if (!Directory.Exists(Folder)) return result;
            var files = new List<FileInfo>(new DirectoryInfo(Folder).GetFiles("*.json"));
            files.Sort((a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
            var seen = new HashSet<string>();
            foreach (var f in files)
            {
                try
                {
                    var t = TrackData.FromJson(File.ReadAllText(f.FullName));
                    // Shared files may carry a built-in or duplicate id: key user tracks by their file name.
                    t.Id = UserPrefix + Sanitize(Path.GetFileNameWithoutExtension(f.Name).Replace(UserPrefix, ""));
                    if (!seen.Add(t.Id)) continue;
                    if (string.IsNullOrWhiteSpace(t.Name)) t.Name = Path.GetFileNameWithoutExtension(f.Name);
                    result.Add(t);
                }
                catch (Exception e) when (e is IOException || e is ArgumentException || e is UnauthorizedAccessException)
                {
                    Debug.LogWarning($"[GateRush] Skipping track file {f.Name}: {e.Message}");
                }
            }
            return result;
        }

        public TrackData Load(string id)
        {
            string path = PathFor(id);
            if (!File.Exists(path)) return null;
            var t = TrackData.FromJson(File.ReadAllText(path));
            t.Id = id;
            return t;
        }

        /// <summary>Saves a user track (assigning a fresh user id if it has none, or a built-in id). Returns the file path.</summary>
        public string Save(TrackData track)
        {
            if (!IsUserId(track.Id)) track.Id = NewId(track.Name);
            Directory.CreateDirectory(Folder);
            string path = PathFor(track.Id);
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, track.ToJson());
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
            return path;
        }

        public bool Delete(string id)
        {
            string path = PathFor(id);
            if (!File.Exists(path)) return false;
            File.Delete(path);
            return true;
        }

        /// <summary>A copy of any track (e.g. a built-in) as a new user track, not yet saved.</summary>
        public TrackData Duplicate(TrackData source, string newName)
        {
            var copy = TrackData.FromJson(source.ToJson(false));
            copy.Name = newName;
            copy.Author = "Pilot";
            copy.Id = NewId(newName);
            return copy;
        }

        /// <summary>Unique user id derived from a name.</summary>
        public string NewId(string name)
        {
            string stem = UserPrefix + Sanitize(string.IsNullOrWhiteSpace(name) ? "track" : name).ToLowerInvariant();
            string id = stem;
            for (int i = 2; File.Exists(PathFor(id)); i++) id = stem + "-" + i;
            return id;
        }

        /// <summary>Letters, digits, '-' and '_' only (file-name and id safe), at most 40 characters.</summary>
        public static string Sanitize(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s ?? "")
            {
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_') sb.Append(c);
                else if (c == ' ' && sb.Length > 0 && sb[sb.Length - 1] != '-') sb.Append('-');
                if (sb.Length >= 40) break;
            }
            return sb.Length == 0 ? "track" : sb.ToString().Trim('-');
        }
    }
}
