using System;
using GateRush.Flight;
using UnityEngine;

namespace GateRush.Core
{
    [Serializable]
    public sealed class CameraPrefs
    {
        [Range(0f, 60f)] public float TiltDegrees = 25f;
        [Range(90f, 150f)] public float HorizontalFov = 120f;
        /// <summary>0 = FPV, 1 = Chase, 2 = Line of sight (GateRush.Cameras.ViewMode).</summary>
        public int ViewMode;
        /// <summary>Wide-angle FPV lens distortion (barrel), FPV view only.</summary>
        public bool LensDistortion;
    }

    [Serializable]
    public sealed class InputPrefs
    {
        /// <summary>GateRush.Input.ThrottleMapping for gamepads.</summary>
        public int GamepadThrottle = 1;
        [Range(0f, 0.3f)] public float Deadband = 0.02f;
        [Range(0f, 1f)] public float Expo;
        /// <summary>Flip pitch on the right stick (all controllers).</summary>
        public bool InvertPitch;
        /// <summary>Flip roll on the right stick (all controllers).</summary>
        public bool InvertRoll;
        /// <summary>GateRush.Input.RadioBackend: 0 = auto, 1 = Input System, 2 = Android raw reader, 3 = Windows HID.</summary>
        public int RadioBackend;
    }

    [Serializable]
    public sealed class FeedPrefs
    {
        /// <summary>Analog video noise on the goggles feed: 0 off, 1 light, 2 realistic.</summary>
        public int VtxNoise = 1;
        [Range(0f, 1f)] public float MotorVolume = 0.6f;
        /// <summary>Developer readout (sticks, rates, motors) instead of the clean OSD.</summary>
        public bool ShowDebugOverlay;
        /// <summary>Stadium time of day: 0 night (floodlights), 1 dusk, 2 day (GateRush.Core.TimeOfDay).</summary>
        public int StadiumLighting;
    }

    /// <summary>How much the game helps you find the route (T5).</summary>
    public enum RouteGuidanceLevel
    {
        /// <summary>Only the physical track: lane lights and chevrons.</summary>
        Off = 0,
        /// <summary>Next gate glows and pulses, the one after shows an outline, passed gates dim.</summary>
        Standard = 1,
        /// <summary>Standard + a racing line coloured by recommended speed and an off-screen arrow to the next gate.</summary>
        Trainer = 2,
    }

    [Serializable]
    public sealed class GuidancePrefs
    {
        /// <summary><see cref="RouteGuidanceLevel"/>.</summary>
        public int RouteGuidance = (int)RouteGuidanceLevel.Standard;
        /// <summary>Lane posts have no colliders (beginners fly through them).</summary>
        public bool GhostLanes;
    }

    /// <summary>Online features. Everything is off until the player opts in (the game never connects on its own).</summary>
    [Serializable]
    public sealed class OnlinePrefs
    {
        /// <summary>Post the best lap of each finished race on a built-in track to the online leaderboard.</summary>
        public bool PostLapTimes;
    }

    [Serializable]
    public sealed class PhysicsPrefs
    {
        /// <summary>Normal impact speed that counts as a crash (m/s).</summary>
        [Range(3f, 20f)] public float CrashSpeed = 7f;
        /// <summary>Multiplier on the quad's air drag.</summary>
        [Range(0.5f, 2f)] public float DragScale = 1f;
        /// <summary>Multiplier on motor spool time (higher = laggier motors).</summary>
        [Range(0.5f, 2f)] public float MotorLagScale = 1f;
        /// <summary>Battery voltage sag and drain affect thrust.</summary>
        public bool BatterySag = true;
    }

    /// <summary>Everything the player can change. Saved as JSON by <see cref="SaveSystem"/>.</summary>
    [Serializable]
    public sealed class GameSettings
    {
        public const int CurrentVersion = 1;

