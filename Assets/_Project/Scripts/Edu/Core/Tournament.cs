using System;
using System.Collections.Generic;
using System.Globalization;

namespace Ushqyr.Edu
{
    /// <summary>One pilot's result in one race.</summary>
    public sealed class RaceEntry
    {
        public string Pilot = "";
        /// <summary>Times of the finished, valid laps, in seconds, in the order flown.</summary>
        public readonly List<double> Laps = new List<double>();
        /// <summary>The time registered for the laps completed, from where the timing started.</summary>
        public double Time;
        public bool Disqualified, DidNotFly;
        /// <summary>Seconds added by the race director.</summary>
        public double Penalty;
        /// <summary>Place in the race, 1 for the winner; set when the race is recorded.</summary>
        public int Place;

        public double Total => Time + Penalty;
    }

    /// <summary>One race of a tournament: who flies it, and (once flown) how it ended.</summary>
    public sealed class RaceHeat
    {
        /// <summary>"Q2-1" (qualification round 2, group 1), "R1" to "R13", "S1", "S2", "SF" (small final), "F", "F2" …, "P3-1" (points round 3, group 1).</summary>
        public string Id = "";
        /// <summary>"qualification", "elimination", "small_final", "final" or "points".</summary>
        public string Stage = "";
        public int Round;
        /// <summary>Where each start place comes from: "#5" (5th after qualification) or "R1:2" (2nd of race R1). Empty for drawn races.</summary>
        public readonly List<string> Sources = new List<string>();
        /// <summary>The pilots, in start order; known for an elimination race once the races before it are flown.</summary>
        public readonly List<string> Pilots = new List<string>();
        public bool LineUpKnown;
        /// <summary>Null until the race is flown.</summary>
        public List<RaceEntry> Results;

        public bool Done => Results != null;

        /// <summary>The pilot placed n-th, or null.</summary>
        public string Placed(int n)
        {
            if (Results == null) return null;
            foreach (RaceEntry e in Results)
                if (e.Place == n) return e.Pilot;
            return null;
        }
    }

    public sealed class TournamentSettings
    {
        /// <summary>"fai" (qualification, elimination by FAI F9U scenario C, final) or "points" (a fixed number of rounds, for small classes).</summary>
        public string Format = "fai";
        public int Laps = 3;
        public int GroupSize = 4;
        public int QualificationRounds = 3;
        /// <summary>1: fastest time for the laps of a race. 2: average of the three best lap times over all rounds.</summary>
        public int QualificationOption = 2;
        public bool DoubleElimination;
        public bool SmallFinal = true;
        /// <summary>Final races are flown until one finalist has won two.</summary>
        public bool SuccessiveFinals;
        public int PointsRounds = 4;
        /// <summary>The draws come from this number, so a tournament can be made again exactly.</summary>
        public int Seed = 1;
    }

    /// <summary>
    /// A drone racing tournament by the FAI F9U rules (Docs/reference/fai/f9u_values.md): qualification rounds drawn
    /// blind, the ranking by either FAI option, the elimination of scenario C with or without the double
    /// elimination sequence, a single final or successive finals, and the points format for small classes. It holds
    /// no clock and no network: races are recorded into it, and it says which race is next. Everything can be
    /// written to text and read back, so a tournament survives a power cut.
    /// </summary>
    public sealed class Tournament
    {
        public string Name = "";
        public TournamentSettings Settings = new TournamentSettings();
        public readonly List<string> Pilots = new List<string>();
        public readonly List<RaceHeat> Heats = new List<RaceHeat>();
        /// <summary>Pilots disqualified from the whole event: placed last, marked DISQ.</summary>
        public readonly HashSet<string> Excluded = new HashSet<string>();

        // ------------------------------------------------------------ making it

