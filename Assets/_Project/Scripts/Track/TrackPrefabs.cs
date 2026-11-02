using System;
using System.Collections.Generic;
using UnityEngine;

namespace GateRush.Track
{
    /// <summary>Maps the type names used in track JSON to gate and prop prefabs.</summary>
    [CreateAssetMenu(menuName = "GateRush/Track Prefabs", fileName = "TrackPrefabs")]
    public sealed class TrackPrefabs : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public string Type;
            public GameObject Prefab;
        }

        /// <summary>Materials for the geometry <see cref="TrackBuilder"/> generates along a spline route.</summary>
        [Serializable]
        public sealed class RouteKitMaterials
        {
            [Tooltip("Inner lane line LEDs (warm white, HDR emissive).")]
            public Material LaneWarm;
            [Tooltip("Outer lane line LEDs (red, HDR emissive).")]
            public Material LaneRed;
            [Tooltip("Lane posts, clamps and cables.")]
            public Material Hardware;
            [Tooltip("Black-and-white chevron boards on the centre line.")]
            public Material Chevron;
            [Tooltip("Supports and truss bridges under elevated sections.")]
            public Material Support;
            [Tooltip("Ground wear marks under obstacles.")]
            public Material Wear;
            [Tooltip("Start pad mat.")]
            public Material StartPad;
            [Tooltip("Trainer racing line (GateRush/LED; its colour strip is generated per track).")]
            public Material RacingLine;
        }

        public List<Entry> Gates = new List<Entry>();
        public List<Entry> Props = new List<Entry>();
        public RouteKitMaterials RouteKit = new RouteKitMaterials();

        public GameObject Gate(string type) => Find(Gates, type);
        public GameObject Prop(string type) => Find(Props, type);

        public List<string> GateTypes() => Types(Gates);
        public List<string> PropTypes() => Types(Props);

        private static GameObject Find(List<Entry> list, string type)
        {
            for (int i = 0; i < list.Count; i++)
                if (list[i].Type == type) return list[i].Prefab;
            return null;
        }

        private static List<string> Types(List<Entry> list)
        {
            var result = new List<string>(list.Count);
            foreach (var e in list) result.Add(e.Type);
            return result;
        }
    }
}
