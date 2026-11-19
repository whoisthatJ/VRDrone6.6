using System;
using System.Collections.Generic;
using System.Globalization;
using Ushqyr.Flight;
using UnityEngine;

namespace Ushqyr.Bench
{
    /// <summary>
    /// The small part of YAML a drone file uses: nested keys by indentation, numbers, strings, true/false, and lists
    /// written in one line as [a, b, c]. Comments start with #. No anchors, no multi-line strings, no "- item" lists.
    /// Values come back as Dictionary&lt;string, object&gt;, List&lt;object&gt;, double, bool or string.
    /// </summary>
    public static class MiniYaml
    {
        public static Dictionary<string, object> Parse(string text)
        {
            var root = new Dictionary<string, object>();
            var stack = new List<(int indent, Dictionary<string, object> map)> { (-1, root) };
            string[] lines = text.Replace("\r", "").Split('\n');
            for (int n = 0; n < lines.Length; n++)
            {
                string raw = StripComment(lines[n]);
                if (raw.Trim().Length == 0) continue;
                if (raw.Contains("\t")) throw new FormatException($"line {n + 1}: use spaces, not tabs");
                int indent = raw.Length - raw.TrimStart(' ').Length;
                string line = raw.Trim();
                if (line.StartsWith("- ") || line == "-") throw new FormatException($"line {n + 1}: write lists in one line, as [a, b, c]");
                int colon = line.IndexOf(':');
                if (colon <= 0) throw new FormatException($"line {n + 1}: expected \"key: value\"");
                string key = Unquote(line.Substring(0, colon).Trim());
                string value = line.Substring(colon + 1).Trim();
                while (stack.Count > 1 && stack[stack.Count - 1].indent >= indent) stack.RemoveAt(stack.Count - 1);
                Dictionary<string, object> parent = stack[stack.Count - 1].map;
                if (parent.ContainsKey(key)) throw new FormatException($"line {n + 1}: \"{key}\" appears twice");
                if (value.Length == 0)
                {
                    var child = new Dictionary<string, object>();
                    parent[key] = child;
                    stack.Add((indent, child));
                }
                else parent[key] = Scalar(value, n + 1);
            }
            return root;
        }

        private static string StripComment(string line)
        {
            bool quoted = false;
            char quote = '"';
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (quoted) { if (c == quote) quoted = false; }
                else if (c == '"' || c == '\'') { quoted = true; quote = c; }
                else if (c == '#' && (i == 0 || line[i - 1] == ' ')) return line.Substring(0, i);
            }
            return line;
        }

        private static string Unquote(string s) =>
            s.Length >= 2 && (s[0] == '"' || s[0] == '\'') && s[s.Length - 1] == s[0] ? s.Substring(1, s.Length - 2) : s;

