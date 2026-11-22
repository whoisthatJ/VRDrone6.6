using System;
using System.Collections.Generic;

namespace Ushqyr.Edu
{
    /// <summary>What one student has done on one activity.</summary>
    public sealed class JournalCell
    {
        /// <summary>Best grade that counts (the teacher's own mark when there is one); null when never flown.</summary>
        public double? Best;
        public int Attempts, Stars;
        public bool Passed;
        /// <summary>Time in the air over all attempts, in seconds.</summary>
        public double Seconds;
        /// <summary>The attempts, newest first.</summary>
        public readonly List<AttemptRecord> All = new List<AttemptRecord>();
    }

    /// <summary>One student's line of the journal.</summary>
    public sealed class JournalRow
    {
        public EduUser User;
        public JournalCell[] Cells;
        public int Passed, Flown, Attempts, Stars;
        public double Seconds;
        /// <summary>Average of the best grades of the activities flown; 0 when none was.</summary>
        public double Average;
    }

    /// <summary>How a group did on one activity.</summary>
    public struct ActivityStats
    {
        public int Flown, Passed, Attempts;
        public double AverageBest, AverageAttempts;
    }

    /// <summary>
    /// The teacher's journal (EDU_PLAN §9): students down, activities across, and in each cell the best grade, the
    /// number of attempts, the time and whether it was passed. Built from the class database whenever it is shown,
    /// so it is never out of step with the attempts stored there.
    /// </summary>
    public sealed class Journal
    {
        public readonly List<ActivityDefinition> Activities = new List<ActivityDefinition>();
        public readonly List<JournalRow> Rows = new List<JournalRow>();
        /// <summary>The group's name; empty for everyone.</summary>
        public string Group = "";

        /// <summary>
        /// Reads the journal of a group (or of everyone). Students are listed by name; a seat profile is listed after
        /// them, and only when something was flown under it. Activities come in the order of the course.
        /// </summary>
        public static Journal Build(EduStore store, IEnumerable<ActivityDefinition> activities, long? groupId = null, string type = "exercise")
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            var j = new Journal();
            foreach (ActivityDefinition a in activities)
                if (type == null || a.Type == type) j.Activities.Add(a);
            j.Activities.Sort((x, y) =>
            {
                int c = string.CompareOrdinal(x.Block, y.Block);
                if (c == 0) c = x.Number.CompareTo(y.Number);
                return c != 0 ? c : string.CompareOrdinal(x.Id, y.Id);
            });
            var column = new Dictionary<string, int>();
            for (int i = 0; i < j.Activities.Count; i++) column[j.Activities[i].Id] = i;
            if (groupId != null)
                foreach (EduGroup g in store.Groups())
                    if (g.Id == groupId.Value) j.Group = g.Name;

            var seats = new List<JournalRow>();
            foreach (EduUser user in store.Users(groupId))
            {
                if (user.Role == "teacher") continue;
                var row = new JournalRow { User = user, Cells = new JournalCell[j.Activities.Count] };
                for (int i = 0; i < row.Cells.Length; i++) row.Cells[i] = new JournalCell();
                double sum = 0;
                foreach (AttemptRecord attempt in store.Attempts(user.Id))
                {
                    if (!column.TryGetValue(attempt.ActivityId, out int i)) continue;
                    JournalCell cell = row.Cells[i];
                    cell.All.Add(attempt);
                    cell.Attempts++;
                    cell.Seconds += attempt.Duration;
                    if (cell.Best == null || attempt.Final > cell.Best) cell.Best = attempt.Final;
                    // A teacher's mark decides the pass too: the pass mark is the activity's.
                    bool passed = attempt.ManualScore != null ? attempt.ManualScore.Value >= j.Activities[i].PassMark : attempt.Passed;
                    if (passed)
                    {
                        cell.Passed = true;
                        if (attempt.Stars > cell.Stars) cell.Stars = attempt.Stars;
                    }
                }
                foreach (JournalCell cell in row.Cells)
                {
                    row.Attempts += cell.Attempts;
                    row.Seconds += cell.Seconds;
                    row.Stars += cell.Stars;
                    if (cell.Passed) row.Passed++;
                    if (cell.Best == null) continue;
                    row.Flown++;
                    sum += cell.Best.Value;
                }
                row.Average = row.Flown > 0 ? sum / row.Flown : 0;
                if (user.Role == "seat")
                {
                    if (row.Attempts > 0) seats.Add(row);
                }
                else j.Rows.Add(row);
            }
            j.Rows.AddRange(seats);
            return j;
        }

        /// <summary>How the journal's students did on the activity in this column.</summary>
        public ActivityStats Stats(int column)
        {
            var s = new ActivityStats();
            double sum = 0;
            foreach (JournalRow row in Rows)
            {
                JournalCell cell = row.Cells[column];
                if (cell.Best == null) continue;
                s.Flown++;
                s.Attempts += cell.Attempts;
                sum += cell.Best.Value;
                if (cell.Passed) s.Passed++;
            }
            if (s.Flown > 0)
            {
                s.AverageBest = sum / s.Flown;
                s.AverageAttempts = (double)s.Attempts / s.Flown;
            }
            return s;
        }
    }
}
