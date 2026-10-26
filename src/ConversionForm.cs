// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace NaiwaCompanion
{
    internal sealed class ConversionEntry { internal string Source, Output; }
    internal sealed class ConversionForm : UtilityForm
    {
        private readonly ListView files = new ListView();
        private readonly ComboBox formats = new ComboBox();
        private readonly TextBox destination = new TextBox();
        private readonly Label note, status;
        private readonly Button start, add, clear, folder, configure;
        private readonly ProgressBar progress = new ProgressBar();
        private readonly LocalData<ConverterTools> tools;
        private CancellationTokenSource cancellation;
        private Task<ConversionReceipt> workTask;
        private bool running;
        internal event EventHandler Finished;
        internal string CompletionMessage;
        internal ConversionForm(bool ephemeral) : base("奶蛙 · 文件转换", 704, 540)
        {
            tools = ConverterTools.Load(ephemeral);
            LabelAt("文件转换", 20, 15, 400, 28);
            LabelAt("拖入文件，选择格式；原文件保留，结果校验后另存。", 20, 46, 660, 26);
            add = ButtonAt("选择文件", 20, 84, 108, delegate
            { using (OpenFileDialog dialog = new OpenFileDialog { Multiselect = true, Title = "选择要转换的文件", Filter = "所有文件|*.*" }) if (dialog.ShowDialog(this) == DialogResult.OK) AddFiles(dialog.FileNames); });
            clear = ButtonAt("清空列表", 140, 84, 108, delegate { files.Items.Clear(); UpdateOptions(); });
            configure = ButtonAt("转换工具", 576, 84, 108, delegate { ConfigureTools(); });
            files.SetBounds(U(20), U(130), U(664), U(183)); files.View = View.Details; files.FullRowSelect = true; files.HideSelection = false; files.ShowItemToolTips = true; files.AccessibleName = "转换文件列表";
            files.Columns.Add("文件", U(245)); files.Columns.Add("状态", U(150)); files.Columns.Add("输出位置", U(240)); Controls.Add(files);
            LabelAt("转成", 20, 329, 70, 26); formats.DropDownStyle = ComboBoxStyle.DropDownList; formats.SetBounds(U(94), U(324), U(350), U(28)); formats.AccessibleName = "目标格式"; Controls.Add(formats); formats.SelectedIndexChanged += delegate { UpdateNote(); };
            LabelAt("保存到", 20, 370, 70, 26); destination.Text = StoragePaths.Current.ConversionDirectory; destination.SetBounds(U(94), U(366), U(470), U(28)); destination.AccessibleName = "转换输出文件夹"; Controls.Add(destination);
            folder = ButtonAt("选择目录", 576, 363, 108, delegate { using (FolderBrowserDialog dialog = new FolderBrowserDialog { Description = "选择转换结果保存位置", SelectedPath = destination.Text }) if (dialog.ShowDialog(this) == DialogResult.OK) destination.Text = dialog.SelectedPath; });
            note = LabelAt("请选择文件。", 20, 408, 664, 42);
            start = ButtonAt("开始转换", 20, 462, 130, async delegate { await StartConversion(); });
            ButtonAt("取消", 162, 462, 90, delegate { if (cancellation != null) cancellation.Cancel(); });
            ButtonAt("查看结果", 564, 462, 120, delegate { RevealResult(); });
            progress.SetBounds(U(270), U(468), U(90), U(18)); progress.Style = ProgressBarStyle.Marquee; progress.Visible = false; Controls.Add(progress);
            status = LabelAt("仅在本机处理。支持的目标格式会随所选文件变化。", 20, 509, 664, 26); status.Font = new System.Drawing.Font(Font.FontFamily, 8f);
            AllowDrop = true; DragEnter += delegate(object sender, DragEventArgs e) { e.Effect = !running && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; };
            DragDrop += delegate(object sender, DragEventArgs e) { if (!running) AddFiles((string[])e.Data.GetData(DataFormats.FileDrop)); };
            UpdateOptions();
        }
        internal void AddFiles(string[] paths)
        {
            foreach (string path in paths)
            {
                if (!File.Exists(path) || Conversion.Targets(path).Count == 0) { status.Text = "已跳过暂不支持的文件或文件夹。"; continue; }
                bool exists = false; foreach (ListViewItem row in files.Items) if (((ConversionEntry)row.Tag).Source.Equals(path, StringComparison.OrdinalIgnoreCase)) exists = true;
                if (exists) continue;
                ListViewItem item = new ListViewItem(new string[] { Path.GetFileName(path), "待转换", "" }); item.Tag = new ConversionEntry { Source = Path.GetFullPath(path) }; item.ToolTipText = path; files.Items.Add(item);
            }
            UpdateOptions();
        }
        private void UpdateOptions()
        {
            string selected = formats.SelectedItem as string; formats.Items.Clear();
            List<string> common = null;
            foreach (ListViewItem row in files.Items)
            {
                List<string> choices = Conversion.Targets(((ConversionEntry)row.Tag).Source);
                if (common == null) common = choices; else common.RemoveAll(delegate(string f) { return !choices.Contains(f); });
            }
            if (common != null) foreach (string choice in common) formats.Items.Add(choice);
            if (formats.Items.Count > 0) formats.SelectedIndex = selected != null && formats.Items.Contains(selected) ? formats.Items.IndexOf(selected) : 0;
            UpdateNote();
        }
        private void UpdateNote()
        {
            if (start == null || note == null) return;
            if (files.Items.Count == 0 || formats.SelectedItem == null) { note.Text = files.Items.Count == 0 ? "请选择或拖入文件。" : "这些文件没有共同目标格式，请按同类文件分批转换。"; start.Enabled = false; return; }
            string source = ((ConversionEntry)files.Items[0].Tag).Source, target = (string)formats.SelectedItem;
            string unavailable = Conversion.Availability(source, target, tools.Value);
            foreach (ListViewItem row in files.Items)
            {
                string path = ((ConversionEntry)row.Tag).Source, reason = Conversion.Availability(path, target, tools.Value);
                if (reason.Length > 0) { unavailable = Path.GetFileName(path) + "：" + reason; break; }
            }
            note.Text = unavailable.Length > 0 ? unavailable : Conversion.Note(source, target);
            start.Enabled = !running && unavailable.Length == 0;
        }
        private async Task StartConversion()
        {
            if (running || formats.SelectedItem == null) return;
            string root;
            try { root = Path.GetFullPath(destination.Text); Directory.CreateDirectory(root); } catch (Exception e) { status.Text = "保存目录不可用：" + e.Message; return; }
            running = true; cancellation = new CancellationTokenSource(); SetBusy(true); int completed = 0, failed = 0; string target = (string)formats.SelectedItem;
            try
            {
                foreach (ListViewItem row in files.Items)
                {
                    if (cancellation.IsCancellationRequested) break;
                    ConversionEntry entry = (ConversionEntry)row.Tag; row.SubItems[1].Text = "转换与校验中";
                    ConversionRequest request = new ConversionRequest { Source = entry.Source, Format = target, OutputDirectory = root };
                    workTask = Task.Run(delegate { return Conversion.Convert(request, tools.Value, cancellation.Token, delegate(string text) { if (!IsDisposed && IsHandleCreated) try { BeginInvoke((MethodInvoker)delegate { if (!IsDisposed) status.Text = text; }); } catch { } }); });
                    ConversionReceipt result = await workTask;
                    if (IsDisposed) return;
                    if (result.Success) { entry.Output = result.Output; row.SubItems[1].Text = "已转换并校验"; row.SubItems[2].Text = result.Output; row.ToolTipText = result.Output + "\n" + result.Note; completed++; }
                    else { row.SubItems[1].Text = cancellation.IsCancellationRequested ? "已取消" : "未通过"; row.SubItems[2].Text = result.Error; row.ToolTipText = result.Error; failed++; }
                }
                CompletionMessage = cancellation.IsCancellationRequested ? "转换已取消，原文件都还在。" : "转换完成 " + completed + " 个" + (failed > 0 ? "，" + failed + " 个未通过；请查看列表。" : "，结果已检查。" );
                status.Text = CompletionMessage; if (Finished != null) Finished(this, EventArgs.Empty);
            }
            finally { running = false; SetBusy(false); cancellation.Dispose(); cancellation = null; workTask = null; }
        }
        private void SetBusy(bool value)
        { add.Enabled = clear.Enabled = folder.Enabled = configure.Enabled = formats.Enabled = destination.Enabled = !value; progress.Visible = value; if (value) start.Enabled = false; else UpdateNote(); }
        private void RevealResult()
        {
            ConversionEntry selected = files.SelectedItems.Count > 0 ? (ConversionEntry)files.SelectedItems[0].Tag : null;
            if (selected == null || String.IsNullOrEmpty(selected.Output)) { status.Text = "先选择一个已完成的项目。"; return; }
            try { if (Directory.Exists(selected.Output)) Process.Start(selected.Output); else Process.Start("explorer.exe", "/select," + Conversion.Quote(selected.Output)); } catch (Exception e) { status.Text = e.Message; }
        }
        internal void CancelAndWait()
        { if (cancellation != null) cancellation.Cancel(); if (workTask != null) try { workTask.Wait(5000); } catch { } }
        private void ConfigureTools()
        {
            using (Form dialog = new Form { Text = "转换工具", Font = Font, BackColor = BackColor, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, ClientSize = new System.Drawing.Size(U(500), U(245)), MaximizeBox = false, MinimizeBox = false })
            {
                Label info = new Label { Text = "办公格式使用本机 Microsoft Office；图片、音视频和 PDF 校验使用下面的工具。", AutoSize = false }; info.SetBounds(U(15), U(15), U(470), U(45)); dialog.Controls.Add(info);
                TextBox ffmpeg = new TextBox { Text = tools.Value.Find("ffmpeg.exe") ?? "", ReadOnly = true }, poppler = new TextBox { Text = String.IsNullOrEmpty(tools.Value.Poppler) ? Path.GetDirectoryName(tools.Value.Find("pdfinfo.exe") ?? "") : tools.Value.Poppler, ReadOnly = true };
                ffmpeg.SetBounds(U(15), U(78), U(350), U(28)); poppler.SetBounds(U(15), U(130), U(350), U(28)); dialog.Controls.Add(ffmpeg); dialog.Controls.Add(poppler);
                Button chooseFfmpeg = new Button { Text = "FFmpeg…" }, choosePoppler = new Button { Text = "PDF工具…" }, save = new Button { Text = "保存" };
                chooseFfmpeg.SetBounds(U(375), U(76), U(110), U(32)); choosePoppler.SetBounds(U(375), U(128), U(110), U(32)); save.SetBounds(U(375), U(190), U(110), U(32)); dialog.Controls.Add(chooseFfmpeg); dialog.Controls.Add(choosePoppler); dialog.Controls.Add(save);
                chooseFfmpeg.Click += delegate { using (OpenFileDialog file = new OpenFileDialog { Filter = "FFmpeg|ffmpeg.exe", Title = "选择 FFmpeg" }) if (file.ShowDialog(dialog) == DialogResult.OK) ffmpeg.Text = file.FileName; };
                choosePoppler.Click += delegate { using (FolderBrowserDialog folderDialog = new FolderBrowserDialog { Description = "选择含 pdfinfo.exe、pdftoppm.exe 和 pdftotext.exe 的文件夹" }) if (folderDialog.ShowDialog(dialog) == DialogResult.OK) poppler.Text = folderDialog.SelectedPath; };
                save.Click += delegate
                {
                    ConverterTools value = new ConverterTools { Ffmpeg = ffmpeg.Text, Poppler = poppler.Text };
                    if (ffmpeg.Text.Length > 0 && !File.Exists(ffmpeg.Text)) { MessageBox.Show(dialog, "FFmpeg 路径不可用。"); return; }
                    if (poppler.Text.Length > 0 && (!File.Exists(Path.Combine(poppler.Text, "pdfinfo.exe")) || !File.Exists(Path.Combine(poppler.Text, "pdftoppm.exe")))) { MessageBox.Show(dialog, "该文件夹缺少 PDF 工具。"); return; }
                    if (tools.Save(value)) { dialog.DialogResult = DialogResult.OK; } else MessageBox.Show(dialog, tools.Error);
                };
                dialog.ShowDialog(this); UpdateNote();
            }
        }
    }
}
