using System.Collections.Generic;
using System.Text;

namespace GateRush.Core
{
    /// <summary>One translated string: the English key with its Kazakh and Russian text.</summary>
    public struct Translation
    {
        public string En, Kk, Ru;
        public string File;
        public int Line;
    }

    /// <summary>
    /// Reads the translation sheets in <c>Assets/_Project/Localization/Text</c>: tab-separated <c>en  kk  ru</c>, one
    /// string per line, <c>#</c> starts a comment line. "\n" and "\t" in a cell are a newline and a tab.
    /// </summary>
    public static class StringSheet
    {
        public static List<Translation> Parse(string text, string file = "")
        {
            var list = new List<Translation>();
            var lines = (text ?? "").Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.Trim().Length == 0 || line.TrimStart().StartsWith("#")) continue;
                var cells = line.Split('\t');
                list.Add(new Translation
                {
                    En = Unescape(cells[0]),
                    Kk = cells.Length > 1 ? Unescape(cells[1]) : "",
                    Ru = cells.Length > 2 ? Unescape(cells[2]) : "",
                    File = file,
                    Line = i + 1,
                });
            }
            return list;
        }

        public static string Unescape(string cell)
        {
            if (cell.IndexOf('\\') < 0) return cell;
            var sb = new StringBuilder(cell.Length);
            for (int i = 0; i < cell.Length; i++)
            {
                char c = cell[i];
                if (c == '\\' && i + 1 < cell.Length)
                {
                    char n = cell[++i];
                    sb.Append(n == 'n' ? '\n' : n == 't' ? '\t' : n);
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>The {0}, {1}... placeholders in a format string, sorted (to compare translations with the key).</summary>
        public static string Placeholders(string format)
        {
            var found = new SortedSet<string>();
            for (int i = 0; i < format.Length; i++)
            {
                if (format[i] != '{') continue;
                if (i + 1 < format.Length && format[i + 1] == '{')
                {
                    i++;
                    continue;
                }
                int end = format.IndexOf('}', i);
                if (end < 0) break;
                string inner = format.Substring(i + 1, end - i - 1);
                int colon = inner.IndexOf(':');
                found.Add(colon >= 0 ? inner.Substring(0, colon) : inner);
                i = end;
            }
            return string.Join(",", found);
        }
    }
}
