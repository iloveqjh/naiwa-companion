// SPDX-License-Identifier: MIT
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Media;
using System.Windows.Forms;

namespace NaiwaCompanion
{
    internal sealed class PetForm : Form
    {
        private Theme theme;
        private readonly Preferences prefs;
        internal readonly Animation Animation = new Animation();
        private readonly ClickTracker pointer = new ClickTracker();
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly Timer timer = new Timer();
        private readonly ContextMenuStrip menu = new ContextMenuStrip();
        private readonly NotifyIcon tray = new NotifyIcon();
        private SoundPlayer audio;
        private Icon petIcon;
        private byte[] alphaPixels;
        private Point dragOrigin, lastPointer;
        private double lastMoveAt, checkAt, fullscreenSince;
        private bool hidden, fullscreenCandidate, menuOpen, ready;
        private string lastFrame = "";
        internal bool HiddenByFullscreen { get { return hidden; } }

        internal PetForm(Theme loaded, Preferences settings)
        {
            theme = loaded; prefs = settings;
            Text = Desktop.WindowName; FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual; AutoScaleMode = AutoScaleMode.None; TopMost = prefs.AlwaysOnTop;
            SetStyle(ControlStyles.StandardClick | ControlStyles.StandardDoubleClick, true);
            ClientSize = new Size(theme.Width * prefs.Size / 100, theme.Height * prefs.Size / 100);
            if (prefs.X == int.MinValue || prefs.Y == int.MinValue) Home(); else Location = Clamp(new Point(prefs.X, prefs.Y), Size);
            Cursor = Cursors.Hand;
            menu.Font = new Font("Microsoft YaHei UI", 10);
            using (Bitmap iconImage = Theme.DrawDemo(0, 0, 0, false))
            using (Bitmap small = new Bitmap(iconImage, 32, 32))
            {
                IntPtr native = small.GetHicon();
                try { using (Icon temp = Icon.FromHandle(native)) petIcon = (Icon)temp.Clone(); }
                finally { Desktop.DestroyIcon(native); }
            }
            Icon = petIcon; LoadAudio(); BuildMenu();
            tray.Icon = petIcon; tray.Text = "奶蛙陪伴 · 双击笑 / 拖动 / 右键设置"; tray.ContextMenuStrip = menu;
            tray.DoubleClick += delegate { Home(); if (!hidden) { Show(); Render(true); } Save(); };
            timer.Interval = 16; timer.Tick += delegate { Tick(); };
            Shown += delegate { ready = true; tray.Visible = true; Render(true); timer.Start(); };
            menu.Opening += delegate { menuOpen = true; BuildMenu(); };
            menu.Closed += delegate { menuOpen = false; };
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get { CreateParams p = base.CreateParams; p.ExStyle |= 0x00080000 | 0x00000080 | 0x08000000; p.ClassStyle |= 8; return p; }
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Desktop.WakeMessage) { Home(); if (!hidden) { Show(); Render(true); } Save(); return; }
            if (m.Msg == 0x21) { m.Result = (IntPtr)3; return; }
            if (m.Msg == 0x84 && ready && !pointer.Held)
            {
                long packed = m.LParam.ToInt64(); Point p = PointToClient(new Point((short)packed, (short)(packed >> 16)));
                m.Result = (IntPtr)(Hit(p) ? 1 : -1); return;
            }
            base.WndProc(ref m);
            if (m.Msg == 0x7E && ready) { Location = Clamp(Location, Size); Render(true); Save(); }
        }
        internal bool Hit(Point p)
        { return alphaPixels != null && p.X >= 0 && p.Y >= 0 && p.X < Width && p.Y < Height && alphaPixels[(p.Y * Width + p.X) * 4 + 3] > 28; }
        private double Now { get { return clock.Elapsed.TotalSeconds; } }

