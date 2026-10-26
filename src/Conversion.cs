// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Xml;

namespace NaiwaCompanion
{
    internal sealed class ConverterTools
    {
        public int Version = 1;
        public string Ffmpeg = "", Poppler = "";
        internal static LocalData<ConverterTools> Load(bool ephemeral)
        { return new LocalData<ConverterTools>(ephemeral ? null : Path.Combine(Preferences.DirectoryPath, "conversion-tools.json"), delegate(ConverterTools t) { return t.Version == 1 && t.Ffmpeg != null && t.Poppler != null; }); }
        internal string Find(string executable)
        {
            string specified = executable == "ffmpeg.exe" ? Ffmpeg : Path.Combine(Poppler, executable);
            if (File.Exists(specified)) return Path.GetFullPath(specified);
            string bundled = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools", executable == "ffmpeg.exe" ? "ffmpeg" : "poppler", executable);
            if (File.Exists(bundled)) return bundled;
            foreach (string folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
                try { string candidate = Path.Combine(folder.Trim('"'), executable); if (File.Exists(candidate)) return Path.GetFullPath(candidate); } catch { }
            return null;
        }
    }
    internal sealed class ConversionRequest { public string Source, Format, OutputDirectory; }
    internal sealed class ConversionJob
    {
        public string Input, Format, Work, ResultFile, ProcessFile;
        public ConverterTools Tools;
    }
    internal sealed class ConversionReceipt
    {
        public bool Success;
        public string Error = "", Note = "", Output = "", SourceHash = "";
        public List<string> Files = new List<string>();
    }
    internal sealed class ConversionProcess { public int Id; public long Started; public string Name; }

