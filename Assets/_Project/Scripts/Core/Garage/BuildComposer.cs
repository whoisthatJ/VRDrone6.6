using System;
using System.Collections.Generic;
using System.Linq;
using GateRush.Flight;
using UnityEngine;

namespace GateRush.Garage
{
    /// <summary>What the garage shows for a build; every number comes from the same physics the flight model uses.</summary>
    public struct BuildStats
    {
        public float ThrustG;        // static, all four motors, full throttle
        public float WeightG;
        public float TopSpeedKmh;
        public float TorqueNm;       // peak motor torque
        public float Drag;           // forward quadratic drag (kg/m = N per (m/s)²)
        public float Rpm;            // full throttle under load
        public float PeakTempC;      // estimate
        public float ThrustToWeight;
        public float HoverThrottle;  // 0..1
        public float HoverMinutes;
        public float CurrentA;       // per motor, full throttle
        public float VoltageV;       // pack under full load
    }

    /// <summary>
    /// Turns a <see cref="QuadBuild"/> into the <see cref="QuadProfile"/> the simulator flies, and into stats:
    /// <list type="bullet">
    /// <item>full-throttle operating point per motor: battery at 3.85 V/cell (≈80 % charge) minus pack sag, motor
    /// ω = Kv (V − I·Rm), motor torque Kt (I − I0) = prop torque Cq ρ n² D⁵, thrust = Ct ρ n² D⁴;</item>
    /// <item>mass = sum of parts; inertia from motor/prop point masses on the arms, arm rods, body, battery and extras;</item>
    /// <item>drag = body drag areas plus the prop discs' share (ram drag), per axis or averaged;</item>
    /// <item>PID gains scaled from the nearest hand-tuned class by inertia ÷ control authority;</item>
    /// <item>top speed = best pitch angle where thrust (with inflow loss) balances drag and still holds altitude.</item>
    /// </list>
    /// </summary>
    public static class BuildComposer
    {
        public const float Rho = 1.225f;
        public const float Gravity = 9.81f;
        /// <summary>Cell voltage under load at about 80 % charge.</summary>
        public const float CellVoltage = 3.85f;

        public struct OperatingPoint
        {
            public float CurrentA, VoltageV, RevPerSec, ThrustN, TorqueNm;
        }

        public static List<string> Validate(QuadBuild b)
        {
            var problems = new List<string>();
            var f = b.Frame;
            if (f == null)
            {
                problems.Add("No frame.");
                return problems;
            }
            void Check(PartDef p, string what)
            {
                if (p == null) problems.Add($"No {what}.");
                else if (!p.Fits(f.Size)) problems.Add($"{p.Name} doesn't fit a {PartCatalog.SizeLabel(f.Size)} frame.");
            }
            Check(b.Motor, "motors");
            Check(b.Prop, "props");
            Check(b.Battery, "battery");
            if (b.Prop != null && b.Prop.DiameterIn > f.MaxPropIn + 0.05f) problems.Add($"{b.Prop.Name} is too big for {f.Name} (max {f.MaxPropIn:0.#}\").");
            foreach (var e in b.Extras)
                if (!e.Fits(f.Size)) problems.Add($"{e.Name} doesn't fit a {PartCatalog.SizeLabel(f.Size)} quad.");
            return problems;
        }

        /// <summary>Flight controller, ESC, camera, VTX, receiver, wires and strap for the class (g).</summary>
        public static float ElectronicsMassG(SizeClass s) => s switch
        {
            SizeClass.Whoop => 6f,
            SizeClass.TwoFive => 14f,
            SizeClass.Three => 16f,
            SizeClass.Five => 32f,
            _ => 40f,
        };

        public static float MassG(QuadBuild b) =>
            b.Frame.MassG + 4f * (b.Motor.MassG + b.Prop.MassG) + b.Battery.MassG + ElectronicsMassG(b.Size) + b.Extras.Sum(e => e.MassG);

        /// <summary>Motor arm positions (m): right/left = ±ArmX, front/back = ±ArmZ.</summary>
        public static void Arms(FrameDef f, out float armX, out float armZ)
        {
            float r = f.WheelbaseMm * 0.0005f;
            float a = Mathf.Atan(Mathf.Max(f.Stretch, 0.5f));
            armX = r * Mathf.Cos(a);
            armZ = r * Mathf.Sin(a);
        }

