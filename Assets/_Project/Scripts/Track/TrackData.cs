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
    /// A spline track has a <see cref="Route"/> instead of gates: its obstacles, lanes and checkpoints are generated.
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
        /// <summary>Spline route (empty for gate-list tracks).</summary>
        public RouteData Route = new RouteData();

        public bool IsSplineTrack => Route != null && Route.HasRoute;

        public string ToJson(bool pretty = true) => JsonUtility.ToJson(this, pretty);

        public static TrackData FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new ArgumentException("Empty track JSON");
            var t = new TrackData();
            JsonUtility.FromJsonOverwrite(json, t);
            t.Gates ??= new List<GateData>();
            t.Props ??= new List<PropData>();
            t.Spawn ??= new SpawnData();
            t.Route ??= new RouteData();
            t.Route.Knots ??= new List<RouteKnot>();
            t.Route.Obstacles ??= new List<RouteObstacle>();
            return t;
        }

        /// <summary>Returns problems that would stop the track from being raced (empty list = fine).</summary>
        public List<string> Validate(ICollection<string> knownGateTypes = null, ICollection<string> knownPropTypes = null)
        {
            var errors = new List<string>();
            if (Version > CurrentVersion) errors.Add($"Track version {Version} is newer than this game ({CurrentVersion})");
            if (string.IsNullOrWhiteSpace(Id)) errors.Add("Missing id");
            if (IsSplineTrack) ValidateRoute(errors, knownGateTypes, knownPropTypes);
            else if (Gates.Count == 0) errors.Add("A track needs at least one gate");
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

        private void ValidateRoute(List<string> errors, ICollection<string> knownGateTypes, ICollection<string> knownPropTypes)
        {
            var r = Route;
            if (r.CorridorWidth < 1f || r.CorridorWidth > 20f) errors.Add($"Route: corridor width {r.CorridorWidth} out of range (1-20 m)");
            if (r.FinishObstacle >= r.Obstacles.Count) errors.Add($"Route: finish obstacle {r.FinishObstacle} does not exist");
            int checkpoints = 0;
            for (int i = 0; i < r.Obstacles.Count; i++)
            {
                var o = r.Obstacles[i];
                if (o.IsCheckpoint) checkpoints++;
                bool known = knownGateTypes == null || knownGateTypes.Contains(o.Type) || (knownPropTypes != null && knownPropTypes.Contains(o.Type));
                if (!known) errors.Add($"Obstacle {i}: unknown type '{o.Type}'");
                if (o.Scale <= 0f || o.Scale > 10f) errors.Add($"Obstacle {i}: scale {o.Scale} out of range");
                if (o.Distance < 0f) errors.Add($"Obstacle {i}: negative distance");
            }
            if (checkpoints == 0) errors.Add("Route: needs at least one checkpoint obstacle");
        }

        public static Quaternion GateRotation(GateData g) => Quaternion.Euler(g.Pitch, g.Yaw, 0f);
    }
}
