using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Ushqyr.EditorTools
{
    public static class BuildScript
    {
        public const string ApkPath = "Builds/Ushqyr.apk";
        public const string WindowsPath = "Builds/Windows/Ushqyr.exe";
        public const string MacPath = "Builds/macOS/Ushqyr.app";
        /// <summary>The game's version, one line ("1.4"): what every build and the installer are stamped with.</summary>
        public const string VersionPath = "Version.txt";
        public const string ResultPath = "Builds/build-result.txt";

        /// <summary>Starts the build on the next editor tick so a remote caller (MCP eval) returns immediately; poll <see cref="ResultPath"/>.</summary>
        public static string BuildQuestDeferred()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ResultPath));
            File.WriteAllText(ResultPath, "building " + System.DateTime.Now.ToString("HH:mm:ss"));
            EditorApplication.delayCall += BuildQuest;
            return ResultPath;
        }

        /// <summary>Entry point for -executeMethod Ushqyr.EditorTools.BuildScript.BuildQuest.</summary>
        [MenuItem("Ushqyr/Build/Build Quest APK")]
        public static void BuildQuest() => Build(ApkPath, BuildTarget.Android, BuildTargetGroup.Android);

        /// <summary>Entry point for -executeMethod Ushqyr.EditorTools.BuildScript.BuildWindows. Flat-screen: Standalone has no XR loader, the drone camera renders to the monitor.</summary>
        [MenuItem("Ushqyr/Build/Build Windows (PC)")]
        public static void BuildWindows() => BuildWindowsTo(WindowsPath);

        /// <summary>The Windows player into <paramref name="exePath"/>; opens borderless fullscreen at the monitor's resolution.</summary>
        public static void BuildWindowsTo(string exePath)
        {
            // First launch (no saved settings yet): fullscreen at native resolution. ScreenDisplay keeps it that way.
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.defaultIsNativeResolution = true;
            PlayerSettings.resizableWindow = true;
            Build(exePath, BuildTarget.StandaloneWindows64, BuildTargetGroup.Standalone);
        }

        /// <summary>
        /// The macOS player (Intel and Apple Silicon in one app) into <paramref name="appPath"/>; fullscreen at the
        /// display's resolution, like the Windows one. Built from Windows it is not code-signed.
        /// </summary>
        [MenuItem("Ushqyr/Build/Build macOS")]
        public static void BuildMac() => BuildMacTo(MacPath);

        public static void BuildMacTo(string appPath)
        {
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.defaultIsNativeResolution = true;
            PlayerSettings.resizableWindow = true;
            UnityEditor.OSXStandalone.UserBuildSettings.architecture = UnityEditor.Build.OSArchitecture.x64ARM64;
            Build(appPath, BuildTarget.StandaloneOSX, BuildTargetGroup.Standalone);
        }

        // ---------- version ----------

        /// <summary>The current version, from <see cref="VersionPath"/> (1.0 if there is none yet).</summary>
        public static string Version
        {
            get
            {
                string v = File.Exists(VersionPath) ? File.ReadAllText(VersionPath).Trim() : "";
                return string.IsNullOrEmpty(v) ? "1.0" : v;
            }
        }

        /// <summary>
        /// Moves the version on by one (1.0 -> 1.1 -> ... -> 1.9 -> 1.10) and stamps it into the player settings.
        /// Call once per build request, before the builds of that request, so all of them carry the same number.
        /// </summary>
        [MenuItem("Ushqyr/Build/Bump Version")]
        public static string BumpVersion()
        {
            var parts = Version.Split('.');
            int major = int.Parse(parts[0]), minor = parts.Length > 1 ? int.Parse(parts[1]) : 0;
            string next = $"{major}.{minor + 1}";
            File.WriteAllText(VersionPath, next + "\n");
            StampVersion();
            return next;
        }

        /// <summary>Puts <see cref="Version"/> into the player settings (Application.version, the macOS bundle).</summary>
        public static void StampVersion()
        {
            string v = Version;
            PlayerSettings.bundleVersion = v;
            PlayerSettings.macOS.buildNumber = v;
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Standalone, "kz.sdu.multimedialab.ushqyr");
            AssetDatabase.SaveAssets();
        }

        /// <summary>Like <see cref="BuildQuestDeferred"/>, for the Windows player.</summary>
        public static string BuildWindowsDeferred()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ResultPath));
            File.WriteAllText(ResultPath, "building " + System.DateTime.Now.ToString("HH:mm:ss"));
            EditorApplication.delayCall += BuildWindows;
            return ResultPath;
        }

        private static void Build(string path, BuildTarget target, BuildTargetGroup targetGroup)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = path,
                target = target,
                targetGroup = targetGroup,
                options = BuildOptions.None,
            };

            ApplyIcon();
            StampVersion();
            BuildReport report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            string line = $"Build {summary.result} v{Version}: {summary.outputPath} " +
                          $"({summary.totalSize / (1024 * 1024)} MB, {summary.totalErrors} errors, {summary.totalWarnings} warnings, {summary.totalTime:mm\\:ss})";
            Debug.Log("[Ushqyr] " + line);

            var sb = new System.Text.StringBuilder(line).AppendLine();
            foreach (var step in report.steps)
            foreach (var m in step.messages)
                if (m.type == LogType.Error || m.type == LogType.Exception || m.type == LogType.Warning)
                    sb.AppendLine($"[{m.type}] {step.name}: {m.content}");
            File.WriteAllText(ResultPath, sb.ToString());

            if (Application.isBatchMode)
                EditorApplication.Exit(summary.result == BuildResult.Succeeded ? 0 : 1);
        }

        public const string IconPath = "Assets/_Project/Art/Icon/Ushqyr.png";

        /// <summary>
        /// Sets the game's icon (the 5" Race drone over the stadium, made by Tools/Icon/make_icon.py) for every build,
        /// so the icon does not depend on what happens to be saved in the project settings.
        /// </summary>
        public static void ApplyIcon()
        {
            var importer = AssetImporter.GetAtPath(IconPath) as TextureImporter;
            if (importer == null) return;
            if (importer.mipmapEnabled || importer.textureCompression != TextureImporterCompression.Uncompressed || importer.npotScale != TextureImporterNPOTScale.None || !importer.alphaIsTransparency)
            {
                importer.mipmapEnabled = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.alphaIsTransparency = true;
                importer.SaveAndReimport();
            }
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            if (icon == null) return;
            PlayerSettings.SetIcons(UnityEditor.Build.NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
        }
    }
}
