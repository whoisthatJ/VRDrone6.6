using System;
using System.Collections.Generic;

namespace Ushqyr.Edu
{
    /// <summary>One pilot's race as the timing sees it.</summary>
    public sealed class PilotTiming
    {
        public string Pilot = "";
        /// <summary>Times of the valid laps, in the order flown.</summary>
        public readonly List<double> Laps = new List<double>();
        /// <summary>For each valid lap, the time from its start to each gate of the course (the last is the lap time).</summary>
        public readonly List<double[]> Splits = new List<double[]>();
        /// <summary>Seconds from the start signal to the first pass of the start/finish gate; NaN until then.</summary>
        public double Holeshot = double.NaN;
        /// <summary>The time registered for the laps completed (see <see cref="RaceTiming"/> for where it starts); NaN with no lap.</summary>
        public double RaceTime = double.NaN;
        public bool Finished, Disqualified, Out;
        /// <summary>Left the start before the signal and went through the first gate (F9U.6.2).</summary>
        public bool FalseStart;
        /// <summary>Flew a whole lap more than the race has (F9U.9.1).</summary>
        public bool ExtraLap;
        /// <summary>Laps not validated because a gate was missed and not gone back for (F9U.7.2).</summary>
        public int VoidLaps;
        /// <summary>The gate to fly through next; gate 0 is the start/finish.</summary>
        public int NextGate;
        /// <summary>The gate being skipped right now, or -1: the lap will not count unless the pilot goes back through it.</summary>
        public int Missed = -1;
        /// <summary>Position in the race right now, 1 for the leader.</summary>
        public int Position;

        internal bool LeftEarly, Moved;
        internal double LapStart;
        internal int Crossings;
        internal readonly List<int> LapCrossing = new List<int>();
        internal readonly List<double> Passes = new List<double>();
        internal double[] Current;

        public double BestLap
        {
            get
            {
                double best = double.NaN;
                foreach (double lap in Laps)
                    if (double.IsNaN(best) || lap < best) best = lap;
                return best;
            }
        }

        /// <summary>The fastest three laps flown one after another with no void lap between them; NaN when there are none.</summary>
        public double BestThree
        {
            get
            {
                double best = double.NaN;
                for (int i = 0; i + 2 < Laps.Count; i++)
                {
                    if (LapCrossing[i + 1] != LapCrossing[i] + 1 || LapCrossing[i + 2] != LapCrossing[i] + 2) continue;
                    double sum = Laps[i] + Laps[i + 1] + Laps[i + 2];
                    if (double.IsNaN(best) || sum < best) best = sum;
                }
                return best;
            }
        }

        /// <summary>Gates passed that count: the holeshot is one, each valid lap the whole course, plus the gates of the lap being flown.</summary>
        public int Progress => Passes.Count;
    }

    /// <summary>
    /// The timing of one race by FAI F9U (Docs/reference/fai/f9u_values.md), from gate passes alone. Gate 0 is the
    /// start/finish: the first pass through it after the start is the holeshot, and every later pass through it,
    /// with all the other gates passed in order since the last, completes a lap.
    ///
    /// <para>A gate passed out of turn does not count. Crossing the start/finish with a gate of the lap still
    /// missing voids that lap (the pilot may go back through the missed gate first; then the lap counts). The time
    /// registered runs from the start signal (elimination, final) or from the holeshot (qualification) to the end of
    /// the last counted lap, so a void lap costs all the time it took. Times are kept to a thousandth of a second.</para>
    /// </summary>
    public sealed class RaceTiming
    {
        private readonly List<PilotTiming> _pilots = new List<PilotTiming>();
        private readonly int _gates, _laps;
        private readonly bool _fromSignal;
        private double _start = double.NaN;

