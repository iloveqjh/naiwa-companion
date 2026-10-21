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
                TestCrawlMotion();
                TestPalette();
                using (Theme theme = new Theme(assets))
                {
                    if (theme.Recorded)
                    {
                        TestRecorded(theme, output);
                        log.Add("RESULT PASS"); File.WriteAllLines(Path.Combine(output, "results.txt"), log, new UTF8Encoding(false)); return 0;
                    }
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
                    TestRecordedFixture(output);
                }
                log.Add("RESULT PASS"); File.WriteAllLines(Path.Combine(output, "results.txt"), log, new UTF8Encoding(false)); return 0;
            }
            catch (Exception e) { log.Add(e.ToString()); log.Add("RESULT FAIL"); File.WriteAllLines(Path.Combine(output, "results.txt"), log, new UTF8Encoding(false)); return 1; }
        }
        private static void CheckRegion(bool ok) { if (!ok) throw new Exception("belly warp changed protected face or feet"); }
        private static void TestCrawlMotion()
        {
            CrawlMotion crawl = new CrawlMotion(); Rectangle area = new Rectangle(-1600, -200, 1600, 1000); Size size = new Size(300, 380);
            crawl.Reset(new Point(-900, 200)); crawl.Advance(0, area, size, 54, false);
            for (int n = 1; n <= 60; n++) crawl.Advance(n / 60.0, area, size, 54, false);
            Check(Math.Abs(crawl.X + 846) < .001, "crawl preserves fractional movement across 60 Hz ticks");
            double x = crawl.X, phase = crawl.Time;
            crawl.Advance(1.06, area, size, 54, true);
            Check(crawl.X == x && crawl.Time == phase, "paused crawl freezes position and gait together");
            crawl.Reset(new Point(-301, 200)); crawl.Direction = 1; crawl.Advance(2, area, size, 54, false);
            crawl.Advance(2.08, area, size, 54, false);
            Check(crawl.Direction == -1 && crawl.X < -300, "right boundary turns around and preserves overshoot");
            crawl.Reset(new Point(-1599, 200)); crawl.Direction = -1; crawl.Advance(3, area, size, 54, false);
            crawl.Advance(3.08, area, size, 54, false);
            Check(crawl.Direction == 1 && crawl.X > -1600, "left boundary turns around on a negative-coordinate monitor");
            for (int n = 1; n <= 30000; n++)
            {
                Point p = crawl.Advance(3.08 + n / 60.0, area, size, 54, false);
                if (p.X < area.Left || p.X + size.Width > area.Right || p.Y < area.Top || p.Y + size.Height > area.Bottom) throw new Exception("crawl escaped work area");
            }
            Check(true, "continuous crawl stays inside monitor work area through repeated turns");
            crawl.Reset(new Point(-20000, -20000)); crawl.Advance(0, new Rectangle(0, 0, 200, 200), size, 54, false);
            Check(crawl.X == 0 && crawl.Y == 0, "oversized pet on a small work area does not loop or escape");
            crawl.Reset(new Point(-1200, 400)); crawl.Advance(0, area, size, 54, false);
            Check(crawl.X == -1200 && crawl.Y == 400, "drag release and resize reset movement to current position");
        }
        private static void TestPalette()
        {
            using (Bitmap image = new Bitmap(3, 1, PixelFormat.Format32bppPArgb))
            {
                image.SetPixel(0, 0, Color.FromArgb(255, 230, 155, 45));
                image.SetPixel(1, 0, Color.FromArgb(80, 100, 70, 30));
                image.SetPixel(2, 0, Color.Transparent);
                byte[] before = Painter.ReadPixels(image); Theme.ApplyPalette(image, .86, 1.035, 1.06); byte[] after = Painter.ReadPixels(image);
                Check(after[1] > before[1] && after[5] > before[5], "theme palette lifts midtone yellow and translucent edges");
                for (int n = 0; n < after.Length; n += 4)
                    if (after[n + 3] != before[n + 3] || after[n] > after[n + 3] || after[n + 1] > after[n + 3] || after[n + 2] > after[n + 3]) throw new Exception("palette damaged premultiplied alpha");
                Check(true, "palette keeps original alpha and valid premultiplied edges");
            }
        }
        private static void TestRecorded(Theme theme, string output)
        {
            Check(theme.Recorded && theme.Manifest.actions.Length > 0, "recorded theme manifest and action list load");
            Check(theme.Idle.GetPixel(0, 0).A == 0, "recorded theme has transparent canvas corners");
            string first = theme.Manifest.actions[0].id;
            foreach (RecordedAction action in theme.Manifest.actions)
            {
                theme.SelectAction(action.id);
                Check(theme.Frames == action.frames && Math.Abs(theme.Duration - action.frames / action.fps) < .001, action.name + ": independent frame count and duration");
                Animation state = new Animation(); state.StartLaugh(0);
                Stopwatch sw = Stopwatch.StartNew();
                for (int i = 0; i < action.frames; i++)
                {
                    state.Advance(i / action.fps, PointF.Empty, theme.Duration, 1.0 / 30, false);
                    using (Bitmap image = Painter.Render(theme, state, 65))
                    {
                        if (image.GetPixel(0, 0).A != 0 || image.GetPixel(image.Width - 1, image.Height - 1).A != 0) throw new Exception("non-transparent theme corner");
                        if (i == action.frames / 2) image.Save(Path.Combine(output, action.id + ".png"), ImageFormat.Png);
                    }
                }
                sw.Stop();
                Check(theme.CacheCount <= 16, action.name + ": every frame rendered with bounded cache");
                log.Add("METRIC " + action.id + " render ms/frame=" + (sw.Elapsed.TotalMilliseconds / action.frames).ToString("F2"));
                state.Advance(theme.Duration + .01, PointF.Empty, theme.Duration, 1.0 / 30, false);
                Check(state.Resting && !state.Laughing, action.name + ": playback completes");
                if (theme.ReturnsToIdle) { state.Stand(theme.Duration + .01); Check(!state.Resting && state.StandAt >= 0, action.name + ": returns through transition to idle"); }
            }
            theme.SelectAction(first);
            if (theme.CanCrawl)
            {
                using (Bitmap right = Painter.Render(theme, new Animation { Crawling = true, CrawlTime = .3 }, 100))
                using (Bitmap left = Painter.Render(theme, new Animation { Crawling = true, FacingLeft = true, CrawlTime = .3 }, 100))
                {
                    byte[] pixels = Painter.ReadPixels(right), mirrored = Painter.ReadPixels(left);
                    int differences = 0;
                    for (int y = 0; y < theme.Height; y++) for (int x = 0; x < theme.Width; x++)
                    {
                        int a = (y * theme.Width + x) * 4, b = (y * theme.Width + theme.Width - 1 - x) * 4;
                        for (int c = 0; c < 4; c++) if (Math.Abs(pixels[a + c] - mirrored[b + c]) > 2) differences++;
                    }
                    Check(differences < theme.Width * theme.Height / 100, "crawl turn mirrors body and gait consistently");
                    right.Save(Path.Combine(output, "crawl-right.png")); left.Save(Path.Combine(output, "crawl-left.png"));
                }
            }
            bool rejected = false; try { theme.SelectAction("does-not-exist"); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "unknown recorded action rejected without replacing active action");
            using (Bitmap moved = Painter.Render(theme, new Animation { GazeX = 1, GazeY = -.7 }, 100))
            {
                byte[] before = Painter.ReadPixels(theme.Idle), after = Painter.ReadPixels(moved);
                // Pixel-for-pixel checks of the warp itself avoid scaling interpolation differences.
                using (Bitmap warped = RecordedGaze(theme, 1, -.7))
                {
                    byte[] changed = Painter.ReadPixels(warped); int differences = 0;
                    for (int y = 0; y < theme.Height; y++) for (int x = 0; x < theme.Width; x++)
                    {
                        int p = (y * theme.Width + x) * 4;
                        bool diff = false; for (int c = 0; c < 4; c++) if (before[p + c] != changed[p + c]) diff = true;
                        if (!diff) continue; differences++;
                        bool inside = false; if (theme.Manifest.eyes != null) foreach (EyeArea eye in theme.Manifest.eyes) if (eye.Rectangle.Contains(x, y)) inside = true;
                        if (!inside) throw new Exception("recorded gaze changed pixels outside eye calibration");
                    }
                    Check(differences > 0 || theme.Manifest.eyes == null || theme.Manifest.eyes.Length == 0, "recorded gaze changes only calibrated eye regions");
                }
            }
            Preferences prefs = new Preferences { Ephemeral = true };
            using (Theme another = new Theme(theme.Folder)) using (PetForm form = new PetForm(another, prefs))
            {
                form.Location = new Point(-20000, -20000); form.Show(); Application.DoEvents();
                form.PlayAction(first); Check(form.Animation.Laughing || (theme.CanCrawl && form.Animation.Crawling), "recorded menu action starts native playback");
                form.SetHidden(true); Check(!form.Animation.Laughing && !form.Animation.Crawling, "recorded playback stops when hidden");
                form.SetHidden(false); form.Close();
            }
        }
        private static Bitmap RecordedGaze(Theme theme, double x, double y)
        {
            Bitmap result = (Bitmap)theme.Idle.Clone();
            if (theme.Manifest.eyes != null) foreach (EyeArea eye in theme.Manifest.eyes)
            { Bitmap next = Painter.Warp(result, true, x, y, 0, eye.Rectangle); result.Dispose(); result = next; }
            return result;
        }
        private static void TestRecordedFixture(string output)
        {
            string folder = Path.Combine(output, "recorded-fixture"); Directory.CreateDirectory(folder);
            string pack = Path.Combine(folder, "theme.nct");
            using (FileStream file = new FileStream(pack, FileMode.Create))
            using (ZipArchive zip = new ZipArchive(file, ZipArchiveMode.Create))
            {
                using (Bitmap idle = Theme.DrawDemo(0, 0, 0, false)) using (Stream s = zip.CreateEntry("idle.png").Open()) idle.Save(s, ImageFormat.Png);
                for (int i = 0; i < 4; i++) using (Bitmap frame = Theme.DrawDemo(0, 0, i * .2 + .01, false)) using (Stream s = zip.CreateEntry("actions/wave/" + i.ToString("0000") + ".png").Open()) frame.Save(s, ImageFormat.Png);
                using (StreamWriter w = new StreamWriter(zip.CreateEntry("manifest.json").Open()))
                    w.Write("{\"schemaVersion\":1,\"id\":\"test\",\"name\":\"Test theme\",\"width\":288,\"height\":368,\"idle\":\"idle.png\",\"eyes\":[{\"x\":110,\"y\":57,\"width\":18,\"height\":22}],\"actions\":[{\"id\":\"wave\",\"name\":\"Wave\",\"frames\":4,\"fps\":30,\"prefix\":\"actions/wave/\",\"endPose\":\"idle\"}]}");
            }
            using (Theme fixture = new Theme(folder)) TestRecorded(fixture, output);
        }
        internal static void Render(string assets, string output)
        {
            Directory.CreateDirectory(output);
            using (Theme theme = new Theme(assets))
            using (Bitmap sheet = new Bitmap(960, 620))
            using (Graphics page = Graphics.FromImage(sheet))
            using (Font heading = new Font("Microsoft YaHei UI", 13))
            using (Font title = new Font("Microsoft YaHei UI", 22, FontStyle.Bold))
            {
                if (theme.Recorded) { RenderRecorded(theme, output); return; }
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
        private static void RenderRecorded(Theme theme, string output)
        {
            if (theme.CanCrawl)
            {
                string motion = Path.Combine(output, "motion"); Directory.CreateDirectory(motion);
                CrawlMotion crawl = new CrawlMotion(); Rectangle area = new Rectangle(0, 0, 960, 448); Size size = new Size(307, 358);
                crawl.Reset(new Point(530, 64));
                for (int frame = 0; frame < 300; frame++)
                {
                    Point position = crawl.Advance(frame / 30.0, area, size, theme.Manifest.crawlSpeed * .8, false);
                    Animation state = new Animation { Crawling = true, CrawlTime = crawl.Time, FacingLeft = crawl.Direction < 0 };
                    using (Bitmap image = new Bitmap(area.Width, area.Height))
                    using (Graphics page = Graphics.FromImage(image))
                    using (Bitmap pet = Painter.Render(theme, state, 80))
                    using (Font caption = new Font("Microsoft YaHei UI", 15))
                    {
                        page.Clear(Color.FromArgb(239, 244, 247));
                        page.DrawString("爬爬奶蛙 · 连续爬行 / 碰边折返", caption, Brushes.DarkSlateGray, 24, 18);
                        page.DrawImageUnscaled(pet, position);
                        image.Save(Path.Combine(motion, frame.ToString("0000") + ".png"), ImageFormat.Png);
                    }
                }
            }
            int count = theme.Manifest.actions.Length + 1;
            using (Bitmap sheet = new Bitmap(260 * count, 390))
            using (Graphics g = Graphics.FromImage(sheet))
            using (Font font = new Font("Microsoft YaHei UI", 13))
            {
                g.Clear(Color.FromArgb(39, 43, 49)); g.DrawString(theme.Name + " · 动作预览", font, Brushes.White, 16, 10);
                for (int i = 0; i < count; i++)
                {
                    Animation a = new Animation(); string label = "待机 / 眼神跟随";
                    if (i > 0) { RecordedAction action = theme.Manifest.actions[i - 1]; theme.SelectAction(action.id); a.LaughAt = 0; a.Time = theme.Duration * .5; label = action.name; }
                    using (Bitmap image = Painter.Render(theme, a, 65))
                    { image.Save(Path.Combine(output, "theme-state-" + i + ".png"), ImageFormat.Png); g.DrawImageUnscaled(image, 260 * i + 5, 50); }
                    g.DrawString(label, font, Brushes.White, 260 * i + 12, 350);
                }
                sheet.Save(Path.Combine(output, "preview.png"), ImageFormat.Png);
            }
        }
    }
}
