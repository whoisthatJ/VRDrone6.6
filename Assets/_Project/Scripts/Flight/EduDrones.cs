using System;

namespace Ushqyr.Flight
{
    /// <summary>
    /// The drone classes of the Education Suite (EDU_PLAN F3): two camera drones, a delivery drone and a small
    /// education drone. Generic designs of our own; the numbers are those of typical aircraft of each size. They fly
    /// in the same flight model as the racing quads, only slower, heavier and calmer.
    /// </summary>
    public static class EduDrones
    {
        /// <summary>Folding camera drone, about 900 g, 8.7" propellers: steady in wind, half an hour in the air.</summary>
        public static QuadProfile Camera900 => Build("Camera 900", 0.90f, 0.124f, 4.9f, new Vec3(0.0080f, 0.0140f, 0.0080f),
            motorUp: 0.060f, motorDown: 0.080f, pitchSpeed: 28f, linearDrag: 0.10f, quadDrag: new Vec3(0.015f, 0.030f, 0.013f),
            maxAngle: 35f, rate: 200f, yawRate: 120f,
            battery: new BatterySettings { Cells = 4, CapacityMah = 3850f, InternalResistance = 0.040f, MaxCurrent = 40f, IdleCurrent = 0.8f });

        /// <summary>The under-250 g camera drone: light, quiet, pushed about by wind sooner.</summary>
        public static QuadProfile Camera250 => Build("Camera 250", 0.249f, 0.075f, 1.25f, new Vec3(0.0011f, 0.0020f, 0.0011f),
            motorUp: 0.050f, motorDown: 0.065f, pitchSpeed: 22f, linearDrag: 0.05f, quadDrag: new Vec3(0.008f, 0.016f, 0.007f),
            maxAngle: 30f, rate: 200f, yawRate: 120f,
            battery: new BatterySettings { Cells = 2, CapacityMah = 2400f, InternalResistance = 0.060f, MaxCurrent = 14f, IdleCurrent = 0.5f });

        /// <summary>Delivery drone, 2.5 kg empty, built to lift a 1 kg payload (see <see cref="WithPayload"/>).</summary>
        public static QuadProfile Delivery => Build("Delivery", 2.5f, 0.25f, 15.5f, new Vec3(0.060f, 0.110f, 0.060f),
            motorUp: 0.090f, motorDown: 0.120f, pitchSpeed: 24f, linearDrag: 0.25f, quadDrag: new Vec3(0.050f, 0.100f, 0.050f),
            maxAngle: 30f, rate: 150f, yawRate: 90f,
            battery: new BatterySettings { Cells = 6, CapacityMah = 10000f, InternalResistance = 0.012f, MaxCurrent = 120f, IdleCurrent = 1.5f });

        /// <summary>Small education drone with propeller guards, about 90 g: the one students program.</summary>
        public static QuadProfile EduSmall => Build("Edu", 0.087f, 0.045f, 0.47f, new Vec3(0.00012f, 0.00020f, 0.00012f),
            motorUp: 0.030f, motorDown: 0.040f, pitchSpeed: 14f, linearDrag: 0.03f, quadDrag: new Vec3(0.004f, 0.008f, 0.004f),
            maxAngle: 25f, rate: 250f, yawRate: 150f,
            battery: new BatterySettings { Cells = 1, CapacityMah = 1100f, InternalResistance = 0.080f, MaxCurrent = 9f, IdleCurrent = 0.3f });

        /// <summary>Every class, for tests and lists.</summary>
        public static QuadProfile[] All => new[] { Camera900, Camera250, Delivery, EduSmall };

        /// <summary>
        /// The same drone carrying <paramref name="kilograms"/> under its centre: heavier, and slower to turn. Dropping
        /// the payload is going back to the profile without it.
        /// </summary>
        public static QuadProfile WithPayload(in QuadProfile drone, float kilograms, float hangBelow = 0.12f)
        {
            if (kilograms <= 0f) return drone;
            QuadProfile p = drone;
            p.Mass += kilograms;
            // A point mass below the centre adds m·d² about the two level axes and nothing about the upright one.
            float extra = kilograms * hangBelow * hangBelow;
            p.Inertia = new Vec3(drone.Inertia.x + extra, drone.Inertia.y, drone.Inertia.z + extra);
            return p;
        }

        /// <summary>Thrust of all four motors over weight.</summary>
        public static float ThrustToWeight(in QuadProfile p, float gravity = 9.81f) => 4f * p.Motors.MaxThrustPerMotor / (p.Mass * gravity);

        private static QuadProfile Build(string name, float mass, float arm, float thrustPerMotor, Vec3 inertia, float motorUp, float motorDown,
            float pitchSpeed, float linearDrag, Vec3 quadDrag, float maxAngle, float rate, float yawRate, BatterySettings battery)
        {
            const float yawTorquePerThrust = 0.020f;
            // Rate-loop gains from the drone's own size: the torque one unit of command gives, over the inertia it
            // has to turn, set to the same quickness for every class. Slow motors (large propellers) get less.
            float quickness = motorUp > 0.07f ? 22f : motorUp > 0.045f ? 30f : 40f;
            float torque = 4f * arm * thrustPerMotor, yawTorque = 4f * thrustPerMotor * yawTorquePerThrust;
            float p = quickness * inertia.x / torque, d = 0.35f * inertia.x / torque;
            float yawP = 0.6f * quickness * inertia.y / yawTorque;
            return new QuadProfile
            {
                Name = name,
                Mass = mass,
                Inertia = inertia,
                ArmX = arm,
                ArmZ = arm,
                Motors = new MotorSettings
                {
                    MaxThrustPerMotor = thrustPerMotor,
                    YawTorquePerThrust = yawTorquePerThrust,
                    TimeConstantUp = motorUp,
                    TimeConstantDown = motorDown,
                    IdleFraction = 0.06f,
                    PropPitchSpeed = pitchSpeed,
                    ThrottleCurveExpo = 1f,
                },
                Aero = new AeroSettings
                {
                    LinearDrag = new Vec3(linearDrag, linearDrag, linearDrag),
                    QuadraticDrag = quadDrag,
                    AngularDrag = new Vec3(inertia.x * 0.25f, inertia.y * 0.25f, inertia.x * 0.25f),
                },
                Pid = new PidSettings
                {
                    Pitch = new PidGains(p, 3f * p, d),
                    Roll = new PidGains(p, 3f * p, d),
                    Yaw = new PidGains(yawP, 2f * yawP, 0f),
                    ITermLimit = 0.15f,
                    DTermLowpassHz = 60f,
                    OutputLimit = 1f,
                    AirMode = true,
                    LowThrottleCutoff = 0.05f,
                },
                // A drone with slow motors is asked to level more gently, or it swings past the angle.
                AngleMode = new AngleModeSettings { MaxAngleDeg = maxAngle, AngleGain = motorUp > 0.07f ? 4.5f : 6f, MaxOuterRateDegPerSec = rate },
                Rates = new RateProfile
                {
                    Type = RatesType.Actual,
                    Roll = new AxisRates(60f, rate, 0.3f),
                    Pitch = new AxisRates(60f, rate, 0.3f),
                    Yaw = new AxisRates(60f, yawRate, 0.3f),
                },
                Battery = battery,
            };
        }
    }
}
