using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using TMPro;
using Ushqyr.Core;
using UnityEditor;
using UnityEngine;

namespace Ushqyr.EditorTools
{
    /// <summary>
    /// The "no clipped text" check of EDU_PLAN F7. In play mode it opens every screen of whatever builds its screens
    /// with parameterless "Show…" methods (the main menu does), in each of the three languages, and lists every label
    /// whose text does not fit its box: cut with "…", or running past the box's edge.
    /// <para><i>Ushqyr > Localization > Check Text Fits</i> while the Menu scene is playing; the result is also returned
    /// for scripted runs.</para>
    /// </summary>
    public static class TextFitCheck
    {
        [MenuItem("Ushqyr/Localization/Check Text Fits")]
        public static void RunFromMenu() => Debug.Log("[Ushqyr] " + Run("MenuController"));

        /// <param name="typeName">The MonoBehaviour whose screens are walked, by class name.</param>
        public static string Run(string typeName)
        {
            if (!Application.isPlaying) return "enter play mode in the scene of the screen first";
            MonoBehaviour screen = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).FirstOrDefault(m => m.GetType().Name == typeName);
            if (screen == null) return "no " + typeName + " in the scene";
            MethodInfo[] shows = screen.GetType()
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(m => m.Name.StartsWith("Show", StringComparison.Ordinal) && m.GetParameters().Length == 0 && m.ReturnType == typeof(void))
                .OrderBy(m => m.Name, StringComparer.Ordinal).ToArray();
            Transform root = screen.GetComponentInParent<Canvas>() != null ? screen.GetComponentInParent<Canvas>().transform : screen.transform.root;

            string before = L.Current;
            var bad = new List<string>();
            var skipped = new List<string>();
            int labels = 0, screens = 0;
            // Opening a screen removes the one before it only at the end of the frame, and this walk runs inside one
            // frame: so only the labels a screen has just made are looked at.
            var seen = new HashSet<TMP_Text>();
            foreach (string code in L.Codes)
            {
                L.Set(code);
                foreach ((MethodInfo show, string tab) in Steps(screen, shows))
                {
                    try
                    {
                        if (tab != null) TabField(screen).SetValue(screen, tab);
                        show.Invoke(screen, null);
                    }
                    catch (Exception e)
                    {
                        // A screen that needs something this run does not have (a network session, a radio).
                        skipped.Add(show.Name + ": " + (e.InnerException ?? e).GetType().Name);
                        continue;
                    }
                    screens++;
                    Canvas.ForceUpdateCanvases();
                    foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(false))
                    {
                        if (!seen.Add(text) || string.IsNullOrEmpty(text.text)) continue;
                        text.ForceMeshUpdate();
                        labels++;
                        string why = Problem(text);
                        if (why != null) bad.Add($"{code} {show.Name}{(tab != null ? " [" + tab + "]" : "")}: {why}: \"{Short(text.text)}\"");
                    }
                }
            }
            L.Set(before);
            MethodInfo home = shows.FirstOrDefault(m => m.Name == "ShowMain") ?? shows.FirstOrDefault();
            try { home?.Invoke(screen, null); } catch (Exception) { /* the screen stays where the walk ended */ }

            var sb = new StringBuilder();
            sb.Append($"{screens} screens, {labels} labels, {bad.Count} that do not fit");
            if (skipped.Count > 0) sb.Append("; not opened: " + string.Join(", ", skipped.Distinct()));
            foreach (string line in bad.Distinct()) sb.Append("\n  " + line);
            return sb.ToString();
        }

        private static FieldInfo TabField(MonoBehaviour screen) =>
            screen.GetType().GetField("_settingsTab", BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>
        /// Every screen once; a settings screen with tabs (a "_settingsTab" field and a static "Tabs" list, as the
        /// main menu has) once for each tab.
        /// </summary>
        private static IEnumerable<(MethodInfo, string)> Steps(MonoBehaviour screen, MethodInfo[] shows)
        {
            FieldInfo tabs = screen.GetType().GetField("Tabs", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            foreach (MethodInfo show in shows)
            {
                if (show.Name == "ShowSettings" && TabField(screen) != null && tabs?.GetValue(null) is string[] names)
                    foreach (string name in names) yield return (show, name);
                else yield return (show, null);
            }
        }

        /// <summary>Why the label's text does not fit its box, or null when it does.</summary>
        public static string Problem(TMP_Text text)
        {
            if (text.isTextTruncated) return "cut short";
            Rect box = text.rectTransform.rect;
            if (box.width <= 0f || box.height <= 0f) return null; // sized by a layout that has not run: nothing to compare with
            Bounds drawn = text.textBounds;
            if (drawn.size.x > box.width + 2f) return "wider than its box by " + Mathf.RoundToInt(drawn.size.x - box.width);
            if (drawn.size.y > box.height + 2f) return "taller than its box by " + Mathf.RoundToInt(drawn.size.y - box.height);
            return null;
        }

        private static string Short(string s)
        {
            s = System.Text.RegularExpressions.Regex.Replace(s, "<[^>]+>", "").Replace("\n", " / ");
            return s.Length > 70 ? s.Substring(0, 70) + "…" : s;
        }
    }
}
