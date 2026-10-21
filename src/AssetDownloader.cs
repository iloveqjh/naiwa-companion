// SPDX-License-Identifier: MIT
using System;
using System.Drawing;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace NaiwaCompanion
{
    internal static class AssetDownloader
    {
        private static readonly string[] names = { "素材.nwp", "原版笑声.wav" };
        private static readonly string[] hashes = { "A9C7795974D860DA0C82B45237456698640EE8D4252B5DB37503383D23FA46AE", "E808F6F932ABFB81E7104EB1C586FE6C828BDA5DB4EF62E52E0638A64A01FDFA" };
        private const string prefix = "https://raw.githubusercontent.com/yugh6/Naiwa/553afb87508d3b05cda3af935616ed00e94dd3e4/";
        private static string Hash(string path)
        { using (SHA256 sha = SHA256.Create()) using (FileStream file = File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(file)).Replace("-", ""); }
        internal static async Task Download(string directory, Action<string, int> progress, CancellationToken token)
        {
            Directory.CreateDirectory(directory); ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            for (int i = 0; i < names.Length; i++)
            {
                token.ThrowIfCancellationRequested(); string file = Path.Combine(directory, names[i]);
                if (File.Exists(file) && Hash(file) == hashes[i]) { progress(names[i] + " 已校验", 100); continue; }
                string temp = file + "." + Guid.NewGuid().ToString("N") + ".download";
                string name = names[i];
                using (WebClient client = new WebClient())
                using (token.Register(client.CancelAsync))
                {
                    client.DownloadProgressChanged += delegate(object sender, DownloadProgressChangedEventArgs e) { progress(name, e.ProgressPercentage); };
                    await client.DownloadFileTaskAsync(new Uri(prefix + Uri.EscapeDataString(name)), temp).ConfigureAwait(false);
                }
                token.ThrowIfCancellationRequested();
                if (Hash(temp) != hashes[i]) throw new InvalidDataException("素材校验失败，未替换已有文件：" + name);
                if (File.Exists(file)) File.Replace(temp, file, null); else File.Move(temp, file);
                progress(name + " 下载并校验完成", 100);
            }
        }
    }

    internal sealed class AssetSetupForm : Form
    {
        internal string SelectedFolder;
        private readonly ProgressBar progress = new ProgressBar();
        private readonly Label status = new Label();
        private readonly Button start = new Button();
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        internal AssetSetupForm()
        {
            Text = "准备原版奶蛙素材"; ClientSize = new Size(500, 245); Font = new Font("Microsoft YaHei UI", 10);
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false; StartPosition = FormStartPosition.CenterScreen;
            Label intro = new Label { Text = "准备原版立体奶蛙素材，保留皮肤和动作质感。\n可下载约 26 MB 素材，或选择电脑里已有的素材文件夹。\n下载来源：原作者 yugh6/Naiwa。", AutoSize = false, Bounds = new Rectangle(22, 20, 456, 80) };
            status.Text = "准备好后点击下载"; status.SetBounds(22, 112, 456, 28); progress.SetBounds(22, 145, 456, 20);
            start.Text = "下载并使用"; start.SetBounds(345, 190, 133, 34);
            Button choose = new Button { Text = "选择已有素材" }; choose.SetBounds(22, 190, 155, 34);
            choose.Click += delegate
            {
                using (FolderBrowserDialog folder = new FolderBrowserDialog())
                {
                    folder.Description = "选择包含 素材.nwp 或 theme.nct 的文件夹";
                    if (folder.ShowDialog() != DialogResult.OK) return;
                    if (!File.Exists(Path.Combine(folder.SelectedPath, "素材.nwp")) && !File.Exists(Path.Combine(folder.SelectedPath, "theme.nct")))
                    { status.Text = "这个文件夹里还没有支持的素材包。"; return; }
                    cancellation.Cancel(); SelectedFolder = folder.SelectedPath; DialogResult = DialogResult.OK; Close();
                }
            };
            start.Click += BeginDownload; Controls.AddRange(new Control[] { intro, status, progress, start, choose });
            FormClosing += delegate { cancellation.Cancel(); };
        }
        private async void BeginDownload(object sender, EventArgs e)
        {
            start.Enabled = false;
            try
            {
                string target = Path.Combine(Preferences.DirectoryPath, "assets");
                await AssetDownloader.Download(target, delegate(string text, int value)
                {
                    if (!IsDisposed && IsHandleCreated) BeginInvoke((Action)delegate { if (!IsDisposed) { status.Text = text; progress.Value = Math.Max(0, Math.Min(100, value)); } });
                }, cancellation.Token);
                if (IsDisposed) return; SelectedFolder = target; DialogResult = DialogResult.OK; Close();
            }
            catch (OperationCanceledException) { if (!IsDisposed) Close(); }
            catch (Exception error) { if (!IsDisposed) { status.Text = "下载未完成：" + error.Message; start.Enabled = true; } }
        }
    }
}
