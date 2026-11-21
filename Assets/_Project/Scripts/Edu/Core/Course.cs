using System;
using System.Collections.Generic;

namespace Ushqyr.Edu
{
    /// <summary>One exercise in the training hub, with what the student has done on it.</summary>
    public sealed class CourseEntry
    {
        public ActivityDefinition Activity;
        /// <summary>Best score so far (the teacher's own mark counts when there is one); null when never flown.</summary>
        public double? Best;
        public int Stars;
        public bool Passed;
        public int Attempts;
    }

    /// <summary>
    /// The training course as the hub shows it (EDU_PLAN §4): the exercises in their blocks and order, a student's
    /// progress, and which blocks are open. Blocks open in order: a block is open when every exercise of the block
    /// before it has been passed. The teacher can open everything.
    /// </summary>
    public sealed class Course
    {
        public static readonly string[] Blocks = { "A", "B", "C", "D", "E" };
        /// <summary>The blocks' names (keys of the language table).</summary>
        public static readonly string[] BlockNames =
        {
            "First flights", "Orientation and precision", "Camera-drone proficiency", "FPV basics", "Racing and freestyle technique",
        };

        private readonly Dictionary<string, List<CourseEntry>> _blocks = new Dictionary<string, List<CourseEntry>>();

        /// <summary>Builds the course from the shipped exercises; with a store and a student, their results are filled in.</summary>
        public Course(IEnumerable<ActivityDefinition> activities, EduStore store = null, long userId = 0)
        {
            foreach (string block in Blocks) _blocks[block] = new List<CourseEntry>();
            foreach (ActivityDefinition a in activities)
            {
                if (a.Type != "exercise" || !_blocks.TryGetValue(a.Block, out List<CourseEntry> list)) continue;
                var entry = new CourseEntry { Activity = a };
                if (store != null && userId != 0)
                    foreach (AttemptRecord attempt in store.Attempts(userId, a.Id))
                    {
                        entry.Attempts++;
                        if (entry.Best == null || attempt.Final > entry.Best) entry.Best = attempt.Final;
                        if (attempt.Passed)
                        {
                            entry.Passed = true;
                            if (attempt.Stars > entry.Stars) entry.Stars = attempt.Stars;
                        }
                    }
                list.Add(entry);
            }
            foreach (List<CourseEntry> list in _blocks.Values)
                list.Sort((x, y) => x.Activity.Number != y.Activity.Number ? x.Activity.Number.CompareTo(y.Activity.Number) : string.CompareOrdinal(x.Activity.Id, y.Activity.Id));
        }

        public IReadOnlyList<CourseEntry> In(string block) => _blocks.TryGetValue(block, out List<CourseEntry> list) ? list : new List<CourseEntry>();

        /// <summary>True when every exercise of the block has been passed (a block with no exercises counts as done).</summary>
        public bool Done(string block)
        {
            foreach (CourseEntry e in In(block))
                if (!e.Passed) return false;
            return true;
        }

        /// <summary>True when the student may fly the block's exercises.</summary>
        public bool Open(string block, bool everythingUnlocked = false)
        {
            int index = Array.IndexOf(Blocks, block);
            if (index < 0) return false;
            if (everythingUnlocked) return true;
            for (int i = 0; i < index; i++)
                if (!Done(Blocks[i])) return false;
            return true;
        }

        /// <summary>Passed exercises and all exercises, over the whole course.</summary>
        public void Progress(out int passed, out int total)
        {
            passed = total = 0;
            foreach (List<CourseEntry> list in _blocks.Values)
                foreach (CourseEntry e in list)
                {
                    total++;
                    if (e.Passed) passed++;
                }
        }
    }
}
