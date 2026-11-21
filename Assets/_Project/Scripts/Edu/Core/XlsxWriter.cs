using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Ushqyr.Edu
{
    /// <summary>
    /// Writes a small .xlsx workbook (sheets of text and number cells, a bold first row) with nothing but the zip
    /// support of .NET. Text is stored as inline strings in UTF-8, so Kazakh, Russian and English need no extra care.
    /// </summary>
    public sealed class XlsxWriter
    {
        private sealed class Sheet
        {
            public string Name;
            public readonly List<object[]> Rows = new List<object[]>();
            public double[] Widths;
        }

        private readonly List<Sheet> _sheets = new List<Sheet>();

        /// <summary>Starts a new sheet. Excel allows 31 characters and none of \ / ? * [ ] : in a sheet name.</summary>
        public void AddSheet(string name, double[] columnWidths = null)
        {
            var clean = new StringBuilder();
            foreach (char c in name ?? "Sheet") clean.Append("\\/?*[]:".IndexOf(c) >= 0 ? ' ' : c);
            if (clean.Length > 31) clean.Length = 31;
            if (clean.Length == 0) clean.Append("Sheet");
            _sheets.Add(new Sheet { Name = clean.ToString(), Widths = columnWidths });
        }

        /// <summary>Adds a row to the last sheet. Numbers stay numbers; everything else is written as text.</summary>
        public void AddRow(params object[] cells)
        {
            if (_sheets.Count == 0) AddSheet("Sheet1");
            _sheets[_sheets.Count - 1].Rows.Add(cells ?? new object[0]);
        }

        public void Save(string path)
        {
            using (var file = new FileStream(path, FileMode.Create, FileAccess.Write)) Save(file);
        }

        public void Save(Stream stream)
        {
            if (_sheets.Count == 0) AddSheet("Sheet1");
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
            {
                var types = new StringBuilder();
                var rels = new StringBuilder();
                var list = new StringBuilder();
                for (int i = 0; i < _sheets.Count; i++)
                {
                    int n = i + 1;
                    types.Append("<Override PartName=\"/xl/worksheets/sheet" + n + ".xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
                    rels.Append("<Relationship Id=\"rId" + n + "\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet" + n + ".xml\"/>");
                    list.Append("<sheet name=\"" + Escape(_sheets[i].Name) + "\" sheetId=\"" + n + "\" r:id=\"rId" + n + "\"/>");
                    Put(zip, "xl/worksheets/sheet" + n + ".xml", SheetXml(_sheets[i]));
                }
                Put(zip, "[Content_Types].xml",
                    "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                    "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                    "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                    "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
                    "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
                    types + "</Types>");
                Put(zip, "_rels/.rels",
                    "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                    "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
                    "</Relationships>");
                Put(zip, "xl/workbook.xml",
                    "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
                    "<sheets>" + list + "</sheets></workbook>");
                Put(zip, "xl/_rels/workbook.xml.rels",
                    "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" + rels +
                    "<Relationship Id=\"rId" + (_sheets.Count + 1) + "\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
                    "</Relationships>");
                Put(zip, "xl/styles.xml",
                    "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
                    "<fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts>" +
                    "<fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills>" +
                    "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>" +
                    "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
                    "<cellXfs count=\"2\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
                    "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/></cellXfs>" +
                    "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>" +
                    "</styleSheet>");
            }
        }

        private static string SheetXml(Sheet sheet)
        {
            var sb = new StringBuilder();
            sb.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
            if (sheet.Widths != null && sheet.Widths.Length > 0)
            {
                sb.Append("<cols>");
                for (int c = 0; c < sheet.Widths.Length; c++)
                    sb.Append("<col min=\"" + (c + 1) + "\" max=\"" + (c + 1) + "\" width=\"" +
                              sheet.Widths[c].ToString("0.##", CultureInfo.InvariantCulture) + "\" customWidth=\"1\"/>");
                sb.Append("</cols>");
            }
            sb.Append("<sheetData>");
            for (int r = 0; r < sheet.Rows.Count; r++)
            {
                sb.Append("<row r=\"" + (r + 1) + "\">");
                object[] row = sheet.Rows[r];
                for (int c = 0; c < row.Length; c++)
                {
                    object v = row[c];
                    if (v == null) continue;
                    string at = ColumnName(c) + (r + 1);
                    string style = r == 0 ? " s=\"1\"" : "";
                    if (IsNumber(v))
                        sb.Append("<c r=\"" + at + "\"" + style + "><v>" +
                                  Convert.ToDouble(v, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture) + "</v></c>");
                    else
                        sb.Append("<c r=\"" + at + "\"" + style + " t=\"inlineStr\"><is><t xml:space=\"preserve\">" +
                                  Escape(Convert.ToString(v, CultureInfo.InvariantCulture)) + "</t></is></c>");
                }
                sb.Append("</row>");
            }
            sb.Append("</sheetData></worksheet>");
            return sb.ToString();
        }

        private static bool IsNumber(object v)
        {
            if (v is double d) return !double.IsNaN(d) && !double.IsInfinity(d);
            if (v is float f) return !float.IsNaN(f) && !float.IsInfinity(f);
            return v is int || v is long || v is short || v is byte || v is uint || v is decimal;
        }

        /// <summary>0 → A, 25 → Z, 26 → AA.</summary>
        public static string ColumnName(int index)
        {
            string s = "";
            for (int n = index + 1; n > 0; n = (n - 1) / 26) s = (char)('A' + (n - 1) % 26) + s;
            return s;
        }

        /// <summary>XML text escaping; characters XML 1.0 does not allow are dropped.</summary>
        public static string Escape(string s)
        {
            var sb = new StringBuilder(s == null ? 0 : s.Length + 8);
            foreach (char c in s ?? "")
            {
                if (c == '&') sb.Append("&amp;");
                else if (c == '<') sb.Append("&lt;");
                else if (c == '>') sb.Append("&gt;");
                else if (c == '"') sb.Append("&quot;");
                else if (c < 0x20 && c != '\t' && c != '\n' && c != '\r') continue;
                else if (c == 0xFFFE || c == 0xFFFF) continue;
                else sb.Append(c);
            }
            return sb.ToString();
        }

        private static void Put(ZipArchive zip, string name, string xml)
        {
            ZipArchiveEntry entry = zip.CreateEntry(name, CompressionLevel.Optimal);
            using (Stream s = entry.Open())
            {
                byte[] bytes = new UTF8Encoding(false).GetBytes("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n" + xml);
                s.Write(bytes, 0, bytes.Length);
            }
        }
    }
}