        public static Tournament Create(string name, IList<string> pilots, TournamentSettings settings)
        {
            if (pilots == null || pilots.Count < 2) throw new ArgumentException("a tournament needs at least two pilots");
            var seen = new HashSet<string>();
            foreach (string p in pilots)
                if (string.IsNullOrWhiteSpace(p) || !seen.Add(p)) throw new ArgumentException("every pilot needs a name of their own");
            settings = settings ?? new TournamentSettings();
            if (settings.Format != "fai" && settings.Format != "points") throw new ArgumentException("the format is \"fai\" or \"points\"");
            if (settings.Laps < 1 || settings.Laps > 20) throw new ArgumentException("1 to 20 laps");
            if (settings.GroupSize < 2 || settings.GroupSize > 8) throw new ArgumentException("2 to 8 pilots in a race");
            if (settings.QualificationOption != 1 && settings.QualificationOption != 2) throw new ArgumentException("the qualification option is 1 or 2");
            var t = new Tournament { Name = name ?? "", Settings = settings };
            t.Pilots.AddRange(pilots);
            bool points = settings.Format == "points";
            int rounds = points ? settings.PointsRounds : settings.QualificationRounds;
            if (rounds < 1 || rounds > 20) throw new ArgumentException("1 to 20 rounds");
            for (int r = 1; r <= rounds; r++) t.Draw(points ? "points" : "qualification", points ? "P" : "Q", r);
            return t;
        }

        /// <summary>A blind draw of everyone into the races of one round; a different draw each round. Short races go last.</summary>
        private void Draw(string stage, string letter, int round)
        {
            var order = new List<string>(Pilots);
            var rng = new Random(unchecked(Settings.Seed * 7919 + round * 104729));
            for (int i = order.Count - 1; i > 0; i--)
            {
                int k = rng.Next(i + 1);
                string swap = order[i];
                order[i] = order[k];
                order[k] = swap;
            }
            int groups = (order.Count + Settings.GroupSize - 1) / Settings.GroupSize, at = 0;
            for (int g = 0; g < groups; g++)
            {
                // Sizes differ by one at most (ten pilots in fours: 4, 3, 3), the fuller races first.
                int size = order.Count / groups + (g < order.Count % groups ? 1 : 0);
                var heat = new RaceHeat { Id = letter + round + "-" + (g + 1), Stage = stage, Round = round, LineUpKnown = true };
                for (int i = 0; i < size; i++) heat.Pilots.Add(order[at++]);
                Heats.Add(heat);
            }
        }

        public RaceHeat Heat(string id) => Heats.Find(h => h.Id == id);

        /// <summary>The race to fly now: the first one not flown whose pilots are known. Null when there is none.</summary>
        public RaceHeat Next => Heats.Find(h => !h.Done && h.LineUpKnown && h.Pilots.Count > 0);

        public bool Finished => Heats.Count > 0 && Next == null && Heats.TrueForAll(h => h.Done || !h.LineUpKnown || h.Pilots.Count == 0) && StageComplete();

        private bool StageComplete()
        {
            if (Settings.Format == "points") return Heats.TrueForAll(h => h.Done);
            return Heats.Exists(h => h.Stage == "final") && Heats.FindAll(h => h.Stage == "final" || h.Stage == "small_final").TrueForAll(h => h.Done || h.Pilots.Count == 0);
        }

        // ------------------------------------------------------------ placing in a race

        /// <summary>
        /// Places the pilots of one race (F9U.6.4): by the time for all the laps; then those who did not finish, by laps
        /// completed and their time; then the disqualified; then those who did not fly. Ties, and the order among the
        /// disqualified, follow <paramref name="ranking"/> (the qualification ranking; any fixed order before there is one).
        /// </summary>
        public static void Place(List<RaceEntry> results, int laps, IList<string> ranking)
        {
            int Rank(string pilot)
            {
                int i = ranking != null ? ranking.IndexOf(pilot) : -1;
                return i < 0 ? int.MaxValue : i;
            }
            int Class(RaceEntry e) => e.DidNotFly ? 3 : e.Disqualified ? 2 : e.Laps.Count >= laps ? 0 : 1;
            var order = new List<RaceEntry>(results);
            order.Sort((a, b) =>
            {
                int c = Class(a).CompareTo(Class(b));
                if (c != 0) return c;
                if (Class(a) <= 1)
                {
                    c = Math.Min(b.Laps.Count, laps).CompareTo(Math.Min(a.Laps.Count, laps));
                    if (c != 0) return c;
                    // Times are kept to a thousandth of a second: closer than that is a tie.
                    c = Math.Round(a.Total, 3).CompareTo(Math.Round(b.Total, 3));
                    if (c != 0) return c;
                }
                c = Rank(a.Pilot).CompareTo(Rank(b.Pilot));
                return c != 0 ? c : string.CompareOrdinal(a.Pilot, b.Pilot);
            });
            for (int i = 0; i < order.Count; i++) order[i].Place = i + 1;
        }

