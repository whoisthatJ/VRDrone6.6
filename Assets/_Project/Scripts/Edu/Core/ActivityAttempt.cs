using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Ushqyr.Edu
{
    /// <summary>
    /// The end of an attempt (EDU_PLAN F1): the recorded flight is graded against the activity, the recording is written
    /// next to the database, and the attempt goes into the journal. The grade comes from the recording alone, so
    /// <see cref="Regrade"/> gives the same result later, or a new one if the activity's rubric was corrected.
    /// </summary>
    public static class ActivityAttempt
    {
        /// <summary>Grades and stores an attempt. <paramref name="flags"/> are the unusual circumstances (weather changed by the teacher, a failure injected, paused).</summary>
        public static AttemptRecord Finish(EduStore store, string replayFolder, ActivityDefinition activity, FlightRecording flight,
            long userId, string build, IDictionary<string, object> flags = null, string program = null)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            if (activity == null) throw new ArgumentNullException(nameof(activity));
            if (flight == null) throw new ArgumentNullException(nameof(flight));
            ScoreResult score = Rubric.Score(activity, flight);

            flight.Notes["activity"] = activity.Id;
            flight.Notes["activity_hash"] = activity.Hash;
            Directory.CreateDirectory(replayFolder);
            string hash = flight.Hash();
            // Named by content: the same flight saved twice is one file, and a name tells nothing about the student.
            string name = activity.Id + "-" + hash.Substring(0, 16) + ".grreplay";
            string path = Path.Combine(replayFolder, name);
            if (!File.Exists(path))
            {
                string temp = path + ".tmp";
                flight.Save(temp);
                File.Move(temp, path);
            }

            var record = new AttemptRecord
            {
                UserId = userId,
                ActivityId = activity.Id,
                ActivityHash = activity.Hash,
                Started = flight.Notes.TryGetValue("started", out string started) && started.Length > 0 ? started : null,
                Duration = flight.Duration,
                Score = score.Score,
                Passed = score.Passed,
                Stars = score.Stars,
                Metrics = MetricsJson(score),
                Flags = flags != null && flags.Count > 0 ? Json.Write(new Dictionary<string, object>(flags)) : null,
                Program = program,
                Build = build,
                ReplayPath = name,
                ReplayHash = hash,
                ReplayBytes = new FileInfo(path).Length,
            };
            store.SaveAttempt(record);
            return record;
        }

        /// <summary>Grades a stored attempt again from its recording, with the activity as it is now. Nothing is written.</summary>
        public static ScoreResult Regrade(EduStore store, AttemptRecord attempt, ActivityDefinition activity)
        {
            string path = store.ReplayFile(attempt.ReplayPath);
            if (path == null || !File.Exists(path)) throw new FileNotFoundException("the recording of this attempt is missing", attempt.ReplayPath ?? "");
            FlightRecording flight = FlightRecording.Load(path);
            if (!string.IsNullOrEmpty(attempt.ReplayHash) && flight.Hash() != attempt.ReplayHash)
                throw new InvalidDataException("the recording is not the one this attempt was graded on");
            return Rubric.Score(activity, flight);
        }

        /// <summary>The breakdown as JSON: for every rubric line its measured value and the share of its points earned.</summary>
        public static string MetricsJson(ScoreResult score)
        {
            var lines = new Dictionary<string, object>();
            foreach (CriterionResult line in score.Lines)
            {
                var one = new Dictionary<string, object> { ["earned"] = Math.Round(line.Fraction, 4), ["weight"] = line.Weight };
                // "No value" (never landed, never in the air) is written as null: JSON has no NaN.
                one["value"] = double.IsNaN(line.Value) || double.IsInfinity(line.Value) ? null : (object)Math.Round(line.Value, 4);
                lines[line.Id] = one;
            }
            var tips = new List<object>();
            foreach (string tip in score.Tips) tips.Add(tip);
            return Json.Write(new Dictionary<string, object> { ["lines"] = lines, ["tips"] = tips, ["score"] = score.Score.ToString("0.0", CultureInfo.InvariantCulture) });
        }
    }
}
