using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Ushqyr.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.Localization.Tables;

namespace Ushqyr.Tests
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

        // ---------- text that does not reach L.T as a literal ----------

        /// <summary>Where text is put on the screen: assignments to a label and the code-built UI helpers.</summary>
        private static readonly Regex Sink = new Regex(
            @"\.text\s*\+?=|\bSetText\(|\bUiKit\.(Label|Button|SliderRow|StepperRow|SetButtonText)\(|\bBenchUi\.(Text|Button|SetLabel)\(|\bEduUi\.(Text|Button|SetLabel)\(",
            RegexOptions.Compiled);
        private static readonly Regex Literal = new Regex("\\$?\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.Compiled);

        /// <summary>
        /// Literals on those lines that are allowed to stay as they are: names of products and techniques, the keys of
        /// the keyboard, and words that are arguments of something else on the same line (not text).
        /// </summary>
        private static readonly HashSet<string> NotText = new HashSet<string>
        {
            "AI BENCH", "AI BENCH  ·  ", "USHQYR", "Ushqyr", "Betaflight", "Actual", "SMAA", "MSAA 2x", "MSAA 4x",
            "Тіл / Язык / Language:  ", "Space  ← →  C  − +", "  V  Esc",
            "  (Space)", "  (Esc)", // names of keys, as printed on the keyboard
            "hall", "parking", "blink", "front", " label",
            "   · PIN", // the same three letters in Kazakh, Russian and English
        };

        /// <summary>
        /// Files whose text is an on-screen display in the style of real FPV goggles (THR, MAH, KM/H) or a developer's
        /// overlay: abbreviations that pilots read in English on real equipment too.
        /// </summary>
        private static readonly string[] EnglishByDesign = { "OsdHud.cs", "FlightDebugOverlay.cs" };

        [Test]
        public void NoTextIsPutOnScreen_WithoutTheLanguageTable()
        {
            var sheets = LoadSheets(out _);
            var found = new List<string>();
            foreach (string path in Directory.GetFiles("Assets/_Project/Scripts", "*.cs", SearchOption.AllDirectories))
            {
                if (EnglishByDesign.Contains(Path.GetFileName(path))) continue;
                string[] lines = File.ReadAllLines(path);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (line.TrimStart().StartsWith("//") || !Sink.IsMatch(line)) continue;
                    foreach (Match m in Literal.Matches(line))
                    {
                        // A literal used as an index (_buttons["snap"]) is a name, not text.
                        if (m.Index > 0 && line[m.Index - 1] == '[') continue;
                        string text = Regex.Unescape(m.Groups[1].Value);
                        if (sheets.ContainsKey(text) || NotText.Contains(text)) continue;
                        // What is left after the placeholders and rich-text tags: is there a word in it?
                        string plain = Regex.Replace(text, @"\{[^}]*\}|<[^>]*>", " ");
                        if (!Regex.IsMatch(plain, "[A-Za-zА-Яа-яӘәҒғҚқҢңӨөҰұҮүҺһІі]{3,}")) continue;
                        found.Add($"{Path.GetFileName(path)}:{i + 1} \"{text}\"");
                    }
                }
            }
            Assert.IsEmpty(found, "text on screen that is not a key of the sheets (wrap it in L.T, or add it to NotText if it is a name):\n" + string.Join("\n", found));
        }

        [Test]
        public void TextKeptInData_IsTranslated()
        {
            var sheets = LoadSheets(out _);
            var missing = new List<string>();
            void Need(string where, string text)
            {
                if (!string.IsNullOrEmpty(text) && !sheets.ContainsKey(text)) missing.Add(where + ": \"" + text + "\"");
            }

            foreach (string mode in ScreenDisplay.Modes) Need("display mode", mode);
            foreach (string reason in Ushqyr.Game.ControllerHotplugGuard.Reasons) Need("pause reason", reason);
            foreach (Ushqyr.Garage.SizeClass size in System.Enum.GetValues(typeof(Ushqyr.Garage.SizeClass)))
                Need("size", Ushqyr.Garage.PartCatalog.SizeLabel(size));

            // The calibration wizard keeps its instructions as plain literals (its assembly does not know the tables).
            string wizard = File.ReadAllText("Assets/_Project/Scripts/Input/CalibrationWizard.cs");
            int steps = 0;
            foreach (Match m in Regex.Matches(wizard, "return\\s+\"((?:[^\"\\\\]|\\\\.)*)\";"))
            {
                Need("calibration wizard", Regex.Unescape(m.Groups[1].Value));
                steps++;
            }
            Assert.Greater(steps, 5, "the wizard's instructions were found");

            // Parts: every description, and every name that is a description in itself ("Action camera"). Product
            // names with a number in them and bare battery figures stay as they are in every language.
            foreach (Ushqyr.Garage.PartDef part in Ushqyr.Garage.PartCatalog.All)
            {
                if (!Regex.IsMatch(part.Name ?? "", "[0-9]")) Need("part name", part.Name);
                if (!Regex.IsMatch(part.Description ?? "", @"^\dS \d+ mAh, \d+C\.$")) Need("part description", part.Description);
            }

            var quads = AssetDatabase.LoadAssetAtPath<Ushqyr.Game.QuadCatalog>("Assets/_Project/Data/QuadCatalog.asset");
            var tracks = AssetDatabase.LoadAssetAtPath<Ushqyr.Game.TrackCatalog>("Assets/_Project/Data/TrackCatalog.asset");
            Assert.IsNotNull(quads);
            Assert.IsNotNull(tracks);
            foreach (var q in quads.Quads) { Need("quad", q.DisplayName); Need("quad", q.Description); }
            foreach (var t in tracks.Tracks) { Need("track", t.DisplayName); Need("track", t.Description); Need("track", t.Variation); }
            foreach (var e in tracks.Environments) { Need("place", e.DisplayName); Need("place", e.Description); }

            // The Education Suite's lists of names shown through L.T.
            foreach (string name in System.Enum.GetNames(typeof(Ushqyr.Flight.FlightMode))) Need("flight mode", name);
            foreach (string name in new[] { "Chase", "Onboard", "Fixed", "From above", "Free", "Height", "Speed", "Throttle", "crash", "respawn", "gate" })
                Need("replay viewer", name);

            // Activities: what the pilot reads in the briefing, on the way and in the result.
            Assert.IsNotEmpty(Ushqyr.Edu.EduSession.Activities);
            foreach (Ushqyr.Edu.ActivityDefinition a in Ushqyr.Edu.EduSession.Activities.Values)
            {
                Need(a.Id + " title", a.Title);
                Need(a.Id + " briefing", a.Briefing);
                foreach (Ushqyr.Edu.Criterion c in a.Criteria)
                {
                    Need(a.Id + " label", c.Label);
                    Need(a.Id + " tip", c.Tip);
                    if (c.Steps != null)
                        foreach (Ushqyr.Edu.StepSpec step in c.Steps) Need(a.Id + " step", step.Say);
                }
            }
            foreach (string name in Ushqyr.Edu.Course.BlockNames) Need("course block", name);
            foreach (string name in Ushqyr.Edu.EduFlight.ModeNames) Need("flight mode", name);

            Assert.IsEmpty(missing, string.Join("\n", missing));
        }

        /// <summary>
        /// Every character of every translation is in a font asset that does not change while the program runs: the
        /// main (Latin) one or the baked Cyrillic and Kazakh one. Nothing is left for the asset that draws letters on
        /// demand, so a Kazakh screen costs a weak PC nothing extra and leaves no asset changed behind it.
        /// </summary>
        [Test]
        public void EveryCharacterOfTheSheets_IsInABakedFont()
        {
            var main = TMPro.TMP_Settings.defaultFontAsset;
            var baked = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(Ushqyr.EditorTools.FontBuilder.AssetPath);
            Assert.IsNotNull(baked, "run Ushqyr > Localization > Bake Cyrillic Font");
            Assert.AreEqual(TMPro.AtlasPopulationMode.Static, baked.atlasPopulationMode);
            Assert.AreEqual(TMPro.AtlasPopulationMode.Static, main.atlasPopulationMode);
            Assert.AreSame(baked, main.fallbackFontAssetTable[0], "the baked letters are asked before the changing asset");
            Assert.AreEqual(main.atlasPadding / main.faceInfo.pointSize, baked.atlasPadding / baked.faceInfo.pointSize, 1e-4,
                "the same padding for the same size, or the two alphabets differ in weight on one line");

            var missing = new SortedSet<char>();
            var sheets = LoadSheets(out _);
            string all = Ushqyr.EditorTools.FontBuilder.Alphabets + string.Concat(sheets.Values.Select(t => t.En + t.Kk + t.Ru));
            foreach (char c in all)
                if (c > ' ' && !Ushqyr.EditorTools.FontBuilder.InTable(main, c) && !Ushqyr.EditorTools.FontBuilder.InTable(baked, c)) missing.Add(c);
            Assert.IsEmpty(missing, "not in a baked font (run Ushqyr > Localization > Bake Cyrillic Font): "
                                    + string.Join(" ", missing.Select(c => $"{c} U+{(int)c:X4}")));
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
                Assert.IsEmpty(stale, $"{table.LocaleIdentifier.Code} table is behind the sheets: run Ushqyr > Localization > Import Strings");
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
