using System;

namespace GateRush.Flight
{
    /// <summary>Four motor values. Order: 0 = rear-right, 1 = front-right, 2 = rear-left, 3 = front-left (Betaflight Quad-X).</summary>
    [Serializable]
    public struct Motor4
    {
        public float M0, M1, M2, M3;

        public float this[int i]
        {
            get
            {
                switch (i)
                {
                    case 0: return M0;
                    case 1: return M1;
                    case 2: return M2;
                    default: return M3;
                }
            }
            set
            {
                switch (i)
                {
                    case 0: M0 = value; break;
                    case 1: M1 = value; break;
                    case 2: M2 = value; break;
                    default: M3 = value; break;
                }
            }
        }
    }

    /// <summary>PID controller memory, kept in the state so a QuadState snapshot fully determines the future.</summary>
    [Serializable]
    public struct PidState
    {
        public Vec3 Integral;
        public Vec3 PrevGyro;
        public Vec3 DFiltered;
        public Vec3 PrevSetpoint;
        public bool Initialized;
    }

    /// <summary>Complete simulation state of one quad.</summary>
    [Serializable]
    public struct QuadState
    {
        /// <summary>World position (m).</summary>
        public Vec3 Position;
        /// <summary>World velocity (m/s).</summary>
        public Vec3 Velocity;
        /// <summary>Body-to-world rotation.</summary>
        public Quat Orientation;
        /// <summary>Body-frame angular velocity (rad/s): X = pitch (nose down +), Y = yaw (right +), Z = roll (left +).</summary>
        public Vec3 AngularVelocity;
        /// <summary>Motor speed as a fraction of max RPM (0..1).</summary>
        public Motor4 MotorSpeed;
        /// <summary>Last mixer output per motor (0..1), for debug overlays.</summary>
        public Motor4 MotorCommand;
        /// <summary>Last rate setpoint (rad/s), for debug overlays.</summary>
        public Vec3 Setpoint;
        public PidState Pid;
        public bool Armed;
        /// <summary>Simulated time (s).</summary>
        public double Time;

        public static QuadState AtRest(Vec3 position, Quat orientation) => new QuadState
        {
            Position = position,
            Orientation = orientation,
        };

        public Vec3 Up => Orientation * Vec3.Up;
        public Vec3 Forward => Orientation * Vec3.Forward;
        public Vec3 Right => Orientation * Vec3.Right;
    }
}
