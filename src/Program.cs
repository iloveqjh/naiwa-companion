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
                    File.WriteAllText(output, "Found=" + (found != IntPtr.Zero) + "\nVisible=" + Desktop.IsWindowVisible(found) + "\nBounds=" + box.L + "," + box.T + "," + box.R + "," + box.B, Encoding.UTF8); return;
                }
                if (Array.IndexOf(args, "--quit") >= 0) { Desktop.PostMessage(Desktop.FindWindow(null, Desktop.WindowName), 0x10, IntPtr.Zero, IntPtr.Zero); return; }
                Preferences prefs = Preferences.Load(); string folder = Argument(args, "--assets");
                if (folder == null) folder = !String.IsNullOrEmpty(prefs.ThemeFolder) && File.Exists(Path.Combine(prefs.ThemeFolder, "theme.nct")) ? prefs.ThemeFolder : File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "素材.nwp")) ? AppDomain.CurrentDomain.BaseDirectory : prefs.Assets;
                output = Argument(args, "--self-test");
                if (output != null) { Environment.ExitCode = SelfTests.Run(folder, output); return; }
                output = Argument(args, "--render");
                if (output != null) { SelfTests.Render(folder, output); return; }
                bool first;
                using (Mutex single = new Mutex(true, "Local\\NaiwaCompanion.1", out first))
                {
                    if (!first) { Desktop.PostMessage(Desktop.FindWindow(null, Desktop.WindowName), Desktop.WakeMessage, IntPtr.Zero, IntPtr.Zero); return; }
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
                        Application.Run(pet);
                    }
                }
            }
            catch (Exception e) { Report(e); Environment.ExitCode = 1; }
        }
        private static void Report(Exception e)
        { Preferences.Log(e); MessageBox.Show("奶蛙陪伴遇到问题：\n" + e.Message + "\n\n错误记录：" + Preferences.DirectoryPath, "奶蛙陪伴"); }
    }
}