        /// <summary>Seconds of flight allowed (F9U.6: three minutes); the race is over for everyone when they pass.</summary>
        public double FlightTime = 180;
        /// <summary>Strict FAI: a pilot who flies a whole lap more than the race has is disqualified from it.</summary>
        public bool DisqualifyExtraLap;
        public bool Started => !double.IsNaN(_start);
        public bool TimeIsUp { get; private set; }
        public int Laps => _laps;
        public int Gates => _gates;
        public IReadOnlyList<PilotTiming> Pilots => _pilots;

        /// <param name="timeFromSignal">True for elimination and final races (timing starts at the start signal); false for
        /// qualification (timing starts when the model passes the timing gate).</param>
        public RaceTiming(IList<string> pilots, int gates, int laps, bool timeFromSignal)
        {
            if (gates < 1) throw new ArgumentException("a course has at least one gate");
            if (laps < 1) throw new ArgumentException("a race has at least one lap");
            _gates = gates;
            _laps = laps;
            _fromSignal = timeFromSignal;
            foreach (string p in pilots) _pilots.Add(new PilotTiming { Pilot = p, Current = new double[gates] });
            Rank();
        }

        public PilotTiming Of(string pilot) => _pilots.Find(p => p.Pilot == pilot);

        private static double Ms(double t) => Math.Round(t, 3);

        /// <summary>The start signal sounded at this time (any clock, in seconds, as long as every call uses the same one).</summary>
        public void Start(double t) => _start = Ms(t);

        /// <summary>The model is no longer touching its start position. Before the signal, that is the first half of a false start.</summary>
        public void LeftStart(string pilot, double t)
        {
            PilotTiming p = Of(pilot);
            if (p == null) return;
            p.Moved = true;
            if (!Started || Ms(t) < _start) p.LeftEarly = true;
        }

        /// <summary>The model is back on its start position before the signal (it dropped to the ground at once): no false start.</summary>
        public void BackOnStart(string pilot)
        {
            PilotTiming p = Of(pilot);
            if (p != null && !Started) p.LeftEarly = false;
        }

        /// <summary>The pilot can fly no more in this race (a crash they cannot recover from, or they gave up).</summary>
        public void Retire(string pilot)
        {
            PilotTiming p = Of(pilot);
            if (p != null && !p.Finished) p.Out = true;
            Rank();
        }

        /// <summary>The race director disqualifies the pilot from the race.</summary>
        public void Disqualify(string pilot)
        {
            PilotTiming p = Of(pilot);
            if (p != null) p.Disqualified = true;
            Rank();
        }

        /// <summary>The model flew through this gate the right way at this time.</summary>
        public void Gate(string pilot, int gate, double t)
        {
            PilotTiming p = Of(pilot);
            if (p == null || gate < 0 || gate >= _gates || p.Disqualified || p.Out || TimeIsUp) return;
            t = Ms(t);
            p.Moved = true;
            if (!Started || t < _start)
            {
                // Through the first gate before the signal: nothing is timed; it is a false start in the making.
                p.LeftEarly = true;
                return;
            }

            if (double.IsNaN(p.Holeshot))
            {
                if (gate != 0) return;
                p.Holeshot = Ms(t - _start);
                if (p.LeftEarly)
                {
                    p.FalseStart = p.Disqualified = true;
                    Rank();
                    return;
                }
                p.LapStart = t;
                p.Crossings = 0;
                p.NextGate = _gates > 1 ? 1 : 0;
                p.Passes.Add(t);
                Rank();
                return;
            }

            if (gate == 0 && p.NextGate != 0)
            {
                // Over the line with a gate of this lap missing: the lap is void, and a new one starts here.
                p.VoidLaps++;
                p.Crossings++;
                p.LapStart = t;
                p.NextGate = _gates > 1 ? 1 : 0;
                p.Missed = -1;
                p.Passes.RemoveRange(1 + p.Laps.Count * _gates, p.Passes.Count - 1 - p.Laps.Count * _gates);
                Rank();
                return;
            }
            if (gate != p.NextGate)
            {
                // Out of turn: it does not count, and the gate skipped is noted until the pilot goes back through it.
                if (p.Missed < 0) p.Missed = p.NextGate;
                return;
            }

            p.Missed = -1;
            p.Passes.Add(t);
            p.Current[gate == 0 ? _gates - 1 : gate - 1] = Ms(t - p.LapStart);
            if (gate != 0)
            {
                p.NextGate = gate + 1 < _gates ? gate + 1 : 0;
                Rank();
                return;
            }

            // A lap.
            p.Crossings++;
            if (p.Finished)
            {
                p.ExtraLap = true;
                if (DisqualifyExtraLap) p.Disqualified = true;
                Rank();
                return;
            }
            p.Laps.Add(Ms(t - p.LapStart));
            p.LapCrossing.Add(p.Crossings);
            p.Splits.Add((double[])p.Current.Clone());
            p.RaceTime = Ms(t - (_fromSignal ? _start : _start + p.Holeshot));
            p.LapStart = t;
            p.NextGate = _gates > 1 ? 1 : 0;
            if (p.Laps.Count >= _laps) p.Finished = true;
            Rank();
        }

