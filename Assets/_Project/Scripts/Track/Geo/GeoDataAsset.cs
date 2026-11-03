using UnityEngine;

namespace GateRush.Track.Geo
{
    /// <summary>The imported OSM data in local metres (written by the Sphere District OSM importer).</summary>
    public sealed class GeoDataAsset : ScriptableObject
    {
        public GeoData Data = new GeoData();
    }
}
