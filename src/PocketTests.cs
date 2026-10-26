// SPDX-License-Identifier: MIT
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace NaiwaCompanion
{
    internal static class PocketTests
    {
        internal static void Run(string output, Action<bool, string> check)
        {
            string root = Path.Combine(output, "pocket-tests", Guid.NewGuid().ToString("N")), source = Path.Combine(root, "source"), destination = Path.Combine(root, "pocket");
            Directory.CreateDirectory(source); PocketStore store = new PocketStore(destination);
            string original = Path.Combine(source, "中文 & 文件.txt"); File.WriteAllText(original, "奶蛙文件口袋\r\n原件必须保留"); string hash = Conversion.Hash(original);
            PocketReceipt first = store.Stash(new string[] { original }, CancellationToken.None, delegate { });
            check(first.Saved.Count == 1 && Conversion.Hash(first.Saved[0]) == hash && Conversion.Hash(original) == hash, "pocket verifies copied content and preserves original files");
            PocketReceipt second = store.Stash(new string[] { original }, CancellationToken.None, delegate { });
            check(second.Saved.Count == 1 && first.Saved[0] != second.Saved[0] && File.Exists(first.Saved[0]), "pocket duplicate names keep both copies");
            string blocked = Path.Combine(destination, "中文 & 文件 (3).txt"); Directory.CreateDirectory(blocked);
            second = store.Stash(new string[] { original }, CancellationToken.None, delegate { });
            check(second.Saved.Count == 1 && second.Saved[0] != blocked && Directory.Exists(blocked), "pocket avoids collisions with existing directories as well as files");
            string folder = Path.Combine(source, "整套资料.v1"), empty = Path.Combine(folder, "空目录"), nested = Path.Combine(folder, "子目录"); Directory.CreateDirectory(empty); Directory.CreateDirectory(nested);
            File.WriteAllText(Path.Combine(nested, "说明.md"), "nested content"); File.WriteAllBytes(Path.Combine(folder, "空文件.bin"), new byte[0]);
            PocketReceipt tree = store.Stash(new string[] { folder }, CancellationToken.None, delegate { });
            check(tree.Saved.Count == 1 && Directory.Exists(Path.Combine(tree.Saved[0], "空目录")) && new FileInfo(Path.Combine(tree.Saved[0], "空文件.bin")).Length == 0 && File.ReadAllText(Path.Combine(tree.Saved[0], "子目录", "说明.md")) == "nested content", "pocket copies nested folders, empty folders and zero-byte files");
            PocketReceipt inside = store.Stash(new string[] { first.Saved[0] }, CancellationToken.None, delegate { });
            check(inside.AlreadyHere == 1 && inside.Saved.Count == 0, "dropping an item already in the pocket avoids another copy");
            PocketReceipt ancestor = store.Stash(new string[] { root }, CancellationToken.None, delegate { });
            check(ancestor.Errors.Count == 1 && ancestor.Saved.Count == 0, "pocket rejects recursive copies of its own parent directory");
            PocketReceipt drive = store.Stash(new string[] { Path.GetPathRoot(root) }, CancellationToken.None, delegate { });
            check(drive.Errors.Count == 1 && drive.Saved.Count == 0, "pocket rejects whole-drive drops");
            CancellationTokenSource cancellation = new CancellationTokenSource();
            PocketReceipt cancelled = store.Stash(new string[] { folder }, cancellation.Token, delegate { cancellation.Cancel(); }); cancellation.Dispose();
            check(cancelled.Cancelled && cancelled.Saved.Count == 0 && Directory.GetDirectories(destination, PocketStore.PendingPrefix + "*").Length == 0, "cancelled copies publish no partial item and clean their staging directory");
            bool added = false;
            PocketReceipt changed = store.Stash(new string[] { folder }, CancellationToken.None, delegate { if (!added) { added = true; File.WriteAllText(Path.Combine(folder, "new-during-copy.txt"), "added"); } });
            check(changed.Errors.Count == 1 && changed.Saved.Count == 0 && File.Exists(Path.Combine(folder, "new-during-copy.txt")), "a changing source tree is reported instead of silently omitting new files");
            string locked = Path.Combine(source, "被占用.bin"); File.WriteAllText(locked, "locked");
            using (FileStream held = new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                PocketReceipt partial = store.Stash(new string[] { locked, original }, CancellationToken.None, delegate { });
                check(partial.Errors.Count == 1 && partial.Saved.Count == 1, "a locked source reports its failure while other files still copy");
            }
            check(Conversion.Hash(original) == hash && File.ReadAllText(locked) == "locked", "failure and cancellation leave all original contents intact");
            string stage = Path.Combine(destination, PocketStore.PendingPrefix + "interrupted"); Directory.CreateDirectory(stage);
            int visible = store.Items().Length;
            check(visible == Directory.GetFileSystemEntries(destination).Length - 1, "an interrupted pending copy does not appear as a saved file");
            using (PocketForm panel = new PocketForm(store, delegate { }))
            {
                Snapshot(panel, Path.Combine(output, "pocket.png"));
                panel.SuspendForFullscreen(true); check(!panel.Visible && panel.IsSuspended, "pocket suspends with fullscreen apps");
                panel.SuspendForFullscreen(false); check(panel.Visible && !panel.IsSuspended, "pocket restores after fullscreen apps");
            }
            using (DropChoiceForm panel = new DropChoiceForm(new string[] { original, folder }, delegate { }, delegate { }, delegate { })) Snapshot(panel, Path.Combine(output, "drop-choice.png"));
            using (DropChoiceForm panel = new DropChoiceForm(new string[] { folder }, delegate { }, delegate { }, delegate { }))
            {
                bool disabled = false; foreach (Control control in panel.Controls) if (control.Text == "转换格式…") disabled = !control.Enabled;
                check(disabled, "folder-only drops offer storage without an invalid conversion option");
            }
            string uiPocket = Path.Combine(root, "ui-pocket");
            JavaScriptSerializer json = new JavaScriptSerializer();
            check(json.Deserialize<Preferences>("{\"Size\":85}").AllowFileDrop, "legacy settings without a drop preference keep the enabled default");
            using (Theme theme = new Theme(null)) using (PetForm pet = new PetForm(theme, new Preferences { Ephemeral = true }, uiPocket))
            {
                pet.Show(); Application.DoEvents();
                DataObject data = new DataObject(DataFormats.FileDrop, new string[] { original });
                DragEventArgs enter = new DragEventArgs(data, 0, 0, 0, DragDropEffects.Copy | DragDropEffects.Move, DragDropEffects.None);
                typeof(Control).GetMethod("OnDragEnter", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(pet, new object[] { enter });
                check(pet.AllowDrop && enter.Effect == DragDropEffects.Copy, "pet accepts file drops using copy semantics");
                DragEventArgs text = new DragEventArgs(new DataObject(DataFormats.Text, "text"), 0, 0, 0, DragDropEffects.Copy, DragDropEffects.None);
                typeof(Control).GetMethod("OnDragEnter", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(pet, new object[] { text });
                check(text.Effect == DragDropEffects.None, "pet rejects non-file drag payloads");
                typeof(Control).GetMethod("OnDragDrop", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(pet, new object[] { enter });
                check(pet.DropPanel != null && pet.DropPanel.Visible, "dropping on the pet opens the action chooser");
                pet.SetHidden(true); check(!pet.DropPanel.Visible, "drop chooser suspends together with the pet");
                pet.SetHidden(false); check(pet.DropPanel.Visible, "drop chooser restores together with the pet");
                Click(pet.DropPanel, "暂存到文件口袋");
                DateTime deadline = DateTime.UtcNow.AddSeconds(10);
                while (pet.PocketPanel.Busy && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(10); }
                check(pet.PocketPanel.Visible && !pet.PocketPanel.Busy && File.Exists(Path.Combine(uiPocket, Path.GetFileName(original))) && Conversion.Hash(original) == hash, "chooser storage action copies into the pocket and opens its file list");
                File.WriteAllText(Path.Combine(uiPocket, "外部放入.txt"), "external"); pet.PocketPanel.RefreshFiles();
                int rows = 0; foreach (Control control in pet.PocketPanel.Controls) if (control is ListView) rows = ((ListView)control).Items.Count;
                check(rows == 2, "pocket refresh includes files placed in its folder outside the app");
                pet.ReceiveFiles(new string[] { original }); Click(pet.DropPanel, "转换格式…");
                rows = 0; foreach (Control control in pet.ConversionPanel.Controls) if (control is ListView) rows = ((ListView)control).Items.Count;
                check(pet.ConversionPanel.Visible && rows == 1, "chooser conversion action queues the dropped file in the existing converter");
                pet.ReceiveFiles(new string[] { original });
                pet.SetFileDropEnabled(false);
                DragEventArgs disabled = new DragEventArgs(data, 0, 0, 0, DragDropEffects.Copy, DragDropEffects.Copy);
                typeof(Control).GetMethod("OnDragEnter", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(pet, new object[] { disabled });
                pet.ReceiveFiles(new string[] { original });
                check(!pet.AllowDrop && disabled.Effect == DragDropEffects.None && pet.DropPanel == null, "disabling file drops unregisters the target and dismisses pending choices");
                pet.SetHidden(true); pet.SetHidden(false);
                check(pet.DropPanel == null && File.Exists(Path.Combine(uiPocket, Path.GetFileName(original))), "disabled drop choices stay closed across fullscreen and stored files remain intact");
                pet.ShowPocket();
                check(pet.PocketPanel.Visible, "manual pocket access stays available when pet drops are disabled");
                pet.SetFileDropEnabled(true);
                pet.ReceiveFiles(new string[] { original });
                check(pet.AllowDrop && pet.DropPanel != null && pet.DropPanel.Visible, "re-enabling pet drops immediately restores the chooser");
            }
            Preferences saved = json.Deserialize<Preferences>(json.Serialize(new Preferences { AllowFileDrop = false })); saved.Ephemeral = true;
            using (Theme theme = new Theme(null)) using (PetForm restarted = new PetForm(theme, saved, uiPocket))
            {
                restarted.ReceiveFiles(new string[] { original });
                check(!saved.AllowFileDrop && !restarted.AllowDrop && restarted.DropPanel == null, "a saved disabled preference is honored by a restarted pet");
            }
        }
        private static void Click(Form form, string label) { foreach (Control control in form.Controls) if (control is Button && control.Text == label) { ((Button)control).PerformClick(); return; } throw new Exception("Missing button " + label); }
        private static void Snapshot(Form form, string file)
        { form.Show(); Application.DoEvents(); using (Bitmap image = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size)); image.Save(file, ImageFormat.Png); } }
    }
}
