// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Web.Script.Serialization;

namespace NaiwaCompanion
{
    internal sealed class Theme : IDisposable
    {
        internal int Width = 288, Height = 368, Frames = 150, BlinkFrames = 7;
        internal double Fps = 30, BlinkFps = 30;
        internal string AudioFile, Folder;
        internal bool Original { get { return archive != null; } }
        internal double Duration { get { return Frames / Fps; } }
        private ZipArchive archive;
        private Bitmap idle;
        private readonly Dictionary<string, Bitmap> images = new Dictionary<string, Bitmap>();
        private readonly Queue<string> fifo = new Queue<string>();
        internal int CacheCount { get { return images.Count; } }

        internal Theme(string folder)
        {
            Folder = folder;
            string path = Path.Combine(folder ?? "", "素材.nwp");
            if (String.IsNullOrEmpty(folder) || !File.Exists(path)) { idle = DrawDemo(0, 0, 0, false); return; }
            if (new FileInfo(path).Length > 100 * 1024 * 1024) throw new InvalidDataException("素材包大于 100 MB，无法加载。");
            try
            {
                archive = ZipFile.OpenRead(path);
                if (archive.Entries.Count > 3000) throw new InvalidDataException("素材包条目过多。");
                Dictionary<string, object> meta;
                using (StreamReader r = new StreamReader(Entry("metadata.json", 16384).Open()))
                    meta = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(r.ReadToEnd());
                Width = Convert.ToInt32(meta["width"]); Height = Convert.ToInt32(meta["height"]);
                Frames = Convert.ToInt32(meta["frames"]); Fps = Convert.ToDouble(meta["fps"]);
                BlinkFrames = Convert.ToInt32(meta["blinkFrames"]); BlinkFps = Convert.ToDouble(meta["blinkFps"]);
                if (Width < 32 || Height < 32 || Width > 1024 || Height > 1024 || Frames < 1 || Frames > 2000 || Fps < 1 || Fps > 120 || BlinkFrames < 1 || BlinkFrames > 60 || BlinkFps < 1 || BlinkFps > 120)
                    throw new InvalidDataException("素材包参数不在支持范围内。");
                // Reject missing entries before starting animation, so a bad pack cannot fail half way through.
                Entry("look/00.png", 8 * 1024 * 1024);
                for (int i = 0; i < Frames; i++) Entry("laugh/" + i.ToString("0000") + ".png", 8 * 1024 * 1024);
                for (int i = 0; i < BlinkFrames; i++) Entry("blink/0/" + i.ToString("00") + ".png", 8 * 1024 * 1024);
                idle = Decode("look/00.png");
                AudioFile = Path.Combine(folder, "原版笑声.wav");
                if (!File.Exists(AudioFile) || new FileInfo(AudioFile).Length > 20 * 1024 * 1024) AudioFile = null;
            }
            catch { Dispose(); throw; }
        }
        private ZipArchiveEntry Entry(string name, long max)
        {
            ZipArchiveEntry item = archive.GetEntry(name);
            if (item == null || item.Length > max) throw new InvalidDataException("素材缺失或过大：" + name);
            return item;
        }
        private Bitmap Decode(string name)
        {
            using (Stream s = Entry(name, 8 * 1024 * 1024).Open())
            using (Image image = Image.FromStream(s))
            {
                if (image.Width != Width || image.Height != Height) throw new InvalidDataException("动作图片尺寸不一致：" + name);
                Bitmap result = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb);
                using (Graphics g = Graphics.FromImage(result)) { g.CompositingMode = CompositingMode.SourceCopy; g.DrawImageUnscaled(image, 0, 0); }
                return result;
            }
        }
        private Bitmap Cached(string name)
        {
            Bitmap result;
            if (images.TryGetValue(name, out result)) return result;
            result = Decode(name); images.Add(name, result); fifo.Enqueue(name);
            while (fifo.Count > 16) { string old = fifo.Dequeue(); images[old].Dispose(); images.Remove(old); }
            return result;
        }
        internal Bitmap Idle { get { return idle; } }
        internal Bitmap Laugh(int frame) { return Cached("laugh/" + Math.Max(0, Math.Min(Frames - 1, frame)).ToString("0000") + ".png"); }
        internal Bitmap Blink(int frame) { return Cached("blink/0/" + Math.Max(0, Math.Min(BlinkFrames - 1, frame)).ToString("00") + ".png"); }

