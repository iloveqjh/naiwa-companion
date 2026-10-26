// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;

namespace NaiwaCompanion
{
    internal sealed class Snippet
    {
        public string Id, Title, Body;
        public override string ToString() { return Title; }
    }
    internal sealed class SnippetDocument
    {
        public int Version = 1;
        public List<Snippet> Items = new List<Snippet>();
    }
    internal sealed class SnippetStore
    {
        internal readonly LocalData<SnippetDocument> Data;
        private SnippetDocument undo;
        internal SnippetStore(string file)
        {
            Data = new LocalData<SnippetDocument>(file, delegate(SnippetDocument d)
            {
                if (d.Version != 1 || d.Items == null || d.Items.Count > 200) return false;
                HashSet<string> ids = new HashSet<string>();
                foreach (Snippet item in d.Items)
                    if (item == null || String.IsNullOrWhiteSpace(item.Id) || !ids.Add(item.Id) || String.IsNullOrWhiteSpace(item.Title) || item.Title.Length > 80 || String.IsNullOrWhiteSpace(item.Body) || item.Body.Length > 20000) return false;
                return true;
            });
        }
        internal bool Save(string id, string title, string body)
        {
            if (String.IsNullOrWhiteSpace(body) || body.Length > 20000) { Data.Error = "请填写收藏内容，最多 20000 个字符。"; return false; }
            title = (title ?? "").Trim();
            if (title.Length == 0) { title = body.Trim().Replace("\r", " ").Replace("\n", " "); if (title.Length > 30) title = title.Substring(0, 30); }
            if (title.Length > 80) { Data.Error = "标题最多 80 个字符。"; return false; }
            SnippetDocument next = Data.Copy(), before = Data.Copy();
            Snippet item = next.Items.Find(delegate(Snippet s) { return s.Id == id; });
            if (item == null) { if (next.Items.Count >= 200) { Data.Error = "收藏已满，请先整理现有收藏。"; return false; } item = new Snippet { Id = Guid.NewGuid().ToString("N") }; next.Items.Add(item); }
            item.Title = title; item.Body = body;
            if (!Data.Save(next)) return false; undo = before; return true;
        }
        internal bool Delete(string id)
        {
            SnippetDocument next = Data.Copy(), before = Data.Copy();
            if (next.Items.RemoveAll(delegate(Snippet s) { return s.Id == id; }) == 0) return false;
            if (!Data.Save(next)) return false; undo = before; return true;
        }
        internal bool Undo() { if (undo == null || !Data.Save(undo)) return false; undo = null; return true; }
    }
    internal sealed class SnippetForm : UtilityForm
    {
        private readonly SnippetStore store;
        private readonly TextBox search = new TextBox(), title = new TextBox(), body = new TextBox();
        private readonly ListBox list = new ListBox();
        private readonly Label status;
        private string selectedId;
        private bool loading, dirty;
        internal SnippetForm(SnippetStore snippets) : base("奶蛙 · 文字收藏", 620, 432)
        {
            store = snippets;
            LabelAt("文字收藏", 18, 14, 200, 26);
            LabelAt("保存常用文字，复制后到任意应用粘贴。", 18, 44, 580, 24);
            search.SetBounds(U(18), U(82), U(196), U(28)); search.AccessibleName = "搜索收藏"; Controls.Add(search);
            list.SetBounds(U(18), U(120), U(196), U(246)); list.AccessibleName = "文字收藏列表"; Controls.Add(list);
            LabelAt("标题（可留空）", 232, 80, 300, 24);
            title.SetBounds(U(232), U(108), U(368), U(28)); title.MaxLength = 80; title.AccessibleName = "收藏标题"; Controls.Add(title);
            body.SetBounds(U(232), U(148), U(368), U(218)); body.Multiline = true; body.AcceptsReturn = true; body.ScrollBars = ScrollBars.Vertical; body.MaxLength = 20000; body.AccessibleName = "收藏内容"; Controls.Add(body);
            status = LabelAt(store.Data.Notice.Length > 0 ? store.Data.Notice : "仅保存在本机；按保存后收藏。", 18, 402, 580, 24);
            ButtonAt("新建", 18, 374, 58, delegate { NewDraft(""); });
            ButtonAt("删除", 84, 374, 58, delegate { if (selectedId != null && store.Delete(selectedId)) { selectedId = null; dirty = false; Rebuild(); LoadItem(null); status.Text = "已删除，可撤销。"; } else status.Text = store.Data.Error; });
            ButtonAt("撤销", 150, 374, 64, delegate { if (store.Undo()) { dirty = false; Rebuild(); LoadItem(null); status.Text = "已撤销上一步。"; } else status.Text = store.Data.Error.Length > 0 ? store.Data.Error : "没有可撤销的修改。"; });
            ButtonAt("收藏剪贴板", 232, 374, 112, delegate { CaptureClipboard(); });
            ButtonAt("保存", 352, 374, 112, delegate { SaveCurrent(); });
            ButtonAt("复制文字", 476, 374, 124, delegate { CopyCurrent(); });
            title.TextChanged += delegate { if (!loading) dirty = true; }; body.TextChanged += delegate { if (!loading) dirty = true; };
            search.TextChanged += delegate { Rebuild(); };
            list.SelectedIndexChanged += delegate
            {
                if (loading || list.SelectedItem == null) return;
                Snippet item = (Snippet)list.SelectedItem;
                if (item.Id == selectedId) return;
                if (!DiscardDraft()) { Rebuild(); return; }
                LoadItem(item);
            };
            Rebuild(); if (store.Data.ReadOnly) title.ReadOnly = body.ReadOnly = true;
        }
        private bool DiscardDraft()
        {
            if (!dirty) return true;
            DialogResult result = MessageBox.Show(this, "当前内容还没保存。是否先保存？", "文字收藏", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (result == DialogResult.Cancel) return false;
            return result != DialogResult.Yes || SaveCurrent();
        }
        private void Rebuild()
        {
            loading = true; list.BeginUpdate(); list.Items.Clear();
            foreach (Snippet item in store.Data.Value.Items)
                if ((item.Title + "\n" + item.Body).IndexOf(search.Text, StringComparison.OrdinalIgnoreCase) >= 0) { list.Items.Add(item); if (item.Id == selectedId) list.SelectedItem = item; }
            list.EndUpdate(); loading = false;
        }
        private void LoadItem(Snippet item)
        { loading = true; selectedId = item == null ? null : item.Id; title.Text = item == null ? "" : item.Title; body.Text = item == null ? "" : item.Body; loading = false; dirty = false; }
        private void NewDraft(string text)
        { if (!DiscardDraft()) return; LoadItem(null); body.Text = text; body.Focus(); }
        internal void CaptureClipboard()
        {
            try { if (Clipboard.ContainsText()) NewDraft(Clipboard.GetText()); else status.Text = "剪贴板里暂时没有文字。"; }
            catch { status.Text = "剪贴板正被其他应用使用，请稍后再试。"; }
        }
        private bool SaveCurrent()
        {
            if (!store.Save(selectedId, title.Text, body.Text)) { status.Text = store.Data.Error; return false; }
            if (selectedId == null) selectedId = store.Data.Value.Items[store.Data.Value.Items.Count - 1].Id;
            dirty = false; Rebuild(); status.Text = "已保存到本机。"; return true;
        }
        private void CopyCurrent()
        {
            if (String.IsNullOrEmpty(body.Text)) { status.Text = "先选择或输入一段文字。"; return; }
            try { Clipboard.SetText(body.Text); status.Text = "已复制，到其他应用按 Ctrl+V 粘贴。"; }
            catch { status.Text = "剪贴板正被占用，请再试一次。"; }
        }
    }
}