        // ------------------------------------------------------------ qualification

        /// <summary>
        /// The ranking after qualification by the option of the settings (F9U.6.3). Before any race it is the order
        /// the pilots were entered in; pilots disqualified from the event are left out.
        /// </summary>
        public List<string> QualificationRanking()
        {
            var keys = new Dictionary<string, List<double>>();
            foreach (string p in Pilots) keys[p] = new List<double>();
            int laps = Settings.Laps;
            foreach (RaceHeat h in Heats)
            {
                if (h.Stage != "qualification" || !h.Done) continue;
                foreach (RaceEntry e in h.Results)
                {
                    if (e.Disqualified || e.DidNotFly || !keys.ContainsKey(e.Pilot)) continue;
                    if (Settings.QualificationOption == 2)
                    {
                        for (int i = 0; i < e.Laps.Count && i < laps; i++) keys[e.Pilot].Add(e.Laps[i]);
                    }
                    else if (e.Laps.Count > 0)
                    {
                        // One number per race that orders "3 laps ahead of 2 ahead of 1", then by time: a day per missing lap.
                        int done = Math.Min(e.Laps.Count, laps);
                        keys[e.Pilot].Add((laps - done) * 86400.0 + e.Total);
                    }
                }
            }
            foreach (List<double> k in keys.Values) k.Sort();
            var order = new List<string>(Pilots);
            order.RemoveAll(p => Excluded.Contains(p));
            order.Sort((a, b) =>
            {
                List<double> x = keys[a], y = keys[b];
                int c;
                if (Settings.QualificationOption == 2)
                {
                    // The average of the three best laps; with fewer, the average of those, ranked after.
                    int nx = Math.Min(3, x.Count), ny = Math.Min(3, y.Count);
                    c = ny.CompareTo(nx);
                    if (c != 0) return c;
                    if (nx > 0)
                    {
                        double ax = 0, ay = 0;
                        for (int i = 0; i < nx; i++) { ax += x[i]; ay += y[i]; }
                        c = Math.Round(ax / nx, 3).CompareTo(Math.Round(ay / ny, 3));
                        if (c != 0) return c;
                    }
                    // A tie: the fourth best lap, then the fifth, and so on; having one beats having none.
                    for (int i = 3; i < Math.Max(x.Count, y.Count); i++)
                    {
                        if (i >= x.Count) return 1;
                        if (i >= y.Count) return -1;
                        c = Math.Round(x[i], 3).CompareTo(Math.Round(y[i], 3));
                        if (c != 0) return c;
                    }
                }
                else
                {
                    // The best result; a tie by the second best, then the third; having one beats having none.
                    for (int i = 0; i < Math.Max(x.Count, y.Count); i++)
                    {
                        if (i >= x.Count) return 1;
                        if (i >= y.Count) return -1;
                        c = Math.Round(x[i], 3).CompareTo(Math.Round(y[i], 3));
                        if (c != 0) return c;
                    }
                }
                return Pilots.IndexOf(a).CompareTo(Pilots.IndexOf(b));
            });
            return order;
        }

        // ------------------------------------------------------------ the elimination and the final

        private RaceHeat Add(string id, string stage, int round, params string[] sources)
        {
            var h = new RaceHeat { Id = id, Stage = stage, Round = round };
            h.Sources.AddRange(sources);
            Heats.Add(h);
            return h;
        }

