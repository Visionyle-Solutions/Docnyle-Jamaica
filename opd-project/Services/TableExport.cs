using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace OpdProject.Services
{
    /// <summary>
    /// Excel (.xlsx) and PDF downloads of an on-screen list, with no third-party libraries: the workbook is
    /// written as Office Open XML into a zip, the PDF as a plain landscape-A4 table in the standard Helvetica fonts.
    /// </summary>
    public static class TableExport
    {
        /// <summary>One list to export. Cells are strings (already formatted for display) or numbers (int, decimal, double).</summary>
        public class Table
        {
            public string Title { get; set; }
            /// <summary>Filters and context under the title, e.g. "All Doctors · Friday, 16 Oct 2026".</summary>
            public string Subtitle { get; set; }
            /// <summary>Hospital name, shown above the title.</summary>
            public string Organisation { get; set; }
            /// <summary>"Generated … by …", shown at the foot of every PDF page.</summary>
            public string Footer { get; set; }
            public List<string> Headers { get; set; }
            public List<object[]> Rows { get; set; }

            public Table() { Headers = new List<string>(); Rows = new List<object[]>(); }
        }

        private static string Text(object v)
        {
            if (v == null) return "";
            if (v is IFormattable) return ((IFormattable)v).ToString(null, CultureInfo.InvariantCulture);
            return v.ToString();
        }

        private static bool IsNumber(object v) { return v is int || v is long || v is decimal || v is double; }

        /* ── Excel ── */

        public static byte[] ToXlsx(Table t)
        {
            using (var ms = new MemoryStream())
            {
                using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
                {
                    Entry(zip, "[Content_Types].xml",
                        "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">"
                        + "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>"
                        + "<Default Extension=\"xml\" ContentType=\"application/xml\"/>"
                        + "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>"
                        + "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>"
                        + "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>"
                        + "</Types>");
                    Entry(zip, "_rels/.rels",
                        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
                        + "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>"
                        + "</Relationships>");
                    Entry(zip, "xl/workbook.xml",
                        "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">"
                        + "<sheets><sheet name=\"" + Xml(SheetName(t.Title)) + "\" sheetId=\"1\" r:id=\"rId1\"/></sheets>"
                        + "<definedNames><definedName name=\"_xlnm._FilterDatabase\" localSheetId=\"0\" hidden=\"1\">" + Xml("'" + SheetName(t.Title).Replace("'", "''") + "'!" + HeaderRange(t)) + "</definedName></definedNames>"
                        + "</workbook>");
                    Entry(zip, "xl/_rels/workbook.xml.rels",
                        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
                        + "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>"
                        + "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>"
                        + "</Relationships>");
                    /* styles: 0 normal, 1 bold header with grey fill and border, 2 bold title, 3 muted subtitle */
                    Entry(zip, "xl/styles.xml",
                        "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">"
                        + "<fonts count=\"4\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font>"
                        + "<font><b/><sz val=\"14\"/><name val=\"Calibri\"/></font><font><sz val=\"10\"/><color rgb=\"FF666666\"/><name val=\"Calibri\"/></font></fonts>"
                        + "<fills count=\"3\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill>"
                        + "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFEDEFF2\"/><bgColor indexed=\"64\"/></patternFill></fill></fills>"
                        + "<borders count=\"2\"><border><left/><right/><top/><bottom/><diagonal/></border>"
                        + "<border><left/><right/><top/><bottom style=\"thin\"><color rgb=\"FF9CA3AF\"/></bottom><diagonal/></border></borders>"
                        + "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>"
                        + "<cellXfs count=\"4\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>"
                        + "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"2\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\"/>"
                        + "<xf numFmtId=\"0\" fontId=\"2\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>"
                        + "<xf numFmtId=\"0\" fontId=\"3\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/></cellXfs>"
                        + "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>"
                        + "</styleSheet>");
                    Entry(zip, "xl/worksheets/sheet1.xml", Sheet(t));
                }
                return ms.ToArray();
            }
        }

        /* rows: [organisation], title, [subtitle], blank, header, data — the header row index is needed for the freeze pane and filter */
        private static List<object[]> Preamble(Table t)
        {
            var lines = new List<object[]>();
            if (!string.IsNullOrEmpty(t.Organisation)) lines.Add(new object[] { t.Organisation, 3 });
            lines.Add(new object[] { t.Title, 2 });
            if (!string.IsNullOrEmpty(t.Subtitle)) lines.Add(new object[] { t.Subtitle, 3 });
            return lines;
        }

        private static int HeaderRow(Table t) { return Preamble(t).Count + 2; }

        private static string HeaderRange(Table t)
        {
            var r = HeaderRow(t);
            return "$A$" + r + ":$" + Col(t.Headers.Count - 1) + "$" + (r + Math.Max(t.Rows.Count, 1));
        }

        private static string Sheet(Table t)
        {
            var headerRow = HeaderRow(t);
            var sb = new StringBuilder("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
            sb.Append("<sheetViews><sheetView workbookViewId=\"0\"><pane ySplit=\"" + headerRow + "\" topLeftCell=\"A" + (headerRow + 1) + "\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews>");
            sb.Append("<cols>");
            for (int c = 0; c < t.Headers.Count; c++)
            {
                var len = t.Rows.Select(r => c < r.Length ? Text(r[c]).Length : 0).Concat(new[] { t.Headers[c].Length }).Max();
                sb.Append("<col min=\"" + (c + 1) + "\" max=\"" + (c + 1) + "\" width=\"" + Math.Min(60, Math.Max(8, len + 3)) + "\" customWidth=\"1\"/>");
            }
            sb.Append("</cols><sheetData>");
            var row = 1;
            foreach (var p in Preamble(t)) { sb.Append("<row r=\"" + row + "\">" + Cell(0, row, p[0], (int)p[1]) + "</row>"); row++; }
            row++;
            sb.Append("<row r=\"" + row + "\">");
            for (int c = 0; c < t.Headers.Count; c++) sb.Append(Cell(c, row, t.Headers[c], 1));
            sb.Append("</row>");
            foreach (var r in t.Rows)
            {
                row++;
                sb.Append("<row r=\"" + row + "\">");
                for (int c = 0; c < t.Headers.Count; c++) sb.Append(Cell(c, row, c < r.Length ? r[c] : null, 0));
                sb.Append("</row>");
            }
            sb.Append("</sheetData>");
            sb.Append("<autoFilter ref=\"" + HeaderRange(t).Replace("$", "") + "\"/>");
            sb.Append("<pageSetup orientation=\"landscape\"/></worksheet>");
            return sb.ToString();
        }

        private static string Cell(int col, int row, object v, int style)
        {
            var r = Col(col) + row;
            var s = style == 0 ? "" : " s=\"" + style + "\"";
            if (IsNumber(v)) return "<c r=\"" + r + "\"" + s + "><v>" + Text(v) + "</v></c>";
            return "<c r=\"" + r + "\"" + s + " t=\"inlineStr\"><is><t xml:space=\"preserve\">" + Xml(Text(v)) + "</t></is></c>";
        }

        private static string Col(int index)
        {
            var name = "";
            for (index++; index > 0; index = (index - 1) / 26) name = (char)('A' + (index - 1) % 26) + name;
            return name;
        }

        private static string SheetName(string title)
        {
            var name = new string((title ?? "Sheet1").Where(ch => "[]:*?/\\".IndexOf(ch) < 0).ToArray()).Trim();
            if (name.Length == 0) name = "Sheet1";
            return name.Length > 31 ? name.Substring(0, 31) : name;
        }

        /* escapes markup and drops control characters XML cannot carry */
        private static string Xml(string s)
        {
            var sb = new StringBuilder();
            foreach (var ch in s ?? "")
            {
                if (ch < 0x20 && ch != '\t' && ch != '\n' && ch != '\r') continue;
                switch (ch)
                {
                    case '&': sb.Append("&amp;"); break;
                    case '<': sb.Append("&lt;"); break;
                    case '>': sb.Append("&gt;"); break;
                    case '"': sb.Append("&quot;"); break;
                    default: sb.Append(ch); break;
                }
            }
            return sb.ToString();
        }

        private static void Entry(ZipArchive zip, string path, string xml)
        {
            using (var w = new StreamWriter(zip.CreateEntry(path, CompressionLevel.Optimal).Open(), new UTF8Encoding(false)))
                w.Write("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n" + xml);
        }

        /* ── PDF ── */

        private const float PageW = 842, PageH = 595, Margin = 36, RowH = 16, BodySize = 9, Pad = 4;

        public static byte[] ToPdf(Table t)
        {
            var avail = PageW - 2 * Margin;
            var widths = ColumnWidths(t, avail);
            float top = PageH - Margin;
            var headTop = top - 20 - (string.IsNullOrEmpty(t.Organisation) ? 0 : 14) - (string.IsNullOrEmpty(t.Subtitle) ? 0 : 14) - 8;
            var perPage = Math.Max(1, (int)((headTop - RowH - (Margin + 18)) / RowH));
            var pageCount = Math.Max(1, (t.Rows.Count + perPage - 1) / perPage);

            var pages = new List<string>();
            for (int p = 0; p < pageCount; p++)
            {
                var sb = new StringBuilder();
                float y = top;
                if (!string.IsNullOrEmpty(t.Organisation)) { y -= 10; TextAt(sb, "F1", 9, Margin, y, t.Organisation, 0.4f); y -= 4; }
                y -= 14; TextAt(sb, "F2", 14, Margin, y, t.Title, 0);
                if (!string.IsNullOrEmpty(t.Subtitle)) { y -= 14; TextAt(sb, "F1", 9, Margin, y, t.Subtitle, 0.4f); }

                y = headTop;
                sb.Append("0.93 g " + N(Margin) + " " + N(y - RowH) + " " + N(avail) + " " + N(RowH) + " re f\n");
                RowText(sb, "F2", t.Headers.Cast<object>().ToArray(), widths, y);
                y -= RowH;
                foreach (var r in t.Rows.Skip(p * perPage).Take(perPage))
                {
                    RowText(sb, "F1", r, widths, y);
                    y -= RowH;
                    sb.Append("0.85 G 0.5 w " + N(Margin) + " " + N(y) + " m " + N(Margin + avail) + " " + N(y) + " l S\n");
                }
                if (t.Rows.Count == 0) { TextAt(sb, "F1", BodySize, Margin + Pad, y - 11, "No records.", 0.4f); }

                if (!string.IsNullOrEmpty(t.Footer)) TextAt(sb, "F1", 8, Margin, Margin - 6, t.Footer, 0.4f);
                var pageNo = "Page " + (p + 1) + " of " + pageCount;
                TextAt(sb, "F1", 8, PageW - Margin - Measure(pageNo, 8), Margin - 6, pageNo, 0.4f);
                pages.Add(sb.ToString());
            }
            return Assemble(pages);
        }

        /* natural width per column (widest cell, header capped at its own width), scaled to fill the printable width */
        private static float[] ColumnWidths(Table t, float avail)
        {
            var n = Math.Max(1, t.Headers.Count);
            var natural = new float[n];
            for (int c = 0; c < t.Headers.Count; c++)
            {
                var w = Measure(t.Headers[c], BodySize) * 1.08f;
                foreach (var r in t.Rows) if (c < r.Length) w = Math.Max(w, Measure(Text(r[c]), BodySize));
                natural[c] = Math.Min(w, avail * 0.4f) + 2 * Pad;
            }
            var total = natural.Sum();
            return natural.Select(w => w * avail / total).ToArray();
        }

        private static void RowText(StringBuilder sb, string font, object[] cells, float[] widths, float rowTop)
        {
            float x = Margin;
            for (int c = 0; c < widths.Length; c++)
            {
                var s = Fit(c < cells.Length ? Text(cells[c]) : "", widths[c] - 2 * Pad, font == "F2" ? 1.08f : 1f);
                var tx = IsNumber(c < cells.Length ? cells[c] : null) ? x + widths[c] - Pad - Measure(s, BodySize) : x + Pad;
                TextAt(sb, font, BodySize, tx, rowTop - 11, s, 0);
                x += widths[c];
            }
        }

        /* cuts text to the column with a trailing "..." */
        private static string Fit(string s, float width, float factor)
        {
            s = (s ?? "").Replace("\r", " ").Replace("\n", " ");
            if (Measure(s, BodySize) * factor <= width) return s;
            while (s.Length > 0 && Measure(s + "...", BodySize) * factor > width) s = s.Substring(0, s.Length - 1);
            return s.TrimEnd() + "...";
        }

        private static void TextAt(StringBuilder sb, string font, float size, float x, float y, string s, float grey)
        {
            sb.Append(N(grey) + " g BT /" + font + " " + N(size) + " Tf " + N(x) + " " + N(y) + " Td " + Literal(s) + " Tj ET\n");
        }

        private static string N(float v) { return v.ToString("0.##", CultureInfo.InvariantCulture); }

        private static readonly Encoding WinAnsi = Encoding.GetEncoding(1252, EncoderFallback.ReplacementFallback, DecoderFallback.ReplacementFallback);

        /* a PDF string in WinAnsiEncoding; bytes above ASCII are octal-escaped so the content stream stays 7-bit */
        private static string Literal(string s)
        {
            var sb = new StringBuilder("(");
            foreach (var b in WinAnsi.GetBytes(s ?? ""))
            {
                if (b == '(' || b == ')' || b == '\\') sb.Append('\\').Append((char)b);
                else if (b < 32 || b > 126) sb.Append('\\').Append(Convert.ToString(b, 8).PadLeft(3, '0'));
                else sb.Append((char)b);
            }
            return sb.Append(')').ToString();
        }

        /* Helvetica advance widths (1/1000 em) for ASCII 32–126 */
        private static readonly short[] Helvetica =
        {
            278,278,355,556,556,889,667,191,333,333,389,584,278,333,278,278,556,556,556,556,556,556,556,556,556,556,278,278,584,584,584,556,
            1015,667,667,722,722,667,611,778,722,278,500,667,556,833,722,778,667,778,722,667,611,722,667,944,667,667,611,278,278,278,469,556,
            333,556,556,500,556,556,278,556,556,222,222,500,222,833,556,556,556,556,333,500,278,556,500,722,500,500,500,334,260,334,584
        };

        private static float Measure(string s, float size)
        {
            float units = 0;
            foreach (var ch in s ?? "") units += ch >= 32 && ch <= 126 ? Helvetica[ch - 32] : ch == '—' ? 1000 : 556;
            return units * size / 1000f;
        }

        private static byte[] Assemble(List<string> pageStreams)
        {
            /* objects: 1 catalog, 2 page tree, 3 Helvetica, 4 Helvetica-Bold, then a page + content stream pair per page */
            var objects = new List<string>
            {
                "<< /Type /Catalog /Pages 2 0 R >>",
                "<< /Type /Pages /Kids [" + string.Join(" ", pageStreams.Select((_, i) => (5 + 2 * i) + " 0 R")) + "] /Count " + pageStreams.Count + " >>",
                "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
                "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>",
            };
            for (int i = 0; i < pageStreams.Count; i++)
            {
                objects.Add("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 " + N(PageW) + " " + N(PageH) + "] /Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents " + (6 + 2 * i) + " 0 R >>");
                objects.Add("<< /Length " + Encoding.ASCII.GetByteCount(pageStreams[i]) + " >>\nstream\n" + pageStreams[i] + "endstream");
            }

            var pdf = new StringBuilder("%PDF-1.4\n");
            var offsets = new List<int>();
            for (int i = 0; i < objects.Count; i++)
            {
                offsets.Add(pdf.Length);
                pdf.Append(i + 1).Append(" 0 obj\n").Append(objects[i]).Append("\nendobj\n");
            }
            var xref = pdf.Length;
            pdf.Append("xref\n0 ").Append(objects.Count + 1).Append("\n0000000000 65535 f \n");
            foreach (var o in offsets) pdf.Append(o.ToString("D10")).Append(" 00000 n \n");
            pdf.Append("trailer\n<< /Size ").Append(objects.Count + 1).Append(" /Root 1 0 R >>\nstartxref\n").Append(xref).Append("\n%%EOF\n");
            return Encoding.ASCII.GetBytes(pdf.ToString());
        }
    }
}
