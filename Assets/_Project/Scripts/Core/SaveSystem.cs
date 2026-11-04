using System;
using System.IO;
using UnityEngine;

namespace Ushqyr.Core
{
    /// <summary>
    /// Owns the current <see cref="GameSettings"/>: loads them from persistentDataPath/settings.json on first use,
    /// saves atomically (write temp, then replace), and tells listeners when they change.
    /// </summary>
    public static class SaveSystem
    {
        public const string SettingsFileName = "settings.json";

        private static GameSettings _settings;

        public static event Action<GameSettings> SettingsChanged;

        public static string SettingsPath => Path.Combine(Application.persistentDataPath, SettingsFileName);

        public static GameSettings Settings => _settings ??= Load(SettingsPath);

        /// <summary>Company / product folder names the game used before it was renamed to Ushqyr.</summary>
        public const string LegacyCompany = "Gate" + "Rush", LegacyProduct = "Gate" + "Rush VR";

        /// <summary>
        /// One-time move to the Ushqyr name: the user folder follows the company / product name, so settings, records,
        /// ghosts, builds and own tracks would seem lost. When the new folder has no settings yet and the old one
        /// exists next to it, everything is copied over (the old folder is left as it was).
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void MigrateLegacyData()
        {
            try
            {
                string now = Application.persistentDataPath;
                if (File.Exists(Path.Combine(now, SettingsFileName))) return;
                var company = Directory.GetParent(now)?.Parent;
                if (company == null) return;
                string old = Path.Combine(company.FullName, LegacyCompany, LegacyProduct);
                if (!Directory.Exists(old) || !File.Exists(Path.Combine(old, SettingsFileName))) return;
                Copy(old, now);
                Debug.Log($"[Ushqyr] Copied your settings and records from {old}");
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException)
            {
                Debug.LogWarning($"[Ushqyr] Could not copy the old user folder: {e.Message}");
            }
        }

        private static void Copy(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (var f in Directory.GetFiles(from))
            {
                string dst = Path.Combine(to, Path.GetFileName(f));
                if (!File.Exists(dst)) File.Copy(f, dst);
            }
            foreach (var d in Directory.GetDirectories(from))
            {
                string name = Path.GetFileName(d);
                if (name == "Unity" || name.StartsWith(".")) continue; // engine caches
                Copy(d, Path.Combine(to, name));
            }
        }

        public static GameSettings Load(string path)
        {
            try
            {
                return GameSettings.FromJson(File.Exists(path) ? File.ReadAllText(path) : null);
            }
            catch (Exception e) when (e is IOException || e is ArgumentException)
            {
                Debug.LogWarning($"[Ushqyr] Settings unreadable ({e.Message}); using defaults");
                return new GameSettings();
            }
        }

        public static void Save(GameSettings settings, string path)
        {
            settings.Clamp();
            try
            {
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, settings.ToJson());
                if (File.Exists(path)) File.Replace(tmp, path, null);
                else File.Move(tmp, path);
            }
            catch (IOException e)
            {
                Debug.LogWarning($"[Ushqyr] Could not save settings: {e.Message}");
            }
        }

        /// <summary>Clamps, saves and broadcasts the current settings after they were edited.</summary>
        public static void Commit()
        {
            var s = Settings;
            Save(s, SettingsPath);
            SettingsChanged?.Invoke(s);
        }

        /// <summary>Replaces the settings (e.g. "reset to defaults"), then commits.</summary>
        public static void Replace(GameSettings settings)
        {
            _settings = settings;
            Commit();
        }
    }
}
