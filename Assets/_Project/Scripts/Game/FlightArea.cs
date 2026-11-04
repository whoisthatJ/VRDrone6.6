using System.Collections.Generic;
using Ushqyr.Track;
using Ushqyr.Vehicle;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Ushqyr.Game
{
    /// <summary>
    /// E7: the flyable area of an open venue and free flight.
    /// <list type="bullet">
    /// <item>Soft boundary: past <see cref="Radius"/> − <see cref="WarnMargin"/> from the origin, or above
    /// <see cref="Ceiling"/> − 10 m, the HUD warns; staying past the hard edge for <see cref="ResetSeconds"/> respawns
    /// the quad (at the last checkpoint in a race, at the current spawn in free flight).</item>
    /// <item>Free flight: several named spawn points, switched with F / gamepad D-pad up; T resets to the current one.
    /// A spawn above the ground holds the quad in a hover until the pilot arms and throttles up.</item>
    /// </list>
    /// </summary>
    public sealed class FlightArea : MonoBehaviour
    {
        public const float DefaultRadius = 1200f, DefaultCeiling = 150f, WarnMargin = 100f, ResetSeconds = 2f;
        private const float ReleaseThrottle = 0.12f;

        private QuadVehicle _vehicle;
        private readonly List<(Transform point, string name)> _spawns = new List<(Transform, string)>();
        private int _spawnIndex;
        private float _outside;
        private bool _holding;

        /// <summary>The area of the loaded track, or null when it has no boundary and isn't free flight.</summary>
        public static FlightArea Current { get; private set; }

        public float Radius { get; private set; } = DefaultRadius;
        public float Ceiling { get; private set; } = DefaultCeiling;
        public bool Bounded { get; private set; }
        public bool FreeFlight { get; private set; }
        /// <summary>In the warning band or beyond: the HUD tells the pilot to turn back.</summary>
        public bool Warning { get; private set; }
        public string SpawnName => _spawns.Count > 0 ? _spawns[_spawnIndex].name : "";
        public int SpawnCount => _spawns.Count;
        /// <summary>Held at an aerial spawn until armed and throttled up.</summary>
        public bool Holding => _holding;

        /// <summary>Sets the area up for a loaded track (or turns it off).</summary>
        public void Configure(QuadVehicle vehicle, TrackData data, Transform spawn, bool bounded)
        {
            _vehicle = vehicle;
            Bounded = bounded;
            FreeFlight = data.FreeFlight;
            Radius = DefaultRadius;
            Ceiling = DefaultCeiling;
            foreach (var s in _spawns)
                if (s.point != null && s.point != spawn) Destroy(s.point.gameObject);
            _spawns.Clear();
            _spawnIndex = 0;
            _outside = 0f;
            Warning = false;
            if (FreeFlight)
            {
                _spawns.Add((spawn, string.IsNullOrEmpty(data.Spawn.Name) ? data.Name : data.Spawn.Name));
                foreach (var e in data.ExtraSpawns)
                {
                    var t = new GameObject("Free Flight Spawn " + e.Name).transform;
                    t.SetParent(spawn.parent, false);
                    t.SetPositionAndRotation(e.Position, Quaternion.Euler(0f, e.Yaw, 0f));
                    _spawns.Add((t, e.Name));
                }
            }
            enabled = Bounded || FreeFlight;
            Current = enabled ? this : null;
        }

        private void OnDisable()
        {
            if (Current == this) Current = null;
            if (_holding && _vehicle != null) _vehicle.HoldAtSpawn = false;
            _holding = false;
        }

        private void OnEnable()
        {
            if (Bounded || FreeFlight) Current = this;
        }

        private void Update()
        {
            if (_vehicle == null) return;
            if (FreeFlight) FreeFlightInput();

            if (_holding)
            {
                _vehicle.HoldAtSpawn = true;
                if (_vehicle.LastInput.Arm && _vehicle.LastInput.Throttle > ReleaseThrottle)
                {
                    _holding = false;
                    _vehicle.HoldAtSpawn = false;
                }
            }

            if (!Bounded) return;
            var p = _vehicle.transform.position;
            float r = new Vector2(p.x, p.z).magnitude;
            bool outside = r > Radius || p.y > Ceiling;
            Warning = r > Radius - WarnMargin || p.y > Ceiling - 10f;
            _outside = outside && !_vehicle.IsCrashed ? _outside + Time.deltaTime : 0f;
            if (_outside >= ResetSeconds)
            {
                _outside = 0f;
                Warning = false;
                Respawn();
            }
        }

        private void FreeFlightInput()
        {
            var kb = Keyboard.current;
            var pad = Gamepad.current;
            if ((kb != null && kb.fKey.wasPressedThisFrame) || (pad != null && pad.dpad.up.wasPressedThisFrame))
            {
                _spawnIndex = (_spawnIndex + 1) % _spawns.Count;
                Respawn();
            }
            else if ((kb != null && kb.tKey.wasPressedThisFrame) || (pad != null && pad.leftStickButton.wasPressedThisFrame)) Respawn();
        }

        /// <summary>Back to the current free-flight spawn, or (racing) to the race's own respawn point.</summary>
        public void Respawn()
        {
            if (FreeFlight && _spawns.Count > 0)
            {
                var spawn = _spawns[_spawnIndex].point;
                _vehicle.SpawnPoint = spawn;
                _vehicle.Respawn();
                _holding = spawn.position.y > 1f;
                _vehicle.HoldAtSpawn = _holding;
                return;
            }
            _vehicle.Respawn();
        }
    }
}