        /// <summary>Lays out the races after qualification for the number of pilots that came through it.</summary>
        private void BuildElimination(int qualified)
        {
            string final = Settings.SuccessiveFinals ? "F1" : "F";
            if (qualified <= 4)
            {
                Add(final, "final", 1, "#1", "#2", "#3", "#4");
                return;
            }
            if (qualified <= 8)
            {
                // Our own, outside the FAI text (its scenarios start at 16): straight to the semi-finals.
                Add("S1", "elimination", 1, "#1", "#4", "#5", "#8");
                Add("S2", "elimination", 1, "#2", "#3", "#6", "#7");
                if (Settings.SmallFinal) Add("SF", "small_final", 2, "S1:4", "S1:3", "S2:3", "S2:4");
                Add(final, "final", 2, "S1:2", "S1:1", "S2:1", "S2:2");
                return;
            }
            // FAI F9U Annex C: the first elimination round by the places after qualification.
            Add("R1", "elimination", 1, "#16", "#1", "#8", "#9");
            Add("R2", "elimination", 1, "#13", "#4", "#5", "#12");
            Add("R3", "elimination", 1, "#14", "#3", "#6", "#10");
            Add("R4", "elimination", 1, "#15", "#2", "#7", "#11");
            if (!Settings.DoubleElimination)
            {
                Add("S1", "elimination", 2, "R1:2", "R1:1", "R2:1", "R2:2");
                Add("S2", "elimination", 2, "R3:2", "R3:1", "R4:1", "R4:2");
                if (Settings.SmallFinal) Add("SF", "small_final", 3, "S1:4", "S1:3", "S2:3", "S2:4");
                Add(final, "final", 3, "S1:2", "S1:1", "S2:1", "S2:2");
                return;
            }
            Add("R5", "elimination", 2, "R1:4", "R2:3", "R3:3", "R4:4");
            Add("R6", "elimination", 2, "R2:4", "R1:3", "R4:3", "R3:4");
            Add("R7", "elimination", 2, "R1:2", "R1:1", "R2:1", "R2:2");
            Add("R8", "elimination", 2, "R3:2", "R3:1", "R4:1", "R4:2");
            Add("R9", "elimination", 3, "R8:4", "R6:2", "R5:1", "R7:3");
            Add("R10", "elimination", 3, "R7:4", "R5:2", "R6:1", "R8:3");
            Add("R11", "elimination", 4, "R9:2", "R9:1", "R10:1", "R10:2");
            Add("R12", "elimination", 4, "R7:2", "R7:1", "R8:1", "R8:2");
            Add("R13", "elimination", 5, "R12:4", "R11:2", "R11:1", "R12:3");
            Add(final, "final", 6, "R13:2", "R12:2", "R12:1", "R13:1");
        }

        /// <summary>Fills in the pilots of every race whose earlier races are all flown, and lays out what comes next.</summary>
        private void Advance()
        {
            if (Settings.Format != "fai") return;
            List<RaceHeat> quali = Heats.FindAll(h => h.Stage == "qualification");
            if (!quali.TrueForAll(h => h.Done)) return;
            List<string> ranking = QualificationRanking();
            if (!Heats.Exists(h => h.Stage != "qualification")) BuildElimination(Math.Min(16, ranking.Count));

            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (RaceHeat h in Heats)
                {
                    if (h.LineUpKnown || h.Sources.Count == 0) continue;
                    var pilots = new List<string>();
                    bool ready = true;
                    foreach (string source in h.Sources)
                    {
                        if (source[0] == '#')
                        {
                            int place = int.Parse(source.Substring(1), CultureInfo.InvariantCulture);
                            if (place <= ranking.Count) pilots.Add(ranking[place - 1]);
                            continue;
                        }
                        int colon = source.IndexOf(':');
                        RaceHeat from = Heat(source.Substring(0, colon));
                        if (from == null || !from.LineUpKnown) { ready = false; break; }
                        if (from.Pilots.Count == 0) continue; // an empty race sends nobody on
                        if (!from.Done) { ready = false; break; }
                        string who = from.Placed(int.Parse(source.Substring(colon + 1), CultureInfo.InvariantCulture));
                        if (who != null) pilots.Add(who);
                    }
                    if (!ready) continue;
                    pilots.RemoveAll(p => Excluded.Contains(p));
                    h.Pilots.Clear();
                    h.Pilots.AddRange(pilots);
                    h.LineUpKnown = true;
                    changed = true;
                }
            }

