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
        /// <summary>Tracks made in the editor on this computer.</summary>
        public static string UserTracksFolder => Path.Combine(Application.persistentDataPath, "BenchTracks");

        /// <summary>The track file open in the track editor (set by <see cref="Edit"/>, or <c>-bench-edit</c>); null otherwise.</summary>
        public static string EditPath { get => _edit ?? (_editCleared ? null : ResolveTrack(Arg("-bench-edit"))); private set => _edit = value; }
        private static string _edit;

        public struct TrackEntry
        {
            public string Name, Path, Venue;
            public bool Public;
            public int Gates;
        }

        public static string[] PublicTrackNames()
        {
            if (!Directory.Exists(TracksFolder)) return new string[0];
            string[] files = Directory.GetFiles(TracksFolder, "*" + TrackExtension);
            for (int i = 0; i < files.Length; i++) files[i] = Path.GetFileName(files[i]).Replace(TrackExtension, "");
            Array.Sort(files, StringComparer.Ordinal);
            return files;
        }

        /// <summary>Public tracks first, then the user's own.</summary>
        public static System.Collections.Generic.List<TrackEntry> ListTracks()
        {
            var list = new System.Collections.Generic.List<TrackEntry>();
            foreach (string folder in new[] { TracksFolder, UserTracksFolder })
            {
                if (!Directory.Exists(folder)) continue;
                string[] files = Directory.GetFiles(folder, "*" + TrackExtension);
                Array.Sort(files, StringComparer.Ordinal);
                foreach (string file in files)
                {
                    try
                    {
                        BenchTrack t = BenchTrack.Parse(File.ReadAllText(file));
                        list.Add(new TrackEntry { Name = t.id, Path = file, Venue = t.venue, Public = folder == TracksFolder, Gates = t.gates.Count });
                    }
                    catch (Exception) { /* a broken file is not listed */ }
                }
            }
            return list;
        }

        /// <summary>A track given as a file path, or by name: a public track, or one of the user's own.</summary>
        public static string ResolveTrack(string nameOrPath)
        {
            if (string.IsNullOrEmpty(nameOrPath)) return null;
            if (File.Exists(nameOrPath)) return nameOrPath;
            foreach (string folder in new[] { TracksFolder, UserTracksFolder })
            {
                string p = Path.Combine(folder, nameOrPath + TrackExtension);
                if (File.Exists(p)) return p;
            }
            return null;
        }

        /// <summary>Opens the track editor on a track file (a public track is edited as a copy).</summary>
        public static void Edit(string nameOrPath)
        {
            string path = ResolveTrack(nameOrPath);
            if (path == null) throw new FileNotFoundException($"track not found: {nameOrPath}");
            EditPath = path;
            ReplayFolder = null;
            SceneManager.LoadScene(SceneFor(BenchTrack.Parse(File.ReadAllText(path))));
        }

        /// <summary>A new generated track in the user's folder, opened in the editor.</summary>
        public static void EditNew(string venue)
        {
            long seed = DateTime.UtcNow.Ticks % 900 + 1;
            BenchTrack t = TrackGenerator.Generate(venue, seed, 1);
            t.id = $"MY_{venue}_{seed}";
            Directory.CreateDirectory(UserTracksFolder);
            string path = Path.Combine(UserTracksFolder, t.id + TrackExtension);
            File.WriteAllText(path, t.ToJson() + "\n");
            Edit(path);
        }
        public static string DefaultRulesPath => Path.Combine(Application.streamingAssetsPath, "Bench", "rules.json");

        public static bool LoopbackOnly => !HasFlag("-bench-listen-all");

        /// <summary><c>-bench-team</c>: the team this attempt belongs to (results, folder name).</summary>
        public static string Team => Arg("-bench-team") ?? "local";
        /// <summary><c>-bench-attempt</c>: the attempt's number on this track.</summary>
        public static int Attempt => int.TryParse(Arg("-bench-attempt"), out int n) && n > 0 ? n : 1;
        /// <summary><c>-bench-solution</c>: digest of the team's container image, written into the result.</summary>
        public static string SolutionDigest => Arg("-bench-solution") ?? "";
        /// <summary><c>-bench-exit</c>: quit when the attempt is over and its files are written (server runs).</summary>
        public static bool ExitWhenDone => HasFlag("-bench-exit");
        /// <summary><c>-bench-out</c> was given: record attempts in training mode too.</summary>
        public static bool OutputGiven => Arg("-bench-out") != null;
        /// <summary><c>-bench-replay &lt;attempt folder&gt;</c>, or set from the leaderboard: open the judge replay instead of a run.</summary>
        public static string ReplayFolder { get => _replay ?? Arg("-bench-replay"); set => _replay = value; }
        private static string _replay;

        /// <summary>Where attempt folders go: <c>-bench-out</c>, or "BenchResults" in the user's data folder.</summary>
        public static string OutputFolder => Arg("-bench-out") ?? Path.Combine(Application.persistentDataPath, "BenchResults");

        /// <summary>
        /// The organisers' signing key: the bytes of the file named by <c>-bench-key</c> or USHQYR_BENCH_KEY_FILE.
        /// It lives outside the project and outside every build; without it attempts carry checksums but no signature.
        /// </summary>
        public static byte[] SigningKey()
        {
            string path = Arg("-bench-key") ?? Environment.GetEnvironmentVariable("USHQYR_BENCH_KEY_FILE");
            if (string.IsNullOrEmpty(path)) return null;
            if (!File.Exists(path)) throw new FileNotFoundException($"signing key file not found: {path}");
            byte[] key = File.ReadAllBytes(path);
            if (key.Length < 16) throw new FormatException("the signing key must be at least 16 bytes");
            return key;
        }

        /// <summary>ffmpeg for the videos: <c>-bench-ffmpeg</c>, USHQYR_FFMPEG, next to the tracks, or on the PATH. Null if none.</summary>
        public static string FindFfmpeg()
        {
            string exe = Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor ? "ffmpeg.exe" : "ffmpeg";
            foreach (string candidate in new[] { Arg("-bench-ffmpeg"), Environment.GetEnvironmentVariable("USHQYR_FFMPEG"),
                         Path.Combine(Application.streamingAssetsPath, "Bench", exe) })
                if (!string.IsNullOrEmpty(candidate) && File.Exists(candidate)) return candidate;
            foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                try
                {
                    string p = Path.Combine(dir.Trim(), exe);
                    if (File.Exists(p)) return p;
                }
                catch (ArgumentException) { }
            }
            return null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void FromCommandLine()
        {
            // -bench-generate <venue> <seed> <difficulty> <file>: write a random track and quit (for making hidden tracks).
            string[] all = Args();
            int gen = Array.FindIndex(all, a => string.Equals(a, "-bench-generate", StringComparison.OrdinalIgnoreCase));
            if (!Application.isEditor && gen >= 0)
            {
                int code = 1;
                try
                {
                    BenchTrack t = TrackGenerator.Generate(all[gen + 1], long.Parse(all[gen + 2]), int.Parse(all[gen + 3]));
                    File.WriteAllText(all[gen + 4], t.ToJson() + "\n");
                    Debug.Log($"[Bench] generated {t.id} ({t.gates.Count} gates) -> {all[gen + 4]}");
                    code = 0;
                }
                catch (Exception e) { Debug.LogError("[Bench] -bench-generate <venue> <seed> <difficulty> <file>: " + e.Message); }
                Application.Quit(code);
                return;
            }
            if (Application.isEditor || !(HasFlag("-bench") || Arg("-bench-replay") != null || Arg("-bench-edit") != null)) return;
            try
            {
                AppBootstrap.FirstSceneOverride = Arg("-bench-replay") != null ? SceneFor(LoadReplayTrack())
                    : Arg("-bench-edit") != null ? SceneFor(BenchTrack.Parse(File.ReadAllText(EditPath))) : SceneFor(LoadTrack("H1_Oval"));
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
            ReplayFolder = null;
            EditPath = null;
            _editCleared = true;
            SceneManager.LoadScene(SceneFor(LoadTrack(track)));
        }

        private static bool _editCleared;

        public static string SceneFor(BenchTrack track) => track.venue == "parking" ? "Bench_Parking" : "Bench_Hall";

        /// <summary>The track an attempt folder was flown on (its own copy of the file).</summary>
        public static BenchTrack LoadReplayTrack()
        {
            string path = Path.Combine(ReplayFolder, "track.benchtrack.json");
            if (!File.Exists(path)) throw new FileNotFoundException($"not an attempt folder (no track file): {ReplayFolder}");
            return BenchTrack.Parse(File.ReadAllText(path));
        }

        /// <summary>Opens the judge replay of an attempt folder.</summary>
        public static void Replay(string folder)
        {
            ReplayFolder = folder;
            SceneManager.LoadScene(SceneFor(LoadReplayTrack()));
        }

        public static BenchTrack LoadTrack(string fallback)
        {
            string chosen = ChosenTrack ?? Arg("-bench-track") ?? fallback;
            string path = ResolveTrack(chosen);
            if (path == null) throw new FileNotFoundException($"track not found: {chosen}");
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

        private static string[] _args;

        /// <summary>
        /// The command line. In the editor, where there is none, the options are read from
        /// UserSettings/bench-args.txt (one option or value per line), so every mode can be tried in Play mode.
        /// </summary>
        private static string[] Args()
        {
            if (_args != null && !Application.isEditor) return _args;
            _args = Environment.GetCommandLineArgs();
            if (Application.isEditor)
            {
                string file = Path.Combine(Application.dataPath, "..", "UserSettings", "bench-args.txt");
                _args = File.Exists(file) ? Array.FindAll(File.ReadAllLines(file), l => l.Trim().Length > 0) : new string[0];
                for (int i = 0; i < _args.Length; i++) _args[i] = _args[i].Trim();
            }
            return _args;
        }

        private static bool HasFlag(string name) =>
            Array.Exists(Args(), a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));

        private static string Arg(string name)
        {
            string[] args = Args();
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return null;
        }
    }
}
