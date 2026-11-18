using System.Collections;
using System.IO;
using Ushqyr.Bench;
using UnityEngine;

namespace Ushqyr.EditorTools.Bench
{
    /// <summary>
    /// Records the Game view of a bench run as numbered JPEG frames at a fixed 30 frames per second of simulator time
    /// (Recordings/bench_demo), for the demo video. Start Play mode in the bench scene, call <see cref="Run"/>, then start
    /// the program; recording begins with the attempt and stops after <c>seconds</c> or when the attempt ends.
    /// </summary>
    public static class BenchDemoRecorder
    {
        public const int Fps = 30;
        public static string Root => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Recordings", "bench_demo"));

        public static string Run(float seconds)
        {
            if (!Application.isPlaying) return "enter Play mode first";
            var session = Object.FindAnyObjectByType<BenchSession>();
            if (session == null) return "no bench session in this scene";
            var go = new GameObject("Bench Demo Recorder");
            var r = go.AddComponent<BenchDemoDirector>();
            r.Session = session;
            r.Seconds = seconds;
            return "waiting for the attempt to start";
        }

        public static string Status() => File.Exists(Path.Combine(Root, "done.txt")) ? File.ReadAllText(Path.Combine(Root, "done.txt")) : "running";
    }

    public sealed class BenchDemoDirector : MonoBehaviour
    {
        public BenchSession Session;
        public float Seconds = 40f;

        private IEnumerator Start()
        {
            string dir = BenchDemoRecorder.Root;
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            var spectator = FindAnyObjectByType<BenchSpectatorCamera>();
            if (spectator != null) spectator.Overview = false;

            while (Session.State != AttemptState.Running) yield return null;

            Time.captureFramerate = BenchDemoRecorder.Fps;
            var eof = new WaitForEndOfFrame();
            int frame = 0, total = Mathf.RoundToInt(Seconds * BenchDemoRecorder.Fps);
            while (frame < total && Session.State == AttemptState.Running)
            {
                // One stretch from the fixed overview in the middle, the rest behind the drone.
                if (spectator != null) spectator.Overview = frame > total * 0.45f && frame < total * 0.7f;
                yield return eof;
                Texture2D tex = ScreenCapture.CaptureScreenshotAsTexture();
                File.WriteAllBytes(Path.Combine(dir, $"f{frame++:0000}.jpg"), tex.EncodeToJPG(92));
                Destroy(tex);
            }
            Time.captureFramerate = 0;
            if (spectator != null) spectator.Overview = false;
            File.WriteAllText(Path.Combine(dir, "done.txt"), $"done {frame} frames, points {Session.Points}");
            Destroy(gameObject);
        }
    }
}
