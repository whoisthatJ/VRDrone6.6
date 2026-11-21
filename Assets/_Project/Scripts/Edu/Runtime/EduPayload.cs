using System.Globalization;
using Ushqyr.Flight;
using Ushqyr.Vehicle;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Ushqyr.Edu
{
    /// <summary>
    /// The delivery drone's load (EDU_PLAN F3): a box with mass under the drone. While it hangs there the drone is
    /// heavier and slower to turn; released (key B), the drone is light again and the box falls as an ordinary body
    /// and comes to rest somewhere. Both moments are events of the flight, so a delivery can be scored on where the
    /// box ended up. A new start (respawn) hangs a new box on the hook.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EduPayload : MonoBehaviour
    {
        private QuadVehicle _vehicle;
        private Transform _box;
        private QuadProfile _empty;
        private float _mass = 1f;
        private GameObject _dropped;
        private Rigidbody _body;
        private float _still;
        private bool _reported;

        public bool Attached { get; private set; }
        /// <summary>A failure: the release does not open.</summary>
        public bool Jammed { get; set; }
        /// <summary>Where the last dropped box came to rest; null while it hangs or is still moving.</summary>
        public Vector3? RestedAt { get; private set; }
        public float Mass => _mass;

        /// <summary>Hangs a box of <paramref name="kilograms"/> on the drone; <paramref name="box"/> is the box of the model.</summary>
        public static EduPayload Attach(QuadVehicle vehicle, Transform box, float kilograms = 1f)
        {
            if (vehicle == null) return null;
            EduPayload payload = vehicle.GetComponent<EduPayload>();
            if (payload == null)
            {
                payload = vehicle.gameObject.AddComponent<EduPayload>();
                vehicle.Respawned += payload.Reload;
            }
            payload._vehicle = vehicle;
            payload._box = box;
            payload._mass = Mathf.Max(0f, kilograms);
            payload._empty = vehicle.Profile; // as configured for the empty drone
            payload.enabled = true;
            payload.Attached = false;
            payload.Reload();
            return payload;
        }

        /// <summary>Takes the payload off for good (another drone was chosen).</summary>
        public static void Detach(QuadVehicle vehicle)
        {
            EduPayload payload = vehicle != null ? vehicle.GetComponent<EduPayload>() : null;
            if (payload == null || !payload.enabled) return;
            payload.enabled = false;
            payload.Attached = false;
            if (payload._dropped != null) Destroy(payload._dropped);
        }

        private void OnDestroy()
        {
            if (_vehicle != null) _vehicle.Respawned -= Reload;
            if (_dropped != null) Destroy(_dropped);
        }

        /// <summary>A new box on the hook: the drone is heavy again.</summary>
        public void Reload()
        {
            if (!enabled || _vehicle == null) return;
            if (_dropped != null) Destroy(_dropped);
            _dropped = null;
            _body = null;
            RestedAt = null;
            if (_box != null) _box.gameObject.SetActive(true);
            if (!Attached) _vehicle.Configure(EduDrones.WithPayload(_empty, _mass), _vehicle.Collision);
            Attached = true;
        }

        /// <summary>Opens the release. False when nothing hangs there or the release is jammed.</summary>
        public bool Drop()
        {
            if (!enabled || !Attached) return false;
            EduRecorder recorder = GetComponent<EduRecorder>();
            if (Jammed)
            {
                if (recorder != null) recorder.Event("drop_jammed");
                return false;
            }
            Attached = false;
            _vehicle.Configure(_empty, _vehicle.Collision);

            Vector3 at = _box != null ? _box.position : transform.position + Vector3.down * 0.17f;
            Quaternion facing = _box != null ? _box.rotation : transform.rotation;
            if (_box != null)
            {
                // The falling box is a copy; the model's own box only hides, ready for the next start.
                _dropped = Instantiate(_box.gameObject, at, facing);
                _box.gameObject.SetActive(false);
            }
            else _dropped = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _dropped.name = "Dropped Payload";
            _dropped.transform.SetParent(null, true);
            _dropped.transform.SetPositionAndRotation(at, facing);
            // Out of the way of the drone's own collision sweep, which looks at track geometry only.
            foreach (Transform t in _dropped.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 2;
            foreach (Collider c in _dropped.GetComponentsInChildren<Collider>()) Destroy(c);
            var collider = _dropped.AddComponent<BoxCollider>();
            collider.size = _box != null ? new Vector3(0.18f, 0.16f, 0.18f) : Vector3.one * 0.18f;
            _body = _dropped.AddComponent<Rigidbody>();
            _body.mass = Mathf.Max(0.05f, _mass);
            _body.linearDamping = 0.05f;
            _body.angularDamping = 0.5f;
            _body.collisionDetectionMode = CollisionDetectionMode.Continuous;
            ref readonly QuadState s = ref _vehicle.State;
            _body.linearVelocity = new Vector3(s.Velocity.x, s.Velocity.y, s.Velocity.z);
            _still = 0f;
            _reported = false;
            if (recorder != null) recorder.Event("drop", Where(at));
            return true;
        }

        private static string Where(Vector3 p) => string.Format(CultureInfo.InvariantCulture, "{0:0.00};{1:0.00};{2:0.00}", p.x, p.y, p.z);

        private void Update()
        {
            Keyboard kb = Keyboard.current;
            if (kb != null && EduReplayViewer.Current == null && kb.bKey.wasPressedThisFrame) Drop();

            if (_body == null || _reported) return;
            // At rest: slow for half a second (a box can sit on a slope without ever "sleeping").
            _still = _body.linearVelocity.sqrMagnitude < 0.01f ? _still + Time.deltaTime : 0f;
            if (_still < 0.5f && !_body.IsSleeping()) return;
            _reported = true;
            RestedAt = _body.position;
            EduRecorder recorder = GetComponent<EduRecorder>();
            if (recorder != null) recorder.Event("payload_landed", Where(_body.position));
        }
    }
}
