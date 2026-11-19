using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Ushqyr.Bench
{
    /// <summary>result.json of one attempt: the score and everything needed to say what exactly was run.</summary>
    [Serializable]
    public sealed class AttemptResult
    {
        public int schema = 1;
        public string team = "";
        public string track_id = "";
        public string track_hash = "";
        public int attempt = 1;
        public int points;
        /// <summary>Simulator time of the last scored gate (the tie-break), seconds.</summary>
        public double time;
        /// <summary>Simulator time when the attempt ended, seconds.</summary>
        public double sim_time;
        /// <summary>finished, time_limit, timeout, disconnected, stuck or invalid.</summary>
        public string status = "";
        public string mode = "";
        public long seed;
        public string date_utc = "";
        public string build = "";
        public string rules_hash = "";
        public string drone = "";
        public bool drone_calibrated;
        public string drone_config_hash = "";
        public string solution_digest = "";
        public int gates_total;
        public int gate_passes;

        /// <summary>The folder the result was read from (not stored in the file).</summary>
        [NonSerialized] public string Folder;

        /// <summary>A failed run of ours (not the team's) doesn't count and is run again.</summary>
        public bool Counts => status != "invalid";
    }

    public static class BenchHash
    {
        public static string Hex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        public static string Sha256(byte[] data)
        {
            using (var sha = SHA256.Create()) return Hex(sha.ComputeHash(data));
        }

        public static string Sha256(string text) => Sha256(Encoding.UTF8.GetBytes(text));

        public static string Sha256File(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
                return Hex(sha.ComputeHash(stream));
        }

        public static string Hmac(byte[] key, byte[] data)
        {
            using (var h = new HMACSHA256(key)) return Hex(h.ComputeHash(data));
        }

        /// <summary>Compares two hex strings without stopping at the first difference.</summary>
        public static bool SameHex(string a, string b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= char.ToLowerInvariant(a[i]) ^ char.ToLowerInvariant(b[i]);
            return diff == 0;
        }
    }

    public enum ManifestState
    {
        /// <summary>Every file matches its checksum and the signature matches the organisers' key.</summary>
        Verified,
        /// <summary>Every file matches its checksum; the signature could not be checked (no key here, or the attempt was not signed).</summary>
        ChecksumsOnly,
        /// <summary>A file was changed, added or removed, or the signature is wrong.</summary>
        Modified,
        /// <summary>No manifest in the folder.</summary>
        Missing,
    }

    /// <summary>
    /// manifest.json lists every file of an attempt folder with its SHA-256; manifest.sig is the HMAC-SHA256 of
    /// manifest.json with a key that exists only on the organisers' server. Changing any log, video or result
    /// afterwards breaks a checksum; rewriting the manifest to match breaks the signature.
    /// </summary>
    public static class AttemptManifest
    {
        public const string FileName = "manifest.json";
        public const string SignatureName = "manifest.sig";

        [Serializable]
        public sealed class Entry
        {
            public string name;
            public string sha256;
            public long bytes;
        }

        [Serializable]
        public sealed class Data
        {
            public int schema = 1;
            public bool signed;
            public string track_hash = "";
            public string rules_hash = "";
            public string drone_config_hash = "";
            public string solution_digest = "";
            public string build = "";
            public long seed;
            public List<Entry> files = new List<Entry>();
        }

        private static List<string> Files(string folder)
        {
            var names = new List<string>();
            foreach (string path in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
            {
                string name = path.Substring(folder.Length).TrimStart('\\', '/').Replace('\\', '/');
                if (name != FileName && name != SignatureName) names.Add(name);
            }
            names.Sort(StringComparer.Ordinal);
            return names;
        }

        /// <summary>Writes the manifest (and the signature when <paramref name="key"/> is given). Call last.</summary>
        public static void Write(string folder, AttemptResult result, byte[] key)
        {
            var data = new Data
            {
                signed = key != null && key.Length > 0,
                track_hash = result.track_hash, rules_hash = result.rules_hash, drone_config_hash = result.drone_config_hash,
                solution_digest = result.solution_digest, build = result.build, seed = result.seed,
            };
            foreach (string name in Files(folder))
            {
                string path = Path.Combine(folder, name);
                data.files.Add(new Entry { name = name, sha256 = BenchHash.Sha256File(path), bytes = new FileInfo(path).Length });
            }
            byte[] json = Encoding.UTF8.GetBytes(JsonUtility.ToJson(data, true) + "\n");
            File.WriteAllBytes(Path.Combine(folder, FileName), json);
            string sig = Path.Combine(folder, SignatureName);
            if (data.signed) File.WriteAllText(sig, BenchHash.Hmac(key, json) + "\n");
            else if (File.Exists(sig)) File.Delete(sig);
        }

        /// <summary>Checks a folder against its manifest. <paramref name="key"/> may be null (checksums only).</summary>
        public static ManifestState Verify(string folder, byte[] key, out string detail)
        {
            string manifestPath = Path.Combine(folder, FileName);
            if (!File.Exists(manifestPath))
            {
                detail = "no manifest.json";
                return ManifestState.Missing;
            }
            byte[] json = File.ReadAllBytes(manifestPath);
            Data data;
            try { data = JsonUtility.FromJson<Data>(Encoding.UTF8.GetString(json)); }
            catch (Exception) { data = null; }
            if (data == null || data.files == null)
            {
                detail = "manifest.json can't be read";
                return ManifestState.Modified;
            }

            var listed = new HashSet<string>();
            foreach (Entry e in data.files)
            {
                listed.Add(e.name);
                string path = Path.Combine(folder, e.name);
                if (e.name.Contains("..") || !File.Exists(path))
                {
                    detail = "missing file: " + e.name;
                    return ManifestState.Modified;
                }
                if (!BenchHash.SameHex(BenchHash.Sha256File(path), e.sha256))
                {
                    detail = "changed file: " + e.name;
                    return ManifestState.Modified;
                }
            }
            foreach (string name in Files(folder))
                if (!listed.Contains(name))
                {
                    detail = "file not in the manifest: " + name;
                    return ManifestState.Modified;
                }

            string sigPath = Path.Combine(folder, SignatureName);
            bool hasSig = File.Exists(sigPath);
            if (data.signed && !hasSig)
            {
                detail = "the signature file is missing";
                return ManifestState.Modified;
            }
            if (!hasSig)
            {
                detail = "checksums match; the attempt was not signed";
                return ManifestState.ChecksumsOnly;
            }
            if (key == null || key.Length == 0)
            {
                detail = "checksums match; no key here to check the signature";
                return ManifestState.ChecksumsOnly;
            }
            if (!BenchHash.SameHex(File.ReadAllText(sigPath).Trim(), BenchHash.Hmac(key, json)))
            {
                detail = "the signature doesn't match";
                return ManifestState.Modified;
            }
            detail = "checksums and signature match";
            return ManifestState.Verified;
        }
    }

    /// <summary>One line of a leaderboard.</summary>
    public sealed class LeaderboardRow
    {
        public int Rank;
        public string Team;
        public int Points;
        public double Time;
        /// <summary>Attempts that went into this line (one per track on the overall board).</summary>
        public readonly List<AttemptResult> Attempts = new List<AttemptResult>();
    }

    /// <summary>
    /// Ranking: more points first; with equal points the smaller time (time of the last scored gate). A team's line for a
    /// track is its best counted attempt there; the overall board adds up one line per track.
    /// </summary>
    public static class Leaderboard
    {
        /// <summary>Reads every result.json below <paramref name="root"/> (one folder per attempt).</summary>
        public static List<AttemptResult> Load(string root)
        {
            var all = new List<AttemptResult>();
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return all;
            foreach (string path in Directory.GetFiles(root, "result.json", SearchOption.AllDirectories))
            {
                try
                {
                    var r = JsonUtility.FromJson<AttemptResult>(File.ReadAllText(path));
                    if (r == null) continue;
                    r.Folder = Path.GetDirectoryName(path);
                    all.Add(r);
                }
                catch (Exception) { /* a broken file is skipped; verify reports it */ }
            }
            all.Sort((a, b) => string.CompareOrdinal(a.date_utc, b.date_utc));
            return all;
        }

        public static bool Better(AttemptResult a, AttemptResult b) =>
            a.points != b.points ? a.points > b.points : a.time < b.time;

        /// <summary>The board of one track, or the overall board when <paramref name="trackId"/> is null.</summary>
        public static List<LeaderboardRow> Rank(IEnumerable<AttemptResult> results, string trackId)
        {
            // Best counted attempt per team and track.
            var best = new Dictionary<string, AttemptResult>();
            foreach (AttemptResult r in results)
            {
                if (!r.Counts || (trackId != null && r.track_id != trackId)) continue;
                string key = r.team + "\n" + r.track_id;
                if (!best.TryGetValue(key, out AttemptResult have) || Better(r, have)) best[key] = r;
            }
            var rows = new Dictionary<string, LeaderboardRow>();
            foreach (AttemptResult r in best.Values)
            {
                if (!rows.TryGetValue(r.team, out LeaderboardRow row)) rows[r.team] = row = new LeaderboardRow { Team = r.team };
                row.Points += r.points;
                row.Time += r.time;
                row.Attempts.Add(r);
            }
            var list = new List<LeaderboardRow>(rows.Values);
            list.Sort((a, b) => a.Points != b.Points ? b.Points.CompareTo(a.Points)
                : a.Time != b.Time ? a.Time.CompareTo(b.Time) : string.CompareOrdinal(a.Team, b.Team));
            for (int i = 0; i < list.Count; i++)
                list[i].Rank = i > 0 && list[i].Points == list[i - 1].Points && list[i].Time == list[i - 1].Time ? list[i - 1].Rank : i + 1;
            return list;
        }

        public const string CsvHeader = "team,track,attempt,points,time,status,date,build,track_hash,drone_config_hash,solution_digest";

        /// <summary>
        /// Every attempt as CSV. <paramref name="separator"/> ';' is for spreadsheet programs set to a region that writes
        /// decimals with a comma (Russian, Kazakh); numbers then use a decimal comma too.
        /// </summary>
        public static string ToCsv(IEnumerable<AttemptResult> results, char separator = ',')
        {
            var sb = new StringBuilder();
            sb.Append(CsvHeader.Replace(',', separator)).Append("\r\n");
            foreach (AttemptResult r in results)
            {
                string time = r.time.ToString("F3", CultureInfo.InvariantCulture);
                if (separator == ';') time = time.Replace('.', ',');
                string[] cells =
                {
                    r.team, r.track_id, r.attempt.ToString(CultureInfo.InvariantCulture), r.points.ToString(CultureInfo.InvariantCulture), time,
                    r.status, r.date_utc, r.build, r.track_hash, r.drone_config_hash, r.solution_digest,
                };
                for (int i = 0; i < cells.Length; i++)
                {
                    if (i > 0) sb.Append(separator);
                    sb.Append(Cell(cells[i], separator));
                }
                sb.Append("\r\n");
            }
            return sb.ToString();
        }

        /// <summary>The ranked board as CSV: rank, team, points, time.</summary>
        public static string RankedCsv(IEnumerable<LeaderboardRow> rows, char separator = ',')
        {
            var sb = new StringBuilder();
            sb.Append("rank").Append(separator).Append("team").Append(separator).Append("points").Append(separator).Append("time").Append("\r\n");
            foreach (LeaderboardRow r in rows)
            {
                string time = r.Time.ToString("F3", CultureInfo.InvariantCulture);
                if (separator == ';') time = time.Replace('.', ',');
                sb.Append(r.Rank).Append(separator).Append(Cell(r.Team, separator)).Append(separator).Append(r.Points).Append(separator).Append(time).Append("\r\n");
            }
            return sb.ToString();
        }

        /// <summary>
        /// One CSV cell: quoted when needed, and never starting with a character a spreadsheet would run as a formula
        /// (team names come from outside).
        /// </summary>
        public static string Cell(string value, char separator)
        {
            value = value ?? "";
            if (value.Length > 0 && "=+-@\t\r".IndexOf(value[0]) >= 0) value = "'" + value;
            if (value.IndexOf(separator) < 0 && value.IndexOf('"') < 0 && value.IndexOf('\n') < 0 && value.IndexOf('\r') < 0) return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        /// <summary>UTF-8 with a byte-order mark, so spreadsheet programs read Cyrillic and Kazakh letters correctly.</summary>
        public static void WriteCsvFile(string path, string csv) => File.WriteAllText(path, csv, new UTF8Encoding(true));
    }

    /// <summary>Wording of scoring decisions, shared by the logs and the judge replay.</summary>
    public static class GateEventText
    {
        public static string Kind(GateEventKind k) =>
            k == GateEventKind.Scored ? "scored" : k == GateEventKind.RepeatIgnored ? "repeat_ignored" : "wrong_direction";

        public static string Reason(in GateEvent e, int repeatWindow)
        {
            switch (e.Kind)
            {
                case GateEventKind.Scored: return "pass through the opening";
                case GateEventKind.RepeatIgnored:
                    return $"repeat ignored: only {e.OthersSince} other gate{(e.OthersSince == 1 ? "" : "s")} since (needs {repeatWindow})";
                default: return "passed against the allowed direction";
            }
        }
    }
}
