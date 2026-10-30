using System;
using System.IO;
using UnityEngine;

namespace GateRush.Core
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

        public static GameSettings Load(string path)
        {
            try
            {
                return GameSettings.FromJson(File.Exists(path) ? File.ReadAllText(path) : null);
            }
            catch (Exception e) when (e is IOException || e is ArgumentException)
            {
                Debug.LogWarning($"[GateRush] Settings unreadable ({e.Message}); using defaults");
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
                Debug.LogWarning($"[GateRush] Could not save settings: {e.Message}");
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
