using System;
using System.Collections.Generic;
using GateRush.Track;
using UnityEngine;

namespace GateRush.Game
{
    /// <summary>Built-in tracks (JSON) and the environment scene each one is raced in.</summary>
    [CreateAssetMenu(menuName = "GateRush/Track Catalog", fileName = "TrackCatalog")]
    public sealed class TrackCatalog : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            public string Id;
            public string DisplayName;
            [TextArea] public string Description;
            public TextAsset Json;
        }

        [Serializable]
        public sealed class EnvironmentScene
        {
            public string Environment;
            public string SceneName;
        }

        public List<Entry> Tracks = new List<Entry>();
        public List<EnvironmentScene> Environments = new List<EnvironmentScene>();

        public Entry Find(string id)
        {
            foreach (var t in Tracks)
                if (t.Id == id) return t;
            return null;
        }

        public TrackData Load(Entry entry) => entry != null && entry.Json != null ? TrackData.FromJson(entry.Json.text) : null;

        public string SceneFor(string environment)
        {
            foreach (var e in Environments)
                if (e.Environment == environment) return e.SceneName;
            return null;
        }

        /// <summary>First built-in track for an environment (fallback when the selected track belongs elsewhere).</summary>
        public Entry FirstFor(string environment)
        {
            foreach (var t in Tracks)
            {
                var data = Load(t);
                if (data != null && data.Environment == environment) return t;
            }
            return null;
        }
    }
}