        private void Tick()
        {
            double now = Now;
            if (now >= checkAt && !menuOpen && !pointer.Held)
            {
                checkAt = now + .12;
                bool detected = prefs.HideFullscreen && Desktop.IsFullscreen(Desktop.GetForegroundWindow(), Handle, Screen.FromRectangle(Bounds).Bounds);
                if (detected != fullscreenCandidate) { fullscreenCandidate = detected; fullscreenSince = now; }
                if (hidden != detected && now - fullscreenSince >= (detected ? .18 : .26)) SetHidden(detected);
            }
            if (hidden) return;
            Point p = PointToClient(Cursor.Position);
            bool hovered = Hit(p) || menuOpen || pointer.Held;
            PointF target = NaiwaCompanion.Animation.GazeTarget(p, ClientSize, hovered, prefs.FollowPointer);
            bool wasLaughing = Animation.Laughing;
            Animation.Advance(now, target, theme.Duration, theme.BlinkFrames / theme.BlinkFps);
            if (wasLaughing && !Animation.Laughing && audio != null) audio.Stop();
            Render(false);
            // When the gaze settles, reduce idle wakeups. Playback still uses its own clock.
            bool changingEyes = Math.Abs(Animation.GazeX - target.X) + Math.Abs(Animation.GazeY - target.Y) > .01;
            timer.Interval = Animation.Active || changingEyes ? 16 : 80;
        }
        internal void SetHidden(bool value)
        {
            if (hidden == value) return;
            hidden = value; bool resting = Animation.Resting;
            Animation.Stop(Now); Animation.Resting = resting;
            if (audio != null) audio.Stop(); pointer.Cancel(); Capture = false;
            if (value) { Hide(); timer.Interval = 150; }
            else { Show(); timer.Interval = 16; Render(true); }
        }
        internal void Render(bool force)
        {
            if (!IsHandleCreated || hidden || IsDisposed) return;
            string key = prefs.Size + "/" + (Animation.Resting ? "rest" : Animation.Laughing ? "laugh" + (int)(Animation.LaughTime * theme.Fps) : Animation.Blinking ? "blink" + (int)((Animation.Time - Animation.BlinkAt) * theme.BlinkFps) : "eye" + Math.Round(Animation.GazeX * 30) + "," + Math.Round(Animation.GazeY * 30));
            if (Animation.PokeAt >= 0 || Animation.DropAt >= 0 || Animation.StandAt >= 0 || Animation.Dragging || Math.Abs(Animation.Tilt) > .01) key += "/" + (int)(Now * 60);
            if (!force && key == lastFrame) return;
            using (Bitmap image = Painter.Render(theme, Animation, prefs.Size))
            { alphaPixels = Painter.ReadPixels(image); Desktop.Present(Handle, image, Location); }
            lastFrame = key;
        }
        private void LoadAudio()
        {
            if (audio != null) { audio.Stop(); audio.Dispose(); audio = null; }
            if (theme.AudioFile == null) return;
            try { audio = new SoundPlayer(theme.AudioFile); audio.Load(); }
            catch (Exception e) { Preferences.Log(e); if (audio != null) audio.Dispose(); audio = null; }
        }
        internal bool Laugh()
        {
            if (hidden || !Animation.StartLaugh(Now)) return false;
            if (prefs.Sound && audio != null) audio.Play(); Render(true); return true;
        }
        private void StopLaugh() { Animation.Stop(Now); if (audio != null) audio.Stop(); Render(true); }
        private ToolStripMenuItem Item(string label, EventHandler action, bool selected)
        { ToolStripMenuItem result = new ToolStripMenuItem(label, null, action); result.Checked = selected; menu.Items.Add(result); return result; }
        private void BuildMenu()
        {
            while (menu.Items.Count > 0) menu.Items[0].Dispose();
            Item("奶蛙陪伴 1.0 · " + (theme.Original ? "原版素材" : "内置演示"), null, false).Enabled = false;
            menu.Items.Add(new ToolStripSeparator());
            Item("捧腹大笑（双击）", delegate { Laugh(); }, false);
            Item("停止大笑 / 站起来", delegate { if (Animation.Resting) Animation.Stand(Now); else StopLaugh(); Render(true); }, false);
            Item("身外跟随，身上回正", delegate { prefs.FollowPointer = !prefs.FollowPointer; Save(); }, prefs.FollowPointer);
            ToolStripMenuItem sound = Item("原版笑声", delegate { prefs.Sound = !prefs.Sound; if (!prefs.Sound && audio != null) audio.Stop(); Save(); }, prefs.Sound); sound.Enabled = audio != null;
            menu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem sizes = Item("大小", null, false);
            int[] values = { 65, 85, 110, 135 };
            foreach (int value in values)
            {
                int size = value; ToolStripMenuItem choice = new ToolStripMenuItem(value + "%", null, delegate { SetSize(size); }); choice.Checked = prefs.Size == value; sizes.DropDownItems.Add(choice);
            }
            Item("总在最上面", delegate { prefs.AlwaysOnTop = !prefs.AlwaysOnTop; TopMost = prefs.AlwaysOnTop; Save(); }, prefs.AlwaysOnTop);
            Item("全屏时自动隐藏", delegate { prefs.HideFullscreen = !prefs.HideFullscreen; if (!prefs.HideFullscreen) SetHidden(false); Save(); }, prefs.HideFullscreen);
            Item("回到屏幕右下角", delegate { Home(); Render(true); Save(); }, false);
            menu.Items.Add(new ToolStripSeparator());
            Item("下载原版素材…", delegate
            {
                using (AssetSetupForm setup = new AssetSetupForm()) if (setup.ShowDialog() == DialogResult.OK) SwitchTheme(setup.SelectedFolder);
            }, false);
            Item("导入原版素材…", delegate { ImportTheme(); }, false);
            Item("使用说明", delegate { OpenGuide(); }, false);
            Item("项目与素材来源", delegate { Process.Start("https://github.com/iloveqjh/naiwa-companion"); }, false);
            menu.Items.Add(new ToolStripSeparator()); Item("退出奶蛙", delegate { Close(); }, false);
        }
        private void ImportTheme()
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "选择同时包含 素材.nwp 和 原版笑声.wav 的文件夹";
                if (dialog.ShowDialog() != DialogResult.OK) return;
                SwitchTheme(dialog.SelectedPath);
            }
        }
        private void SwitchTheme(string folder)
        {
            try
            {
                    Theme replacement = new Theme(folder);
                    if (!replacement.Original) { replacement.Dispose(); throw new InvalidDataException("这个文件夹中没有素材.nwp。"); }
                    StopLaugh(); Theme previous = theme; theme = replacement; previous.Dispose();
                    prefs.Assets = folder; LoadAudio(); SetSize(prefs.Size); Save();
            }
            catch (Exception e) { MessageBox.Show("素材导入失败：" + e.Message, "奶蛙陪伴"); }
        }
        private void OpenGuide()
        {
            string local = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "使用说明.txt");
            Process.Start(File.Exists(local) ? local : "https://github.com/iloveqjh/naiwa-companion/blob/main/docs/使用指南.md");
        }
        internal void SetSize(int percent)
        {
            int bottom = Bottom, center = Left + Width / 2; prefs.Size = Math.Max(50, Math.Min(150, percent));
            ClientSize = new Size(theme.Width * prefs.Size / 100, theme.Height * prefs.Size / 100);
            Location = Clamp(new Point(center - Width / 2, bottom - Height), Size); Render(true); Save();
        }
        internal static Point Clamp(Point desired, Size size)
        {
            Rectangle area = Screen.FromRectangle(new Rectangle(desired, size)).WorkingArea;
            return new Point(Math.Max(area.Left, Math.Min(desired.X, area.Right - size.Width)), Math.Max(area.Top, Math.Min(desired.Y, area.Bottom - size.Height + 8)));
        }
        private void Home()
        {
            Rectangle screen = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = Clamp(new Point(screen.Right - Width - 24, screen.Bottom - Height + 8), Size);
        }
        private void Save() { prefs.X = Left; prefs.Y = Top; prefs.Save(); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Right) { pointer.Cancel(); Capture = false; menu.Show(this, e.Location); return; }
            if (e.Button != MouseButtons.Left) return;
            pointer.Down(PointToScreen(e.Location)); dragOrigin = Location; lastPointer = pointer.Origin; lastMoveAt = Now; Capture = true;
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e); if (!pointer.Held) return;
            Point p = PointToScreen(e.Location); pointer.Move(p);
            if (!pointer.Moved) return;
            if (!Animation.Dragging) { Animation.BeginDrag(Now); if (audio != null) audio.Stop(); }
            Animation.DragVelocity(Now, p.X - lastPointer.X, Now - lastMoveAt); lastMoveAt = Now; lastPointer = p;
            Location = new Point(dragOrigin.X + p.X - pointer.Origin.X, dragOrigin.Y + p.Y - pointer.Origin.Y); Render(true);
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e); if (e.Button != MouseButtons.Left || !pointer.Held) return;
            bool moved = pointer.Moved;
            bool twice = pointer.Up(PointToScreen(e.Location), Now, SystemInformation.DoubleClickTime, SystemInformation.DoubleClickSize); Capture = false;
            if (moved) { Animation.Release(Now); Location = Clamp(Location, Size); Save(); }
            else if (twice) Laugh();
            else if (Animation.Resting) Animation.Stand(Now);
            else Animation.Poke(Now, new PointF(e.X * 100f / prefs.Size, e.Y * 100f / prefs.Size), new Size(theme.Width, theme.Height));
            Render(true);
        }
        protected override void OnMouseCaptureChanged(EventArgs e)
        { base.OnMouseCaptureChanged(e); if (!Capture && pointer.Held) { pointer.Cancel(); Animation.Release(Now); Location = Clamp(Location, Size); Render(true); Save(); } }
        protected override void OnFormClosing(FormClosingEventArgs e)
        { timer.Stop(); Save(); if (audio != null) audio.Stop(); base.OnFormClosing(e); }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { timer.Dispose(); tray.Visible = false; tray.Dispose(); Font menuFont = menu.Font; menu.Dispose(); menuFont.Dispose(); if (audio != null) audio.Dispose(); if (petIcon != null) petIcon.Dispose(); theme.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
