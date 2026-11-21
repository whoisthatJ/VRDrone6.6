using System;

namespace Ushqyr.Flight
{
    /// <summary>
    /// Everything that can go wrong with a drone on purpose (EDU_PLAN F4): set by an activity's timeline or by the
    /// teacher, by name, so the same words work in an activity file, on the teacher's screen and in the attempt
    /// record. Zero (the default) is a healthy drone.
    /// </summary>
    [Serializable]
    public struct FailureSet
    {
        /// <summary>Share of thrust each motor has lost, 0..1 (1 = stopped). Mixer order: rear right, front right, rear left, front left.</summary>
        public Motor4 MotorLoss;
        /// <summary>Damage of each propeller, 0..1: thrust is lost and the drone shakes.</summary>
        public Motor4 PropDamage;
        /// <summary>This share of the battery's charge is suddenly gone.</summary>
        public float BatteryDrop;
        public bool GpsLost;
        /// <summary>0..1: the position wanders more and fixes come less often.</summary>
        public float GpsDegraded;
        /// <summary>Compass interference: the drone believes its heading is off by this many degrees.</summary>
        public float CompassOffsetDeg;
        public bool RadioLost;
        /// <summary>0..1: snow and tearing in the picture from the drone.</summary>
        public float VideoNoise;
        public bool VideoLost;
        /// <summary>The attitude sensor is wrong by these angles: the drone levels itself to a horizon that is not level.</summary>
        public float ImuPitchDeg, ImuRollDeg;
        /// <summary>The barometer's height runs away at this speed (m/s; positive reads higher and higher).</summary>
        public float BaroDriftRate;
        public bool PayloadJam, GimbalStuck;

        /// <summary>The names <see cref="Set"/> and <see cref="Get"/> understand.</summary>
        public static readonly string[] Names =
        {
            "motor0", "motor1", "motor2", "motor3", "prop0", "prop1", "prop2", "prop3",
            "battery_drop", "gps_lost", "gps_degraded", "compass", "radio_lost", "video_noise", "video_lost",
            "imu_pitch", "imu_roll", "baro_drift", "payload_jam", "gimbal_stuck",
        };

        /// <summary>True when nothing is wrong.</summary>
        public bool Healthy
        {
            get
            {
                foreach (string name in Names)
                    if (Get(name) != 0f) return false;
                return true;
            }
        }

        /// <summary>Sets one failure; 0 mends it. Yes/no failures take 0 or anything else. False for a name that is not known.</summary>
        public bool Set(string name, float value)
        {
            if (float.IsNaN(value)) return false;
            float share = value < 0f ? 0f : value > 1f ? 1f : value;
            bool on = value != 0f;
            switch (name)
            {
                case "motor0": MotorLoss.M0 = share; break;
                case "motor1": MotorLoss.M1 = share; break;
                case "motor2": MotorLoss.M2 = share; break;
                case "motor3": MotorLoss.M3 = share; break;
                case "prop0": PropDamage.M0 = share; break;
                case "prop1": PropDamage.M1 = share; break;
                case "prop2": PropDamage.M2 = share; break;
                case "prop3": PropDamage.M3 = share; break;
                case "battery_drop": BatteryDrop = share; break;
                case "gps_lost": GpsLost = on; break;
                case "gps_degraded": GpsDegraded = share; break;
                case "compass": CompassOffsetDeg = Math.Clamp(value, -180f, 180f); break;
                case "radio_lost": RadioLost = on; break;
                case "video_noise": VideoNoise = share; break;
                case "video_lost": VideoLost = on; break;
                case "imu_pitch": ImuPitchDeg = Math.Clamp(value, -20f, 20f); break;
                case "imu_roll": ImuRollDeg = Math.Clamp(value, -20f, 20f); break;
                case "baro_drift": BaroDriftRate = Math.Clamp(value, -2f, 2f); break;
                case "payload_jam": PayloadJam = on; break;
                case "gimbal_stuck": GimbalStuck = on; break;
                default: return false;
            }
            return true;
        }

        /// <summary>The value of one failure (yes/no ones as 1 or 0); NaN for a name that is not known.</summary>
        public float Get(string name)
        {
            switch (name)
            {
                case "motor0": return MotorLoss.M0;
                case "motor1": return MotorLoss.M1;
                case "motor2": return MotorLoss.M2;
                case "motor3": return MotorLoss.M3;
                case "prop0": return PropDamage.M0;
                case "prop1": return PropDamage.M1;
                case "prop2": return PropDamage.M2;
                case "prop3": return PropDamage.M3;
                case "battery_drop": return BatteryDrop;
                case "gps_lost": return GpsLost ? 1f : 0f;
                case "gps_degraded": return GpsDegraded;
                case "compass": return CompassOffsetDeg;
                case "radio_lost": return RadioLost ? 1f : 0f;
                case "video_noise": return VideoNoise;
                case "video_lost": return VideoLost ? 1f : 0f;
                case "imu_pitch": return ImuPitchDeg;
                case "imu_roll": return ImuRollDeg;
                case "baro_drift": return BaroDriftRate;
                case "payload_jam": return PayloadJam ? 1f : 0f;
                case "gimbal_stuck": return GimbalStuck ? 1f : 0f;
                default: return float.NaN;
            }
        }

        /// <summary>
        /// Thrust lost by each motor at <paramref name="time"/>: a weak motor loses a steady share; a damaged propeller
        /// loses up to 35% and adds a shake, a different beat for each motor so the four never cancel.
        /// </summary>
        public Motor4 MotorLossAt(double time)
        {
            return new Motor4
            {
                M0 = One(MotorLoss.M0, PropDamage.M0, time, 0),
                M1 = One(MotorLoss.M1, PropDamage.M1, time, 1),
                M2 = One(MotorLoss.M2, PropDamage.M2, time, 2),
                M3 = One(MotorLoss.M3, PropDamage.M3, time, 3),
            };
        }

        private static float One(float loss, float damage, double time, int motor)
        {
            if (damage > 0f)
            {
                double beat = 9.0 + 1.7 * motor;
                loss += damage * (0.35f + 0.2f * (float)Math.Sin(2.0 * Math.PI * beat * time));
            }
            return loss < 0f ? 0f : loss > 1f ? 1f : loss;
        }

        /// <summary>
        /// A wrong attitude sensor, as the flight controller feels it: every self-levelling command is off by the
        /// sensor's error. Acro mode does not level itself and is not touched.
        /// </summary>
        public PilotInput WithImuError(in PilotInput sticks, in QuadProfile p)
        {
            if ((ImuPitchDeg == 0f && ImuRollDeg == 0f) || sticks.Mode == FlightMode.Acro) return sticks;
            float full = p.AngleMode.MaxAngleDeg > 1f ? p.AngleMode.MaxAngleDeg : 45f;
            PilotInput o = sticks;
            o.Pitch = Math.Clamp(o.Pitch + ImuPitchDeg / full, -1f, 1f);
            o.Roll = Math.Clamp(o.Roll + ImuRollDeg / full, -1f, 1f);
            return o;
        }
    }
}
