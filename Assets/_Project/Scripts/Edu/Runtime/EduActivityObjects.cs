using System.Collections.Generic;
using UnityEngine;

namespace Ushqyr.Edu
{
    /// <summary>
    /// The things an activity puts into the scene (EDU_PLAN §4, "on-screen guidance"): a see-through box to hover in,
    /// a pad to land on, poles and flags to fly round, gates to fly through, targets to point the camera at, markers
    /// on the ground. They are built from simple shapes where the activity's frame puts them. Poles, flags, gates
    /// and targets are solid; boxes, pads and markers are only pictures.
    /// </summary>
    public static class EduActivityObjects
    {
        private static readonly Color Guide = new Color(0.25f, 0.85f, 1f, 0.16f);
        private static readonly Color GuideEdge = new Color(0.25f, 0.85f, 1f, 0.9f);
        private static readonly Color Solid = new Color(1f, 0.55f, 0.15f);

        public static Transform Build(List<ActivityObject> objects, FlightFrame frame, Transform parent, LayerMask trackGeometry, List<Object> made)
        {
            var root = new GameObject("Activity Objects").transform;
            root.SetParent(parent, false);
            root.SetPositionAndRotation(new Vector3(frame.X, frame.Y, frame.Z), Quaternion.Euler(0f, frame.HeadingDeg, 0f));
            if (objects == null || objects.Count == 0) return root;

            // Solid objects go on a layer the drone's collision sweep looks at.
            int solidLayer = 0;
            for (int i = 0; i < 32; i++)
                if ((trackGeometry.value & (1 << i)) != 0) { solidLayer = i; break; }
            Material clear = new Material(Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit")) { name = "Activity Guide" };
            Material paint = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Sprites/Default")) { name = "Activity Paint" };
            made.Add(clear);
            made.Add(paint);

            foreach (ActivityObject o in objects)
            {
                var holder = new GameObject(o.Type + (o.Id.Length > 0 ? " " + o.Id : "")).transform;
                holder.SetParent(root, false);
                holder.localPosition = new Vector3((float)o.X, (float)o.Y, (float)o.Z);
                holder.localRotation = Quaternion.Euler(0f, (float)o.HeadingDeg, 0f);
                float size = (float)o.Size, height = (float)o.Height;
                switch (o.Type)
                {
                    case "box":
                        // A volume in the air: faint faces and bright edges, so its limits can be judged from any side.
                        Shape(PrimitiveType.Cube, holder, Vector3.zero, new Vector3(size, height, size), clear, Guide, false, 0);
                        Edges(holder, new Vector3(size, height, size), clear);
                        break;
                    case "pad":
                        Shape(PrimitiveType.Cylinder, holder, new Vector3(0f, 0.01f, 0f), new Vector3(size, 0.02f, size), paint, new Color(0.15f, 0.16f, 0.18f), false, 0);
                        Shape(PrimitiveType.Cylinder, holder, new Vector3(0f, 0.022f, 0f), new Vector3(size * 0.72f, 0.004f, size * 0.72f), paint, Color.white, false, 0);
                        Shape(PrimitiveType.Cylinder, holder, new Vector3(0f, 0.026f, 0f), new Vector3(size * 0.5f, 0.004f, size * 0.5f), paint, Solid, false, 0);
                        break;
                    case "marker":
                        Shape(PrimitiveType.Cylinder, holder, new Vector3(0f, 0.012f, 0f), new Vector3(size, 0.02f, size), paint, GuideEdge, false, 0);
                        break;
                    case "pole":
                        Shape(PrimitiveType.Cylinder, holder, new Vector3(0f, height * 0.5f, 0f), new Vector3(size, height, size), paint, Solid, true, solidLayer);
                        Shape(PrimitiveType.Cylinder, holder, new Vector3(0f, height - 0.15f, 0f), new Vector3(size * 1.15f, 0.2f, size * 1.15f), paint, Color.white, false, 0);
                        break;
                    case "flag":
                        Shape(PrimitiveType.Cylinder, holder, new Vector3(0f, height * 0.5f, 0f), new Vector3(0.04f, height, 0.04f), paint, Color.white, true, solidLayer);
                        Shape(PrimitiveType.Cube, holder, new Vector3(size * 0.25f, height - size * 0.2f, 0f), new Vector3(size * 0.5f, size * 0.35f, 0.01f), paint, Solid, false, 0);
                        break;
                    case "gate":
                    {
                        // An opening "size" wide and "height" tall, its bottom at the object's own height.
                        float post = 0.08f;
                        foreach (float side in new[] { -1f, 1f })
                            Shape(PrimitiveType.Cube, holder, new Vector3(side * (size + post) * 0.5f, height * 0.5f, 0f), new Vector3(post, height + post, post), paint, Solid, true, solidLayer);
                        Shape(PrimitiveType.Cube, holder, new Vector3(0f, height + post * 0.5f, 0f), new Vector3(size + 2f * post, post, post), paint, Solid, true, solidLayer);
                        if (o.Y > 0.05)
                            Shape(PrimitiveType.Cube, holder, new Vector3(0f, -post * 0.5f, 0f), new Vector3(size + 2f * post, post, post), paint, Solid, true, solidLayer);
                        break;
                    }
                    case "target":
                        // A bucket on a stand: the camera must look into its mouth, which faces along the object's heading.
                        Shape(PrimitiveType.Cylinder, holder, new Vector3(0f, -0.5f * (float)o.Y, 0f), new Vector3(0.05f, Mathf.Max(0.05f, (float)o.Y), 0.05f), paint, Color.white, true, solidLayer);
                        Shape(PrimitiveType.Cylinder, holder, Vector3.zero, new Vector3(size, size * 0.6f, size), paint, new Color(0.9f, 0.9f, 0.92f), true, solidLayer, new Vector3(90f, 0f, 0f));
                        Shape(PrimitiveType.Cylinder, holder, new Vector3(0f, 0f, size * 0.305f), new Vector3(size * 0.8f, 0.004f, size * 0.8f), paint, new Color(0.1f, 0.1f, 0.12f), false, 0, new Vector3(90f, 0f, 0f));
                        Shape(PrimitiveType.Cylinder, holder, new Vector3(0f, 0f, size * 0.31f), new Vector3(size * 0.3f, 0.004f, size * 0.3f), paint, Solid, false, 0, new Vector3(90f, 0f, 0f));
                        break;
                }
            }
            return root;
        }

        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static MaterialPropertyBlock _block;

        private static Transform Shape(PrimitiveType type, Transform parent, Vector3 position, Vector3 size, Material material, Color color, bool solid, int layer, Vector3 euler = default)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            Transform t = go.transform;
            t.SetParent(parent, false);
            t.localPosition = position;
            t.localEulerAngles = euler;
            t.localScale = type == PrimitiveType.Cylinder ? new Vector3(size.x, size.y * 0.5f, size.z) : size;
            Collider collider = go.GetComponent<Collider>();
            if (!solid) Object.Destroy(collider);
            else go.layer = layer;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = solid ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            _block ??= new MaterialPropertyBlock();
            _block.SetColor(BaseColor, color);
            _block.SetColor(ColorId, color);
            renderer.SetPropertyBlock(_block);
            return t;
        }

        /// <summary>The twelve edges of a box, as thin bright bars.</summary>
        private static void Edges(Transform parent, Vector3 size, Material material)
        {
            const float w = 0.03f;
            Vector3 h = size * 0.5f;
            foreach (float a in new[] { -1f, 1f })
                foreach (float b in new[] { -1f, 1f })
                {
                    Shape(PrimitiveType.Cube, parent, new Vector3(0f, a * h.y, b * h.z), new Vector3(size.x, w, w), material, GuideEdge, false, 0);
                    Shape(PrimitiveType.Cube, parent, new Vector3(a * h.x, 0f, b * h.z), new Vector3(w, size.y, w), material, GuideEdge, false, 0);
                    Shape(PrimitiveType.Cube, parent, new Vector3(a * h.x, b * h.y, 0f), new Vector3(w, w, size.z), material, GuideEdge, false, 0);
                }
        }
    }
}
