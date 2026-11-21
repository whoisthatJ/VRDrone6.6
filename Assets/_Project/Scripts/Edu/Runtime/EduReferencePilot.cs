using System.Collections;
using System.Collections.Generic;
using Ushqyr.Flight;
using Ushqyr.Input;
using Ushqyr.Vehicle;
using UnityEngine;

namespace Ushqyr.Edu
{
    /// <summary>
    /// Flies an activity the way its file says a good flight goes (the "reference" list of the activity), through the
    /// autopilot or by moving the sticks, on the same flight model a student flies. It is how an activity's reference
    /// flight is made (EDU_PLAN F1: every activity ships a flight that scores 90 or more), and it can be made again
    /// whenever the exercise changes.
    /// <para>Commands, in order: <c>takeoff</c> (height), <c>goto</c> ([x, y, z] in the activity's frame, with
    /// <c>heading</c> and <c>speed</c>), <c>heading</c> alone (turn on the spot), <c>orbit</c> ([x, z] with
    /// <c>radius</c> and <c>speed</c>), <c>land</c>, <c>gimbal</c> (tilt), <c>zoom</c>, <c>photo</c>, <c>sticks</c>
    /// ({throttle, yaw, pitch, roll}). Each waits for <c>wait</c> seconds, <c>until_step</c> N steps are done, or
    /// <c>until_landed</c>, and gives up after a minute.</para>
    /// </summary>
    public sealed class EduReferencePilot : MonoBehaviour
    {
        private EduActivityRunner _runner;
        private QuadVehicle _vehicle;
        private ScriptedInputSource _sticks;
        private PilotInputSource _before;

        public bool Done { get; private set; }
        /// <summary>What stopped the flight before its end, or "" when it went through.</summary>
        public string Problem { get; private set; } = "";

        /// <summary>Starts the attempt and flies it. False when the activity has no "reference" list.</summary>
        public static EduReferencePilot Fly(EduActivityRunner runner, QuadVehicle vehicle)
        {
            if (runner == null || vehicle == null || !(runner.Activity.Raw.TryGetValue("reference", out object list) && list is List<object> commands) || commands.Count == 0)
                return null;
            EduReferencePilot pilot = runner.gameObject.AddComponent<EduReferencePilot>();
            pilot._runner = runner;
            pilot._vehicle = vehicle;
            pilot.StartCoroutine(pilot.Run(commands));
            return pilot;
        }

        private void OnDestroy()
        {
            if (_sticks == null) return;
            if (_vehicle != null && _vehicle.InputSource == _sticks) _vehicle.InputSource = _before;
            Destroy(_sticks);
        }

        private IEnumerator Run(List<object> commands)
        {
            _runner.StartFlying();
            yield return null;
            EduFlight flight = _vehicle.GetComponent<EduFlight>();
            FlightFrame frame = _runner.Frame;
            Vec3 target = _vehicle.State.Position;
            float heading = frame.HeadingDeg;
            bool airborne = false;

            foreach (object item in commands)
            {
                if (!(item is Dictionary<string, object> c)) continue;
                if (_runner.State != EduActivityRunner.Phase.Flying) break;

                // The camera: tilt and zoom at once, a photo after the "settle" seconds the command gives it.
                EduGimbal gimbal = _vehicle.GetComponent<EduGimbal>();
                if (gimbal != null && gimbal.enabled)
                {
                    if (c.ContainsKey("gimbal")) gimbal.TiltDeg = (float)Json.Num(c, "gimbal", 0);
                    if (c.ContainsKey("zoom")) gimbal.Zoom = Mathf.Clamp((float)Json.Num(c, "zoom", 1), 1f, EduGimbal.MaxZoom);
                    if (Json.Flag(c, "photo")) gimbal.TakePhoto();
                }

                if (c.ContainsKey("sticks"))
                {
                    if (_sticks == null)
                    {
                        // A source added while the program runs is used only when the drone is pointed at it.
                        _sticks = _vehicle.gameObject.AddComponent<ScriptedInputSource>();
                        _before = _vehicle.InputSource;
                        _vehicle.InputSource = _sticks;
                    }
                    Dictionary<string, object> s = Json.Obj(c, "sticks") ?? new Dictionary<string, object>();
                    _sticks.Throttle = (float)Json.Num(s, "throttle", 0.5);
                    _sticks.Yaw = (float)Json.Num(s, "yaw", 0);
                    _sticks.Pitch = (float)Json.Num(s, "pitch", 0);
                    _sticks.Roll = (float)Json.Num(s, "roll", 0);
                }
                else
                {
                    if (flight == null)
                    {
                        Problem = "the drone has no flight layer (the activity needs an assist for a flown reference)";
                        break;
                    }
                    flight.PilotPresent = false;
                    flight.ArmRequested = true;
                    if (c.ContainsKey("heading")) heading = frame.HeadingDeg + (float)Json.Num(c, "heading", 0);
                    if (c.ContainsKey("takeoff"))
                    {
                        float height = (float)Json.Num(c, "takeoff", 2);
                        QuadState now = _vehicle.State;
                        target = new Vec3(now.Position.x, frame.Y + height, now.Position.z);
                        flight.Autopilot.Takeoff(now, height, frame.Y);
                        airborne = true;
                    }
                    else if (c.ContainsKey("goto") && Json.Arr(c, "goto") is List<object> p && p.Count == 3 && p[0] is double x && p[1] is double y && p[2] is double z)
                    {
                        frame.ToWorld((float)x, (float)y, (float)z, out float wx, out float wy, out float wz);
                        target = new Vec3(wx, wy, wz);
                        flight.Autopilot.GoTo(target, heading, (float)Json.Num(c, "speed", 0));
                    }
                    else if (c.ContainsKey("orbit") && Json.Arr(c, "orbit") is List<object> o && o.Count == 2 && o[0] is double ox && o[1] is double oz)
                    {
                        // Round a point at the present height, nose on the point; a negative speed goes the other way.
                        frame.ToWorld((float)ox, 0f, (float)oz, out float cx, out _, out float cz);
                        QuadState now = _vehicle.State;
                        flight.Autopilot.Orbit(now, new Vec3(cx, now.Position.y, cz), (float)Json.Num(c, "radius", 5), (float)Json.Num(c, "speed", 2));
                        target = now.Position;
                    }
                    else if (Json.Flag(c, "land")) flight.Autopilot.Land();
                    else if (c.ContainsKey("heading") && airborne && !c.ContainsKey("photo") && !c.ContainsKey("gimbal")) flight.Autopilot.GoTo(target, heading);
                }

                // Wait for what the command says, at most a minute.
                float waited = 0f, wait = (float)Json.Num(c, "wait", 0);
                int untilStep = (int)Json.Num(c, "until_step", -1);
                bool untilLanded = Json.Flag(c, "until_landed");
                while (_runner != null && _runner.State == EduActivityRunner.Phase.Flying && waited < 60f)
                {
                    if (untilStep >= 0)
                    {
                        if (_runner.Steps != null && _runner.Steps.Done >= untilStep) break;
                    }
                    else if (untilLanded)
                    {
                        if (flight != null && flight.Autopilot.Landed) break;
                    }
                    else if (waited >= wait) break;
                    waited += Time.deltaTime;
                    yield return null;
                }
                if (waited >= 60f)
                {
                    Problem = "a command did not finish in a minute: " + Json.Write(c);
                    break;
                }
            }
            if (flight != null)
            {
                flight.PilotPresent = true;
                flight.ArmRequested = false;
            }
            Done = true;
        }
    }
}
