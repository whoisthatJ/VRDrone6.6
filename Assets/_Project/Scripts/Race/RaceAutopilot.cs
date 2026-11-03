using GateRush.Flight;
using GateRush.Input;
using GateRush.Vehicle;
using UnityEngine;

namespace GateRush.Race
{
    /// <summary>
    /// Simple Angle-mode autopilot that flies the course through a <see cref="ScriptedInputSource"/>.
    /// Used for automated race smoke tests and as an attract-mode/demo pilot. Not tuned for speed.
    /// </summary>
    [DefaultExecutionOrder(-110)]
    public sealed class RaceAutopilot : MonoBehaviour
    {
        [SerializeField] private RaceSession session;
        [SerializeField] private QuadVehicle vehicle;
        [SerializeField] private ScriptedInputSource pilot;
        [SerializeField] private Gate[] gates = new Gate[0];
        [SerializeField] private float cruiseSpeed = 8f;
        [SerializeField] private float approachDistance = 5f;
        [Header("Stuck watchdog")]
        [Tooltip("Moving less than this (m) for Stuck Seconds counts as stuck.")]
        [SerializeField] private float stuckDistance = 0.6f;
        [SerializeField] private float stuckSeconds = 3f;
        [Tooltip("Rescues allowed at one gate before the autopilot gives up and reports it.")]
        [SerializeField] private int maxRescuesPerGate = 3;

        private bool _armRequested;
        private bool _angleModeSet;
        private Vector3 _stuckAnchor;
        private float _stuckTimer;
        private int _rescueGate = -1, _rescuesAtGate;
        private Transform _rescueSpawn;

        /// <summary>How often the watchdog had to put the quad back in front of a gate.</summary>
        public int Rescues { get; private set; }
        /// <summary>The autopilot could not get through a gate after <see cref="maxRescuesPerGate"/> rescues.</summary>
        public bool GaveUp { get; private set; }
        /// <summary>Last watchdog message (gate, position, what the quad was touching).</summary>
        public string LastReport { get; private set; } = "";

        public void Configure(RaceSession raceSession, QuadVehicle quad, ScriptedInputSource source, Gate[] course)
        {
            session = raceSession;
            vehicle = quad;
            pilot = source;
            gates = course;
        }

        private void Update()
        {
            if (session == null || vehicle == null || pilot == null || gates.Length == 0) return;
            ref readonly QuadState s = ref vehicle.State;

            // Make sure we fly in Angle mode (the source cycles Acro → Angle).
            if (!_angleModeSet && vehicle.LastInput.Mode != FlightMode.Angle)
            {
                pilot.PressMode();
                return;
            }
            _angleModeSet = vehicle.LastInput.Mode == FlightMode.Angle;

            if (!vehicle.LastInput.Arm)
            {
                pilot.Throttle = 0f;
                pilot.Pitch = pilot.Roll = pilot.Yaw = 0f;
                if (!_armRequested && !vehicle.IsCrashed) pilot.PressArm();
                _armRequested = true;
                return;
            }
            _armRequested = false;

            // Online races start on the host's signal: wait on the pad until then.
            if (session.Phase == RacePhase.Countdown || session.Phase == RacePhase.Finished
                || (session.Phase == RacePhase.Idle && session.ExternalStart))
            {
                pilot.Throttle = 0f;
                return;
            }

            Gate gate = gates[session.Timer.NextCheckpoint];
            Vector3 pos = new Vector3(s.Position.x, s.Position.y, s.Position.z);
            if (Watchdog(pos, session.Timer.NextCheckpoint, gate)) return;
            Vector3 vel = new Vector3(s.Velocity.x, s.Velocity.y, s.Velocity.z);
            Vector3 centre = gate.AimPointWorld;
            Vector3 gateFwd = gate.transform.forward;

            // Aim at a point before the gate, then through it once lined up and close.
            // Past the plane without the gate counting = missed: loop back to a longer approach.
            float along = Vector3.Dot(pos - centre, gateFwd);
            // Only commit to the opening once lined up with it: on a bend the quad reaches the last few metres from
            // the side, so keep steering for the approach point until the sideways offset is small.
            float offAxis = Mathf.Abs(Vector3.Dot(pos - centre, Vector3.Cross(Vector3.up, gateFwd).normalized));
            bool lined = offAxis < 0.8f || along > -1.5f;
            Vector3 target;
            if (along >= 0f) target = centre - gateFwd * (approachDistance * 2f);
            else if (along < -approachDistance || !lined) target = centre - gateFwd * approachDistance;
            else target = centre + gateFwd * 4f;

            Vector3 fwd = vehicle.transform.forward;
            Vector3 flatFwd = new Vector3(fwd.x, 0f, fwd.z).normalized;
            Vector3 toTarget = target - pos;
            Vector3 flatTo = new Vector3(toTarget.x, 0f, toTarget.z);
            float headingError = Vector3.SignedAngle(flatFwd, flatTo, Vector3.up);

            Vector3 right = Vector3.Cross(Vector3.up, flatFwd);
            float vForward = Vector3.Dot(vel, flatFwd);
            float vLateral = Vector3.Dot(vel, right);
            float turnFactor = Mathf.Clamp01(1f - Mathf.Abs(headingError) / 60f);
            float targetSpeed = cruiseSpeed * Mathf.Lerp(0.3f, 1f, turnFactor);

            // Default rates give ~670 °/s at full stick; keep turns gentle (≤ ~150 °/s).
            pilot.Yaw = Mathf.Clamp(headingError / 90f, -0.5f, 0.5f);
            pilot.Pitch = Mathf.Clamp(0.12f + 0.06f * (targetSpeed - vForward), -0.3f, 0.45f);
            // Kill sideways drift so the quad goes where it points; on the final approach also centre on the opening.
            float lateralOffset = along < 0f && along >= -approachDistance ? Vector3.Dot(pos - centre, right) : 0f;
            pilot.Roll = Mathf.Clamp(-0.12f * vLateral - 0.2f * lateralOffset, -0.5f, 0.5f);

            float tilt = Mathf.Max(Mathf.Abs(pilot.Pitch), Mathf.Abs(pilot.Roll)) * vehicle.Profile.AngleMode.MaxAngleDeg * Mathf.Deg2Rad;
            // Cascade: altitude error → desired climb rate → throttle.
            float climbTarget = Mathf.Clamp(1.2f * (target.y - pos.y), -2.5f, 2.5f);
            float throttle = vehicle.HoverThrottle / Mathf.Max(0.4f, Mathf.Cos(tilt)) + 0.06f * (climbTarget - vel.y);
            pilot.Throttle = Mathf.Clamp(throttle, 0.06f, 0.9f);
        }

