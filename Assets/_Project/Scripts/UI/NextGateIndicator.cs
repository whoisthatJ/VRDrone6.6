using GateRush.Race;
using GateRush.Vehicle;
using TMPro;
using UnityEngine;

namespace GateRush.UI
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

        private void Awake() => _canvas = (RectTransform)GetComponentInParent<Canvas>().transform;

        private void LateUpdate()
        {
            var gate = race != null && race.isActiveAndEnabled ? race.NextGate : null;
            if (_camera == null || !_camera.isActiveAndEnabled) _camera = Camera.main;
            if (gate == null || _camera == null)
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
                if (metres != _shownMetres)
                {
                    _shownMetres = metres;
                    distance.SetText("{0}M", metres);
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
