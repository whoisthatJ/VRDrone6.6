using Ushqyr.Race;
using Ushqyr.Vehicle;
using TMPro;
using UnityEngine;

namespace Ushqyr.UI
{
    /// <summary>
    /// Screen-space marker for the next gate: a bracket on the gate with its distance while it is in view, an arrow at
    /// the screen edge pointing towards it while it is not. Works with whichever camera is rendering (FPV, chase, LOS).
    /// </summary>
    public sealed class NextGateIndicator : MonoBehaviour
    {
        [SerializeField] private RaceSession race;
        [SerializeField] private QuadVehicle vehicle;
        [Tooltip("Shown on the gate while it is on screen.")]
        [SerializeField] private RectTransform marker;
        [Tooltip("Shown at the screen edge (rotated to point at the gate) while it is off screen.")]
        [SerializeField] private RectTransform arrow;
        [SerializeField] private TMP_Text distance;
        [Tooltip("Distance of the edge arrow from the screen border, in canvas pixels.")]
        [SerializeField] private float edgeMargin = 90f;

        private RectTransform _canvas;
        private Camera _camera;
        private int _shownMetres = -1;

        private int _shownDy;

        private void Awake() => _canvas = (RectTransform)GetComponentInParent<Canvas>().transform;

        private void LateUpdate()
        {
            var gate = race != null && race.isActiveAndEnabled ? race.NextGate : null;
            if (_camera == null || !_camera.isActiveAndEnabled) _camera = Camera.main;
            // Route guidance (from the race): Off hides it; Standard shows the edge arrow only after a missed gate; Trainer always.
            var guidance = race != null ? race.Guidance : Ushqyr.Core.RouteGuidanceLevel.Standard;
            if (gate == null || _camera == null || guidance == Ushqyr.Core.RouteGuidanceLevel.Off)
            {
                Show(false, false);
                return;
            }

            Vector3 target = gate.AimPointWorld;
            Vector3 view = _camera.WorldToViewportPoint(target);
            bool onScreen = view.z > 0f && view.x > 0.02f && view.x < 0.98f && view.y > 0.02f && view.y < 0.98f;
            Vector2 size = _canvas.rect.size;

            if (onScreen)
            {
                Show(true, false);
                marker.anchoredPosition = new Vector2((view.x - 0.5f) * size.x, (view.y - 0.5f) * size.y);
            }
            else if (guidance != Ushqyr.Core.RouteGuidanceLevel.Trainer && race.Now >= race.WrongGateUntil)
            {
                Show(false, false);
                return;
            }
            else
            {
                Show(false, true);
                // Direction from the screen centre; behind the camera the projection is mirrored.
                Vector2 dir = new Vector2(view.x - 0.5f, view.y - 0.5f);
                if (view.z < 0f) dir = -dir;
                if (dir.sqrMagnitude < 1e-6f) dir = Vector2.down;
                dir.Normalize();
                Vector2 half = size * 0.5f - new Vector2(edgeMargin, edgeMargin);
                float scale = Mathf.Min(half.x / Mathf.Max(Mathf.Abs(dir.x), 1e-4f), half.y / Mathf.Max(Mathf.Abs(dir.y), 1e-4f));
                arrow.anchoredPosition = dir * scale;
                arrow.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f);
            }

            if (distance != null)
            {
                Vector3 from = vehicle != null ? vehicle.transform.position : _camera.transform.position;
                int metres = Mathf.RoundToInt(Vector3.Distance(from, target));
                // P3: how far above (↑) or below (↓) the gate is, once that matters (vertical routes).
                int dy = Mathf.RoundToInt(target.y - from.y);
                if (Mathf.Abs(dy) < 5) dy = 0;
                if (metres != _shownMetres || dy != _shownDy)
                {
                    _shownMetres = metres;
                    _shownDy = dy;
                    if (dy > 0) distance.SetText("{0:0}M ↑{1:0}", (float)metres, (float)dy);
                    else if (dy < 0) distance.SetText("{0:0}M ↓{1:0}", (float)metres, (float)-dy);
                    else distance.SetText("{0}M", metres);
                }
                distance.rectTransform.anchoredPosition = (onScreen ? marker.anchoredPosition : arrow.anchoredPosition) + new Vector2(0f, -56f);
            }
        }

        private void Show(bool showMarker, bool showArrow)
        {
            if (marker != null && marker.gameObject.activeSelf != showMarker) marker.gameObject.SetActive(showMarker);
            if (arrow != null && arrow.gameObject.activeSelf != showArrow) arrow.gameObject.SetActive(showArrow);
            if (distance != null && distance.gameObject.activeSelf != (showMarker || showArrow)) distance.gameObject.SetActive(showMarker || showArrow);
        }
    }
}
