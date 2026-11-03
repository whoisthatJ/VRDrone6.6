using System.Collections.Generic;
using System.IO;
using System.Linq;
using GateRush.Core;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;

namespace GateRush.EditorTools
{
    /// <summary>
    /// Builds the Unity Localization setup from the translation sheets: settings asset, the kk / ru / en locales
    /// (Kazakh first and the startup default) and the "UI" string table collection, one entry per English key.
    /// The .tsv sheets are the source; re-run after editing them (<i>GateRush > Localization > Import Strings</i>).
    /// </summary>
    public static class StringImporter
    {
        public const string Root = "Assets/_Project/Localization";
        public const string SheetFolder = Root + "/Text";
        private const string SettingsPath = Root + "/LocalizationSettings.asset";
        private const string LocaleFolder = Root + "/Locales";
        private const string TableFolder = Root + "/Tables";

        public static List<Translation> LoadSheets()
        {
            var all = new List<Translation>();
            foreach (string path in Directory.GetFiles(SheetFolder, "*.tsv").OrderBy(p => p))
                all.AddRange(StringSheet.Parse(File.ReadAllText(path), Path.GetFileName(path)));
            return all;
        }

        [MenuItem("GateRush/Localization/Import Strings")]
        public static string Import()
        {
            var settings = EnsureSettings();
            var locales = EnsureLocales();

            var collection = LocalizationEditorSettings.GetStringTableCollection(L.TableName)
                             ?? LocalizationEditorSettings.CreateStringTableCollection(L.TableName, TableFolder, locales.Values.ToList());
            foreach (var locale in locales.Values)
                if (collection.GetTable(locale.Identifier) == null) collection.AddNewTable(locale.Identifier);

            var sheets = LoadSheets();
            var keys = new HashSet<string>();
            foreach (var t in sheets)
            {
                if (!keys.Add(t.En)) continue; // first sheet wins on duplicates (a test reports them)
                Set(collection, "en", t.En, t.En);
                Set(collection, "kk", t.En, t.Kk);
                Set(collection, "ru", t.En, t.Ru);
            }

            // Drop keys no sheet has any more.
            var stale = collection.SharedData.Entries.Where(e => !keys.Contains(e.Key)).Select(e => e.Id).ToList();
            foreach (long id in stale) collection.RemoveEntry(id);

            foreach (var table in collection.StringTables) EditorUtility.SetDirty(table);
            EditorUtility.SetDirty(collection);
            EditorUtility.SetDirty(collection.SharedData);
            LocalizationEditorSettings.EditorEvents.RaiseCollectionModified(null, collection);
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            string report = $"Imported {keys.Count} strings ({stale.Count} removed) into '{L.TableName}' for {string.Join(", ", locales.Keys)}";
            Debug.Log("[GateRush] " + report);
            return report;
        }

        private static void Set(StringTableCollection collection, string code, string key, string value)
        {
            var table = (StringTable)collection.GetTable(new LocaleIdentifier(code));
            var entry = table.GetEntry(key);
            if (entry == null) table.AddEntry(key, value);
            else entry.Value = value;
        }

        private static LocalizationSettings EnsureSettings()
        {
            var settings = LocalizationEditorSettings.ActiveLocalizationSettings;
            if (settings == null)
            {
                string guid = AssetDatabase.FindAssets("t:LocalizationSettings", new[] { "Assets" }).FirstOrDefault();
                settings = guid != null ? AssetDatabase.LoadAssetAtPath<LocalizationSettings>(AssetDatabase.GUIDToAssetPath(guid)) : null;
                if (settings == null)
                {
                    Directory.CreateDirectory(Root);
                    settings = ScriptableObject.CreateInstance<LocalizationSettings>();
                    AssetDatabase.CreateAsset(settings, SettingsPath);
                }
                LocalizationEditorSettings.ActiveLocalizationSettings = settings;
            }

            // Start in Kazakh unless the game's own setting says otherwise (GateRush.Core.L applies that at startup).
            var selectors = settings.GetStartupLocaleSelectors();
            selectors.Clear();
            selectors.Add(new CommandLineLocaleSelector());
            selectors.Add(new SpecificLocaleSelector { LocaleId = new LocaleIdentifier(L.Default) });
            return settings;
        }

        private static Dictionary<string, Locale> EnsureLocales()
        {
            Directory.CreateDirectory(LocaleFolder);
            var result = new Dictionary<string, Locale>();
            foreach (string code in L.Codes)
            {
                var locale = LocalizationEditorSettings.GetLocale(code);
                if (locale == null)
                {
                    locale = Locale.CreateLocale(new LocaleIdentifier(code));
                    string english = locale.Identifier.CultureInfo != null ? locale.Identifier.CultureInfo.EnglishName : code;
                    locale.name = $"{english} ({code})";
                    AssetDatabase.CreateAsset(locale, $"{LocaleFolder}/{locale.name}.asset");
                    LocalizationEditorSettings.AddLocale(locale);
                }
                result[code] = locale;
            }
            return result;
        }
    }
}