            // Successive finals: another final race for the same four until someone has won two.
            if (!Settings.SuccessiveFinals) return;
            List<RaceHeat> finals = Heats.FindAll(h => h.Stage == "final");
            if (finals.Count == 0 || !finals.TrueForAll(h => h.Done) || finals.Count >= 9) return;
            var wins = new Dictionary<string, int>();
            foreach (RaceHeat f in finals)
            {
                string winner = f.Placed(1);
                if (winner == null) continue;
                wins.TryGetValue(winner, out int n);
                wins[winner] = n + 1;
                if (n + 1 >= 2) return;
            }
            var next = new RaceHeat { Id = "F" + (finals.Count + 1), Stage = "final", Round = finals[0].Round, LineUpKnown = true };
            next.Pilots.AddRange(finals[0].Pilots);
            Heats.Add(next);
        }

        // ------------------------------------------------------------ recording and correcting

        /// <summary>Records how a race ended. Pilots of the race with no entry are taken as not having flown.</summary>
        public void Record(string heatId, IList<RaceEntry> results)
        {
            RaceHeat h = Heat(heatId) ?? throw new ArgumentException("no such race: " + heatId);
            if (!h.LineUpKnown) throw new InvalidOperationException("the pilots of " + heatId + " are not known yet");
            if (h.Done) throw new InvalidOperationException(heatId + " is already recorded; re-run it first");
            var list = new List<RaceEntry>();
            foreach (string pilot in h.Pilots)
            {
                RaceEntry found = null;
                foreach (RaceEntry e in results)
                    if (e.Pilot == pilot) found = e;
                list.Add(found ?? new RaceEntry { Pilot = pilot, DidNotFly = true });
            }
            foreach (RaceEntry e in results)
                if (!h.Pilots.Contains(e.Pilot)) throw new ArgumentException(e.Pilot + " is not in " + heatId);
            // Before there is a qualification ranking, ties fall back on the order of entry.
            Place(list, Settings.Laps, h.Stage == "qualification" || h.Stage == "points" ? (IList<string>)Pilots : QualificationRanking());
            h.Results = list;
            Advance();
        }

        /// <summary>Every race whose pilots depend on this one, directly or through others.</summary>
        private List<RaceHeat> After(RaceHeat heat)
        {
            var after = new List<RaceHeat>();
            if (heat.Stage == "qualification")
            {
                // The ranking decides the whole elimination: all of it is laid out again.
                after.AddRange(Heats.FindAll(h => h.Stage != "qualification"));
                return after;
            }
            var ids = new HashSet<string> { heat.Id };
            bool grew = true;
            while (grew)
            {
                grew = false;
                foreach (RaceHeat h in Heats)
                {
                    if (ids.Contains(h.Id)) continue;
                    bool depends = false;
                    foreach (string s in h.Sources)
                        if (s[0] != '#' && ids.Contains(s.Substring(0, s.IndexOf(':')))) depends = true;
                    // An extra race of successive finals follows from every final race before it.
                    if (!depends && h.Stage == "final" && h.Sources.Count == 0)
                        foreach (RaceHeat earlier in Heats)
                        {
                            if (earlier == h) break;
                            if (earlier.Stage == "final" && ids.Contains(earlier.Id)) depends = true;
                        }
                    if (!depends) continue;
                    ids.Add(h.Id);
                    after.Add(h);
                    grew = true;
                }
            }
            return after;
        }

