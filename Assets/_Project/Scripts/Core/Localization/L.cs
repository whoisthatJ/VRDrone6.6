using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;

namespace GateRush.Core
{
    /// <summary>
    /// Text in the player's language through the Unity Localization package. Keys are the English text, so code
    /// reads <c>L.T("Quick race")</c>; the "UI" string table (built from <c>Assets/_Project/Localization/Text/*.tsv</c>
    /// by <i>GateRush > Localization > Import Strings</i>) holds Kazakh, Russian and English. Kazakh is the default.
    /// The language lives in <see cref="GameSettings.Language"/> and is applied with
    /// <see cref="LocalizationSettings.SelectedLocale"/>; UI built in code rebuilds on <see cref="Changed"/>.
    /// </summary>
    public static class L
    {
        public const string TableName = "UI";
        public const string Default = "kk";

        /// <summary>Language codes in menu order, with their own names.</summary>
        public static readonly string[] Codes = { "kk", "ru", "en" };
        public static readonly string[] Names = { "Қазақша", "Русский", "English" };

        /// <summary>The language changed: rebuild text that was built from <see cref="T"/>.</summary>
        public static event Action Changed;

        private static StringTable _table;
        private static string _tableCode;

        public static string Current
        {
            get
            {
                var locale = Ready ? LocalizationSettings.SelectedLocale : null;
                return locale != null ? locale.Identifier.Code : Default;
            }
        }

        private static bool Ready => LocalizationSettings.HasSettings && LocalizationSettings.InitializationOperation.IsDone;

        /// <summary>The text in the current language (the English key itself when there is no translation).</summary>
        public static string T(string english)
        {
            if (string.IsNullOrEmpty(english)) return english;
            var table = Table();
            var entry = table != null ? table.GetEntry(english) : null;
            string value = entry != null ? entry.Value : null;
            return string.IsNullOrEmpty(value) ? english : value;
        }

        /// <summary><see cref="T"/> then <see cref="string.Format(IFormatProvider, string, object[])"/>: "{0} laps".</summary>
        public static string F(string english, params object[] args) =>
            string.Format(CultureInfo.InvariantCulture, T(english), args);

        /// <summary>Switches language (a code from <see cref="Codes"/>).</summary>
        public static void Set(string code)
        {
            if (Array.IndexOf(Codes, code) < 0) code = Default;
            if (!LocalizationSettings.HasSettings) return;
            Init();
            var locale = LocalizationSettings.AvailableLocales.GetLocale(code);
            if (locale != null && LocalizationSettings.SelectedLocale != locale) LocalizationSettings.SelectedLocale = locale;
        }

        /// <summary>Next language in <see cref="Codes"/> after <paramref name="code"/>.</summary>
        public static string Next(string code) => Codes[(Math.Max(0, Array.IndexOf(Codes, code)) + 1) % Codes.Length];

        public static string NameOf(string code)
        {
            int i = Array.IndexOf(Codes, code);
            return i >= 0 ? Names[i] : code;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Startup()
        {
            if (!LocalizationSettings.HasSettings) return;
            Init();
            Set(SaveSystem.Settings.Language);
            LocalizationSettings.SelectedLocaleChanged -= OnLocaleChanged;
            LocalizationSettings.SelectedLocaleChanged += OnLocaleChanged;
        }

        private static void Init()
        {
            var init = LocalizationSettings.InitializationOperation;
            if (!init.IsDone) init.WaitForCompletion();
        }

        private static void OnLocaleChanged(Locale locale)
        {
            _table = null;
            Changed?.Invoke();
        }

        private static StringTable Table()
        {
            if (!LocalizationSettings.HasSettings) return null;
            Init();
            var locale = LocalizationSettings.SelectedLocale;
            if (locale == null) return null;
            string code = locale.Identifier.Code;
            if (_table != null && _tableCode == code) return _table;
            _table = LocalizationSettings.StringDatabase.GetTable(TableName, locale);
            _tableCode = code;
            return _table;
        }
    }
}
