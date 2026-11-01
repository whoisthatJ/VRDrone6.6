using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace GateRush.EditorTools
{
    public static class BuildScript
    {
        public const string ApkPath = "Builds/GateRush.apk";
        public const string WindowsPath = "Builds/Windows/GateRush.exe";
        public const string ResultPath = "Builds/build-result.txt";

        /// <summary>Starts the build on the next editor tick so a remote caller (MCP eval) returns immediately; poll <see cref="ResultPath"/>.</summary>
        public static string BuildQuestDeferred()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ResultPath));
            File.WriteAllText(ResultPath, "building " + System.DateTime.Now.ToString("HH:mm:ss"));
            EditorApplication.delayCall += BuildQuest;
            return ResultPath;
        }

        /// <summary>Entry point for -executeMethod GateRush.EditorTools.BuildScript.BuildQuest.</summary>
        [MenuItem("GateRush/Build/Build Quest APK")]
        public static void BuildQuest() => Build(ApkPath, BuildTarget.Android, BuildTargetGroup.Android);

        /// <summary>Entry point for -executeMethod GateRush.EditorTools.BuildScript.BuildWindows. Flat-screen: Standalone has no XR loader, the drone camera renders to the monitor.</summary>
        [MenuItem("GateRush/Build/Build Windows (PC)")]
        public static void BuildWindows() => Build(WindowsPath, BuildTarget.StandaloneWindows64, BuildTargetGroup.Standalone);

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

            BuildReport report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            string line = $"Build {summary.result}: {summary.outputPath} " +
                          $"({summary.totalSize / (1024 * 1024)} MB, {summary.totalErrors} errors, {summary.totalWarnings} warnings, {summary.totalTime:mm\\:ss})";
            Debug.Log("[GateRush] " + line);

            var sb = new System.Text.StringBuilder(line).AppendLine();
            foreach (var step in report.steps)
            foreach (var m in step.messages)
                if (m.type == LogType.Error || m.type == LogType.Exception || m.type == LogType.Warning)
                    sb.AppendLine($"[{m.type}] {step.name}: {m.content}");
            File.WriteAllText(ResultPath, sb.ToString());

            if (Application.isBatchMode)
                EditorApplication.Exit(summary.result == BuildResult.Succeeded ? 0 : 1);
        }
    }
}
