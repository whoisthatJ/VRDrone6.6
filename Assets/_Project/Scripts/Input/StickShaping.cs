using System;

namespace GateRush.Input
{
    public enum ThrottleMapping
    {
        /// <summary>Full stick range -1..1 maps to 0..1 (non-centering throttle, like an RC radio).</summary>
        Direct = 0,
        /// <summary>Spring-centred stick: centre = hover throttle, down = 0, up = full.</summary>
        SpringCenterHover = 1,
        /// <summary>A 0..1 trigger is the throttle.</summary>
        Trigger = 2,
    }

    /// <summary>Deadband and expo applied to raw stick axes before they reach the simulator.</summary>
    [Serializable]
    public struct StickShaping
    {
        /// <summary>0..0.5, fraction of travel around centre treated as zero.</summary>
        public float Deadband;
        /// <summary>0..1 stick expo (cubic blend), on top of the rate curve's own expo.</summary>
        public float Expo;
        /// <summary>Flip the right stick's pitch (forward = nose up) for this source.</summary>
        public bool InvertPitch;
        /// <summary>Flip the right stick's roll for this source.</summary>
        public bool InvertRoll;

        public static StickShaping Default => new StickShaping { Deadband = 0.02f, Expo = 0f };

        /// <summary>Applies deadband (rescaled so the output still reaches ±1) and expo to a -1..1 axis.</summary>
        public float Apply(float v)
        {
            v = Clamp(v, -1f, 1f);
            float a = MathF.Abs(v);
            if (a <= Deadband) return 0f;
            float scaled = (a - Deadband) / (1f - Deadband);
            scaled = scaled * scaled * scaled * Expo + scaled * (1f - Expo);
            return MathF.Sign(v) * scaled;
        }

        /// <summary>Maps a raw throttle axis to 0..1 according to <paramref name="mapping"/>.</summary>
        public static float MapThrottle(ThrottleMapping mapping, float raw, float hoverThrottle)
        {
            switch (mapping)
            {
                case ThrottleMapping.SpringCenterHover:
                    raw = Clamp(raw, -1f, 1f);
                    return raw < 0f ? hoverThrottle * (1f + raw) : hoverThrottle + (1f - hoverThrottle) * raw;
                case ThrottleMapping.Trigger:
                    return Clamp(raw, 0f, 1f);
                default:
                    return Clamp((raw + 1f) * 0.5f, 0f, 1f);
            }
        }

        internal static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);
    }
}
