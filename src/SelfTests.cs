// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows.Forms;

namespace NaiwaCompanion
{
    internal static class SelfTests
    {
        private static readonly List<string> log = new List<string>();
        private static void Check(bool ok, string label) { if (!ok) throw new Exception("FAIL " + label); log.Add("PASS " + label); }
        private static void Step(Animation a, double start, double end, PointF gaze, Theme theme)
        { for (double t = start; t <= end + .000001; t += 1.0 / 60) a.Advance(t, gaze, theme.Duration, theme.BlinkFrames / theme.BlinkFps); }
        internal static int Run(string assets, string output)
        {
            Directory.CreateDirectory(output); log.Clear();
            try
            {
                using (Theme theme = new Theme(assets))
                {
                    PointF left = Animation.GazeTarget(new PointF(-900, 50), new SizeF(288, 368), false, true);
                    PointF right = Animation.GazeTarget(new PointF(900, 50), new SizeF(288, 368), false, true);
                    PointF down = Animation.GazeTarget(new PointF(124, 1500), new SizeF(288, 368), false, true);
                    Check(left.X < -.9 && right.X > .9 && down.Y > .9, "pointer outside body gives independent horizontal and vertical gaze");
                    Check(Animation.GazeTarget(new PointF(900, 900), new SizeF(288, 368), true, true) == PointF.Empty, "pointer on body returns neutral gaze");
                    Check(Animation.GazeTarget(new PointF(900, 900), new SizeF(288, 368), false, false) == PointF.Empty, "gaze toggle disables tracking");
                    Animation a = new Animation(); Step(a, 0, .5, right, theme); Check(a.GazeX > .9, "gaze eases toward external pointer");
                    Step(a, .51, 1.2, PointF.Empty, theme); Check(Math.Abs(a.GazeX) < .002, "hover returns smoothly to neutral");
                    Check(a.StartLaugh(2) && !a.StartLaugh(2.1), "laugh cannot overlap");
                    a.Advance(2 + theme.Duration + .01, PointF.Empty, theme.Duration, .24);
                    Check(a.Resting && !a.Laughing, "laugh ends in resting state");
                    Step(a, 100, 101, PointF.Empty, theme); Check(a.Resting && !a.Blinking, "resting state stays still");
                    a.Stand(102); Check(!a.Resting && a.StandAt >= 0, "single click can stand up");
                    a.Stop(103); Check(!a.Poke(103, new PointF(124, 68), new Size(288, 368)), "face click does not deform belly");
                    Check(a.Poke(103, new PointF(144, 211), new Size(288, 368)), "belly click starts feedback");
                    a.Advance(103.1, PointF.Empty, theme.Duration, .24); Check(a.BellyAmount > .4, "belly compresses before rebound");
                    Step(a, 103.11, 104.3, PointF.Empty, theme); Check(a.PokeAt < 0 && a.BellyAmount == 0, "belly settles fully");
                    a.BeginDrag(105); a.DragVelocity(105.01, 50, .01); Step(a, 105.01, 105.09, PointF.Empty, theme);
                    Check(a.Tilt > 0 && a.Tilt < 7, "drag tilt is smooth and bounded"); a.Release(105.1);
                    Step(a, 105.1, 107.5, PointF.Empty, theme); Check(Math.Abs(a.Tilt) < .02 && a.DropAt < 0, "drag and landing settle");
                    a = new Animation(); a.Advance(5, PointF.Empty, theme.Duration, .24); Check(a.Blinking, "idle blinking starts");
                    a.Advance(5.3, PointF.Empty, theme.Duration, .24); Check(!a.Blinking, "blink completes");
                    ClickTracker clicks = new ClickTracker(); Point click = new Point(100, 100);
                    clicks.Down(click); Check(!clicks.Up(click, 1, 500, new Size(4, 4)), "single click remains single");
                    clicks.Down(click); Check(clicks.Up(click, 1.2, 500, new Size(4, 4)), "double click recognized");
                    clicks.Down(click); clicks.Move(new Point(200, 200)); Check(!clicks.Up(new Point(200, 200), 2, 500, new Size(4, 4)), "drag cannot become a double click");
                    using (Bitmap warped = Painter.Warp(theme.Idle, true, 1, -.8, 0))
                    {
                        int total = 0, outside = 0;
                        for (int y = 0; y < theme.Height; y++) for (int x = 0; x < theme.Width; x++)
                            if (warped.GetPixel(x, y) != theme.Idle.GetPixel(x, y)) { total++; if (x < theme.Width * .39 || x > theme.Width * .47 || y < theme.Height * .15 || y > theme.Height * .22) outside++; }
                        Check(total > 0 && outside == 0, "gaze changes only eye interior; body, face outline and alpha stay intact");
                    }
                    using (Bitmap warped = Painter.Warp(theme.Idle, false, 0, 0, .4))
                    {
                        int differences = 0;
                        for (int y = 0; y < theme.Height; y++) for (int x = 0; x < theme.Width; x++)
                            if (warped.GetPixel(x, y) != theme.Idle.GetPixel(x, y)) { differences++; CheckRegion(y >= 105 && y <= 300); }
                        Check(differences > 10, "belly warp preserves face and feet");
                    }
                    if (theme.Original)
                    {
                        Stopwatch speed = Stopwatch.StartNew();
                        for (int i = 0; i < theme.Frames; i++) theme.Laugh(i);
                        speed.Stop(); Check(theme.CacheCount <= 16, "full animation keeps decoded cache bounded");
                        log.Add("METRIC decode ms/frame=" + (speed.Elapsed.TotalMilliseconds / theme.Frames).ToString("F2"));
                    }
                    using (Theme demo = new Theme(null)) Check(!demo.Original && demo.Idle.GetPixel(0, 0).A == 0, "bundled demo works offline with transparent background");
                    // Exercise the actual native window without injecting input into another application.
                    Preferences temp = new Preferences(); temp.Ephemeral = true;
                    using (Theme demo = new Theme(null)) using (PetForm form = new PetForm(demo, temp))
                    {
                        form.Location = new Point(-20000, -20000); form.Show(); Application.DoEvents(); form.Render(true);
                        Check(Desktop.IsWindowVisible(form.Handle), "native transparent window starts");
                        int style = Desktop.GetWindowLong(form.Handle, -20);
                        Check((style & 0x80000) != 0 && (style & 0x08000000) != 0, "layered window does not take keyboard focus");
                        Check(!form.Hit(Point.Empty), "transparent pixels are click-through");
                        form.SetSize(110); Check(form.Width == 316, "size setting rescales window");
                        Check(form.Laugh() && !form.Laugh(), "native laugh rejects duplicate playback");
                        form.SetHidden(true); Check(!Desktop.IsWindowVisible(form.Handle) && !form.Laugh(), "hidden pet cannot laugh");
                        form.SetHidden(false); Check(Desktop.IsWindowVisible(form.Handle), "fullscreen recovery reveals pet");
                        form.Animation.Resting = true; form.SetHidden(true); form.SetHidden(false); Check(form.Animation.Resting, "fullscreen recovery preserves resting pose");
                        int before = Desktop.GetGuiResources(Process.GetCurrentProcess().Handle, 0);
                        for (int i = 0; i < 90; i++) form.Render(true);
                        int after = Desktop.GetGuiResources(Process.GetCurrentProcess().Handle, 0);
                        Check(after - before < 5, "repeated rendering releases GDI objects"); form.Close();
                    }
                    string invalidDir = Path.Combine(output, "invalid-pack"); Directory.CreateDirectory(invalidDir);
                    string invalidFile = Path.Combine(invalidDir, "素材.nwp");
                    if (!File.Exists(invalidFile)) using (ZipArchive zip = ZipFile.Open(invalidFile, ZipArchiveMode.Create))
                    { using (StreamWriter w = new StreamWriter(zip.CreateEntry("metadata.json").Open())) w.Write("{\"width\":99999,\"height\":368,\"frames\":1,\"fps\":30,\"blinkFrames\":7,\"blinkFps\":30}"); }
                    bool rejected = false; try { using (Theme bad = new Theme(invalidDir)) { } } catch (InvalidDataException) { rejected = true; }
                    Check(rejected, "invalid asset dimensions are rejected");
                }
                log.Add("RESULT PASS"); File.WriteAllLines(Path.Combine(output, "results.txt"), log, new UTF8Encoding(false)); return 0;
            }
            catch (Exception e) { log.Add(e.ToString()); log.Add("RESULT FAIL"); File.WriteAllLines(Path.Combine(output, "results.txt"), log, new UTF8Encoding(false)); return 1; }
        }
        private static void CheckRegion(bool ok) { if (!ok) throw new Exception("belly warp changed protected face or feet"); }
        internal static void Render(string assets, string output)
        {
            Directory.CreateDirectory(output);
            using (Theme theme = new Theme(assets))
            using (Bitmap sheet = new Bitmap(960, 620))
            using (Graphics page = Graphics.FromImage(sheet))
            using (Font heading = new Font("Microsoft YaHei UI", 13))
            using (Font title = new Font("Microsoft YaHei UI", 22, FontStyle.Bold))
            {
                page.Clear(Color.FromArgb(246, 244, 235)); page.DrawString("奶蛙陪伴 · 眼神与互动预览", title, Brushes.DarkOliveGreen, 30, 18);
                string[] labels = { "看向左上", "看向右下", "移到身上：回正", "自然眨眼", "戳肚子", "拖动倾斜", "捧腹大笑", "笑倒休息" };
                for (int i = 0; i < 8; i++)
                {
                    Animation a = new Animation();
                    if (i == 0) { a.GazeX = -1; a.GazeY = -1; }
                    if (i == 1) { a.GazeX = 1; a.GazeY = 1; }
                    if (i == 3) { a.BlinkAt = 0; a.Time = .10; }
                    if (i == 4) { a.PokeAt = 0; a.Time = .10; }
                    if (i == 5) { a.Dragging = true; a.Tilt = 6; }
                    if (i == 6) { a.LaughAt = 0; a.Time = 2; }
                    if (i == 7) a.Resting = true;
                    using (Bitmap frame = Painter.Render(theme, a, 65))
                    {
                        frame.Save(Path.Combine(output, "state-" + i + ".png"), ImageFormat.Png);
                        int x = 20 + (i % 4) * 238, y = 70 + (i / 4) * 270;
                        page.DrawImageUnscaled(frame, x + 15, y); page.DrawString(labels[i], heading, Brushes.DarkOliveGreen, x + 3, y + 242);
                    }
                }
                sheet.Save(Path.Combine(output, "preview.png"), ImageFormat.Png);
            }
        }
    }
}
