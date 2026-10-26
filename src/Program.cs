// SPDX-License-Identifier: MIT
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace NaiwaCompanion
{
    internal static class Program
    {
        internal static string Argument(string[] args, string flag)
        { for (int i = 0; i + 1 < args.Length; i++) if (args[i] == flag) return Path.GetFullPath(args[i + 1]); return null; }
        internal static string Value(string[] args, string flag)
        { for (int i = 0; i + 1 < args.Length; i++) if (args[i] == flag) return args[i + 1]; return null; }
        [STAThread]
        private static void Main(string[] args)
        {
            Desktop.SetProcessDPIAware(); Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                string conversion = Argument(args, "--convert-worker");
                if (conversion != null) { Environment.ExitCode = Conversion.Worker(conversion); return; }
                conversion = Argument(args, "--convert-request");
                if (conversion != null)
                {
                    ConversionReceipt receipt = Conversion.Convert(Conversion.ReadJson<ConversionRequest>(conversion), ConverterTools.Load(false).Value, CancellationToken.None, delegate { });
                    Conversion.WriteJson(conversion + ".result.json", receipt); Environment.ExitCode = receipt.Success ? 0 : 1; return;
                }
                string download = Argument(args, "--download-assets");
                if (download != null)
                {
                    try { AssetDownloader.Download(download, delegate { }, CancellationToken.None).GetAwaiter().GetResult(); }
                    catch (Exception e) { Directory.CreateDirectory(download); File.WriteAllText(Path.Combine(download, "download-error.txt"), e.Message); Environment.ExitCode = 1; }
                    return;
                }
                string output = Argument(args, "--probe");
                if (output != null)
                {
                    IntPtr found = Desktop.FindWindow(null, Desktop.WindowName); Desktop.Box box; Desktop.GetWindowRect(found, out box);
                    int state = found == IntPtr.Zero ? 0 : Desktop.SendMessage(found, Desktop.StatusMessage, IntPtr.Zero, IntPtr.Zero).ToInt32();
                    File.WriteAllText(output, "Found=" + (found != IntPtr.Zero) + "\nVisible=" + Desktop.IsWindowVisible(found) + "\nBounds=" + box.L + "," + box.T + "," + box.R + "," + box.B + "\nCrawling=" + ((state & 2) != 0) + "\nPlaying=" + ((state & 4) != 0) + "\nHeadHeld=" + ((state & 8) != 0) + "\nHeadActive=" + ((state & 16) != 0) + "\nSoundEnabled=" + ((state & 32) != 0) + "\nReactionSoundReady=" + ((state & 64) != 0) + "\nTodoVisible=" + ((state & 128) != 0), Encoding.UTF8); return;
                }
                if (Array.IndexOf(args, "--quit") >= 0) { Desktop.PostMessage(Desktop.FindWindow(null, Desktop.WindowName), 0x10, IntPtr.Zero, IntPtr.Zero); return; }
                Preferences prefs = Preferences.Load(); string folder = Argument(args, "--assets");
                if (folder == null) folder = !String.IsNullOrEmpty(prefs.ThemeFolder) && File.Exists(Path.Combine(prefs.ThemeFolder, "theme.nct")) ? prefs.ThemeFolder : File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "素材.nwp")) ? AppDomain.CurrentDomain.BaseDirectory : prefs.Assets;
                output = Argument(args, "--self-test");
                if (output != null) { Environment.ExitCode = SelfTests.Run(folder, output); return; }
                output = Argument(args, "--render");
                if (output != null) { SelfTests.Render(folder, output); return; }
                output = Argument(args, "--render-press");
                if (output != null) { SelfTests.RenderHeadPress(folder, output); return; }
                bool first;
                using (Mutex single = new Mutex(true, "Local\\NaiwaCompanion.1", out first))
                {
                    if (!first) { int message = Array.IndexOf(args, "--show-todos") >= 0 ? Desktop.TodoMessage : Array.IndexOf(args, "--show-snippets") >= 0 ? Desktop.SnippetsMessage : Array.IndexOf(args, "--show-converter") >= 0 ? Desktop.ConvertMessage : Array.IndexOf(args, "--show-pocket") >= 0 ? Desktop.PocketMessage : Array.IndexOf(args, "--say") >= 0 ? Desktop.SayMessage : Desktop.WakeMessage; Desktop.PostMessage(Desktop.FindWindow(null, Desktop.WindowName), message, IntPtr.Zero, IntPtr.Zero); return; }
                    Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e) { Report(e.Exception); Application.Exit(); };
                    Theme theme = null;
                    try { theme = new Theme(folder); }
                    catch (Exception e) { Preferences.Log(e); MessageBox.Show("素材暂时无法读取，请重新准备原版素材。\n\n" + e.Message, "奶蛙陪伴"); }
                    if (theme == null || !theme.Original)
                    {
                        if (theme != null) theme.Dispose();
                        using (AssetSetupForm setup = new AssetSetupForm())
                        {
                            if (setup.ShowDialog() != DialogResult.OK) return;
                            folder = setup.SelectedFolder; theme = new Theme(folder);
                            if (!theme.Original) { theme.Dispose(); return; }
                        }
                    }
                    if (!String.IsNullOrEmpty(folder) && theme.Original) { if (theme.Recorded) prefs.ThemeFolder = folder; else { prefs.Assets = folder; prefs.ThemeFolder = ""; } }
                    if (Array.IndexOf(args, "--repeat") >= 0) prefs.RepeatAction = true;
                    using (theme) using (PetForm pet = new PetForm(theme, prefs))
                    {
                        string action = Value(args, "--play");
                        if (action != null) pet.Shown += delegate { pet.PlayAction(action); };
                        if (Array.IndexOf(args, "--press-demo") >= 0) pet.Shown += delegate { pet.DemoHeadPress(); };
                        if (Array.IndexOf(args, "--show-todos") >= 0) pet.Shown += delegate { pet.ShowTodos(true); };
                        if (Array.IndexOf(args, "--show-snippets") >= 0) pet.Shown += delegate { pet.ShowSnippets(false); };
                        if (Array.IndexOf(args, "--show-converter") >= 0) pet.Shown += delegate { pet.ShowConverter(); };
                        if (Array.IndexOf(args, "--show-pocket") >= 0) pet.Shown += delegate { pet.ShowPocket(); };
                        if (Array.IndexOf(args, "--say") >= 0) pet.Shown += delegate { if (pet.Speech != null) pet.Speech.ManualLine(pet.Bounds, prefs.AlwaysOnTop); };
                        Application.Run(pet);
                    }
                }
            }
            catch (Exception e) { Report(e); Environment.ExitCode = 1; }
        }
        private static void Report(Exception e)
        {
            Preferences.Log(e); string location;
            try { location = "错误记录：" + Preferences.DirectoryPath; }
            catch { location = "请检查程序旁的 storage.local.json 存储位置配置。"; }
            MessageBox.Show("奶蛙陪伴遇到问题：\n" + e.Message + "\n\n" + location, "奶蛙陪伴");
        }
    }
}
