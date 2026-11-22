using System;
using System.Globalization;

namespace Ushqyr.Edu
{
    /// <summary>
    /// The certificate of the training course (EDU_PLAN §4, exercise 30): one printable A4 page saying who finished the
    /// course and with what results. It is earned by passing the check ride. It says plainly that the flying was done in
    /// a simulator: it is a record of training, not a permit to fly.
    /// </summary>
    public sealed class Certificate
    {
        /// <summary>The exercise whose pass completes the course.</summary>
        public const string CheckRideId = "e30-check-ride";

        /// <summary>The student's name; empty leaves a line to write it on by hand (a seat is not a person).</summary>
        public string Student = "";
        public DateTime Date;
        public int Passed, Total, Stars, MaxStars;
        /// <summary>Best score of the check ride, and the average of the best scores of the exercises flown.</summary>
        public double CheckRide, Average;

        /// <summary>True when the student has passed the check ride.</summary>
        public static bool Earned(Course course)
        {
            CourseEntry ride = course.Find(CheckRideId);
            return ride != null && ride.Passed;
        }

        /// <summary>The certificate's figures, taken from the student's results; null when it is not earned yet.</summary>
        public static Certificate From(Course course, string student, DateTime date)
        {
            if (!Earned(course)) return null;
            var c = new Certificate { Student = student ?? "", Date = date, CheckRide = course.Find(CheckRideId).Best ?? 0 };
            double sum = 0;
            int flown = 0;
            foreach (string block in Course.Blocks)
                foreach (CourseEntry e in course.In(block))
                {
                    c.Total++;
                    c.MaxStars += 3;
                    c.Stars += e.Stars;
                    if (e.Passed) c.Passed++;
                    if (e.Best == null) continue;
                    sum += e.Best.Value;
                    flown++;
                }
            c.Average = flown > 0 ? sum / flown : 0;
            return c;
        }

        /// <summary>
        /// Draws the certificate as one PDF page. <paramref name="text"/> turns an English key into the language of the
        /// page; a key holding {0} and {1} is filled in here.
        /// </summary>
        public PdfWriter Write(byte[] trueTypeFont, Func<string, string> text)
        {
            var pdf = new PdfWriter(trueTypeFont, "LiberationSans");
            pdf.AddPage();
            const float w = PdfWriter.PageWidth, h = PdfWriter.PageHeight;
            // A double frame.
            Box(pdf, 28, 28, w - 28, h - 28, 2f);
            Box(pdf, 36, 36, w - 36, h - 36, 0.6f);

            Centre(pdf, 110, 16, "USHQYR");
            Centre(pdf, 180, 44, text("CERTIFICATE"), true);
            Centre(pdf, 215, 15, text("of the drone pilot training course"));
            pdf.Line(w / 2 - 120, 235, w / 2 + 120, 235, 0.8f);

            Centre(pdf, 290, 14, text("This certifies that"));
            if (Student.Length > 0) Centre(pdf, 340, Fit(pdf, Student, 30, w - 140), Student, true);
            pdf.Line(110, 352, w - 110, 352, 0.6f);
            Centre(pdf, 392, 14, text("has completed the training course of 30 exercises"));
            Centre(pdf, 414, 14, text("and passed the check ride."));

            float y = 480;
            Row(pdf, ref y, text("Exercises passed"), string.Format(CultureInfo.InvariantCulture, text("{0} of {1}"), Passed, Total));
            Row(pdf, ref y, text("Check ride"), string.Format(CultureInfo.InvariantCulture, text("{0} of {1}"), Math.Round(CheckRide), 100));
            Row(pdf, ref y, text("Average score"), Math.Round(Average).ToString(CultureInfo.InvariantCulture));
            Row(pdf, ref y, text("Stars"), string.Format(CultureInfo.InvariantCulture, text("{0} of {1}"), Stars, MaxStars));

            pdf.Text(90, 660, 12, text("Date") + ":  " + Date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture));
            // The lines to write on start after the longer of the two labels.
            string school = text("School") + ":", teacher = text("Teacher") + ":";
            float from = 90 + Math.Max(pdf.TextWidth(school, 12), pdf.TextWidth(teacher, 12)) + 10;
            pdf.Text(90, 700, 12, school);
            pdf.Line(from, 702, w - 90, 702, 0.5f);
            pdf.Text(90, 740, 12, teacher);
            pdf.Line(from, 742, w - 90, 742, 0.5f);

            // The small print, on as many lines as it needs.
            float line = 778;
            string row = "";
            foreach (string word in text("The flights were flown in the Ushqyr simulator. This certificate records training; it is not a permit to fly a drone.").Split(' '))
            {
                string longer = row.Length == 0 ? word : row + " " + word;
                if (row.Length > 0 && pdf.TextWidth(longer, 9) > w - 150)
                {
                    Centre(pdf, line, 9, row);
                    line += 12;
                    row = word;
                }
                else row = longer;
            }
            if (row.Length > 0) Centre(pdf, line, 9, row);
            return pdf;
        }

        /// <summary>Every text of the page that the font cannot print (empty when all of it can be).</summary>
        public string Missing(byte[] trueTypeFont, Func<string, string> text)
        {
            var pdf = new PdfWriter(trueTypeFont, "LiberationSans");
            string all = Student;
            foreach (string key in Texts) all += text(key);
            return pdf.Missing(all);
        }

        /// <summary>The keys of the page's texts (the language sheet must hold each of them).</summary>
        public static readonly string[] Texts =
        {
            "CERTIFICATE", "of the drone pilot training course", "This certifies that", "has completed the training course of 30 exercises",
            "and passed the check ride.", "Exercises passed", "Check ride", "Average score", "Stars", "{0} of {1}", "Date", "School", "Teacher",
            "The flights were flown in the Ushqyr simulator. This certificate records training; it is not a permit to fly a drone.",
        };

        private static void Centre(PdfWriter pdf, float y, float size, string s, bool bold = false) =>
            pdf.Text((PdfWriter.PageWidth - pdf.TextWidth(s, size)) / 2f, y, size, s, bold);

        /// <summary>The largest size, up to <paramref name="size"/>, at which the text is no wider than the room for it.</summary>
        private static float Fit(PdfWriter pdf, string s, float size, float room)
        {
            float wide = pdf.TextWidth(s, size);
            return wide <= room ? size : Math.Max(10f, size * room / wide);
        }

        private static void Row(PdfWriter pdf, ref float y, string name, string value)
        {
            pdf.Text(150, y, 14, name);
            pdf.Text(PdfWriter.PageWidth - 150 - pdf.TextWidth(value, 14), y, 14, value, true);
            pdf.Line(150, y + 8, PdfWriter.PageWidth - 150, y + 8, 0.3f);
            y += 34;
        }

        private static void Box(PdfWriter pdf, float x1, float y1, float x2, float y2, float width)
        {
            pdf.Line(x1, y1, x2, y1, width);
            pdf.Line(x2, y1, x2, y2, width);
            pdf.Line(x2, y2, x1, y2, width);
            pdf.Line(x1, y2, x1, y1, width);
        }
    }
}
