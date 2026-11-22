using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Ushqyr.Net;

namespace Ushqyr.Game
{
    /// <summary>
    /// The race director's view of a LAN race (EDU_PLAN §5): a camera of its own that follows the leading pilot from
    /// behind and above. Tab goes to the next pilot, Backspace back to "the leader, whoever that is", and O gives the
    /// view over the start. It draws over the director's own (unused) view.
    /// </summary>
    public sealed class DirectorCamera : MonoBehaviour
    {
        private OnlineRaceDriver _driver;
        private Transform _start;
        private Camera _camera;
        private int _pick = -1;
        private bool _overview;

        /// <summary>The pilot being followed; null on the overview or before anyone flies.</summary>
        public NetPilot Following { get; private set; }

        public void Configure(OnlineRaceDriver driver, Transform start)
        {
            _driver = driver;
            _start = start;
        }

        private void Start()
        {
            var go = new GameObject("Director Camera");
            go.transform.SetParent(transform, false);
            _camera = go.AddComponent<Camera>();
            _camera.depth = 60f;
            _camera.fieldOfView = 70f;
            _camera.nearClipPlane = 0.1f;
            _camera.farClipPlane = 3000f;
            Overview(true);
        }

        private void Overview(bool atOnce)
        {
            if (_start == null) return;
            Vector3 pos = _start.position - _start.forward * 14f + Vector3.up * 9f;
            Quaternion rot = Quaternion.LookRotation(_start.position + _start.forward * 12f - pos, Vector3.up);
            float k = atOnce ? 1f : 1f - Mathf.Exp(-3f * Time.unscaledDeltaTime);
            _camera.transform.SetPositionAndRotation(Vector3.Lerp(_camera.transform.position, pos, k), Quaternion.Slerp(_camera.transform.rotation, rot, k));
        }

        private void LateUpdate()
        {
            if (_camera == null || _driver == null) return;
            Keyboard kb = Keyboard.current;
            var views = new List<RemotePilotView>();
            foreach (RemotePilotView v in _driver.Views)
                if (v != null && v.Pilot != null) views.Add(v);
            views.Sort((a, b) => a.Pilot.Slot.CompareTo(b.Pilot.Slot));
            if (kb != null)
            {
                if (kb.tabKey.wasPressedThisFrame && views.Count > 0) { _pick = (_pick + 1) % views.Count; _overview = false; }
                if (kb.backspaceKey.wasPressedThisFrame) { _pick = -1; _overview = false; }
                if (kb.oKey.wasPressedThisFrame) _overview = !_overview;
            }

            RemotePilotView target = null;
            if (!_overview)
            {
                if (_pick >= 0 && _pick < views.Count) target = views[_pick];
                else
                    foreach (RemotePilotView v in views)
                        if (v.Visible && (target == null || v.Pilot.Progress > target.Pilot.Progress)) target = v;
            }
            // Before the start everyone is on the grid: the view over the start shows them all.
            if (target == null || !target.Visible || target.Pilot.Progress <= 0f)
            {
                Following = null;
                Overview(false);
                return;
            }
            Following = target.Pilot;
            Transform t = target.transform;
            Vector3 back = Vector3.ProjectOnPlane(t.forward, Vector3.up);
            if (back.sqrMagnitude < 0.01f) back = _camera.transform.forward;
            Vector3 pos = t.position - back.normalized * 4.5f + Vector3.up * 1.8f;
            float k = 1f - Mathf.Exp(-6f * Time.unscaledDeltaTime);
            _camera.transform.position = Vector3.Lerp(_camera.transform.position, pos, k);
            _camera.transform.rotation = Quaternion.Slerp(_camera.transform.rotation, Quaternion.LookRotation(t.position + Vector3.up * 0.3f - _camera.transform.position, Vector3.up), k);
        }
    }
}
