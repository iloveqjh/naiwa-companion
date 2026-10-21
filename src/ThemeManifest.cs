// SPDX-License-Identifier: MIT
using System;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Web.Script.Serialization;

namespace NaiwaCompanion
{
    public sealed class EyeArea
    {
        public float x, y, width, height;
        internal RectangleF Rectangle { get { return new RectangleF(x, y, width, height); } }
    }
    public sealed class RecordedAction
    {
        public string id, name, prefix, endPose;
        public int frames;
        public double fps;
    }
    public sealed class ThemeManifest
    {
        public int schemaVersion, width, height;
        public string id, name, idle;
        public EyeArea[] eyes;
        public EyeArea belly;
        public RecordedAction[] actions;
        internal static ThemeManifest Load(ZipArchive archive)
        {
            ZipArchiveEntry entry = archive.GetEntry("manifest.json");
            if (entry == null || entry.Length > 32768) throw new InvalidDataException("主题说明缺失或过大。");
            ThemeManifest result;
            using (StreamReader reader = new StreamReader(entry.Open())) result = new JavaScriptSerializer().Deserialize<ThemeManifest>(reader.ReadToEnd());
            if (result == null || result.schemaVersion != 1 || result.width < 32 || result.width > 1024 || result.height < 32 || result.height > 1024 || String.IsNullOrWhiteSpace(result.name) || result.name.Length > 30 || result.idle != "idle.png" || result.actions == null || result.actions.Length < 1 || result.actions.Length > 32)
                throw new InvalidDataException("主题说明不在支持范围内。");
            System.Collections.Generic.HashSet<string> ids = new System.Collections.Generic.HashSet<string>();
            int total = 0;
            foreach (RecordedAction a in result.actions)
            {
                if (a == null || String.IsNullOrEmpty(a.id) || !System.Text.RegularExpressions.Regex.IsMatch(a.id, "^[a-z0-9-]{1,40}$") || !ids.Add(a.id) || String.IsNullOrWhiteSpace(a.name) || a.name.Length > 30 || a.prefix != "actions/" + a.id + "/" || a.frames < 1 || a.frames > 2000 || Double.IsNaN(a.fps) || Double.IsInfinity(a.fps) || a.fps < 1 || a.fps > 120 || (a.endPose != "idle" && a.endPose != "hold"))
                    throw new InvalidDataException("动作参数不正确。");
                total += a.frames;
                if (total > 5000) throw new InvalidDataException("主题总帧数过多。");
                for (int i = 0; i < a.frames; i++)
                {
                    ZipArchiveEntry frame = archive.GetEntry(a.prefix + i.ToString("0000") + ".png");
                    if (frame == null || frame.Length > 8 * 1024 * 1024) throw new InvalidDataException("主题动作缺帧：" + a.name);
                }
            }
            if (result.eyes != null)
            {
                if (result.eyes.Length > 4) throw new InvalidDataException("眼部区域过多。");
                foreach (EyeArea eye in result.eyes) ValidateArea(eye, result.width, result.height);
            }
            if (result.belly != null) ValidateArea(result.belly, result.width, result.height);
            return result;
        }
        private static void ValidateArea(EyeArea a, int w, int h)
        {
            if (a == null || Single.IsNaN(a.x + a.y + a.width + a.height) || Single.IsInfinity(a.x + a.y + a.width + a.height) || a.x < 0 || a.y < 0 || a.width <= 1 || a.height <= 1 || a.x + a.width > w || a.y + a.height > h)
                throw new InvalidDataException("主题局部区域超出画布。");
        }
        internal static string DisplayName(string folder)
        {
            using (ZipArchive zip = ZipFile.OpenRead(Path.Combine(folder, "theme.nct")))
            {
                ZipArchiveEntry entry = zip.GetEntry("manifest.json");
                if (entry == null || entry.Length > 32768) return "未命名主题";
                using (StreamReader r = new StreamReader(entry.Open()))
                { ThemeManifest m = new JavaScriptSerializer().Deserialize<ThemeManifest>(r.ReadToEnd()); return m != null && m.name != null && m.name.Length <= 30 ? m.name : "未命名主题"; }
            }
        }
    }
}
