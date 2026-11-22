using System;
using System.Collections.Generic;
using NUnit.Framework;
using Ushqyr.Edu;

namespace Ushqyr.Tests
{
    /// <summary>The racing tournament by FAI F9U (Docs/reference/fai/f9u_values.md): placing, qualification, brackets, finals, corrections.</summary>
    public class TournamentTests
    {
        private static List<string> Names(int n)
        {
            var list = new List<string>();
            for (int i = 1; i <= n; i++) list.Add("P" + i.ToString("00"));
            return list;
        }

        private static RaceEntry Flew(string pilot, params double[] laps)
        {
            var e = new RaceEntry { Pilot = pilot };
            foreach (double lap in laps)
            {
                e.Laps.Add(lap);
                e.Time += lap;
            }
            return e;
        }

        /// <summary>Flies a race in which a pilot's number decides the pace: P01 is always the fastest.</summary>
        private static void Fly(Tournament t, RaceHeat heat, Func<string, double> pace = null)
        {
            var results = new List<RaceEntry>();
            foreach (string pilot in heat.Pilots)
            {
                double lap = pace != null ? pace(pilot) : 20 + int.Parse(pilot.Substring(1)) * 0.25;
                results.Add(Flew(pilot, lap + 0.4, lap, lap + 0.1));
            }
            t.Record(heat.Id, results);
        }

        private static void FlyAll(Tournament t, Func<RaceHeat, bool> until = null)
        {
            for (int guard = 0; guard < 200; guard++)
            {
                RaceHeat next = t.Next;
                if (next == null || (until != null && until(next))) return;
                Fly(t, next);
            }
            Assert.Fail("the tournament did not end");
        }

        private static string Line(RaceHeat h) => string.Join(",", h.Pilots);

        [Test]
        public void ARace_IsPlacedByTime_ThenLaps_ThenDisqualified_ThenNotFlown()
        {
            var ranking = new List<string> { "A", "B", "C", "D", "E", "F", "G" };
            var results = new List<RaceEntry>
            {
                new RaceEntry { Pilot = "G", DidNotFly = true },
                new RaceEntry { Pilot = "F", Disqualified = true, Time = 50 },
                Flew("E", 21, 22),          // two laps
                Flew("D"),                  // flew, no lap finished
                Flew("C", 20, 20, 20.5),    // 60.5
                Flew("B", 20, 20, 20),      // 60.0
                new RaceEntry { Pilot = "A", Disqualified = true },
            };
            Tournament.Place(results, 3, ranking);
            string order = "";
            for (int n = 1; n <= 7; n++) order += results.Find(e => e.Place == n).Pilot;
            Assert.AreEqual("BCEDAFG", order, "three laps by time; two laps; none; the disqualified in ranking order; not flown");

            // A penalty counts; a dead heat to the thousandth goes to the better qualifier.
            var tie = new List<RaceEntry> { Flew("C", 20, 20, 20.0004), Flew("A", 20, 20, 20), Flew("B", 19, 19, 19) };
            tie[2].Penalty = 4;
            Tournament.Place(tie, 3, ranking);
            Assert.AreEqual(1, tie[1].Place);
            Assert.AreEqual(2, tie[0].Place);
            Assert.AreEqual(3, tie[2].Place, "57 s and 4 s of penalty is 61 s");
        }

        [Test]
        public void TheDraw_IsBlind_Different_EachRound_AndTheSameForTheSameSeed()
        {
            Tournament t = Tournament.Create("x", Names(10), new TournamentSettings { Seed = 5 });
            List<RaceHeat> first = t.Heats.FindAll(h => h.Round == 1);
            Assert.AreEqual(3, first.Count);
            Assert.AreEqual("4,3,3", first[0].Pilots.Count + "," + first[1].Pilots.Count + "," + first[2].Pilots.Count, "ten pilots in fours: no race of two");
            Assert.AreEqual(9, t.Heats.Count, "three rounds");
            var everyone = new HashSet<string>();
            foreach (RaceHeat h in first) everyone.UnionWith(h.Pilots);
            Assert.AreEqual(10, everyone.Count);
            Assert.AreNotEqual(Line(first[0]), Line(t.Heat("Q2-1")), "another draw for the second round");
            Assert.AreEqual(Line(first[0]), Line(Tournament.Create("x", Names(10), new TournamentSettings { Seed = 5 }).Heat("Q1-1")));
            Assert.AreNotEqual(Line(first[0]), Line(Tournament.Create("x", Names(10), new TournamentSettings { Seed = 6 }).Heat("Q1-1")));
            Assert.Throws<ArgumentException>(() => Tournament.Create("x", new[] { "A", "A" }, null));
            Assert.Throws<ArgumentException>(() => Tournament.Create("x", new[] { "A" }, null));
        }

