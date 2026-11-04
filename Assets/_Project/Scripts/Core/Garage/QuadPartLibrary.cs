using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ushqyr.Garage
{
    /// <summary>
    /// P2: the detailed quad part meshes generated in Blender from the garage part data (Tools/Blender/quadgen.py via
    /// Ushqyr > Quads > Generate Quad Parts): one mesh per frame, motor, prop (and its blur disc), battery, plus the
    /// shared electronics. Each sub-mesh is tagged with a material slot name; frames carry named sockets (motor
    /// mounts, stack, camera, top plate, antenna, receiver). <see cref="QuadModelBuilder"/> uses a part's mesh when it
    /// is here and falls back to the procedural shape otherwise. Loaded from Resources.
    /// </summary>
    public sealed class QuadPartLibrary : ScriptableObject
    {
        public const string ResourcePath = "QuadPartLibrary";

        [Serializable]
        public sealed class Socket
        {
            public string Name;
            public Vector3 Position;
        }

        [Serializable]
        public sealed class Entry
        {
            public string Id;
            public Mesh Mesh;
            /// <summary>Material slot of each sub-mesh, in order (Carbon, Edge, Metal, Bell, Prop, ...).</summary>
            public string[] Slots = new string[0];
            public List<Socket> Sockets = new List<Socket>();

            public bool TryGetSocket(string name, out Vector3 position)
            {
                foreach (var s in Sockets)
                    if (s.Name == name)
                    {
                        position = s.Position;
                        return true;
                    }
                position = default;
                return false;
            }
        }

        public List<Entry> Entries = new List<Entry>();

        private Dictionary<string, Entry> _byId;
        private static QuadPartLibrary _instance;
        private static bool _loaded;

        /// <summary>The library in Resources, or null when the parts haven't been generated.</summary>
        public static QuadPartLibrary Instance
        {
            get
            {
                if (!_loaded || (_instance == null && Application.isEditor))
                {
                    _instance = Resources.Load<QuadPartLibrary>(ResourcePath);
                    _loaded = true;
                }
                return _instance;
            }
        }

        public Entry Find(string id)
        {
            if (_byId == null || _byId.Count != Entries.Count)
            {
                _byId = new Dictionary<string, Entry>();
                foreach (var e in Entries)
                    if (e != null && !string.IsNullOrEmpty(e.Id)) _byId[e.Id] = e;
            }
            return id != null && _byId.TryGetValue(id, out var entry) && entry.Mesh != null ? entry : null;
        }

        /// <summary>Forgets the cached instance (after regenerating the parts in the Editor).</summary>
        public static void Reload()
        {
            _instance = null;
            _loaded = false;
        }
    }
}
