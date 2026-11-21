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
        public const string LinuxPath = "Builds/Linux/Ushqyr.x86_64";
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

        /// <summary>
        /// The Linux player (x86-64, Mono) into <paramref name="path"/>: the same game, for teams on Linux and, started
        /// in batch mode under a virtual display, for the qualification server.
        /// </summary>
        [MenuItem("Ushqyr/Build/Build Linux")]
        public static void BuildLinux() => BuildLinuxTo(LinuxPath);

        public static void BuildLinuxTo(string path)
        {
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.defaultIsNativeResolution = true;
            PlayerSettings.resizableWindow = true;
            Build(path, BuildTarget.StandaloneLinux64, BuildTargetGroup.Standalone);
        }

        public const string BenchServerPath = "Builds/BenchServer/UshqyrBench.x86_64";

        /// <summary>
        /// The qualification server's player: Linux, the AI Bench scenes only, compiled with USHQYR_BENCH_SERVER so
        /// ground truth, the oracle, the track editor and the dataset tool are not in it. Run it in batch mode under
        /// a virtual display (not -nographics: the drone's camera has to render).
        /// </summary>
        [MenuItem("Ushqyr/Build/Build Bench Server (Linux)")]
        public static void BuildBenchServer()
        {
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultIsNativeResolution = false;
            PlayerSettings.resizableWindow = true;
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path)
                .Where(s => s.EndsWith("/Boot.unity") || Path.GetFileName(s).StartsWith("Bench_")).ToArray();
            Build(BenchServerPath, BuildTarget.StandaloneLinux64, BuildTargetGroup.Standalone, scenes, new[] { "USHQYR_BENCH_SERVER" });
        }

        public static string BuildBenchServerDeferred()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ResultPath));
            File.WriteAllText(ResultPath, "building " + System.DateTime.Now.ToString("HH:mm:ss"));
            EditorApplication.delayCall += BuildBenchServer;
            return ResultPath;
        }

        public const string TeamKitWindowsPath = "Builds/TeamKit/Windows/Ushqyr.exe";
        public const string TeamKitLinuxPath = "Builds/TeamKit/Linux/Ushqyr.x86_64";

        /// <summary>
        /// The teams' player: the AI Bench scenes only, compiled with USHQYR_BENCH_KIT so it opens AI Bench straight
        /// away. It keeps what the server build leaves out (track editor, oracle pilot, dataset tool, training mode).
        /// `ushqyr-bench make-kit` puts the SDK, examples and documentation beside it.
        /// </summary>
        [MenuItem("Ushqyr/Build/Build Team Kit (Windows)")]
        public static void BuildTeamKitWindows() => BuildTeamKit(TeamKitWindowsPath, BuildTarget.StandaloneWindows64);

        [MenuItem("Ushqyr/Build/Build Team Kit (Linux)")]
        public static void BuildTeamKitLinux() => BuildTeamKit(TeamKitLinuxPath, BuildTarget.StandaloneLinux64);

        private static void BuildTeamKit(string path, BuildTarget target)
        {
            // A window, not fullscreen: a team works with a terminal and an editor beside the simulator.
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultIsNativeResolution = false;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = true;
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path)
                .Where(s => s.EndsWith("/Boot.unity") || Path.GetFileName(s).StartsWith("Bench_")).ToArray();
            Build(path, target, BuildTargetGroup.Standalone, scenes, new[] { "USHQYR_BENCH_KIT" });
        }

        public static string BuildTeamKitWindowsDeferred() => Deferred(BuildTeamKitWindows);
        public static string BuildTeamKitLinuxDeferred() => Deferred(BuildTeamKitLinux);

        // ---------- the school build ----------

        public const string SchoolWindowsPath = "Builds/School/Windows/Ushqyr.exe";
        public const string SchoolProbePath = "Builds/SchoolProbe/Ushqyr.exe";

        /// <summary>
        /// The version for schools (EDU_PLAN F5): the whole program, but with nothing in it that can talk to a server
        /// on the internet. Run <c>python -I Tools/Edu/school_manifest.py strip</c> first and wait for the editor to
        /// finish; this refuses to build while the internet packages are still in the project. Afterwards it looks
        /// into the built program and reports any library that should not be there.
        /// </summary>
        [MenuItem("Ushqyr/Build/Build School Version (Windows)")]
        public static void BuildSchoolWindows() => BuildSchool(SchoolWindowsPath, null);

        /// <summary>The same check on a small build (the start-up and AI Bench scenes only): a minute instead of an hour.</summary>
        public static void BuildSchoolProbe() => BuildSchool(SchoolProbePath,
            EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path)
                .Where(s => s.EndsWith("/Boot.unity") || Path.GetFileName(s).StartsWith("Bench_")).ToArray());

        public static string BuildSchoolWindowsDeferred() => Deferred(BuildSchoolWindows);
        public static string BuildSchoolProbeDeferred() => Deferred(BuildSchoolProbe);

        private static void BuildSchool(string path, string[] scenes)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ResultPath));
            if (System.AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name.StartsWith("Unity.Services.", System.StringComparison.Ordinal)))
            {
                File.WriteAllText(ResultPath, "Build Failed: the internet packages are still in the project. Run \"python -I Tools/Edu/school_manifest.py strip\", wait for the editor, then build again.");
                Debug.LogError("[Ushqyr] School build refused: the internet packages are still in the project.");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.defaultIsNativeResolution = true;
            PlayerSettings.resizableWindow = true;
            // The engine's own reporting to Unity: off for this build, and put back afterwards.
            bool analytics = UnityEditor.Analytics.AnalyticsSettings.enabled;
            bool crashes = UnityEditor.CrashReporting.CrashReportingSettings.enabled;
            bool performance = UnityEditor.Analytics.PerformanceReportingSettings.enabled;
            // The engine's own hardware statistics and its link to Unity's services have no scripting switch in this
            // version; they are fields of the project settings.
            bool statistics = ProjectFlag("ProjectSettings/ProjectSettings.asset", "submitAnalytics", false);
            bool connect = ProjectFlag("ProjectSettings/UnityConnectSettings.asset", "m_Enabled", false);
            // "Engine Diagnostics" (Project Settings > Services > Insights): on by default, it sends data about the
            // running program to engine-data.unity3d.com.
            bool diagnostics = ProjectFlag("ProjectSettings/UnityConnectSettings.asset", "InsightsSettings.m_EngineDiagnosticsEnabled", false);
            bool insights = ProjectFlag("ProjectSettings/UnityConnectSettings.asset", "InsightsSettings.m_Enabled", false);
            UnityEditor.Analytics.PerformanceReportingSettings.enabled = false;
            UnityEditor.Analytics.AnalyticsSettings.enabled = false;
            UnityEditor.CrashReporting.CrashReportingSettings.enabled = false;
            try
            {
                Build(path, BuildTarget.StandaloneWindows64, BuildTargetGroup.Standalone, scenes, new[] { "USHQYR_SCHOOL" }, () => SchoolCheck(path));
            }
            finally
            {
                UnityEditor.Analytics.AnalyticsSettings.enabled = analytics;
                UnityEditor.CrashReporting.CrashReportingSettings.enabled = crashes;
                UnityEditor.Analytics.PerformanceReportingSettings.enabled = performance;
                ProjectFlag("ProjectSettings/ProjectSettings.asset", "submitAnalytics", statistics);
                ProjectFlag("ProjectSettings/UnityConnectSettings.asset", "m_Enabled", connect);
                ProjectFlag("ProjectSettings/UnityConnectSettings.asset", "InsightsSettings.m_EngineDiagnosticsEnabled", diagnostics);
                ProjectFlag("ProjectSettings/UnityConnectSettings.asset", "InsightsSettings.m_Enabled", insights);
                AssetDatabase.SaveAssets();
            }
        }

        /// <summary>Sets a yes/no field of a project settings file and returns what it was (true when there is no such field).</summary>
        private static bool ProjectFlag(string settingsFile, string field, bool value)
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(settingsFile);
            if (assets == null || assets.Length == 0) return true;
            var settings = new SerializedObject(assets[0]);
            SerializedProperty property = settings.FindProperty(field);
            if (property == null || property.propertyType != SerializedPropertyType.Boolean)
            {
                Debug.LogWarning($"[Ushqyr] {settingsFile} has no yes/no field '{field}'");
                return true;
            }
            bool before = property.boolValue;
            property.boolValue = value;
            settings.ApplyModifiedPropertiesWithoutUndo();
            return before;
        }

        /// <summary>Libraries of a built program that exist to reach the internet; the school build must have none.</summary>
        public static string[] InternetLibraries(string exePath)
        {
            string managed = Path.Combine(Path.GetDirectoryName(exePath), Path.GetFileNameWithoutExtension(exePath) + "_Data", "Managed");
            if (!Directory.Exists(managed)) return new[] { "(no Managed folder at " + managed + ")" };
            return Directory.GetFiles(managed, "*.dll").Select(Path.GetFileName)
                .Where(n => n.StartsWith("Unity.Services.", System.StringComparison.OrdinalIgnoreCase)
                            || n.IndexOf("UnityAnalytics", System.StringComparison.OrdinalIgnoreCase) >= 0
                            || n.IndexOf("UnityConnect", System.StringComparison.OrdinalIgnoreCase) >= 0
                            || n.IndexOf("CrashReporting", System.StringComparison.OrdinalIgnoreCase) >= 0
                            || n.IndexOf("PerformanceReporting", System.StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(n => n).ToArray();
        }

        private static string SchoolCheck(string exePath)
        {
            string[] found = InternetLibraries(exePath);
            string[] services = found.Where(n => n.StartsWith("Unity.Services.", System.StringComparison.OrdinalIgnoreCase)).ToArray();
            if (services.Length > 0) return "School check FAILED: " + string.Join(", ", services);
            // The engine's own reporting modules cannot be left out of a program built this way (they are part of the
            // engine's managed layer); they are switched off in the settings of this build and again at start-up.
            return "School check passed: no Unity services library in the program."
                   + (found.Length > 0 ? " Engine reporting modules present but switched off: " + string.Join(", ", found) + "." : "")
                   + " Confirm with a run: the program must hold no connection (Docs/edu/school_build.md).";
        }

        private static string Deferred(EditorApplication.CallbackFunction build)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ResultPath));
            File.WriteAllText(ResultPath, "building " + System.DateTime.Now.ToString("HH:mm:ss"));
            EditorApplication.delayCall += build;
            return ResultPath;
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

        private static void Build(string path, BuildTarget target, BuildTargetGroup targetGroup, string[] scenes = null, string[] defines = null,
            System.Func<string> afterBuild = null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            scenes ??= EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = path,
                target = target,
                targetGroup = targetGroup,
                options = BuildOptions.None,
                extraScriptingDefines = defines,
            };

            ApplyIcon();
            StampVersion();
            BuildAddressables(target, targetGroup);
            BuildReport report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            string line = $"Build {summary.result} v{Version}: {summary.outputPath} " +
                          $"({summary.totalSize / (1024 * 1024)} MB, {summary.totalErrors} errors, {summary.totalWarnings} warnings, {summary.totalTime:mm\\:ss})";
            Debug.Log("[Ushqyr] " + line);

            var sb = new System.Text.StringBuilder(line).AppendLine();
            if (afterBuild != null && summary.result == BuildResult.Succeeded)
            {
                string extra = afterBuild();
                Debug.Log("[Ushqyr] " + extra);
                sb.AppendLine(extra);
            }
            foreach (var step in report.steps)
            foreach (var m in step.messages)
                if (m.type == LogType.Error || m.type == LogType.Exception || m.type == LogType.Warning)
                    sb.AppendLine($"[{m.type}] {step.name}: {m.content}");
            File.WriteAllText(ResultPath, sb.ToString());

            if (Application.isBatchMode)
                EditorApplication.Exit(summary.result == BuildResult.Succeeded ? 0 : 1);
        }

        /// <summary>
        /// Rebuilds the addressable content (the language tables) for <paramref name="target"/>. Without this a build
        /// for another platform carries the bundles of the platform built last and cannot load them.
        /// </summary>
        private static void BuildAddressables(BuildTarget target, BuildTargetGroup targetGroup)
        {
            if (EditorUserBuildSettings.activeBuildTarget != target)
                EditorUserBuildSettings.SwitchActiveBuildTarget(targetGroup, target);
            var settings = System.Type.GetType("UnityEditor.AddressableAssets.Settings.AddressableAssetSettings, Unity.Addressables.Editor");
            var build = settings?.GetMethod("BuildPlayerContent", System.Type.EmptyTypes);
            if (build == null) { Debug.LogWarning("[Ushqyr] Addressables not found: content not rebuilt"); return; }
            build.Invoke(null, null);
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
