using GateRush.Flight;
using GateRush.Vehicle;
using TMPro;
using UnityEngine;

namespace GateRush.UI
{
    /// <summary>
    /// Debug overlay: arm/mode, sticks, setpoint vs gyro per axis, motor bars, speed/altitude.
    /// Uses TMP SetText with numeric arguments so it doesn't allocate per frame.
    /// </summary>
    public sealed class FlightDebugOverlay : MonoBehaviour
    {
        private const float RadToDeg = 57.29578f;

        [SerializeField] private QuadVehicle vehicle;
        [SerializeField] private TMP_Text status;
        [SerializeField] private TMP_Text sticks;
        [SerializeField] private TMP_Text rates;
        [SerializeField] private TMP_Text motion;
        [Tooltip("Bar transforms scaled on Y by motor command (RR, FR, RL, FL). Pivot at the bottom.")]
        [SerializeField] private RectTransform[] motorBars = new RectTransform[4];

        private string _lastStatus;

        private void LateUpdate()
        {
            if (vehicle == null) return;
            ref readonly QuadState s = ref vehicle.State;
            PilotInput input = vehicle.LastInput;

            string statusText = StatusText(s, input, vehicle.IsCrashed);
            if (status != null && !ReferenceEquals(statusText, _lastStatus))
            {
                status.SetText(statusText);
                _lastStatus = statusText;
            }
            if (sticks != null)
                sticks.SetText("THR {0:2}  YAW {1:2}  PIT {2:2}  ROL {3:2}", input.Throttle, input.Yaw, input.Pitch, input.Roll);
            if (rates != null)
                rates.SetText("deg/s set/gyro  P {0:0}/{1:0}  R {2:0}/{3:0}  Y {4:0}/{5:0}",
                    s.Setpoint.x * RadToDeg, s.AngularVelocity.x * RadToDeg,
                    -s.Setpoint.z * RadToDeg, -s.AngularVelocity.z * RadToDeg,
                    s.Setpoint.y * RadToDeg, s.AngularVelocity.y * RadToDeg);
            if (motion != null)
                motion.SetText("{0:1} km/h  ALT {1:1} m  IMPACT {2:1} m/s",
                    s.Velocity.Magnitude * 3.6f, s.Position.y, vehicle.LastImpactSpeed);

            for (int i = 0; i < motorBars.Length && i < 4; i++)
            {
                if (motorBars[i] == null) continue;
                motorBars[i].localScale = new Vector3(1f, Mathf.Max(0.01f, s.MotorCommand[i]), 1f);
            }
        }

        private static string StatusText(in QuadState s, in PilotInput input, bool crashed)
        {
            if (crashed) return "CRASHED";
            if (!s.Armed) return "DISARMED  (throttle low + Space/Start to arm)";
            switch (input.Mode)
            {
                case FlightMode.Angle: return "ARMED  ANGLE";
                case FlightMode.Horizon: return "ARMED  HORIZON";
                default: return "ARMED  ACRO";
            }
        }
    }
}
