// SPDX-License-Identifier: MIT
using System;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace NaiwaCompanion
{
    internal sealed class LocalData<T> where T : class, new()
    {
        private readonly string path;
        private readonly Func<T, bool> valid;
        internal T Value { get; private set; }
        internal bool ReadOnly { get; private set; }
        internal string Error = "", Notice = "";
        private bool recovered;
        internal LocalData(string file, Func<T, bool> validator)
        {
            path = file; valid = validator; Value = new T();
            if (file == null || (!File.Exists(file) && !File.Exists(file + ".bak"))) return;
            try { Value = Read(file); return; }
            catch (NotSupportedException) { ReadOnly = true; Error = Notice = "此数据来自更新版本，原文件已保护。"; return; }
            catch { }
            try
            {
                T backup = Read(file + ".bak");
                if (File.Exists(file)) File.Copy(file, file + ".damaged-" + Guid.NewGuid().ToString("N"));
                Value = backup; recovered = true; Notice = "已恢复上一次备份，异常文件已保留。";
            }
            catch { ReadOnly = true; Error = Notice = "数据暂时无法读取，已保护原文件。请先备份数据文件夹中的文件。"; }
        }
        private T Read(string file)
        {
            if (new FileInfo(file).Length > 32 * 1024 * 1024) throw new InvalidDataException();
            string text = File.ReadAllText(file, Encoding.UTF8);
            JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 32 * 1024 * 1024 };
            System.Collections.Generic.Dictionary<string, object> shape = json.DeserializeObject(text) as System.Collections.Generic.Dictionary<string, object>;
            if (shape == null || !shape.ContainsKey("Version")) throw new InvalidDataException();
            if (Convert.ToInt32(shape["Version"]) > 1) throw new NotSupportedException();
            T value = json.Deserialize<T>(text);
            if (value == null || !valid(value)) throw new InvalidDataException();
            return value;
        }
        internal T Copy() { JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 32 * 1024 * 1024 }; return json.Deserialize<T>(json.Serialize(Value)); }
        internal bool Save(T next)
        {
            Error = "";
            if (ReadOnly) { Error = Notice; return false; }
            if (!valid(next)) { Error = "内容不完整，尚未保存。"; return false; }
            try
            {
                if (path != null)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
                    byte[] bytes = new UTF8Encoding(false).GetBytes(new JavaScriptSerializer { MaxJsonLength = 32 * 1024 * 1024 }.Serialize(next));
                    using (FileStream stream = new FileStream(path + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None))
                    { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
                    if (File.Exists(path)) File.Replace(path + ".tmp", path, recovered ? null : path + ".bak"); else File.Move(path + ".tmp", path);
                    recovered = false;
                }
                Value = next; return true;
            }
            catch (Exception e) { Error = "保存失败，原内容未改动：" + e.Message; return false; }
        }
    }

    internal class UtilityForm : System.Windows.Forms.Form
    {
        private bool suspended, shutdown, passive;
        protected readonly float UiScale;
        internal bool IsSuspended { get { return suspended; } }
        protected override bool ShowWithoutActivation { get { return passive; } }
        internal UtilityForm(string title, int width, int height)
        {
            Text = title; StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
            using (System.Drawing.Graphics graphics = CreateGraphics()) UiScale = graphics.DpiX / 96f;
            Font = new System.Drawing.Font("Microsoft YaHei UI", 10);
            BackColor = System.Drawing.Color.FromArgb(249, 250, 244);
            ForeColor = System.Drawing.Color.FromArgb(48, 66, 52);
            FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle; MaximizeBox = false;
            ClientSize = new System.Drawing.Size(U(width), U(height));
        }
        protected int U(int value) { return (int)Math.Round(value * UiScale); }
        protected System.Windows.Forms.Button ButtonAt(string text, int x, int y, int width, EventHandler click)
        {
            System.Windows.Forms.Button button = new System.Windows.Forms.Button { Text = text, FlatStyle = System.Windows.Forms.FlatStyle.Flat, BackColor = System.Drawing.Color.FromArgb(232, 239, 225) };
            button.FlatAppearance.BorderSize = 0; button.SetBounds(U(x), U(y), U(width), U(32)); button.Click += click; Controls.Add(button); return button;
        }
        protected System.Windows.Forms.Label LabelAt(string text, int x, int y, int width, int height)
        {
            System.Windows.Forms.Label label = new System.Windows.Forms.Label { Text = text, UseMnemonic = false };
            label.SetBounds(U(x), U(y), U(width), U(height)); Controls.Add(label); return label;
        }
        internal void Reveal(bool topMost)
        { TopMost = topMost; suspended = false; Location = PetForm.Clamp(Location, Size); if (!Visible) Show(); Activate(); }
        internal void SuspendForFullscreen(bool value)
        {
            if (value && Visible) { suspended = true; Hide(); }
            else if (!value && suspended) { suspended = false; passive = true; Location = PetForm.Clamp(Location, Size); Show(); passive = false; }
        }
        internal void ShutdownPanel() { shutdown = true; Close(); }
        protected override void OnFormClosing(System.Windows.Forms.FormClosingEventArgs e)
        { if (!shutdown && e.CloseReason == System.Windows.Forms.CloseReason.UserClosing) { e.Cancel = true; suspended = false; Hide(); } base.OnFormClosing(e); }
    }
}