        public static OperatingPoint FullThrottle(MotorDef m, PropDef p, BatteryDef bat)
        {
            float d = p.DiameterIn * 0.0254f;
            float kv = m.Kv * 2f * Mathf.PI / 60f;
            float kt = 1f / kv;
            float rBat = bat.ResistanceMilliOhm * 0.001f;
            float vOpen = bat.Cells * CellVoltage;
            float lo = m.I0, hi = vOpen / (4f * rBat + m.Rm);
            float n = 0f, v = vOpen, current = lo;
            for (int i = 0; i < 60; i++)
            {
                current = 0.5f * (lo + hi);
                v = vOpen - 4f * current * rBat;
                float w = Mathf.Max(kv * (v - current * m.Rm), 0f);
                n = w / (2f * Mathf.PI);
                float residual = kt * (current - m.I0) - p.Cq * Rho * n * n * Mathf.Pow(d, 5f);
                if (residual < 0f) lo = current;
                else hi = current;
            }
            return new OperatingPoint
            {
                CurrentA = current, VoltageV = v, RevPerSec = n,
                ThrustN = p.Ct * Rho * n * n * Mathf.Pow(d, 4f), TorqueNm = kt * (current - m.I0),
            };
        }

        /// <summary>Total prop disc area (m²).</summary>
        private static float DiscArea(PropDef p)
        {
            float r = p.DiameterIn * 0.0254f * 0.5f;
            return 4f * Mathf.PI * r * r;
        }

        /// <summary>Quadratic drag coefficients (kg/m) for the body x (side), y (top), z (front) axes.</summary>
        public static Vec3 QuadraticDrag(QuadBuild b)
        {
            var f = b.Frame;
            float disc = DiscArea(b.Prop) * 1e4f; // cm²
            float ducts = f.Geometry == FrameGeometry.Ducted ? 2.5f : 1f;
            float battery = 0.34f * Mathf.Pow(b.Battery.MassG, 2f / 3f);
            float extrasFront = b.Extras.Sum(e => e.CdAFront), extrasTop = b.Extras.Sum(e => e.CdATop);
            float front = f.CdAFront + battery + extrasFront + 0.40f * disc * ducts;
            float top = f.CdATop + battery * 1.6f + extrasTop + 1.15f * disc * ducts;
            float side = f.CdASide + battery + extrasFront * 0.8f + 0.55f * disc * ducts;
            if (!b.AdvancedDrag) front = top = side = (front + top + side) / 3f;
            float k = 0.5f * Rho * 1e-4f;
            return new Vec3(k * side, k * top, k * front);
        }

        public static Vec3 Inertia(QuadBuild b)
        {
            var f = b.Frame;
            Arms(f, out float ax, out float az);
            float ixx = 0f, iyy = 0f, izz = 0f;
            void Point(float m, float x, float y, float z)
            {
                ixx += m * (y * y + z * z);
                iyy += m * (x * x + z * z);
                izz += m * (x * x + y * y);
            }
            void Box(float m, float sx, float sy, float sz, float x, float y, float z)
            {
                ixx += m * (sy * sy + sz * sz) / 12f;
                iyy += m * (sx * sx + sz * sz) / 12f;
                izz += m * (sx * sx + sy * sy) / 12f;
                Point(m, x, y, z);
            }
            float mp = (b.Motor.MassG + b.Prop.MassG) * 0.001f;
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { -1f, 1f })
                    Point(mp, sx * ax, 0f, sz * az);
            // Frame: 60 % in the four arms (rods from the centre), 40 % in the body plates.
            float frame = f.MassG * 0.001f;
            float arm = Mathf.Sqrt(ax * ax + az * az);
            float armRod = 0.6f * frame * arm * arm / 3f;
            ixx += armRod * (az * az) / (arm * arm);
            izz += armRod * (ax * ax) / (arm * arm);
            iyy += armRod;
            float bl = f.BodyLengthMm * 0.001f, bw = f.BodyWidthMm * 0.001f, st = f.StackMm * 0.001f;
            Box(0.4f * frame + ElectronicsMassG(b.Size) * 0.001f, bw, st, bl, 0f, 0f, 0f);
            float bat = b.Battery.MassG * 0.001f;
            Box(bat, bw * 0.9f, st * 1.2f, bl * 0.8f, 0f, st, 0f);
            foreach (var e in b.Extras)
                Point(e.MassG * 0.001f, 0f, st * 1.4f, bl * 0.3f);
            // Wires, straps, TPU and screws the parts list leaves out.
            const float unmodelled = 1.3f;
            return new Vec3(ixx * unmodelled, iyy * unmodelled, izz * unmodelled);
        }

