using UnityEngine;
using UnityEngine.Rendering;

namespace Ushqyr.Strike
{
    /// <summary>Marks a target's hitbox collider with its slot, so a contact knows what it hit.</summary>
    public sealed class StrikeHitbox : MonoBehaviour
    {
        public int Slot;
    }

    /// <summary>
    /// Fictional generic vehicle silhouettes for the range (P5), built from boxes and cylinders in code: no real-world
    /// model, insignia, unit or country. Each comes with simple box hitboxes on the track-geometry layer.
    /// </summary>
    public static class RangeVehicles
    {
        public static readonly Color Tan = new Color(0.62f, 0.56f, 0.42f), Olive = new Color(0.36f, 0.38f, 0.26f),
            Dark = new Color(0.16f, 0.16f, 0.15f), Steel = new Color(0.45f, 0.46f, 0.47f);

        private static Material _paint, _dark, _steel;
        private static Mesh _cube, _cyl;

        private static Material Mat(ref Material m, Color c, float smooth)
        {
            if (m != null) return m;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            m = new Material(shader) { name = "Range Vehicle", enableInstancing = true };
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", smooth);
            return m;
        }

        private static Mesh Cube => _cube != null ? _cube : (_cube = Primitive(PrimitiveType.Cube));
        private static Mesh Cylinder => _cyl != null ? _cyl : (_cyl = Primitive(PrimitiveType.Cylinder));

        private static Mesh Primitive(PrimitiveType t)
        {
            var go = GameObject.CreatePrimitive(t);
            var m = go.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(go);
            return m;
        }

        /// <summary>Builds a vehicle under <paramref name="parent"/>; the root's forward is the driving direction.</summary>
        public static GameObject Build(VehicleKind kind, Transform parent, int layer, Color paint, int slot = -1)
        {
            var root = new GameObject(kind.ToString());
            root.transform.SetParent(parent, false);
            root.layer = layer;
            var body = Mat(ref _paint, Tan, 0.15f);
            var dark = Mat(ref _dark, Dark, 0.1f);
            var steel = Mat(ref _steel, Steel, 0.4f);
            var tint = new MaterialPropertyBlock();
            tint.SetColor("_BaseColor", paint);

            void Box(Vector3 c, Vector3 s, Material m, bool painted = false, Quaternion? r = null) => Part(root.transform, Cube, c, s, r ?? Quaternion.identity, m, painted ? tint : null, layer);
            void Wheel(Vector3 c, float radius, float width) => Part(root.transform, Cylinder, c, new Vector3(radius * 2f, width * 0.5f, radius * 2f), Quaternion.Euler(0f, 0f, 90f), dark, null, layer);

            switch (kind)
            {
                case VehicleKind.Tracked:
                    Box(new Vector3(0f, 0.95f, 0f), new Vector3(3.0f, 1.0f, 6.4f), body, true);
                    Box(new Vector3(0f, 1.65f, -0.6f), new Vector3(2.4f, 0.6f, 3.6f), body, true);
                    Box(new Vector3(0f, 1.6f, 2.5f), new Vector3(2.6f, 0.35f, 1.2f), body, true, Quaternion.Euler(-18f, 0f, 0f));
                    foreach (float sx in new[] { -1.55f, 1.55f }) Box(new Vector3(sx, 0.55f, 0f), new Vector3(0.55f, 1.0f, 6.6f), dark);
                    Box(new Vector3(0f, 2.15f, -0.9f), new Vector3(1.2f, 0.45f, 1.4f), steel);
                    AddHitbox(root, new Vector3(0f, 1.1f, 0f), new Vector3(3.4f, 2.2f, 6.8f), slot, layer);
                    break;
                case VehicleKind.Wheeled:
                    Box(new Vector3(0f, 1.25f, 0f), new Vector3(2.7f, 1.3f, 6.8f), body, true);
                    Box(new Vector3(0f, 1.25f, 3.6f), new Vector3(2.5f, 0.9f, 0.8f), body, true, Quaternion.Euler(25f, 0f, 0f));
                    Box(new Vector3(0.5f, 2.05f, 0.4f), new Vector3(1.0f, 0.4f, 1.2f), steel);
                    for (int i = 0; i < 4; i++)
                        foreach (float sx in new[] { -1.35f, 1.35f }) Wheel(new Vector3(sx, 0.55f, -2.4f + i * 1.6f), 0.55f, 0.4f);
                    AddHitbox(root, new Vector3(0f, 1.2f, 0f), new Vector3(3.0f, 2.4f, 7.2f), slot, layer);
                    break;
                case VehicleKind.Truck:
                    Box(new Vector3(0f, 1.55f, 2.6f), new Vector3(2.4f, 1.7f, 1.8f), body, true);
                    Box(new Vector3(0f, 1.0f, -0.6f), new Vector3(2.4f, 0.3f, 5.0f), steel);
                    Box(new Vector3(0f, 1.85f, -0.9f), new Vector3(2.5f, 1.4f, 4.2f), body, true);
                    for (int i = 0; i < 3; i++)
                        foreach (float sx in new[] { -1.15f, 1.15f }) Wheel(new Vector3(sx, 0.5f, i == 0 ? 2.4f : -0.4f - (i - 1) * 1.4f), 0.5f, 0.35f);
                    AddHitbox(root, new Vector3(0f, 1.4f, 0.3f), new Vector3(2.7f, 2.8f, 7.0f), slot, layer);
                    break;
                case VehicleKind.Artillery:
                    Box(new Vector3(0f, 1.0f, 0f), new Vector3(2.8f, 0.9f, 6.6f), body, true);
                    Box(new Vector3(0f, 1.7f, -0.8f), new Vector3(2.2f, 0.7f, 2.6f), body, true);
                    Part(root.transform, Cylinder, new Vector3(0f, 2.3f, 1.6f), new Vector3(0.28f, 2.6f, 0.28f), Quaternion.Euler(70f, 0f, 0f), steel, null, layer);
                    foreach (float sx in new[] { -1.45f, 1.45f }) Box(new Vector3(sx, 0.55f, 0f), new Vector3(0.5f, 0.95f, 6.6f), dark);
                    AddHitbox(root, new Vector3(0f, 1.2f, 0.4f), new Vector3(3.2f, 2.4f, 7.4f), slot, layer);
                    break;
                default: // radar truck
                    Box(new Vector3(0f, 1.55f, 2.6f), new Vector3(2.4f, 1.7f, 1.8f), body, true);
                    Box(new Vector3(0f, 1.0f, -0.6f), new Vector3(2.4f, 0.3f, 5.0f), steel);
                    Box(new Vector3(0f, 1.7f, -1.2f), new Vector3(2.2f, 1.1f, 3.0f), body, true);
                    Part(root.transform, Cylinder, new Vector3(0f, 2.9f, -1.2f), new Vector3(0.25f, 0.7f, 0.25f), Quaternion.identity, steel, null, layer);
                    var dish = Part(root.transform, Cube, new Vector3(0f, 3.9f, -1.2f), new Vector3(3.2f, 1.4f, 0.12f), Quaternion.Euler(-15f, 0f, 0f), steel, null, layer);
                    dish.name = "Radar";
                    dish.AddComponent<Ushqyr.Core.Spinner>();
                    for (int i = 0; i < 3; i++)
                        foreach (float sx in new[] { -1.15f, 1.15f }) Wheel(new Vector3(sx, 0.5f, i == 0 ? 2.4f : -0.4f - (i - 1) * 1.4f), 0.5f, 0.35f);
                    AddHitbox(root, new Vector3(0f, 1.5f, 0.3f), new Vector3(2.7f, 3.0f, 7.0f), slot, layer);
                    AddHitbox(root, new Vector3(0f, 3.8f, -1.2f), new Vector3(3.2f, 1.6f, 0.6f), slot, layer);
                    break;
            }
            return root;
        }

