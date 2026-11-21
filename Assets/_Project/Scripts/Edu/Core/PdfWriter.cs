using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Ushqyr.Edu
{
    /// <summary>
    /// Writes a PDF with real, selectable text in any language the given TrueType font covers. The font file is put
    /// into the PDF, so the report looks the same on a PC that does not have it. Pages are A4, coordinates are in
    /// points (1/72 inch) from the top left corner.
    /// </summary>
    public sealed class PdfWriter
    {
        public const float PageWidth = 595.28f;
        public const float PageHeight = 841.89f;

        private readonly TrueTypeFont _font;
        private readonly string _fontName;
        private readonly List<StringBuilder> _pages = new List<StringBuilder>();
        private readonly SortedDictionary<int, int> _used = new SortedDictionary<int, int>(); // glyph → code point

        public PdfWriter(byte[] trueTypeFont, string fontName)
        {
            _font = new TrueTypeFont(trueTypeFont);
            var name = new StringBuilder();
            foreach (char c in fontName ?? "Font") if (char.IsLetterOrDigit(c) && c < 128) name.Append(c);
            _fontName = name.Length > 0 ? name.ToString() : "Font";
        }

        public int PageCount => _pages.Count;

        public void AddPage() => _pages.Add(new StringBuilder());

        /// <summary>Width of the text in points at this size.</summary>
        public float TextWidth(string text, float size)
        {
            long units = 0;
            foreach (int cp in CodePoints(text)) units += _font.Advance(_font.Glyph(cp));
            return units * size / _font.UnitsPerEm;
        }

        /// <summary>Characters of the text the font has no glyph for (they would print as empty boxes).</summary>
        public string Missing(string text)
        {
            var sb = new StringBuilder();
            foreach (int cp in CodePoints(text))
                if (cp > 32 && _font.Glyph(cp) == 0) sb.Append(char.ConvertFromUtf32(cp));
            return sb.ToString();
        }

        /// <summary>One line of text; x is its left edge and y its baseline, measured from the top of the page.</summary>
        public void Text(float x, float y, float size, string text, bool bold = false)
        {
            if (_pages.Count == 0) AddPage();
            var hex = new StringBuilder();
            foreach (int cp in CodePoints(text))
            {
                int glyph = _font.Glyph(cp);
                if (!_used.ContainsKey(glyph)) _used[glyph] = cp;
                hex.Append(glyph.ToString("X4"));
            }
            StringBuilder page = _pages[_pages.Count - 1];
            // The font file has one weight; bold is the same outline filled and stroked.
            page.Append("BT /F1 " + F(size) + " Tf " + (bold ? "2 Tr " + F(size * 0.03f) + " w " : "0 Tr ") +
                        F(x) + " " + F(PageHeight - y) + " Td <" + hex + "> Tj ET\n");
        }

        public void Line(float x1, float y1, float x2, float y2, float width = 0.5f)
        {
            if (_pages.Count == 0) AddPage();
            _pages[_pages.Count - 1].Append(F(width) + " w " + F(x1) + " " + F(PageHeight - y1) + " m " +
                                             F(x2) + " " + F(PageHeight - y2) + " l S\n");
        }

        public void Save(string path)
        {
            using (var file = new FileStream(path, FileMode.Create, FileAccess.Write)) Save(file);
        }

        public void Save(Stream output)
        {
            if (_pages.Count == 0) AddPage();
            var objects = new List<byte[]>();
            int firstPage = 8; // objects 1..7 are the catalogue, the page tree and the font
            var kids = new StringBuilder();
            for (int i = 0; i < _pages.Count; i++) kids.Append((firstPage + i * 2) + " 0 R ");

            float k = 1000f / _font.UnitsPerEm;
            var widths = new StringBuilder();
            var map = new StringBuilder();
            foreach (KeyValuePair<int, int> g in _used)
            {
                widths.Append(g.Key + " [" + F(_font.Advance(g.Key) * k) + "] ");
                string utf16 = "";
                foreach (char c in char.ConvertFromUtf32(g.Value)) utf16 += ((int)c).ToString("X4");
                map.Append("<" + g.Key.ToString("X4") + "> <" + utf16 + ">\n");
            }
            string toUnicode =
                "/CIDInit /ProcSet findresource begin\n12 dict begin\nbegincmap\n" +
                "/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def\n" +
                "/CMapName /Adobe-Identity-UCS def\n/CMapType 2 def\n" +
                "1 begincodespacerange\n<0000> <FFFF>\nendcodespacerange\n" + Chunked(map.ToString()) +
                "endcmap\nCMapName currentdict /CMap defineresource pop\nend\nend\n";

            objects.Add(Ascii("<< /Type /Catalog /Pages 2 0 R >>"));
            objects.Add(Ascii("<< /Type /Pages /Kids [" + kids + "] /Count " + _pages.Count + " >>"));
            objects.Add(Ascii("<< /Type /Font /Subtype /Type0 /BaseFont /" + _fontName +
                              " /Encoding /Identity-H /DescendantFonts [4 0 R] /ToUnicode 7 0 R >>"));
            objects.Add(Ascii("<< /Type /Font /Subtype /CIDFontType2 /BaseFont /" + _fontName +
                              " /CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >>" +
                              " /FontDescriptor 5 0 R /DW 1000 /W [" + widths + "] /CIDToGIDMap /Identity >>"));
            objects.Add(Ascii("<< /Type /FontDescriptor /FontName /" + _fontName + " /Flags 4 /FontBBox [" +
                              F(_font.XMin * k) + " " + F(_font.YMin * k) + " " + F(_font.XMax * k) + " " + F(_font.YMax * k) +
                              "] /ItalicAngle 0 /Ascent " + F(_font.Ascent * k) + " /Descent " + F(_font.Descent * k) +
                              " /CapHeight " + F(_font.Ascent * k) + " /StemV 80 /FontFile2 6 0 R >>"));
            objects.Add(StreamObject(_font.Data, "/Length1 " + _font.Data.Length));
            objects.Add(StreamObject(Ascii(toUnicode), ""));
            foreach (StringBuilder page in _pages)
            {
                int self = objects.Count + 1;
                objects.Add(Ascii("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 " + F(PageWidth) + " " + F(PageHeight) +
                                  "] /Resources << /Font << /F1 3 0 R >> >> /Contents " + (self + 1) + " 0 R >>"));
                objects.Add(StreamObject(Ascii(page.ToString()), ""));
            }

            var offsets = new List<long>();
            long at = 0;
            void Write(byte[] b) { output.Write(b, 0, b.Length); at += b.Length; }
            Write(new byte[] { (byte)'%', (byte)'P', (byte)'D', (byte)'F', (byte)'-', (byte)'1', (byte)'.', (byte)'7', 10,
                               (byte)'%', 0xE2, 0xE3, 0xCF, 0xD3, 10 });
            for (int i = 0; i < objects.Count; i++)
            {
                offsets.Add(at);
                Write(Ascii((i + 1) + " 0 obj\n"));
                Write(objects[i]);
                Write(Ascii("\nendobj\n"));
            }
            long xref = at;
            var tail = new StringBuilder("xref\n0 " + (objects.Count + 1) + "\n0000000000 65535 f \n");
            foreach (long o in offsets) tail.Append(o.ToString("D10") + " 00000 n \n");
            tail.Append("trailer\n<< /Size " + (objects.Count + 1) + " /Root 1 0 R >>\nstartxref\n" + xref + "\n%%EOF\n");
            Write(Ascii(tail.ToString()));
        }

        private static string Chunked(string lines)
        {
            // A PDF reader accepts at most 100 mappings in one block.
            string[] all = lines.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var sb = new StringBuilder();
            for (int i = 0; i < all.Length; i += 100)
            {
                int n = Math.Min(100, all.Length - i);
                sb.Append(n + " beginbfchar\n");
                for (int j = 0; j < n; j++) sb.Append(all[i + j] + "\n");
                sb.Append("endbfchar\n");
            }
            return sb.ToString();
        }

        private static byte[] StreamObject(byte[] data, string extra)
        {
            byte[] packed = Zlib(data);
            byte[] head = Ascii("<< /Length " + packed.Length + " /Filter /FlateDecode " + extra + " >>\nstream\n");
            byte[] foot = Ascii("\nendstream");
            var all = new byte[head.Length + packed.Length + foot.Length];
            Buffer.BlockCopy(head, 0, all, 0, head.Length);
            Buffer.BlockCopy(packed, 0, all, head.Length, packed.Length);
            Buffer.BlockCopy(foot, 0, all, head.Length + packed.Length, foot.Length);
            return all;
        }

        /// <summary>Deflate in the zlib wrapper PDF asks for (2 bytes of header, Adler-32 at the end).</summary>
        public static byte[] Zlib(byte[] data)
        {
            using (var ms = new MemoryStream())
            {
                ms.WriteByte(0x78);
                ms.WriteByte(0x9C);
                using (var deflate = new DeflateStream(ms, CompressionLevel.Optimal, true)) deflate.Write(data, 0, data.Length);
                uint a = 1, b = 0;
                foreach (byte v in data)
                {
                    a = (a + v) % 65521;
                    b = (b + a) % 65521;
                }
                uint adler = (b << 16) | a;
                ms.WriteByte((byte)(adler >> 24));
                ms.WriteByte((byte)(adler >> 16));
                ms.WriteByte((byte)(adler >> 8));
                ms.WriteByte((byte)adler);
                return ms.ToArray();
            }
        }

        private static IEnumerable<int> CodePoints(string text)
        {
            if (text == null) yield break;
            for (int i = 0; i < text.Length; i++)
            {
                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    yield return char.ConvertToUtf32(text[i], text[i + 1]);
                    i++;
                }
                else yield return text[i];
            }
        }

        private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        private static byte[] Ascii(string s) => Encoding.ASCII.GetBytes(s);
    }

    /// <summary>The few tables of a TrueType file a PDF needs: character → glyph, glyph widths, the font's box.</summary>
    public sealed class TrueTypeFont
    {
        public readonly byte[] Data;
        public readonly int UnitsPerEm, XMin, YMin, XMax, YMax, Ascent, Descent;
        private readonly int _hmtx, _metrics, _glyphs;
        private readonly Dictionary<int, int> _cmap = new Dictionary<int, int>();

        public TrueTypeFont(byte[] data)
        {
            Data = data ?? throw new ArgumentNullException(nameof(data));
            var tables = new Dictionary<string, int>();
            int count = U16(4);
            for (int i = 0; i < count; i++)
            {
                int rec = 12 + i * 16;
                tables[Encoding.ASCII.GetString(data, rec, 4)] = (int)U32(rec + 8);
            }
            foreach (string need in new[] { "head", "hhea", "hmtx", "cmap", "maxp" })
                if (!tables.ContainsKey(need)) throw new InvalidDataException("not a TrueType font: no '" + need + "' table");
            int head = tables["head"], hhea = tables["hhea"];
            UnitsPerEm = U16(head + 18);
            XMin = S16(head + 36);
            YMin = S16(head + 38);
            XMax = S16(head + 40);
            YMax = S16(head + 42);
            Ascent = S16(hhea + 4);
            Descent = S16(hhea + 6);
            _metrics = U16(hhea + 34);
            _hmtx = tables["hmtx"];
            _glyphs = U16(tables["maxp"] + 4);
            ReadCmap(tables["cmap"]);
        }

        /// <summary>Glyph number of a character; 0 is the font's "missing" box.</summary>
        public int Glyph(int codePoint) => _cmap.TryGetValue(codePoint, out int g) ? g : 0;

        public int Advance(int glyph)
        {
            if (glyph < 0 || glyph >= _glyphs || _metrics == 0) return 0;
            return U16(_hmtx + Math.Min(glyph, _metrics - 1) * 4);
        }

        private void ReadCmap(int cmap)
        {
            int n = U16(cmap + 2), best = -1, bestRank = -1;
            for (int i = 0; i < n; i++)
            {
                int platform = U16(cmap + 4 + i * 8), encoding = U16(cmap + 6 + i * 8);
                int sub = cmap + (int)U32(cmap + 8 + i * 8);
                int format = U16(sub);
                int rank = format == 12 && (platform == 3 && encoding == 10 || platform == 0) ? 3
                    : format == 4 && platform == 3 && encoding == 1 ? 2
                    : format == 4 && platform == 0 ? 1 : -1;
                if (rank > bestRank) { bestRank = rank; best = sub; }
            }
            if (best < 0) throw new InvalidDataException("the font has no Unicode character map");
            if (U16(best) == 12)
            {
                long groups = U32(best + 12);
                for (long g = 0; g < groups; g++)
                {
                    int at = best + 16 + (int)g * 12;
                    long start = U32(at), end = U32(at + 4), glyph = U32(at + 8);
                    for (long c = start; c <= end && c - start < 70000; c++) _cmap[(int)c] = (int)(glyph + c - start);
                }
                return;
            }
            int seg = U16(best + 6) / 2;
            int ends = best + 14, starts = ends + seg * 2 + 2, deltas = starts + seg * 2, ranges = deltas + seg * 2;
            for (int s = 0; s < seg; s++)
            {
                int end = U16(ends + s * 2), start = U16(starts + s * 2), delta = U16(deltas + s * 2);
                int range = U16(ranges + s * 2);
                for (int c = start; c <= end && c != 0xFFFF; c++)
                {
                    int glyph;
                    if (range == 0) glyph = (c + delta) & 0xFFFF;
                    else
                    {
                        int at = ranges + s * 2 + range + (c - start) * 2;
                        if (at + 1 >= Data.Length) continue;
                        glyph = U16(at);
                        if (glyph != 0) glyph = (glyph + delta) & 0xFFFF;
                    }
                    if (glyph != 0) _cmap[c] = glyph;
                }
            }
        }

        private int U16(int at) => (Data[at] << 8) | Data[at + 1];
        private int S16(int at) => (short)U16(at);
        private uint U32(int at) => ((uint)Data[at] << 24) | ((uint)Data[at + 1] << 16) | ((uint)Data[at + 2] << 8) | Data[at + 3];
    }
}