        [Test]
        public void Qualification_Option1_BestThreeLapTime_WithTheFaiTieBreaks()
        {
            Tournament t = Tournament.Create("x", new[] { "A", "B", "C", "D", "E" }, new TournamentSettings { QualificationOption = 1, QualificationRounds = 2, GroupSize = 6 });
            t.Record("Q1-1", new[] { Flew("A", 20, 20, 20), Flew("B", 20, 20, 20), Flew("C", 19, 19), Flew("D", 30), new RaceEntry { Pilot = "E", Disqualified = true } });
            t.Record("Q2-1", new[] { Flew("A", 25, 25, 25), Flew("B", 21, 21, 21), Flew("C", 18, 18), Flew("D", 15) });
            // A and B share a best of 60: B's second result (63) beats A's (75). C has only two laps, D one, E nothing.
            Assert.AreEqual("B,A,C,D,E", string.Join(",", t.QualificationRanking()));
        }

        [Test]
        public void Qualification_Option2_AverageOfTheThreeBestLaps_FromAnyRounds()
        {
            Tournament t = Tournament.Create("x", new[] { "A", "B", "C", "D", "E" }, new TournamentSettings { QualificationOption = 2, QualificationRounds = 2, GroupSize = 6 });
            t.Record("Q1-1", new[] { Flew("A", 22, 20, 21), Flew("B", 30, 19, 30), Flew("C", 18, 18), Flew("D", 17) });
            t.Record("Q2-1", new[] { Flew("A", 20, 21, 25), Flew("B", 19, 22, 30), Flew("C", 40), Flew("D") });
            // A: 20, 20, 21 (average 20.33). B: 19, 19, 22 (average 20). C has three laps over two rounds: 18, 18, 40.
            // D has one lap. E never flew.
            Assert.AreEqual("B,A,C,D,E", string.Join(",", t.QualificationRanking()));

            // A tie on the three best is split by the fourth best lap.
            Tournament tie = Tournament.Create("x", new[] { "A", "B" }, new TournamentSettings { QualificationOption = 2, QualificationRounds = 2, GroupSize = 6 });
            tie.Record("Q1-1", new[] { Flew("A", 20, 20, 20), Flew("B", 20, 20, 20) });
            tie.Record("Q2-1", new[] { Flew("A", 23, 30, 30), Flew("B", 22, 30, 30) });
            Assert.AreEqual("B,A", string.Join(",", tie.QualificationRanking()));
        }

