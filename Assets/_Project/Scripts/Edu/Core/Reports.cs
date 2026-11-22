using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Ushqyr.Edu
{
    /// <summary>
    /// The teacher's reports (EDU_PLAN §9), made from the journal: the group's gradebook as a spreadsheet (XLSX) and
    /// as CSV, and one student's progress as a PDF page. <c>text</c> turns an English key into the report's language.
    /// </summary>
    public static class Reports
    {
        /// <summary>The keys of the reports' own texts (the language sheet must hold each of them).</summary>
        public static readonly string[] Texts =
        {
            "Gradebook", "Exercises", "Group", "Everyone", "Date", "Student", "Passed", "Average score", "Attempts", "Time, min", "Stars",
            "Exercise", "Flown by", "Passed by", "Average attempts", "Student progress", "Best", "yes", "no", "School", "Teacher", "Seat {0}",
            "Attendance", "Lessons attended",
        };

        private static string Name(EduUser user, Func<string, string> text) =>
            user.Role == "seat" ? string.Format(CultureInfo.InvariantCulture, text("Seat {0}"), user.Name) : user.Name;

        private static string Title(ActivityDefinition a, Func<string, string> text) => (a.Number > 0 ? a.Number + ". " : "") + text(a.Title);

        /// <summary>
        /// The gradebook: one sheet with a line per student and a column per exercise (best grades as numbers, so a
        /// teacher can sort and average them), and a second sheet with how the group did on each exercise.
        /// </summary>
        public static XlsxWriter Gradebook(Journal j, Func<string, string> text, string school, DateTime date, EduStore attendance)
        {
            XlsxWriter x = Gradebook(j, text, school, date);
            if (attendance == null) return x;
            // A third sheet: students down, lesson days across, "+" where the student was seen in the class.
            List<string> days = attendance.LessonDays();
            var widths = new double[days.Count + 2];
            widths[0] = 30;
            for (int i = 1; i < widths.Length; i++) widths[i] = 11;
            x.AddSheet(Sheet(text("Attendance")), widths);
            var head = new List<object> { text("Student") };
            foreach (string day in days) head.Add(day);
            head.Add(text("Lessons attended"));
            x.AddRow(head.ToArray());
            foreach (JournalRow row in j.Rows)
            {
                if (row.User.Role != "student") continue;
                HashSet<string> present = attendance.DaysPresent(row.User.Id);
                var cells = new List<object> { row.User.Name };
                foreach (string day in days) cells.Add(present.Contains(day) ? "+" : "");
                cells.Add(present.Count);
                x.AddRow(cells.ToArray());
            }
            return x;
        }

        public static XlsxWriter Gradebook(Journal j, Func<string, string> text, string school, DateTime date)
        {
            var x = new XlsxWriter();
            var widths = new double[j.Activities.Count + 6];
            widths[0] = 30;
            for (int i = 1; i <= j.Activities.Count; i++) widths[i] = 7;
            for (int i = j.Activities.Count + 1; i < widths.Length; i++) widths[i] = 12;
            x.AddSheet(Sheet(text("Gradebook")), widths);
            if (!string.IsNullOrEmpty(school)) x.AddRow(school);
            x.AddRow(text("Group") + ": " + (j.Group.Length > 0 ? j.Group : text("Everyone")));
            x.AddRow(text("Date") + ": " + date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture));
            x.AddRow();
            var head = new List<object> { text("Student") };
            foreach (ActivityDefinition a in j.Activities) head.Add(Title(a, text));
            head.AddRange(new object[] { text("Passed"), text("Average score"), text("Stars"), text("Attempts"), text("Time, min") });
            x.AddRow(head.ToArray());
            foreach (JournalRow row in j.Rows)
            {
                var cells = new List<object> { Name(row.User, text) };
                foreach (JournalCell c in row.Cells) cells.Add(c.Best == null ? (object)"" : Math.Round(c.Best.Value, 1));
                cells.AddRange(new object[] { row.Passed, Math.Round(row.Average, 1), row.Stars, row.Attempts, Math.Round(row.Seconds / 60.0, 1) });
                x.AddRow(cells.ToArray());
            }

            x.AddSheet(Sheet(text("Exercises")), new double[] { 48, 12, 12, 16, 18 });
            x.AddRow(text("Exercise"), text("Flown by"), text("Passed by"), text("Average score"), text("Average attempts"));
            for (int i = 0; i < j.Activities.Count; i++)
            {
                ActivityStats s = j.Stats(i);
                x.AddRow(Title(j.Activities[i], text), s.Flown, s.Passed, Math.Round(s.AverageBest, 1), Math.Round(s.AverageAttempts, 1));
            }
            return x;
        }

        /// <summary>A sheet name Excel accepts: at most 31 characters, none of \ / ? * [ ] :</summary>
        private static string Sheet(string name)
        {
            var b = new StringBuilder();
            foreach (char c in name)
                if ("\\/?*[]:".IndexOf(c) < 0 && b.Length < 31) b.Append(c);
            return b.Length > 0 ? b.ToString() : "Sheet";
        }

        /// <summary>
        /// The gradebook as CSV: semicolons between the cells and a comma in the numbers when
        /// <paramref name="decimalComma"/> is set (what a spreadsheet set up for Russian or Kazakh expects), otherwise
        /// commas and points. Save it as UTF-8 with a byte-order mark so the Cyrillic letters open correctly.
        /// </summary>
        public static string GradebookCsv(Journal j, Func<string, string> text, bool decimalComma = true)
        {
            char sep = decimalComma ? ';' : ',';
            CultureInfo inv = CultureInfo.InvariantCulture;
            string Num(double v) => decimalComma ? v.ToString("0.#", inv).Replace('.', ',') : v.ToString("0.#", inv);
            var b = new StringBuilder();
            void Line(List<string> cells)
            {
                for (int i = 0; i < cells.Count; i++)
                {
                    if (i > 0) b.Append(sep);
                    string c = cells[i] ?? "";
                    // A cell that starts like a formula is given as text, so a name cannot run anything in a spreadsheet.
                    if (c.Length > 0 && "=+-@\t\r".IndexOf(c[0]) >= 0 && !double.TryParse(c.Replace(',', '.'), NumberStyles.Float, inv, out _)) c = "'" + c;
                    bool quote = c.IndexOf(sep) >= 0 || c.IndexOf('"') >= 0 || c.IndexOf('\n') >= 0 || c.IndexOf('\r') >= 0;
                    b.Append(quote ? "\"" + c.Replace("\"", "\"\"") + "\"" : c);
                }
                b.Append("\r\n");
            }
            var head = new List<string> { text("Student") };
            foreach (ActivityDefinition a in j.Activities) head.Add(Title(a, text));
            head.AddRange(new[] { text("Passed"), text("Average score"), text("Stars"), text("Attempts"), text("Time, min") });
            Line(head);
            foreach (JournalRow row in j.Rows)
            {
                var cells = new List<string> { Name(row.User, text) };
                foreach (JournalCell c in row.Cells) cells.Add(c.Best == null ? "" : Num(c.Best.Value));
                cells.AddRange(new[] { row.Passed.ToString(inv), Num(row.Average), row.Stars.ToString(inv), row.Attempts.ToString(inv), Num(row.Seconds / 60.0) });
                Line(cells);
            }
            return b.ToString();
        }

        /// <summary>One student's progress: the totals, then every exercise with the best grade, attempts, stars and pass.</summary>
        public static PdfWriter StudentProgress(Journal j, JournalRow row, byte[] trueTypeFont, Func<string, string> text, string school, DateTime date)
        {
            var pdf = new PdfWriter(trueTypeFont, "LiberationSans");
            CultureInfo inv = CultureInfo.InvariantCulture;
            const float left = 50, right = PdfWriter.PageWidth - 50;
            // Columns: exercise, best, attempts, stars, passed (right edges of the number columns).
            float[] edge = { right - 210, right - 140, right - 70, right };
            float y = 0;

            void Head(bool first)
            {
                pdf.AddPage();
                y = 60;
                if (first)
                {
                    if (!string.IsNullOrEmpty(school)) { pdf.Text(left, y, 11, Cut(pdf, school, 11, right - left)); y += 22; }
                    pdf.Text(left, y + 8, 22, text("Student progress"), true);
                    y += 40;
                    pdf.Text(left, y, 15, Cut(pdf, Name(row.User, text), 15, right - left), true);
                    y += 22;
                    pdf.Text(left, y, 11, text("Group") + ": " + (j.Group.Length > 0 ? j.Group : text("Everyone")) + "      " + text("Date") + ": " + date.ToString("dd.MM.yyyy", inv));
                    y += 26;
                    string totals = text("Passed") + ": " + row.Passed + " / " + j.Activities.Count + "      " + text("Average score") + ": " + Math.Round(row.Average).ToString(inv) +
                                    "      " + text("Stars") + ": " + row.Stars + " / " + 3 * j.Activities.Count + "      " + text("Attempts") + ": " + row.Attempts +
                                    "      " + text("Time, min") + ": " + Math.Round(row.Seconds / 60.0).ToString(inv);
                    pdf.Text(left, y, 11, Cut(pdf, totals, 11, right - left));
                    y += 30;
                }
                pdf.Text(left, y, 10, text("Exercise"), true);
                RightText(pdf, edge[0], y, 10, text("Best"), true);
                RightText(pdf, edge[1], y, 10, text("Attempts"), true);
                RightText(pdf, edge[2], y, 10, text("Stars"), true);
                RightText(pdf, edge[3], y, 10, text("Passed"), true);
                pdf.Line(left, y + 6, right, y + 6, 0.8f);
                y += 22;
            }

            Head(true);
            for (int i = 0; i < j.Activities.Count; i++)
            {
                if (y > PdfWriter.PageHeight - 90) Head(false);
                JournalCell c = row.Cells[i];
                pdf.Text(left, y, 10, Cut(pdf, Title(j.Activities[i], text), 10, edge[0] - 60 - left));
                RightText(pdf, edge[0], y, 10, c.Best == null ? "—" : Math.Round(c.Best.Value).ToString(inv));
                RightText(pdf, edge[1], y, 10, c.Attempts.ToString(inv));
                RightText(pdf, edge[2], y, 10, c.Attempts == 0 ? "" : c.Stars.ToString(inv));
                RightText(pdf, edge[3], y, 10, c.Attempts == 0 ? "" : text(c.Passed ? "yes" : "no"), c.Passed);
                pdf.Line(left, y + 5, right, y + 5, 0.2f);
                y += 18;
            }
            // The line for the signature goes under the table, or alone on a new page when the table filled this one.
            if (y > PdfWriter.PageHeight - 75)
            {
                pdf.AddPage();
                y = 40;
            }
            y += 30;
            string teacher = text("Teacher") + ":";
            pdf.Text(left, y, 11, teacher);
            pdf.Line(left + pdf.TextWidth(teacher, 11) + 10, y + 2, left + 320, y + 2, 0.5f);
            return pdf;
        }

        private static void RightText(PdfWriter pdf, float rightEdge, float y, float size, string s, bool bold = false) =>
            pdf.Text(rightEdge - pdf.TextWidth(s, size), y, size, s, bold);

        /// <summary>The text, shortened with "…" when it is wider than the room for it.</summary>
        private static string Cut(PdfWriter pdf, string s, float size, float room)
        {
            if (pdf.TextWidth(s, size) <= room) return s;
            while (s.Length > 1 && pdf.TextWidth(s + "…", size) > room) s = s.Substring(0, s.Length - 1);
            return s + "…";
        }
    }
}
