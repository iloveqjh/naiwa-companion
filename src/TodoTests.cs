// SPDX-License-Identifier: MIT
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace NaiwaCompanion
{
    internal static class TodoTests
    {
        internal static void Run(string output, Action<bool, string> check)
        {
            string directory = Path.Combine(output, "todo-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "todos.json");
            TodoStore store = new TodoStore(path);
            check(store.Items.Count == 0 && !store.ReadOnly, "todo starts empty without sample tasks");
            check(!store.Add("  ") && !store.Add(new String('字', 121)) && !File.Exists(path), "invalid todo input never creates or changes storage");
            check(store.Add("  整理设计稿 🐸  ") && store.Add("核对明天要带的东西"), "todo saves Chinese and Unicode text immediately");
            string first = store.Items[0].Id, second = store.Items[1].Id;
            TodoStore reopened = new TodoStore(path);
            check(reopened.Items[0].Id == first && reopened.Items[0].Text == "整理设计稿 🐸" && reopened.PendingCount == 2, "todo restart preserves text identity and insertion order");
            check(store.Toggle(first) && store.PendingCount == 1 && store.Items[0].CompletedUtc != null, "completed todo keeps its content and completion time");
            check(store.Undo() && store.PendingCount == 2 && store.Items[0].CompletedUtc == null && !store.CanUndo, "undo restores the exact prior completion state once");
            check(store.Edit(first, "整理桌面文件") && store.Delete(second) && store.Undo() && store.Items[1].Id == second, "delete undo restores item order while retaining earlier edits");
            store.Toggle(first); store.Toggle(second);
            check(store.ClearCompleted() && store.Items.Count == 0 && store.Undo() && store.Items.Count == 2 && store.PendingCount == 0, "clear completed supports restoring every removed item");
            store.SaveView(true, true, new Point(-800, 200));
            reopened = new TodoStore(path);
            check(reopened.View.PanelOpen && reopened.View.CompletedExpanded && reopened.View.X == -800 && reopened.View.Y == 200, "panel visibility folding and negative-monitor coordinates survive restart");
            byte[] original = File.ReadAllBytes(path);
            using (FileStream locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                check(!store.Add("不能丢失的输入") && store.Items.Count == 2 && store.LastError.Contains("保存失败"), "failed replacement does not accept an unsaved task");
            check(Convert.ToBase64String(File.ReadAllBytes(path)) == Convert.ToBase64String(original), "failed write leaves the previously saved file byte-identical");
            store.Add("最近新增的一件事");
            File.WriteAllText(path, "{ interrupted");
            TodoStore recovered = new TodoStore(path);
            check(!recovered.ReadOnly && recovered.Items.Count == 2 && recovered.Notice.Contains("备份") && Directory.GetFiles(directory, "*.damaged-*.json").Length == 1, "corrupt primary recovers its valid backup and preserves the broken original");
            check(recovered.Add("恢复后仍然能保存") && new TodoStore(path).Items.Count == 3, "recovered data can be atomically saved and reopened");
            string damaged = Path.Combine(directory, "damaged.json"); File.WriteAllText(damaged, "broken");
            TodoStore blocked = new TodoStore(damaged);
            check(blocked.ReadOnly && !blocked.Add("不覆盖原文件") && File.ReadAllText(damaged) == "broken", "unrecoverable storage remains protected instead of being silently overwritten");
            string future = Path.Combine(directory, "future.json"); File.WriteAllText(future, "{\"Version\":99,\"Items\":[]}");
            File.Copy(path, future + ".bak");
            check(new TodoStore(future).ReadOnly, "future todo formats are protected from an older reader");
            string incomplete = Path.Combine(directory, "incomplete.json"); File.WriteAllText(incomplete, "{}");
            check(new TodoStore(incomplete).ReadOnly, "missing schema fields cannot silently become an empty list");
            string orphan = Path.Combine(directory, "orphan.json"); File.Copy(path, orphan + ".bak");
            check(new TodoStore(orphan).Items.Count == 3, "missing primary can recover an existing backup");
            TodoStore capacity = new TodoStore(null);
            for (int i = 0; i < TodoStore.ItemLimit; i++) capacity.Add("第 " + i + " 件事");
            check(!capacity.Add("超出上限") && capacity.Items.Count == TodoStore.ItemLimit, "todo capacity rejects overflow while keeping all existing items");
            TodoStore memory = new TodoStore(null);
            using (TodoForm panel = new TodoForm(memory, false))
            {
                panel.Location = new Point(-20000, -20000); panel.Show(); Application.DoEvents();
                Snapshot(panel, Path.Combine(output, "todo-empty.png"));
                panel.Input.Text = "整理桌面上的资料";
                check(panel.AddFromInput() && panel.Input.Text == "" && memory.PendingCount == 1, "native card adds input and clears it only after success");
                panel.Input.Text = " ";
                check(!panel.AddFromInput() && panel.Input.Text == " " && panel.StatusText.Contains("先写"), "native card retains rejected input and explains the problem");
                panel.Input.Text = "";
                panel.Input.Text = "新增草稿"; panel.EditItem(memory.Items[0]); panel.Input.Text = "没有保存的修改"; panel.CancelEdit();
                check(panel.Input.Text == "新增草稿" && memory.Items[0].Text == "整理桌面上的资料", "cancel editing preserves both the saved task and the unfinished add draft");
                panel.EditItem(memory.Items[0]); panel.Input.Text = "整理桌面上的资料"; panel.AddFromInput(); panel.Input.Clear();
                memory.Add("完成奶蛙的新功能 🐸"); memory.Add("准备明天要带的东西");
                string id = memory.Items[1].Id; panel.ToggleItem(id, false);
                Snapshot(panel, Path.Combine(output, "todo-filled.png"));
                panel.ToggleCompleted(); Snapshot(panel, Path.Combine(output, "todo-completed.png"));
                check(panel.Expanded && memory.PendingCount == 2 && panel.StatusText.Contains("完成"), "native completion updates count and can expand completed items");
                panel.Input.Text = "还没按添加的草稿";
                panel.SetSuspended(true);
                check(!panel.Visible && panel.Suspended && memory.View.PanelOpen, "fullscreen suspension hides the card without recording a user collapse");
                panel.SetSuspended(false);
                check(panel.Visible && panel.Input.Text == "还没按添加的草稿", "fullscreen recovery preserves the draft in the same panel");
                for (int i = 0; i < 16; i++) memory.Add("列表滚动检查 " + i + "：这是一条较长的事项，完整内容可以通过悬停查看。 ");
                Application.DoEvents();
                Panel scrolling = null;
                foreach (Control control in panel.Controls) if (control.AccessibleName == "待办列表") scrolling = (Panel)control;
                check(scrolling != null && scrolling.VerticalScroll.Visible && !scrolling.HorizontalScroll.Visible, "long todo lists scroll vertically without a horizontal overflow");
                foreach (Control row in scrolling.Controls)
                    foreach (Control child in row.Controls) if (child.Right > row.ClientSize.Width || child.Left < 0) throw new Exception("todo row controls clipped horizontally");
                check(true, "long-list row actions remain within the card at the current system DPI");
                panel.Close();
                check(!panel.Visible && !panel.IsDisposed && !memory.View.PanelOpen, "closing the card collapses it while the pet can keep running");
                panel.Shutdown();
            }
            using (Theme demo = new Theme(null)) using (PetForm pet = new PetForm(demo, new Preferences { Ephemeral = true }))
            {
                pet.Location = new Point(-20000, -20000); pet.Show(); Application.DoEvents();
                pet.ShowTodos(false); pet.SetHidden(true);
                check(pet.TodoPanel.Suspended && !pet.TodoPanel.Visible, "pet fullscreen lifecycle also hides its todo panel");
                pet.SetHidden(false);
                check(pet.TodoPanel.Visible && !pet.TodoPanel.Suspended, "pet fullscreen recovery restores an open todo panel");
                pet.TodoPanel.Close(); pet.SetHidden(true); pet.SetHidden(false);
                check(!pet.TodoPanel.Visible, "fullscreen recovery never reopens a manually collapsed todo panel");
                pet.Close();
            }
        }
        private static void Snapshot(Form panel, string path)
        {
            using (Bitmap image = new Bitmap(panel.Width, panel.Height))
            { panel.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size)); image.Save(path, ImageFormat.Png); }
        }
    }
}