        [Test]
        public void SixteenPilots_FromQualificationToTheFinal_WithAReRunAndADisqualification()
        {
            Tournament t = Tournament.Create("Күзгі кубок", Names(16), new TournamentSettings { QualificationRounds = 3, Seed = 3 });
            Assert.AreEqual(12, t.Heats.Count, "three rounds of four races");
            FlyAll(t, h => h.Stage != "qualification");
            Assert.AreEqual(string.Join(",", Names(16)), string.Join(",", t.QualificationRanking()), "the faster pilot qualifies higher");

            // The first elimination round is FAI Annex C, C.1.
            Assert.AreEqual("P16,P01,P08,P09", Line(t.Heat("R1")));
            Assert.AreEqual("P13,P04,P05,P12", Line(t.Heat("R2")));
            Assert.AreEqual("P14,P03,P06,P10", Line(t.Heat("R3")));
            Assert.AreEqual("P15,P02,P07,P11", Line(t.Heat("R4")));
            Assert.IsFalse(t.Heat("S1").LineUpKnown, "a semi-final is not known before its races");

            FlyAll(t, h => h.Id == "S1");
            Assert.AreEqual("P08,P01,P04,P05", Line(t.Heat("S1")), "the two best of races 1 and 2");
            Assert.AreEqual("P06,P03,P02,P07", Line(t.Heat("S2")));

            // Kept as text and read back in the middle of the event: the same tournament goes on.
            Tournament resumed = Tournament.FromJson(t.ToJson());
            Assert.AreEqual(t.ToJson(), resumed.ToJson());
            Assert.AreEqual("S1", resumed.Next.Id);
            t = resumed;

            // A disqualification in race 2: P04 is out of it, so P12 goes on in his place and the semi-final changes.
            t.Correct("R2", "P04", true, 0);
            Assert.AreEqual(4, t.Heat("R2").Results.Find(e => e.Pilot == "P04").Place);
            Assert.AreEqual("P08,P01,P05,P12", Line(t.Heat("S1")));

            FlyAll(t, h => h.Stage == "final");
            Assert.AreEqual("P05,P01,P02,P03", Line(t.Heat("F")));
            Assert.AreEqual("P12,P08,P06,P07", Line(t.Heat("SF")));

            // The final is flown, then ordered to be flown again; the second time P03 wins it.
            Fly(t, t.Heat("F"));
            Assert.IsTrue(t.Finished);
            Assert.AreEqual("P01", t.Classification()[0]);
            t.Rerun("F");
            Assert.IsFalse(t.Finished);
            Assert.AreEqual("F", t.Next.Id);
            Assert.Throws<InvalidOperationException>(() => t.Correct("F", "P01", true, 0), "nothing to correct in a race not flown");
            Fly(t, t.Heat("F"), p => p == "P03" ? 15 : 20 + int.Parse(p.Substring(1)) * 0.25);
            Assert.IsTrue(t.Finished);

            // 1 to 4 from the final, 5 to 8 from the small final, 9 to 16 the third and fourth of races 1 to 4 in the
            // order of qualification (P04, disqualified in race 2, is among them).
            Assert.AreEqual("P03,P01,P02,P05,P06,P07,P08,P12,P04,P09,P10,P11,P13,P14,P15,P16", string.Join(",", t.Classification()));
            Assert.Throws<InvalidOperationException>(() => t.Record("F", new List<RaceEntry>()), "a recorded race is not recorded twice");
            Assert.Throws<ArgumentException>(() => t.Rerun("R99"));
        }

        [Test]
        public void DoubleElimination_FollowsTheFaiBracket_RaceByRace()
        {
            Tournament t = Tournament.Create("x", Names(16), new TournamentSettings { DoubleElimination = true, Seed = 9 });
            FlyAll(t);
            Assert.IsTrue(t.Finished);
            // With the faster qualifier always ahead, each race of Annex C, C.3 has exactly these pilots.
            var expected = new Dictionary<string, string>
            {
                ["R5"] = "P16,P12,P10,P15", ["R6"] = "P13,P09,P11,P14", ["R7"] = "P08,P01,P04,P05", ["R8"] = "P06,P03,P02,P07",
                ["R9"] = "P07,P11,P10,P05", ["R10"] = "P08,P12,P09,P06", ["R11"] = "P07,P05,P06,P08", ["R12"] = "P04,P01,P02,P03",
                ["R13"] = "P04,P06,P05,P03", ["F"] = "P04,P02,P01,P03",
            };
            foreach (KeyValuePair<string, string> race in expected) Assert.AreEqual(race.Value, Line(t.Heat(race.Key)), race.Key);
            Assert.AreEqual(4 * 3 + 13 + 1, t.Heats.Count, "twelve qualifying races, thirteen elimination races, the final");
            // The final classification of C.4 then returns the qualification order, place by place.
            Assert.AreEqual(string.Join(",", Names(16)), string.Join(",", t.Classification()));
        }

