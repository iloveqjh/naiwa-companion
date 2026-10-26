// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace NaiwaCompanion
{
    internal sealed class StoragePaths
    {
        public string DataDirectory = "", PocketDirectory = "", ConversionDirectory = "";
        private static readonly StoragePaths current = Load();
        internal static StoragePaths Current { get { return current; } }
        private static StoragePaths Load()
        {
            string fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NaiwaCompanion");
            StoragePaths paths = new StoragePaths();
            string config = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "storage.local.json");
            // An invalid explicit location must not silently redirect a user's files to C:.
            if (File.Exists(config))
            {
                paths = new JavaScriptSerializer().Deserialize<StoragePaths>(File.ReadAllText(config));
                if (paths == null) throw new InvalidDataException("存储位置配置为空，请检查 storage.local.json。");
            }
            paths.DataDirectory = Resolve(paths.DataDirectory, fallback);
            paths.PocketDirectory = Resolve(paths.PocketDirectory, Path.Combine(paths.DataDirectory, "文件口袋"));
            paths.ConversionDirectory = Resolve(paths.ConversionDirectory, Path.Combine(paths.DataDirectory, "转换结果"));
            return paths;
        }
        private static string Resolve(string configured, string fallback)
        { return Path.GetFullPath(String.IsNullOrWhiteSpace(configured) ? fallback : Path.IsPathRooted(configured) ? configured : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, configured)); }
    }

    internal sealed class PocketReceipt
    {
        internal readonly List<string> Saved = new List<string>(), Errors = new List<string>();
        internal int AlreadyHere;
        internal bool Cancelled;
        internal string Message { get { return "已暂存 " + Saved.Count + " 项" + (AlreadyHere > 0 ? "，" + AlreadyHere + " 项已在口袋里" : "") + (Errors.Count > 0 ? "，" + Errors.Count + " 项未保存" : "") + (Cancelled ? "；已取消剩余项目" : "") + "。原文件保留。"; } }
    }
    internal sealed class PocketStore
    {
        internal const string PendingPrefix = ".naiwa-pending-";
        internal readonly string Root;
        internal PocketStore(string root) { Root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar; }
        internal static bool Inside(string path, string directory)
        { string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar), root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar); return full.Equals(root, StringComparison.OrdinalIgnoreCase) || full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase); }
        internal FileSystemInfo[] Items()
        {
            if (!Directory.Exists(Root)) return new FileSystemInfo[0];
            List<FileSystemInfo> items = new List<FileSystemInfo>();
            foreach (FileSystemInfo item in new DirectoryInfo(Root).GetFileSystemInfos()) if (!item.Name.StartsWith(PendingPrefix, StringComparison.OrdinalIgnoreCase)) items.Add(item);
            items.Sort(delegate(FileSystemInfo a, FileSystemInfo b) { return b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc); }); return items.ToArray();
        }
        internal PocketReceipt Stash(string[] sources, CancellationToken cancellation, Action<string> progress)
        {
            PocketReceipt receipt = new PocketReceipt();
            foreach (string selected in sources)
            {
                string stage = null;
                try
                {
                    cancellation.ThrowIfCancellationRequested();
                    string source = Path.GetFullPath(selected);
                    if (source.Equals(Path.GetPathRoot(source), StringComparison.OrdinalIgnoreCase)) throw new IOException("请拖入普通文件或文件夹，暂不接收整个磁盘。");
                    source = source.TrimEnd(Path.DirectorySeparatorChar);
                    if (!File.Exists(source) && !Directory.Exists(source)) throw new FileNotFoundException("文件不存在，可能已被移动。");
                    if (Inside(source, Root)) { receipt.AlreadyHere++; continue; }
                    if (Directory.Exists(source) && Inside(Root, source)) throw new IOException("不能把口袋所在的上级目录再次放入口袋。");
                    FileAttributes attributes = File.GetAttributes(source);
                    if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("暂不复制链接或联接目录，请选择实际文件。");
                    bool directory = (attributes & FileAttributes.Directory) != 0;
                    List<string> files = new List<string>(), directories = new List<string>();
                    if (directory) Plan(source, files, directories, cancellation); else files.Add(source);
                    Directory.CreateDirectory(Root);
                    stage = Path.Combine(Root, PendingPrefix + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(stage);
                    string name = Path.GetFileName(source), payload = Path.Combine(stage, name);
                    if (name.StartsWith(PendingPrefix, StringComparison.OrdinalIgnoreCase)) throw new IOException("此名称保留用于暂存过程，请先重命名。");
                    if (directory)
                    {
                        Directory.CreateDirectory(payload);
                        foreach (string child in directories) Directory.CreateDirectory(Path.Combine(payload, child.Substring(source.Length + 1)));
                    }
                    foreach (string file in files)
                    {
                        cancellation.ThrowIfCancellationRequested(); progress("正在复制并校验：" + Path.GetFileName(file));
                        CopyVerified(file, directory ? Path.Combine(payload, file.Substring(source.Length + 1)) : payload, cancellation);
                    }
                    if (directory)
                    {
                        List<string> currentFiles = new List<string>(), currentDirectories = new List<string>(); Plan(source, currentFiles, currentDirectories, cancellation);
                        if (currentFiles.Count != files.Count || currentDirectories.Count != directories.Count || !new HashSet<string>(files, StringComparer.OrdinalIgnoreCase).SetEquals(currentFiles) || !new HashSet<string>(directories, StringComparer.OrdinalIgnoreCase).SetEquals(currentDirectories)) throw new IOException("复制过程中目录内容发生变化，请完成整理后重新拖入。");
                    }
                    cancellation.ThrowIfCancellationRequested();
                    // The final move publishes a complete item and never overwrites a name.
                    string destination = Unique(name, directory);
                    if (directory) Directory.Move(payload, destination); else File.Move(payload, destination);
                    receipt.Saved.Add(destination);
                }
                catch (OperationCanceledException) { receipt.Cancelled = true; break; }
                catch (Exception e) { receipt.Errors.Add(Path.GetFileName(selected) + "：" + e.Message); }
                finally { if (stage != null) try { Directory.Delete(stage, true); } catch { } }
            }
            return receipt;
        }
        private static void Plan(string root, List<string> files, List<string> directories, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) throw new IOException("目录中有链接，未复制此项目。");
            foreach (FileSystemInfo child in new DirectoryInfo(root).GetFileSystemInfos())
            {
                cancellation.ThrowIfCancellationRequested();
                if ((child.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("目录中有链接「" + child.Name + "」，未复制此项目。");
                if ((child.Attributes & FileAttributes.Directory) != 0) { directories.Add(child.FullName); Plan(child.FullName, files, directories, cancellation); }
                else files.Add(child.FullName);
            }
        }
        private static void CopyVerified(string source, string destination, CancellationToken cancellation)
        {
            if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0) throw new IOException("文件已变成链接，请重新选择。");
            using (FileStream input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (SHA256 original = SHA256.Create())
            {
                using (FileStream output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    byte[] buffer = new byte[1024 * 1024]; int count;
                    while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
                    { cancellation.ThrowIfCancellationRequested(); output.Write(buffer, 0, count); original.TransformBlock(buffer, 0, count, buffer, 0); }
                    original.TransformFinalBlock(new byte[0], 0, 0); output.Flush(true);
                }
                cancellation.ThrowIfCancellationRequested();
                using (SHA256 copied = SHA256.Create()) using (FileStream output = File.OpenRead(destination))
                {
                    byte[] buffer = new byte[1024 * 1024]; int count;
                    while ((count = output.Read(buffer, 0, buffer.Length)) > 0) { cancellation.ThrowIfCancellationRequested(); copied.TransformBlock(buffer, 0, count, buffer, 0); }
                    copied.TransformFinalBlock(new byte[0], 0, 0);
                    if (input.Length != output.Length || !Convert.ToBase64String(original.Hash).Equals(Convert.ToBase64String(copied.Hash))) throw new IOException("复制校验未通过，请重新拖入。");
                }
            }
            File.SetLastWriteTimeUtc(destination, File.GetLastWriteTimeUtc(source));
        }
        private string Unique(string name, bool directory)
        {
            string stem = directory ? name : Path.GetFileNameWithoutExtension(name), extension = directory ? "" : Path.GetExtension(name), candidate = Path.Combine(Root, name);
            for (int n = 2; File.Exists(candidate) || Directory.Exists(candidate); n++) candidate = Path.Combine(Root, stem + " (" + n + ")" + extension);
            return candidate;
        }
        internal void OpenFolder() { Directory.CreateDirectory(Root); Process.Start("explorer.exe", Conversion.Quote(Root.TrimEnd(Path.DirectorySeparatorChar))); }
        internal static void Reveal(string path) { Process.Start("explorer.exe", "/select," + Conversion.Quote(path)); }
    }

    internal sealed class DropChoiceForm : UtilityForm
    {
        internal DropChoiceForm(string[] paths, Action<string[]> stash, Action<string[]> convert, Action browse) : base("奶蛙 · 收到文件啦", 590, 330)
        {
            LabelAt("收到 " + paths.Length + " 项，想怎么处理？", 22, 18, 546, 32).Font = new Font(Font.FontFamily, 14, FontStyle.Bold);
            ListBox list = new ListBox { IntegralHeight = false, HorizontalScrollbar = true, AccessibleName = "拖入的文件" }; list.SetBounds(U(22), U(63), U(546), U(115)); Controls.Add(list);
            List<string> supported = new List<string>();
            foreach (string path in paths) { list.Items.Add(Path.GetFileName(path.TrimEnd('\\'))); if (File.Exists(path) && Conversion.Targets(path).Count > 0) supported.Add(path); }
            LabelAt("暂存会复制到文件口袋，原文件保留。\n" + supported.Count + " 项可进入格式转换，其余项目可暂存。", 22, 191, 546, 53);
            ButtonAt("暂存到文件口袋", 22, 259, 170, delegate { Hide(); stash(paths); });
            Button conversion = ButtonAt("转换格式…", 204, 259, 124, delegate { Hide(); convert(supported.ToArray()); }); conversion.Enabled = supported.Count > 0;
            ButtonAt("查看口袋", 340, 259, 108, delegate { Hide(); browse(); });
            ButtonAt("取消", 460, 259, 108, delegate { Hide(); });
        }
    }

    internal sealed class PocketForm : UtilityForm
    {
        private readonly PocketStore store;
        private readonly ListView files = new ListView();
        private readonly Label status, location;
        private readonly Button add, convert;
        private CancellationTokenSource cancellation;
        private Task<PocketReceipt> copyTask;
        internal event EventHandler Finished;
        internal string CompletionMessage;
        private readonly Action<string[]> conversion;
        internal bool Busy { get { return copyTask != null; } }
        internal PocketForm(PocketStore pocket, Action<string[]> onConvert) : base("奶蛙 · 文件口袋", 704, 504)
        {
            store = pocket; conversion = onConvert;
            LabelAt("文件口袋", 20, 15, 400, 30).Font = new Font(Font.FontFamily, 15, FontStyle.Bold);
            LabelAt("拖入文件或文件夹暂存；双击在资源管理器中定位。", 20, 54, 664, 25);
            location = LabelAt(store.Root, 20, 86, 664, 40); location.AutoEllipsis = true;
            add = ButtonAt("添加文件…", 20, 134, 118, delegate { using (OpenFileDialog dialog = new OpenFileDialog { Multiselect = true, Title = "复制到文件口袋" }) if (dialog.ShowDialog(this) == DialogResult.OK) StashFiles(dialog.FileNames); });
            ButtonAt("打开文件夹", 150, 134, 118, delegate { TryAction(store.OpenFolder); });
            ButtonAt("刷新", 280, 134, 80, delegate { RefreshFiles(); });
            ButtonAt("取消暂存", 574, 134, 110, delegate { if (cancellation != null) cancellation.Cancel(); });
            files.SetBounds(U(20), U(181), U(664), U(214)); files.View = View.Details; files.FullRowSelect = true; files.HideSelection = false; files.ShowItemToolTips = true; files.AccessibleName = "口袋文件列表";
            files.Columns.Add("名称", U(294)); files.Columns.Add("类型", U(88)); files.Columns.Add("大小", U(100)); files.Columns.Add("修改时间", U(155)); Controls.Add(files);
            files.DoubleClick += delegate { RevealSelected(); };
            convert = ButtonAt("转换所选文件…", 20, 410, 156, delegate { ConvertSelected(); });
            ButtonAt("定位所选项目", 188, 410, 144, delegate { RevealSelected(); });
            status = LabelAt("文件保存在本机，不会自动删除。", 20, 459, 664, 35); status.AutoEllipsis = true;
            AllowDrop = files.AllowDrop = true;
            DragEventHandler enter = delegate(object sender, DragEventArgs e) { e.Effect = !Busy && e.Data.GetDataPresent(DataFormats.FileDrop) && (e.AllowedEffect & DragDropEffects.Copy) != 0 ? DragDropEffects.Copy : DragDropEffects.None; };
            DragEventHandler drop = delegate(object sender, DragEventArgs e) { if (!Busy && e.Data.GetDataPresent(DataFormats.FileDrop)) StashFiles((string[])e.Data.GetData(DataFormats.FileDrop)); };
            DragEnter += enter; files.DragEnter += enter; DragDrop += drop; files.DragDrop += drop;
            Activated += delegate { RefreshFiles(); }; RefreshFiles();
        }
        internal void RefreshFiles()
        {
            try
            {
                files.Items.Clear(); foreach (FileSystemInfo item in store.Items())
                {
                    FileInfo file = item as FileInfo;
                    ListViewItem row = new ListViewItem(new string[] { item.Name, file == null ? "文件夹" : item.Extension, file == null ? "—" : SizeLabel(file.Length), item.LastWriteTime.ToString("MM-dd HH:mm") });
                    row.Tag = item.FullName; row.ToolTipText = item.FullName; files.Items.Add(row);
                }
                if (!Busy) status.Text = files.Items.Count == 0 ? "口袋还是空的，把文件拖到奶蛙身上试试。" : "口袋里有 " + files.Items.Count + " 项。文件会一直保留，直到你自行移走。";
            }
            catch (Exception e) { status.Text = "无法读取文件口袋：" + e.Message; }
        }
        private static string SizeLabel(long bytes) { return bytes < 1024 ? bytes + " B" : bytes < 1024 * 1024 ? (bytes / 1024.0).ToString("0.#") + " KB" : (bytes / 1048576.0).ToString("0.#") + " MB"; }
        internal async void StashFiles(string[] paths)
        {
            if (Busy) { status.Text = "正在暂存，请等这一批完成后再拖入。"; return; }
            cancellation = new CancellationTokenSource(); add.Enabled = convert.Enabled = false;
            try
            {
                copyTask = Task.Run(delegate { return store.Stash(paths, cancellation.Token, delegate(string message) { if (!IsDisposed && IsHandleCreated) try { BeginInvoke((MethodInvoker)delegate { if (!IsDisposed) status.Text = message; }); } catch { } }); });
                PocketReceipt receipt = await copyTask; if (IsDisposed) return;
                RefreshFiles(); CompletionMessage = receipt.Message; status.Text = receipt.Message;
                if (receipt.Errors.Count > 0) MessageBox.Show(this, String.Join(Environment.NewLine, receipt.Errors.ToArray()), "部分文件未暂存");
                if (Finished != null) Finished(this, EventArgs.Empty);
            }
            catch (Exception e) { if (!IsDisposed) status.Text = "暂存未完成：" + e.Message; }
            finally { copyTask = null; if (cancellation != null) cancellation.Dispose(); cancellation = null; if (!IsDisposed) add.Enabled = convert.Enabled = true; }
        }
        private void RevealSelected() { if (files.SelectedItems.Count == 0) { status.Text = "先选择一个项目。"; return; } TryAction(delegate { PocketStore.Reveal((string)files.SelectedItems[0].Tag); }); }
        private void ConvertSelected()
        {
            List<string> selected = new List<string>();
            foreach (ListViewItem row in files.SelectedItems) if (File.Exists((string)row.Tag) && Conversion.Targets((string)row.Tag).Count > 0) selected.Add((string)row.Tag);
            if (selected.Count == 0) { status.Text = "请选择支持转换的文件；文件夹请先打开后选择内部文件。"; return; }
            conversion(selected.ToArray());
        }
        private void TryAction(Action action) { try { action(); } catch (Exception e) { status.Text = e.Message; } }
        internal void CancelAndWait() { if (cancellation != null) cancellation.Cancel(); if (copyTask != null) try { copyTask.Wait(5000); } catch { } }
    }
}
