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

        /// <summary>Entry point for -executeMethod GateRush.EditorTools.BuildScript.BuildQuest.</summary>
        [MenuItem("GateRush/Build/Build Quest APK")]
        public static void BuildQuest()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ApkPath));
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = ApkPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.None,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            Debug.Log($"[GateRush] Build {summary.result}: {summary.outputPath} " +
                      $"({summary.totalSize / (1024 * 1024)} MB, {summary.totalErrors} errors, {summary.totalWarnings} warnings)");

            if (Application.isBatchMode)
                EditorApplication.Exit(summary.result == BuildResult.Succeeded ? 0 : 1);
        }
    }
}
