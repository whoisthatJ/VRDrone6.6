using UnityEngine;

namespace GateRush.Track
{
    /// <summary>
    /// Editable asset form of a spline track: the full <see cref="TrackData"/> (route knots, corridor, obstacles, start
    /// pad = spawn, environment) plus the JSON file it is exported to. The game loads the JSON (catalog, user library);
    /// the asset is for editing in Unity and for <c>Tools > Track > Rebuild</c>.
    /// </summary>
    [CreateAssetMenu(menuName = "GateRush/Track Definition", fileName = "TrackDefinition")]
    public sealed class TrackDefinition : ScriptableObject
    {
        public TrackData Track = new TrackData();
        [Tooltip("Exported JSON (what the catalog and the game load).")]
        public TextAsset Json;

        public string ToJson() => Track.ToJson();

        /// <summary>Replaces the track with parsed JSON (same rules as loading a track file).</summary>
        public void LoadJson(string json) => Track = TrackData.FromJson(json);
    }
}
