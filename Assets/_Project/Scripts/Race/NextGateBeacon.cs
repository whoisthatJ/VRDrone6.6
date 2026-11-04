using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ushqyr.Race
{
    /// <summary>
    /// Guidance in the open city (E6, P3) for air courses, with route guidance on:
    /// <list type="bullet">
    /// <item>a pulsing holo ring round the next gate, facing the way it is flown (works for gates overhead or below);</item>
    /// <item>for low, upright gates also the soft vertical light column (found from far away behind buildings);</item>
    /// <item>holographic chevrons floating along the flight line ahead of the quad, in the route's 3D frame, so climbs,
    /// dives and loops read in the air where ground lanes can't help.</item>
    /// </list>
    /// Shown while racing (and on the first gate before the start). <see cref="Show"/> comes from the track's
    /// air-course flag; <see cref="SetRoute"/> gives the flight line for the chevrons.
    /// </summary>
    public sealed class NextGateBeacon : MonoBehaviour
    {
        [SerializeField] private RaceSession session;
        [SerializeField] private Material material;
        [Tooltip("Hologram material for the ring and the chevrons (falls back to the column material).")]
        [SerializeField] private Material guide;
        [SerializeField] private float radius = 0.9f;
        [SerializeField] private float height = 150f;
        [SerializeField] private int chevrons = 6;
        [SerializeField] private float chevronSpacing = 10f;

        private Transform _beam, _ring;
        private Transform[] _chevrons = new Transform[0];
        private Func<Vector3, float> _closest;
        private Func<float, (Vector3 position, Quaternion rotation)> _frame;
        private float _length, _lineDistance = -1f;

        /// <summary>Whether this track uses beacons (city courses).</summary>
        public bool Show { get; set; }

        public void Configure(RaceSession race, Material beam, Material hologram = null)
        {
            session = race;
            material = beam;
            guide = hologram;
        }

        /// <summary>The flight line for the chevrons: closest distance to a point, the frame at a distance, the lap length.</summary>
        public void SetRoute(Func<Vector3, float> closest, Func<float, (Vector3, Quaternion)> frame, float length)
        {
            _closest = closest;
            _frame = frame;
            _length = length;
            _lineDistance = -1f;
        }

        public void ClearRoute() => _closest = null;

        private void Awake()
        {
            _beam = Make("Next Gate Beacon", Column(radius, height, 16), material);
            _ring = Make("Next Gate Ring", Torus(1f, 0.06f, 48, 6), guide != null ? guide : material);
            _chevrons = new Transform[chevrons];
            var mesh = Chevron();
            for (int i = 0; i < chevrons; i++) _chevrons[i] = Make($"Chevron {i}", mesh, guide != null ? guide : material);
        }

        private Transform Make(string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            go.SetActive(false);
            return go.transform;
        }

        private void LateUpdate()
        {
            Gate gate = null;
            bool guided = Show && session != null && session.Guidance != Ushqyr.Core.RouteGuidanceLevel.Off;
            if (guided)
            {
                if (session.Phase == RacePhase.Racing) gate = session.NextGate;
                else if (session.Phase != RacePhase.Finished && session.GateCount > 1) gate = session.FinishGate;
            }

            bool on = gate != null && material != null;
            Vector3 p = on ? gate.AimPointWorld : default;
            // The column only for gates near the ground that face sideways; the ring for every gate. Without the
            // hologram material (Track_Expo_v1, frozen) it stays the E6 column only.
            bool holo = guide != null;
            bool column = on && (!holo || (p.y < 25f && Mathf.Abs(gate.transform.forward.y) < 0.5f));
            SetActive(_beam, column);
            if (column) _beam.position = new Vector3(p.x, 0f, p.z);
            SetActive(_ring, on && holo);
            if (on && holo)
            {
                var size = gate.OpeningSize;
                float r = Mathf.Max(size.x, size.y) * 0.5f + 0.8f;
                float pulse = 1f + 0.08f * Mathf.Sin(Time.time * 5f);
                _ring.SetPositionAndRotation(gate.OpeningCenterWorld, gate.transform.rotation);
                _ring.localScale = Vector3.one * r * pulse;
            }

            bool chevronsOn = holo && guided && session.Phase == RacePhase.Racing && _closest != null && _frame != null && _length > 0f;
            float d0 = 0f;
            if (chevronsOn)
            {
                Vector3 quad = session.FinishGate != null ? QuadPosition() : transform.position;
                d0 = LineDistance(quad);
            }
            for (int i = 0; i < _chevrons.Length; i++)
            {
                SetActive(_chevrons[i], chevronsOn);
                if (!chevronsOn) continue;
                var (pos, rot) = _frame(Mathf.Repeat(d0 + 8f + i * chevronSpacing, _length));
                // A little below the line, so they lead the eye without blocking the gates.
                _chevrons[i].SetPositionAndRotation(pos + rot * Vector3.down * 1.4f, rot);
            }
        }

        private Vector3 QuadPosition()
        {
            var v = session.Vehicle;
            return v != null ? v.transform.position : transform.position;
        }

        /// <summary>Where the quad is along the line: a local search near the last value, a global one after a jump.</summary>
        private float LineDistance(Vector3 quad)
        {
            if (_lineDistance < 0f)
            {
                _lineDistance = _closest(quad);
                return _lineDistance;
            }
            float best = float.MaxValue, bestD = _lineDistance;
            for (float d = _lineDistance - 10f; d <= _lineDistance + 30f; d += 1f)
            {
                float e = (_frame(Mathf.Repeat(d, _length)).position - quad).sqrMagnitude;
                if (e < best)
                {
                    best = e;
                    bestD = d;
                }
            }
            _lineDistance = best > 40f * 40f ? -1f : Mathf.Repeat(bestD, _length);
            return bestD;
        }

        private static void SetActive(Transform t, bool on)
        {
            if (t != null && t.gameObject.activeSelf != on) t.gameObject.SetActive(on);
        }

        // ---------- meshes ----------

        /// <summary>An open cylinder from y = 0 to <paramref name="h"/>.</summary>
        private static Mesh Column(float r, float h, int sides)
        {
            var v = new Vector3[(sides + 1) * 2];
            var uv = new Vector2[v.Length];
            var n = new Vector3[v.Length];
            var t = new int[sides * 6];
            for (int i = 0; i <= sides; i++)
            {
                float a = i * Mathf.PI * 2f / sides;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                v[i * 2] = dir * r;
                v[i * 2 + 1] = dir * r + Vector3.up * h;
                n[i * 2] = n[i * 2 + 1] = dir;
                uv[i * 2] = new Vector2((float)i / sides, 0f);
                uv[i * 2 + 1] = new Vector2((float)i / sides, 1f);
                if (i == sides) break;
                int k = i * 6, b = i * 2;
                t[k] = b; t[k + 1] = b + 1; t[k + 2] = b + 2;
                t[k + 3] = b + 2; t[k + 4] = b + 1; t[k + 5] = b + 3;
            }
            var mesh = new Mesh { name = "Beacon Column", vertices = v, normals = n, uv = uv, triangles = t };
            mesh.bounds = new Bounds(new Vector3(0f, h / 2, 0f), new Vector3(r * 2, h, r * 2));
            return mesh;
        }

        /// <summary>A unit-radius torus in the XY plane (scaled to the gate).</summary>
        private static Mesh Torus(float r, float tube, int seg, int sides)
        {
            var v = new System.Collections.Generic.List<Vector3>();
            var n = new System.Collections.Generic.List<Vector3>();
            var t = new System.Collections.Generic.List<int>();
            for (int i = 0; i <= seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                var c = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                for (int j = 0; j <= sides; j++)
                {
                    float b = j * Mathf.PI * 2f / sides;
                    var dir = c * Mathf.Cos(b) + Vector3.forward * Mathf.Sin(b);
                    v.Add(c * r + dir * tube);
                    n.Add(dir);
                }
            }
            int row = sides + 1;
            for (int i = 0; i < seg; i++)
                for (int j = 0; j < sides; j++)
                {
                    int a0 = i * row + j, a1 = a0 + 1, b0 = a0 + row, b1 = b0 + 1;
                    t.AddRange(new[] { a0, b0, a1, a1, b0, b1 });
                }
            var mesh = new Mesh { name = "Beacon Ring" };
            mesh.SetVertices(v);
            mesh.SetNormals(n);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>A flat "V" pointing along +Z in the XZ plane, 1.6 m wide (both faces).</summary>
        private static Mesh Chevron()
        {
            const float w = 0.8f, len = 0.9f, th = 0.22f;
            var pts = new[]
            {
                new Vector3(-w, 0f, -len * 0.5f), new Vector3(-w + th, 0f, -len * 0.5f), new Vector3(0f, 0f, len * 0.5f - th * 1.2f),
                new Vector3(w - th, 0f, -len * 0.5f), new Vector3(w, 0f, -len * 0.5f), new Vector3(0f, 0f, len * 0.5f),
            };
            var v = new System.Collections.Generic.List<Vector3>(pts);
            v.AddRange(pts);
            var n = new System.Collections.Generic.List<Vector3>();
            for (int i = 0; i < 6; i++) n.Add(Vector3.up);
            for (int i = 0; i < 6; i++) n.Add(Vector3.down);
            // Left arm (0,1,2,5), right arm (2,3,4,5), front and back faces.
            var t = new System.Collections.Generic.List<int> { 0, 5, 1, 1, 5, 2, 2, 5, 3, 3, 5, 4 };
            for (int i = 0; i < 12; i += 3) t.AddRange(new[] { t[i] + 6, t[i + 2] + 6, t[i + 1] + 6 });
            var mesh = new Mesh { name = "Chevron" };
            mesh.SetVertices(v);
            mesh.SetNormals(n);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
