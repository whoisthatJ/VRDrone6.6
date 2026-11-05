using System.Collections.Generic;
using Ushqyr.Flight;
using Ushqyr.Race;
using Ushqyr.Track;
using UnityEngine;

namespace Ushqyr.AI
{
    /// <summary>
    /// A baked racing line cached per track × quad size class (P4), in <c>Resources/RacingLines/</c>. The hash covers the
    /// gates, the corridor and the quad, so a changed track (or quad) is detected at load and the line is rebaked on the
    /// spot; the editor button Ushqyr > Bots > Bake Racing Lines refreshes every asset.
    /// </summary>
    public sealed class RacingLineAsset : ScriptableObject
    {
        public const string Folder = "RacingLines";

        public string TrackId;
        public int SizeClass;
        public BotLine Line = new BotLine();

        public static string NameFor(string trackId, int sizeClass) => $"{trackId}_{sizeClass}";

        private static readonly Dictionary<string, BotLine> Runtime = new Dictionary<string, BotLine>();

        /// <summary>
        /// The line for a built course: the cached asset if its hash still matches, else a fresh bake (kept for the
        /// session). <paramref name="rebaked"/> tells which.
        /// </summary>
        public static BotLine Get(string trackId, int sizeClass, TrackRoute route, RouteData data, IReadOnlyList<Gate> gates,
            Vector3 start, in QuadProfile profile, int mask, out bool rebaked)
        {
            string key = NameFor(trackId, sizeClass);
            int hash = RacingLineBaker.HashFor(route, data, gates, start, profile, mask);
            rebaked = false;
            if (Runtime.TryGetValue(key, out var cached) && cached.Hash == hash) return cached;
            var asset = Resources.Load<RacingLineAsset>(Folder + "/" + key);
            if (asset != null && asset.Line != null && asset.Line.IsValid && asset.Line.Hash == hash)
            {
                Runtime[key] = asset.Line;
                return asset.Line;
            }
            var line = RacingLineBaker.Bake(route, data, gates, start, profile, mask);
            Runtime[key] = line;
            rebaked = true;
            return line;
        }
    }
}
