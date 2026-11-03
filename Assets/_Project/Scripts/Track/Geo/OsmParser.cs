using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace GateRush.Track.Geo
{
    /// <summary>
    /// Turns an Overpass JSON extract ("out body" with the referenced nodes) into <see cref="GeoData"/> in local metres:
    /// building footprints (ways and the outer rings of building relations) with heights from height / building:levels
    /// / per-kind defaults, road centrelines with lanes and width, areas (parks, grass, parking, water, ...) and trees.
    /// </summary>
    public static class OsmParser
    {
        /// <summary>Metres per building level when only building:levels is tagged.</summary>
        public const float LevelHeight = 3.2f;

        /// <summary>Height for a building without height or levels, by its building=* kind.</summary>
        public static float DefaultHeight(string kind) => kind switch
        {
            "apartments" or "residential" => 30f,
            "house" or "detached" or "hut" => 7f,
            "garage" or "garages" or "shed" or "roof" or "kiosk" => 3.5f,
            "commercial" or "retail" => 12f,
            "office" => 40f,
            "industrial" or "warehouse" => 10f,
            "school" or "university" or "college" or "kindergarten" or "hospital" => 15f,
            "construction" => 6f,
            _ => 12f,
        };

        /// <summary>Carriageway width for a road kind when width and lanes are missing.</summary>
        public static float DefaultWidth(string kind, int lanes)
        {
            if (lanes > 0) return lanes * 3.5f;
            return kind switch
            {
                "motorway" or "trunk" or "primary" => 14f,
                "secondary" or "tertiary" => 10.5f,
                "residential" or "unclassified" or "living_street" or "service" => 6f,
                "footway" or "path" or "pedestrian" or "cycleway" or "steps" => 3f,
                _ => 6f,
            };
        }

        public static GeoData Parse(string json, GeoProjection projection)
        {
            var root = (Dictionary<string, object>)new Reader(json).Value();
            var data = new GeoData { OriginLat = projection.OriginLat, OriginLon = projection.OriginLon };
            if (root.TryGetValue("osm3s", out var meta) && meta is Dictionary<string, object> m && m.TryGetValue("timestamp_osm_base", out var ts))
                data.Timestamp = ts as string ?? "";

            var elements = (List<object>)root["elements"];
            var nodes = new Dictionary<long, Vector2>();
            var ways = new Dictionary<long, List<long>>();
            foreach (Dictionary<string, object> e in elements)
            {
                if ((string)e["type"] != "node") continue;
                var (x, y) = projection.ToLocal(Num(e["lat"]), Num(e["lon"]));
                var p = new Vector2((float)x, (float)y);
                nodes[Id(e)] = p;
                if (Tags(e).TryGetValue("natural", out var nat) && nat == "tree") data.Trees.Add(p);
            }
            foreach (Dictionary<string, object> e in elements)
            {
                if ((string)e["type"] != "way" || !e.TryGetValue("nodes", out var ns)) continue;
                var ids = new List<long>();
                foreach (var n in (List<object>)ns) ids.Add((long)Num(n));
                ways[Id(e)] = ids;
            }

            foreach (Dictionary<string, object> e in elements)
            {
                string type = (string)e["type"];
                var tags = Tags(e);
                if (type == "way")
                {
                    var pts = Points(ways[Id(e)], nodes, out bool closed);
                    if (pts.Count < 2) continue;
                    if (tags.ContainsKey("building") && closed && pts.Count >= 3) data.Buildings.Add(Building(Id(e), tags, pts));
                    else if (tags.TryGetValue("highway", out var hw) && (!closed || IsLinearHighway(hw))) data.Roads.Add(Road(Id(e), tags, hw, pts));
                    else if (closed && pts.Count >= 3 && AreaKind(tags) is string kind) data.Areas.Add(new GeoArea { Id = Id(e), Kind = kind, Polygon = pts });
                }
                else if (type == "relation" && tags.ContainsKey("building") && e.TryGetValue("members", out var members))
                {
                    // Multipolygon buildings: each closed outer ring becomes a footprint; inner rings (courtyards) go
                    // to the first outer one.
                    GeoBuilding first = null;
                    var holes = new List<GeoRing>();
                    foreach (Dictionary<string, object> mem in (List<object>)members)
                    {
                        if ((string)mem["type"] != "way") continue;
                        long wid = (long)Num(mem["ref"]);
                        if (!ways.TryGetValue(wid, out var ids)) continue;
                        var pts = Points(ids, nodes, out bool closed);
                        if (!closed || pts.Count < 3) continue;
                        string role = mem.TryGetValue("role", out var r) ? (string)r : "outer";
                        if (role == "inner") holes.Add(new GeoRing { Points = pts });
                        else
                        {
                            var b = Building(Id(e), tags, pts);
                            first ??= b;
                            data.Buildings.Add(b);
                        }
                    }
                    if (first != null) first.Holes.AddRange(holes);
                }
            }
            return data;
        }

        private static bool IsLinearHighway(string kind) => kind != "pedestrian" && kind != "platform";

        private static GeoBuilding Building(long id, Dictionary<string, string> tags, List<Vector2> pts)
        {
            var b = new GeoBuilding { Id = id, Kind = tags["building"], Footprint = pts };
            tags.TryGetValue("name", out b.Name);
            b.Name ??= "";
            if (tags.TryGetValue("building:levels", out var lv) && int.TryParse(lv.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int levels))
                b.Levels = levels;
            if (tags.TryGetValue("height", out var h) && Metres(h) is float height && height > 0f) b.Height = height;
            else if (b.Levels > 0) b.Height = b.Levels * LevelHeight;
            else
            {
                b.Height = DefaultHeight(b.Kind);
                b.HeightEstimated = true;
            }
            return b;
        }

        private static GeoRoad Road(long id, Dictionary<string, string> tags, string kind, List<Vector2> pts)
        {
            var r = new GeoRoad { Id = id, Kind = kind, Points = pts };
            tags.TryGetValue("name", out r.Name);
            r.Name ??= "";
            if (tags.TryGetValue("lanes", out var ln)) int.TryParse(ln.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out r.Lanes);
            r.Width = tags.TryGetValue("width", out var w) && Metres(w) is float width && width > 0f ? width : DefaultWidth(kind, r.Lanes);
            r.OneWay = tags.TryGetValue("oneway", out var ow) && ow == "yes";
            return r;
        }

        private static string AreaKind(Dictionary<string, string> tags)
        {
            if (tags.TryGetValue("amenity", out var a) && a == "parking") return "parking";
            if (tags.TryGetValue("highway", out var hw) && (hw == "pedestrian" || hw == "platform")) return "pedestrian";
            if (tags.TryGetValue("natural", out var n) && (n == "water" || n == "wood" || n == "scrub" || n == "grassland" || n == "sand")) return n;
            if (tags.ContainsKey("water")) return "water";
            if (tags.TryGetValue("leisure", out var l)) return l;
            if (tags.TryGetValue("landuse", out var u)) return u;
            if (tags.TryGetValue("man_made", out var mm)) return mm;
            if (tags.TryGetValue("power", out var p) && p == "generator") return "solar";
            return null;
        }

        private static List<Vector2> Points(List<long> ids, Dictionary<long, Vector2> nodes, out bool closed)
        {
            closed = ids.Count > 3 && ids[0] == ids[ids.Count - 1];
            var pts = new List<Vector2>(ids.Count);
            int n = closed ? ids.Count - 1 : ids.Count;
            for (int i = 0; i < n; i++)
                if (nodes.TryGetValue(ids[i], out var p)) pts.Add(p);
            return pts;
        }

        /// <summary>"46", "46 m", "12.5m" → metres; feet and other units are not expected here.</summary>
        private static float? Metres(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s.Trim())
            {
                if (char.IsDigit(c) || c == '.') sb.Append(c);
                else if (c == ',') sb.Append('.');
                else break;
            }
            return float.TryParse(sb.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : (float?)null;
        }

        private static long Id(Dictionary<string, object> e) => (long)Num(e["id"]);
        private static double Num(object o) => o is double d ? d : Convert.ToDouble(o, CultureInfo.InvariantCulture);

        private static Dictionary<string, string> Tags(Dictionary<string, object> e)
        {
            var tags = new Dictionary<string, string>();
            if (e.TryGetValue("tags", out var t) && t is Dictionary<string, object> d)
                foreach (var kv in d) tags[kv.Key] = kv.Value as string ?? Convert.ToString(kv.Value, CultureInfo.InvariantCulture);
            return tags;
        }

        /// <summary>Minimal JSON reader: objects → Dictionary, arrays → List, numbers → double, strings, bools, null.</summary>
        private sealed class Reader
        {
            private readonly string _s;
            private int _i;

            public Reader(string s) => _s = s;

            public object Value()
            {
                Skip();
                char c = _s[_i];
                switch (c)
                {
                    case '{':
                    {
                        _i++;
                        var o = new Dictionary<string, object>();
                        Skip();
                        if (_s[_i] == '}') { _i++; return o; }
                        while (true)
                        {
                            Skip();
                            string key = Str();
                            Skip();
                            _i++; // ':'
                            o[key] = Value();
                            Skip();
                            if (_s[_i++] == '}') return o;
                        }
                    }
                    case '[':
                    {
                        _i++;
                        var a = new List<object>();
                        Skip();
                        if (_s[_i] == ']') { _i++; return a; }
                        while (true)
                        {
                            a.Add(Value());
                            Skip();
                            if (_s[_i++] == ']') return a;
                        }
                    }
                    case '"': return Str();
                    case 't': _i += 4; return true;
                    case 'f': _i += 5; return false;
                    case 'n': _i += 4; return null;
                    default:
                    {
                        int start = _i;
                        while (_i < _s.Length && "+-0123456789.eE".IndexOf(_s[_i]) >= 0) _i++;
                        return double.Parse(_s.Substring(start, _i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
                    }
                }
            }

            private string Str()
            {
                _i++; // opening quote
                var sb = new StringBuilder();
                while (true)
                {
                    char c = _s[_i++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }
                    char e = _s[_i++];
                    switch (e)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case 'r': sb.Append('\r'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'u':
                            sb.Append((char)int.Parse(_s.Substring(_i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            _i += 4;
                            break;
                        default: sb.Append(e); break;
                    }
                }
            }

            private void Skip()
            {
                while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++;
            }
        }
    }
}
