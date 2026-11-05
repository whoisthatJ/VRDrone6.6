using Ushqyr.AI;
using Ushqyr.Cameras;
using Ushqyr.Garage;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Ushqyr.Game
{
    /// <summary>
    /// "Watch winner replay" (P4): after a race with bots, R (or gamepad Y) plays the winner's recorded flight with a
    /// chase camera; any key ends it. The winner's own model flies the recording (a stand-in model when the player won).
    /// </summary>
    public sealed class WinnerReplay : MonoBehaviour
    {
        private BotRace _bots;
        private ViewModeManager _views;
        private QuadModelMaterials _materials;
        private Transform _model;
        private bool _ownModel;
        private Vector3 _restorePos;
        private Quaternion _restoreRot;
        private BotRace.Racer _racer;
        private float _t;
        private Vector3 _camVel;

        public bool Playing { get; private set; }

        public void Configure(BotRace bots, ViewModeManager views, QuadModelMaterials materials)
        {
            _bots = bots;
            _views = views;
            _materials = materials;
        }

        /// <summary>True when a finished race has a winner recording to show.</summary>
        public bool Available => _bots != null && _bots.Active && _bots.Winner != null && _bots.Winner.TrackPositions.Count > 10;

        public void Play()
        {
            if (!Available || Playing || _views == null) return;
            _racer = _bots.Winner;
            if (_racer.IsPlayer || _racer.Visual == null)
            {
                var build = BotRoster.ReferenceBuild(SizeClass.Five).Clone();
                build.ColorId = "color-white";
                _model = QuadModelBuilder.Build(build, _materials, null).Root.transform;
                _ownModel = true;
            }
            else
            {
                _model = _racer.Visual;
                _restorePos = _model.position;
                _restoreRot = _model.rotation;
                _ownModel = false;
            }
            _bots.enabled = false;
            _t = 0f;
            _views.SetFreeCamera(true);
            var cam = _views.OutputCamera.transform;
            cam.position = _racer.TrackPositions[0] + Vector3.up * 2f - Vector3.forward * 3f;
            Playing = true;
        }

        public void Stop()
        {
            if (!Playing) return;
            Playing = false;
            if (_ownModel && _model != null) Destroy(_model.gameObject);
            else if (_model != null) _model.SetPositionAndRotation(_restorePos, _restoreRot);
            _model = null;
            if (_bots != null) _bots.enabled = true;
            if (_views != null) _views.SetFreeCamera(false);
        }

        private void Update()
        {
            if (!Playing)
            {
                var k = Keyboard.current;
                var g = Gamepad.current;
                bool ask = (k != null && k.rKey.wasPressedThisFrame) || (g != null && g.buttonNorth.wasPressedThisFrame);
                if (ask && Available && _bots.Winner.Finished) Play();
                return;
            }
            if (AnyKey())
            {
                Stop();
                return;
            }
            _t += Time.deltaTime;
            var pos = _racer.TrackPositions;
            var rot = _racer.TrackRotations;
            float f = _t / BotRace.RecordInterval;
            int i = (int)f;
            if (i >= pos.Count - 1)
            {
                Stop();
                return;
            }
            float u = f - i;
            Vector3 p = Vector3.Lerp(pos[i], pos[i + 1], u);
            Quaternion q = Quaternion.Slerp(rot[i], rot[i + 1], u);
            _model.SetPositionAndRotation(p, q);

            // Chase camera: behind the direction of travel, a little above, smoothed.
            Vector3 dir = pos[Mathf.Min(i + 4, pos.Count - 1)] - pos[i];
            if (dir.sqrMagnitude < 1e-3f) dir = q * Vector3.forward;
            dir.Normalize();
            Vector3 want = p - dir * 3.2f + Vector3.up * 1.1f;
            var cam = _views.OutputCamera.transform;
            cam.position = Vector3.SmoothDamp(cam.position, want, ref _camVel, 0.12f);
            cam.rotation = Quaternion.LookRotation(p + dir * 2f - cam.position, Vector3.up);
        }

        private bool AnyKey()
        {
            var k = Keyboard.current;
            if (k != null && k.anyKey.wasPressedThisFrame && !k.rKey.wasPressedThisFrame) return true;
            var g = Gamepad.current;
            return g != null && (g.buttonSouth.wasPressedThisFrame || g.buttonEast.wasPressedThisFrame || g.startButton.wasPressedThisFrame);
        }

        private void OnDisable() => Stop();
    }
}