        [Test]
        public void TwelvePilots_FlyRacesOfThree_WhereAPlaceIsEmpty()
        {
            Tournament t = Tournament.Create("x", Names(12), new TournamentSettings { DoubleElimination = true });
            FlyAll(t, h => h.Stage != "qualification");
            Assert.AreEqual("P01,P08,P09", Line(t.Heat("R1")), "nobody was placed 16th");
            Assert.AreEqual("P04,P05,P12", Line(t.Heat("R2")));
            FlyAll(t, h => h.Id == "R5");
            Assert.AreEqual("P12,P10", Line(t.Heat("R5")), "the fourths of races 1 and 4 do not exist");
            FlyAll(t);
            Assert.IsTrue(t.Finished);
            Assert.AreEqual(12, t.Classification().Count);
            Assert.AreEqual("P01", t.Classification()[0]);
        }

        [Test]
        public void SuccessiveFinals_EndWhenSomeoneHasWonTwo()
        {
            Tournament t = Tournament.Create("x", Names(4), new TournamentSettings { SuccessiveFinals = true });
            FlyAll(t, h => h.Stage == "final");
            Assert.AreEqual("F1", t.Next.Id);
            Fly(t, t.Heat("F1"));                                   // P01 wins
            Assert.AreEqual("F2", t.Next.Id, "one win is not enough");
            Fly(t, t.Heat("F2"), p => p == "P03" ? 15 : 20 + int.Parse(p.Substring(1)) * 0.25); // P03 wins
            Assert.AreEqual("F3", t.Next.Id);
            Fly(t, t.Heat("F3"), p => p == "P03" ? 15 : p == "P04" ? 16 : 20 + int.Parse(p.Substring(1)) * 0.25); // P03 again
            Assert.IsTrue(t.Finished);
            Assert.IsNull(t.Heat("F4"));
            // P03 has two wins. Places 2 to 4 by the sum of places: P01 1+2+3 = 6, P02 2+3+4 = 9, P04 4+4+2 = 10.
            Assert.AreEqual("P03,P01,P02,P04", string.Join(",", t.Classification()));

            // The second final is flown again and P01 wins it: he has two wins, and the third final is not needed.
            t.Rerun("F2");
            Assert.IsNull(t.Heat("F3"));
            Fly(t, t.Heat("F2"));
            Assert.IsTrue(t.Finished);
            Assert.AreEqual("P01", t.Classification()[0]);
        }

        [Test]
        public void SmallClasses_FlyForPoints_OverAFixedNumberOfRounds()
        {
            Tournament t = Tournament.Create("x", Names(6), new TournamentSettings { Format = "points", PointsRounds = 3, GroupSize = 3 });
            Assert.AreEqual(6, t.Heats.Count, "three rounds of two races");
            FlyAll(t);
            Assert.IsTrue(t.Finished);
            Dictionary<string, int> points = t.Points();
            Assert.GreaterOrEqual(points["P06"], points["P01"]);
            Assert.AreEqual(3, points["P01"], "the fastest pilot wins all three of his races: 1 + 1 + 1");
            Assert.AreEqual("P01", t.Classification()[0]);
            Assert.AreEqual("P06", t.Classification()[5]);
        }