        /// <summary>The flight profile for a valid build.</summary>
        public static QuadProfile Compose(QuadBuild b)
        {
            var f = b.Frame;
            var op = FullThrottle(b.Motor, b.Prop, b.Battery);
            float mass = MassG(b) * 0.001f;
            Arms(f, out float ax, out float az);
            float d = b.Prop.DiameterIn * 0.0254f;
            var baseProfile = mass < 0.12f ? QuadProfile.Whoop65 : QuadProfile.Race5Inch;

            var p = baseProfile;
            p.Name = b.Name;
            p.Mass = mass;
            p.Inertia = Inertia(b);
            p.ArmX = ax;
            p.ArmZ = az;

            float discRatio = DiscArea(b.Prop) / DiscArea(PartCatalog.Find<PropDef>("prop-5143-3"));
            float propInertia = b.Prop.MassG / 4.4f * (b.Prop.DiameterIn / 5.1f) * (b.Prop.DiameterIn / 5.1f);
            float torqueRatio = Mathf.Max(op.TorqueNm / 0.117f, 1e-3f);
            float tauUp = Mathf.Clamp(0.030f * Mathf.Sqrt(propInertia / torqueRatio), 0.015f, 0.06f);
            p.Motors = new MotorSettings
            {
                MaxThrustPerMotor = op.ThrustN,
                YawTorquePerThrust = b.Prop.Cq * d / b.Prop.Ct,
                TimeConstantUp = tauUp,
                TimeConstantDown = tauUp * 1.3f,
                IdleFraction = 0.05f,
                PropPitchSpeed = b.Prop.PitchIn * 0.0254f * op.RevPerSec * 1.1f,
                ThrottleCurveExpo = 1f,
            };

            float lin = 0.05f * Mathf.Sqrt(mass / 0.65f);
            float ang = 0.0004f * Mathf.Pow(discRatio, 1.6f);
            p.Aero = new AeroSettings
            {
                LinearDrag = new Vec3(lin, lin, lin),
                QuadraticDrag = QuadraticDrag(b),
                AngularDrag = new Vec3(ang, ang, ang),
            };

            // Same response as the hand-tuned class: scale gains by inertia over control authority, per axis.
            float baseArm = baseProfile.ArmX, baseT = baseProfile.Motors.MaxThrustPerMotor;
            float Scale(float inertia, float baseInertia, float authority, float baseAuthority) =>
                Mathf.Clamp(inertia / baseInertia / (authority / baseAuthority), 0.4f, 2.5f);
            float kPitch = Scale(p.Inertia.x, baseProfile.Inertia.x, op.ThrustN * az, baseT * baseProfile.ArmZ);
            float kRoll = Scale(p.Inertia.z, baseProfile.Inertia.z, op.ThrustN * ax, baseT * baseArm);
            float kYaw = Scale(p.Inertia.y, baseProfile.Inertia.y, op.ThrustN * p.Motors.YawTorquePerThrust,
                baseT * baseProfile.Motors.YawTorquePerThrust);
            PidGains S(PidGains g, float k) => new PidGains(g.P * k, g.I * k, g.D * k, g.FF * k);
            var pid = baseProfile.Pid;
            pid.Pitch = S(pid.Pitch, kPitch);
            pid.Roll = S(pid.Roll, kRoll);
            pid.Yaw = S(pid.Yaw, kYaw);
            p.Pid = pid;

            p.Battery = new BatterySettings
            {
                Cells = b.Battery.Cells,
                CapacityMah = b.Battery.CapacityMah,
                InternalResistance = b.Battery.ResistanceMilliOhm * 0.001f,
                MaxCurrent = 4f * op.CurrentA,
                IdleCurrent = b.Size switch { SizeClass.Whoop => 0.15f, SizeClass.TwoFive => 0.3f, SizeClass.Three => 0.35f, SizeClass.Five => 0.6f, _ => 0.8f },
            };
            return p;
        }