    internal static class Conversion
    {
        internal static readonly string[] Documents = { ".docx", ".doc", ".odt", ".rtf", ".txt" };
        internal static readonly string[] Sheets = { ".xlsx", ".xls", ".ods", ".csv" };
        internal static readonly string[] Slides = { ".pptx", ".ppt", ".odp" };
        internal static readonly string[] Pictures = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp" };
        internal static readonly string[] Videos = { ".mp4", ".mov", ".mkv", ".avi", ".webm", ".m4v", ".wmv" };
        internal static readonly string[] Audio = { ".mp3", ".wav", ".flac", ".m4a", ".aac", ".ogg", ".wma", ".aiff" };
        internal static bool In(string extension, string[] values) { return Array.IndexOf(values, extension) >= 0; }
        internal static List<string> Targets(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant(); List<string> formats = new List<string>();
            if (ext == ".pdf") formats.AddRange(new string[] { "docx", "txt", "png", "jpg" });
            else if (In(ext, Documents)) formats.AddRange(new string[] { "docx", "pdf", "doc", "rtf", "txt", "odt" });
            else if (In(ext, Sheets)) formats.AddRange(new string[] { "xlsx", "pdf", "csv", "xls", "ods" });
            else if (In(ext, Slides)) formats.AddRange(new string[] { "pptx", "pdf", "png", "jpg", "ppt", "odp" });
            else if (In(ext, Pictures)) { formats.AddRange(new string[] { "png", "jpg", "pdf", "webp", "bmp", "tiff", "gif" }); if (ext == ".gif") formats.Add("mp4"); }
            else if (In(ext, Videos)) formats.AddRange(new string[] { "mp4", "mkv", "mov", "webm", "avi", "mp3", "wav", "flac", "m4a", "ogg", "gif" });
            else if (In(ext, Audio)) formats.AddRange(new string[] { "mp3", "wav", "flac", "m4a", "aac", "ogg" });
            formats.Remove(ext.TrimStart('.')); if (ext == ".jpeg") formats.Remove("jpg"); if (ext == ".tif") formats.Remove("tiff"); return formats;
        }
        internal static string Note(string source, string target)
        {
            string ext = Path.GetExtension(source).ToLowerInvariant();
            if (ext == ".pdf" && target == "docx") return "PDF 文字以可编辑文字框保留，图形嵌入页面；扫描文字需要 OCR，表格不自动变成 Word 表格。";
            if (In(ext, Sheets) && target == "csv") return "CSV 仅保存表格值，每张工作表分别输出；不保留格式、公式和图表。";
            if (ext == ".gif" && target != "mp4") return "静态图片和 PDF 保留 GIF 的第一帧；GIF 转 MP4 保留动画。";
            if (In(ext, Videos)) return "保留主画面及音轨；字幕和封面不转入。转 GIF 不含声音。";
            if (target == "txt") return "纯文本不保留图片和排版；扫描 PDF 没有可提取文字时会提示失败。";
            return "转换后重新读取并校验输出；原文件保持不变。";
        }
        internal static string Availability(string source, string target, ConverterTools tools)
        {
            string ext = Path.GetExtension(source).ToLowerInvariant();
            if (!Targets(source).Contains(target)) return "暂不支持这种格式组合。";
            bool pdf = target == "pdf" || ext == ".pdf";
            if (pdf && (tools.Find("pdfinfo.exe") == null || tools.Find("pdftoppm.exe") == null)) return "PDF 工具尚未配置，请在「转换工具」中选择 Poppler 文件夹。";
            if (ext == ".pdf" && target == "txt" && tools.Find("pdftotext.exe") == null) return "缺少 PDF 文字提取工具 pdftotext。";
            if (ext == ".pdf" && target == "docx" && tools.Find("pdftohtml.exe") == null) return "缺少 PDF 转 Word 工具 pdftohtml。";
            string office = In(ext, Documents) || (ext == ".pdf" && target == "docx") ? "Word.Application" : In(ext, Sheets) ? "Excel.Application" : In(ext, Slides) ? "PowerPoint.Application" : null;
            if (office != null && Type.GetTypeFromProgID(office) == null) return "此项需要本机安装并初始化 " + office.Split('.')[0] + "。";
            if ((In(ext, Videos) || In(ext, Audio) || ext == ".webp" || target == "webp" || (ext == ".gif" && target == "mp4")) && tools.Find("ffmpeg.exe") == null) return "音视频 / WebP 工具尚未配置，请选择 FFmpeg。";
            return "";
        }
        internal static string Hash(string file)
        { using (SHA256 hash = SHA256.Create()) using (FileStream stream = File.OpenRead(file)) return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", ""); }
        internal static string Quote(string value)
        {
            StringBuilder result = new StringBuilder("\""); int slashes = 0;
            foreach (char c in value)
            {
                if (c == '\\') { slashes++; continue; }
                if (c == '"') { result.Append('\\', slashes * 2 + 1); result.Append('"'); }
                else { result.Append('\\', slashes); result.Append(c); }
                slashes = 0;
            }
            result.Append('\\', slashes * 2); return result.Append('"').ToString();
        }
        internal static void WriteJson(string path, object value)
        { File.WriteAllText(path, new JavaScriptSerializer().Serialize(value), new UTF8Encoding(false)); }
        internal static T ReadJson<T>(string path)
        { return new JavaScriptSerializer().Deserialize<T>(File.ReadAllText(path, Encoding.UTF8)); }
        internal static ConversionReceipt Convert(ConversionRequest request, ConverterTools tools, CancellationToken cancel, Action<string> status)
        {
            string source = Path.GetFullPath(request.Source), root = Path.GetFullPath(request.OutputDirectory), stage = null, processFile = null;
            string target = request.Format.ToLowerInvariant().TrimStart('.');
            ConversionReceipt result = new ConversionReceipt();
            try
            {
                if (!File.Exists(source)) throw new IOException("找不到原文件。");
                string unavailable = Availability(source, target, tools); if (unavailable.Length > 0) throw new InvalidOperationException(unavailable);
                if (new FileInfo(source).Length == 0 || new FileInfo(source).Length > 16L * 1024 * 1024 * 1024) throw new IOException("文件为空或超过 16 GB，未开始转换。");
                Directory.CreateDirectory(root); stage = Path.Combine(root, ".naiwa-work-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(stage);
                status("复制原文件…"); string sourceHash = Hash(source), input = Path.Combine(stage, "input" + Path.GetExtension(source)); File.Copy(source, input); File.SetAttributes(input, FileAttributes.Normal);
                if (Hash(input) != sourceHash || Hash(source) != sourceHash) throw new IOException("复制期间原文件发生变化，请保存文件后重试。");
                string jobFile = Path.Combine(stage, "job.json");
                ConversionJob job = new ConversionJob { Input = input, Format = target, Work = Path.Combine(stage, "result"), ResultFile = Path.Combine(stage, "result.json"), ProcessFile = Path.Combine(stage, "processes.json"), Tools = tools };
                processFile = job.ProcessFile;
                WriteJson(jobFile, job); status("转换与完整性校验…");
                using (Process worker = Process.Start(new ProcessStartInfo(ApplicationPath(), "--convert-worker " + Quote(jobFile)) { UseShellExecute = false, CreateNoWindow = true }))
                {
                    Stopwatch elapsed = Stopwatch.StartNew(); int seconds = In(Path.GetExtension(source).ToLowerInvariant(), Videos) || In(Path.GetExtension(source).ToLowerInvariant(), Audio) ? 1800 : 240;
                    try
                    {
                        while (!worker.WaitForExit(100)) { cancel.ThrowIfCancellationRequested(); if (elapsed.Elapsed.TotalSeconds > seconds) throw new TimeoutException("转换超时。请先手动打开 Office 完成初始化，或分批转换较大的文件。"); }
                        cancel.ThrowIfCancellationRequested();
                    }
                    catch { try { if (!worker.HasExited) worker.Kill(); } catch { } KillOwnedProcesses(job.ProcessFile); throw; }
                    if (!File.Exists(job.ResultFile)) throw new IOException("转换工具未正常结束，没有交付未验证文件。");
                    result = ReadJson<ConversionReceipt>(job.ResultFile);
                    if (!result.Success || worker.ExitCode != 0) throw new IOException(String.IsNullOrEmpty(result.Error) ? "转换未通过校验。" : result.Error);
                }
                if (Hash(source) != sourceHash) throw new IOException("转换期间原文件发生变化，结果未交付；请保存后重试。");
                cancel.ThrowIfCancellationRequested(); status("保存已校验结果…");
                string baseName = Path.GetFileNameWithoutExtension(source) + "_转换";
                if (result.Files.Count == 1)
                {
                    string produced = SafeOutput(job.Work, result.Files[0]);
                    for (int i = 0; ; i++)
                    {
                        string destination = Path.Combine(root, baseName + (i == 0 ? "" : " (" + (i + 1) + ")") + Path.GetExtension(produced));
                        try { File.Move(produced, destination); result.Output = destination; break; } catch (IOException) { if (!File.Exists(destination) && !Directory.Exists(destination)) throw; }
                    }
                }
                else
                {
                    foreach (string file in result.Files) SafeOutput(job.Work, file);
                    for (int i = 0; ; i++)
                    {
                        string destination = Path.Combine(root, baseName + "_" + target.ToUpperInvariant() + (i == 0 ? "" : " (" + (i + 1) + ")"));
                        try { Directory.Move(job.Work, destination); result.Output = destination; break; } catch (IOException) { if (!Directory.Exists(destination) && !File.Exists(destination)) throw; }
                    }
                }
                result.SourceHash = sourceHash; result.Success = true; return result;
            }
            catch (OperationCanceledException) { result.Success = false; result.Error = "已取消，原文件未改动。"; return result; }
            catch (Exception e) { result.Success = false; result.Error = e.Message; return result; }
            finally
            {
                if (processFile != null) KillOwnedProcesses(processFile);
                if (stage != null && Path.GetDirectoryName(stage).TrimEnd(Path.DirectorySeparatorChar).Equals(root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) && Path.GetFileName(stage).StartsWith(".naiwa-work-", StringComparison.Ordinal))
                    try { Directory.Delete(stage, true); } catch { }
            }
        }
        private static string ApplicationPath() { return Assembly.GetExecutingAssembly().Location; }
        private static string SafeOutput(string root, string name)
        {
            string full = Path.GetFullPath(Path.Combine(root, name)), prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !File.Exists(full) || new FileInfo(full).Length == 0) throw new IOException("输出文件未通过检查。"); return full;
        }
        internal static void RegisterProcess(string path, Process process)
        {
            try
            {
                List<ConversionProcess> all = File.Exists(path) ? ReadJson<List<ConversionProcess>>(path) : new List<ConversionProcess>();
                all.Add(new ConversionProcess { Id = process.Id, Started = process.StartTime.ToUniversalTime().Ticks, Name = process.ProcessName }); WriteJson(path + ".tmp", all); if (File.Exists(path)) File.Replace(path + ".tmp", path, null); else File.Move(path + ".tmp", path);
            }
            catch { }
        }
        private static void KillOwnedProcesses(string path)
        {
            if (!File.Exists(path)) return;
            try { foreach (ConversionProcess record in ReadJson<List<ConversionProcess>>(path)) try { using (Process process = Process.GetProcessById(record.Id)) if (process.StartTime.ToUniversalTime().Ticks == record.Started && process.ProcessName == record.Name) process.Kill(); } catch { } } catch { }
        }
        internal static string RunTool(ConversionJob job, string executable, params string[] args)
        {
            if (String.IsNullOrEmpty(executable)) throw new FileNotFoundException("所需转换工具尚未配置。");
            StringBuilder command = new StringBuilder(); foreach (string arg in args) { if (command.Length > 0) command.Append(' '); command.Append(Quote(arg)); }
            ProcessStartInfo start = new ProcessStartInfo(executable, command.ToString()) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
            start.EnvironmentVariables["LC_ALL"] = "C";
            using (Process process = Process.Start(start))
            {
                RegisterProcess(job.ProcessFile, process);
                Task<string> output = process.StandardOutput.ReadToEndAsync(), error = process.StandardError.ReadToEndAsync();
                process.WaitForExit(); string stdout = output.Result, stderr = error.Result;
                if (process.ExitCode != 0 || Regex.IsMatch(stderr, @"Syntax Error|Error opening|Invalid data|Couldn't", RegexOptions.IgnoreCase)) throw new IOException("转换 / 校验工具未通过：" + (stderr.Length > 700 ? stderr.Substring(0, 700) : stderr));
                return stdout;
            }
        }
        internal static int Worker(string file)
        {
            ConversionJob job = ReadJson<ConversionJob>(file); ConversionReceipt receipt = new ConversionReceipt();
            try
            {
                Directory.CreateDirectory(job.Work);
                string ext = Path.GetExtension(job.Input).ToLowerInvariant(), target = job.Format;
                receipt.Note = Note(job.Input, target);
                if (!Targets(job.Input).Contains(target)) throw new IOException("不支持此格式组合。");
                if (ext == ".docx" || ext == ".xlsx" || ext == ".pptx" || ext == ".odt" || ext == ".ods" || ext == ".odp" || ext == ".pdf") Validate(job.Input, job);
                if (ext == ".doc" || ext == ".xls" || ext == ".ppt")
                {
                    byte[] signature = new byte[8]; using (FileStream stream = File.OpenRead(job.Input)) if (stream.Read(signature, 0, 8) != 8) throw new IOException("原文件结构不完整。");
                    if (BitConverter.ToString(signature) != "D0-CF-11-E0-A1-B1-1A-E1") throw new IOException("原文件不是有效的 Office 旧版文档。");
                }
                if (ext == ".pdf" && (target == "png" || target == "jpg" || target == "txt")) PdfInput(job);
                else if (ext == ".pdf" && target == "docx") PdfWord.Convert(job);
                else if (In(ext, Documents) || In(ext, Sheets) || In(ext, Slides)) OfficeConversion.Perform(job);
                else if (In(ext, Pictures) && target != "mp4") ImageInput(job);
                else MediaInput(job);
                foreach (string output in Directory.GetFiles(job.Work)) { Validate(output, job); receipt.Files.Add(Path.GetFileName(output)); }
                if (receipt.Files.Count == 0) throw new IOException("没有生成可用文件。");
                receipt.Success = true;
            }
            catch (Exception e) { receipt.Success = false; receipt.Error = e.GetBaseException().Message; }
            WriteJson(job.ResultFile, receipt); return receipt.Success ? 0 : 1;
        }
        private static void PdfInput(ConversionJob job)
        {
            if (job.Format == "txt")
            {
                string output = Path.Combine(job.Work, "result.txt"); RunTool(job, job.Tools.Find("pdftotext.exe"), "-layout", "-enc", "UTF-8", job.Input, output);
                if (String.IsNullOrWhiteSpace(File.ReadAllText(output))) throw new IOException("PDF 中未找到可提取文字；扫描件需要 OCR，原文件未改动。");
            }
            else RunTool(job, job.Tools.Find("pdftoppm.exe"), job.Format == "png" ? "-png" : "-jpeg", "-r", "144", job.Input, Path.Combine(job.Work, "page"));
        }
        private static void ImageInput(ConversionJob job)
        {
            string source = job.Input;
            if (Path.GetExtension(source).Equals(".webp", StringComparison.OrdinalIgnoreCase))
            {
                byte[] header = new byte[21]; using (FileStream stream = File.OpenRead(source))
                    if (stream.Read(header, 0, header.Length) == header.Length && Encoding.ASCII.GetString(header, 0, 4) == "RIFF" && Encoding.ASCII.GetString(header, 8, 4) == "WEBP" && Encoding.ASCII.GetString(header, 12, 4) == "VP8X" && (header[20] & 2) != 0)
                        throw new IOException("暂不支持动画 WebP，请先导出为 GIF / MP4 或静态 WebP。");
                source = Path.Combine(Path.GetDirectoryName(job.Work), "decoded.png"); RunTool(job, job.Tools.Find("ffmpeg.exe"), "-v", "error", "-nostdin", "-i", job.Input, "-frames:v", "1", source);
            }
            using (Image image = Image.FromFile(source))
            {
                if ((long)image.Width * image.Height > 50000000) throw new IOException("图片像素过大，请先缩小后转换。");
                bool tiff = Path.GetExtension(source).Equals(".tif", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(source).Equals(".tiff", StringComparison.OrdinalIgnoreCase);
                int frames = tiff ? image.GetFrameCount(FrameDimension.Page) : 1;
                for (int index = 0; index < frames; index++)
                {
                    if (frames > 1) image.SelectActiveFrame(FrameDimension.Page, index);
                    using (Bitmap frame = Oriented(image))
                    {
                    string output = Path.Combine(job.Work, (frames > 1 ? "page-" + (index + 1).ToString("000") : "result") + "." + job.Format);
                    if (job.Format == "webp") { string decoded = Path.Combine(Path.GetDirectoryName(job.Work), "image-" + index + ".png"); frame.Save(decoded, ImageFormat.Png); RunTool(job, job.Tools.Find("ffmpeg.exe"), "-v", "error", "-nostdin", "-i", decoded, "-frames:v", "1", "-lossless", "1", output); }
                    else if (job.Format == "pdf") RasterPdf(frame, output);
                    else
                    {
                        ImageFormat format = job.Format == "jpg" ? ImageFormat.Jpeg : job.Format == "bmp" ? ImageFormat.Bmp : job.Format == "gif" ? ImageFormat.Gif : job.Format == "tiff" ? ImageFormat.Tiff : ImageFormat.Png;
                        if (job.Format == "jpg") using (Bitmap flat = Flatten(frame)) flat.Save(output, format); else frame.Save(output, format);
                    }
                    }
                }
            }
        }
        private static Bitmap Oriented(Image image)
        {
            int orientation = 1;
            if (Array.IndexOf(image.PropertyIdList, 0x112) >= 0)
            { byte[] value = image.GetPropertyItem(0x112).Value; if (value != null && value.Length >= 2) orientation = BitConverter.ToUInt16(value, 0); }
            Bitmap frame = new Bitmap(image);
            RotateFlipType[] transforms = { RotateFlipType.RotateNoneFlipNone, RotateFlipType.RotateNoneFlipNone, RotateFlipType.RotateNoneFlipX, RotateFlipType.Rotate180FlipNone, RotateFlipType.Rotate180FlipX, RotateFlipType.Rotate90FlipX, RotateFlipType.Rotate90FlipNone, RotateFlipType.Rotate270FlipX, RotateFlipType.Rotate270FlipNone };
            if (orientation >= 2 && orientation <= 8) frame.RotateFlip(transforms[orientation]);
            return frame;
        }
        private static Bitmap Flatten(Image image)
        { Bitmap flat = new Bitmap(image.Width, image.Height, PixelFormat.Format24bppRgb); using (Graphics g = Graphics.FromImage(flat)) { g.Clear(Color.White); g.DrawImageUnscaled(image, 0, 0); } return flat; }
        private static void RasterPdf(Image image, string output)
        {
            byte[] jpeg; using (Bitmap flat = Flatten(image)) using (MemoryStream buffer = new MemoryStream()) { flat.Save(buffer, ImageFormat.Jpeg); jpeg = buffer.ToArray(); }
            double ratio = Math.Min(1.0, 14400.0 / Math.Max(image.Width, image.Height));
            string width = (image.Width * ratio).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture), height = (image.Height * ratio).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            using (FileStream stream = File.Create(output))
            {
                List<long> positions = new List<long> { 0 }; Action<string> write = text => { byte[] bytes = Encoding.ASCII.GetBytes(text); stream.Write(bytes, 0, bytes.Length); };
                Action<int, string> obj = (id, text) => { positions.Add(stream.Position); write(id + " 0 obj\n" + text + "\nendobj\n"); };
                write("%PDF-1.4\n"); obj(1, "<< /Type /Catalog /Pages 2 0 R >>"); obj(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
                obj(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 " + width + " " + height + "] /Resources << /XObject << /Im0 4 0 R >> >> /Contents 5 0 R >>");
                positions.Add(stream.Position); write("4 0 obj\n<< /Type /XObject /Subtype /Image /Width " + image.Width + " /Height " + image.Height + " /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length " + jpeg.Length + " >>\nstream\n"); stream.Write(jpeg, 0, jpeg.Length); write("\nendstream\nendobj\n");
                string content = "q\n" + width + " 0 0 " + height + " 0 0 cm\n/Im0 Do\nQ\n"; obj(5, "<< /Length " + Encoding.ASCII.GetByteCount(content) + " >>\nstream\n" + content + "endstream");
                long xref = stream.Position; write("xref\n0 6\n0000000000 65535 f \n"); for (int i = 1; i < positions.Count; i++) write(positions[i].ToString("0000000000") + " 00000 n \n");
                write("trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n" + xref + "\n%%EOF\n");
            }
        }
        private static void MediaInput(ConversionJob job)
        {
            string output = Path.Combine(job.Work, "result." + job.Format);
            List<string> args = new List<string> { "-v", "error", "-xerror", "-nostdin", "-i", job.Input };
            string target = job.Format;
            if (target == "gif") args.AddRange(new string[] { "-filter_complex", "fps=12,scale=min(720\\,iw):-1:flags=lanczos,split[a][b];[a]palettegen[p];[b][p]paletteuse", "-an" });
            else if (In("." + target, Audio))
            {
                string codec = target == "mp3" ? "libmp3lame" : target == "wav" ? "pcm_s16le" : target == "flac" ? "flac" : target == "ogg" ? "libvorbis" : "aac";
                args.AddRange(new string[] { "-vn", "-c:a", codec }); if (target != "wav" && target != "flac") args.AddRange(new string[] { "-b:a", "192k" });
            }
            else
            {
                args.AddRange(new string[] { "-map", "0:v:0", "-map", "0:a?", "-sn", "-dn", "-vf", "pad=ceil(iw/2)*2:ceil(ih/2)*2", "-c:v", target == "webm" ? "libvpx-vp9" : target == "avi" ? "mpeg4" : "libx264", "-pix_fmt", "yuv420p", "-c:a", target == "webm" ? "libopus" : target == "avi" ? "libmp3lame" : "aac" });
                if (target == "mp4" || target == "mov" || target == "mkv") args.AddRange(new string[] { "-preset", "medium", "-crf", "20" });
                if (target == "webm") args.AddRange(new string[] { "-b:v", "0", "-crf", "31" });
                if (target == "avi") args.AddRange(new string[] { "-q:v", "3" });
                if (target == "mp4" || target == "mov") args.AddRange(new string[] { "-movflags", "+faststart" });
            }
            args.AddRange(new string[] { "-threads", "2", output }); RunTool(job, job.Tools.Find("ffmpeg.exe"), args.ToArray());
        }
        internal static void Validate(string output, ConversionJob job)
        {
            if (!File.Exists(output) || new FileInfo(output).Length == 0) throw new IOException("输出文件为空。");
            string ext = Path.GetExtension(output).ToLowerInvariant();
            if (ext == ".docx" || ext == ".xlsx" || ext == ".pptx" || ext == ".odt" || ext == ".ods" || ext == ".odp")
            {
                using (ZipArchive zip = ZipFile.OpenRead(output))
                {
                    string required = ext == ".docx" ? "word/document.xml" : ext == ".xlsx" ? "xl/workbook.xml" : ext == ".pptx" ? "ppt/presentation.xml" : "content.xml";
                    if (zip.GetEntry(required) == null) throw new IOException("文档结构不完整。");
                    if (ext == ".docx") foreach (ZipArchiveEntry entry in zip.Entries)
                        if (entry.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase))
                            using (Stream stream = entry.Open()) using (XmlReader xml = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
                                while (xml.Read()) if (xml.NodeType == XmlNodeType.Element && xml.LocalName == "Relationship" && (xml.GetAttribute("Type") ?? "").EndsWith("/image") && xml.GetAttribute("TargetMode") == "External")
                                    throw new IOException("文档含有未嵌入的图片链接，未交付可能丢图的结果。");
                    foreach (ZipArchiveEntry entry in zip.Entries)
                        using (Stream stream = entry.Open())
                            if (entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) using (XmlReader xml = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null })) while (xml.Read()) { }
                            else stream.CopyTo(Stream.Null);
                }
            }
            else if (ext == ".pdf")
            {
                string info = RunTool(job, job.Tools.Find("pdfinfo.exe"), output); Match pages = Regex.Match(info, @"(?m)^Pages:\s+(\d+)");
                if (!pages.Success || Int32.Parse(pages.Groups[1].Value) <= 0 || Int32.Parse(pages.Groups[1].Value) > 2000) throw new IOException("PDF 页数异常或超过 2000 页，未交付结果。");
                string verify = Path.Combine(Path.GetDirectoryName(job.Work), "verify-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(verify);
                RunTool(job, job.Tools.Find("pdftoppm.exe"), "-png", "-r", "12", output, Path.Combine(verify, "page"));
                if (Directory.GetFiles(verify, "*.png").Length != Int32.Parse(pages.Groups[1].Value)) throw new IOException("PDF 页面未全部通过解析。");
            }
            else if (ext == ".webp" || In(ext, Videos) || In(ext, Audio)) RunTool(job, job.Tools.Find("ffmpeg.exe"), "-v", "error", "-xerror", "-nostdin", "-err_detect", "explode", "-i", output, "-f", "null", "-");
            else if (In(ext, Pictures)) using (Image image = Image.FromFile(output))
            {
                if (image.Width < 1 || image.Height < 1) throw new IOException("图片无法重新解码。");
                FrameDimension dimension = ext == ".gif" ? FrameDimension.Time : ext == ".tif" || ext == ".tiff" ? FrameDimension.Page : null;
                int count = dimension == null ? 1 : image.GetFrameCount(dimension);
                for (int index = 0; index < count; index++) { if (dimension != null) image.SelectActiveFrame(dimension, index); using (Bitmap copy = new Bitmap(image)) { } }
            }
            else if (ext == ".txt" || ext == ".csv") { using (StreamReader reader = new StreamReader(output, new UTF8Encoding(false, true), true)) reader.ReadToEnd(); }
        }
    }
}
