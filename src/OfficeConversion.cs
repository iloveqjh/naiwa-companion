// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace NaiwaCompanion
{
    internal static class OfficeConversion
    {
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        internal static void Perform(ConversionJob job)
        {
            string ext = Path.GetExtension(job.Input).ToLowerInvariant();
            if (Conversion.In(ext, Conversion.Sheets)) Spreadsheet(job);
            else if (Conversion.In(ext, Conversion.Slides)) Presentation(job);
            else Document(job);
        }
        private static HashSet<int> Existing(string name)
        { HashSet<int> ids = new HashSet<int>(); foreach (Process p in Process.GetProcessesByName(name)) { ids.Add(p.Id); p.Dispose(); } return ids; }
        private static void Claim(object application, HashSet<int> existing, string name, ConversionJob job)
        {
            uint id = 0;
            try { dynamic app = application; GetWindowThreadProcessId((IntPtr)(int)app.Hwnd, out id); } catch { }
            if (id == 0)
            {
                foreach (Process p in Process.GetProcessesByName(name)) using (p) if (!existing.Contains(p.Id)) { if (id != 0) throw new IOException("无法确认独立转换进程，未继续操作 Office。"); id = (uint)p.Id; }
            }
            if (id == 0 || existing.Contains((int)id)) throw new IOException("未获得独立的 " + name + " 转换实例。请先保存并关闭该 Office 应用，再重试。");
            using (Process process = Process.GetProcessById((int)id)) Conversion.RegisterProcess(job.ProcessFile, process);
        }
        private static void Release(object value)
        { if (value != null && Marshal.IsComObject(value)) try { Marshal.FinalReleaseComObject(value); } catch { } }
        private static string Output(ConversionJob job) { return Path.Combine(job.Work, "result." + job.Format); }
        internal static void VerifyWord(string file, int pages, ConversionJob job)
        {
            object application = null, document = null; bool owned = false; HashSet<int> before = Existing("WINWORD");
            try
            {
                application = Activator.CreateInstance(Type.GetTypeFromProgID("Word.Application", true)); Claim(application, before, "WINWORD", job); owned = true;
                dynamic app = application; app.Visible = false; app.DisplayAlerts = 0; app.AutomationSecurity = 3;
                document = app.Documents.Open(FileName:file, ConfirmConversions:false, ReadOnly:true, AddToRecentFiles:false, OpenAndRepair:false, NoEncodingDialog:true, Visible:false);
                if ((int)((dynamic)document).ComputeStatistics(2) != pages) throw new IOException("Word 重新打开后的页数与 PDF 不一致，未交付结果。");
            }
            finally
            {
                if (document != null) try { ((dynamic)document).Close(0); } catch { }
                if (owned && application != null) try { ((dynamic)application).Quit(0); } catch { }
                Release(document); Release(application);
            }
        }
        private static string ReadText(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length >= 2 && ((bytes[0] == 255 && bytes[1] == 254) || (bytes[0] == 254 && bytes[1] == 255))) return File.ReadAllText(path);
            try { return new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF'); }
            catch (DecoderFallbackException) { return Encoding.GetEncoding("GB18030", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback).GetString(bytes); }
        }
        private static void Document(ConversionJob job)
        {
            object appObject = null, documentObject = null; bool owned = false;
            HashSet<int> before = Existing("WINWORD");
            try
            {
                appObject = Activator.CreateInstance(Type.GetTypeFromProgID("Word.Application", true)); Claim(appObject, before, "WINWORD", job); owned = true;
                dynamic app = appObject; app.Visible = false; app.DisplayAlerts = 0; app.AutomationSecurity = 3; app.Options.SaveNormalPrompt = false; app.Options.UpdateLinksAtOpen = false;
                if (Path.GetExtension(job.Input).Equals(".txt", StringComparison.OrdinalIgnoreCase)) File.WriteAllText(job.Input, ReadText(job.Input), new UTF8Encoding(true));
                documentObject = app.Documents.Open(FileName:job.Input, ConfirmConversions:false, ReadOnly:true, AddToRecentFiles:false, PasswordDocument:Path.GetExtension(job.Input).Equals(".pdf", StringComparison.OrdinalIgnoreCase) ? "" : Guid.NewGuid().ToString("N").Substring(0, 15), OpenAndRepair:false, NoEncodingDialog:true, Visible:false);
                dynamic document = documentObject; int originalText = ((string)document.Content.Text).Trim().Length;
                int originalImages = (int)document.InlineShapes.Count + (int)document.Shapes.Count;
                if (Path.GetExtension(job.Input).Equals(".html", StringComparison.OrdinalIgnoreCase))
                {
                    for (int index = 1; index <= (int)document.InlineShapes.Count; index++)
                    { dynamic shape = document.InlineShapes[index]; try { shape.LinkFormat.SavePictureWithDocument = true; shape.LinkFormat.BreakLink(); } catch { } finally { Release(shape); } }
                    for (int index = 1; index <= (int)document.Shapes.Count; index++)
                    { dynamic shape = document.Shapes[index]; try { shape.LinkFormat.SavePictureWithDocument = true; shape.LinkFormat.BreakLink(); } catch { } finally { Release(shape); } }
                }
                int format = job.Format == "pdf" ? 17 : job.Format == "doc" ? 0 : job.Format == "rtf" ? 6 : job.Format == "txt" ? 7 : job.Format == "odt" ? 23 : 12;
                document.SaveAs2(FileName:Output(job), FileFormat:format, AddToRecentFiles:false);
                document.Close(0); Release(documentObject); documentObject = null;
                if (job.Format == "txt")
                {
                    string content = ReadText(Output(job)); File.WriteAllText(Output(job), content, new UTF8Encoding(true));
                    if (originalText > 0 && String.IsNullOrWhiteSpace(content)) throw new IOException("输出文字为空，未通过内容校验。");
                }
                else if (job.Format != "pdf")
                {
                    documentObject = app.Documents.Open(FileName:Output(job), ConfirmConversions:false, ReadOnly:true, AddToRecentFiles:false, OpenAndRepair:false, NoEncodingDialog:true);
                    dynamic verified = documentObject;
                    if (originalText > 0 && ((string)verified.Content.Text).Trim().Length == 0) throw new IOException("重新打开后缺少正文，未交付结果。");
                    if (originalImages > 0 && originalText == 0 && (int)verified.InlineShapes.Count + (int)verified.Shapes.Count == 0) throw new IOException("重新打开后缺少原图片，未交付结果。");
                }
            }
            finally
            {
                if (documentObject != null) try { ((dynamic)documentObject).Close(0); } catch { }
                if (owned && appObject != null) try { ((dynamic)appObject).Quit(0); } catch { }
                Release(documentObject); Release(appObject);
            }
        }
        private static List<List<string>> CsvRows(string text)
        {
            bool quoted = false, afterQuote = false, ended = false; StringBuilder cell = new StringBuilder();
            List<List<string>> rows = new List<List<string>>(); List<string> row = new List<string>();
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i]; ended = false;
                if (quoted)
                {
                    if (c == '"') { if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; } else { quoted = false; afterQuote = true; } }
                    else cell.Append(c);
                }
                else
                {
                    if (afterQuote && c != ',' && c != '\r' && c != '\n') throw new IOException("CSV 引号结束后存在多余字符，请修复原文件后重试。");
                    if (c == '"') { if (cell.Length != 0) throw new IOException("CSV 字段中有未转义的引号，请修复原文件后重试。"); quoted = true; }
                    else if (c == ',') { row.Add(cell.ToString()); cell.Length = 0; afterQuote = false; }
                    else if (c == '\n' || c == '\r')
                    { row.Add(cell.ToString()); cell.Length = 0; rows.Add(row); row = new List<string>(); afterQuote = false; ended = true; if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++; }
                    else cell.Append(c);
                }
                if (row.Count > 16384 || rows.Count > 1048576 || cell.Length > 32767) throw new IOException("CSV 超过 Excel 的行列或单元格长度限制。");
            }
            if (quoted) throw new IOException("CSV 引号不完整，请修复原文件后重试。");
            if (!ended) { row.Add(cell.ToString()); rows.Add(row); }
            if (rows.Count > 1048576) throw new IOException("CSV 行数超过 Excel 支持范围。");
            foreach (List<string> item in rows) if (item.Count > 16384) throw new IOException("CSV 列数超过 Excel 支持范围。");
            return rows;
        }
        private static void Spreadsheet(ConversionJob job)
        {
            object appObject = null, bookObject = null; bool owned = false; HashSet<int> before = Existing("EXCEL");
            try
            {
                appObject = Activator.CreateInstance(Type.GetTypeFromProgID("Excel.Application", true)); Claim(appObject, before, "EXCEL", job); owned = true;
                dynamic app = appObject; app.Visible = false; app.DisplayAlerts = false; app.EnableEvents = false; app.AskToUpdateLinks = false; app.AutomationSecurity = 3;
                if (Path.GetExtension(job.Input).Equals(".csv", StringComparison.OrdinalIgnoreCase))
                {
                    List<List<string>> rows = CsvRows(ReadText(job.Input)); int columns = 1;
                    foreach (List<string> row in rows) columns = Math.Max(columns, row.Count);
                    if ((long)rows.Count * columns > 2000000) throw new IOException("CSV 超过两百万个单元格，请分批转换。");
                    bookObject = app.Workbooks.Add(-4167); dynamic sheet = ((dynamic)bookObject).Worksheets[1];
                    object[,] values = new object[rows.Count, columns];
                    for (int r = 0; r < rows.Count; r++) for (int c = 0; c < rows[r].Count; c++) values[r, c] = rows[r][c].Replace("\r\n", "\n").Replace("\r", "\n");
                    dynamic range = sheet.Range[sheet.Cells[1, 1], sheet.Cells[rows.Count, columns]];
                    try
                    {
                        range.NumberFormat = "@"; range.Value2 = values;
                        range.Columns.AutoFit();
                        for (int column = 1; column <= columns; column++) if ((double)sheet.Columns[column].ColumnWidth > 60) sheet.Columns[column].ColumnWidth = 60;
                        range.WrapText = true; range.Rows.AutoFit();
                    }
                    finally { Release(range); Release(sheet); }
                }
                else bookObject = app.Workbooks.Open(Filename:job.Input, UpdateLinks:0, ReadOnly:true, Password:Guid.NewGuid().ToString("N").Substring(0, 15), WriteResPassword:Guid.NewGuid().ToString("N").Substring(0, 15), IgnoreReadOnlyRecommended:true, Notify:false, AddToMru:false);
                dynamic book = bookObject; int count = (int)book.Worksheets.Count;
                if (job.Format == "xls") for (int index = 1; index <= count; index++)
                {
                    object sheetObject = book.Worksheets[index], usedObject = null;
                    try
                    {
                        dynamic sheet = sheetObject; usedObject = sheet.UsedRange; dynamic used = usedObject;
                        if ((long)used.Row + (int)used.Rows.Count - 1 > 65536 || (long)used.Column + (int)used.Columns.Count - 1 > 256)
                            throw new IOException("工作表「" + sheet.Name + "」超出 XLS 的 65536 行或 256 列限制。为避免截断数据，请保留 XLSX 或 ODS。");
                    }
                    finally { Release(usedObject); Release(sheetObject); }
                }
                if (job.Format == "pdf") book.ExportAsFixedFormat(Type:0, Filename:Output(job), Quality:0, IncludeDocProperties:false, IgnorePrintAreas:false, OpenAfterPublish:false);
                else if (job.Format == "csv")
                {
                    for (int index = 1; index <= count; index++)
                    {
                        object sheetObject = null, copyObject = null, usedObject = null, destinationObject = null, rangeObject = null;
                        try
                        {
                            sheetObject = book.Worksheets[index]; dynamic sheet = sheetObject; string name = SafeName((string)sheet.Name);
                            usedObject = sheet.UsedRange; dynamic used = usedObject;
                            copyObject = app.Workbooks.Add(-4167); destinationObject = ((dynamic)copyObject).Worksheets[1]; dynamic destination = destinationObject;
                            used.Copy(Destination:destination.Cells[(int)used.Row, (int)used.Column]);
                            rangeObject = destination.UsedRange; ((dynamic)rangeObject).Value2 = used.Value2;
                            ((dynamic)copyObject).SaveAs(Filename:Path.Combine(job.Work, index.ToString("000") + "_" + name + ".csv"), FileFormat:62, AddToMru:false, Local:true);
                        }
                        finally { if (copyObject != null) try { ((dynamic)copyObject).Close(false); } catch { } Release(rangeObject); Release(destinationObject); Release(usedObject); Release(copyObject); Release(sheetObject); }
                    }
                }
                else
                {
                    book.SaveAs(Filename:Output(job), FileFormat:job.Format == "xls" ? 56 : job.Format == "ods" ? 60 : 51, AddToMru:false);
                    book.Close(false); Release(bookObject); bookObject = null;
                    bookObject = app.Workbooks.Open(Filename:Output(job), UpdateLinks:0, ReadOnly:true, IgnoreReadOnlyRecommended:true, AddToMru:false);
                    if ((int)((dynamic)bookObject).Worksheets.Count != count) throw new IOException("重新打开后工作表数量不一致。");
                }
            }
            finally
            {
                if (bookObject != null) try { ((dynamic)bookObject).Close(false); } catch { }
                if (owned && appObject != null) try { ((dynamic)appObject).Quit(); } catch { }
                Release(bookObject); Release(appObject);
            }
        }
        private static string SafeName(string value)
        { foreach (char c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '_'); return value.Length > 60 ? value.Substring(0, 60) : value; }
        private static void Presentation(ConversionJob job)
        {
            object appObject = null, deckObject = null; bool owned = false; HashSet<int> before = Existing("POWERPNT");
            try
            {
                appObject = Activator.CreateInstance(Type.GetTypeFromProgID("PowerPoint.Application", true)); Claim(appObject, before, "POWERPNT", job); owned = true;
                dynamic app = appObject; app.DisplayAlerts = 1; app.AutomationSecurity = 3;
                deckObject = app.Presentations.Open(FileName:job.Input, ReadOnly:-1, Untitled:0, WithWindow:0);
                dynamic deck = deckObject; int count = (int)deck.Slides.Count;
                if (job.Format == "png" || job.Format == "jpg") deck.Export(job.Work, job.Format.ToUpperInvariant(), 1600, Math.Max(1, (int)Math.Round(1600.0 * (double)deck.PageSetup.SlideHeight / (double)deck.PageSetup.SlideWidth)));
                else
                {
                    int format = job.Format == "pdf" ? 32 : job.Format == "ppt" ? 1 : job.Format == "odp" ? 35 : 24;
                    deck.SaveAs(Output(job), format); deck.Close(); Release(deckObject); deckObject = null;
                    if (job.Format != "pdf") { deckObject = app.Presentations.Open(FileName:Output(job), ReadOnly:-1, Untitled:0, WithWindow:0); if ((int)((dynamic)deckObject).Slides.Count != count) throw new IOException("重新打开后幻灯片数量不一致。"); }
                }
            }
            finally
            {
                if (deckObject != null) try { ((dynamic)deckObject).Close(); } catch { }
                if (owned && appObject != null) try { ((dynamic)appObject).Quit(); } catch { }
                Release(deckObject); Release(appObject);
            }
        }
    }
}
