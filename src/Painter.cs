// SPDX-License-Identifier: MIT
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace NaiwaCompanion
{
    internal static class Painter
    {
        // Reserve room for the widest squash, rebound stretch and drag rotation.
        // This expands the transparent window without changing the pet's size.
        internal static int Padding(Theme theme, int percent)
        { return (int)Math.Ceiling(Math.Max(theme.Width, theme.Height) * percent / 100.0 * .16) + 3; }
        internal static Size CanvasSize(Theme theme, int percent)
        { int pad = Padding(theme, percent); return new Size(Math.Max(32, theme.Width * percent / 100) + pad * 2, Math.Max(32, theme.Height * percent / 100) + pad * 2); }
        internal static Bitmap Render(Theme theme, Animation state, int percent)
        {
            Bitmap scene;
            if (!theme.Original)
            {
                scene = Theme.DrawDemo(state.GazeX, state.GazeY, state.Laughing ? state.LaughTime + .01 : 0, state.Blinking);
                if (state.Resting)
                {
                    Bitmap lying = new Bitmap(theme.Width, theme.Height, PixelFormat.Format32bppPArgb);
                    using (Graphics g = Graphics.FromImage(lying))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.TranslateTransform(theme.Width * .5f, theme.Height * .77f); g.RotateTransform(82);
                        g.ScaleTransform(.70f, .70f); g.TranslateTransform(-theme.Width * .5f, -theme.Height * .5f);
                        g.DrawImageUnscaled(scene, 0, 0);
                    }
                    scene.Dispose(); scene = lying;
                }
            }
            else if (state.Head.Reacting && !state.Laughing && !state.Dragging)
            {
                scene = (Bitmap)theme.Reaction(state.Head.ReactionTime).Clone();
                if (theme.AlignReaction)
                {
                    Bitmap aligned = AlignReaction(scene, theme.BodyBounds); scene.Dispose(); scene = aligned;
                }
                if ((!theme.Recorded || !theme.Manifest.headReactionSeamless) && state.Head.ReactionWeight < .999)
                {
                    Bitmap mixed = Mix(scene, theme.Idle, 1 - state.Head.ReactionWeight); scene.Dispose(); scene = mixed;
                }
            }
            else if (state.Crawling && !state.Head.Active && theme.CanCrawl && !state.Laughing && !state.Dragging && state.StandAt < 0) scene = (Bitmap)theme.Crawl(state.CrawlTime).Clone();
            else if (state.Resting) scene = (Bitmap)theme.Laugh(theme.Frames - 1).Clone();
            else if (state.StandAt >= 0)
            {
                scene = new Bitmap(theme.Width, theme.Height, PixelFormat.Format32bppPArgb);
                using (Graphics g = Graphics.FromImage(scene))
                {
                    float t = (float)Math.Min(1, (state.Time - state.StandAt) / .26);
                    Blend(g, theme.Laugh(theme.Frames - 1), 1 - t); Blend(g, theme.Idle, t);
                }
            }
            else if (state.Laughing)
            {
                scene = (Bitmap)theme.Laugh((int)(state.LaughTime * theme.Fps)).Clone();
                if (state.LaughTime < .09)
                {
                    using (Graphics g = Graphics.FromImage(scene)) Blend(g, theme.Idle, (float)(1 - state.LaughTime / .09));
                }
            }
            else if (state.Blinking) scene = (Bitmap)theme.Blink((int)((state.Time - state.BlinkAt) * theme.BlinkFps)).Clone();
            else if (theme.Recorded)
            {
                scene = (Bitmap)theme.Idle.Clone();
                if (theme.Manifest.eyes != null) foreach (EyeArea eye in theme.Manifest.eyes)
                { Bitmap adjusted = Warp(scene, true, state.GazeX, state.GazeY, 0, eye.Rectangle); scene.Dispose(); scene = adjusted; }
            }
            else scene = Warp(theme.Idle, true, state.GazeX, state.GazeY, 0);

            if (theme.CanCrawl && state.FacingLeft) scene.RotateFlip(RotateFlipType.RotateNoneFlipX);
            if (state.PokeAt >= 0 && !state.Crawling)
            {
                Bitmap next = Warp(scene, false, 0, 0, state.BellyAmount, theme.Belly); scene.Dispose(); scene = next;
            }
            int width = Math.Max(32, theme.Width * percent / 100), height = Math.Max(32, theme.Height * percent / 100);
            int pad = Padding(theme, percent);
            Size canvas = CanvasSize(theme, percent);
            Bitmap result = new Bitmap(canvas.Width, canvas.Height, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(result))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.TranslateTransform(pad, pad);
                float cx = width * .5f, foot = height * (state.Dragging ? .56f : .956f);
                if (state.Head.Active) foot = (theme.BodyBounds.Bottom - 1) * percent / 100f;
                g.TranslateTransform(cx, foot); g.RotateTransform((float)state.Tilt);
                if (!theme.Original && state.Laughing)
                {
                    float bounce = (float)Math.Sin(state.LaughTime * 20) * .025f;
                    g.RotateTransform((float)Math.Sin(state.LaughTime * 9) * 2);
                    g.ScaleTransform(1 + bounce, 1 - bounce);
                }
                float squash = (float)state.DropAmount;
                g.ScaleTransform(state.Dragging ? 1.006f : 1 + squash, state.Dragging ? 1.014f : 1 - squash);
                if (state.Head.Active)
                {
                    float sy = (float)Math.Max(.70, Math.Min(1.10, 1 - state.Head.Amount));
                    float sx = (float)(1 / Math.Sqrt(sy));
                    g.ScaleTransform(sx, sy);
                }
                g.TranslateTransform(-cx, -foot); g.DrawImage(scene, new Rectangle(0, 0, width, height));
            }
            scene.Dispose(); return result;
        }
        private static void Blend(Graphics g, Bitmap b, float opacity)
        {
            if (opacity <= 0) return;
            using (ImageAttributes attr = new ImageAttributes())
            {
                ColorMatrix matrix = new ColorMatrix(); matrix.Matrix33 = Math.Min(1, opacity); attr.SetColorMatrix(matrix);
                g.DrawImage(b, new Rectangle(0, 0, b.Width, b.Height), 0, 0, b.Width, b.Height, GraphicsUnit.Pixel, attr);
            }
        }
        private static Bitmap Mix(Bitmap first, Bitmap second, double weight)
        {
            byte[] a = ReadPixels(first), b = ReadPixels(second);
            for (int n = 0; n < a.Length; n++) a[n] = (byte)Math.Round(a[n] * (1 - weight) + b[n] * weight);
            Bitmap result = new Bitmap(first.Width, first.Height, PixelFormat.Format32bppPArgb);
            BitmapData data = result.LockBits(new Rectangle(0, 0, first.Width, first.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            try { Marshal.Copy(a, 0, data.Scan0, a.Length); } finally { result.UnlockBits(data); }
            return result;
        }
        private static Bitmap AlignReaction(Bitmap input, Rectangle target)
        {
            byte[] pixels = ReadPixels(input); int left = input.Width, right = 0, bottom = 0;
            for (int y = 0; y < input.Height; y++) for (int x = 0; x < input.Width; x++) if (pixels[(y * input.Width + x) * 4 + 3] > 28)
            { left = Math.Min(left, x); right = Math.Max(right, x + 1); bottom = Math.Max(bottom, y + 1); }
            Bitmap result = new Bitmap(input.Width, input.Height, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(result))
            { g.CompositingMode = CompositingMode.SourceCopy; g.DrawImageUnscaled(input, (target.Left + target.Right - left - right) / 2, target.Bottom - bottom); }
            return result;
        }
        // Local inverse warping moves existing texture; no replacement face or eye is painted.
        internal static Bitmap Warp(Bitmap input, bool eye, double gazeX, double gazeY, double belly, RectangleF? region = null)
        {
            int w = input.Width, h = input.Height;
            byte[] pixels = ReadPixels(input), output = (byte[])pixels.Clone();
            double cx = w * (eye ? .431 : .50), cy = h * (eye ? .185 : .575);
            double rx = w * (eye ? .029 : .33), ry = h * (eye ? .024 : .195);
            if (region.HasValue) { RectangleF r = region.Value; cx = r.X + r.Width / 2; cy = r.Y + r.Height / 2; rx = r.Width / 2; ry = r.Height / 2; }
            int left = Math.Max(0, (int)(cx - rx)), right = Math.Min(w - 2, (int)(cx + rx));
            int top = Math.Max(0, (int)(cy - ry)), bottom = Math.Min(h - 2, (int)(cy + ry));
            for (int y = top; y <= bottom; y++) for (int x = left; x <= right; x++)
            {
                double nx = (x - cx) / rx, ny = (y - cy) / ry, r = nx * nx + ny * ny;
                if (r >= 1) continue;
                double sx, sy;
                if (eye)
                {
                    double weight = Math.Pow(1 - r, .65);
                    sx = x - gazeX * (region.HasValue ? rx * .28 : w * .0083) * weight; sy = y - gazeY * (region.HasValue ? ry * .23 : h * .0055) * weight;
                }
                else
                {
                    double weight = (1 - r) * (1 - r) * belly;
                    sx = x + (x - cx) * weight; sy = y + (y - cy) * weight * .65;
                }
                sx = Math.Max(0, Math.Min(w - 1.001, sx)); sy = Math.Max(0, Math.Min(h - 1.001, sy));
                int px = (int)sx, py = (int)sy; double fx = sx - px, fy = sy - py;
                int a = (py * w + px) * 4, dest = (y * w + x) * 4;
                for (int c = 0; c < 4; c++)
                {
                    double upper = pixels[a + c] * (1 - fx) + pixels[a + 4 + c] * fx;
                    double lower = pixels[a + w * 4 + c] * (1 - fx) + pixels[a + w * 4 + 4 + c] * fx;
                    output[dest + c] = (byte)Math.Max(0, Math.Min(255, upper * (1 - fy) + lower * fy));
                }
            }
            Bitmap result = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            BitmapData data = result.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            try { Marshal.Copy(output, 0, data.Scan0, output.Length); } finally { result.UnlockBits(data); }
            return result;
        }
        internal static byte[] ReadPixels(Bitmap input)
        {
            byte[] result = new byte[input.Width * input.Height * 4];
            BitmapData data = input.LockBits(new Rectangle(0, 0, input.Width, input.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            try { Marshal.Copy(data.Scan0, result, 0, result.Length); } finally { input.UnlockBits(data); }
            return result;
        }
    }
}
