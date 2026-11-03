using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using GateRush.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.Localization.Tables;

namespace GateRush.Tests
{
    /// <summary>
    /// Every string the code passes to L.T / L.F has a Kazakh and a Russian translation with the same placeholders,
    /// and the Unity string tables are in step with the sheets. Kazakh is the default language.
    /// </summary>
    public class LocalizationTests
    {
        private const string Sheets = "Assets/_Project/Localization/Text";
        private static readonly Regex Call = new Regex("\\bL\\.[TF]\\(\\s*\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.Compiled);

        private static Dictionary<string, Translation> LoadSheets(out List<string> duplicates)
        {
            var map = new Dictionary<string, Translation>();
            duplicates = new List<string>();
            foreach (string path in Directory.GetFiles(Sheets, "*.tsv"))
                foreach (var t in StringSheet.Parse(File.ReadAllText(path), Path.GetFileName(path)))
                {
                    if (map.ContainsKey(t.En)) duplicates.Add($"{t.File}:{t.Line} \"{t.En}\" (also {map[t.En].File}:{map[t.En].Line})");
                    else map[t.En] = t;
                }
            return map;
        }

        /// <summary>Keys used in code: string literals passed straight to L.T / L.F, C# escapes resolved.</summary>
        private static Dictionary<string, string> KeysInCode()
        {
            var keys = new Dictionary<string, string>();
            foreach (string path in Directory.GetFiles("Assets/_Project/Scripts", "*.cs", SearchOption.AllDirectories))
            {
                string[] lines = File.ReadAllLines(path);
                for (int i = 0; i < lines.Length; i++)
                    foreach (Match m in Call.Matches(lines[i]))
                    {
                        string key = Regex.Unescape(m.Groups[1].Value);
                        if (!keys.ContainsKey(key)) keys[key] = $"{Path.GetFileName(path)}:{i + 1}";
                    }
            }
            return keys;
        }

        [Test]
        public void EveryStringInCode_IsTranslated()
        {
            var sheets = LoadSheets(out _);
            var code = KeysInCode();
            Assert.Greater(code.Count, 0, "the scan found L.T calls");
            var missing = new List<string>();
            foreach (var kv in code)
            {
                if (!sheets.TryGetValue(kv.Key, out var t)) missing.Add($"{kv.Value} \"{kv.Key}\" has no sheet row");
                else if (string.IsNullOrWhiteSpace(t.Kk) || string.IsNullOrWhiteSpace(t.Ru)) missing.Add($"{kv.Value} \"{kv.Key}\" lacks kk or ru");
            }
            Assert.IsEmpty(missing, string.Join("\n", missing));
        }

        [Test]
        public void Sheets_HaveNoDuplicates_AndKeepPlaceholders()
        {
            var sheets = LoadSheets(out var duplicates);
            Assert.IsEmpty(duplicates, string.Join("\n", duplicates));
            var bad = new List<string>();
            foreach (var t in sheets.Values)
            {
                string want = StringSheet.Placeholders(t.En);
                if (StringSheet.Placeholders(t.Kk) != want) bad.Add($"{t.File}:{t.Line} kk placeholders differ: \"{t.En}\"");
                if (StringSheet.Placeholders(t.Ru) != want) bad.Add($"{t.File}:{t.Line} ru placeholders differ: \"{t.En}\"");
                if (t.En.Contains("<") && (CountTags(t.Kk) != CountTags(t.En) || CountTags(t.Ru) != CountTags(t.En)))
                    bad.Add($"{t.File}:{t.Line} rich-text tags differ: \"{t.En}\"");
            }
            Assert.IsEmpty(bad, string.Join("\n", bad));
        }

        private static int CountTags(string s) => Regex.Matches(s ?? "", "<[^>]+>").Count;

        [Test]
        public void UnityTables_MatchTheSheets()
        {
            var sheets = LoadSheets(out _);
            var tables = AssetDatabase.FindAssets("t:StringTable", new[] { "Assets/_Project/Localization" })
                .Select(g => AssetDatabase.LoadAssetAtPath<StringTable>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(t => t != null && t.TableCollectionName == L.TableName)
                .ToList();
            CollectionAssert.AreEquivalent(L.Codes, tables.Select(t => t.LocaleIdentifier.Code), "kk, ru and en tables");
            foreach (var table in tables)
            {
                var stale = sheets.Keys.Where(k => table.GetEntry(k) == null).Take(5).ToList();
                Assert.IsEmpty(stale, $"{table.LocaleIdentifier.Code} table is behind the sheets: run GateRush > Localization > Import Strings");
            }
            var kk = tables.First(t => t.LocaleIdentifier.Code == "kk");
            var sample = sheets.Values.First();
            Assert.AreEqual(sample.Kk, kk.GetEntry(sample.En).Value);
        }

        [Test]
        public void KazakhIsTheDefault()
        {
            Assert.AreEqual("kk", L.Default);
            Assert.AreEqual("kk", new GameSettings().Language);
            var s = new GameSettings { Language = "de" };
            s.Clamp();
            Assert.AreEqual("kk", s.Language, "unknown languages fall back to Kazakh");
            Assert.AreEqual("ru", L.Next("kk"));
            Assert.AreEqual("kk", L.Next("en"));
        }

        [Test]
        public void Sheet_ParsesEscapesAndComments()
        {
            var rows = StringSheet.Parse("# comment\nA\\nB\tә\\tx\tя\n\nC\tқ\tк\n");
            Assert.AreEqual(2, rows.Count);
            Assert.AreEqual("A\nB", rows[0].En);
            Assert.AreEqual("ә\tx", rows[0].Kk);
            Assert.AreEqual("0,1", StringSheet.Placeholders("{1} of {0:F1} {{x}}"));
        }
    }
}
