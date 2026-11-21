using UnityEngine;

namespace Ushqyr.Garage
{
    /// <summary>What a drone of the Education Suite is, for its looks and its equipment.</summary>
    public enum EduDroneKind { Camera900, Camera250, Delivery, EduSmall }

    /// <summary>
    /// The looks of the Education Suite's drones (EDU_PLAN F3), put together from simple shapes at run time, in metres:
    /// a folding camera drone with a gimbal under its nose, the same at 250 g, a delivery drone with its box on a
    /// hook, and a small education drone with propeller guards. Generic designs of our own. +Z is forward; the origin
    /// is the drone's centre. Propellers are separate children in mixer order (rear right, front right, rear left,
    /// front left) so they can spin.
    /// </summary>
    public static class EduDroneModel
    {
        public sealed class Result
        {
            public GameObject Root;
            public Transform[] Props = new Transform[4];
            /// <summary>Turns to point the camera up and down (camera drones); null on the others.</summary>
            public Transform Gimbal;
            /// <summary>Where the lens is, for the on-board view.</summary>
            public Transform Lens;
            /// <summary>The delivery drone's load; hidden when it has been dropped. Null on the others.</summary>
            public Transform Payload;
            /// <summary>Half the widest span, propellers included (m): the size to use for collisions.</summary>
            public float Radius;
        }

        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        /// <summary>The kind for a catalogue id ("edu-camera900", …); false when the id is not one of these drones.</summary>
        public static bool TryKind(string quadId, out EduDroneKind kind)
        {
            switch (quadId)
            {
                case "edu-camera900": kind = EduDroneKind.Camera900; return true;
                case "edu-camera250": kind = EduDroneKind.Camera250; return true;
                case "edu-delivery": kind = EduDroneKind.Delivery; return true;
                case "edu-small": kind = EduDroneKind.EduSmall; return true;
                default: kind = default; return false;
            }
        }

        public static Result Build(EduDroneKind kind, QuadModelMaterials mats, Transform parent, int layer = 0)
        {
            var res = new Result { Root = new GameObject("Drone: " + kind) { layer = layer } };
            Transform root = res.Root.transform;
            root.SetParent(parent, false);
            var b = new Builder { Root = root, Layer = layer, Mats = mats };
            switch (kind)
            {
                case EduDroneKind.Camera900: Camera(b, res, 1f); break;
                case EduDroneKind.Camera250: Camera(b, res, 0.6f); break;
                case EduDroneKind.Delivery: Delivery(b, res); break;
                default: Edu(b, res); break;
            }
            return res;
        }

        private sealed class Builder
        {
            public Transform Root;
            public int Layer;
            public QuadModelMaterials Mats;
            private readonly System.Collections.Generic.Dictionary<(Material, Color), Material> _coloured =
                new System.Collections.Generic.Dictionary<(Material, Color), Material>();

            /// <summary>The material in this colour; one copy for each pair, shared by every part that uses it.</summary>
            private Material Coloured(Material material, Color color)
            {
                if (material == null) return null;
                if (_coloured.TryGetValue((material, color), out Material made)) return made;
                made = new Material(material) { name = material.name + " (drone)" };
                if (made.HasProperty(BaseColor)) made.SetColor(BaseColor, color);
                if (made.HasProperty(ColorId)) made.SetColor(ColorId, color);
                // The parts' own textures (carbon weave, labels) do not belong on these plain shapes.
                foreach (string texture in new[] { "_BaseMap", "_MainTex", "_BumpMap" })
                    if (made.HasProperty(texture)) made.SetTexture(texture, null);
                _coloured[(material, color)] = made;
                return made;
            }

            public Transform Shape(PrimitiveType type, string name, Transform parent, Vector3 position, Vector3 size, Material material, Color color, Vector3 euler = default)
            {
                GameObject go = GameObject.CreatePrimitive(type);
                go.name = name;
                go.layer = Layer;
                Collider collider = go.GetComponent<Collider>();
                if (collider != null)
                {
                    if (Application.isPlaying) Object.Destroy(collider); else Object.DestroyImmediate(collider);
                }
                Transform t = go.transform;
                t.SetParent(parent != null ? parent : Root, false);
                t.localPosition = position;
                t.localEulerAngles = euler;
                // Unity's cylinder is 2 units tall: "size.y" here is the real height.
                t.localScale = type == PrimitiveType.Cylinder ? new Vector3(size.x, size.y * 0.5f, size.z) : size;
                var renderer = go.GetComponent<MeshRenderer>();
                if (material != null) renderer.sharedMaterial = Coloured(material, color);
                return t;
            }

            public Transform Empty(string name, Transform parent, Vector3 position)
            {
                var go = new GameObject(name) { layer = Layer };
                go.transform.SetParent(parent != null ? parent : Root, false);
                go.transform.localPosition = position;
                return go.transform;
            }

