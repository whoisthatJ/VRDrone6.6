using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Ushqyr.Bench
{
    /// <summary>LED strip of one gate, as stored in the track file.</summary>
    [Serializable]
    public sealed class GateLed
    {
        /// <summary>Colour as "#RRGGBB".</summary>
        public string color = "#FF00C8";
        /// <summary>0..1.</summary>
        public float brightness = 1f;
        /// <summary>"solid" or "blink".</summary>
        public string mode = "solid";
    }

    [Serializable]
    public sealed class TrackGate
    {
        public string id;
        /// <summary>Where the gate stands: the middle of its base on the floor (x, y, z in metres, y up).</summary>
        public float[] position = new float[3];
        /// <summary>Heading of the gate's front-to-back direction in degrees (0 = +z, 90 = +x).</summary>
        public float yaw;
        public GateLed led = new GateLed();
        /// <summary>"any" or "front".</summary>
        public string direction = "any";
    }

    /// <summary>
    /// Something standing on the track that is not a gate: a coloured panel or box (a distractor for colour detectors,
    /// or simply an obstacle). Solid.
    /// </summary>
    [Serializable]
    public sealed class TrackProp
    {
        public string id;
        /// <summary>Middle of the base on the floor.</summary>
        public float[] position = new float[3];
        public float yaw;
        /// <summary>Width, height, depth (m).</summary>
        public float[] size = { 1f, 1f, 0.1f };
        public string color = "#FF00C8";
        /// <summary>True: it shines like an LED strip. False: painted.</summary>
        public bool glow;
    }

    [Serializable]
    public sealed class TrackStart
    {
        public float[] position = new float[3];
        public float yaw;
    }

    [Serializable]
    public sealed class TrackRules
    {
        /// <summary>A gate scores again only after this many other gates have scored.</summary>
        public int repeat_window = 4;
        public float time_limit = 120f;
    }

    /// <summary>
    /// A track file (*.benchtrack.json). Plain data: the venue, the gates with their own LED settings, the start pad and
    /// the rule overrides. Its hash goes into every result.
    /// </summary>
    [Serializable]
    public sealed class BenchTrack
    {
        public const int CurrentSchema = 1;

        public int schema = CurrentSchema;
        public string id;
        public string name;
        public string author;
        public string difficulty;
        public string venue = "hall";
        public string lighting = "default";
        public TrackStart start = new TrackStart();
        public List<TrackGate> gates = new List<TrackGate>();
        public List<TrackProp> props = new List<TrackProp>();
        public TrackRules rules = new TrackRules();

        /// <summary>SHA-256 of the file's bytes, set by <see cref="Parse"/>.</summary>
        [NonSerialized] public string Hash;
        /// <summary>The file's text as read, set by <see cref="Parse"/> (copied into every attempt folder).</summary>
        [NonSerialized] public string Json;

        public static BenchTrack Parse(string json)
        {
            var t = JsonUtility.FromJson<BenchTrack>(json);
            if (t == null) throw new FormatException("the track file is empty");
            if (t.schema != CurrentSchema) throw new FormatException($"track schema {t.schema} is not supported (expected {CurrentSchema})");
            if (t.gates == null || t.gates.Count == 0) throw new FormatException("the track has no gates");
            var ids = new HashSet<string>();
            foreach (var g in t.gates)
            {
                if (string.IsNullOrEmpty(g.id) || !ids.Add(g.id)) throw new FormatException("every gate needs its own id");
                if (g.position == null || g.position.Length != 3) throw new FormatException($"gate {g.id}: position needs 3 numbers");
            }
            if (t.start?.position == null || t.start.position.Length != 3) throw new FormatException("start: position needs 3 numbers");
            using (var sha = SHA256.Create())
            {
                var sb = new StringBuilder(64);
                foreach (byte b in sha.ComputeHash(Encoding.UTF8.GetBytes(json))) sb.Append(b.ToString("x2"));
                t.Hash = sb.ToString();
            }
            t.Json = json;
            return t;
        }

        public string ToJson() => JsonUtility.ToJson(this, true);

        public static Vector3 V(float[] p) => new Vector3(p[0], p[1], p[2]);

        public static Color LedColor(GateLed led)
        {
            if (led != null && ColorUtility.TryParseHtmlString(led.color, out var c)) return c;
            return new Color(1f, 0f, 0.78f);
        }
    }

    /// <summary>
    /// Everything the organisers may want to change without a rebuild (plan §2: the values marked as open questions).
    /// Read from a JSON file; missing fields keep these defaults.
    /// </summary>
    [Serializable]
    public sealed class BenchRules
    {
        /// <summary>"training" or "competition".</summary>
        public string mode = "training";
        /// <summary>Steps per second in sync mode: one RC packet per step.</summary>
        public float control_hz = 100f;
        public float rc_latency_ms = 10f;
        public float rc_failsafe_ms = 500f;
        /// <summary>Extra link delay that changes from packet to packet: uniform between 0 and this.</summary>
        public float rc_jitter_ms = 0f;
        /// <summary>Share of RC packets that never arrive, in percent.</summary>
        public float rc_loss_percent = 0f;
        /// <summary>
        /// A fixed delay that stands for the program's own processing time, added to the link delay. 0 in training;
        /// the organisers set it for the competition so that slow and fast computers fly the same.
        /// </summary>
        public float compute_delay_ms = 0f;
        /// <summary>"sync": the simulator advances one step per command. "realtime": it runs with the wall clock.</summary>
        public string time_mode = "sync";
        public float lidar_hz = 10f;
        /// <summary>
        /// How much each attempt varies the scene from its seed: "none", "mild" or "strong" (gate and start poses,
        /// LED brightness, lights, video noise; "strong" also changes surfaces and adds clutter). The same seed gives
        /// every team the same variation.
        /// </summary>
        public string randomization = "none";
        public bool lidar = true;
        public int camera_width = 640;
        public int camera_height = 480;
        public float camera_fps = 30f;
        /// <summary>Horizontal field of view of the FPV camera (degrees).</summary>
        public float camera_hfov = 120f;
        /// <summary>Upward tilt of the FPV camera (degrees).</summary>
        public float camera_tilt = 20f;
        /// <summary>Delay from the moment a frame is taken to the moment the program gets it.</summary>
        public float camera_latency_ms = 30f;
        public bool camera_analog = true;
        /// <summary>Barrel distortion of the lens (radial coefficient k1; 0 = a plain perspective picture).</summary>
        public float camera_distortion = 0.18f;
        /// <summary>The camera adjusts its exposure to the brightness of the scene, as a real one does.</summary>
        public bool camera_auto_exposure = true;
        /// <summary>Distance at which the analog video signal is at its worst (m).</summary>
        public float video_range = 80f;
        public float imu_hz = 500f;
        public float tof_hz = 50f;
        public float battery_hz = 10f;
        /// <summary>Per-attempt time limit (s); a track file may override it.</summary>
        public float time_limit = 120f;
        public int repeat_window = 4;
        /// <summary>The attempt ends when the drone has not moved for this long (s); 0 switches the check off.</summary>
        public float stuck_time = 10f;
        /// <summary>Seed of the sensor noise.</summary>
        public long seed = 1;
        /// <summary>Sync mode only: don't run faster than real time, so the flight looks normal on screen.</summary>
        public bool pace_realtime = true;
        /// <summary>Attempt folders with logs: "on", "off" or "auto" (always in competition; in training when an output folder is given).</summary>
        public string record = "auto";
        /// <summary>Onboard, trackside and judge video: "on", "off" or "auto" (competition only). Needs ffmpeg.</summary>
        public string video = "auto";
        public int trackside_width = 1280;
        public int trackside_height = 720;
        /// <summary>Competition: the program must connect and start within this many seconds of real time.</summary>
        public float connect_timeout = 60f;
        /// <summary>Competition: the longest the program may take to answer one step, in seconds of real time.</summary>
        public float step_timeout = 10f;
        public int port = 5757;
        /// <summary>The program must present this token; empty = no check.</summary>
        public string token = "";

        public static BenchRules Parse(string json)
        {
            var r = new BenchRules();
            if (!string.IsNullOrWhiteSpace(json)) JsonUtility.FromJsonOverwrite(json, r);
            r.control_hz = Mathf.Clamp(r.control_hz, 10f, 500f);
            r.camera_width = Mathf.Clamp(r.camera_width, 64, 1920);
            r.camera_height = Mathf.Clamp(r.camera_height, 48, 1080);
            r.camera_fps = Mathf.Clamp(r.camera_fps, 1f, 120f);
            return r;
        }

        public bool Competition => string.Equals(mode, "competition", StringComparison.OrdinalIgnoreCase);
        public bool Realtime => string.Equals(time_mode, "realtime", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Hash of everything that shapes an attempt. The token and the port are left out: they are not rules, and the
        /// token must never reach a log.
        /// </summary>
        public string Hash()
        {
            var copy = JsonUtility.FromJson<BenchRules>(JsonUtility.ToJson(this));
            copy.token = "";
            copy.port = 0;
            return BenchHash.Sha256(JsonUtility.ToJson(copy));
        }
    }

    /// <summary>Message numbers and limits of protocol version 1 (Docs/bench/en/protocol.md).</summary>
    public static class BenchProtocol
    {
        public const int Version = 1;

        // Program -> simulator.
        public const byte Hello = 1;
        public const byte Start = 2;
        public const byte Step = 3;
        public const byte Reset = 4;
        public const byte Done = 5;
        public const byte Ping = 6;

        // Simulator -> program.
        public const byte Welcome = 129;
        public const byte Observation = 130;
        public const byte Pong = 131;
        public const byte Error = 132;

        /// <summary>Largest frame the simulator accepts from a program (bytes after the length field).</summary>
        public const int MaxInboundFrame = 16 * 1024;
        /// <summary>Largest ping payload a program may ask for.</summary>
        public const int MaxPingPayload = 4 * 1024 * 1024;

        public static bool AllowedInCompetition(byte type) => type == Hello || type == Start || type == Step || type == Done;
    }
}
