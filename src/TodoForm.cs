// SPDX-License-Identifier: MIT
using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace NaiwaCompanion
{
    internal sealed class TodoForm : Form
    {
        private static readonly Color Ink = Color.FromArgb(48, 66, 52), Green = Color.FromArgb(66, 112, 75);
        private readonly TodoStore store;
        private readonly TextBox input = new TextBox();
        private readonly Button add = new Button(), undo = new Button(), more = new Button(), cancelEdit = new Button();
        private readonly Label count = new Label(), status = new Label();
        private Label inputPrompt;
        private string editingId, addDraft = "";
        private readonly Panel list = new Panel();
        private readonly ToolTip tips = new ToolTip();
        private readonly Font heading, small, itemFont, doneFont;
        private bool expanded, suspended, shuttingDown, placed, restoring;
        private readonly float scale;
        internal event EventHandler Collapsed;
        internal TextBox Input { get { return input; } }
        internal bool Expanded { get { return expanded; } }
        internal string StatusText { get { return status.Text; } }
        internal bool Suspended { get { return suspended; } }
        protected override bool ShowWithoutActivation { get { return restoring; } }

        internal TodoForm(TodoStore todos, bool topMost)
        {
            store = todos; expanded = store.View.CompletedExpanded;
            using (Graphics g = CreateGraphics()) scale = g.DpiX / 96f;
            Font = new Font("Microsoft YaHei UI", 10f);
            heading = new Font(Font.FontFamily, 18f, FontStyle.Bold);
            small = new Font(Font.FontFamily, 9f);
            itemFont = new Font(Font.FontFamily, 10f);
            doneFont = new Font(Font.FontFamily, 10f, FontStyle.Strikeout);
            AutoScaleMode = AutoScaleMode.None;
            Text = "奶蛙 · 小待办"; Name = "NaiwaTodo";
            FormBorderStyle = FormBorderStyle.FixedToolWindow; MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
            TopMost = topMost; BackColor = Color.FromArgb(249, 250, 244); ForeColor = Ink;
            ClientSize = new Size(S(352), S(480));

            Label("小待办", heading, 18, 13, 186, 36);
            count.Font = small; count.TextAlign = ContentAlignment.MiddleRight; count.ForeColor = Green;
            count.SetBounds(S(198), S(22), S(134), S(24)); Controls.Add(count);
            Label("先做好眼前这一件。", small, 20, 53, 310, 24).ForeColor = Color.FromArgb(113, 124, 110);
            inputPrompt = Label("写下一件小事", small, 20, 89, 220, 22);
            StyleButton(cancelEdit, "取消修改", false); cancelEdit.Font = small;
            cancelEdit.SetBounds(S(251), S(83), S(85), S(29)); cancelEdit.Visible = false;
            cancelEdit.Click += delegate { CancelEdit(); }; Controls.Add(cancelEdit);
            Panel inputBorder = new Panel { BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
            inputBorder.SetBounds(S(20), S(116), S(246), S(34)); Controls.Add(inputBorder);
            input.BorderStyle = BorderStyle.None; input.MaxLength = TodoStore.TextLimit;
            input.SetBounds(S(8), S(5), S(228), S(25)); input.AccessibleName = "新增待办内容";
            inputBorder.Controls.Add(input);
            StyleButton(add, "添加", true); add.SetBounds(S(274), S(116), S(58), S(34));
            add.AccessibleName = "添加待办"; Controls.Add(add);
            add.Click += delegate { AddFromInput(); };
            input.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; AddFromInput(); }
                if (e.KeyCode == Keys.Escape && editingId != null) { e.SuppressKeyPress = true; CancelEdit(); }
            };
            list.SetBounds(S(14), S(164), S(324), S(242)); list.AutoScroll = true;
            list.AccessibleName = "待办列表"; Controls.Add(list);
            list.Resize += delegate { LayoutRows(); };

            status.SetBounds(S(20), S(414), S(312), S(24)); status.Font = small;
            status.AutoEllipsis = true; status.AccessibleName = "待办保存状态"; Controls.Add(status);
            StyleButton(undo, "撤销", false); undo.SetBounds(S(18), S(442), S(60), S(28)); Controls.Add(undo);
            undo.Click += delegate { Apply(store.Undo(), "已撤销上一步。"); };
            Label("Enter 添加 · 双击修改", small, 88, 445, 203, 24).ForeColor = Color.FromArgb(113, 124, 110);
            StyleButton(more, "⋯", false); more.SetBounds(S(298), S(441), S(36), S(29));
            more.AccessibleName = "更多待办操作"; Controls.Add(more); more.Click += delegate { ShowMore(); };
            tips.SetToolTip(more, "清空已完成 / 打开数据文件夹");
            store.Changed += StoreChanged;
            Shown += delegate { if (!restoring) input.Focus(); };
            ResizeEnd += delegate { if (placed && Visible) SaveView(true); };
            RefreshItems();
            if (!String.IsNullOrEmpty(store.Notice)) SetStatus(store.Notice, true);
        }
        private int S(int value) { return (int)Math.Round(value * scale); }
        private Label Label(string text, Font font, int x, int y, int width, int height)
        {
            Label label = new Label { Text = text, Font = font, ForeColor = Ink, BackColor = Color.Transparent };
            label.SetBounds(S(x), S(y), S(width), S(height)); Controls.Add(label); return label;
        }
        private static void StyleButton(Button button, string text, bool primary)
        {
            button.Text = text; button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0; button.Cursor = Cursors.Hand;
            button.BackColor = primary ? Green : Color.Transparent; button.ForeColor = primary ? Color.White : Green;
            button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(52, 95, 61) : Color.FromArgb(230, 238, 226);
        }
        internal void OpenNear(Rectangle pet, bool activate)
        {
            if (!placed)
            {
                Point desired = store.View.X == int.MinValue || store.View.Y == int.MinValue
                    ? new Point(pet.Left - Width - S(12), pet.Top + S(16)) : new Point(store.View.X, store.View.Y);
                Rectangle area = Screen.FromRectangle(pet).WorkingArea;
                if (store.View.X == int.MinValue && desired.X < area.Left) desired.X = pet.Right + S(12);
                Location = PetForm.Clamp(desired, Size); placed = true;
            }
            else Location = PetForm.Clamp(Location, Size);
            suspended = false; restoring = !activate;
            if (!Visible) Show();
            if (activate) { Activate(); input.Focus(); }
            restoring = false; SaveView(true);
        }
        internal void SetSuspended(bool value)
        {
            if (value)
            {
                if (!Visible) return;
                SaveView(true); suspended = true; Hide();
            }
            else if (suspended)
            {
                suspended = false; restoring = true; Location = PetForm.Clamp(Location, Size); Show();
                restoring = false;
            }
        }
        internal void Shutdown()
        {
            SaveView(Visible || suspended); shuttingDown = true; Close();
        }
        private void SaveView(bool open)
        {
            if (store.ReadOnly) return;
            if (!store.SaveView(open, expanded, Location)) SetStatus(store.LastError, true);
        }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!shuttingDown && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true; SaveView(false); suspended = false; Hide();
                if (Collapsed != null) Collapsed(this, EventArgs.Empty);
            }
            base.OnFormClosing(e);
        }
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == 0x7E && placed) { Location = PetForm.Clamp(Location, Size); SaveView(Visible || suspended); }
        }
        internal bool AddFromInput()
        {
            bool editing = editingId != null;
            bool result = editing ? store.Edit(editingId, input.Text) : store.Add(input.Text);
            if (result)
            {
                if (editing) EndEdit(); else input.Clear();
                SetStatus(editing ? "修改已保存。" : "已记下，慢慢来。", false); input.Focus();
            }
            else SetStatus(store.LastError, true);
            return result;
        }
        internal void ToggleItem(string id, bool wasCompleted)
        { Apply(store.Toggle(id), wasCompleted ? "已放回待办。" : "完成一件，真好。可以撤销。 "); }
        internal void DeleteItem(string id) { Apply(store.Delete(id), "已删除，可以撤销。"); }
        internal void ToggleCompleted()
        { expanded = !expanded; RefreshItems(); SaveView(Visible || suspended); }
        private void StoreChanged(object sender, EventArgs e) { RefreshItems(); }
        private void Apply(bool success, string message)
        { SetStatus(success ? message : (String.IsNullOrEmpty(store.LastError) ? "没有可操作的事项。" : store.LastError), !success); }
        private void SetStatus(string message, bool error)
        { status.Text = message; status.AccessibleName = "待办状态：" + message; status.ForeColor = error ? Color.FromArgb(154, 73, 50) : Green; tips.SetToolTip(status, message); }

        internal void RefreshItems()
        {
            Point scroll = list.AutoScrollPosition;
            list.SuspendLayout(); list.AutoScrollPosition = Point.Empty;
            while (list.Controls.Count > 0) list.Controls[0].Dispose();
            int pending = store.PendingCount, completed = store.Items.Count - pending;
            count.Text = pending == 0 ? "暂时都完成啦" : pending + " 件待完成";
            input.Enabled = add.Enabled = !store.ReadOnly; undo.Enabled = store.CanUndo;
            if (editingId != null && !store.Items.Exists(delegate(TodoItem item) { return item.Id == editingId; })) EndEdit();
            if (pending == 0)
            {
                Label empty = new Label { Text = completed > 0 ? "都做完了，休息一下吧。" : "把想做的事写下来，\n奶蛙陪你一件件完成。", Font = itemFont,
                    ForeColor = Color.FromArgb(120, 132, 116), TextAlign = ContentAlignment.MiddleCenter, Height = S(98) };
                list.Controls.Add(empty);
            }
            foreach (TodoItem item in store.Items) if (!item.Completed) AddRow(item);
            if (completed > 0)
            {
                Button section = new Button { Height = S(38), TextAlign = ContentAlignment.MiddleLeft };
                StyleButton(section, (expanded ? "▾" : "▸") + "  已完成 " + completed + (expanded ? " · 收起" : " · 展开"), false);
                section.AccessibleName = "展开或收起已完成";
                section.Click += delegate { ToggleCompleted(); }; list.Controls.Add(section);
                if (expanded) foreach (TodoItem item in store.Items) if (item.Completed) AddRow(item);
            }
            LayoutRows(); list.ResumeLayout(); list.AutoScrollPosition = new Point(-scroll.X, -scroll.Y);
            if (String.IsNullOrEmpty(status.Text)) SetStatus("自动保存在这台电脑上。", false);
        }
        private void LayoutRows()
        {
            int y = 0, width = Math.Max(S(80), list.ClientSize.Width - S(12) - SystemInformation.VerticalScrollBarWidth);
            foreach (Control row in list.Controls) { row.SetBounds(S(6), y, width, row.Height); y += row.Height + S(7); }
            list.AutoScrollMinSize = new Size(0, Math.Max(0, y));
        }
        private void AddRow(TodoItem item)
        {
            Panel row = new Panel { BackColor = item.Completed ? Color.FromArgb(240, 244, 235) : Color.White,
                Width = Math.Max(S(80), list.ClientSize.Width - S(12) - SystemInformation.VerticalScrollBarWidth), Height = S(65) };
            CheckBox check = new CheckBox { Checked = item.Completed, AutoSize = false, AccessibleName = (item.Completed ? "恢复待办：" : "完成待办：") + item.Text,
                Enabled = !store.ReadOnly };
            check.SetBounds(S(10), S(23), S(22), S(22)); row.Controls.Add(check);
            check.CheckedChanged += delegate { ToggleItem(item.Id, item.Completed); };
            Label text = new Label { Text = item.Text, UseMnemonic = false, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft,
                Font = item.Completed ? doneFont : itemFont, ForeColor = item.Completed ? Color.FromArgb(128, 142, 124) : Ink, Cursor = Cursors.IBeam };
            text.SetBounds(S(38), S(7), row.Width - S(76), S(51)); text.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
            row.Controls.Add(text); tips.SetToolTip(text, item.Text);
            text.DoubleClick += delegate { if (!store.ReadOnly) EditItem(item); };
            Button options = new Button(); StyleButton(options, "⋯", false);
            options.SetBounds(row.Width - S(34), S(19), S(28), S(28)); options.Anchor = AnchorStyles.Right | AnchorStyles.Top;
            options.AccessibleName = "操作待办：" + item.Text; options.Enabled = !store.ReadOnly; row.Controls.Add(options);
            options.Click += delegate
            {
                ContextMenuStrip menu = new ContextMenuStrip { Font = Font };
                menu.Items.Add("修改内容…", null, delegate { EditItem(item); });
                menu.Items.Add(item.Completed ? "放回待办" : "标记完成", null, delegate { ToggleItem(item.Id, item.Completed); });
                menu.Items.Add("删除", null, delegate { DeleteItem(item.Id); });
                menu.Closed += delegate { BeginInvoke((MethodInvoker)delegate { menu.Dispose(); }); };
                menu.Show(options, new Point(0, options.Height));
            };
            list.Controls.Add(row);
        }
        internal void EditItem(TodoItem item)
        {
            if (editingId == null) addDraft = input.Text;
            editingId = item.Id; input.Text = item.Text;
            input.AccessibleName = "修改待办内容"; add.AccessibleName = "保存修改";
            add.Text = "保存"; inputPrompt.Text = "修改这件小事"; cancelEdit.Visible = true;
            SetStatus("Enter 保存 · Esc 取消修改。", false); input.Focus(); input.SelectAll();
        }
        private void EndEdit()
        { editingId = null; input.Text = addDraft; addDraft = ""; add.Text = "添加"; input.AccessibleName = "新增待办内容"; add.AccessibleName = "添加待办"; inputPrompt.Text = "写下一件小事"; cancelEdit.Visible = false; }
        internal void CancelEdit()
        { EndEdit(); SetStatus("已取消修改。", false); input.Focus(); }
        private void ShowMore()
        {
            ContextMenuStrip menu = new ContextMenuStrip { Font = Font };
            ToolStripItem clear = menu.Items.Add("清空已完成（可撤销）", null, delegate { Apply(store.ClearCompleted(), "已清空完成事项，可以撤销。"); });
            clear.Enabled = !store.ReadOnly && store.Items.Exists(delegate(TodoItem item) { return item.Completed; });
            menu.Items.Add("打开数据文件夹", null, delegate
            { try { System.IO.Directory.CreateDirectory(Preferences.DirectoryPath); Process.Start(Preferences.DirectoryPath); } catch (Exception e) { SetStatus(e.Message, true); } });
            menu.Closed += delegate { BeginInvoke((MethodInvoker)delegate { menu.Dispose(); }); };
            menu.Show(more, new Point(0, more.Height));
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            { store.Changed -= StoreChanged; tips.Dispose(); heading.Dispose(); small.Dispose(); itemFont.Dispose(); doneFont.Dispose(); Font.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
