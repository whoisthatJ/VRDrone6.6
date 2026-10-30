using System;

namespace GateRush.Flight
{
    /// <summary>
    /// Deterministic quad flight model. Pure C#: no engine types, no allocations.
    /// Pipeline per step: sticks → rates (+ outer angle loop) → rate PID → mixer → motor lag → forces/torques → integrate.
    /// </summary>
    public static class QuadSimulator
    {
        private const float DegToRad = MathF.PI / 180f;
        private const float RadToDeg = 180f / MathF.PI;

        public static void Step(ref QuadState s, in PilotInput input, in QuadProfile p, float dt) =>
            Step(ref s, input, p, PhysicsSettings.Default, dt);

        public static void Step(ref QuadState s, in PilotInput input, in QuadProfile p, in PhysicsSettings env, float dt)
        {
            s.Armed = input.Arm;
            float throttle = MotorModel.ThrottleCurve(p.Motors, input.Throttle);

            // 1. Setpoint (rad/s, body axes).
            Vec3 setpoint = ComputeSetpoint(s, input, p);
            s.Setpoint = setpoint;

            // 2. Rate PID.
            bool pidActive = s.Armed && (p.Pid.AirMode || throttle > p.Pid.LowThrottleCutoff);
            Vec3 axisCommand = PidController.Update(ref s.Pid, p.Pid, setpoint, s.AngularVelocity, dt, pidActive);

            // 3. Mixer.
            s.MotorCommand = s.Armed ? Mixer.Mix(throttle, axisCommand, p.Pid.AirMode) : default;

            // 4. Motors → forces and torques in body frame.
            Quat q = s.Orientation;
            Vec3 vBody = q.Conjugate * s.Velocity;
            Vec3 thrustBody = Vec3.Zero;
            Vec3 torqueBody = Vec3.Zero;
            for (int i = 0; i < 4; i++)
            {
                float target = MotorModel.TargetSpeed(p.Motors, s.MotorCommand[i], s.Armed);
                float speed = MotorModel.Spool(p.Motors, s.MotorSpeed[i], target, dt);
                s.MotorSpeed[i] = speed;

                Vec3 r = Mixer.MotorPosition(i, p.ArmX, p.ArmZ);
                // Axial inflow (air entering the disc from above) includes rotation of the frame.
                float inflow = vBody.y + Vec3.Cross(s.AngularVelocity, r).y;
                float t = MotorModel.Thrust(p.Motors, speed, inflow);
                Vec3 f = new Vec3(0f, t, 0f);
                thrustBody += f;
                torqueBody += Vec3.Cross(r, f);
                torqueBody += new Vec3(0f, Mixer.YawSign(i) * p.Motors.YawTorquePerThrust * t, 0f);
            }

            Vec3 forceBody = thrustBody + AeroModel.DragForce(p.Aero, vBody);
            torqueBody += AeroModel.DampingTorque(p.Aero, s.AngularVelocity);

            // 5. Rotational dynamics (Euler's equations with diagonal inertia).
            Vec3 w = s.AngularVelocity;
            Vec3 iw = Vec3.Scale(p.Inertia, w);
            Vec3 angAccel = Vec3.Divide(torqueBody - Vec3.Cross(w, iw), p.Inertia);
            s.AngularVelocity = w + angAccel * dt;
            s.Orientation = q.IntegrateBodyRate(s.AngularVelocity, dt);

            // 6. Translational dynamics (semi-implicit Euler).
            Vec3 accel = (q * forceBody) / p.Mass + new Vec3(0f, -env.Gravity, 0f);
            s.Velocity += accel * dt;
            s.Position += s.Velocity * dt;
            s.Time += dt;
        }

        /// <summary>
        /// Runs fixed sub-steps for one rendered frame. Leftover time stays in <paramref name="accumulator"/>;
        /// time beyond MaxSubSteps is dropped so a hitch can't spiral. Returns the number of steps taken.
        /// </summary>
        public static int Advance(ref QuadState s, in PilotInput input, in QuadProfile p, in PhysicsSettings env,
            ref float accumulator, float frameDt)
        {
            var none = new NoHook();
            return Advance(ref s, input, p, env, ref accumulator, frameDt, ref none);
        }

        /// <summary>
        /// As <see cref="Advance(ref QuadState, in PilotInput, in QuadProfile, in PhysicsSettings, ref float, float)"/>,
        /// calling <paramref name="hook"/> after every sub-step (e.g. for collisions). Generic struct hook: no allocations.
        /// </summary>
        public static int Advance<THook>(ref QuadState s, in PilotInput input, in QuadProfile p, in PhysicsSettings env,
            ref float accumulator, float frameDt, ref THook hook) where THook : struct, IStepHook
        {
            accumulator += frameDt;
            int steps = 0;
            while (accumulator >= env.FixedStep && steps < env.MaxSubSteps)
            {
                QuadState prev = s;
                Step(ref s, input, p, env, env.FixedStep);
                accumulator -= env.FixedStep;
                steps++;
                if (!hook.AfterStep(ref s, prev, env.FixedStep)) break;
            }
            if (steps == env.MaxSubSteps && accumulator > env.FixedStep) accumulator = 0f;
            return steps;
        }

        private struct NoHook : IStepHook
        {
            public bool AfterStep(ref QuadState s, in QuadState previous, float dt) => true;
        }

        /// <summary>Rate setpoint in body axes (rad/s) for the current flight mode.</summary>
        public static Vec3 ComputeSetpoint(in QuadState s, in PilotInput input, in QuadProfile p)
        {
            var rp = p.Rates;
            float pitchRate = Rates.DegPerSec(rp.Type, rp.Pitch, input.Pitch);
            float yawRate = Rates.DegPerSec(rp.Type, rp.Yaw, input.Yaw);
            float rollRate = Rates.DegPerSec(rp.Type, rp.Roll, input.Roll);
            // Body Z positive = roll left, so roll-right stick maps to negative Z.
            Vec3 acro = new Vec3(pitchRate, yawRate, -rollRate);

            if (input.Mode == FlightMode.Acro) return acro * DegToRad;

            // Outer attitude loop on the two tilt axes; yaw stays a rate command.
            var a = p.AngleMode;
            Vec3 fwd = s.Forward;
            Vec3 right = s.Right;
            float pitchAngle = -MathF.Asin(Rates.Clamp(fwd.y, -1f, 1f)) * RadToDeg; // nose down +
            float rollZAngle = MathF.Asin(Rates.Clamp(right.y, -1f, 1f)) * RadToDeg; // roll left +
            float pitchTarget = input.Pitch * a.MaxAngleDeg;
            float rollZTarget = -input.Roll * a.MaxAngleDeg;
            float maxOuter = a.MaxOuterRateDegPerSec;
            Vec3 level = new Vec3(
                Rates.Clamp((pitchTarget - pitchAngle) * a.AngleGain, -maxOuter, maxOuter),
                yawRate,
                Rates.Clamp((rollZTarget - rollZAngle) * a.AngleGain, -maxOuter, maxOuter));

            if (input.Mode == FlightMode.Horizon)
            {
                // Self-levels near centre stick, blends to full acro at full deflection.
                float deflection = MathF.Max(MathF.Abs(input.Pitch), MathF.Abs(input.Roll));
                level = Vec3.Lerp(level, acro, deflection);
                level.y = yawRate;
            }
            return level * DegToRad;
        }
    }
}
