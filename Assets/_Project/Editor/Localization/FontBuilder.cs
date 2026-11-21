using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Ushqyr.Core;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace Ushqyr.EditorTools
{
    /// <summary>
    /// Bakes the Cyrillic and Kazakh letters into a font asset that never changes at run time (EDU_PLAN F7). The
    /// project's main font asset holds Latin only; until now every Russian and Kazakh letter was drawn into the
    /// "Fallback" asset while the program ran, which costs time on a weak PC and rewrites that asset after every run.
    /// The baked asset goes first in the main font's fallback list; the changing one stays behind it for the rare
    /// letter nobody planned for (a student's name in another alphabet).
    /// <para>Run again (<i>Ushqyr > Localization > Bake Cyrillic Font</i>) when the sheets gain a new symbol; a test says so.</para>
    /// </summary>
    public static class FontBuilder
    {
        public const string AssetPath = "Assets/_Project/Localization/Fonts/LiberationSans SDF - Cyrillic.asset";
        private const string SourceFont = "Assets/TextMesh Pro/Fonts/LiberationSans.ttf";
        private const string MainFont = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";
        private const string ChangingFont = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF - Fallback.asset";

        /// <summary>Russian and Kazakh alphabets in full, whether or not a text uses every letter today.</summary>
        public const string Alphabets =
            "АБВГДЕЁЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯабвгдеёжзийклмнопрстуфхцчшщъыьэюя" +
            "ӘәҒғҚқҢңӨөҰұҮүҺһІі";

        /// <summary>
        /// Signs that Russian and Kazakh text needs and the Latin asset may lack. The tenge sign (U+20B8) is not here:
        /// Liberation Sans has no such glyph, and no text of the program names a price.
        /// </summary>
        public const string Signs = "«»„“”‘’—–…№°×·−←→↑↓²³±≤≥•";

        /// <summary>
        /// What the baked asset must hold: the alphabets, the signs, and every other character of the translation
        /// sheets that the main (Latin) asset does not have.
        /// </summary>
        public static string Wanted()
        {
            var main = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(MainFont);
            var set = new SortedSet<char>(Alphabets + Signs);
            foreach (Translation t in StringImporter.LoadSheets())
                foreach (char c in t.En + t.Kk + t.Ru)
                    if (c > ' ' && !char.IsSurrogate(c)) set.Add(c);
            var sb = new StringBuilder();
            foreach (char c in set)
                if (main == null || !InTable(main, c)) sb.Append(c);
            return sb.ToString();
        }

        /// <summary>True when the asset itself holds the character (its fallbacks are not asked).</summary>
        public static bool InTable(TMP_FontAsset font, char c) => font.characterLookupTable != null && font.characterLookupTable.ContainsKey(c);

        [MenuItem("Ushqyr/Localization/Bake Cyrillic Font")]
        public static string Bake()
        {
            var source = AssetDatabase.LoadAssetAtPath<Font>(SourceFont);
            var main = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(MainFont);
            var changing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(ChangingFont);
            if (source == null || main == null) return "the main font or its source file was not found";

            string wanted = Wanted();
            // Nothing new to bake: leave the asset as it is, so that running this again changes no file.
            var old = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetPath);
            if (old != null && old.atlasPopulationMode == AtlasPopulationMode.Static && wanted.All(c => InTable(old, c))
                && main.fallbackFontAssetTable != null && main.fallbackFontAssetTable.Count > 0 && main.fallbackFontAssetTable[0] == old)
                return $"Up to date: {old.characterTable.Count} characters in {AssetPath}";
            // The same size and padding as the main asset, so letters of both stand on one line with one weight.
            int size = Mathf.RoundToInt(main.faceInfo.pointSize);
            TMP_FontAsset baked = TMP_FontAsset.CreateFontAsset(source, size, main.atlasPadding, GlyphRenderMode.SDFAA_HINTED, 1024, 1024,
                AtlasPopulationMode.Dynamic, false);
            baked.TryAddCharacters(wanted, out string missing);
            if (baked.atlasTextures.Length != 1) return "the letters did not fit one 1024 x 1024 picture";

            Directory.CreateDirectory(Path.GetDirectoryName(AssetPath));
            AssetDatabase.DeleteAsset(AssetPath);
            baked.name = Path.GetFileNameWithoutExtension(AssetPath);
            AssetDatabase.CreateAsset(baked, AssetPath);
            baked.atlasTextures[0].name = baked.name + " Atlas";
            AssetDatabase.AddObjectToAsset(baked.atlasTextures[0], baked);
            baked.material.name = baked.name + " Material";
            AssetDatabase.AddObjectToAsset(baked.material, baked);
            baked.atlasPopulationMode = AtlasPopulationMode.Static; // from here on it is never drawn into again
            EditorUtility.SetDirty(baked);

            var list = new List<TMP_FontAsset> { baked };
            if (changing != null) list.Add(changing);
            foreach (TMP_FontAsset other in main.fallbackFontAssetTable ?? new List<TMP_FontAsset>())
                if (other != null && !list.Contains(other)) list.Add(other);
            main.fallbackFontAssetTable = list;
            EditorUtility.SetDirty(main);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(AssetPath);

            string report = $"Baked {baked.characterTable.Count} characters at {size} pt into {AssetPath}"
                            + (string.IsNullOrEmpty(missing) ? "" : "; the font file has no: " + string.Join(" ", missing.Select(c => $"U+{(int)c:X4}")));
            Debug.Log("[Ushqyr] " + report);
            return report;
        }
    }
}
