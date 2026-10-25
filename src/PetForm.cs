// SPDX-License-Identifier: MIT
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
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
        internal readonly TodoStore Todos;
        internal TodoForm TodoPanel { get; private set; }
        private ActionSound audio, reactionAudio;
        private bool reactionSoundStarted;
        private Icon petIcon;
        private byte[] alphaPixels;
        private Point dragOrigin, lastPointer;
        private double lastMoveAt, checkAt, fullscreenSince;
        private double repeatAt = -1;
        private bool hidden, fullscreenCandidate, menuOpen, ready;
        private bool headCandidate, headPressGesture;
        private string lastFrame = "";
        internal readonly CrawlMotion Crawl = new CrawlMotion();
        internal bool HiddenByFullscreen { get { return hidden; } }
        internal bool HasReactionSound { get { return reactionAudio != null && reactionAudio.Available; } }

        internal PetForm(Theme loaded, Preferences settings)
        {
            theme = loaded; prefs = settings;
            Todos = new TodoStore(prefs.Ephemeral ? null : TodoStore.DefaultPath);
            Animation.Head.ReactionDuration = theme.ReactionDuration;
            Text = Desktop.WindowName; FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual; AutoScaleMode = AutoScaleMode.None; TopMost = prefs.AlwaysOnTop;
            SetStyle(ControlStyles.StandardClick | ControlStyles.StandardDoubleClick, true);
            ClientSize = Painter.CanvasSize(theme, prefs.Size);
            if (!prefs.Ephemeral && prefs.CanvasVersion == 0 && prefs.X != int.MinValue && prefs.Y != int.MinValue)
            { int pad = Painter.Padding(theme, prefs.Size); prefs.X -= pad; prefs.Y -= pad; }
            prefs.CanvasVersion = 1;
            if (prefs.X == int.MinValue || prefs.Y == int.MinValue) Home(); else Location = Clamp(new Point(prefs.X, prefs.Y), Size);
            Cursor = Cursors.Hand;
            Crawl.Reset(Location);
            menu.Font = new Font("Microsoft YaHei UI", 10);
            RefreshIcon();
            Icon = petIcon; LoadAudio(); BuildMenu();
            tray.Icon = petIcon; tray.Text = "奶蛙陪伴 · 双击笑 / 拖动 / 右键设置"; tray.ContextMenuStrip = menu;
            tray.DoubleClick += delegate { Home(); if (!hidden) { Show(); Render(true); } Save(); };
            timer.Interval = 16; timer.Tick += delegate { Tick(); };
            Shown += delegate { ready = true; tray.Visible = !prefs.Ephemeral; Render(true); timer.Start(); if (theme.Recorded && prefs.RepeatAction && !theme.CanCrawl) repeatAt = Now + .6; if (!prefs.Ephemeral && Todos.View.PanelOpen) ShowTodos(false); };
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
            if (m.Msg == Desktop.TodoMessage) { ShowTodos(true); return; }
            if (m.Msg == Desktop.StatusMessage) { m.Result = (IntPtr)((hidden ? 1 : 0) | (Animation.Crawling ? 2 : 0) | (Animation.Laughing ? 4 : 0) | (Animation.Head.Held ? 8 : 0) | (Animation.Head.Active ? 16 : 0) | (prefs.Sound ? 32 : 0) | (HasReactionSound ? 64 : 0) | (TodoPanel != null && TodoPanel.Visible ? 128 : 0)); return; }
            if (m.Msg == 0x21) { m.Result = (IntPtr)3; return; }
            if (m.Msg == 0x84 && ready && !pointer.Held)
            {
                long packed = m.LParam.ToInt64(); Point p = PointToClient(new Point((short)packed, (short)(packed >> 16)));
                m.Result = (IntPtr)(Hit(p) ? 1 : -1); return;
            }
            base.WndProc(ref m);
            if (m.Msg == 0x7E && ready) { Location = Clamp(Location, Size); Crawl.Reset(Location); Render(true); Save(); }
        }
        internal bool Hit(Point p)
        { return alphaPixels != null && p.X >= 0 && p.Y >= 0 && p.X < Width && p.Y < Height && alphaPixels[(p.Y * Width + p.X) * 4 + 3] > 28; }
        internal bool HitHead(Point p)
        {
            if (!Hit(p) || Animation.Laughing || Animation.Resting || Animation.Dragging) return false;
            int pad = Painter.Padding(theme, prefs.Size);
            float x = (p.X - pad) * 100f / prefs.Size, y = (p.Y - pad) * 100f / prefs.Size;
            if (Animation.Head.Active)
            {
                double sy = Math.Max(.70, Math.Min(1.10, 1 - Animation.Head.Amount));
                Rectangle body = theme.BodyBounds;
                double sx = 1 / Math.Sqrt(sy);
                x = (float)(theme.Width * .5 + (x - theme.Width * .5) / sx);
                y = (float)(body.Bottom - 1 + (y - body.Bottom + 1) / sy);
            }
            if (theme.CanCrawl && Animation.FacingLeft) x = theme.Width - x;
            return theme.HeadRegion.Contains(x, y);
        }
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
            PointF? gazeOrigin = theme.GazeOrigin(prefs.Size);
            if (gazeOrigin.HasValue) { int pad = Painter.Padding(theme, prefs.Size); gazeOrigin = new PointF(gazeOrigin.Value.X + pad, gazeOrigin.Value.Y + pad); }
            if (theme.CanCrawl && Animation.FacingLeft && gazeOrigin.HasValue) gazeOrigin = new PointF(Width - gazeOrigin.Value.X, gazeOrigin.Value.Y);
            PointF target = NaiwaCompanion.Animation.GazeTarget(p, ClientSize, hovered, prefs.FollowPointer, gazeOrigin);
            if (theme.CanCrawl && Animation.FacingLeft) target.X = -target.X;
            bool wasLaughing = Animation.Laughing;
            Animation.Advance(now, target, theme.Duration, theme.BlinkFrames / theme.BlinkFps, !theme.Recorded);
            if (Animation.Head.Reacting)
            {
                if (!reactionSoundStarted) { reactionSoundStarted = true; if (prefs.Sound && reactionAudio != null) reactionAudio.Play(); }
            }
            else if (reactionSoundStarted) { reactionSoundStarted = false; if (reactionAudio != null) reactionAudio.Stop(); }
            if (wasLaughing && !Animation.Laughing)
            {
                if (audio != null) audio.Stop();
                if (theme.ReturnsToIdle) Animation.Stand(now);
                if (prefs.RepeatAction && !theme.CanCrawl) repeatAt = now + .85;
            }
            Animation.Crawling = theme.CanCrawl && prefs.CrawlEnabled && !Animation.Head.Active;
            if (theme.CanCrawl)
            {
                bool paused = !Animation.Crawling || Animation.Laughing || Animation.StandAt >= 0 || Animation.Dragging || pointer.Held || menuOpen || prefs.Ephemeral;
                Point next = Crawl.Advance(now, Screen.FromRectangle(Bounds).WorkingArea, Size, theme.Manifest.crawlSpeed * prefs.Size / 100.0, paused);
                if (!paused) Location = next;
                Animation.CrawlTime = Crawl.Time; Animation.FacingLeft = Crawl.Direction < 0;
            }
            if (repeatAt >= 0 && now >= repeatAt && !pointer.Held && !menuOpen) { repeatAt = -1; Laugh(); }
            Render(false);
            // When the gaze settles, reduce idle wakeups. Playback still uses its own clock.
            bool changingEyes = Math.Abs(Animation.GazeX - target.X) + Math.Abs(Animation.GazeY - target.Y) > .01;
            timer.Interval = Animation.Active || Animation.Crawling || changingEyes ? 16 : 80;
        }
        internal void SetHidden(bool value)
        {
            if (hidden == value) return;
            hidden = value; bool resting = Animation.Resting;
            headCandidate = headPressGesture = false;
            repeatAt = -1;
            Animation.Crawling = false; Crawl.Reset(Location);
            Animation.Stop(Now); Animation.Resting = resting;
            StopAudio(); pointer.Cancel(); Capture = false;
            if (TodoPanel != null) TodoPanel.SetSuspended(value);
            if (value) { Hide(); timer.Interval = 150; }
            else { Show(); timer.Interval = 16; Render(true); }
        }
        internal void Render(bool force)
        {
            if (!IsHandleCreated || hidden || IsDisposed) return;
            string key = prefs.Size + "/" + theme.ActionName + "/" + (Animation.Resting ? "rest" : Animation.Laughing ? "laugh" + (int)(Animation.LaughTime * theme.Fps) : Animation.Blinking ? "blink" + (int)((Animation.Time - Animation.BlinkAt) * theme.BlinkFps) : "eye" + Math.Round(Animation.GazeX * 30) + "," + Math.Round(Animation.GazeY * 30));
            if (theme.CanCrawl) key += "/crawl/" + Animation.Crawling + "/" + Animation.FacingLeft + "/" + theme.CrawlFrameIndex(Animation.CrawlTime) + "/" + Left + "/" + Top;
            if (Animation.Head.Reacting) key += "/reaction/" + (int)(Animation.Head.ReactionTime * (theme.ReactionAction != null ? theme.ReactionAction.fps : 60));
            if (Animation.Head.Active || Animation.PokeAt >= 0 || Animation.DropAt >= 0 || Animation.StandAt >= 0 || Animation.Dragging || Math.Abs(Animation.Tilt) > .01) key += "/" + (int)(Now * 60);
            if (!force && key == lastFrame) return;
            using (Bitmap image = Painter.Render(theme, Animation, prefs.Size))
            { alphaPixels = Painter.ReadPixels(image); Desktop.Present(Handle, image, Location); }
            lastFrame = key;
        }
        private void LoadAudio()
        {
            if (audio != null) { audio.Stop(); audio.Dispose(); audio = null; }
            if (reactionAudio != null) { reactionAudio.Dispose(); reactionAudio = null; }
            reactionSoundStarted = false;
            try { audio = new ActionSound(theme.SoundBytes(false)); }
            catch (Exception e) { Preferences.Log(e); }
            try { reactionAudio = new ActionSound(theme.SoundBytes(true)); }
            catch (Exception e) { Preferences.Log(e); }
        }
        private void StopAudio()
        { if (audio != null) audio.Stop(); if (reactionAudio != null) reactionAudio.Stop(); reactionSoundStarted = false; }
        internal bool Laugh()
        {
            if (theme.CanCrawl && theme.SelectedAction.id == theme.Manifest.locomotionAction)
            { if (hidden) return false; prefs.CrawlEnabled = true; Animation.Stop(Now); Animation.Crawling = true; Save(); Render(true); return true; }
            if (hidden || !Animation.StartLaugh(Now)) return false;
            StopAudio(); repeatAt = -1;
            if (prefs.Sound && audio != null) audio.Play(); Render(true); return true;
        }
        private void StopLaugh() { repeatAt = -1; Animation.Stop(Now); StopAudio(); Render(true); }
        internal void PlayAction(string id)
        { StopLaugh(); theme.SelectAction(id); LoadAudio(); Laugh(); }
        internal void DemoHeadPress()
        { repeatAt = -1; StopAudio(); Animation.DemoHeadPress(Now); timer.Interval = 16; Render(true); }
        private void ToggleCrawl()
        { prefs.CrawlEnabled = !prefs.CrawlEnabled; StopLaugh(); Animation.Crawling = prefs.CrawlEnabled; Crawl.Reset(Location); Save(); }
        private ToolStripMenuItem Item(string label, EventHandler action, bool selected)
        { ToolStripMenuItem result = new ToolStripMenuItem(label, null, action); result.Checked = selected; menu.Items.Add(result); return result; }
        private void BuildMenu()
        {
            while (menu.Items.Count > 0) menu.Items[0].Dispose();
            Item("奶蛙陪伴 1.5.0 · " + theme.Name, null, false).Enabled = false;
            Item("小待办 · " + Todos.PendingCount + " 件待完成", delegate { ShowTodos(true); }, false);
            BuildThemesMenu();
            menu.Items.Add(new ToolStripSeparator());
            Item("按头回弹（演示）", delegate { DemoHeadPress(); }, false);
            Item(theme.ActionName + "（双击）", delegate { Laugh(); }, false);
            if (theme.Recorded)
            {
                ToolStripMenuItem actions = Item("选择动作", null, false);
                foreach (RecordedAction action in theme.Manifest.actions)
                {
                    string id = action.id;
                    ToolStripMenuItem item = new ToolStripMenuItem(action.name, null, delegate { PlayAction(id); });
                    item.Checked = id == theme.SelectedAction.id; actions.DropDownItems.Add(item);
                }
            }
            if (theme.CanCrawl) Item(prefs.CrawlEnabled ? "暂停爬行（单击）" : "开始爬行（单击）", delegate { ToggleCrawl(); }, prefs.CrawlEnabled);
            else Item("循环当前动作", delegate { prefs.RepeatAction = !prefs.RepeatAction; if (!prefs.RepeatAction) repeatAt = -1; else if (!Animation.Laughing) repeatAt = Now + .2; Save(); }, prefs.RepeatAction);
            Item(theme.Recorded ? "停止动作 / 恢复待机" : "停止大笑 / 站起来", delegate { if (theme.CanCrawl) { prefs.CrawlEnabled = false; Animation.Crawling = false; Save(); } if (Animation.Resting) Animation.Stand(Now); else StopLaugh(); Render(true); }, false);
            Item("身外跟随，身上回正", delegate { prefs.FollowPointer = !prefs.FollowPointer; Save(); }, prefs.FollowPointer);
            ToolStripMenuItem sound = Item("动作声音", delegate { prefs.Sound = !prefs.Sound; if (!prefs.Sound) StopAudio(); Save(); }, prefs.Sound);
            sound.Enabled = (audio != null && audio.Available) || (reactionAudio != null && reactionAudio.Available);
            menu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem sizes = Item("大小", null, false);
            int[] values = { 65, 85, 110, 135 };
            foreach (int value in values)
            {
                int size = value; ToolStripMenuItem choice = new ToolStripMenuItem(value + "%", null, delegate { SetSize(size); }); choice.Checked = prefs.Size == value; sizes.DropDownItems.Add(choice);
            }
            Item("总在最上面", delegate { prefs.AlwaysOnTop = !prefs.AlwaysOnTop; TopMost = prefs.AlwaysOnTop; if (TodoPanel != null) TodoPanel.TopMost = prefs.AlwaysOnTop; Save(); }, prefs.AlwaysOnTop);
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
        private void BuildThemesMenu()
        {
            ToolStripMenuItem themes = Item("切换主题", null, false);
            ToolStripMenuItem classic = new ToolStripMenuItem("经典奶蛙", null, delegate { SwitchTheme(prefs.Assets); });
            classic.Checked = !theme.Recorded; themes.DropDownItems.Add(classic);
            System.Collections.Generic.HashSet<string> visited = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string[] roots = { Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "themes"), Path.Combine(Preferences.DirectoryPath, "themes") };
            foreach (string root in roots)
            {
                if (!Directory.Exists(root)) continue;
                foreach (string folder in Directory.GetDirectories(root))
                {
                    if (!File.Exists(Path.Combine(folder, "theme.nct")) || !visited.Add(Path.GetFullPath(folder))) continue;
                    try
                    {
                        string path = folder;
                        ToolStripMenuItem entry = new ToolStripMenuItem(ThemeManifest.DisplayName(folder), null, delegate { SwitchTheme(path); });
                        entry.Checked = String.Equals(Path.GetFullPath(folder), Path.GetFullPath(theme.Folder ?? "."), StringComparison.OrdinalIgnoreCase); themes.DropDownItems.Add(entry);
                    }
                    catch (Exception e) { Preferences.Log(e); }
                }
            }
            if (themes.DropDownItems.Count == 1) { ToolStripMenuItem hint = new ToolStripMenuItem("将动作包放入 themes 文件夹"); hint.Enabled = false; themes.DropDownItems.Add(hint); }
        }
        internal void ShowTodos(bool activate)
        {
            if (TodoPanel == null || TodoPanel.IsDisposed)
                TodoPanel = new TodoForm(Todos, prefs.AlwaysOnTop);
            TodoPanel.OpenNear(Bounds, activate && !hidden);
            if (hidden) TodoPanel.SetSuspended(true);
        }
        private void ImportTheme()
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "选择包含 素材.nwp 或 theme.nct 的素材文件夹";
                if (dialog.ShowDialog() != DialogResult.OK) return;
                SwitchTheme(dialog.SelectedPath);
            }
        }
        private void SwitchTheme(string folder)
        {
            try
            {
                    Theme replacement = new Theme(folder);
                    if (!replacement.Original) { replacement.Dispose(); throw new InvalidDataException("这个文件夹中没有素材.nwp 或 theme.nct。"); }
                    StopLaugh(); Theme previous = theme; theme = replacement; previous.Dispose();
                    if (theme.Recorded) prefs.ThemeFolder = folder; else { prefs.Assets = folder; prefs.ThemeFolder = ""; }
                    Animation.Crawling = false; Animation.FacingLeft = false; RefreshIcon();
                    Animation.Head.ReactionDuration = theme.ReactionDuration;
                    LoadAudio(); SetSize(prefs.Size); if (theme.Recorded && prefs.RepeatAction && !theme.CanCrawl) repeatAt = Now + .6; Save();
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
            ClientSize = Painter.CanvasSize(theme, prefs.Size);
            Location = prefs.Ephemeral ? new Point(-20000, -20000) : Clamp(new Point(center - Width / 2, bottom - Height), Size); Crawl.Reset(Location); Render(true); Save();
        }
        internal static Point Clamp(Point desired, Size size)
        {
            Rectangle area = Screen.FromRectangle(new Rectangle(desired, size)).WorkingArea;
            return new Point(Math.Max(area.Left, Math.Min(desired.X, area.Right - size.Width)), Math.Max(area.Top, Math.Min(desired.Y, area.Bottom - size.Height)));
        }
        private void Home()
        {
            Rectangle screen = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = Clamp(new Point(screen.Right - Width - 24, screen.Bottom - Height), Size);
            Crawl.Reset(Location);
        }
        private void Save() { prefs.X = Left; prefs.Y = Top; prefs.Save(); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Right) { pointer.Cancel(); Capture = false; menu.Show(this, e.Location); return; }
            if (e.Button != MouseButtons.Left) return;
            headPressGesture = false;
            headCandidate = HitHead(e.Location);
            pointer.Down(PointToScreen(e.Location)); dragOrigin = Location; lastPointer = pointer.Origin; lastMoveAt = Now; Capture = true;
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e); if (!pointer.Held) return;
            Point p = PointToScreen(e.Location); pointer.Move(p);
            if (headPressGesture && Animation.Head.Held)
            { Animation.Head.Pull(p.Y - pointer.Origin.Y, theme.BodyBounds.Height * prefs.Size / 100.0); return; }
            if (headCandidate && HeadPressMotion.DownwardGesture(pointer.Origin, p))
            {
                if (Animation.BeginHeadPress(Now))
                {
                    headPressGesture = true;
                    timer.Interval = 16;
                    repeatAt = -1; StopAudio();
                    Animation.Head.Pull(p.Y - pointer.Origin.Y, theme.BodyBounds.Height * prefs.Size / 100.0);
                    Render(true); return;
                }
            }
            if (!pointer.Moved) return;
            headCandidate = false;
            if (!Animation.Dragging) { repeatAt = -1; Animation.BeginDrag(Now); StopAudio(); }
            Animation.DragVelocity(Now, p.X - lastPointer.X, Now - lastMoveAt); lastMoveAt = Now; lastPointer = p;
            Location = new Point(dragOrigin.X + p.X - pointer.Origin.X, dragOrigin.Y + p.Y - pointer.Origin.Y); Render(true);
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e); if (e.Button != MouseButtons.Left || !pointer.Held) return;
            headCandidate = false;
            if (headPressGesture && Animation.Head.Held)
            {
                headPressGesture = false;
                Animation.Head.Advance(Now); Animation.Head.Release(Now); pointer.Cancel(); Capture = false;
                Crawl.Reset(Location); Render(true); return;
            }
            bool moved = pointer.Moved;
            bool twice = pointer.Up(PointToScreen(e.Location), Now, SystemInformation.DoubleClickTime, SystemInformation.DoubleClickSize); Capture = false;
            if (moved) { Animation.Release(Now); Location = Clamp(Location, Size); Crawl.Reset(Location); Save(); }
            else if (theme.CanCrawl && !twice) ToggleCrawl();
            else if (twice) Laugh();
            else if (Animation.Resting) Animation.Stand(Now);
            else { int pad = Painter.Padding(theme, prefs.Size); Animation.Poke(Now, new PointF((e.X - pad) * 100f / prefs.Size, (e.Y - pad) * 100f / prefs.Size), new Size(theme.Width, theme.Height), theme.Belly); }
            Render(true);
        }
        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (!Capture && pointer.Held)
            {
                headCandidate = false; pointer.Cancel();
                if (headPressGesture && Animation.Head.Held) Animation.Head.Release(Now); else Animation.Release(Now);
                headPressGesture = false;
                Location = Clamp(Location, Size); Crawl.Reset(Location); Render(true); Save();
            }
        }
        private void RefreshIcon()
        {
            Icon previous = petIcon;
            using (Bitmap small = new Bitmap(theme.Idle, 32, 32))
            {
                IntPtr native = small.GetHicon();
                try { using (Icon temp = Icon.FromHandle(native)) petIcon = (Icon)temp.Clone(); }
                finally { Desktop.DestroyIcon(native); }
            }
            Icon = petIcon; tray.Icon = petIcon; if (previous != null) previous.Dispose();
        }
        protected override void OnFormClosing(FormClosingEventArgs e)
        { timer.Stop(); if (TodoPanel != null && !TodoPanel.IsDisposed) TodoPanel.Shutdown(); Save(); StopAudio(); base.OnFormClosing(e); }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { if (TodoPanel != null) TodoPanel.Dispose(); timer.Dispose(); tray.Visible = false; tray.Dispose(); Font menuFont = menu.Font; menu.Dispose(); menuFont.Dispose(); if (audio != null) audio.Dispose(); if (reactionAudio != null) reactionAudio.Dispose(); if (petIcon != null) petIcon.Dispose(); theme.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
