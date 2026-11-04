using System;
using System.Collections.Generic;
using Ushqyr.Track;
using UnityEngine;

namespace Ushqyr.Game
{
    /// <summary>Built-in tracks (JSON) and the environment scene each one is raced in.</summary>
    [CreateAssetMenu(menuName = "Ushqyr/Track Catalog", fileName = "TrackCatalog")]
    public sealed class TrackCatalog : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            public string Id;
            public string DisplayName;
            [TextArea] public string Description;
            public TextAsset Json;
            /// <summary>Environment (venue) the track is raced in, as in its JSON.</summary>
            public string Environment;
            /// <summary>Short name within its venue (Sprint, Pro, Free flight ...).</summary>
            public string Variation;
            /// <summary>Menu preview (an aerial view of the course).</summary>
            public Texture2D Preview;
        }

        [Serializable]
        public sealed class EnvironmentScene
        {
            public string Environment;
            public string SceneName;
            /// <summary>Venue name in the menu (one entry per scene).</summary>
            public string DisplayName;
            [TextArea] public string Description;
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

        public EnvironmentScene Venue(string environment)
        {
            foreach (var e in Environments)
                if (e.Environment == environment) return e;
            return null;
        }

        /// <summary>The built-in tracks of a venue, in catalogue order.</summary>
        public List<Entry> TracksFor(string environment)
        {
            var list = new List<Entry>();
            foreach (var t in Tracks)
                if (EnvironmentOf(t) == environment) list.Add(t);
            return list;
        }

        public string EnvironmentOf(Entry t)
        {
            if (!string.IsNullOrEmpty(t.Environment)) return t.Environment;
            var data = Load(t);
            return data != null ? data.Environment : "";
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
