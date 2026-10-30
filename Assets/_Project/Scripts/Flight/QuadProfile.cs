using System;

namespace GateRush.Flight
{
    /// <summary>Rate-PID gains for one axis. Output is in normalised mixer units per rad/s of error.</summary>
    [Serializable]
    public struct PidGains
    {
        public float P;
        public float I;
        public float D;
        /// <summary>Feedforward on setpoint change (mixer units per rad/s²).</summary>
        public float FF;

        public PidGains(float p, float i, float d, float ff = 0f) { P = p; I = i; D = d; FF = ff; }
    }

    [Serializable]
    public struct PidSettings
    {
        /// <summary>Pitch (body X) gains.</summary>
        public PidGains Pitch;
        /// <summary>Yaw (body Y) gains.</summary>
        public PidGains Yaw;
        /// <summary>Roll (body Z) gains.</summary>
        public PidGains Roll;
        /// <summary>|I-term| limit in mixer units.</summary>
        public float ITermLimit;
        /// <summary>D-term first-order low-pass cutoff (Hz).</summary>
        public float DTermLowpassHz;
        /// <summary>Total PID authority limit (mixer units) per axis.</summary>
        public float OutputLimit;
        /// <summary>Keep PID authority at zero throttle.</summary>
        public bool AirMode;
        /// <summary>Below this throttle (no Air Mode) PIDs are off and I is reset.</summary>
        public float LowThrottleCutoff;
    }

    [Serializable]
    public struct AngleModeSettings
    {
        /// <summary>Max tilt in Angle mode (degrees).</summary>
        public float MaxAngleDeg;
        /// <summary>Outer loop gain: commanded rate (deg/s) per degree of attitude error.</summary>
        public float AngleGain;
        /// <summary>Clamp on commanded rate from the outer loop (deg/s).</summary>
        public float MaxOuterRateDegPerSec;
    }

    [Serializable]
    public struct MotorSettings
    {
        /// <summary>Static thrust of one motor at full RPM (N).</summary>
        public float MaxThrustPerMotor;
        /// <summary>Prop reaction torque per newton of thrust (N·m/N).</summary>
        public float YawTorquePerThrust;
        /// <summary>First-order spool time constant when speeding up (s).</summary>
        public float TimeConstantUp;
        /// <summary>First-order spool time constant when slowing down (s).</summary>
        public float TimeConstantDown;
        /// <summary>Armed idle as a fraction of max RPM.</summary>
        public float IdleFraction;
        /// <summary>Axial inflow speed at which a prop produces no thrust (m/s).</summary>
        public float PropPitchSpeed;
        /// <summary>Throttle curve exponent applied to stick throttle (1 = linear).</summary>
        public float ThrottleCurveExpo;
    }

    [Serializable]
    public struct AeroSettings
    {
        /// <summary>Linear drag per body axis (N per m/s).</summary>
        public Vec3 LinearDrag;
        /// <summary>Quadratic drag per body axis (N per (m/s)²).</summary>
        public Vec3 QuadraticDrag;
        /// <summary>Rotational damping per body axis (N·m per rad/s).</summary>
        public Vec3 AngularDrag;
    }

    /// <summary>All parameters of one quad. Plain data; wrapped by a ScriptableObject in the Vehicle layer.</summary>
    [Serializable]
    public struct QuadProfile
    {
        public string Name;
        /// <summary>All-up weight (kg).</summary>
        public float Mass;
        /// <summary>Principal moments of inertia about body X (pitch), Y (yaw), Z (roll), kg·m².</summary>
        public Vec3 Inertia;
        /// <summary>Motor offset from centre along body X and Z (m). Quad-X layout.</summary>
        public float ArmX;
        public float ArmZ;
        public MotorSettings Motors;
        public AeroSettings Aero;
        public PidSettings Pid;
        public AngleModeSettings AngleMode;
        public RateProfile Rates;

        /// <summary>A 5" race quad. Starting values; tuned by feel later.</summary>
        public static QuadProfile Race5Inch => new QuadProfile
        {
            Name = "5\" Race",
            Mass = 0.65f,
            Inertia = new Vec3(0.0020f, 0.0035f, 0.0020f),
            ArmX = 0.078f,
            ArmZ = 0.078f,
            Motors = new MotorSettings
            {
                MaxThrustPerMotor = 15.7f,
                YawTorquePerThrust = 0.016f,
                TimeConstantUp = 0.030f,
                TimeConstantDown = 0.040f,
                IdleFraction = 0.05f,
                PropPitchSpeed = 55f,
                ThrottleCurveExpo = 1f,
            },
            Aero = new AeroSettings
            {
                LinearDrag = new Vec3(0.05f, 0.05f, 0.05f),
                QuadraticDrag = new Vec3(0.020f, 0.040f, 0.016f),
                AngularDrag = new Vec3(0.0004f, 0.0004f, 0.0004f),
            },
            Pid = new PidSettings
            {
                Pitch = new PidGains(0.030f, 0.10f, 0.00040f),
                Roll = new PidGains(0.030f, 0.10f, 0.00040f),
                Yaw = new PidGains(0.150f, 0.30f, 0f),
                ITermLimit = 0.15f,
                DTermLowpassHz = 100f,
                OutputLimit = 1f,
                AirMode = true,
                LowThrottleCutoff = 0.05f,
            },
            AngleMode = new AngleModeSettings
            {
                MaxAngleDeg = 55f,
                AngleGain = 10f,
                MaxOuterRateDegPerSec = 400f,
            },
            Rates = RateProfile.BetaflightDefault,
        };
    }

    /// <summary>World/environment settings shared by all quads.</summary>
    [Serializable]
    public struct PhysicsSettings
    {
        /// <summary>Gravity acceleration (m/s²), applied along world -Y.</summary>
        public float Gravity;
        /// <summary>Fixed simulation step (s).</summary>
        public float FixedStep;
        /// <summary>Max sub-steps per rendered frame; time beyond this is dropped.</summary>
        public int MaxSubSteps;

        public static PhysicsSettings Default => new PhysicsSettings
        {
            Gravity = 9.81f,
            FixedStep = 0.001f,
            MaxSubSteps = 16,
        };
    }
}
