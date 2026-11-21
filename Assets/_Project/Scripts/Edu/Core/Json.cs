using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Ushqyr.Edu
{
    /// <summary>
    /// A small JSON reader and writer with no engine types, for activity files whose shape varies from one activity to
    /// the next. Objects become <c>Dictionary&lt;string, object&gt;</c>, arrays <c>List&lt;object&gt;</c>, numbers
    /// <c>double</c>. A bad file is refused with the line and column of the fault.
    /// </summary>
    public static class Json
    {
        private const int MaxDepth = 64;

        public static object Parse(string text)
        {
            if (text == null) throw new FormatException("JSON: no text");
            int at = 0;
            object value = Value(text, ref at, 0);
            Space(text, ref at);
            if (at != text.Length) throw Fail(text, at, "text after the end of the value");
            return value;
        }

        public static Dictionary<string, object> ParseObject(string text) =>
            Parse(text) as Dictionary<string, object> ?? throw new FormatException("JSON: an object { } was expected at the top");

        private static object Value(string s, ref int at, int depth)
        {
            if (depth > MaxDepth) throw Fail(s, at, "nested too deeply");
            Space(s, ref at);
            if (at >= s.Length) throw Fail(s, at, "unexpected end");
            char c = s[at];
            if (c == '{')
            {
                var map = new Dictionary<string, object>();
                at++;
                Space(s, ref at);
                if (at < s.Length && s[at] == '}') { at++; return map; }
                while (true)
                {
                    Space(s, ref at);
                    if (at >= s.Length || s[at] != '"') throw Fail(s, at, "a name in quotes was expected");
                    string key = Text(s, ref at);
                    Space(s, ref at);
                    if (at >= s.Length || s[at] != ':') throw Fail(s, at, "':' was expected");
                    at++;
                    if (map.ContainsKey(key)) throw Fail(s, at, "the name '" + key + "' is used twice");
                    map[key] = Value(s, ref at, depth + 1);
                    Space(s, ref at);
                    if (at < s.Length && s[at] == ',') { at++; continue; }
                    if (at < s.Length && s[at] == '}') { at++; return map; }
                    throw Fail(s, at, "',' or '}' was expected");
                }
            }
            if (c == '[')
            {
                var list = new List<object>();
                at++;
                Space(s, ref at);
                if (at < s.Length && s[at] == ']') { at++; return list; }
                while (true)
                {
                    list.Add(Value(s, ref at, depth + 1));
                    Space(s, ref at);
                    if (at < s.Length && s[at] == ',') { at++; continue; }
                    if (at < s.Length && s[at] == ']') { at++; return list; }
                    throw Fail(s, at, "',' or ']' was expected");
                }
            }
            if (c == '"') return Text(s, ref at);
            if (Word(s, ref at, "true")) return true;
            if (Word(s, ref at, "false")) return false;
            if (Word(s, ref at, "null")) return null;
            int start = at;
            while (at < s.Length && (char.IsDigit(s[at]) || "+-.eE".IndexOf(s[at]) >= 0)) at++;
            if (at > start && double.TryParse(s.Substring(start, at - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double d) &&
                !double.IsInfinity(d))
                return d;
            throw Fail(s, start, "a value was expected");
        }

        private static bool Word(string s, ref int at, string word)
        {
            if (string.CompareOrdinal(s, at, word, 0, word.Length) != 0) return false;
            at += word.Length;
            return true;
        }

        private static string Text(string s, ref int at)
        {
            var sb = new StringBuilder();
            at++;
            while (true)
            {
                if (at >= s.Length) throw Fail(s, at, "the text is not closed");
                char c = s[at++];
                if (c == '"') return sb.ToString();
                if (c < 0x20) throw Fail(s, at - 1, "a line break or control character inside text");
                if (c != '\\') { sb.Append(c); continue; }
                if (at >= s.Length) throw Fail(s, at, "the text is not closed");
                char e = s[at++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case '"': case '\\': case '/': sb.Append(e); break;
                    case 'u':
                        if (at + 4 > s.Length || !int.TryParse(s.Substring(at, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
                            throw Fail(s, at, "\\u needs four hex digits");
                        sb.Append((char)code);
                        at += 4;
                        break;
                    default: throw Fail(s, at - 1, "unknown escape \\" + e);
                }
            }
        }

        private static void Space(string s, ref int at)
        {
            while (at < s.Length && (s[at] == ' ' || s[at] == '\n' || s[at] == '\r' || s[at] == '\t')) at++;
        }

        private static FormatException Fail(string s, int at, string what)
        {
            int line = 1, col = 1;
            for (int i = 0; i < at && i < s.Length; i++)
            {
                if (s[i] == '\n') { line++; col = 1; }
                else col++;
            }
            return new FormatException("JSON, line " + line + ", column " + col + ": " + what);
        }

        /// <summary>Writes a value back as compact JSON with the names of every object in alphabetical order, so the same data always gives the same text (and the same hash).</summary>
        public static string Write(object value)
        {
            var sb = new StringBuilder();
            Write(sb, value);
            return sb.ToString();
        }

        private static void Write(StringBuilder sb, object v)
        {
            if (v == null) sb.Append("null");
            else if (v is bool b) sb.Append(b ? "true" : "false");
            else if (v is string s) Quote(sb, s);
            else if (v is Dictionary<string, object> map)
            {
                var keys = new List<string>(map.Keys);
                keys.Sort(string.CompareOrdinal);
                sb.Append('{');
                for (int i = 0; i < keys.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    Quote(sb, keys[i]);
                    sb.Append(':');
                    Write(sb, map[keys[i]]);
                }
                sb.Append('}');
            }
            else if (v is List<object> list)
            {
                sb.Append('[');
                for (int i = 0; i < list.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    Write(sb, list[i]);
                }
                sb.Append(']');
            }
            else sb.Append(Convert.ToDouble(v, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture));
        }

        private static void Quote(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c == '\n') sb.Append("\\n");
                else if (c == '\r') sb.Append("\\r");
                else if (c == '\t') sb.Append("\\t");
                else if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                else sb.Append(c);
            }
            sb.Append('"');
        }

        // Readers with defaults, for code that walks a parsed file.
        public static double Num(Dictionary<string, object> map, string key, double fallback) =>
            map != null && map.TryGetValue(key, out object v) && v is double d ? d : fallback;

        public static string Str(Dictionary<string, object> map, string key, string fallback = null) =>
            map != null && map.TryGetValue(key, out object v) && v is string s ? s : fallback;

        public static bool Flag(Dictionary<string, object> map, string key, bool fallback = false) =>
            map != null && map.TryGetValue(key, out object v) && v is bool b ? b : fallback;

        public static Dictionary<string, object> Obj(Dictionary<string, object> map, string key) =>
            map != null && map.TryGetValue(key, out object v) ? v as Dictionary<string, object> : null;

        public static List<object> Arr(Dictionary<string, object> map, string key) =>
            map != null && map.TryGetValue(key, out object v) ? v as List<object> : null;
    }
}
