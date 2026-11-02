using System.Collections.Generic;
using GateRush.Race;
using UnityEngine;

namespace GateRush.Track
{
    /// <summary>Result of instantiating a track.</summary>
    public sealed class LoadedTrack
    {
        public TrackData Data;
        public Transform Root;
        public Gate[] Gates;
        public Transform Spawn;
        /// <summary>The sampled route of a spline track (null for gate-list tracks).</summary>
        public TrackRoute Route;
        /// <summary>Trainer racing line (spline tracks; inactive until route guidance turns it on).</summary>
        public GameObject RacingLine;
        /// <summary>Colliders of the lane posts (spline tracks; off with Ghost lanes).</summary>
        public GameObject LaneColliders;
    }

    /// <summary>Instantiates a <see cref="TrackData"/> (gates in order, props, spawn point) under a new root.</summary>
    public static class TrackLoader
    {
        public static LoadedTrack Load(TrackData data, TrackPrefabs prefabs, Transform parent = null)
        {
            if (data.IsSplineTrack)
            {
                int layer = LayerMask.NameToLayer("TrackGeometry");
                return TrackBuilder.Build(data, prefabs, parent, layer < 0 ? 0 : layer);
            }

            var root = new GameObject("Track: " + data.Name).transform;
            root.SetParent(parent, false);

            var gates = new List<Gate>(data.Gates.Count);
            for (int i = 0; i < data.Gates.Count; i++)
            {
                var g = data.Gates[i];
                var prefab = prefabs.Gate(g.Type);
                if (prefab == null)
                {
                    Debug.LogWarning($"[GateRush] Track '{data.Id}': unknown gate type '{g.Type}', using Square");
                    prefab = prefabs.Gate("Square");
                }
                var go = Object.Instantiate(prefab, g.Position, TrackData.GateRotation(g), root);
                go.name = $"Gate {i} ({g.Type})";
                go.transform.localScale = Vector3.one * g.Scale;
                var gate = go.GetComponent<Gate>();
                gate.Order = i;
                gates.Add(gate);
            }

            AddProps(data, prefabs, root);

            var spawn = new GameObject("Spawn").transform;
            spawn.SetParent(root, false);
            spawn.SetPositionAndRotation(data.Spawn.Position, Quaternion.Euler(0f, data.Spawn.Yaw, 0f));

            return new LoadedTrack { Data = data, Root = root, Gates = gates.ToArray(), Spawn = spawn };
        }

        /// <summary>Scenery props from the track's prop list.</summary>
        public static void AddProps(TrackData data, TrackPrefabs prefabs, Transform root)
        {
            for (int i = 0; i < data.Props.Count; i++)
            {
                var p = data.Props[i];
                var prefab = prefabs.Prop(p.Type);
                if (prefab == null)
                {
                    Debug.LogWarning($"[GateRush] Track '{data.Id}': unknown prop type '{p.Type}', skipped");
                    continue;
                }
                var go = Object.Instantiate(prefab, p.Position, Quaternion.Euler(p.Euler), root);
                go.name = $"Prop {i} ({p.Type})";
                go.transform.localScale = p.Scale;
            }
        }
    }
}