        /// <summary>Call as time passes: after the flight time nobody's passes count any more.</summary>
        public void Tick(double t)
        {
            if (Started && !TimeIsUp && Ms(t) - _start >= FlightTime) TimeIsUp = true;
        }

        /// <summary>True when nobody can change the result any more.</summary>
        public bool Over
        {
            get
            {
                if (!Started) return false;
                if (TimeIsUp) return true;
                foreach (PilotTiming p in _pilots)
                    if (!p.Finished && !p.Disqualified && !p.Out) return false;
                return true;
            }
        }

        /// <summary>
        /// Sets everyone's position: the finished by their time; then those still racing by how far they are, and at
        /// the same gate by who reached it first; the disqualified last.
        /// </summary>
        private void Rank()
        {
            var order = new List<PilotTiming>(_pilots);
            order.Sort((a, b) =>
            {
                int c = a.Disqualified.CompareTo(b.Disqualified);
                if (c != 0) return c;
                c = b.Finished.CompareTo(a.Finished);
                if (c != 0) return c;
                if (a.Finished) return a.RaceTime.CompareTo(b.RaceTime);
                c = b.Progress.CompareTo(a.Progress);
                if (c != 0 || a.Progress == 0) return c;
                return a.Passes[a.Progress - 1].CompareTo(b.Passes[b.Progress - 1]);
            });
            for (int i = 0; i < order.Count; i++) order[i].Position = i + 1;
        }

        /// <summary>The pilots in the order of their positions.</summary>
        public List<PilotTiming> Order()
        {
            var order = new List<PilotTiming>(_pilots);
            order.Sort((a, b) => a.Position.CompareTo(b.Position));
            return order;
        }

        /// <summary>
        /// Seconds the pilot is behind the leader, measured at the last gate the pilot passed (when the leader was
        /// there). 0 for the leader; NaN when it cannot be said yet.
        /// </summary>
        public double Gap(string pilot)
        {
            PilotTiming p = Of(pilot), leader = _pilots.Find(x => x.Position == 1);
            if (p == null || leader == null || p.Progress == 0 || leader.Progress < p.Progress) return double.NaN;
            if (p == leader) return 0;
            return Ms(p.Passes[p.Progress - 1] - leader.Passes[p.Progress - 1]);
        }

        /// <summary>The race as the tournament records it.</summary>
        public List<RaceEntry> Results()
        {
            var list = new List<RaceEntry>();
            foreach (PilotTiming p in _pilots)
            {
                var e = new RaceEntry { Pilot = p.Pilot, Disqualified = p.Disqualified, DidNotFly = !p.Moved && !p.Disqualified };
                for (int i = 0; i < p.Laps.Count && i < _laps; i++) e.Laps.Add(p.Laps[i]);
                e.Time = double.IsNaN(p.RaceTime) ? 0 : p.RaceTime;
                list.Add(e);
            }
            return list;
        }
    }
}
