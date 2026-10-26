// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace NaiwaCompanion
{
    internal static class UtilityTests
    {
        internal static void Run(string output, Action<bool, string> check)
        {
            string root = Path.Combine(output, "utilities-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            string snippetsFile = Path.Combine(root, "snippets.json"); SnippetStore snippets = new SnippetStore(snippetsFile);
            check(!snippets.Save(null, "空收藏", " ") && !File.Exists(snippetsFile), "empty snippet input cannot create a saved entry");
            string text = "第一行 & <文字> 🐸\r\n第二行保留换行和空格  ";
            check(snippets.Save(null, "常用回复", text), "snippet saves Unicode and multiple lines");
            string id = snippets.Data.Value.Items[0].Id;
            SnippetStore reopened = new SnippetStore(snippetsFile);
            check(reopened.Data.Value.Items.Count == 1 && reopened.Data.Value.Items[0].Body == text, "snippet restart preserves exact clipboard-ready content");
            check(snippets.Save(id, "改名", text + "追加") && snippets.Data.Value.Items.Count == 1 && snippets.Data.Value.Items[0].Id == id, "editing a snippet preserves its identity without duplication");
            check(snippets.Delete(id) && snippets.Undo() && snippets.Data.Value.Items[0].Body == text + "追加", "snippet deletion supports restoring the full saved body");
            using (FileStream locked = new FileStream(snippetsFile, FileMode.Open, FileAccess.Read, FileShare.Read))
                check(!snippets.Save(id, "不应保存", "失败输入") && snippets.Data.Value.Items[0].Title == "改名", "failed snippet writes preserve the prior saved entry");
            string future = Path.Combine(root, "future.json"); File.WriteAllText(future, "{\"Version\":99,\"Items\":[]}");
            check(new SnippetStore(future).Data.ReadOnly, "newer collection formats stay protected");
            SpeechLibrary lines = new SpeechLibrary(); Queue<string> recent = new Queue<string>();
            for (int i = 0; i < 100; i++) { string line = lines.Idle(0); if (recent.Contains(line)) throw new Exception("idle speech repeats too soon"); recent.Enqueue(line); while (recent.Count > 12) recent.Dequeue(); }
            check(true, "embedded original speech avoids the last twelve idle lines across one hundred draws");
            SpeechOptions options = new SpeechOptions { QuietHours = true, QuietFrom = "23:00", QuietTo = "08:00" };
            check(options.IsQuiet(new DateTime(2026, 1, 1, 23, 30, 0), DateTime.UtcNow) && options.IsQuiet(new DateTime(2026, 1, 2, 7, 0, 0), DateTime.UtcNow) && !options.IsQuiet(new DateTime(2026, 1, 2, 12, 0, 0), DateTime.UtcNow), "quiet hours handle a period crossing midnight");
            string speechFile = Path.Combine(root, "speech.json"); LocalData<SpeechOptions> speech = new LocalData<SpeechOptions>(speechFile, SpeechOptions.Valid);
            options.MutedUntilUtc = DateTime.UtcNow.AddHours(1); speech.Save(options);
            check(new LocalData<SpeechOptions>(speechFile, SpeechOptions.Valid).Value.IsQuiet(DateTime.Now, DateTime.UtcNow), "one-hour quiet preference persists through a restart");
            foreach (Point corner in new Point[] { new Point(-1900, 10), new Point(-300, 10), new Point(-1900, 800), new Point(-300, 800) })
            {
                Rectangle area = new Rectangle(-1920, 0, 1920, 1080), pet = new Rectangle(corner, new Size(150, 200));
                Rectangle placed = SpeechBubble.Place(pet, area, new Size(226, 160)); if (!area.Contains(placed)) throw new Exception("speech escaped monitor work area");
            }
            check(true, "compact bubbles fit every corner of a negative-coordinate monitor");
            using (SpeechBubble bubble = new SpeechBubble())
            {
                bubble.Say("我刚刚思考了一下，\n决定先圆着。", false, false, 6, new Rectangle(800, 400, 180, 220), false); Application.DoEvents();
                int style = Desktop.GetWindowLong(bubble.Handle, -20);
                check((style & 0x08000000) != 0 && (style & 0x00000080) != 0, "speech uses a non-activating tool window");
                Snapshot(bubble, Path.Combine(output, "speech-small.png")); bubble.Hide();
                bubble.Say("还有3件小事：\n1. 整理桌面资料\n2. 核对明天的安排\n3. 完成奶蛙新功能", true, true, 10, new Rectangle(800, 400, 180, 220), false); Application.DoEvents();
                Snapshot(bubble, Path.Combine(output, "speech-todo.png"));
            }
            SnippetStore previewStore = new SnippetStore(null); previewStore.Save(null, "常用回复", "收到，我整理好后给你。\r\n谢谢！"); previewStore.Save(null, "提示词片段", "请用清楚、简短的中文说明。");
            using (SnippetForm panel = new SnippetForm(previewStore)) Snapshot(panel, Path.Combine(output, "snippets.png"));
            using (ConversionForm panel = new ConversionForm(true)) Snapshot(panel, Path.Combine(output, "converter.png"));
            check(Conversion.Targets("a.pdf").Contains("docx") && Conversion.Targets("a.mp4").Contains("mp3") && Conversion.Targets("a.xlsx").Contains("csv") && Conversion.Targets("a.unknown").Count == 0, "format choices list actual supported source-to-target combinations");
            string sourceImage = Path.Combine(root, "图片 中文 & 空格.png"), converted = Path.Combine(root, "converted");
            using (Bitmap image = new Bitmap(48, 32)) { using (Graphics g = Graphics.FromImage(image)) g.Clear(Color.Gold); image.Save(sourceImage, ImageFormat.Png); }
            string original = Conversion.Hash(sourceImage);
            ConversionReceipt result = Conversion.Convert(new ConversionRequest { Source = sourceImage, Format = "jpg", OutputDirectory = converted }, new ConverterTools(), CancellationToken.None, delegate { });
            check(result.Success && File.Exists(result.Output) && Conversion.Hash(sourceImage) == original, "native image conversion reopens the result and preserves the source hash");
            string first = result.Output;
            result = Conversion.Convert(new ConversionRequest { Source = sourceImage, Format = "jpg", OutputDirectory = converted }, new ConverterTools(), CancellationToken.None, delegate { });
            check(result.Success && result.Output != first && File.Exists(first), "converting twice creates a unique name instead of overwriting a prior result");
            string blocked = Path.Combine(converted, Path.GetFileNameWithoutExtension(sourceImage) + "_转换 (3).jpg"); Directory.CreateDirectory(blocked);
            result = Conversion.Convert(new ConversionRequest { Source = sourceImage, Format = "jpg", OutputDirectory = converted }, new ConverterTools(), CancellationToken.None, delegate { });
            check(result.Success && result.Output != blocked && Directory.Exists(blocked) && File.Exists(first), "a directory with the target filename is preserved while conversion selects another name");
            string broken = Path.Combine(root, "broken.png"); File.WriteAllText(broken, "not an image");
            result = Conversion.Convert(new ConversionRequest { Source = broken, Format = "jpg", OutputDirectory = converted }, new ConverterTools(), CancellationToken.None, delegate { });
            check(!result.Success && String.IsNullOrEmpty(result.Output) && File.ReadAllText(broken) == "not an image", "invalid inputs never produce a successful result or modify the source");
            CancellationTokenSource cancellation = new CancellationTokenSource(); cancellation.Cancel();
            result = Conversion.Convert(new ConversionRequest { Source = sourceImage, Format = "jpg", OutputDirectory = converted }, new ConverterTools(), cancellation.Token, delegate { });
            check(!result.Success && String.IsNullOrEmpty(result.Output), "cancelled conversion cannot publish its temporary output");
            cancellation.Dispose();
        }
        private static void Snapshot(Form form, string file)
        { if (!form.Visible) { form.Show(); Application.DoEvents(); } using (Bitmap image = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size)); image.Save(file, ImageFormat.Png); } }
    }
}
