using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using GateRush.Core;
using GateRush.Race;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GateRush.Game
{
    /// <summary>
    /// Command-line benchmark for the Windows player (inactive without <c>-benchmark</c>):
    /// <c>GateRush.exe -benchmark [-benchScenes Track_Field,Menu] [-benchPresets Medium,High] [-benchSeconds 20] [-benchOut file.csv]</c>.
    /// For every preset and scene it loads the scene, flies the drone camera along the gate line at a fixed speed
    /// (so every run sees the same frames), and logs average fps, 1 % low, worst frame and CPU/GPU frame times
    /// (FrameTimingManager), then quits.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    public sealed class BenchmarkRunner : MonoBehaviour
    {
        public const string Flag = "-benchmark";
        private const float Speed = 14f, LookAhead = 10f, Warmup = 3f;

        private string[] _scenes = { "Track_Field", "Track_Hangar", "Track_Industrial", "Menu" };
        private string[] _presets = Array.Empty<string>();
        private float _seconds = 20f;
        private string _out;

        private Transform _camera;
        private readonly List<Vector3> _path = new List<Vector3>();
        private float _pathLength, _clock;
        private bool _flying;

        public static bool Requested(string[] args) =>
            args != null && Array.Exists(args, a => string.Equals(a, Flag, StringComparison.OrdinalIgnoreCase));

        public static string Arg(string[] args, string name, string fallback)
        {
            int i = Array.FindIndex(args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init()
        {
            var args = Environment.GetCommandLineArgs();
            if (!Requested(args)) return;
            var go = new GameObject("Benchmark");
            DontDestroyOnLoad(go);
            go.AddComponent<BenchmarkRunner>().Configure(args);
        }

        private void Configure(string[] args)
        {
            string scenes = Arg(args, "-benchScenes", null);
            if (!string.IsNullOrEmpty(scenes)) _scenes = scenes.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            string presets = Arg(args, "-benchPresets", null);
            if (!string.IsNullOrEmpty(presets)) _presets = presets.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            _seconds = float.TryParse(Arg(args, "-benchSeconds", "20"), NumberStyles.Float, CultureInfo.InvariantCulture, out var s) ? s : 20f;
            _out = Arg(args, "-benchOut", Path.Combine(Application.persistentDataPath, "benchmark.csv"));
        }

        private IEnumerator Start()
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            SaveSystem.Settings.Camera.ViewMode = 0; // drone camera on the monitor (not committed to disk)
            yield return new WaitForSecondsRealtime(2f); // let Boot hand over to the menu first

            var presets = _presets.Length > 0 ? _presets : new[] { QualitySettings.names[QualitySettings.GetQualityLevel()] };
            foreach (var preset in presets)
            {
                int level = Array.FindIndex(QualitySettings.names, n => string.Equals(n, preset, StringComparison.OrdinalIgnoreCase));
                if (level >= 0) QualitySettings.SetQualityLevel(level, true);
                else Debug.LogWarning($"[GateRush][Bench] unknown quality level '{preset}', using current");
                foreach (var scene in _scenes) yield return Measure(scene.Trim(), preset);
            }
            Debug.Log("[GateRush][Bench] finished, results in " + _out);
            Application.Quit();
        }

        private IEnumerator Measure(string scene, string preset)
        {
            var load = SceneManager.LoadSceneAsync(scene, LoadSceneMode.Single);
            if (load == null)
            {
                Debug.LogError($"[GateRush][Bench] scene '{scene}' is not in the build");
                yield break;
            }
            while (!load.isDone) yield return null;
            yield return new WaitForSecondsRealtime(Warmup);

            BuildPath();
            _clock = 0f;
            _flying = _camera != null && _path.Count > 1;

            var frames = new List<float>(4096);
            double cpu = 0, gpu = 0;
            int timed = 0;
            var timings = new FrameTiming[1];
            float elapsed = 0f;
            while (elapsed < _seconds)
            {
                yield return null;
                float dt = Time.unscaledDeltaTime;
                elapsed += dt;
                frames.Add(dt);
                FrameTimingManager.CaptureFrameTimings();
                if (FrameTimingManager.GetLatestTimings(1, timings) > 0)
                {
                    cpu += timings[0].cpuFrameTime;
                    gpu += timings[0].gpuFrameTime;
                    timed++;
                }
            }
            _flying = false;
            Report(scene, preset, frames, timed > 0 ? cpu / timed : 0, timed > 0 ? gpu / timed : 0);
        }

        private void BuildPath()
        {
            _path.Clear();
            _pathLength = 0f;
            var cam = GameObject.Find("FPV Camera");
            _camera = cam != null ? cam.transform : Camera.main != null ? Camera.main.transform : null;

            var gates = new List<Gate>(FindObjectsByType<Gate>());
            if (gates.Count == 0) return; // menu: static camera
            gates.Sort((a, b) => GateIndex(a.name).CompareTo(GateIndex(b.name)));
            var spawn = GameObject.Find("Spawn");
            if (spawn != null) _path.Add(spawn.transform.position + Vector3.up * 1.5f);
            foreach (var g in gates) _path.Add(g.OpeningCenterWorld);
            _path.Add(gates[0].OpeningCenterWorld);
            for (int i = 1; i < _path.Count; i++) _pathLength += Vector3.Distance(_path[i - 1], _path[i]);
        }

        private static int GateIndex(string name)
        {
            // "Gate {i} ({Type})"
            var parts = name.Split(' ');
            return parts.Length > 1 && int.TryParse(parts[1], out int i) ? i : int.MaxValue;
        }

        private void LateUpdate()
        {
            if (!_flying || _camera == null) return;
            _clock += Time.unscaledDeltaTime;
            float s = _clock * Speed % _pathLength;
            Vector3 pos = PointAt(s);
            Vector3 ahead = PointAt((s + LookAhead) % _pathLength);
            Vector3 dir = ahead - pos;
            _camera.SetPositionAndRotation(pos, dir.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(dir, Vector3.up) : _camera.rotation);
        }

        private Vector3 PointAt(float s)
        {
            for (int i = 1; i < _path.Count; i++)
            {
                float seg = Vector3.Distance(_path[i - 1], _path[i]);
                if (s <= seg) return Vector3.Lerp(_path[i - 1], _path[i], seg > 0f ? s / seg : 0f);
                s -= seg;
            }
            return _path[_path.Count - 1];
        }

        private void Report(string scene, string preset, List<float> frames, double cpuMs, double gpuMs)
        {
            if (frames.Count == 0) return;
            float total = 0f, worst = 0f;
            foreach (var f in frames)
            {
                total += f;
                worst = Mathf.Max(worst, f);
            }
            var sorted = new List<float>(frames);
            sorted.Sort();
            float p99 = sorted[Mathf.Clamp(Mathf.CeilToInt(sorted.Count * 0.99f) - 1, 0, sorted.Count - 1)];
            float avgFps = frames.Count / total;
            var inv = CultureInfo.InvariantCulture;
            string line = string.Format(inv, "{0:yyyy-MM-dd HH:mm},{1},{2}x{3},{4},{5},{6:F1},{7:F1},{8:F2},{9:F2},{10:F2},{11:F2}",
                DateTime.Now, SystemInfo.graphicsDeviceName.Replace(',', ' '), Screen.width, Screen.height, preset, scene,
                avgFps, 1f / p99, total / frames.Count * 1000f, worst * 1000f, cpuMs, gpuMs);
            Debug.Log($"[GateRush][Bench] {scene} @ {preset}: {avgFps:F1} fps avg, {1f / p99:F1} fps 1% low, worst {worst * 1000f:F1} ms, cpu {cpuMs:F2} ms, gpu {gpuMs:F2} ms");
            try
            {
                bool header = !File.Exists(_out);
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_out)));
                var sb = new StringBuilder();
                if (header) sb.AppendLine("date,gpu,resolution,preset,scene,avg_fps,low1_fps,avg_ms,worst_ms,cpu_ms,gpu_ms");
                sb.AppendLine(line);
                File.AppendAllText(_out, sb.ToString());
            }
            catch (Exception e)
            {
                Debug.LogWarning("[GateRush][Bench] could not write " + _out + ": " + e.Message);
            }
        }
    }
}
