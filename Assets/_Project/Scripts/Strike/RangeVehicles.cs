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
    /// Fictional generic vehicles for the range (P5): no real-world model, insignia, unit or country. Built in code with
    /// <see cref="VehicleKit"/> (chamfered boxes, wheels, tracks with road wheels, cabs with glass), one mesh per
    /// material, cached per kind; textured materials from the range. Simple box hitboxes on the track-geometry layer.
    /// </summary>
    public static class RangeVehicles
    {
        public static readonly Color Tan = new Color(0.66f, 0.6f, 0.45f), Olive = new Color(0.4f, 0.43f, 0.29f),
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

        /// <summary>Half width and half length of a vehicle's footprint (m), for traffic spacing checks.</summary>
        public static Vector2 Footprint(VehicleKind kind) => kind switch
        {
            VehicleKind.Tracked => new Vector2(1.7f, 3.4f),
            VehicleKind.Wheeled => new Vector2(1.5f, 3.9f),
            VehicleKind.Truck => new Vector2(1.3f, 3.6f),
            VehicleKind.Artillery => new Vector2(1.6f, 3.8f),
            _ => new Vector2(1.6f, 3.6f),
        };

        /// <summary>
        /// Materials per <see cref="VehicleKit.Slot"/> (paint, rubber, metal, dark, glass, canvas, light), set from the
        /// range's material assets (textured, so they ship in builds); flat-colour fallbacks otherwise.
        /// </summary>
        public static Material[] MaterialsOverride;
        private static Material[] _fallback;
        private static readonly System.Collections.Generic.Dictionary<VehicleKind, Mesh[]> Meshes = new System.Collections.Generic.Dictionary<VehicleKind, Mesh[]>();

        private sealed class Gear
        {
            public VehicleKit.WheelSpec[] Wheels;
            public Mesh[] WheelMeshes;
            public Mesh Top, Bottom;
        }

        private static readonly System.Collections.Generic.Dictionary<VehicleKind, Gear> Gears = new System.Collections.Generic.Dictionary<VehicleKind, Gear>();

        public static Material MaterialFor(VehicleKit.Slot slot)
        {
            var o = MaterialsOverride;
            if (o != null && (int)slot < o.Length && o[(int)slot] != null) return o[(int)slot];
            if (_fallback == null)
            {
                _fallback = new Material[VehicleKit.Slots];
                var colors = new[] { new Color(0.85f, 0.85f, 0.8f), new Color(0.08f, 0.08f, 0.08f), Steel, Dark, new Color(0.08f, 0.1f, 0.12f), new Color(0.45f, 0.43f, 0.32f), new Color(1f, 0.95f, 0.8f) };
                for (int i = 0; i < _fallback.Length; i++)
                {
                    var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                    _fallback[i] = new Material(shader) { name = "Range Vehicle " + (VehicleKit.Slot)i, enableInstancing = true };
                    _fallback[i].SetColor("_BaseColor", colors[i]);
                    _fallback[i].SetFloat("_Smoothness", i == 4 ? 0.9f : i == 2 ? 0.45f : 0.2f);
                }
            }
            return _fallback[(int)slot];
        }

        /// <summary>Builds a vehicle under <paramref name="parent"/>; the root's forward is the driving direction.</summary>
        public static GameObject Build(VehicleKind kind, Transform parent, int layer, Color paint, int slot = -1)
        {
            var root = new GameObject(kind.ToString());
            root.transform.SetParent(parent, false);
            root.layer = layer;
            if (!Meshes.TryGetValue(kind, out var meshes) || meshes == null || System.Array.TrueForAll(meshes, m => m == null))
            {
                var kit = Model(kind);
                Meshes[kind] = meshes = kit.Build(kind.ToString());
                var gearParts = new Gear { Wheels = kit.Wheels.ToArray(), WheelMeshes = new Mesh[kit.Wheels.Count] };
                var byShape = new System.Collections.Generic.Dictionary<string, Mesh>();
                for (int w = 0; w < gearParts.Wheels.Length; w++)
                {
                    var spec = gearParts.Wheels[w];
                    string key = $"{spec.Road}|{spec.Radius:F3}|{spec.Width:F3}|{spec.Side:F0}";
                    if (!byShape.TryGetValue(key, out var wm)) byShape[key] = wm = VehicleKit.WheelMesh(spec);
                    gearParts.WheelMeshes[w] = wm;
                }
                if (kit.TopCleats != null) gearParts.Top = kit.TopCleats.Build(kind + " Track Top")[(int)VehicleKit.Slot.Rubber];
                if (kit.BottomCleats != null) gearParts.Bottom = kit.BottomCleats.Build(kind + " Track Bottom")[(int)VehicleKit.Slot.Rubber];
                Gears[kind] = gearParts;
            }
            var tint = new MaterialPropertyBlock();
            for (int i = 0; i < meshes.Length; i++)
            {
                if (meshes[i] == null) continue;
                var go = new GameObject(((VehicleKit.Slot)i).ToString()) { layer = layer };
                go.transform.SetParent(root.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = meshes[i];
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = MaterialFor((VehicleKit.Slot)i);
                mr.shadowCastingMode = (VehicleKit.Slot)i == VehicleKit.Slot.Light ? ShadowCastingMode.Off : ShadowCastingMode.On;
                if ((VehicleKit.Slot)i == VehicleKit.Slot.Paint)
                {
                    tint.SetColor("_BaseColor", paint);
                    mr.SetPropertyBlock(tint);
                }
            }
            // Running gear: each wheel its own object (they turn), track cleats in two sliding runs.
            if (Gears.TryGetValue(kind, out var gear) && gear != null && (gear.Wheels.Length > 0 || gear.Top != null))
            {
                var running = root.AddComponent<VehicleRunningGear>();
                var wheelMats = System.Array.ConvertAll(VehicleKit.WheelSlots, MaterialFor);
                var wheels = new Transform[gear.Wheels.Length];
                var radii = new float[gear.Wheels.Length];
                for (int w = 0; w < wheels.Length; w++)
                {
                    var go = new GameObject(gear.WheelMeshes[w].name) { layer = layer };
                    go.transform.SetParent(root.transform, false);
                    go.transform.localPosition = gear.Wheels[w].Centre;
                    go.AddComponent<MeshFilter>().sharedMesh = gear.WheelMeshes[w];
                    go.AddComponent<MeshRenderer>().sharedMaterials = wheelMats;
                    wheels[w] = go.transform;
                    radii[w] = gear.Wheels[w].Radius;
                }
                Transform Run(string name, Mesh mesh)
                {
                    if (mesh == null) return null;
                    var go = new GameObject(name) { layer = layer };
                    go.transform.SetParent(root.transform, false);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    go.AddComponent<MeshRenderer>().sharedMaterial = MaterialFor(VehicleKit.Slot.Rubber);
                    return go.transform;
                }
                running.Set(wheels, radii, Run("Track Top", gear.Top), Run("Track Bottom", gear.Bottom));
            }
            switch (kind)
            {
                case VehicleKind.Tracked:
                    AddHitbox(root, new Vector3(0f, 1.1f, 0f), new Vector3(3.4f, 2.2f, 6.8f), slot, layer);
                    break;
                case VehicleKind.Wheeled:
                    AddHitbox(root, new Vector3(0f, 1.2f, 0f), new Vector3(3.0f, 2.4f, 7.2f), slot, layer);
                    break;
                case VehicleKind.Truck:
                    AddHitbox(root, new Vector3(0f, 1.4f, 0.3f), new Vector3(2.7f, 2.8f, 7.0f), slot, layer);
                    break;
                case VehicleKind.Artillery:
                    AddHitbox(root, new Vector3(0f, 1.2f, 0.4f), new Vector3(3.2f, 2.4f, 7.4f), slot, layer);
                    break;
                default:
                    AddHitbox(root, new Vector3(0f, 1.5f, 0.3f), new Vector3(2.7f, 3.0f, 7.0f), slot, layer);
                    AddHitbox(root, new Vector3(0f, 3.8f, -1.2f), new Vector3(3.2f, 1.6f, 0.6f), slot, layer);
                    // The radar antenna turns: its own object.
                    var dishKit = new VehicleKit();
                    // A curved reflector of slats on a frame (it sweeps round the vertical axis), with a feed horn on a boom.
                    const int panels = 9;
                    for (int i = 0; i < panels; i++)
                    {
                        float t = (i - (panels - 1) * 0.5f) / ((panels - 1) * 0.5f);
                        var c = new Vector3(t * 1.6f, 0f, -0.42f * t * t);
                        var r = Quaternion.Euler(0f, -t * 28f, 0f);
                        dishKit.Box(VehicleKit.Slot.Metal, c, new Vector3(0.05f, 1.35f, 0.06f), 0.01f, r);
                        if (i == panels - 1) continue;
                        float tn = (i + 1 - (panels - 1) * 0.5f) / ((panels - 1) * 0.5f), tm = (t + tn) * 0.5f;
                        for (int s = 0; s < 9; s++)
                            dishKit.Box(VehicleKit.Slot.Canvas, new Vector3(tm * 1.6f, -0.6f + s * 0.15f, -0.42f * tm * tm), new Vector3(0.4f, 0.09f, 0.015f), 0.003f, Quaternion.Euler(0f, -tm * 28f, 0f));
                    }
                    foreach (float y in new[] { -0.66f, 0.66f })
                        for (int i = 0; i < panels - 1; i++)
                        {
                            float t0 = (i - (panels - 1) * 0.5f) / ((panels - 1) * 0.5f), t1 = (i + 1 - (panels - 1) * 0.5f) / ((panels - 1) * 0.5f);
                            dishKit.Cylinder(VehicleKit.Slot.Metal, new Vector3(t0 * 1.6f, y, -0.42f * t0 * t0), new Vector3(t1 * 1.6f, y, -0.42f * t1 * t1), 0.025f, 5);
                        }
                    dishKit.Box(VehicleKit.Slot.Metal, new Vector3(0f, 0f, 0.22f), new Vector3(0.5f, 0.4f, 0.4f), 0.05f);
                    dishKit.Cylinder(VehicleKit.Slot.Metal, new Vector3(0f, -0.6f, 0f), new Vector3(0f, -0.25f, -1.0f), 0.025f, 5);
                    dishKit.Box(VehicleKit.Slot.Dark, new Vector3(0f, -0.2f, -1.05f), new Vector3(0.22f, 0.16f, 0.2f), 0.03f);
                    var dish = new GameObject("Radar") { layer = layer };
                    dish.transform.SetParent(root.transform, false);
                    dish.transform.SetLocalPositionAndRotation(new Vector3(0f, 3.9f, -1.2f), Quaternion.identity);
                    var dm = dishKit.Build("Radar Dish");
                    for (int i = 0; i < dm.Length; i++)
                    {
                        if (dm[i] == null) continue;
                        var g = new GameObject(((VehicleKit.Slot)i).ToString()) { layer = layer };
                        g.transform.SetParent(dish.transform, false);
                        g.transform.localRotation = Quaternion.Euler(-15f, 0f, 0f);
                        g.AddComponent<MeshFilter>().sharedMesh = dm[i];
                        g.AddComponent<MeshRenderer>().sharedMaterial = MaterialFor((VehicleKit.Slot)i);
                    }
                    dish.AddComponent<Ushqyr.Core.Spinner>().Configure(Vector3.up, 7f); // sweeps round its mast
                    break;
            }
            return root;
        }

        // ---------- the models (fictional generic designs) ----------

        private static VehicleKit Model(VehicleKind kind)
        {
            var k = new VehicleKit();
            const VehicleKit.Slot P = VehicleKit.Slot.Paint, R = VehicleKit.Slot.Rubber, M = VehicleKit.Slot.Metal, D = VehicleKit.Slot.Dark,
                G = VehicleKit.Slot.Glass, C = VehicleKit.Slot.Canvas, L = VehicleKit.Slot.Light;
            switch (kind)
            {
                case VehicleKind.Tracked:
                {
                    // Tracked carrier: low hull, sloped glacis, small turret, tracks with six road wheels.
                    foreach (float sx in new[] { -1.3f, 1.3f }) k.Track(sx, 6.4f, 1.05f, 0.5f, 6);
                    k.Box(P, new Vector3(0f, 1.15f, -0.2f), new Vector3(2.5f, 0.75f, 5.6f), 0.08f);
                    k.Box(P, new Vector3(0f, 1.18f, 2.75f), new Vector3(2.45f, 0.5f, 1.1f), 0.06f, Quaternion.Euler(-22f, 0f, 0f));
                    foreach (float sx in new[] { -1f, 1f }) k.Box(P, new Vector3(sx * 1.45f, 1.12f, 0f), new Vector3(0.18f, 0.55f, 5.9f), 0.03f); // side skirts
                    k.Box(P, new Vector3(0f, 1.62f, -0.3f), new Vector3(2.3f, 0.22f, 4.6f), 0.06f);
                    k.Box(P, new Vector3(0.2f, 2.0f, 0.2f), new Vector3(1.45f, 0.55f, 1.7f), 0.12f);
                    k.Box(P, new Vector3(0.2f, 2.0f, 1.1f), new Vector3(1.2f, 0.42f, 0.4f), 0.08f, Quaternion.Euler(-30f, 0f, 0f));
                    k.Cylinder(M, new Vector3(0.45f, 2.02f, 1.1f), new Vector3(0.45f, 2.06f, 3.3f), 0.07f, 10);
                    k.Cylinder(D, new Vector3(0.45f, 2.06f, 3.3f), new Vector3(0.45f, 2.07f, 3.45f), 0.1f, 10);
                    foreach (var (x, z) in new[] { (-0.7f, -1.3f), (0.7f, -1.3f), (-0.6f, 0.4f) })
                        k.Cylinder(P, new Vector3(x, 1.72f, z), new Vector3(x, 1.8f, z), 0.32f, 14); // hatches
                    k.Box(G, new Vector3(-0.75f, 1.62f, 1.45f), new Vector3(0.5f, 0.12f, 0.06f), 0.01f, Quaternion.Euler(-30f, 0f, 0f)); // driver's vision block
                    for (int i = 0; i < 3; i++) k.Box(G, new Vector3(-0.05f + i * 0.28f, 2.2f, 0.95f), new Vector3(0.18f, 0.08f, 0.05f), 0.01f);
                    k.Cylinder(M, new Vector3(-0.45f, 2.25f, -0.4f), new Vector3(-0.45f, 4.0f, -0.6f), 0.012f, 4); // antennas
                    k.Cylinder(M, new Vector3(0.95f, 1.75f, -2.4f), new Vector3(0.95f, 3.3f, -2.6f), 0.012f, 4);
                    foreach (float sx in new[] { -1f, 1f })
                    {
                        k.Box(L, new Vector3(sx * 0.95f, 1.5f, 3.08f), new Vector3(0.18f, 0.12f, 0.06f), 0.02f);
                        k.Box(M, new Vector3(sx * 0.7f, 0.85f, 3.2f), new Vector3(0.2f, 0.25f, 0.3f), 0.03f); // tow hooks
                        k.Box(P, new Vector3(sx * 0.8f, 1.65f, -2.75f), new Vector3(0.7f, 0.45f, 0.5f), 0.05f); // stowage bins
                    }
                    k.Box(D, new Vector3(0.9f, 1.66f, 0.9f), new Vector3(0.6f, 0.04f, 0.9f), 0.01f); // engine grille
                    k.Box(C, new Vector3(-0.4f, 1.85f, -2.5f), new Vector3(1.0f, 0.35f, 0.5f), 0.12f); // rolled tarp
                    FitTracked(k, 1.73f, new Vector3(0.2f, 2.0f, 0.2f), new Vector3(1.45f, 0.55f, 1.7f), new Vector3(0.45f, 2.04f, 1.1f), 1.3f, 0.09f);
                    break;
                }
                case VehicleKind.Wheeled:
                {
                    // Eight-wheeled carrier: long faceted hull, remote weapon station, side doors.
                    for (int i = 0; i < 4; i++)
                        foreach (float sx in new[] { -1f, 1f })
                            k.Wheel(new Vector3(sx * 1.25f, 0.55f, -2.5f + i * 1.65f + (i >= 2 ? 0.25f : 0f)), 0.55f, 0.42f, sx);
                    k.Box(P, new Vector3(0f, 1.3f, -0.2f), new Vector3(2.6f, 1.1f, 6.3f), 0.1f);
                    k.Box(P, new Vector3(0f, 1.2f, 3.15f), new Vector3(2.4f, 0.75f, 1.0f), 0.08f, Quaternion.Euler(28f, 0f, 0f));
                    k.Box(P, new Vector3(0f, 1.95f, -0.5f), new Vector3(2.2f, 0.3f, 4.8f), 0.08f);
                    foreach (float sx in new[] { -1f, 1f })
                    {
                        k.Box(P, new Vector3(sx * 1.32f, 1.75f, -0.2f), new Vector3(0.12f, 0.35f, 6.0f), 0.04f, Quaternion.Euler(0f, 0f, sx * 25f)); // upper side slope
                        for (int i = 0; i < 4; i++) k.Box(P, new Vector3(sx * 1.33f, 1.15f, -2.5f + i * 1.65f + (i >= 2 ? 0.25f : 0f)), new Vector3(0.1f, 0.25f, 1.3f), 0.03f); // wheel arches
                        k.Box(D, new Vector3(sx * 1.31f, 1.35f, 0.6f), new Vector3(0.04f, 0.8f, 0.9f), 0.02f); // side door outline
                        k.Box(L, new Vector3(sx * 0.9f, 1.55f, 3.25f), new Vector3(0.2f, 0.12f, 0.05f), 0.02f);
                        k.Box(M, new Vector3(sx * 1.0f, 0.95f, 3.45f), new Vector3(0.18f, 0.2f, 0.2f), 0.03f);
                    }
                    k.Box(M, new Vector3(0.4f, 2.3f, 0.4f), new Vector3(0.9f, 0.45f, 0.9f), 0.08f); // weapon station
                    k.Cylinder(M, new Vector3(0.5f, 2.35f, 0.85f), new Vector3(0.5f, 2.38f, 2.0f), 0.05f, 8);
                    k.Box(G, new Vector3(0.15f, 2.38f, 0.86f), new Vector3(0.25f, 0.15f, 0.04f), 0.01f);
                    for (int i = 0; i < 3; i++) k.Box(G, new Vector3(-0.6f + i * 0.4f, 1.88f, 2.65f), new Vector3(0.25f, 0.1f, 0.06f), 0.01f, Quaternion.Euler(28f, 0f, 0f));
                    foreach (var z in new[] { -2.2f, -0.8f }) k.Cylinder(P, new Vector3(-0.5f, 2.1f, z), new Vector3(-0.5f, 2.16f, z), 0.3f, 14);
                    k.Cylinder(M, new Vector3(-0.9f, 2.1f, -2.8f), new Vector3(-0.9f, 4.0f, -3.0f), 0.012f, 4);
                    k.Box(D, new Vector3(0f, 1.3f, -3.36f), new Vector3(1.2f, 0.9f, 0.06f), 0.02f); // rear door
                    k.Box(C, new Vector3(0.6f, 2.18f, -2.3f), new Vector3(0.9f, 0.25f, 0.9f), 0.1f); // stowage net
                    FitWheeled(k);
                    break;
                }
                case VehicleKind.Truck:
                    BuildTruckBase(k);
                    // Cargo bed with a canvas cover on hoops.
                    k.Box(P, new Vector3(0f, 1.25f, -1.2f), new Vector3(2.45f, 0.12f, 4.2f), 0.03f);
                    foreach (float sx in new[] { -1f, 1f }) k.Box(P, new Vector3(sx * 1.2f, 1.55f, -1.2f), new Vector3(0.06f, 0.5f, 4.2f), 0.02f);
                    k.Box(P, new Vector3(0f, 1.55f, -3.28f), new Vector3(2.45f, 0.5f, 0.06f), 0.02f);
                    k.Box(C, new Vector3(0f, 2.25f, -1.2f), new Vector3(2.4f, 1.0f, 4.15f), 0.35f);
                    for (int i = 0; i < 5; i++)
                    {
                        // Hoops showing through the canvas, and the lashing along its lower edge.
                        float z = -3.1f + i * 0.95f;
                        k.Box(C, new Vector3(0f, 2.3f, z), new Vector3(2.46f, 1.02f, 0.07f), 0.36f);
                    }
                    foreach (float sx in new[] { -1f, 1f })
                    {
                        k.Cylinder(D, new Vector3(sx * 1.23f, 1.84f, -3.2f), new Vector3(sx * 1.23f, 1.84f, 0.8f), 0.012f, 4, false);
                        for (int i = 0; i < 6; i++) k.Box(P, new Vector3(sx * 1.235f, 1.55f, -3.0f + i * 0.72f), new Vector3(0.04f, 0.52f, 0.09f), 0.008f); // bed stakes
                    }
                    k.Box(D, new Vector3(0f, 2.2f, -3.3f), new Vector3(1.5f, 0.9f, 0.03f), 0.01f); // open back of the cover
                    break;
                case VehicleKind.Artillery:
                {
                    // Self-propelled gun: tracked chassis, a big boxy turret and a long barrel with a muzzle brake.
                    foreach (float sx in new[] { -1.3f, 1.3f }) k.Track(sx, 6.4f, 1.0f, 0.5f, 6);
                    k.Box(P, new Vector3(0f, 1.1f, 0f), new Vector3(2.5f, 0.7f, 6.0f), 0.08f);
                    k.Box(P, new Vector3(0f, 1.1f, 3.0f), new Vector3(2.4f, 0.45f, 0.8f), 0.06f, Quaternion.Euler(-25f, 0f, 0f));
                    foreach (float sx in new[] { -1f, 1f }) k.Box(P, new Vector3(sx * 1.45f, 1.08f, 0f), new Vector3(0.16f, 0.5f, 6.0f), 0.03f);
                    k.Box(P, new Vector3(0f, 1.95f, -0.9f), new Vector3(2.3f, 1.05f, 2.9f), 0.12f);
                    k.Box(P, new Vector3(0f, 1.95f, 0.75f), new Vector3(1.6f, 0.8f, 0.6f), 0.1f, Quaternion.Euler(-20f, 0f, 0f)); // mantlet
                    k.Cylinder(M, new Vector3(0f, 2.0f, 0.95f), new Vector3(0f, 2.0f, 3.5f), 0.11f, 12);
                    k.Cylinder(D, new Vector3(0f, 2.0f, 3.5f), new Vector3(0f, 2.0f, 3.75f), 0.16f, 12); // muzzle brake
                    k.Box(M, new Vector3(0f, 1.62f, 3.25f), new Vector3(0.35f, 0.35f, 0.2f), 0.04f); // travel lock
                    foreach (float sx in new[] { -1f, 1f }) k.Box(P, new Vector3(sx * 1.22f, 2.0f, -1.2f), new Vector3(0.15f, 0.6f, 1.6f), 0.04f); // turret bins
                    k.Cylinder(P, new Vector3(0.6f, 2.48f, -1.4f), new Vector3(0.6f, 2.55f, -1.4f), 0.3f, 14);
                    k.Box(G, new Vector3(-0.6f, 2.45f, -0.1f), new Vector3(0.3f, 0.15f, 0.2f), 0.02f);
                    k.Cylinder(M, new Vector3(-0.8f, 2.5f, -2.0f), new Vector3(-0.8f, 4.2f, -2.2f), 0.012f, 4);
                    k.Box(D, new Vector3(0f, 1.6f, -3.05f), new Vector3(1.6f, 0.5f, 0.05f), 0.02f);
                    foreach (float sx in new[] { -1f, 1f }) k.Box(L, new Vector3(sx * 0.95f, 1.45f, 3.3f), new Vector3(0.18f, 0.12f, 0.05f), 0.02f);
                    FitTracked(k, 1.45f, new Vector3(0f, 1.95f, -0.9f), new Vector3(2.3f, 1.05f, 2.9f), new Vector3(0f, 2.0f, 0.95f), 1.5f, 0.15f);
                    // Fume extractor on the barrel and a recoil spade folded up at the back.
                    k.Cylinder(M, new Vector3(0f, 2.0f, 2.25f), new Vector3(0f, 2.0f, 2.8f), 0.16f, 12);
                    k.Box(M, new Vector3(0f, 0.75f, -3.25f), new Vector3(2.2f, 0.5f, 0.08f), 0.02f, Quaternion.Euler(20f, 0f, 0f));
                    foreach (float sx in new[] { -1f, 1f }) k.Box(M, new Vector3(sx * 0.8f, 1.0f, -3.1f), new Vector3(0.12f, 0.12f, 0.5f), 0.02f);
                    break;
                }
                default:
                    // Radar truck: cab, chassis, an equipment shelter and a mast (the antenna is added separately).
                    BuildTruckBase(k);
                    k.Box(P, new Vector3(0f, 1.85f, -1.2f), new Vector3(2.3f, 1.3f, 3.2f), 0.08f);
                    k.Box(D, new Vector3(1.16f, 1.8f, -0.6f), new Vector3(0.03f, 1.0f, 0.7f), 0.01f); // shelter door
                    k.Box(M, new Vector3(-1.17f, 2.0f, -1.6f), new Vector3(0.03f, 0.5f, 0.8f), 0.01f); // vent
                    k.Cylinder(M, new Vector3(0f, 2.5f, -1.2f), new Vector3(0f, 3.4f, -1.2f), 0.14f, 10); // mast
                    foreach (float sx in new[] { -1f, 1f })
                        foreach (float z in new[] { -2.9f, 0.2f })
                        {
                            // Outriggers: a beam out from the chassis, a jack down to a pad on the ground.
                            k.Box(M, new Vector3(sx * 0.85f, 0.8f, z), new Vector3(0.9f, 0.14f, 0.16f), 0.02f);
                            k.Cylinder(M, new Vector3(sx * 1.25f, 0.85f, z), new Vector3(sx * 1.25f, 0.1f, z), 0.06f, 8);
                            k.Box(D, new Vector3(sx * 1.25f, 0.04f, z), new Vector3(0.35f, 0.06f, 0.35f), 0.02f);
                        }
                    break;
            }
            return k;
        }

        // A three-axle truck: chassis, cab with windows, bonnet, bumper, lights, mirrors, fuel tank, wheels.
        private static void BuildTruckBase(VehicleKit k)
        {
            const VehicleKit.Slot P = VehicleKit.Slot.Paint, M = VehicleKit.Slot.Metal, D = VehicleKit.Slot.Dark, G = VehicleKit.Slot.Glass, L = VehicleKit.Slot.Light;
            foreach (float sx in new[] { -1f, 1f })
            {
                k.Wheel(new Vector3(sx * 1.05f, 0.5f, 2.35f), 0.5f, 0.38f, sx);
                k.Wheel(new Vector3(sx * 1.05f, 0.5f, -0.6f), 0.5f, 0.38f, sx);
                k.Wheel(new Vector3(sx * 1.05f, 0.5f, -1.95f), 0.5f, 0.38f, sx);
                k.Box(D, new Vector3(sx * 0.45f, 0.85f, 0f), new Vector3(0.18f, 0.25f, 6.6f), 0.02f); // chassis rails
                k.Box(P, new Vector3(sx * 1.08f, 1.08f, 2.35f), new Vector3(0.5f, 0.08f, 1.2f), 0.02f); // mudguards
                k.Box(L, new Vector3(sx * 0.85f, 1.25f, 3.42f), new Vector3(0.22f, 0.16f, 0.05f), 0.02f);
                k.Box(D, new Vector3(sx * 1.3f, 2.2f, 1.95f), new Vector3(0.05f, 0.3f, 0.2f), 0.01f); // mirrors
                k.Cylinder(M, new Vector3(sx * 1.22f, 2.05f, 1.9f), new Vector3(sx * 1.3f, 2.2f, 1.95f), 0.015f, 4);
            }
            k.Box(P, new Vector3(0f, 1.7f, 1.75f), new Vector3(2.3f, 1.45f, 1.5f), 0.1f); // cab
            k.Box(P, new Vector3(0f, 1.25f, 2.95f), new Vector3(2.0f, 0.75f, 0.9f), 0.1f); // bonnet
            k.Box(G, new Vector3(0f, 2.05f, 2.51f), new Vector3(1.9f, 0.55f, 0.04f), 0.02f, Quaternion.Euler(-8f, 0f, 0f)); // windscreen
            foreach (float sx in new[] { -1f, 1f }) k.Box(G, new Vector3(sx * 1.16f, 2.05f, 1.85f), new Vector3(0.04f, 0.5f, 0.85f), 0.02f);
            k.Box(D, new Vector3(0f, 1.25f, 3.42f), new Vector3(1.1f, 0.5f, 0.04f), 0.01f); // grille
            k.Box(M, new Vector3(0f, 0.85f, 3.45f), new Vector3(2.3f, 0.2f, 0.15f), 0.03f); // bumper
            k.Cylinder(M, new Vector3(-0.85f, 0.95f, 0.6f), new Vector3(-0.85f, 0.95f, 1.4f), 0.28f, 14); // fuel tank
            k.Cylinder(D, new Vector3(0.95f, 1.6f, 1.0f), new Vector3(0.95f, 2.6f, 1.0f), 0.06f, 8); // exhaust
            for (int i = 0; i < 6; i++) k.Box(M, new Vector3(-0.45f + i * 0.18f, 1.25f, 3.445f), new Vector3(0.035f, 0.46f, 0.02f), 0.004f); // grille bars
            k.Cylinder(M, new Vector3(-0.5f, 0.86f, 3.58f), new Vector3(0.5f, 0.86f, 3.58f), 0.09f, 10); // winch drum
            foreach (float sx in new[] { -1f, 1f })
            {
                k.Box(D, new Vector3(sx * 0.85f, 1.25f, 3.4f), new Vector3(0.32f, 0.26f, 0.05f), 0.02f); // lamp housings
                k.Box(M, new Vector3(sx * 1.2f, 0.75f, 1.75f), new Vector3(0.22f, 0.04f, 0.6f), 0.01f); // cab steps
                k.Box(D, new Vector3(sx * 1.156f, 1.45f, 1.4f), new Vector3(0.012f, 0.9f, 0.025f), 0f); // door seams
                k.Box(D, new Vector3(sx * 1.156f, 1.45f, 2.3f), new Vector3(0.012f, 0.9f, 0.025f), 0f);
                k.Box(M, new Vector3(sx * 1.17f, 1.55f, 1.55f), new Vector3(0.03f, 0.04f, 0.16f), 0.01f); // door handles
                k.Box(VehicleKit.Slot.Rubber, new Vector3(sx * 1.05f, 0.55f, -2.6f), new Vector3(0.42f, 0.6f, 0.03f), 0.005f); // mud flaps
                k.Box(D, new Vector3(sx * 0.95f, 0.8f, 0.9f - sx * 0.2f), new Vector3(0.4f, 0.4f, 0.7f), 0.03f); // tool lockers
                k.Box(P, new Vector3(sx * 1.08f, 1.08f, -1.27f), new Vector3(0.5f, 0.05f, 2.3f), 0.015f); // rear mudguards
            }
            k.Cylinder(P, new Vector3(0.3f, 2.43f, 1.7f), new Vector3(0.3f, 2.49f, 1.7f), 0.3f, 14); // roof hatch
            k.Box(P, new Vector3(0f, 2.44f, 2.45f), new Vector3(2.1f, 0.05f, 0.25f), 0.015f); // sun visor
        }

        // Fittings shared by the tracked hulls: mudguards, spare links, tools, tow cable, exhaust, lamp guards, a
        // turret basket, smoke dischargers and a sleeve on the gun.
        private static void FitTracked(VehicleKit k, float deck, Vector3 turret, Vector3 turretSize, Vector3 gunRoot, float sleeve, float sleeveRadius)
        {
            const VehicleKit.Slot P = VehicleKit.Slot.Paint, R = VehicleKit.Slot.Rubber, M = VehicleKit.Slot.Metal, D = VehicleKit.Slot.Dark, C = VehicleKit.Slot.Canvas;
            foreach (float sx in new[] { -1f, 1f })
            {
                k.Box(P, new Vector3(sx * 1.3f, 1.12f, 3.0f), new Vector3(0.6f, 0.04f, 0.9f), 0.01f, Quaternion.Euler(18f, 0f, 0f)); // front mudguards
                k.Box(P, new Vector3(sx * 1.3f, 1.1f, -3.05f), new Vector3(0.6f, 0.04f, 0.7f), 0.01f, Quaternion.Euler(-14f, 0f, 0f));
                k.Box(D, new Vector3(sx * 0.95f, deck - 0.26f, 3.13f), new Vector3(0.3f, 0.22f, 0.04f), 0.01f); // lamp housings
                foreach (float o in new[] { -0.12f, 0.12f }) k.Cylinder(M, new Vector3(sx * 0.95f + o, deck - 0.38f, 3.16f), new Vector3(sx * 0.95f + o, deck - 0.14f, 3.16f), 0.01f, 4, false);
                // Smoke dischargers on the turret cheeks.
                for (int i = 0; i < 3; i++)
                {
                    Vector3 a = turret + new Vector3(sx * (turretSize.x * 0.5f + 0.06f), 0.05f + i * 0.02f, turretSize.z * 0.25f - i * 0.2f);
                    k.Cylinder(M, a, a + new Vector3(sx * 0.12f, 0.16f, 0.28f), 0.055f, 8);
                }
                // Side skirt panel joints.
                for (int i = 0; i < 4; i++) k.Box(D, new Vector3(sx * 1.545f, 1.1f, -2.2f + i * 1.45f), new Vector3(0.012f, 0.5f, 0.03f), 0f);
            }
            // Spare track links across the nose, a tow cable along the right side, tools on the left.
            for (int i = 0; i < 5; i++) k.Box(R, new Vector3(-0.8f + i * 0.4f, deck - 0.42f, 3.02f), new Vector3(0.34f, 0.07f, 0.16f), 0.015f, Quaternion.Euler(-22f, 0f, 0f));
            k.Cylinder(M, new Vector3(1.05f, deck + 0.03f, -2.2f), new Vector3(1.05f, deck + 0.03f, 2.2f), 0.022f, 5, false);
            k.Cylinder(M, new Vector3(1.05f, deck + 0.03f, 2.2f), new Vector3(0.85f, deck + 0.03f, 2.45f), 0.022f, 5, false);
            k.Cylinder(C, new Vector3(-1.05f, deck + 0.04f, -1.4f), new Vector3(-1.05f, deck + 0.04f, -0.2f), 0.022f, 6); // shovel shaft
            k.Box(M, new Vector3(-1.05f, deck + 0.04f, 0f), new Vector3(0.2f, 0.02f, 0.3f), 0.006f);
            k.Box(C, new Vector3(-1.05f, deck + 0.05f, 0.8f), new Vector3(0.1f, 0.08f, 0.8f), 0.01f); // unditching beam
            // Exhaust with a grille and a heat shield.
            k.Box(M, new Vector3(1.38f, deck - 0.2f, -1.9f), new Vector3(0.14f, 0.3f, 0.9f), 0.03f);
            for (int i = 0; i < 5; i++) k.Box(D, new Vector3(1.455f, deck - 0.2f, -2.2f + i * 0.15f), new Vector3(0.01f, 0.22f, 0.05f), 0f);
            // Turret basket with stowed kit, a hatch ring with a machine gun, lifting eyes.
            float back = turret.z - turretSize.z * 0.5f;
            foreach (float y in new[] { -0.12f, 0.12f })
            {
                k.Cylinder(M, turret + new Vector3(-turretSize.x * 0.45f, y, -turretSize.z * 0.5f - 0.45f), turret + new Vector3(turretSize.x * 0.45f, y, -turretSize.z * 0.5f - 0.45f), 0.015f, 4, false);
                foreach (float sx in new[] { -1f, 1f })
                    k.Cylinder(M, new Vector3(turret.x + sx * turretSize.x * 0.45f, turret.y + y, back), new Vector3(turret.x + sx * turretSize.x * 0.45f, turret.y + y, back - 0.45f), 0.015f, 4, false);
            }
            k.Box(C, new Vector3(turret.x, turret.y, back - 0.24f), new Vector3(turretSize.x * 0.8f, 0.3f, 0.36f), 0.1f);
            float top = turret.y + turretSize.y * 0.5f;
            k.Cylinder(M, new Vector3(turret.x - 0.3f, top, turret.z - 0.3f), new Vector3(turret.x - 0.3f, top + 0.1f, turret.z - 0.3f), 0.3f, 14);
            k.Cylinder(D, new Vector3(turret.x - 0.3f, top + 0.1f, turret.z - 0.3f), new Vector3(turret.x - 0.3f, top + 0.13f, turret.z - 0.3f), 0.24f, 14);
            k.Box(D, new Vector3(turret.x - 0.3f, top + 0.28f, turret.z + 0.05f), new Vector3(0.1f, 0.12f, 0.5f), 0.02f);
            k.Cylinder(D, new Vector3(turret.x - 0.3f, top + 0.29f, turret.z + 0.3f), new Vector3(turret.x - 0.3f, top + 0.3f, turret.z + 1.05f), 0.025f, 6);
            // Thermal sleeve on the gun.
            k.Cylinder(M, gunRoot, gunRoot + new Vector3(0f, 0.02f, sleeve), sleeveRadius, 12);
            foreach (float f in new[] { 0.3f, 0.65f, 1f }) k.Cylinder(D, gunRoot + new Vector3(0f, 0.02f * f, sleeve * f - 0.03f), gunRoot + new Vector3(0f, 0.02f * f, sleeve * f + 0.03f), sleeveRadius + 0.012f, 12);
        }

        private static void FitWheeled(VehicleKit k)
        {
            const VehicleKit.Slot P = VehicleKit.Slot.Paint, M = VehicleKit.Slot.Metal, D = VehicleKit.Slot.Dark, G = VehicleKit.Slot.Glass, C = VehicleKit.Slot.Canvas;
            k.Box(P, new Vector3(0f, 1.0f, 3.62f), new Vector3(2.3f, 0.6f, 0.04f), 0.01f, Quaternion.Euler(62f, 0f, 0f)); // trim vane folded on the nose
            foreach (float sx in new[] { -1f, 1f })
            {
                k.Box(D, new Vector3(sx * 0.9f, 1.55f, 3.2f), new Vector3(0.3f, 0.2f, 0.06f), 0.015f); // lamp housings
                k.Cylinder(M, new Vector3(sx * 1.25f, 1.95f, 2.3f), new Vector3(sx * 1.55f, 2.15f, 2.4f), 0.015f, 4, false); // mirrors
                k.Box(D, new Vector3(sx * 1.57f, 2.22f, 2.4f), new Vector3(0.04f, 0.26f, 0.16f), 0.01f);
                k.Cylinder(M, new Vector3(sx * 1.36f, 1.0f, -3.0f), new Vector3(sx * 1.36f, 1.0f, -0.6f), 0.06f, 8); // exhaust / hand rail low on the side
                for (int i = 0; i < 3; i++) k.Box(P, new Vector3(sx * 1.36f, 1.7f, -2.6f + i * 1.0f), new Vector3(0.1f, 0.3f, 0.7f), 0.03f); // stowage boxes
                for (int i = 0; i < 4; i++) k.Cylinder(M, new Vector3(sx * 1.36f, 2.02f, -1.6f + i * 0.9f), new Vector3(sx * 1.36f, 2.12f, -1.6f + i * 0.9f), 0.02f, 5); // grab handles
                // Smoke dischargers beside the weapon station.
                for (int i = 0; i < 3; i++) k.Cylinder(M, new Vector3(0.4f + sx * 0.52f, 2.3f, 0.6f - i * 0.16f), new Vector3(0.4f + sx * 0.66f, 2.44f, 0.86f - i * 0.16f), 0.045f, 8);
            }
            foreach (float z in new[] { -2.2f, -0.8f }) k.Cylinder(D, new Vector3(-0.5f, 2.16f, z), new Vector3(-0.5f, 2.18f, z), 0.24f, 14); // hatch rims
            k.Cylinder(C, new Vector3(0.9f, 2.14f, -1.6f), new Vector3(0.9f, 2.14f, -0.3f), 0.022f, 6); // shovel
            k.Box(M, new Vector3(0.9f, 2.14f, -0.15f), new Vector3(0.2f, 0.02f, 0.3f), 0.006f);
            k.Cylinder(M, new Vector3(0.9f, 2.1f, -2.9f), new Vector3(0.9f, 3.6f, -3.05f), 0.012f, 4);
            foreach (float sx in new[] { -0.75f, 0.75f }) k.Box(P, new Vector3(sx, 1.25f, -3.42f), new Vector3(0.36f, 0.48f, 0.18f), 0.04f); // jerrycans on the tail
            k.Box(G, new Vector3(0.15f, 2.38f, 0.87f), new Vector3(0.27f, 0.17f, 0.02f), 0.005f);
            k.Cylinder(M, new Vector3(0.5f, 2.35f, 0.85f), new Vector3(0.5f, 2.37f, 1.5f), 0.075f, 10); // barrel jacket
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

    /// <summary>Wheels that turn and track runs that slide as a range vehicle drives.</summary>
    public sealed class VehicleRunningGear : MonoBehaviour
    {
        [SerializeField] private Transform[] wheels;
        [SerializeField] private float[] radii;
        [SerializeField] private Transform trackTop, trackBottom;
        private float _distance;

        public void Set(Transform[] wheelTransforms, float[] wheelRadii, Transform top, Transform bottom)
        {
            wheels = wheelTransforms;
            radii = wheelRadii;
            trackTop = top;
            trackBottom = bottom;
        }

        /// <summary>Turns the wheels and runs the tracks for <paramref name="metres"/> driven forwards (negative: back).</summary>
        public void Advance(float metres)
        {
            _distance += metres;
            if (wheels != null)
                for (int i = 0; i < wheels.Length; i++)
                    if (wheels[i] != null) wheels[i].localRotation = Quaternion.Euler(_distance / Mathf.Max(0.05f, radii[i]) * Mathf.Rad2Deg, 0f, 0f);
            // Over the ground the bottom run stands still, so on the hull it slides back; the top run slides forward.
            float slide = Mathf.Repeat(_distance, VehicleKit.CleatPitch);
            if (trackTop != null) trackTop.localPosition = new Vector3(0f, 0f, slide);
            if (trackBottom != null) trackBottom.localPosition = new Vector3(0f, 0f, -slide);
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
        private VehicleRunningGear _gear;
        private Vector3 _lastPosition;
        private bool _moved;
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
                _gear = _model.GetComponent<VehicleRunningGear>();
                _moved = false;
                _shownDisabled = false;
            }
            // Active: on its lane. Disabled: the wreck veers off the road (clear of the convoy behind it).
            _director.ViewPose(Slot, time, out var p, out float yaw);
            // Sit on the slope: lean with the ground rather than staying level across it.
            transform.SetPositionAndRotation(p, Quaternion.FromToRotation(Vector3.up, _director.GroundNormal(p)) * Quaternion.Euler(0f, yaw, 0f));
            // Wheels turn and tracks run by the distance actually driven (a wreck being pushed aside does neither).
            if (t.Active && _moved && _gear != null)
            {
                float driven = Vector3.Dot(p - _lastPosition, transform.forward);
                if (Mathf.Abs(driven) < 3f) _gear.Advance(driven);
            }
            _lastPosition = p;
            _moved = t.Active;
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
            if (on)
            {
                // The hit: a flash and a burst of sparks, then small flames under the smoke while it burns.
                if (_burst == null) _burst = MakeBurst(transform);
                if (_fire == null) _fire = MakeFire(transform);
                _burst.Play(true);
                _fire.Play();
            }
            else if (_fire != null) _fire.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            if (on) _smoke.Play();
            else _smoke.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            if (on)
            {
                if (_scorch == null) _scorch = MakeScorch();
                _scorch.SetPositionAndRotation(transform.position + Vector3.up * 0.05f, Quaternion.Euler(90f, (Slot * 73) % 360, 0f));
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
        public static Material SmokeOverride, ScorchOverride, FlashOverride;
        private ParticleSystem _burst, _fire;
        private static Material _flashMat;

        /// <summary>Additive glow for flashes, sparks and flames.</summary>
        public static Material NewFlashMaterial()
        {
            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
            var m = new Material(sh) { name = "Range Flash" };
            MakeTransparent(m);
            m.SetFloat("_Blend", 2f); // additive
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            m.SetTexture("_BaseMap", SoftDisc);
            m.mainTexture = SoftDisc;
            m.SetColor("_BaseColor", Color.white);
            return m;
        }

        public static Material FlashMaterial => FlashOverride != null ? FlashOverride : _flashMat != null ? _flashMat : (_flashMat = NewFlashMaterial());

        private static ParticleSystem Particles(Transform parent, string name, Vector3 local)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = FlashMaterial;
            r.shadowCastingMode = ShadowCastingMode.Off;
            return ps;
        }

        // One flash plus sparks thrown out and falling.
        private static ParticleSystem MakeBurst(Transform parent)
        {
            var flash = Particles(parent, "Hit Flash", Vector3.up * 1.5f);
            var main = flash.main;
            main.loop = false;
            main.duration = 0.3f;
            main.startLifetime = 0.18f;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(7f, 9f);
            main.startColor = new Color(1f, 0.85f, 0.55f, 1f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = flash.emission;
            em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, 1), new ParticleSystem.Burst(0.05f, 1) });
            var size = flash.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.4f));
            var col = flash.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.5f, 0.15f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = g;

            var sparks = Particles(flash.transform, "Sparks", Vector3.zero);
            var sm = sparks.main;
            sm.loop = false;
            sm.duration = 0.3f;
            sm.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 1.1f);
            sm.startSpeed = new ParticleSystem.MinMaxCurve(7f, 18f);
            sm.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.18f);
            sm.startColor = new Color(1f, 0.7f, 0.3f, 1f);
            sm.gravityModifier = 1.2f;
            sm.simulationSpace = ParticleSystemSimulationSpace.World;
            sm.maxParticles = 80;
            var se = sparks.emission;
            se.rateOverTime = 0f;
            se.SetBursts(new[] { new ParticleSystem.Burst(0f, 55) });
            var shape = sparks.shape;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = 0.6f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            var sr = sparks.GetComponent<ParticleSystemRenderer>();
            sr.renderMode = ParticleSystemRenderMode.Stretch;
            sr.velocityScale = 0.06f;
            sr.lengthScale = 1.5f;
            var sc = sparks.colorOverLifetime;
            sc.enabled = true;
            var g2 = new Gradient();
            g2.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.85f, 0.5f), 0f), new GradientColorKey(new Color(0.9f, 0.25f, 0.05f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            sc.color = g2;
            return flash;
        }

        // Small flames licking up from the wreck under the smoke.
        private static ParticleSystem MakeFire(Transform parent)
        {
            var fire = Particles(parent, "Fire", Vector3.up * 1.2f);
            var main = fire.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1f, 2.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.6f, 1.3f);
            main.startColor = new Color(1f, 0.55f, 0.2f, 0.85f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 40;
            var em = fire.emission;
            em.rateOverTime = 18f;
            var shape = fire.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(1.6f, 0.3f, 3f);
            var size = fire.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.2f));
            var col = fire.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.8f, 0.4f), 0f), new GradientColorKey(new Color(0.8f, 0.2f, 0.05f), 1f) },
                new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            return fire;
        }

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