            /// <summary>Four arms, motors and two-blade propellers at (±x, y, ±z); returns the propellers in mixer order.</summary>
            public void Rotors(Result res, float x, float z, float y, float armThickness, float motorRadius, float propRadius, Color armColor, Color propColor, bool guards)
            {
                // Mixer order: rear right, front right, rear left, front left.
                var corners = new[] { new Vector3(x, y, -z), new Vector3(x, y, z), new Vector3(-x, y, -z), new Vector3(-x, y, z) };
                for (int i = 0; i < 4; i++)
                {
                    Vector3 c = corners[i];
                    var flat = new Vector3(c.x, 0f, c.z);
                    float length = flat.magnitude;
                    float yaw = Mathf.Atan2(c.x, c.z) * Mathf.Rad2Deg;
                    Shape(PrimitiveType.Cube, "Arm " + i, null, new Vector3(c.x * 0.5f, y - motorRadius * 0.6f, c.z * 0.5f), new Vector3(armThickness, armThickness * 0.7f, length),
                        Mats.Plastic, armColor, new Vector3(0f, yaw, 0f));
                    Shape(PrimitiveType.Cylinder, "Motor " + i, null, c, new Vector3(motorRadius * 2f, motorRadius * 1.3f, motorRadius * 2f), Mats.Metal, new Color(0.25f, 0.25f, 0.27f));
                    Transform prop = Empty("Prop " + i, null, c + Vector3.up * (motorRadius * 0.8f));
                    Shape(PrimitiveType.Cube, "Blades", prop, Vector3.zero, new Vector3(propRadius * 2f, propRadius * 0.03f, propRadius * 0.16f), Mats.Prop, propColor);
                    Shape(PrimitiveType.Cylinder, "Hub", prop, Vector3.zero, new Vector3(motorRadius * 0.9f, motorRadius * 0.5f, motorRadius * 0.9f), Mats.Metal, new Color(0.3f, 0.3f, 0.32f));
                    res.Props[i] = prop;
                    if (!guards) continue;
                    // Propeller guards: a bumper on the outside of each rotor, where it would meet a wall.
                    Vector3 outward = flat.normalized;
                    Shape(PrimitiveType.Cube, "Guard " + i, null, c + outward * (propRadius * 1.08f) + Vector3.up * (motorRadius * 0.6f),
                        new Vector3(propRadius * 1.9f, motorRadius * 0.9f, propRadius * 0.12f), Mats.Plastic, armColor, new Vector3(0f, yaw, 0f));
                }
                res.Radius = new Vector2(x, z).magnitude + propRadius * (guards ? 1.15f : 1f);
            }
        }

        private static void Camera(Builder b, Result res, float k)
        {
            var body = new Color(0.72f, 0.74f, 0.77f);
            var dark = new Color(0.18f, 0.19f, 0.21f);
            b.Shape(PrimitiveType.Cube, "Body", null, Vector3.zero, new Vector3(0.09f, 0.055f, 0.20f) * k, b.Mats.Plastic, body);
            b.Shape(PrimitiveType.Cube, "Battery", null, new Vector3(0f, 0.032f, -0.02f) * k, new Vector3(0.07f, 0.012f, 0.12f) * k, b.Mats.Plastic, dark);
            b.Shape(PrimitiveType.Sphere, "Status light", null, new Vector3(0f, 0.01f, -0.10f) * k, Vector3.one * 0.012f * k, b.Mats.Led, new Color(0.2f, 1f, 0.3f));
            b.Rotors(res, 0.124f * k, 0.124f * k, 0.03f * k, 0.022f * k, 0.018f * k, 0.108f * k, dark, new Color(0.12f, 0.12f, 0.13f), false);
            // Legs under the front arms.
            foreach (float side in new[] { -1f, 1f })
                b.Shape(PrimitiveType.Cube, "Leg", null, new Vector3(0.10f * side, -0.02f, 0.10f) * k, new Vector3(0.012f, 0.06f, 0.012f) * k, b.Mats.Plastic, dark);
            // The gimbal hangs under the nose; it turns about its own side-to-side axis.
            res.Gimbal = b.Empty("Gimbal", null, new Vector3(0f, -0.035f, 0.085f) * k);
            b.Shape(PrimitiveType.Sphere, "Camera", res.Gimbal, Vector3.zero, Vector3.one * 0.046f * k, b.Mats.Plastic, dark);
            b.Shape(PrimitiveType.Cylinder, "Lens", res.Gimbal, new Vector3(0f, 0f, 0.022f) * k, new Vector3(0.022f, 0.012f, 0.022f) * k, b.Mats.Lens, new Color(0.05f, 0.07f, 0.12f), new Vector3(90f, 0f, 0f));
            res.Lens = b.Empty("Lens point", res.Gimbal, new Vector3(0f, 0f, 0.03f) * k);
        }