        // An original vector demo is included so the open-source program also runs without external artwork.
        internal static Bitmap DrawDemo(double gx, double gy, double phase, bool closed)
        {
            Bitmap image = new Bitmap(288, 368, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(image))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (SolidBrush shadow = new SolidBrush(Color.FromArgb(25, 40, 30, 12))) g.FillEllipse(shadow, 70, 340, 152, 12);
                using (LinearGradientBrush yellow = new LinearGradientBrush(new Rectangle(70, 30, 140, 315), Color.FromArgb(255, 231, 107), Color.FromArgb(220, 159, 35), 18))
                {
                    g.FillEllipse(yellow, 85, 287, 38, 59); g.FillEllipse(yellow, 166, 287, 34, 59);
                    using (GraphicsPath body = new GraphicsPath())
                    {
                        body.AddBezier(104, 116, 91, 83, 103, 40, 137, 39);
                        body.AddBezier(137, 39, 175, 34, 178, 89, 193, 121);
                        body.AddBezier(193, 121, 245, 202, 232, 292, 176, 307);
                        body.AddBezier(176, 307, 75, 336, 45, 219, 104, 116);
                        body.CloseFigure(); g.FillPath(yellow, body);
                    }
                    using (LinearGradientBrush belly = new LinearGradientBrush(new Rectangle(80, 136, 107, 149), Color.FromArgb(255, 244, 188), Color.FromArgb(237, 214, 135), 75)) g.FillEllipse(belly, 80, 136, 110, 150);
                    g.FillEllipse(yellow, 62, 173, 31, 81); g.FillEllipse(yellow, 177, 168, 34, 80);
                    g.FillEllipse(yellow, 80, 332, 48, 15); g.FillEllipse(yellow, 158, 332, 48, 15);
                }
                using (Pen line = new Pen(Color.FromArgb(127, 98, 35), 2.2f)) g.DrawArc(line, 112, 91, 36, 12, 8, 155);
                foreach (int x in new int[] { 119, 149 })
                {
                    if (closed) { using (Pen p = new Pen(Color.FromArgb(67, 81, 40), 2.5f)) g.DrawArc(p, x - 8, 65, 16, 5, 0, 180); }
                    else
                    {
                        using (SolidBrush iris = new SolidBrush(Color.FromArgb(177, 192, 101))) g.FillEllipse(iris, x - 9, 57, 18, 22);
                        using (SolidBrush pupil = new SolidBrush(Color.FromArgb(31, 48, 28))) g.FillEllipse(pupil, (float)(x - 4 + gx * 2.5), (float)(62 + gy * 2), 9, 12);
                        using (SolidBrush shine = new SolidBrush(Color.FromArgb(235, 255, 251, 224))) g.FillEllipse(shine, (float)(x - 2 + gx * 2.5), (float)(63 + gy * 2), 3, 3);
                    }
                }
                if (phase > 0)
                {
                    using (SolidBrush mouth = new SolidBrush(Color.FromArgb(105, 63, 35))) g.FillEllipse(mouth, 113, 92, 38, (float)(13 + Math.Abs(Math.Sin(phase * 10)) * 14));
                }
            }
            return image;
        }
        public void Dispose()
        {
            if (idle != null) { idle.Dispose(); idle = null; }
            foreach (Bitmap b in images.Values) b.Dispose(); images.Clear(); fifo.Clear();
            if (archive != null) { archive.Dispose(); archive = null; }
        }
    }
}