        public int Version = CurrentVersion;
        public string PilotName = "Pilot";
        /// <summary>Interface language, a code from <see cref="L.Codes"/>; Kazakh by default.</summary>
        public string Language = L.Default;
        /// <summary>Id in the quad catalog ("race5", "freestyle5", "whoop65").</summary>
        public string QuadId = "race5";
        /// <summary>Id in the track catalog.</summary>
        public string TrackId = "field";
        /// <summary>When false each quad flies its own default rates.</summary>
        public bool UseCustomRates;
        public RateProfile Rates = RateProfile.BetaflightDefault;
        public CameraPrefs Camera = new CameraPrefs();
        public InputPrefs Input = new InputPrefs();
        public PhysicsPrefs Physics = new PhysicsPrefs();
        public FeedPrefs Feed = new FeedPrefs();
        public GraphicsPrefs Graphics = new GraphicsPrefs();
        public GuidancePrefs Guidance = new GuidancePrefs();
        public OnlinePrefs Online = new OnlinePrefs();

        /// <summary>Brings every value into its valid range (after loading or editing).</summary>
        public void Clamp()
        {
            if (string.IsNullOrWhiteSpace(PilotName)) PilotName = "Pilot";
            if (Array.IndexOf(L.Codes, Language) < 0) Language = L.Default;
            if (PilotName.Length > 20) PilotName = PilotName.Substring(0, 20);
            if (string.IsNullOrEmpty(QuadId)) QuadId = "race5";
            if (string.IsNullOrEmpty(TrackId)) TrackId = "field";
            Camera ??= new CameraPrefs();
            Input ??= new InputPrefs();
            Physics ??= new PhysicsPrefs();
            Feed ??= new FeedPrefs();
            Graphics ??= new GraphicsPrefs();
            Guidance ??= new GuidancePrefs();
            Online ??= new OnlinePrefs();
            Guidance.RouteGuidance = Mathf.Clamp(Guidance.RouteGuidance, 0, 2);
            Graphics.Clamp();
            Feed.VtxNoise = Mathf.Clamp(Feed.VtxNoise, 0, 2);
            Feed.MotorVolume = Mathf.Clamp01(Feed.MotorVolume);
            Feed.StadiumLighting = Mathf.Clamp(Feed.StadiumLighting, 0, 2);

            Rates.Roll = ClampAxis(Rates.Type, Rates.Roll);
            Rates.Pitch = ClampAxis(Rates.Type, Rates.Pitch);
            Rates.Yaw = ClampAxis(Rates.Type, Rates.Yaw);

            Camera.TiltDegrees = Mathf.Clamp(Camera.TiltDegrees, 0f, 60f);
            Camera.HorizontalFov = Mathf.Clamp(Camera.HorizontalFov, 90f, 150f);
            Camera.ViewMode = Mathf.Clamp(Camera.ViewMode, 0, 2);

            Input.GamepadThrottle = Mathf.Clamp(Input.GamepadThrottle, 0, 2);
            Input.Deadband = Mathf.Clamp(Input.Deadband, 0f, 0.3f);
            Input.Expo = Mathf.Clamp01(Input.Expo);
            Input.RadioBackend = Mathf.Clamp(Input.RadioBackend, 0, 3);

            Physics.CrashSpeed = Mathf.Clamp(Physics.CrashSpeed, 3f, 20f);
            Physics.DragScale = Mathf.Clamp(Physics.DragScale, 0.5f, 2f);
            Physics.MotorLagScale = Mathf.Clamp(Physics.MotorLagScale, 0.5f, 2f);
        }

        private static AxisRates ClampAxis(RatesType type, AxisRates a)
        {
            if (type == RatesType.Actual)
            {
                a.RcRate = Mathf.Clamp(a.RcRate, 10f, 400f);   // centre sensitivity °/s
                a.Rate = Mathf.Clamp(a.Rate, a.RcRate, 1998f); // max rate °/s
            }
            else
            {
                a.RcRate = Mathf.Clamp(a.RcRate, 0.01f, 2.55f);
                a.Rate = Mathf.Clamp(a.Rate, 0f, 0.99f);
            }
            a.Expo = Mathf.Clamp01(a.Expo);
            return a;
        }

        public GameSettings Clone() => FromJson(ToJson());

        public string ToJson() => JsonUtility.ToJson(this, true);

        /// <summary>Parses JSON over the defaults, so fields missing from older files keep their default values.</summary>
        public static GameSettings FromJson(string json)
        {
            var s = new GameSettings();
            if (!string.IsNullOrEmpty(json)) JsonUtility.FromJsonOverwrite(json, s);
            s.Version = CurrentVersion;
            s.Clamp();
            return s;
        }
    }
}