        private static GameObject Part(Transform parent, Mesh mesh, Vector3 c, Vector3 s, Quaternion r, Material m, MaterialPropertyBlock block, int layer)
        {
            var go = new GameObject("Part") { layer = layer };
            go.transform.SetParent(parent, false);
            go.transform.SetLocalPositionAndRotation(c, r);
            go.transform.localScale = s;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = m;
            mr.shadowCastingMode = ShadowCastingMode.On;
            if (block != null) mr.SetPropertyBlock(block);
            return go;
        }

        private static void AddHitbox(GameObject root, Vector3 c, Vector3 s, int slot, int layer)
        {
            var go = new GameObject("Hitbox") { layer = layer };
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = c;
            var box = go.AddComponent<BoxCollider>();
            box.size = s;
            go.AddComponent<StrikeHitbox>().Slot = slot;
        }
    }

    /// <summary>
    /// One target on the range: the vehicle model following its plan, and its disabled look (dark, smoking, a scorch
    /// mark) until it respawns somewhere else with a new vehicle.
    /// </summary>
    public sealed class RangeTargetView : MonoBehaviour
    {
        public int Slot;
        private RangeDirector _director;
        private GameObject _model;
        private int _generation = -1;
        private ParticleSystem _smoke;
        private Transform _scorch;
        private bool _shownDisabled;
        private int _layer;

        public static RangeTargetView Create(Transform parent, RangeDirector director, int slot, int layer)
        {
            var go = new GameObject("Target " + slot) { layer = layer };
            go.transform.SetParent(parent, false);
            var v = go.AddComponent<RangeTargetView>();
            v.Slot = slot;
            v._director = director;
            v._layer = layer;
            return v;
        }

        public RangeTarget State => _director.Targets[Slot];

