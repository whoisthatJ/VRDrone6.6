using System;
using System.Collections.Generic;
using UnityEngine;

namespace GateRush.Track
{
    [Serializable]
    public sealed class GateData
    {
        /// <summary>Gate prefab type: "Square", "Large", "Hurdle".</summary>
        public string Type = "Square";
        /// <summary>World position of the gate's base (feet).</summary>
        public Vector3 Position;
        /// <summary>Heading in degrees (0 = fly towards +Z).</summary>
        public float Yaw;
        /// <summary>Tilt in degrees (for dive/ramp gates).</summary>
        public float Pitch;
        public float Scale = 1f;
    }

    [Serializable]
    public sealed class PropData
    {
        /// <summary>Prop prefab type: "Container", "Crate", "Pillar", "Wall", "Barrel".</summary>
        public string Type = "Crate";
        public Vector3 Position;
        public Vector3 Euler;
        public Vector3 Scale = Vector3.one;
    }

    [Serializable]
    public sealed class SpawnData
    {
        public Vector3 Position = new Vector3(0f, 0.15f, 0f);
        public float Yaw;
    }

    /// <summary>
    /// A race course as plain data: gates in race order (index 0 = start/finish), optional props, spawn.
    /// Stored as JSON (built-in tracks in Data/Tracks, user tracks in persistentDataPath/tracks).
    /// The environment (scene) supplies the static world; the track only adds gates and props.
    /// </summary>
    [Serializable]
    public sealed class TrackData
    {
        public const int CurrentVersion = 1;

        public int Version = CurrentVersion;
        public string Id;
        public string Name;
        public string Author;
        /// <summary>Environment id this course is built for ("field", "hangar", "industrial").</summary>
        public string Environment = "field";
        public int Laps = 3;
        public SpawnData Spawn = new SpawnData();
        public List<GateData> Gates = new List<GateData>();
        public List<PropData> Props = new List<PropData>();

        public string ToJson(bool pretty = true) => JsonUtility.ToJson(this, pretty);

        public static TrackData FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new ArgumentException("Empty track JSON");
            var t = new TrackData();
            JsonUtility.FromJsonOverwrite(json, t);
            t.Gates ??= new List<GateData>();
            t.Props ??= new List<PropData>();
            t.Spawn ??= new SpawnData();
            return t;
        }

        /// <summary>Returns problems that would stop the track from being raced (empty list = fine).</summary>
        public List<string> Validate(ICollection<string> knownGateTypes = null, ICollection<string> knownPropTypes = null)
        {
            var errors = new List<string>();
            if (Version > CurrentVersion) errors.Add($"Track version {Version} is newer than this game ({CurrentVersion})");
            if (string.IsNullOrWhiteSpace(Id)) errors.Add("Missing id");
            if (Gates.Count == 0) errors.Add("A track needs at least one gate");
            if (Laps < 1 || Laps > 10) errors.Add($"Laps must be 1-10 (is {Laps})");
            for (int i = 0; i < Gates.Count; i++)
            {
                var g = Gates[i];
                if (knownGateTypes != null && !knownGateTypes.Contains(g.Type)) errors.Add($"Gate {i}: unknown type '{g.Type}'");
                if (g.Scale <= 0f || g.Scale > 10f) errors.Add($"Gate {i}: scale {g.Scale} out of range");
            }
            if (knownPropTypes != null)
                for (int i = 0; i < Props.Count; i++)
                    if (!knownPropTypes.Contains(Props[i].Type)) errors.Add($"Prop {i}: unknown type '{Props[i].Type}'");
            return errors;
        }

        public static Quaternion GateRotation(GateData g) => Quaternion.Euler(g.Pitch, g.Yaw, 0f);
    }
}