        private void Undo(RaceHeat heat)
        {
            foreach (RaceHeat h in After(heat))
            {
                // After a change in qualification the elimination is laid out afresh; an extra final race is flown only if still needed.
                if (heat.Stage == "qualification" || (h.Stage == "final" && h.Sources.Count == 0)) Heats.Remove(h);
                else
                {
                    h.Results = null;
                    if (h.Sources.Count > 0)
                    {
                        h.LineUpKnown = false;
                        h.Pilots.Clear();
                    }
                }
            }
        }

        /// <summary>
        /// The race director orders a race to be flown again: its result is dropped, and so is every race that
        /// followed from it. The race is then the next to fly.
        /// </summary>
        public void Rerun(string heatId)
        {
            RaceHeat h = Heat(heatId) ?? throw new ArgumentException("no such race: " + heatId);
            Undo(h);
            h.Results = null;
            Advance();
        }

        /// <summary>
        /// The race director corrects a recorded race: a pilot is disqualified from it (or the disqualification is
        /// lifted), or seconds of penalty are set. The race is placed again, and what followed from it is dropped.
        /// </summary>
        public void Correct(string heatId, string pilot, bool disqualified, double penaltySeconds)
        {
            RaceHeat h = Heat(heatId) ?? throw new ArgumentException("no such race: " + heatId);
            if (!h.Done) throw new InvalidOperationException(heatId + " is not recorded yet");
            RaceEntry e = h.Results.Find(x => x.Pilot == pilot) ?? throw new ArgumentException(pilot + " is not in " + heatId);
            if (penaltySeconds < 0) throw new ArgumentException("a penalty is not negative");
            Undo(h);
            e.Disqualified = disqualified;
            e.Penalty = penaltySeconds;
            Place(h.Results, Settings.Laps, h.Stage == "qualification" || h.Stage == "points" ? (IList<string>)Pilots : QualificationRanking());
            Advance();
        }

        /// <summary>Disqualifies a pilot from the whole event: no further races, last in the classification with DISQ.</summary>
        public void Exclude(string pilot)
        {
            if (!Pilots.Contains(pilot)) throw new ArgumentException("no such pilot: " + pilot);
            Excluded.Add(pilot);
            foreach (RaceHeat h in Heats)
                if (!h.Done) h.Pilots.Remove(pilot);
            Advance();
        }

        // ------------------------------------------------------------ the classification

        /// <summary>Sum of places over the rounds of the points format (the lower the better), and races flown.</summary>
        public Dictionary<string, int> Points()
        {
            var points = new Dictionary<string, int>();
            foreach (string p in Pilots) points[p] = 0;
            foreach (RaceHeat h in Heats)
            {
                if (h.Stage != "points" || !h.Done) continue;
                foreach (RaceEntry e in h.Results) points[e.Pilot] += e.Place;
            }
            return points;
        }

        private double BestLap(string pilot)
        {
            double best = double.MaxValue;
            foreach (RaceHeat h in Heats)
            {
                if (!h.Done) continue;
                foreach (RaceEntry e in h.Results)
                    if (e.Pilot == pilot && !e.Disqualified)
                        foreach (double lap in e.Laps) best = Math.Min(best, lap);
            }
            return best;
        }

