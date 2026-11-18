using System;
using System.IO;
using Ushqyr.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ushqyr.Bench
{
    /// <summary>
    /// Starts AI Bench: from the command line (<c>-bench</c>) or from the menu. Options:
    /// <c>-bench-track &lt;name or file&gt;</c>, <c>-bench-rules &lt;file&gt;</c>, <c>-bench-port &lt;n&gt;</c>,
    /// <c>-bench-token &lt;text&gt;</c>, <c>-bench-seed &lt;n&gt;</c>, <c>-bench-mode training|competition</c>,
    /// <c>-bench-listen-all</c> (accept programs from other machines or containers; default is this computer only).
    /// Public tracks live in <c>StreamingAssets/Bench/Tracks</c>; any other track is given as a file path, which is how
    /// hidden tracks are used without ever being part of a build.
    /// </summary>
    public static class BenchLauncher
    {
        public const string TrackExtension = ".benchtrack.json";

        /// <summary>Track chosen from the menu (a name or a path); the command line wins over it.</summary>
        public static string ChosenTrack;

        public static string TracksFolder => Path.Combine(Application.streamingAssetsPath, "Bench", "Tracks");
        public static string DefaultRulesPath => Path.Combine(Application.streamingAssetsPath, "Bench", "rules.json");

        public static bool LoopbackOnly => !HasFlag("-bench-listen-all");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void FromCommandLine()
        {
            if (Application.isEditor || !HasFlag("-bench")) return;
            try
            {
                AppBootstrap.FirstSceneOverride = SceneFor(LoadTrack("H1_Oval"));
            }
            catch (Exception e)
            {
                Debug.LogError("[Bench] " + e.Message);
            }
        }

        /// <summary>Opens the bench with a public track (menu entry).</summary>
        public static void Launch(string track)
        {
            ChosenTrack = track;
            SceneManager.LoadScene(SceneFor(LoadTrack(track)));
        }

        public static string SceneFor(BenchTrack track) => track.venue == "parking" ? "Bench_Parking" : "Bench_Hall";

        public static BenchTrack LoadTrack(string fallback)
        {
            string chosen = Arg("-bench-track") ?? ChosenTrack ?? fallback;
            string path = File.Exists(chosen) ? chosen : Path.Combine(TracksFolder, chosen + TrackExtension);
            if (!File.Exists(path)) throw new FileNotFoundException($"track file not found: {path}");
            return BenchTrack.Parse(File.ReadAllText(path));
        }

        public static BenchRules LoadRules()
        {
            string path = Arg("-bench-rules") ?? DefaultRulesPath;
            if (Arg("-bench-rules") != null && !File.Exists(path)) throw new FileNotFoundException($"rules file not found: {path}");
            BenchRules rules = BenchRules.Parse(File.Exists(path) ? File.ReadAllText(path) : null);
            if (int.TryParse(Arg("-bench-port"), out int port)) rules.port = port;
            if (long.TryParse(Arg("-bench-seed"), out long seed)) rules.seed = seed;
            if (Arg("-bench-token") != null) rules.token = Arg("-bench-token");
            if (Arg("-bench-mode") != null) rules.mode = Arg("-bench-mode");
            string envToken = Environment.GetEnvironmentVariable("USHQYR_BENCH_TOKEN");
            if (string.IsNullOrEmpty(rules.token) && !string.IsNullOrEmpty(envToken)) rules.token = envToken;
            return rules;
        }

        private static bool HasFlag(string name) =>
            Array.Exists(Environment.GetCommandLineArgs(), a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));

        private static string Arg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return null;
        }
    }
}