        private static void Delivery(Builder b, Result res)
        {
            var body = new Color(0.20f, 0.21f, 0.24f);
            var mark = new Color(1f, 0.55f, 0.15f);
            b.Shape(PrimitiveType.Cube, "Body", null, Vector3.zero, new Vector3(0.20f, 0.09f, 0.30f), b.Mats.Plastic, body);
            b.Shape(PrimitiveType.Cube, "Stripe", null, new Vector3(0f, 0.046f, 0.06f), new Vector3(0.20f, 0.004f, 0.05f), b.Mats.Plastic, mark);
            b.Shape(PrimitiveType.Sphere, "Status light", null, new Vector3(0f, 0.02f, -0.15f), Vector3.one * 0.02f, b.Mats.Led, new Color(0.2f, 1f, 0.3f));
            b.Rotors(res, 0.25f, 0.25f, 0.06f, 0.035f, 0.03f, 0.19f, body, new Color(0.1f, 0.1f, 0.11f), false);
            // Skids tall enough for the box to hang clear of the ground.
            foreach (float side in new[] { -1f, 1f })
            {
                b.Shape(PrimitiveType.Cube, "Skid", null, new Vector3(0.16f * side, -0.26f, 0f), new Vector3(0.02f, 0.02f, 0.36f), b.Mats.Metal, new Color(0.3f, 0.3f, 0.32f));
                foreach (float end in new[] { -1f, 1f })
                    b.Shape(PrimitiveType.Cube, "Strut", null, new Vector3(0.13f * side, -0.15f, 0.12f * end), new Vector3(0.015f, 0.22f, 0.015f), b.Mats.Metal, new Color(0.3f, 0.3f, 0.32f),
                        new Vector3(0f, 0f, -14f * side));
            }
            b.Shape(PrimitiveType.Cylinder, "Hook", null, new Vector3(0f, -0.065f, 0f), new Vector3(0.02f, 0.04f, 0.02f), b.Mats.Metal, new Color(0.6f, 0.6f, 0.62f));
            res.Payload = b.Empty("Payload", null, new Vector3(0f, -0.165f, 0f));
            b.Shape(PrimitiveType.Cube, "Box", res.Payload, Vector3.zero, new Vector3(0.18f, 0.16f, 0.18f), b.Mats.Plastic, mark);
            b.Shape(PrimitiveType.Cube, "Tape", res.Payload, Vector3.zero, new Vector3(0.184f, 0.03f, 0.184f), b.Mats.Plastic, new Color(0.9f, 0.9f, 0.85f));
            res.Lens = b.Empty("Lens point", null, new Vector3(0f, 0.0f, 0.16f));
        }

        private static void Edu(Builder b, Result res)
        {
            var body = new Color(0.92f, 0.93f, 0.95f);
            var trim = new Color(0.15f, 0.45f, 0.95f);
            b.Shape(PrimitiveType.Cube, "Body", null, Vector3.zero, new Vector3(0.045f, 0.035f, 0.10f), b.Mats.Plastic, body);
            b.Shape(PrimitiveType.Cube, "Top", null, new Vector3(0f, 0.019f, -0.005f), new Vector3(0.04f, 0.004f, 0.07f), b.Mats.Plastic, trim);
            // Programmable light on top, and the two cameras and two distance sensors students read.
            b.Shape(PrimitiveType.Sphere, "LED", null, new Vector3(0f, 0.024f, 0.02f), Vector3.one * 0.012f, b.Mats.Led, new Color(0.3f, 0.8f, 1f));
            b.Shape(PrimitiveType.Cylinder, "Front camera", null, new Vector3(0f, 0.004f, 0.051f), new Vector3(0.012f, 0.004f, 0.012f), b.Mats.Lens, new Color(0.05f, 0.07f, 0.12f), new Vector3(90f, 0f, 0f));
            b.Shape(PrimitiveType.Cube, "Front sensor", null, new Vector3(0f, -0.008f, 0.051f), new Vector3(0.016f, 0.006f, 0.003f), b.Mats.Lens, new Color(0.1f, 0.02f, 0.02f));
            b.Shape(PrimitiveType.Cylinder, "Down camera", null, new Vector3(0f, -0.018f, 0.01f), new Vector3(0.010f, 0.003f, 0.010f), b.Mats.Lens, new Color(0.05f, 0.07f, 0.12f));
            b.Rotors(res, 0.045f, 0.045f, 0.012f, 0.010f, 0.007f, 0.038f, body, trim, true);
            res.Lens = b.Empty("Lens point", null, new Vector3(0f, 0.004f, 0.054f));
        }
    }
}
