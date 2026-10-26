// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace NaiwaCompanion
{
    internal sealed class SpeechOptions
    {
        public int Version = 1, Idle = 1, ReminderMinutes = 30, Seconds = 6, Flavor;
        public bool Interactions = true, TaskFeedback = true, QuietHours;
        public string Daily = "18:00", QuietFrom = "23:00", QuietTo = "08:00";
        public DateTime MutedUntilUtc = DateTime.MinValue;
        internal static bool Valid(SpeechOptions s)
        {
            TimeSpan time;
            return s.Version == 1 && s.Idle >= 0 && s.Idle <= 3 && (s.ReminderMinutes == -1 || s.ReminderMinutes == 0 || s.ReminderMinutes == 15 || s.ReminderMinutes == 30 || s.ReminderMinutes == 60) &&
                s.Seconds >= 4 && s.Seconds <= 15 && s.Flavor >= 0 && s.Flavor <= 2 && TimeSpan.TryParse(s.Daily, out time) && time.TotalHours >= 0 && time.TotalHours < 24 &&
                TimeSpan.TryParse(s.QuietFrom, out time) && time.TotalHours >= 0 && time.TotalHours < 24 && TimeSpan.TryParse(s.QuietTo, out time) && time.TotalHours >= 0 && time.TotalHours < 24;
        }
        internal bool IsQuiet(DateTime local, DateTime utc)
        {
            if (utc < MutedUntilUtc) return true;
            if (!QuietHours) return false;
            TimeSpan start = TimeSpan.Parse(QuietFrom), end = TimeSpan.Parse(QuietTo), now = local.TimeOfDay;
            return start < end ? now >= start && now < end : now >= start || now < end;
        }
    }
    internal sealed class SpeechBank
    {
        public List<SpeechGroup> groups = new List<SpeechGroup>();
    }
    internal sealed class SpeechGroup { public string id = ""; public string[] lines = new string[0]; }
    internal sealed class SpeechLibrary
    {
        private readonly Dictionary<string, string[]> groups = new Dictionary<string, string[]>();
        private readonly Queue<string> recent = new Queue<string>();
        private readonly Random random = new Random();
        internal SpeechLibrary()
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("NaiwaCompanion.Speech.json"))
            using (StreamReader reader = new StreamReader(stream))
                foreach (SpeechGroup group in new JavaScriptSerializer().Deserialize<SpeechBank>(reader.ReadToEnd()).groups) groups[group.id] = group.lines;
        }
        internal string Pick(string id)
        {
            string[] lines = groups[id]; List<string> available = new List<string>();
            foreach (string line in lines) if (!recent.Contains(line)) available.Add(line);
            if (available.Count == 0) available.AddRange(lines);
            string result = available[random.Next(available.Count)]; recent.Enqueue(result); while (recent.Count > 12) recent.Dequeue(); return result;
        }
        internal string Idle(int flavor)
        {
            bool absurd = random.Next(100) < (flavor == 1 ? 80 : flavor == 2 ? 20 : 60);
            return Pick(absurd ? "idle_absurd" : "idle_cute");
        }
        internal string Interaction(string tag)
        {
            if (tag == "head") return random.Next(2) == 0 ? "脑袋收到了，我正在把形状整理回来。" : "摸摸头已签收，稍后兑换成好心情。";
            if (tag == "belly") return random.Next(2) == 0 ? "你戳到的不是肚子，是可爱储备仓。" : "不要紧，我有一点弹性，还有很多肚子。";
            if (tag == "drag") return random.Next(2) == 0 ? "刚刚被拎了一下，蛙生海拔发生变化。" : "我站稳了，刚才只是灵魂晚到半步。";
            if (tag == "laugh") return "笑到肚皮打结了，容我圆一会儿。";
            return "看到你啦，本蛙的可爱正在正常营业。";
        }
    }

    internal sealed class SpeechBubble : Form
    {
        private readonly Label body = new Label(), heading = new Label();
        private readonly Button close = new Button(), tasks = new Button(), snooze = new Button();
        private readonly Timer fade = new Timer();
        private readonly System.Diagnostics.Stopwatch lifetime = new System.Diagnostics.Stopwatch();
        private readonly float scale;
        private double hold, hoverAt = -1;
        private int side, tailX, textHeight;
        private Rectangle card;
        internal bool IsSummary { get; private set; }
        internal event EventHandler OpenTasks, Snoozed;
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams { get { CreateParams p = base.CreateParams; p.ExStyle |= 0x08000000 | 0x00000080; p.ClassStyle |= 0x20000; return p; } }
        internal SpeechBubble()
        {
            using (Graphics graphics = CreateGraphics()) scale = graphics.DpiX / 96f;
            Text = "奶蛙说话"; FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(255, 254, 244); DoubleBuffered = true;
            Font = new Font("Microsoft YaHei UI", 9.5f);
            heading.Text = "奶蛙叭叭"; heading.ForeColor = Color.FromArgb(110, 127, 99); heading.Font = new Font(Font.FontFamily, 8f); heading.AutoSize = false; Controls.Add(heading);
            body.ForeColor = Color.FromArgb(49, 64, 42); body.UseMnemonic = false; body.AutoEllipsis = true; Controls.Add(body);
            Prepare(close, "×"); close.AccessibleName = "收起这句话"; close.Click += delegate { Hide(); };
            Prepare(tasks, "打开待办"); tasks.Click += delegate { if (OpenTasks != null) OpenTasks(this, EventArgs.Empty); Hide(); };
            Prepare(snooze, "稍后15分"); snooze.Click += delegate { if (Snoozed != null) Snoozed(this, EventArgs.Empty); Hide(); };
            fade.Interval = 30; fade.Tick += delegate
            {
                if (!Visible) { fade.Stop(); return; }
                double now = lifetime.Elapsed.TotalSeconds;
                if (Bounds.Contains(Cursor.Position)) { if (hoverAt < 0) hoverAt = now; }
                else if (hoverAt >= 0) { hold += now - hoverAt; hoverAt = -1; }
                double elapsed = hoverAt >= 0 ? hoverAt : now;
                Opacity = Math.Max(.03, Math.Min(1, now / .15));
                if (elapsed > hold) { Opacity = Math.Max(.03, 1 - (elapsed - hold) / .18); if (elapsed >= hold + .18) Hide(); }
            };
        }
        private int S(int value) { return (int)Math.Round(value * scale); }
        private void Prepare(Button button, string label)
        { button.Text = label; button.FlatStyle = FlatStyle.Flat; button.FlatAppearance.BorderSize = 0; button.BackColor = Color.FromArgb(237, 243, 226); button.ForeColor = Color.FromArgb(61, 99, 58); button.Font = Font; button.TabStop = false; Controls.Add(button); }
        internal static Rectangle Place(Rectangle pet, Rectangle area, Size size)
        {
            int x = pet.Left + pet.Width / 2 - size.Width / 2, y = pet.Top - size.Height - 9;
            if (y < area.Top) { x = pet.Left - size.Width - 12; y = pet.Top; if (x < area.Left) x = pet.Right + 12; }
            return new Rectangle(Math.Max(area.Left, Math.Min(x, area.Right - size.Width)), Math.Max(area.Top, Math.Min(y, area.Bottom - size.Height)), size.Width, size.Height);
        }
        internal void Say(string text, bool summary, bool canSnooze, int seconds, Rectangle pet, bool topMost)
        {
            body.Text = text; IsSummary = summary; TopMost = topMost;
            int width = S(226), padding = S(13), textWidth = width - padding * 2;
            Size measure = TextRenderer.MeasureText(text, Font, new Size(textWidth, S(150)), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
            textHeight = Math.Min(S(150), Math.Max(S(27), measure.Height + S(4)));
            ClientSize = new Size(width, S(41) + textHeight + (summary ? S(37) : S(5)) + S(12));
            Reanchor(pet);
            tasks.Visible = summary; snooze.Visible = summary && canSnooze;
            hold = seconds; hoverAt = -1; Opacity = .05; lifetime.Restart(); if (!Visible) Show(); fade.Start(); Invalidate();
        }
        internal void Reanchor(Rectangle pet)
        {
            Rectangle area = Screen.FromRectangle(pet).WorkingArea;
            Rectangle placement = Place(pet, area, Size); Location = placement.Location;
            side = placement.Bottom <= pet.Top + 3 ? 0 : placement.Right <= pet.Left ? 1 : placement.Left >= pet.Right ? 2 : 0;
            int tail = S(10);
            card = new Rectangle(side == 2 ? tail : 0, 0, Width - (side == 0 ? 0 : tail), Height - (side == 0 ? tail : 0));
            tailX = Math.Max(S(24), Math.Min(card.Width - S(24), pet.Left + pet.Width / 2 - Left));
            using (GraphicsPath path = Rounded(card, S(13)))
            { path.AddPolygon(Tail()); Region old = Region; Region = new Region(path); if (old != null) old.Dispose(); }
            int left = card.Left + S(13), top = card.Top;
            heading.SetBounds(left, top + S(8), S(155), S(19)); close.SetBounds(card.Right - S(31), top + S(4), S(26), S(25));
            body.SetBounds(left, top + S(33), card.Width - S(26), textHeight);
            tasks.SetBounds(left, top + S(36) + textHeight, S(83), S(28)); snooze.SetBounds(left + S(90), top + S(36) + textHeight, S(83), S(28));
        }
        private Point[] Tail()
        {
            if (side == 1) return new Point[] { new Point(card.Right - S(2), S(30)), new Point(Width, S(38)), new Point(card.Right - S(2), S(46)) };
            if (side == 2) return new Point[] { new Point(card.Left + S(2), S(30)), new Point(0, S(38)), new Point(card.Left + S(2), S(46)) };
            return new Point[] { new Point(tailX - S(9), card.Bottom - S(2)), new Point(tailX, Height), new Point(tailX + S(9), card.Bottom - S(2)) };
        }
        private static GraphicsPath Rounded(Rectangle r, int radius)
        {
            GraphicsPath p = new GraphicsPath(); int d = radius * 2;
            p.AddArc(r.Left, r.Top, d, d, 180, 90); p.AddArc(r.Right - d - 1, r.Top, d, d, 270, 90);
            p.AddArc(r.Right - d - 1, r.Bottom - d - 1, d, d, 0, 90); p.AddArc(r.Left, r.Bottom - d - 1, d, d, 90, 90); p.CloseFigure(); return p;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath p = Rounded(card, S(13))) using (Pen pen = new Pen(Color.FromArgb(157, 174, 139), Math.Max(1, scale))) using (Brush fill = new SolidBrush(BackColor))
            { e.Graphics.FillPath(fill, p); e.Graphics.DrawPath(pen, p); Point[] tail = Tail(); e.Graphics.FillPolygon(fill, tail); e.Graphics.DrawLines(pen, tail); }
        }
        protected override void WndProc(ref Message m) { if (m.Msg == 0x21) { m.Result = (IntPtr)3; return; } base.WndProc(ref m); }
        protected override void Dispose(bool disposing) { if (disposing) { fade.Dispose(); heading.Font.Dispose(); Font.Dispose(); } base.Dispose(disposing); }
    }

    internal sealed class SpeechController : IDisposable
    {
        internal readonly LocalData<SpeechOptions> Settings;
        internal SpeechBubble Bubble { get; private set; }
        private readonly SpeechLibrary lines = new SpeechLibrary();
        private readonly TodoStore todos;
        private readonly Random random = new Random();
        private double nextIdle, nextReminder, lastAuto = -100, resumeAfter, interactionAt;
        private DateTime lastWall = DateTime.UtcNow, dailyDate = DateTime.MinValue;
        private string interaction;
        private bool suppressed;
        internal event EventHandler OpenTasks;
        internal SpeechController(TodoStore tasks, string path)
        { todos = tasks; Settings = new LocalData<SpeechOptions>(path, SpeechOptions.Valid); Reset(0); }
        internal void Reset(double now) { nextIdle = now + IdleDelay(); nextReminder = now + Math.Max(1, Settings.Value.ReminderMinutes) * 60; if (DateTime.Now.TimeOfDay >= TimeSpan.Parse(Settings.Value.Daily)) dailyDate = DateTime.Today; }
        private double IdleDelay()
        { int setting = Settings.Value.Idle; return setting == 3 ? 120 + random.Next(121) : setting == 2 ? 240 + random.Next(241) : 480 + random.Next(421); }
        internal void Interaction(string tag, double now)
        { if (!Settings.Value.Interactions || suppressed) return; interaction = lines.Interaction(tag); interactionAt = now + .45; }
        internal void HideForFullscreen(bool value, double now)
        { suppressed = value; interaction = null; if (Bubble != null) Bubble.Hide(); resumeAfter = now + 30; Reset(now); }
        internal void Tick(double now, bool busy, bool editing, Rectangle pet, bool topMost)
        {
            DateTime utc = DateTime.UtcNow;
            if ((utc - lastWall).TotalSeconds > 4) { interaction = null; resumeAfter = now + 30; Reset(now); if (Bubble != null) Bubble.Hide(); }
            lastWall = utc;
            if (Bubble != null && Bubble.Visible) { Bubble.TopMost = topMost; Bubble.Reanchor(pet); }
            if (suppressed || Settings.Value.IsQuiet(DateTime.Now, utc)) return;
            if (interaction != null && !busy && now >= interactionAt)
            { Say(interaction, false, pet, topMost); interaction = null; lastAuto = now; nextIdle = now + IdleDelay(); return; }
            if (busy || editing) { nextIdle = now + IdleDelay(); return; }
            if (now < resumeAfter || now - lastAuto < 60 || (Bubble != null && Bubble.Visible)) return;
            SpeechOptions s = Settings.Value;
            bool fixedDue = s.ReminderMinutes == -1 && utc >= resumeSnooze && DateTime.Now.TimeOfDay >= TimeSpan.Parse(s.Daily) && dailyDate != DateTime.Today;
            if (todos.PendingCount > 0 && ((s.ReminderMinutes > 0 && now >= nextReminder) || fixedDue))
            { Report(pet, topMost); lastAuto = now; dailyDate = DateTime.Today; nextReminder = now + Math.Max(1, s.ReminderMinutes) * 60; }
            else if (s.Idle > 0 && now >= nextIdle) { Say(lines.Idle(s.Flavor), false, pet, topMost); lastAuto = now; nextIdle = now + IdleDelay(); }
        }
        internal void Report(Rectangle pet, bool topMost)
        {
            string text;
            if (todos.ReadOnly) text = "小本本暂时读不出来，请打开待办看看提示。";
            else
            {
                List<TodoItem> items = todos.Items.FindAll(delegate(TodoItem t) { return !t.Completed; });
                text = items.Count == 0 ? "现在没有待完成事项。\n本蛙负责继续可爱。" : "还有 " + items.Count + " 件小事：";
                for (int i = 0; i < Math.Min(3, items.Count); i++) { string title = items[i].Text; if (title.Length > 18) title = title.Substring(0, 18) + "…"; text += "\n" + (i + 1) + ". " + title; }
                if (items.Count > 3) text += "\n还有 " + (items.Count - 3) + " 件，打开待办查看。";
            }
            Say(text, true, pet, topMost);
        }
        internal void ManualLine(Rectangle pet, bool topMost) { Say(lines.Idle(Settings.Value.Flavor), false, pet, topMost); }
        internal void UtilityMessage(string text, Rectangle pet, bool topMost) { if (!Settings.Value.IsQuiet(DateTime.Now, DateTime.UtcNow)) Say(text, false, pet, topMost); }
        internal void TaskChanged(string kind, string text, Rectangle pet, bool topMost)
        {
            if (Bubble != null && Bubble.Visible && Bubble.IsSummary && kind != "add" && kind != "complete") { Report(pet, topMost); return; }
            if (!Settings.Value.TaskFeedback || suppressed || Settings.Value.IsQuiet(DateTime.Now, DateTime.UtcNow)) return;
            if (kind == "complete") Say((todos.PendingCount == 0 ? lines.Pick("all_done") : lines.Pick("task_done") + "\n还剩 " + todos.PendingCount + " 件待完成。"), false, pet, topMost);
            else if (kind == "add") Say("收到！还有 " + todos.PendingCount + " 件待完成。\n刚记下：" + (text.Length > 28 ? text.Substring(0, 28) + "…" : text), true, pet, topMost);
        }
        private void Say(string text, bool summary, Rectangle pet, bool topMost)
        {
            if (suppressed) return;
            if (Bubble == null) { Bubble = new SpeechBubble(); Bubble.OpenTasks += delegate { if (OpenTasks != null) OpenTasks(this, EventArgs.Empty); }; Bubble.Snoozed += delegate { nextReminder = Double.PositiveInfinity; resumeSnooze = DateTime.UtcNow.AddMinutes(15); }; }
            Bubble.Say(text, summary, summary && todos.PendingCount > 0, summary ? Math.Max(8, Settings.Value.Seconds) : Settings.Value.Seconds, pet, topMost);
        }
        private DateTime resumeSnooze = DateTime.MinValue;
        internal void CheckSnooze(double now) { if (resumeSnooze != DateTime.MinValue && DateTime.UtcNow >= resumeSnooze) { nextReminder = now; resumeSnooze = DateTime.MinValue; } }
        internal bool MuteHour() { SpeechOptions next = Settings.Copy(); next.MutedUntilUtc = DateTime.UtcNow.AddHours(1); bool saved = Settings.Save(next); if (saved && Bubble != null) Bubble.Hide(); return saved; }
        public void Dispose() { if (Bubble != null) { Bubble.Dispose(); Bubble = null; } }
    }

    internal sealed class SpeechSettingsForm : UtilityForm
    {
        internal SpeechSettingsForm(SpeechController speech, Action changed) : base("奶蛙 · 说话设置", 394, 404)
        {
            SpeechOptions value = speech.Settings.Copy();
            CheckBox interaction = new CheckBox { Text = "互动后说一句", Checked = value.Interactions }; interaction.SetBounds(U(20), U(18), U(250), U(28)); Controls.Add(interaction);
            CheckBox feedback = new CheckBox { Text = "添加和完成待办时回应", Checked = value.TaskFeedback }; feedback.SetBounds(U(20), U(50), U(330), U(28)); Controls.Add(feedback);
            ComboBox idle = Choice("闲聊频率", 91, new string[] { "关闭", "安静陪伴 · 8–15 分钟", "刚刚好 · 4–8 分钟", "话痨 · 2–4 分钟" }, value.Idle);
            int[] minutes = { 0, 15, 30, 60, -1 };
            ComboBox reminder = Choice("待办汇报", 134, new string[] { "只手动", "每15分钟", "每30分钟", "每60分钟", "每天固定时间" }, Array.IndexOf(minutes, value.ReminderMinutes));
            DateTimePicker daily = TimeAt(value.Daily, 181, 190); LabelAt("固定时刻", 20, 190, 140, 28);
            ComboBox flavor = Choice("语录口味", 231, new string[] { "可爱和抽象都有", "抽象多一点", "可爱多一点" }, value.Flavor);
            LabelAt("停留秒数", 20, 278, 140, 26); NumericUpDown seconds = new NumericUpDown { Minimum = 4, Maximum = 15, Value = value.Seconds }; seconds.SetBounds(U(181), U(276), U(190), U(28)); Controls.Add(seconds);
            CheckBox quiet = new CheckBox { Text = "安静时段", Checked = value.QuietHours }; quiet.SetBounds(U(20), U(316), U(130), U(28)); Controls.Add(quiet);
            DateTimePicker start = TimeAt(value.QuietFrom, 160, 315), end = TimeAt(value.QuietTo, 278, 315); LabelAt("至", 250, 318, 25, 25);
            ButtonAt("保存设置", 242, 359, 130, delegate
            {
                value.Interactions = interaction.Checked; value.TaskFeedback = feedback.Checked; value.Idle = idle.SelectedIndex; value.ReminderMinutes = minutes[reminder.SelectedIndex]; value.Daily = daily.Value.ToString("HH:mm"); value.Flavor = flavor.SelectedIndex; value.Seconds = (int)seconds.Value; value.QuietHours = quiet.Checked; value.QuietFrom = start.Value.ToString("HH:mm"); value.QuietTo = end.Value.ToString("HH:mm"); value.MutedUntilUtc = speech.Settings.Value.MutedUntilUtc;
                if (speech.Settings.Save(value)) { changed(); Hide(); } else MessageBox.Show(this, speech.Settings.Error, "设置未保存");
            });
        }
        private ComboBox Choice(string label, int y, string[] values, int selected)
        { LabelAt(label, 20, y + 3, 145, 28); ComboBox box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList }; box.Items.AddRange(values); box.SelectedIndex = Math.Max(0, selected); box.SetBounds(U(181), U(y), U(190), U(28)); Controls.Add(box); return box; }
        private DateTimePicker TimeAt(string text, int x, int y)
        { DateTimePicker time = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm", ShowUpDown = true, Value = DateTime.Today.Add(TimeSpan.Parse(text)) }; time.SetBounds(U(x), U(y), U(x == 181 ? 190 : 94), U(28)); Controls.Add(time); return time; }
    }
}
