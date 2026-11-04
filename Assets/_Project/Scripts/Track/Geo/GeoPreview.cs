using UnityEngine;

namespace Ushqyr.Track.Geo
{
    /// <summary>
    /// Scene-view gizmos of the imported OSM data: roads (grey, by width), building footprints and heights (hero
    /// buildings orange), parks and grass (green), parking (blue), water (cyan) and trees (dots). Editor-only aid; it
    /// draws nothing in the game.
    /// </summary>
    [ExecuteAlways]
    public sealed class GeoPreview : MonoBehaviour
    {
        public GeoDataAsset Data;
        public bool Roads = true, Buildings = true, Areas = true, Trees = true;
        [Tooltip("Draw building heights as vertical edges (slower).")]
        public bool Heights = true;

        private static Vector3 P(Vector2 p, float y = 0.2f) => new Vector3(p.x, y, p.y);

        private void OnDrawGizmos()
        {
            if (Data == null || Data.Data == null) return;
            var d = Data.Data;
            if (Areas)
                foreach (var a in d.Areas)
                {
                    Gizmos.color = a.Kind switch
                    {
                        "parking" => new Color(0.3f, 0.5f, 1f),
                        "water" => Color.cyan,
                        "park" or "grass" or "garden" or "recreation_ground" or "meadow" => new Color(0.3f, 0.85f, 0.3f),
                        _ => new Color(0.6f, 0.6f, 0.4f),
                    };
                    Loop(a.Polygon, 0.15f);
                }
            if (Roads)
                foreach (var r in d.Roads)
                {
                    Gizmos.color = r.Width >= 10f ? new Color(0.9f, 0.9f, 0.9f) : new Color(0.55f, 0.55f, 0.55f);
                    for (int i = 0; i + 1 < r.Points.Count; i++) Gizmos.DrawLine(P(r.Points[i]), P(r.Points[i + 1]));
                }
            if (Buildings)
                foreach (var b in d.Buildings)
                {
                    Gizmos.color = b.Hero ? new Color(1f, 0.55f, 0.15f) : b.HeightEstimated ? new Color(0.8f, 0.5f, 0.5f) : new Color(0.85f, 0.85f, 1f);
                    Loop(b.Footprint, 0.3f);
                    if (!Heights) continue;
                    Loop(b.Footprint, b.Height);
                    for (int i = 0; i < b.Footprint.Count; i += Mathf.Max(1, b.Footprint.Count / 4))
                        Gizmos.DrawLine(P(b.Footprint[i], 0.3f), P(b.Footprint[i], b.Height));
                }
            if (Trees)
            {
                Gizmos.color = new Color(0.1f, 0.6f, 0.15f);
                foreach (var t in d.Trees) Gizmos.DrawLine(P(t, 0f), P(t, 4f));
            }
        }

        private static void Loop(System.Collections.Generic.List<Vector2> pts, float y)
        {
            for (int i = 0; i < pts.Count; i++) Gizmos.DrawLine(P(pts[i], y), P(pts[(i + 1) % pts.Count], y));
        }
    }
}