        /// <summary>
        /// The classification as far as it is known: the final placing when the tournament is finished, otherwise
        /// what the races flown so far decide (places not yet decided follow the qualification ranking).
        /// </summary>
        public List<string> Classification()
        {
            var list = new List<string>();
            void Take(string pilot)
            {
                if (pilot != null && !list.Contains(pilot) && !Excluded.Contains(pilot)) list.Add(pilot);
            }
            List<string> ranking = QualificationRanking();
            void TakeByRanking(List<string> pilots)
            {
                pilots.Sort((a, b) => ranking.IndexOf(a).CompareTo(ranking.IndexOf(b)));
                foreach (string p in pilots) Take(p);
            }
            List<string> PlacedIn(int a, int b, params string[] heats)
            {
                var found = new List<string>();
                foreach (string id in heats)
                {
                    RaceHeat h = Heat(id);
                    if (h == null || !h.Done) continue;
                    for (int n = a; n <= b; n++)
                        if (h.Placed(n) != null) found.Add(h.Placed(n));
                }
                return found;
            }

            if (Settings.Format == "points")
            {
                Dictionary<string, int> points = Points();
                var order = new List<string>(Pilots);
                order.Sort((a, b) =>
                {
                    int c = points[a].CompareTo(points[b]);
                    // A tie in the sum: the FAI text is silent; the faster best lap goes ahead.
                    if (c == 0) c = BestLap(a).CompareTo(BestLap(b));
                    return c != 0 ? c : Pilots.IndexOf(a).CompareTo(Pilots.IndexOf(b));
                });
                foreach (string p in order) Take(p);
            }
            else
            {
                List<RaceHeat> finals = Heats.FindAll(h => h.Stage == "final" && h.Done);
                if (finals.Count == 1 && !Settings.SuccessiveFinals)
                {
                    for (int n = 1; n <= finals[0].Results.Count; n++) Take(finals[0].Placed(n));
                }
                else if (finals.Count > 0)
                {
                    // Successive finals: the winner is whoever has two wins; the others by the sum of their places, a
                    // tie split by the place in the last final race.
                    var wins = new Dictionary<string, int>();
                    var sum = new Dictionary<string, int>();
                    foreach (RaceHeat f in finals)
                        foreach (RaceEntry e in f.Results)
                        {
                            sum.TryGetValue(e.Pilot, out int s);
                            sum[e.Pilot] = s + e.Place;
                            if (e.Place != 1) continue;
                            wins.TryGetValue(e.Pilot, out int w);
                            wins[e.Pilot] = w + 1;
                        }
                    RaceHeat last = finals[finals.Count - 1];
                    var order = new List<string>(sum.Keys);
                    order.Sort((a, b) =>
                    {
                        wins.TryGetValue(a, out int wa);
                        wins.TryGetValue(b, out int wb);
                        int c = (wb >= 2).CompareTo(wa >= 2);
                        if (c == 0) c = sum[a].CompareTo(sum[b]);
                        if (c == 0) c = last.Results.Find(e => e.Pilot == a).Place.CompareTo(last.Results.Find(e => e.Pilot == b).Place);
                        return c;
                    });
                    foreach (string p in order) Take(p);
                }
                if (Settings.DoubleElimination && Heat("R13") != null)
                {
                    foreach (string p in PlacedIn(3, 4, "R13")) Take(p);
                    foreach (string p in PlacedIn(3, 4, "R11")) Take(p);
                    TakeByRanking(PlacedIn(3, 4, "R9", "R10"));
                    TakeByRanking(PlacedIn(3, 4, "R5", "R6"));
                }
                else
                {
                    RaceHeat small = Heat("SF");
                    if (small != null && small.Done)
                        for (int n = 1; n <= small.Results.Count; n++) Take(small.Placed(n));
                    TakeByRanking(PlacedIn(3, 4, "S1", "S2"));
                    TakeByRanking(PlacedIn(3, 4, "R1", "R2", "R3", "R4"));
                }
                foreach (string p in ranking) Take(p);
            }
            // Disqualified from the event: at the end.
            foreach (string p in Pilots)
                if (Excluded.Contains(p)) list.Add(p);
            return list;
        }

        // ------------------------------------------------------------ keeping it

