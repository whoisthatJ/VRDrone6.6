using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Ushqyr.Edu
{
    /// <summary>
    /// The result sheets of racing (EDU_PLAN §5): the tournament's classification, its qualification ranking and every
    /// race, as a spreadsheet (XLSX), as CSV and as a PDF with the bracket. <c>text</c> turns an English key into the
    /// language of the sheet.
    /// </summary>
    public static class RaceReports
    {
        /// <summary>The keys of the sheets' own texts (the language sheets must hold each of them).</summary>
        public static readonly string[] Texts =
        {
            "Classification", "Qualification", "Races", "Place", "Pilot", "Race", "Laps", "Time", "Best lap", "Lap {0}", "Note", "Result", "Date",
            "disqualified", "did not fly", "penalty {0} s", "disqualified from the event", "Qualifying round {0}, race {1}", "Round {0}, race {1}", "Elimination race {0}",
            "Semi-final {0}", "Small final", "Final", "Final, race {0}", "Best three laps", "Average of the three best laps", "Points", "Bracket", "School",
        };

        /// <summary>1:02.345 for 62.345 seconds; 9.120 under ten seconds stays 9.120.</summary>
        public static string Clock(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds)) return "";
            long ms = (long)Math.Round(seconds * 1000.0);
            long minutes = ms / 60000;
            double rest = (ms - minutes * 60000) / 1000.0;
            return minutes > 0 ? minutes.ToString(CultureInfo.InvariantCulture) + ":" + rest.ToString("00.000", CultureInfo.InvariantCulture) : rest.ToString("0.000", CultureInfo.InvariantCulture);
        }

        /// <summary>A race's name in words: "Qualifying round 2, race 1", "Elimination race 7", "Semi-final 1", "Final".</summary>
        public static string HeatName(RaceHeat h, Func<string, string> text)
        {
            CultureInfo inv = CultureInfo.InvariantCulture;
            string id = h.Id;
            if (h.Stage == "qualification" || h.Stage == "points")
            {
                int dash = id.IndexOf('-');
                return string.Format(inv, text(h.Stage == "points" ? "Round {0}, race {1}" : "Qualifying round {0}, race {1}"), id.Substring(1, dash - 1), id.Substring(dash + 1));
            }
            if (id == "SF") return text("Small final");
            if (id == "F") return text("Final");
            if (id.StartsWith("F", StringComparison.Ordinal)) return string.Format(inv, text("Final, race {0}"), id.Substring(1));
            if (id.StartsWith("S", StringComparison.Ordinal)) return string.Format(inv, text("Semi-final {0}"), id.Substring(1));
            return string.Format(inv, text("Elimination race {0}"), id.Substring(1));
        }

        private static string Note(RaceEntry e, Func<string, string> text)
        {
            if (e.DidNotFly) return text("did not fly");
            if (e.Disqualified) return text("disqualified");
            return e.Penalty > 0 ? string.Format(CultureInfo.InvariantCulture, text("penalty {0} s"), e.Penalty.ToString("0.###", CultureInfo.InvariantCulture)) : "";
        }

        private static double Best(RaceEntry e)
        {
            double best = double.NaN;
            foreach (double lap in e.Laps)
                if (double.IsNaN(best) || lap < best) best = lap;
            return best;
        }

        /// <summary>What a pilot was ranked on in qualification: their best time for the laps, or the average of their three best laps.</summary>
        public static double QualificationFigure(Tournament t, string pilot)
        {
            var laps = new List<double>();
            double best = double.NaN;
            int bestLaps = 0;
            foreach (RaceHeat h in t.Heats)
            {
                if (h.Stage != "qualification" || !h.Done) continue;
                RaceEntry e = h.Results.Find(x => x.Pilot == pilot);
                if (e == null || e.Disqualified || e.DidNotFly) continue;
                for (int i = 0; i < e.Laps.Count && i < t.Settings.Laps; i++) laps.Add(e.Laps[i]);
                int done = Math.Min(e.Laps.Count, t.Settings.Laps);
                if (done > bestLaps || (done == bestLaps && done > 0 && e.Total < best))
                {
                    bestLaps = done;
                    best = e.Total;
                }
            }
            if (t.Settings.QualificationOption == 1) return bestLaps >= t.Settings.Laps ? best : double.NaN;
            laps.Sort();
            if (laps.Count < 3) return double.NaN;
            return (laps[0] + laps[1] + laps[2]) / 3.0;
        }

        private static List<RaceEntry> ByPlace(RaceHeat h)
        {
            var list = new List<RaceEntry>(h.Results);
            list.Sort((a, b) => a.Place.CompareTo(b.Place));
            return list;
        }

        /// <summary>Three sheets: the classification, the qualification ranking, and every race with its lap times.</summary>
        public static XlsxWriter Workbook(Tournament t, Func<string, string> text, string school, DateTime date)
        {
            CultureInfo inv = CultureInfo.InvariantCulture;
            var x = new XlsxWriter();
            bool points = t.Settings.Format == "points";
            x.AddSheet(Sheet(text("Classification")), new double[] { 8, 32, 24 });
            if (!string.IsNullOrEmpty(school)) x.AddRow(school);
            x.AddRow(t.Name);
            x.AddRow(text("Date") + ": " + date.ToString("dd.MM.yyyy", inv));
            x.AddRow();
            x.AddRow(text("Place"), text("Pilot"), points ? text("Points") : text("Note"));
            Dictionary<string, int> sum = points ? t.Points() : null;
            int place = 1;
            foreach (string pilot in t.Classification())
            {
                bool out_ = t.Excluded.Contains(pilot);
                x.AddRow(out_ ? (object)"DISQ" : place++, pilot, out_ ? text("disqualified from the event") : points ? (object)sum[pilot] : "");
            }

            if (!points)
            {
                x.AddSheet(Sheet(text("Qualification")), new double[] { 8, 32, 26 });
                x.AddRow(text("Place"), text("Pilot"), text(t.Settings.QualificationOption == 1 ? "Best three laps" : "Average of the three best laps"));
                int rank = 1;
                foreach (string pilot in t.QualificationRanking()) x.AddRow(rank++, pilot, Clock(QualificationFigure(t, pilot)));
            }

            var widths = new List<double> { 30, 8, 30, 8, 12, 12 };
            for (int i = 0; i < t.Settings.Laps; i++) widths.Add(11);
            widths.Add(24);
            x.AddSheet(Sheet(text("Races")), widths.ToArray());
            var head = new List<object> { text("Race"), text("Place"), text("Pilot"), text("Laps"), text("Time"), text("Best lap") };
            for (int i = 1; i <= t.Settings.Laps; i++) head.Add(string.Format(inv, text("Lap {0}"), i));
            head.Add(text("Note"));
            x.AddRow(head.ToArray());
            foreach (RaceHeat h in t.Heats)
            {
                if (!h.Done) continue;
                foreach (RaceEntry e in ByPlace(h))
                {
                    var row = new List<object> { HeatName(h, text), e.Place, e.Pilot, e.Laps.Count, e.Laps.Count > 0 ? Clock(e.Total) : "", Clock(Best(e)) };
                    for (int i = 0; i < t.Settings.Laps; i++) row.Add(i < e.Laps.Count ? Clock(e.Laps[i]) : "");
                    row.Add(Note(e, text));
                    x.AddRow(row.ToArray());
                }
            }
            return x;
        }

        private static string Sheet(string name)
        {
            var b = new StringBuilder();
            foreach (char c in name)
                if ("\\/?*[]:".IndexOf(c) < 0 && b.Length < 31) b.Append(c);
            return b.Length > 0 ? b.ToString() : "Sheet";
        }

        /// <summary>Every race as CSV, one line per pilot: semicolons, UTF-8 (to be saved with a byte-order mark).</summary>
        public static string Csv(Tournament t, Func<string, string> text)
        {
            CultureInfo inv = CultureInfo.InvariantCulture;
            var b = new StringBuilder();
            void Line(List<string> cells)
            {
                for (int i = 0; i < cells.Count; i++)
                {
                    if (i > 0) b.Append(';');
                    string c = cells[i] ?? "";
                    if (c.Length > 0 && "=+-@\t\r".IndexOf(c[0]) >= 0) c = "'" + c;
                    bool quote = c.IndexOf(';') >= 0 || c.IndexOf('"') >= 0 || c.IndexOf('\n') >= 0 || c.IndexOf('\r') >= 0;
                    b.Append(quote ? "\"" + c.Replace("\"", "\"\"") + "\"" : c);
                }
                b.Append("\r\n");
            }
            var head = new List<string> { text("Race"), text("Place"), text("Pilot"), text("Laps"), text("Time"), text("Best lap") };
            for (int i = 1; i <= t.Settings.Laps; i++) head.Add(string.Format(inv, text("Lap {0}"), i));
            head.Add(text("Note"));
            Line(head);
            foreach (RaceHeat h in t.Heats)
            {
                if (!h.Done) continue;
                foreach (RaceEntry e in ByPlace(h))
                {
                    var row = new List<string> { HeatName(h, text), e.Place.ToString(inv), e.Pilot, e.Laps.Count.ToString(inv), e.Laps.Count > 0 ? Clock(e.Total) : "", Clock(Best(e)) };
                    for (int i = 0; i < t.Settings.Laps; i++) row.Add(i < e.Laps.Count ? Clock(e.Laps[i]) : "");
                    row.Add(Note(e, text));
                    Line(row);
                }
            }
            return b.ToString();
        }

        /// <summary>
        /// The printable result: the classification on the first page, then the bracket: every race after
        /// qualification as a box with its pilots in the order they finished (or in start order when not flown yet),
        /// round by round across the page.
        /// </summary>
        public static PdfWriter Pdf(Tournament t, byte[] trueTypeFont, Func<string, string> text, string school, DateTime date)
        {
            var pdf = new PdfWriter(trueTypeFont, "LiberationSans");
            CultureInfo inv = CultureInfo.InvariantCulture;
            const float left = 50, right = PdfWriter.PageWidth - 50;
            bool points = t.Settings.Format == "points";

            pdf.AddPage();
            float y = 60;
            if (!string.IsNullOrEmpty(school)) { pdf.Text(left, y, 11, Cut(pdf, school, 11, right - left)); y += 22; }
            pdf.Text(left, y + 8, 22, Cut(pdf, t.Name.Length > 0 ? t.Name : text("Classification"), 22, right - left), true);
            y += 38;
            pdf.Text(left, y, 11, text("Classification") + "      " + text("Date") + ": " + date.ToString("dd.MM.yyyy", inv));
            y += 30;
            pdf.Text(left, y, 10, text("Place"), true);
            pdf.Text(left + 60, y, 10, text("Pilot"), true);
            pdf.Text(left + 330, y, 10, points ? text("Points") : text("Note"), true);
            pdf.Line(left, y + 6, right, y + 6, 0.8f);
            y += 22;
            Dictionary<string, int> sum = points ? t.Points() : null;
            int place = 1;
            foreach (string pilot in t.Classification())
            {
                if (y > PdfWriter.PageHeight - 70)
                {
                    pdf.AddPage();
                    y = 60;
                }
                bool out_ = t.Excluded.Contains(pilot);
                // The first three stand out.
                bool podium = !out_ && place <= 3;
                pdf.Text(left, y, 11, out_ ? "DISQ" : place.ToString(inv), podium);
                pdf.Text(left + 60, y, 11, Cut(pdf, pilot, 11, 260), podium);
                if (!out_) place++;
                pdf.Text(left + 330, y, 10, out_ ? text("disqualified from the event") : points ? sum[pilot].ToString(inv) : "");
                pdf.Line(left, y + 5, right, y + 5, 0.2f);
                y += 19;
            }

            // The bracket: the races after qualification, a column for each round.
            List<RaceHeat> bracket = t.Heats.FindAll(h => h.Stage != "qualification" && h.Stage != "points");
            if (bracket.Count == 0) return pdf;
            var rounds = new SortedDictionary<int, List<RaceHeat>>();
            foreach (RaceHeat h in bracket)
            {
                if (!rounds.TryGetValue(h.Round, out List<RaceHeat> list)) rounds[h.Round] = list = new List<RaceHeat>();
                list.Add(h);
            }
            const int perPage = 3;
            const float boxH = 74;
            float colW = (right - left - (perPage - 1) * 12f) / perPage;
            int column = 0;
            foreach (KeyValuePair<int, List<RaceHeat>> round in rounds)
            {
                if (column % perPage == 0)
                {
                    pdf.AddPage();
                    pdf.Text(left, 50, 16, text("Bracket"), true);
                }
                float x = left + (column % perPage) * (colW + 12f);
                float top = 76;
                foreach (RaceHeat h in round.Value)
                {
                    pdf.Text(x, top + 10, 9, Cut(pdf, HeatName(h, text), 9, colW), true);
                    Box(pdf, x, top + 14, x + colW, top + 14 + boxH - 18);
                    float line = top + 27;
                    if (h.Done)
                        foreach (RaceEntry e in ByPlace(h))
                        {
                            string time = e.DidNotFly || e.Disqualified ? Note(e, text) : e.Laps.Count > 0 ? Clock(e.Total) : "";
                            pdf.Text(x + 4, line, 8.5f, e.Place.ToString(inv) + ".  " + Cut(pdf, e.Pilot, 8.5f, colW - 70 - pdf.TextWidth(time, 8.5f) + 40));
                            pdf.Text(x + colW - 4 - pdf.TextWidth(time, 8.5f), line, 8.5f, time);
                            line += 12.5f;
                        }
                    else
                        foreach (string pilot in h.Pilots)
                        {
                            pdf.Text(x + 4, line, 8.5f, Cut(pdf, pilot, 8.5f, colW - 8));
                            line += 12.5f;
                        }
                    top += boxH + 6;
                }
                column++;
            }
            return pdf;
        }

        private static void Box(PdfWriter pdf, float x1, float y1, float x2, float y2)
        {
            pdf.Line(x1, y1, x2, y1, 0.5f);
            pdf.Line(x2, y1, x2, y2, 0.5f);
            pdf.Line(x2, y2, x1, y2, 0.5f);
            pdf.Line(x1, y2, x1, y1, 0.5f);
        }

        private static string Cut(PdfWriter pdf, string s, float size, float room)
        {
            if (pdf.TextWidth(s, size) <= room) return s;
            while (s.Length > 1 && pdf.TextWidth(s + "…", size) > room) s = s.Substring(0, s.Length - 1);
            return s + "…";
        }
    }
}
