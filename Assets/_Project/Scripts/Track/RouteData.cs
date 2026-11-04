using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ushqyr.Track
{
    /// <summary>A route spline knot. Zero tangents = auto-smooth (Catmull-Rom like) through the neighbours.</summary>
    [Serializable]
    public sealed class RouteKnot
    {
        /// <summary>World position of the flight line (centre of the corridor, at flight height).</summary>
        public Vector3 Position;
        /// <summary>Bezier tangent towards the previous knot, relative to <see cref="Position"/>.</summary>
        public Vector3 TangentIn;
        /// <summary>Bezier tangent towards the next knot, relative to <see cref="Position"/>.</summary>
        public Vector3 TangentOut;
        /// <summary>P3: roll about the route at this knot, degrees (180 = inverted, the top of a loop). Interpolated between knots.</summary>
        public float Roll;

        public bool AutoTangents => TangentIn == Vector3.zero && TangentOut == Vector3.zero;
    }

    /// <summary>An obstacle placed by distance along the route, aligned with the route's heading.</summary>
    [Serializable]
    public sealed class RouteObstacle
    {
        /// <summary>Gate or prop type from <see cref="TrackPrefabs"/> ("Square", "Large", "Hurdle", "Crate"...).</summary>
        public string Type = "Square";
        /// <summary>Metres along the route from knot 0.</summary>
        public float Distance;
        /// <summary>Metres to the right of the centre line (negative = left).</summary>
        public float Lateral;
        /// <summary>Metres above the lane deck (ground, or the bridge deck on elevated sections).</summary>
        public float Height;
        /// <summary>Extra heading in degrees on top of the route's heading.</summary>
        public float Yaw;
        public float Scale = 1f;
        /// <summary>Optional tint name or #RRGGBB ("red", "green", "blue", ...). Empty = the prefab's own colour.</summary>
        public string Color = "";
        /// <summary>Counts for the race (flown in route order). Off = scenery.</summary>
        public bool IsCheckpoint = true;
        /// <summary>
        /// P3: turn with the route's full 3D frame (tangent and roll), with the opening centred on the flight line:
        /// rings on climbs, dives, vertical and inverted sections. Off = the old placement (yaw only, on the deck).
        /// </summary>
        public bool Align3D;
    }

    /// <summary>
    /// The spline part of a track: a closed 3D route (the corridor's centre line at flight height) and everything placed
    /// along it. Lane lights, chevrons, supports and checkpoints are generated from it by <see cref="TrackBuilder"/>.
    /// The start pad is <see cref="TrackData.Spawn"/>.
    /// </summary>
    [Serializable]
    public sealed class RouteData
    {
        public List<RouteKnot> Knots = new List<RouteKnot>();
        public bool Closed = true;
        /// <summary>Distance between the two inner (warm white) lane lines, metres.</summary>
        public float CorridorWidth = 4f;
        /// <summary>Gap between an inner (warm white) line and its outer (red) line, metres.</summary>
        public float OuterLineGap = 0.35f;
        /// <summary>Normal height of the flight line above the lane deck; anything higher raises the deck onto supports.</summary>
        public float FlightHeight = 1.5f;
        /// <summary>Height of the ground under the route.</summary>
        public float GroundHeight;
        /// <summary>Index into <see cref="Obstacles"/> of the finish (start/finish line, race gate 0); -1 = the first checkpoint after the start pad.</summary>
        public int FinishObstacle = -1;
        public List<RouteObstacle> Obstacles = new List<RouteObstacle>();
        /// <summary>
        /// A city course (Sphere District): no lane lights, chevrons, raised decks or wear marks; obstacles stand on the
        /// ground where the line is low and float on the flight line where it climbs (air gates), and the next gate gets
        /// a beacon because buildings hide it.
        /// </summary>
        public bool AirCourse;

        public bool HasRoute => Knots != null && Knots.Count >= 3;
    }
}