        public string ToJson()
        {
            var heats = new List<object>();
            foreach (RaceHeat h in Heats)
            {
                var o = new Dictionary<string, object>
                {
                    ["id"] = h.Id, ["stage"] = h.Stage, ["round"] = (double)h.Round, ["known"] = h.LineUpKnown,
                    ["sources"] = h.Sources.ConvertAll(x => (object)x), ["pilots"] = h.Pilots.ConvertAll(x => (object)x),
                };
                if (h.Results != null)
                {
                    var results = new List<object>();
                    foreach (RaceEntry e in h.Results)
                        results.Add(new Dictionary<string, object>
                        {
                            ["pilot"] = e.Pilot, ["laps"] = e.Laps.ConvertAll(x => (object)x), ["time"] = e.Time, ["dsq"] = e.Disqualified, ["dns"] = e.DidNotFly,
                            ["penalty"] = e.Penalty, ["place"] = (double)e.Place,
                        });
                    o["results"] = results;
                }
                heats.Add(o);
            }
            TournamentSettings s = Settings;
            var excluded = new List<object>();
            foreach (string p in Pilots)
                if (Excluded.Contains(p)) excluded.Add(p);
            return Json.Write(new Dictionary<string, object>
            {
                ["format_version"] = 1.0, ["name"] = Name, ["pilots"] = Pilots.ConvertAll(x => (object)x), ["excluded"] = excluded, ["heats"] = heats,
                ["settings"] = new Dictionary<string, object>
                {
                    ["format"] = s.Format, ["laps"] = (double)s.Laps, ["group"] = (double)s.GroupSize, ["rounds"] = (double)s.QualificationRounds,
                    ["option"] = (double)s.QualificationOption, ["double"] = s.DoubleElimination, ["small_final"] = s.SmallFinal, ["successive"] = s.SuccessiveFinals,
                    ["points_rounds"] = (double)s.PointsRounds, ["seed"] = (double)s.Seed,
                },
            });
        }

        public static Tournament FromJson(string json)
        {
            Dictionary<string, object> o = Json.ParseObject(json);
            if (Json.Num(o, "format_version", 0) != 1) throw new FormatException("this is not a tournament this version can read");
            Dictionary<string, object> s = Json.Obj(o, "settings") ?? throw new FormatException("the tournament has no settings");
            var t = new Tournament
            {
                Name = Json.Str(o, "name", ""),
                Settings = new TournamentSettings
                {
                    Format = Json.Str(s, "format", "fai"), Laps = (int)Json.Num(s, "laps", 3), GroupSize = (int)Json.Num(s, "group", 4),
                    QualificationRounds = (int)Json.Num(s, "rounds", 3), QualificationOption = (int)Json.Num(s, "option", 2), DoubleElimination = Json.Flag(s, "double"),
                    SmallFinal = Json.Flag(s, "small_final", true), SuccessiveFinals = Json.Flag(s, "successive"), PointsRounds = (int)Json.Num(s, "points_rounds", 4),
                    Seed = (int)Json.Num(s, "seed", 1),
                },
            };
            foreach (object p in Json.Arr(o, "pilots") ?? new List<object>()) t.Pilots.Add((string)p);
            foreach (object p in Json.Arr(o, "excluded") ?? new List<object>()) t.Excluded.Add((string)p);
            foreach (object x in Json.Arr(o, "heats") ?? new List<object>())
            {
                var m = (Dictionary<string, object>)x;
                var h = new RaceHeat { Id = Json.Str(m, "id", ""), Stage = Json.Str(m, "stage", ""), Round = (int)Json.Num(m, "round", 0), LineUpKnown = Json.Flag(m, "known") };
                foreach (object v in Json.Arr(m, "sources") ?? new List<object>()) h.Sources.Add((string)v);
                foreach (object v in Json.Arr(m, "pilots") ?? new List<object>()) h.Pilots.Add((string)v);
                List<object> results = Json.Arr(m, "results");
                if (results != null)
                {
                    h.Results = new List<RaceEntry>();
                    foreach (object r in results)
                    {
                        var rm = (Dictionary<string, object>)r;
                        var e = new RaceEntry
                        {
                            Pilot = Json.Str(rm, "pilot", ""), Time = Json.Num(rm, "time", 0), Disqualified = Json.Flag(rm, "dsq"), DidNotFly = Json.Flag(rm, "dns"),
                            Penalty = Json.Num(rm, "penalty", 0), Place = (int)Json.Num(rm, "place", 0),
                        };
                        foreach (object lap in Json.Arr(rm, "laps") ?? new List<object>()) e.Laps.Add((double)lap);
                        h.Results.Add(e);
                    }
                }
                t.Heats.Add(h);
            }
            return t;
        }
    }
}
