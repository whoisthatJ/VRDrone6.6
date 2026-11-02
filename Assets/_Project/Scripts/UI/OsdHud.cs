using GateRush.Cameras;
using GateRush.Flight;
using GateRush.Vehicle;
using TMPro;
using UnityEngine;

namespace GateRush.UI
{
    /// <summary>
    /// Betaflight-style OSD drawn into the FPV feed: pack voltage and per-cell voltage, mAh used, flight mode and
    /// arm state, armed timer, throttle, speed, altitude, link quality, crosshair, artificial horizon and warnings.
    /// All text updates use TMP SetText with numeric arguments (no allocations).
    /// </summary>
    public sealed class OsdHud : MonoBehaviour
    {
        [SerializeField] private QuadVehicle vehicle;
        [SerializeField] private VtxSignal vtx;
        [SerializeField] private TMP_Text battery;
        [SerializeField] private TMP_Text mode;
        [SerializeField] private TMP_Text timer;
        [SerializeField] private TMP_Text throttle;
        [SerializeField] private TMP_Text motion;
        [SerializeField] private TMP_Text link;
        [SerializeField] private TMP_Text warning;
        [Tooltip("Artificial horizon bar: rotated by roll, moved by pitch.")]
        [SerializeField] private RectTransform horizon;
        [SerializeField] private float horizonPixelsPerDegree = 6f;
        [Tooltip("Crosshair and horizon only make sense in the FPV view; hidden in chase / line of sight.")]
        [SerializeField] private GameObject crosshair;
        [SerializeField] private ViewModeManager views;
        [SerializeField] private float lowCellVoltage = 3.5f;

        private const string ModeAcro = "ACRO";
        private const string ModeAngle = "ANGL";
        private const string ModeHorizon = "HOR";
        private const string Disarmed = "DISARMED";
        private const string LowBattery = "LOW BATTERY";
        private const string LinkLost = "LINK";

        private string _modeShown, _warningShown;
        private float _armedTime;

        private void LateUpdate()
        {
            if (vehicle == null) return;
            ref readonly QuadState s = ref vehicle.State;
            var input = vehicle.LastInput;
            var profile = vehicle.Profile;
            if (s.Armed) _armedTime += Time.deltaTime;

            int cells = Mathf.Max(1, profile.Battery.Cells);
            float volts = s.BatteryVoltage > 0f ? s.BatteryVoltage : BatteryModel.CellFull * cells;
            float cell = volts / cells;
            if (battery != null) battery.SetText("{0:1}V {1:2}\n{2:0}MAH", volts, cell, s.BatteryUsedMah);

            string m = !s.Armed ? Disarmed : input.Mode == FlightMode.Angle ? ModeAngle : input.Mode == FlightMode.Horizon ? ModeHorizon : ModeAcro;
            SetIfChanged(mode, m, ref _modeShown);

            if (timer != null)
            {
                int total = (int)_armedTime;
                int sec = total % 60;
                timer.SetText("{0:0}:{1:0}{2:0}", total / 60, sec / 10, sec % 10);
            }
            if (throttle != null) throttle.SetText("THR {0:0}%", input.Throttle * 100f);
            if (motion != null) motion.SetText("{0:0}KM/H  ALT {1:0}M", s.Velocity.Magnitude * 3.6f, s.Position.y);
            float lq = vtx != null ? vtx.LinkQuality : 1f;
            if (link != null) link.SetText("LQ {0:0}", lq * 100f);

            bool blink = (Time.time * 2f) % 1f < 0.6f;
            string w = cell < lowCellVoltage && s.Armed && blink ? LowBattery : lq < 0.3f && blink ? LinkLost : "";
            SetIfChanged(warning, w, ref _warningShown);

            bool fpv = views == null || views.Mode == ViewMode.Fpv;
            if (crosshair != null && crosshair.activeSelf != fpv) crosshair.SetActive(fpv);
            if (horizon != null && horizon.gameObject.activeSelf != fpv) horizon.gameObject.SetActive(fpv);
            if (horizon != null && fpv)
            {
                Vector3 fwd = vehicle.transform.forward;
                Vector3 right = vehicle.transform.right;
                float pitch = Mathf.Asin(Mathf.Clamp(fwd.y, -1f, 1f)) * Mathf.Rad2Deg;   // nose up +
                float roll = Mathf.Asin(Mathf.Clamp(right.y, -1f, 1f)) * Mathf.Rad2Deg;   // left wing down +
                horizon.localRotation = Quaternion.Euler(0f, 0f, roll);
                horizon.anchoredPosition = new Vector2(0f, -pitch * horizonPixelsPerDegree);
            }
        }

        /// <summary>Restarts the armed timer (new race / respawn at the pad).</summary>
        public void ResetTimer() => _armedTime = 0f;

        private static void SetIfChanged(TMP_Text t, string value, ref string shown)
        {
            if (t == null || ReferenceEquals(value, shown)) return;
            shown = value;
            t.SetText(value);
        }
    }
}