        public static BuildStats Stats(QuadBuild b)
        {
            var op = FullThrottle(b.Motor, b.Prop, b.Battery);
            var profile = Compose(b);
            float massG = MassG(b);
            float weightN = massG * 0.001f * Gravity;
            float hover = Mathf.Clamp01(Mathf.Sqrt(weightN / (4f * op.ThrustN)));
            float hoverCurrent = 4f * op.CurrentA * Mathf.Pow(hover, 2.5f) + profile.Battery.IdleCurrent;
            return new BuildStats
            {
                ThrustG = 4f * op.ThrustN / Gravity * 1000f,
                WeightG = massG,
                TopSpeedKmh = TopSpeed(profile) * 3.6f,
                TorqueNm = op.TorqueNm,
                Drag = profile.Aero.QuadraticDrag.z,
                Rpm = op.RevPerSec * 60f,
                PeakTempC = 25f + op.CurrentA * op.CurrentA * b.Motor.Rm * 0.7f,
                ThrustToWeight = 4f * op.ThrustN / weightN,
                HoverThrottle = hover,
                HoverMinutes = b.Battery.CapacityMah * 0.001f * 0.8f / Mathf.Max(hoverCurrent, 0.01f) * 60f,
                CurrentA = op.CurrentA,
                VoltageV = op.VoltageV,
            };
        }

        /// <summary>
        /// Fastest level flight (m/s): for each pitch angle, the speed where full thrust (less the prop inflow loss)
        /// balances drag horizontally, kept only if the quad still holds its altitude there.
        /// </summary>
        public static float TopSpeed(in QuadProfile p)
        {
            float best = 0f;
            var m = p.Motors;
            var a = p.Aero;
            float weight = p.Mass * Gravity;
            for (float deg = 5f; deg <= 85f; deg += 1f)
            {
                float th = deg * Mathf.Deg2Rad, s = Mathf.Sin(th), c = Mathf.Cos(th);
                float Thrust(float v) => 4f * m.MaxThrustPerMotor * Mathf.Clamp01(1f - v * s / Mathf.Max(m.PropPitchSpeed, 1f));
                float vz(float v) => v * c; // body forward
                float vy(float v) => v * s; // body up
                float Fz(float v) => a.LinearDrag.z * vz(v) + a.QuadraticDrag.z * vz(v) * vz(v);
                float Fy(float v) => a.LinearDrag.y * vy(v) + a.QuadraticDrag.y * vy(v) * vy(v);
                float Horizontal(float v) => Thrust(v) * s - Fz(v) * c - Fy(v) * s;
                float lo = 0f, hi = 120f;
                if (Horizontal(lo) <= 0f) continue;
                for (int i = 0; i < 40; i++)
                {
                    float mid = 0.5f * (lo + hi);
                    if (Horizontal(mid) > 0f) lo = mid;
                    else hi = mid;
                }
                float v0 = lo;
                float vertical = Thrust(v0) * c + Fz(v0) * s - Fy(v0) * c;
                if (vertical >= weight && v0 > best) best = v0;
            }
            return best;
        }

        /// <summary>Thrust (g, all motors), current (A, per motor) and RPM at throttle steps, for the Data sheet.</summary>
        public static IEnumerable<(float throttle, float thrustG, float currentA, float rpm)> Curve(QuadBuild b)
        {
            var op = FullThrottle(b.Motor, b.Prop, b.Battery);
            foreach (float t in new[] { 0.25f, 0.5f, 0.75f, 1f })
                yield return (t, 4f * op.ThrustN * t * t / Gravity * 1000f, op.CurrentA * t * t * t, op.RevPerSec * 60f * t);
        }
    }
}