        [Test]
        public void ResultSheets_AreWrittenInEveryLanguage()
        {
            var names = new List<string> { "Әлия Нұрланқызы", "Болат Сейітов", "=Хакер;x", "Gülnar", "Дәурен", "Ерлан", "Жанна", "Зере", "Илья" };
            Tournament t = Tournament.Create("Күзгі кубок 2026", names, new TournamentSettings { Seed = 2 });
            for (int guard = 0; guard < 100 && t.Next != null; guard++)
            {
                RaceHeat next = t.Next;
                var results = new List<RaceEntry>();
                for (int i = 0; i < next.Pilots.Count; i++)
                {
                    double lap = 20 + names.IndexOf(next.Pilots[i]) * 0.37;
                    results.Add(next.Id == "S1" && i == 0 ? new RaceEntry { Pilot = next.Pilots[i], Disqualified = true } : Flew(next.Pilots[i], lap + 0.4, lap, lap + 61.5));
                }
                t.Record(next.Id, results);
            }
            Assert.IsTrue(t.Finished);
            t.Exclude("Илья");

            Assert.AreEqual("9.120", RaceReports.Clock(9.12));
            Assert.AreEqual("1:02.345", RaceReports.Clock(62.345));
            Assert.AreEqual("3:00.000", RaceReports.Clock(179.9996));
            Assert.AreEqual("", RaceReports.Clock(double.NaN));

            var sheet = new Dictionary<string, string[]>();
            foreach (string path in System.IO.Directory.GetFiles(System.IO.Path.Combine(UnityEngine.Application.dataPath, "_Project/Localization/Text"), "*.tsv"))
                foreach (string line in System.IO.File.ReadAllLines(path))
                {
                    string[] cells = line.Split('\t');
                    if (cells.Length == 3 && !line.StartsWith("#")) sheet[cells[0]] = cells;
                }
            foreach (string key in RaceReports.Texts) Assert.IsTrue(sheet.ContainsKey(key), "not in the language sheets: " + key);
            byte[] font = System.IO.File.ReadAllBytes(System.IO.Path.Combine(UnityEngine.Application.streamingAssetsPath, "Edu/LiberationSans-Regular.ttf"));
            for (int language = 0; language < 3; language++)
            {
                int column = language;
                Func<string, string> text = key => sheet[key][column];
                Assert.AreEqual(sheet["Semi-final {0}"][column].Replace("{0}", "2"), RaceReports.HeatName(t.Heat("S2"), text));
                Assert.AreEqual(sheet["Final"][column], RaceReports.HeatName(t.Heat("F"), text));
                StringAssert.Contains("3", RaceReports.HeatName(t.Heat("Q3-1"), text));

                var xlsx = new System.IO.MemoryStream();
                RaceReports.Workbook(t, text, "№ 12 мектеп", new DateTime(2026, 11, 22)).Save(xlsx);
                Assert.Greater(xlsx.Length, 2500);

                string csv = RaceReports.Csv(t, text);
                string[] lines = csv.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
                int races = 0;
                foreach (RaceHeat h in t.Heats) races += h.Done ? h.Results.Count : 0;
                Assert.AreEqual(1 + races, lines.Length, "a heading and a line for every pilot of every race");
                StringAssert.Contains("\"'=Хакер;x\"", csv, "a name that looks like a formula is given as text");
                StringAssert.Contains(text("disqualified"), csv);
                StringAssert.Contains("1:", csv, "a lap of over a minute is written as minutes and seconds");

                PdfWriter pdf = RaceReports.Pdf(t, font, text, "№ 12 мектеп", new DateTime(2026, 11, 22));
                Assert.AreEqual(2, pdf.PageCount, "the classification, and the bracket");
                string every = "";
                foreach (string key in RaceReports.Texts) every += text(key);
                Assert.AreEqual("", pdf.Missing(every + string.Join("", names) + "…№"));
                var bytes = new System.IO.MemoryStream();
                pdf.Save(bytes);
                Assert.Greater(bytes.Length, 20000);
            }
            // The qualification figure: the average of the three best laps (option 2), or the best time for the laps (option 1).
            Assert.AreEqual((20.0 + 20.0 + 20.0) / 3.0, RaceReports.QualificationFigure(t, names[0]), 1e-9);

            // A points event has no bracket: one page.
            Tournament small = Tournament.Create("x", Names(5), new TournamentSettings { Format = "points", PointsRounds = 2 });
            FlyAll(small);
            Assert.AreEqual(1, RaceReports.Pdf(small, font, key => key, "", DateTime.Now).PageCount);
            StringAssert.Contains("Round 2, race 1", RaceReports.Csv(small, key => key));
        }

        [Test]
        public void APilotDisqualifiedFromTheEvent_FliesNoMore_AndIsPlacedLast()
        {
            Tournament t = Tournament.Create("x", Names(8), null);
            FlyAll(t, h => h.Stage != "qualification");
            Assert.AreEqual("P01,P04,P05,P08", Line(t.Heat("S1")), "eight pilots start at the semi-finals");
            t.Exclude("P01");
            Assert.AreEqual("P04,P05,P08", Line(t.Heat("S1")));
            FlyAll(t);
            Assert.IsTrue(t.Finished);
            List<string> final = t.Classification();
            Assert.AreEqual("P01", final[final.Count - 1]);
            Assert.AreEqual("P02", final[0]);
            Assert.AreEqual(8, final.Count);
            Assert.IsTrue(Tournament.FromJson(t.ToJson()).Excluded.Contains("P01"));
        }
    }
}