        /// <summary>
        /// Stuck watchdog: if the quad barely moves for <see cref="stuckSeconds"/> (pinned against a frame, hovering in
        /// a corner), respawn it lined up in front of the gate it is trying to reach. Gives up after
        /// <see cref="maxRescuesPerGate"/> rescues at the same gate and reports what it was touching.
        /// </summary>
        /// <returns>True when it acted this frame.</returns>
        private bool Watchdog(Vector3 pos, int next, Gate gate)
        {
            if (GaveUp) return true;
            // A crashed quad doesn't move either, so a crash also ends in a rescue (it never re-arms by itself).
            if (session.WaitingForRelease)
            {
                _stuckAnchor = pos;
                _stuckTimer = 0f;
                return false;
            }
            if ((pos - _stuckAnchor).sqrMagnitude > stuckDistance * stuckDistance)
            {
                _stuckAnchor = pos;
                _stuckTimer = 0f;
                return false;
            }
            _stuckTimer += Time.deltaTime;
            if (_stuckTimer < stuckSeconds) return false;
            _stuckTimer = 0f;

            if (next != _rescueGate)
            {
                _rescueGate = next;
                _rescuesAtGate = 0;
            }
            string touching = "";
            foreach (var c in Physics.OverlapSphere(pos, 0.6f, ~0, QueryTriggerInteraction.Ignore))
                touching += " " + c.transform.root.name + "/" + c.name;
            LastReport = $"{(vehicle.IsCrashed ? "crashed" : "stuck")} before {gate.name} at {pos:F1}, touching:{(touching.Length > 0 ? touching : " nothing")}";

            if (++_rescuesAtGate > maxRescuesPerGate)
            {
                GaveUp = true;
                pilot.Throttle = 0f;
                Debug.LogError($"[GateRush][Autopilot] gave up: {LastReport} (after {maxRescuesPerGate} rescues)");
                return true;
            }

            Rescues++;
            Debug.LogWarning($"[GateRush][Autopilot] rescue {_rescuesAtGate}/{maxRescuesPerGate}: {LastReport}");
            if (_rescueSpawn == null) _rescueSpawn = new GameObject("Autopilot Rescue Spawn").transform;
            gate.GetRespawnPose(out Vector3 rp, out Quaternion rr);
            _rescueSpawn.SetPositionAndRotation(rp, rr);
            var previous = vehicle.SpawnPoint;
            vehicle.SpawnPoint = _rescueSpawn;
            vehicle.Respawn();
            vehicle.SpawnPoint = previous;
            _stuckAnchor = rp;
            return true;
        }

        private void OnDestroy()
        {
            if (_rescueSpawn != null) Destroy(_rescueSpawn.gameObject);
        }
    }
}