        /// <summary>Places the vehicle for the round time (also used headless by the bot tests).</summary>
        public void Sync(float time)
        {
            var t = State;
            if (t.Plan.Generation != _generation || _model == null)
            {
                if (_model != null) DestroyNow(_model);
                _generation = t.Plan.Generation;
                var paint = t.Plan.Class == TargetClass.Static ? RangeVehicles.Tan : t.Plan.Class == TargetClass.Moving ? RangeVehicles.Olive : new Color(0.5f, 0.45f, 0.34f);
                _model = RangeVehicles.Build(t.Plan.Kind, transform, _layer, paint, Slot);
                _shownDisabled = false;
            }
            if (t.Active)
            {
                _director.Pose(Slot, time, out var p, out float yaw, out _);
                transform.SetPositionAndRotation(p, Quaternion.Euler(0f, yaw, 0f));
            }
            bool disabled = !t.Active;
            if (disabled != _shownDisabled) ShowDisabled(disabled);
        }

        private void ShowDisabled(bool on)
        {
            _shownDisabled = on;
            if (_model == null) return;
            var block = new MaterialPropertyBlock();
            foreach (var r in _model.GetComponentsInChildren<MeshRenderer>())
            {
                r.GetPropertyBlock(block);
                block.SetColor("_BaseColor", on ? new Color(0.09f, 0.085f, 0.08f) : RangeVehicles.Tan);
                r.SetPropertyBlock(block);
            }
            foreach (var c in _model.GetComponentsInChildren<Collider>()) c.enabled = !on;
            if (!Application.isPlaying) return;
            if (_smoke == null) _smoke = MakeSmoke(transform);
            if (on) _smoke.Play();
            else _smoke.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            if (on)
            {
                if (_scorch == null) _scorch = MakeScorch();
                _scorch.SetPositionAndRotation(transform.position + Vector3.up * 0.05f, Quaternion.Euler(90f, Random.Range(0f, 360f), 0f));
                _scorch.gameObject.SetActive(true);
            }
        }

        private static ParticleSystem MakeSmoke(Transform parent)
        {
            var go = new GameObject("Smoke");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.up * 2f;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.startLifetime = 6f;
            main.startSpeed = 2.5f;
            main.startSize = new ParticleSystem.MinMaxCurve(2.5f, 5f);
            main.startColor = new Color(0.12f, 0.12f, 0.12f, 0.6f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 120;
            var em = ps.emission;
            em.rateOverTime = 10f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 1f;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(0.15f, 0.14f, 0.13f), 0f), new GradientColorKey(new Color(0.35f, 0.34f, 0.32f), 1f) },
                new[] { new GradientAlphaKey(0.7f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = SmokeMaterial;
            return ps;
        }

        private static Material _smokeMat, _scorchMat;
        private static Texture2D _disc;

        /// <summary>Material assets made by the range builder (so the shaders ship in builds); runtime fallbacks otherwise.</summary>
        public static Material SmokeOverride, ScorchOverride;

        /// <summary>A soft round puff: alpha falls off smoothly to the edge (no square billboards).</summary>
        public static Texture2D SoftDisc
        {
            get
            {
                if (_disc != null) return _disc;
                const int n = 64;
                _disc = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "Soft Disc", wrapMode = TextureWrapMode.Clamp };
                var px = new Color32[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                        float r = Mathf.Sqrt(dx * dx + dy * dy);
                        float a = Mathf.Clamp01(1f - r);
                        a = a * a * (3f - 2f * a);
                        px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                    }
                _disc.SetPixels32(px);
                _disc.Apply();
                return _disc;
            }
        }

        /// <summary>Alpha-blended, no depth writes: what URP's Unlit / Particles Unlit need set besides _Surface.</summary>
        public static void MakeTransparent(Material m)
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            m.SetOverrideTag("RenderType", "Transparent");
        }

        public static Material NewSmokeMaterial()
        {
            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
            var m = new Material(sh) { name = "Range Smoke" };
            MakeTransparent(m);
            m.SetTexture("_BaseMap", SoftDisc);
            m.mainTexture = SoftDisc;
            m.SetColor("_BaseColor", new Color(1f, 1f, 1f, 1f));
            return m;
        }

        public static Material NewScorchMaterial()
        {
            var sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Transparent");
            var m = new Material(sh) { name = "Scorch" };
            MakeTransparent(m);
            m.SetTexture("_BaseMap", SoftDisc);
            m.mainTexture = SoftDisc;
            m.SetColor("_BaseColor", new Color(0.04f, 0.035f, 0.03f, 0.85f));
            return m;
        }

        public static Material SmokeMaterial => SmokeOverride != null ? SmokeOverride : _smokeMat != null ? _smokeMat : (_smokeMat = NewSmokeMaterial());
        private static Material ScorchMaterial => ScorchOverride != null ? ScorchOverride : _scorchMat != null ? _scorchMat : (_scorchMat = NewScorchMaterial());

        private Transform MakeScorch()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "Scorch";
            Destroy(go.GetComponent<Collider>());
            go.transform.localScale = new Vector3(9f, 9f, 1f);
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = ScorchMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }

        private static void DestroyNow(GameObject go)
        {
            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
        }

        private void OnDestroy()
        {
            if (_scorch != null) DestroyNow(_scorch.gameObject);
        }
    }
}
