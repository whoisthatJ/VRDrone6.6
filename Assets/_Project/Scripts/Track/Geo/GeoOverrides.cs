using System;
using System.Collections.Generic;
using UnityEngine;

namespace GateRush.Track.Geo
{
    /// <summary>
    /// Manual fixes on top of the OSM extract: drop wrong footprints, set missing heights, and mark the footprints the
    /// hero generators build (sphere, petal pavilions, pods) so the generic extruder skips them. The importer fills
    /// the hero entries on first import; edits made in the inspector are kept on re-import.
    /// </summary>
    public sealed class GeoOverrides : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            public long Id;
            public string Note = "";
            public bool Exclude;
            /// <summary>Metres; 0 keeps the OSM / default height.</summary>
            public float Height;
            public bool Hero;
            /// <summary>"sphere", "pavilion", "pod" or "canopy" (which generator owns the footprint).</summary>
            public string HeroRole = "";
        }

        public List<Entry> Entries = new List<Entry>();

        public Entry Find(long id) => Entries.Find(e => e.Id == id);

        public void Apply(GeoData data)
        {
            var byId = new Dictionary<long, Entry>();
            foreach (var e in Entries) byId[e.Id] = e;
            data.Buildings.RemoveAll(b => byId.TryGetValue(b.Id, out var e) && e.Exclude);
            foreach (var b in data.Buildings)
            {
                if (!byId.TryGetValue(b.Id, out var e)) continue;
                if (e.Height > 0f)
                {
                    b.Height = e.Height;
                    b.HeightEstimated = false;
                }
                b.Hero = e.Hero;
                b.HeroRole = e.HeroRole ?? "";
            }
        }
    }
}
