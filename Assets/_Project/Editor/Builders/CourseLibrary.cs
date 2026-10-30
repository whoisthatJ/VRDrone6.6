using System.Collections.Generic;
using System.IO;
using GateRush.Track;
using UnityEditor;
using UnityEngine;

namespace GateRush.EditorTools.Builders
{
    /// <summary>
    /// The built-in courses, defined here and written to Assets/_Project/Data/Tracks/*.json.
    /// The JSON files are what the game loads (and what the M7 editor will read and write).
    /// Gate headings are computed from the neighbouring gates so the line flows.
    /// </summary>
    public static class CourseLibrary
    {
        public const string TrackDir = "Assets/_Project/Data/Tracks";

        private struct G
        {
            public string Type;
            public Vector3 Pos;
            public G(string type, float x, float y, float z) { Type = type; Pos = new Vector3(x, y, z); }
        }

        public static IEnumerable<TrackData> All()
        {
            yield return Field();
            yield return Hangar();
            yield return Industrial();
        }

        [MenuItem("GateRush/Builders/Write Built-in Track JSON")]
        public static List<string> WriteAll()
        {
            Directory.CreateDirectory(TrackDir);
            var paths = new List<string>();
            foreach (var t in All())
            {
                string path = $"{TrackDir}/{t.Id}.json";
                File.WriteAllText(path, t.ToJson());
                paths.Add(path);
            }
            AssetDatabase.Refresh();
            return paths;
        }

        public static TrackData Field() => Make("field", "Open Field", "field", 0f, new Vector3(0f, 0.15f, 0f), new[]
        {
            new G("Square", 0f, 0f, 14f),
            new G("Square", 22f, 0f, 45f),
            new G("Square", 55f, 3f, 62f),
            new G("Hurdle", 88f, 0f, 42f),
            new G("Large", 82f, 0f, 2f),
            new G("Square", 52f, 0f, -26f),
            new G("Large", 16f, 0f, -32f),
            new G("Square", -18f, 0f, -12f),
        });

        public static TrackData Hangar()
        {
            var t = Make("hangar", "Hangar 7", "hangar", 90f, new Vector3(-10f, 0.15f, -14f), new[]
            {
                new G("Square", 0f, 0f, -14f),
                new G("Square", 24f, 0f, -14f),
                new G("Large", 38f, 0f, 0f),
                new G("Square", 24f, 0f, 14f),
                new G("Square", 5f, 3f, 14f),
                new G("Hurdle", -15f, 0f, 14f),
                new G("Large", -36f, 0f, 0f),
                new G("Square", -20f, 0f, -8f),
            });
            Prop(t, "Crate", -5f, 0f, 0f, 0f, new Vector3(2f, 2f, 2f));
            Prop(t, "Crate", -5f, 2f, 0.3f, 15f, new Vector3(1.2f, 1.2f, 1.2f));
            Prop(t, "Crate", 8f, 0f, 2f, 0f, new Vector3(3f, 1.5f, 2f));
            Prop(t, "Crate", 14f, 0f, -3f, 20f, new Vector3(2f, 3f, 2f));
            Prop(t, "Barrel", -28f, 0f, -12f, 0f, Vector3.one);
            Prop(t, "Barrel", -27f, 0f, -13f, 0f, Vector3.one);
            Prop(t, "Barrel", 32f, 0f, 12f, 0f, Vector3.one);
            Prop(t, "Wall", 30f, 0f, -4f, 90f, Vector3.one);
            return t;
        }

        public static TrackData Industrial()
        {
            var t = Make("industrial", "Dockyard", "industrial", 0f, new Vector3(0f, 0.15f, 0f), new[]
            {
                new G("Square", 0f, 0f, 15f),
                new G("Large", 0f, 0f, 50f),
                new G("Square", 25f, 0f, 78f),
                new G("Square", 60f, 4f, 82f),
                new G("Hurdle", 90f, 0f, 55f),
                new G("Large", 82f, 0f, 18f),
                new G("Square", 52f, 0f, -2f),
                new G("Square", 22f, 0f, -14f),
            });
            // Container corridor around gate 1, two high on the outside.
            string[] kinds = { "Container", "ContainerRed", "ContainerGreen" };
            int k = 0;
            for (float z = 32f; z <= 68f; z += 6.5f)
            {
                Prop(t, kinds[k++ % 3], -7.5f, 0f, z, 0f, Vector3.one);
                Prop(t, kinds[k++ % 3], 7.5f, 0f, z, 0f, Vector3.one);
                if (((int)z) % 2 == 0)
                {
                    Prop(t, kinds[k++ % 3], -10.2f, 0f, z, 0f, Vector3.one);
                    Prop(t, kinds[k++ % 3], -10.2f, 2.59f, z, 0f, Vector3.one);
                }
            }
            // A stack under the floating gate 3, and a few near the back straight.
            Prop(t, "ContainerRed", 60f, 0f, 87f, 90f, Vector3.one);
            Prop(t, "Container", 60f, 2.59f, 87f, 90f, Vector3.one);
            Prop(t, "ContainerGreen", 70f, 0f, 8f, 30f, Vector3.one);
            Prop(t, "Barrel", 38f, 0f, -8f, 0f, Vector3.one);
            Prop(t, "Barrel", 39f, 0f, -9f, 0f, Vector3.one);
            return t;
        }

        private static void Prop(TrackData t, string type, float x, float y, float z, float yaw, Vector3 scale) =>
            t.Props.Add(new PropData { Type = type, Position = new Vector3(x, y, z), Euler = new Vector3(0f, yaw, 0f), Scale = scale });

        private static TrackData Make(string id, string name, string env, float startYaw, Vector3 spawn, G[] gates)
        {
            var t = new TrackData { Id = id, Name = name, Author = "GateRush", Environment = env, Laps = 3 };
            t.Spawn.Position = spawn;
            t.Spawn.Yaw = startYaw;
            for (int i = 0; i < gates.Length; i++)
            {
                float yaw;
                if (i == 0) yaw = startYaw;
                else
                {
                    Vector3 d = gates[(i + 1) % gates.Length].Pos - gates[i - 1].Pos;
                    yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
                }
                t.Gates.Add(new GateData { Type = gates[i].Type, Position = gates[i].Pos, Yaw = Mathf.Round(yaw * 10f) / 10f });
            }
            return t;
        }
    }
}