        private static object Scalar(string v, int line)
        {
            if (v.StartsWith("["))
            {
                if (!v.EndsWith("]")) throw new FormatException($"line {line}: a list must close on the same line");
                var list = new List<object>();
                string inner = v.Substring(1, v.Length - 2).Trim();
                if (inner.Length > 0)
                    foreach (string part in inner.Split(','))
                        list.Add(Scalar(part.Trim(), line));
                return list;
            }
            if (v.StartsWith("{")) throw new FormatException($"line {line}: write nested keys on their own indented lines");
            if (v == "true" || v == "True") return true;
            if (v == "false" || v == "False") return false;
            if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) && !double.IsNaN(d) && !double.IsInfinity(d)) return d;
            return Unquote(v);
        }
    }

    /// <summary>What the organisers allow for the team drone class. Read from class_limits.json; these are the defaults.</summary>
    [Serializable]
    public sealed class DroneLimits
    {
        /// <summary>Largest size: width, length, height (mm). The task: 300 x 300 x 180.</summary>
        public float max_width_mm = 300f, max_length_mm = 300f, max_height_mm = 180f;
        public float min_mass_g = 25f, max_mass_g = 1500f;
        /// <summary>Largest total static thrust divided by weight.</summary>
        public float max_thrust_to_weight = 8f;
        /// <summary>The drone must be able to hover with room to spare.</summary>
        public float min_thrust_to_weight = 1.5f;
        public int max_cells = 6;
        public float max_capacity_mah = 2200f;
        public float min_motor_time_constant_ms = 10f;
        public float max_rate_deg_s = 1000f;
        public float max_angle_deg = 70f;
        public float min_camera_tilt_deg = 0f, max_camera_tilt_deg = 60f;
        public float min_camera_hfov_deg = 60f, max_camera_hfov_deg = 150f;
        public string[] sensors = { "camera", "imu", "tof", "lidar", "battery" };

        public static DroneLimits Parse(string json)
        {
            var l = new DroneLimits();
            if (!string.IsNullOrWhiteSpace(json)) JsonUtility.FromJsonOverwrite(json, l);
            return l;
        }
    }

    /// <summary>
    /// A team's drone for the AI CORE class, as declared in its drone.yaml: size, mass, motors, props, battery,
    /// controller limits, camera and sensors. The flight model's parameters are computed from it.
    /// </summary>
    public sealed class DroneConfig
    {
        public string Name = "";
        public float WidthMm, LengthMm, HeightMm;
        public float MassG;
        public float MotorThrustG, MotorTimeConstantMs = 35f;
        public float ArmXMm, ArmZMm;
        public float PropDiameterIn = 3f, PropPitchIn = 3f;
        public int Cells = 4;
        public float CapacityMah = 850f, ResistanceMohm, MaxCurrentA;
        public float RollRate = 500f, PitchRate = 500f, YawRate = 400f, MaxAngleDeg = 45f;
        public float CameraTiltDeg = 20f, CameraHfovDeg = 120f;
        public readonly List<string> Sensors = new List<string>();
        /// <summary>SHA-256 of the file's text: goes into every result.</summary>
        public string Hash = "";
        public readonly List<string> Errors = new List<string>();

        public bool Has(string sensor) => Sensors.Contains(sensor);

        /// <summary>
        /// Reads and checks a drone file. Problems are collected in <see cref="Errors"/> in plain words; a config with
        /// errors must not fly.
        /// </summary>
        public static DroneConfig Parse(string yaml, DroneLimits limits)
        {
            var c = new DroneConfig { Hash = BenchHash.Sha256(yaml) };
            Dictionary<string, object> root;
            try { root = MiniYaml.Parse(yaml); }
            catch (FormatException e)
            {
                c.Errors.Add(e.Message);
                return c;
            }

            var known = new HashSet<string> { "schema", "name", "class", "size_mm", "mass_g", "motors", "props", "battery", "controller", "camera", "sensors" };
            foreach (string key in root.Keys)
                if (!known.Contains(key)) c.Errors.Add($"unknown key \"{key}\"");
            if (c.Num(root, "schema", 1, true) != 1) c.Errors.Add("schema must be 1");
            if (root.TryGetValue("class", out object cls) && !Equals(cls, "ai_core")) c.Errors.Add("class must be ai_core");
            c.Name = root.TryGetValue("name", out object name) && name is string s && s.Length > 0 ? s : "";
            if (c.Name.Length == 0 || c.Name.Length > 40) c.Errors.Add("name: 1 to 40 characters");

            if (root.TryGetValue("size_mm", out object size) && size is List<object> dims && dims.Count == 3 && dims.TrueForAll(d => d is double))
            {
                c.WidthMm = (float)(double)dims[0]; c.LengthMm = (float)(double)dims[1]; c.HeightMm = (float)(double)dims[2];
            }
            else c.Errors.Add("size_mm: three numbers [width, length, height]");
            c.MassG = c.Num(root, "mass_g", 0, true);

            Dictionary<string, object> motors = c.Map(root, "motors", true), props = c.Map(root, "props", false),
                battery = c.Map(root, "battery", true), controller = c.Map(root, "controller", false), camera = c.Map(root, "camera", false);
            if (c.Num(motors, "count", 4, false) != 4) c.Errors.Add("motors.count: only 4 motors are supported");
            c.MotorThrustG = c.Num(motors, "max_thrust_g", 0, true);
            c.MotorTimeConstantMs = c.Num(motors, "time_constant_ms", 35f, false);
            c.ArmXMm = c.Num(motors, "arm_x_mm", 0, false);
            c.ArmZMm = c.Num(motors, "arm_z_mm", 0, false);
            c.PropDiameterIn = c.Num(props, "diameter_in", 3f, false);
            c.PropPitchIn = c.Num(props, "pitch_in", 3f, false);
            c.Cells = (int)c.Num(battery, "cells", 0, true);
            c.CapacityMah = c.Num(battery, "capacity_mah", 0, true);
            c.ResistanceMohm = c.Num(battery, "internal_resistance_mohm", 0, false);
            c.MaxCurrentA = c.Num(battery, "max_current_a", 0, false);
            c.RollRate = c.Num(controller, "roll_rate_deg_s", 500f, false);
            c.PitchRate = c.Num(controller, "pitch_rate_deg_s", 500f, false);
            c.YawRate = c.Num(controller, "yaw_rate_deg_s", 400f, false);
            c.MaxAngleDeg = c.Num(controller, "max_angle_deg", 45f, false);
            c.CameraTiltDeg = c.Num(camera, "tilt_deg", 20f, false);
            c.CameraHfovDeg = c.Num(camera, "hfov_deg", 120f, false);

            if (root.TryGetValue("sensors", out object sensors) && sensors is List<object> list)
            {
                foreach (object o in list)
                {
                    string id = o as string;
                    if (id == null || Array.IndexOf(limits.sensors, id) < 0) c.Errors.Add($"sensors: \"{o}\" is not in the catalogue ({string.Join(", ", limits.sensors)})");
                    else if (!c.Sensors.Contains(id)) c.Sensors.Add(id);
                }
            }
            else c.Errors.Add("sensors: a list such as [camera, imu, tof, battery]");
            if (!c.Sensors.Contains("camera") && c.Errors.Count == 0) c.Errors.Add("sensors: a camera is required");
            if (c.Errors.Count > 0) return c;

            // Class limits.
            void Range(string what, float v, float lo, float hi, string unit)
            {
                if (v < lo || v > hi) c.Errors.Add(FormattableString.Invariant($"{what} = {v:0.##} {unit}: allowed {lo:0.##} to {hi:0.##}"));
            }
            Range("width", c.WidthMm, 30f, limits.max_width_mm, "mm");
            Range("length", c.LengthMm, 30f, limits.max_length_mm, "mm");
            Range("height", c.HeightMm, 15f, limits.max_height_mm, "mm");
            Range("mass_g", c.MassG, limits.min_mass_g, limits.max_mass_g, "g");
            Range("battery.cells", c.Cells, 1, limits.max_cells, "");
            Range("battery.capacity_mah", c.CapacityMah, 100f, limits.max_capacity_mah, "mAh");
            Range("motors.time_constant_ms", c.MotorTimeConstantMs, limits.min_motor_time_constant_ms, 200f, "ms");
            Range("controller.roll_rate_deg_s", c.RollRate, 100f, limits.max_rate_deg_s, "deg/s");
            Range("controller.pitch_rate_deg_s", c.PitchRate, 100f, limits.max_rate_deg_s, "deg/s");
            Range("controller.yaw_rate_deg_s", c.YawRate, 100f, limits.max_rate_deg_s, "deg/s");
            Range("controller.max_angle_deg", c.MaxAngleDeg, 10f, limits.max_angle_deg, "deg");
            Range("camera.tilt_deg", c.CameraTiltDeg, limits.min_camera_tilt_deg, limits.max_camera_tilt_deg, "deg");
            Range("camera.hfov_deg", c.CameraHfovDeg, limits.min_camera_hfov_deg, limits.max_camera_hfov_deg, "deg");
            Range("props.diameter_in", c.PropDiameterIn, 1f, 8f, "in");
            if (c.MassG > 0f)
                Range("thrust-to-weight (4 x motors.max_thrust_g / mass_g)", c.ThrustToWeight, limits.min_thrust_to_weight, limits.max_thrust_to_weight, "");
            float half = Mathf.Min(c.WidthMm, c.LengthMm) * 0.5f;
            if (c.ArmXMm < 0f || c.ArmXMm > c.WidthMm * 0.5f || c.ArmZMm < 0f || c.ArmZMm > c.LengthMm * 0.5f)
                c.Errors.Add("motors.arm_x_mm / arm_z_mm: the motors must be inside the declared size");
            if (c.PropDiameterIn * 25.4f > half * 2f) c.Errors.Add("props.diameter_in: the propeller is wider than the drone");
            return c;
        }

        public float ThrustToWeight => MassG > 0f ? 4f * MotorThrustG / MassG : 0f;

        private float Num(Dictionary<string, object> map, string key, float fallback, bool required)
        {
            if (map != null && map.TryGetValue(key, out object v))
            {
                if (v is double d) return (float)d;
                Errors.Add($"{key}: a number is expected");
                return fallback;
            }
            if (required) Errors.Add($"{key}: missing");
            return fallback;
        }

        private Dictionary<string, object> Map(Dictionary<string, object> root, string key, bool required)
        {
            if (root.TryGetValue(key, out object v))
            {
                if (v is Dictionary<string, object> m) return m;
                Errors.Add($"{key}: nested keys are expected");
            }
            else if (required) Errors.Add($"{key}: missing");
            return null;
        }

        /// <summary>Collision sphere radius (m): the drone seen from above, a little inside its corners.</summary>
        public float CollisionRadius => Mathf.Max(0.03f, 0.5f * 0.001f * Mathf.Max(WidthMm, LengthMm) * 0.8f);

        /// <summary>
        /// The flight model's parameters for this drone. Mass, thrust, motor lag, battery and limits come straight from
        /// the file; inertia, drag and controller gains are computed from the size and mass.
        /// </summary>
        public QuadProfile ToProfile()
        {
            float m = MassG * 0.001f, w = WidthMm * 0.001f, l = LengthMm * 0.001f, h = HeightMm * 0.001f;
            float armX = (ArmXMm > 0f ? ArmXMm : WidthMm * 0.5f - PropDiameterIn * 25.4f * 0.25f) * 0.001f;
            float armZ = (ArmZMm > 0f ? ArmZMm : LengthMm * 0.5f - PropDiameterIn * 25.4f * 0.25f) * 0.001f;
            armX = Mathf.Max(armX, 0.015f);
            armZ = Mathf.Max(armZ, 0.015f);
            float thrust = MotorThrustG * 0.001f * 9.81f;

            // Inertia: 55% of the mass as a box in the middle (battery, electronics), 45% as four motors at the arms.
            float body = m * 0.55f, motor = m * 0.45f * 0.25f;
            float bw = w * 0.45f, bl = l * 0.6f, bh = h * 0.8f;
            var inertia = new Vec3(
                body * (bl * bl + bh * bh) / 12f + 4f * motor * armZ * armZ,
                body * (bw * bw + bl * bl) / 12f + 4f * motor * (armX * armX + armZ * armZ),
                body * (bw * bw + bh * bh) / 12f + 4f * motor * armX * armX);

            // Drag: half rho * Cd * area per body axis; the props' discs dominate from above and below.
            const float rho = 1.2f, cd = 1.1f;
            float disc = 4f * Mathf.PI * Mathf.Pow(PropDiameterIn * 0.0254f * 0.5f, 2f);
            var quadratic = new Vec3(0.5f * rho * cd * (l * h * 0.6f), 0.5f * rho * cd * (w * l * 0.35f + disc * 0.6f), 0.5f * rho * cd * (w * h * 0.6f));
            var linear = new Vec3(quadratic.x, quadratic.y, quadratic.z) * 2.2f;

            // Controller gains from how hard the motors can turn the frame, anchored on the 5" race quad's tuning.
            QuadProfile reference = QuadProfile.Race5Inch;
            float refAuthority = reference.Motors.MaxThrustPerMotor * reference.ArmX / reference.Inertia.x;
            float pitchScale = Mathf.Clamp(Mathf.Sqrt(refAuthority / (thrust * armZ / inertia.x)), 0.4f, 2.2f);
            float rollScale = Mathf.Clamp(Mathf.Sqrt(refAuthority / (thrust * armX / inertia.z)), 0.4f, 2.2f);
            float yawTorque = 0.012f + 0.0015f * PropDiameterIn;
            float refYaw = reference.Motors.MaxThrustPerMotor * reference.Motors.YawTorquePerThrust / reference.Inertia.y;
            float yawScale = Mathf.Clamp(Mathf.Sqrt(refYaw / (thrust * yawTorque / inertia.y)), 0.4f, 2.2f);

            float resistance = ResistanceMohm > 0f ? ResistanceMohm * 0.001f : 0.012f * Cells * (850f / Mathf.Max(200f, CapacityMah));
            // Full-throttle current from the power needed for the thrust, if the file doesn't give it.
            float current = MaxCurrentA > 0f ? MaxCurrentA : 4f * MotorThrustG * 0.11f * 14.8f / (Cells * 3.7f);

            return new QuadProfile
            {
                Name = Name, Mass = m, Inertia = inertia, ArmX = armX, ArmZ = armZ,
                Motors = new MotorSettings
                {
                    MaxThrustPerMotor = thrust, YawTorquePerThrust = yawTorque,
                    TimeConstantUp = MotorTimeConstantMs * 0.001f, TimeConstantDown = MotorTimeConstantMs * 0.0012f,
                    IdleFraction = 0.05f, PropPitchSpeed = Mathf.Clamp(PropPitchIn * 0.0254f * 380f, 12f, 70f), ThrottleCurveExpo = 1f,
                },
                Aero = new AeroSettings { LinearDrag = linear, QuadraticDrag = quadratic, AngularDrag = inertia * 0.2f },
                Pid = new PidSettings
                {
                    Pitch = new PidGains(reference.Pid.Pitch.P * pitchScale, reference.Pid.Pitch.I * pitchScale, reference.Pid.Pitch.D * pitchScale),
                    Roll = new PidGains(reference.Pid.Roll.P * rollScale, reference.Pid.Roll.I * rollScale, reference.Pid.Roll.D * rollScale),
                    Yaw = new PidGains(reference.Pid.Yaw.P * yawScale, reference.Pid.Yaw.I * yawScale, 0f),
                    ITermLimit = reference.Pid.ITermLimit, DTermLowpassHz = reference.Pid.DTermLowpassHz, OutputLimit = 1f,
                    AirMode = true, LowThrottleCutoff = 0.05f,
                },
                AngleMode = new AngleModeSettings { MaxAngleDeg = MaxAngleDeg, AngleGain = 10f, MaxOuterRateDegPerSec = Mathf.Min(RollRate, PitchRate) * 0.8f },
                Rates = new RateProfile
                {
                    Type = RatesType.Actual,
                    Roll = new AxisRates(Mathf.Min(120f, RollRate * 0.25f), RollRate, 0.3f),
                    Pitch = new AxisRates(Mathf.Min(120f, PitchRate * 0.25f), PitchRate, 0.3f),
                    Yaw = new AxisRates(Mathf.Min(120f, YawRate * 0.25f), YawRate, 0.3f),
                },
                Battery = new BatterySettings
                {
                    Cells = Cells, CapacityMah = CapacityMah, InternalResistance = resistance, MaxCurrent = current, IdleCurrent = 0.3f + 0.1f * Cells,
                },
            };
        }
    }
}
