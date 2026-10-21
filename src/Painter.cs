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

            if (state.PokeAt >= 0)
            {
                Bitmap next = Warp(scene, false, 0, 0, state.BellyAmount, theme.Belly); scene.Dispose(); scene = next;
            }
            int width = Math.Max(32, theme.Width * percent / 100), height = Math.Max(32, theme.Height * percent / 100);
            Bitmap result = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(result))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                float cx = width * .5f, foot = height * (state.Dragging ? .56f : .956f);
                g.TranslateTransform(cx, foot); g.RotateTransform((float)state.Tilt);
                if (!theme.Original && state.Laughing)
                {
                    float bounce = (float)Math.Sin(state.LaughTime * 20) * .025f;
                    g.RotateTransform((float)Math.Sin(state.LaughTime * 9) * 2);
                    g.ScaleTransform(1 + bounce, 1 - bounce);
                }
                float squash = (float)state.DropAmount;
                g.ScaleTransform(state.Dragging ? 1.006f : 1 + squash, state.Dragging ? 1.014f : 1 - squash);
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
