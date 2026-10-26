// SPDX-License-Identifier: MIT
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace NaiwaCompanion
{
    internal static class Desktop
    {
        internal const string WindowName = "NaiwaCompanion.Main.1";
        internal const int WakeMessage = 0x8007;
        internal const int StatusMessage = 0x8008;
        internal const int TodoMessage = 0x8009;
        internal const int SnippetsMessage = 0x800A, ConvertMessage = 0x800B, SayMessage = 0x800C;
        internal const int PocketMessage = 0x800D;
        [StructLayout(LayoutKind.Sequential)] internal struct XY { internal int X, Y; internal XY(int x, int y) { X = x; Y = y; } }
        [StructLayout(LayoutKind.Sequential)] internal struct WH { internal int W, H; internal WH(int w, int h) { W = w; H = h; } }
        [StructLayout(LayoutKind.Sequential)] internal struct Box { internal int L, T, R, B; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] internal struct Alpha { internal byte Operation, Flags, Constant, Format; }
        [DllImport("user32.dll")] internal static extern bool SetProcessDPIAware();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32.dll")] internal static extern bool PostMessage(IntPtr window, int message, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] internal static extern IntPtr SendMessage(IntPtr window, int message, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr window, out Box rect);
        [DllImport("user32.dll")] internal static extern int GetWindowLong(IntPtr window, int index);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassName(IntPtr window, StringBuilder name, int size);
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool UpdateLayeredWindow(IntPtr window, IntPtr dst, ref XY point, ref WH size, IntPtr src, ref XY origin, uint key, ref Alpha alpha, uint flags);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);
        [DllImport("user32.dll")] internal static extern int GetGuiResources(IntPtr process, int flags);
        [DllImport("user32.dll")] internal static extern bool DestroyIcon(IntPtr icon);

        // Microsoft layered-window API: use premultiplied alpha, and release each GDI object.
        internal static void Present(IntPtr window, Bitmap image, Point location)
        {
            IntPtr screen = GetDC(IntPtr.Zero), memory = IntPtr.Zero, bitmap = IntPtr.Zero, previous = IntPtr.Zero;
            try
            {
                memory = CreateCompatibleDC(screen); bitmap = image.GetHbitmap(Color.FromArgb(0));
                previous = SelectObject(memory, bitmap);
                XY p = new XY(location.X, location.Y), origin = new XY(0, 0);
                WH size = new WH(image.Width, image.Height);
                Alpha blend = new Alpha { Constant = 255, Format = 1 };
                if (!UpdateLayeredWindow(window, screen, ref p, ref size, memory, ref origin, 0, ref blend, 2))
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }
            finally
            {
                if (previous != IntPtr.Zero) SelectObject(memory, previous);
                if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
                if (memory != IntPtr.Zero) DeleteDC(memory);
                if (screen != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screen);
            }
        }
        internal static bool IsFullscreen(IntPtr foreground, IntPtr pet, Rectangle screen)
        {
            if (foreground == IntPtr.Zero || foreground == pet || !IsWindowVisible(foreground) || IsIconic(foreground)) return false;
            StringBuilder name = new StringBuilder(128); GetClassName(foreground, name, name.Capacity);
            if (name.ToString() == "Progman" || name.ToString() == "WorkerW" || name.ToString().StartsWith("Shell_")) return false;
            if ((GetWindowLong(foreground, -16) & 0x00C00000) != 0) return false;
            try { int cloaked; if (DwmGetWindowAttribute(foreground, 14, out cloaked, 4) == 0 && cloaked != 0) return false; }
            catch (EntryPointNotFoundException) { }
            Box r; if (!GetWindowRect(foreground, out r)) return false;
            return r.L <= screen.Left + 2 && r.T <= screen.Top + 2 && r.R >= screen.Right - 2 && r.B >= screen.Bottom - 2;
        }
    }
}
