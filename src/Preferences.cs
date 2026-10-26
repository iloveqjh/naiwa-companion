// SPDX-License-Identifier: MIT
using System;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace NaiwaCompanion
{
    internal sealed class Preferences
    {
        public int Size = 85, X = int.MinValue, Y = int.MinValue;
        public int CanvasVersion;
        public bool Sound = false, AlwaysOnTop = true, HideFullscreen = true, FollowPointer = true;
        public string Assets = "";
        public string ThemeFolder = "";
        public bool RepeatAction;
        public bool CrawlEnabled = true;
        internal bool Ephemeral;
        internal static string DirectoryPath { get { return StoragePaths.Current.DataDirectory; } }
        internal static Preferences Load()
        {
            try
            {
                Preferences p = new JavaScriptSerializer().Deserialize<Preferences>(File.ReadAllText(Path.Combine(DirectoryPath, "settings.json")));
                p.Size = Math.Max(50, Math.Min(150, p.Size)); return p;
            }
            catch { return new Preferences(); }
        }
        internal void Save()
        {
            if (Ephemeral) return;
            try
            {
                Directory.CreateDirectory(DirectoryPath);
                string file = Path.Combine(DirectoryPath, "settings.json"), temp = file + ".tmp";
                File.WriteAllText(temp, new JavaScriptSerializer().Serialize(this), new UTF8Encoding(false));
                if (File.Exists(file)) File.Replace(temp, file, null); else File.Move(temp, file);
            }
            catch (Exception e) { Log(e); }
        }
        internal static void Log(Exception e)
        { try { Directory.CreateDirectory(DirectoryPath); File.AppendAllText(Path.Combine(DirectoryPath, "error.log"), DateTime.Now.ToString("s") + " " + e.Message + Environment.NewLine); } catch { } }
    }
}
