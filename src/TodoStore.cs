// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace NaiwaCompanion
{
    internal sealed class TodoItem
    {
        public string Id, Text, CreatedUtc, CompletedUtc;
        public bool Completed;
        internal TodoItem Copy() { return (TodoItem)MemberwiseClone(); }
    }

    internal sealed class TodoDocument
    {
        public int Version = 1;
        public List<TodoItem> Items = new List<TodoItem>();
        public bool PanelOpen, CompletedExpanded;
        public int X = int.MinValue, Y = int.MinValue;
    }

    // Changes become visible only after the replacement file has been written successfully.
    internal sealed class TodoStore
    {
        internal const int TextLimit = 120, ItemLimit = 200;
        private readonly string file;
        private TodoDocument data = new TodoDocument();
        private List<TodoItem> undo;
        private bool recovered;
        internal bool ReadOnly { get; private set; }
        internal string Notice { get; private set; }
        internal string LastError { get; private set; }
        internal event EventHandler Changed;
        internal string LastKind = "change", LastText = "";
        internal TodoDocument View { get { return data; } }
        internal List<TodoItem> Items { get { return Copy(data.Items); } }
        internal int PendingCount { get { return data.Items.FindAll(delegate(TodoItem t) { return !t.Completed; }).Count; } }
        internal bool CanUndo { get { return undo != null && !ReadOnly; } }
        internal static string DefaultPath { get { return Path.Combine(Preferences.DirectoryPath, "todos.json"); } }

        internal TodoStore(string path)
        {
            file = path; Notice = LastError = "";
            if (file == null) return; // In-memory store for isolated tests.
            bool primaryExists = File.Exists(file), backupExists = File.Exists(file + ".bak");
            if (!primaryExists && !backupExists) return;
            try { data = Read(file); return; }
            catch (NotSupportedException)
            { ReadOnly = true; Notice = "待办文件来自更新版本，请使用新版程序打开；原文件已保留。"; return; }
            catch (Exception) { }
            try
            {
                TodoDocument backup = Read(file + ".bak");
                if (primaryExists) File.Copy(file, file + ".damaged-" + Guid.NewGuid().ToString("N") + ".json");
                data = backup; recovered = true;
                Notice = "已从上一次备份恢复；异常文件已保留。";
            }
            catch (Exception)
            {
                ReadOnly = true;
                Notice = "待办文件暂时无法读取，原文件已保留。请先查看数据文件夹。";
            }
        }

        private static TodoDocument Read(string path)
        {
            if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("待办文件过大。");
            string json = File.ReadAllText(path, Encoding.UTF8);
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            Dictionary<string, object> shape = serializer.DeserializeObject(json) as Dictionary<string, object>;
            if (shape == null || !shape.ContainsKey("Version") || !shape.ContainsKey("Items"))
                throw new InvalidDataException("待办文件缺少必要字段。");
            TodoDocument loaded = serializer.Deserialize<TodoDocument>(json);
            if (loaded != null && loaded.Version > 1) throw new NotSupportedException("待办文件版本较新。");
            if (loaded == null || loaded.Version != 1 || loaded.Items == null || loaded.Items.Count > ItemLimit)
                throw new InvalidDataException("待办文件格式无法识别。");
            HashSet<string> ids = new HashSet<string>();
            foreach (TodoItem item in loaded.Items)
                if (item == null || String.IsNullOrWhiteSpace(item.Id) || !ids.Add(item.Id) ||
                    String.IsNullOrWhiteSpace(item.Text) || item.Text.Length > TextLimit)
                    throw new InvalidDataException("待办条目不完整。");
            return loaded;
        }

        private static List<TodoItem> Copy(List<TodoItem> source)
        { return source.ConvertAll(delegate(TodoItem item) { return item.Copy(); }); }
        private TodoDocument WithItems(List<TodoItem> items)
        {
            return new TodoDocument { Items = items, PanelOpen = data.PanelOpen, CompletedExpanded = data.CompletedExpanded, X = data.X, Y = data.Y };
        }
        private bool Persist(TodoDocument next)
        {
            LastError = "";
            if (ReadOnly) { LastError = Notice; return false; }
            try
            {
                if (file != null)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file)));
                    string temporary = file + ".tmp";
                    byte[] bytes = new UTF8Encoding(false).GetBytes(new JavaScriptSerializer().Serialize(next));
                    using (FileStream stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                    { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
                    if (File.Exists(file)) File.Replace(temporary, file, recovered ? null : file + ".bak");
                    else File.Move(temporary, file);
                    recovered = false;
                }
                data = next; return true;
            }
            catch (Exception e) { LastError = "保存失败，内容尚未更改：" + e.Message; return false; }
        }
        private bool Commit(List<TodoItem> items, string kind = "change", string text = "")
        {
            List<TodoItem> before = Copy(data.Items);
            if (!Persist(WithItems(items))) return false;
            undo = before; LastKind = kind; LastText = text;
            if (Changed != null) Changed(this, EventArgs.Empty);
            return true;
        }
        private bool ValidText(string text)
        {
            LastError = "";
            if (String.IsNullOrWhiteSpace(text)) LastError = "先写下一件想做的事吧。";
            else if (text.Trim().Length > TextLimit) LastError = "每条最多 " + TextLimit + " 个字。";
            return LastError.Length == 0;
        }
        internal bool Add(string text)
        {
            if (!ValidText(text)) return false;
            if (data.Items.Count >= ItemLimit) { LastError = "待办已满，请先清理一些已完成事项。"; return false; }
            List<TodoItem> items = Items;
            items.Add(new TodoItem { Id = Guid.NewGuid().ToString("N"), Text = text.Trim(), CreatedUtc = DateTime.UtcNow.ToString("o") });
            return Commit(items, "add", text.Trim());
        }
        internal bool Edit(string id, string text)
        {
            if (!ValidText(text)) return false;
            List<TodoItem> items = Items; TodoItem item = items.Find(delegate(TodoItem t) { return t.Id == id; });
            if (item == null) return false;
            item.Text = text.Trim(); return Commit(items, "edit", item.Text);
        }
        internal bool Toggle(string id)
        {
            List<TodoItem> items = Items; TodoItem item = items.Find(delegate(TodoItem t) { return t.Id == id; });
            if (item == null) return false;
            item.Completed = !item.Completed; item.CompletedUtc = item.Completed ? DateTime.UtcNow.ToString("o") : null;
            return Commit(items, item.Completed ? "complete" : "restore", item.Text);
        }
        internal bool Delete(string id)
        {
            List<TodoItem> items = Items;
            if (items.RemoveAll(delegate(TodoItem t) { return t.Id == id; }) == 0) return false;
            return Commit(items);
        }
        internal bool ClearCompleted()
        {
            List<TodoItem> items = Items;
            if (items.RemoveAll(delegate(TodoItem t) { return t.Completed; }) == 0) return false;
            return Commit(items);
        }
        internal bool Undo()
        {
            if (!CanUndo || !Persist(WithItems(Copy(undo)))) return false;
            undo = null; LastKind = "undo"; LastText = ""; if (Changed != null) Changed(this, EventArgs.Empty); return true;
        }
        internal bool SaveView(bool open, bool expanded, Point location)
        {
            TodoDocument next = WithItems(Items);
            next.PanelOpen = open; next.CompletedExpanded = expanded; next.X = location.X; next.Y = location.Y;
            return Persist(next);
        }
    }

}
