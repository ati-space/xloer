using System.Runtime.InteropServices;
using System.Text;

namespace FastShot;

/// <summary>The Win32 calls FastShot needs: hotkeys, window styling, monitors, DPI, cursor, GDI capture.</summary>
public static class Native
{
    public const int WM_HOTKEY = 0x0312;
    public const int WM_COPYDATA = 0x004A;
    public const int WM_NCLBUTTONDOWN = 0x00A1;
    public const int HTCAPTION = 2;
    public const int GWL_EXSTYLE = -20;
    public const uint WS_POPUP = 0x80000000;
    public const uint WS_CLIPCHILDREN = 0x02000000;
    public const uint WS_EX_TOPMOST = 0x8;
    public const uint WS_EX_TRANSPARENT = 0x20;
    public const uint WS_EX_TOOLWINDOW = 0x80;
    public const uint WS_EX_LAYERED = 0x80000;
    public const uint WS_EX_NOACTIVATE = 0x08000000;
    public const uint WDA_NONE = 0;
    public const uint WDA_EXCLUDEFROMCAPTURE = 0x11;
    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public static readonly IntPtr HWND_MESSAGE = new(-3);
    public const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40, SWP_FRAMECHANGED = 0x20;
    public const int SW_HIDE = 0;
    public const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_SHIFT = 4, MOD_WIN = 8, MOD_NOREPEAT = 0x4000;
    public const int DWMWA_TRANSITIONS_FORCEDISABLED = 3, DWMWA_EXTENDED_FRAME_BOUNDS = 9, DWMWA_CLOAKED = 14;
    public const int SRCCOPY = 0x00CC0020, CAPTUREBLT = 0x40000000;
    public const int HALFTONE = 4;
    public const int CURSOR_SHOWING = 1;
    public const uint DI_NORMAL = 3;
    public const uint ASFW_ANY = unchecked((uint)-1);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
        public RECT(int left, int top, int right, int bottom) { Left = left; Top = top; Right = right; Bottom = bottom; }
        public readonly bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MONITORINFOEX
    {
        public int Size;
        public RECT Monitor;
        public RECT Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CURSORINFO
    {
        public int Size;
        public int Flags;
        public IntPtr Cursor;
        public POINT ScreenPos;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ICONINFO
    {
        public int Icon;
        public int XHotspot, YHotspot;
        public IntPtr Mask, Color;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public uint Size;
        public int Width, Height;
        public ushort Planes, BitCount;
        public uint Compression, SizeImage;
        public int XPelsPerMeter, YPelsPerMeter;
        public uint ClrUsed, ClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct COPYDATASTRUCT
    {
        public IntPtr Data;
        public int Length;
        public IntPtr Pointer;
    }

    public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);
    public delegate bool EnumMonitorsProc(IntPtr monitor, IntPtr hdc, IntPtr rect, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)] public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint vk);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll")] public static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] public static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool AllowSetForegroundWindow(uint processId);
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint attach, uint attachTo, bool on);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] public static extern IntPtr SetFocus(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] public static extern bool GetCursorInfo(ref CURSORINFO info);
    [DllImport("user32.dll")] public static extern bool GetIconInfo(IntPtr icon, out ICONINFO info);
    [DllImport("user32.dll")] public static extern bool DrawIconEx(IntPtr hdc, int x, int y, IntPtr icon, int width, int height, uint step, IntPtr brush, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFOEX info);
    [DllImport("user32.dll")] public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, EnumMonitorsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hwnd, StringBuilder name, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextLength(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint RegisterWindowMessage(string message);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wParam, ref COPYDATASTRUCT lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? className, string? windowName);
    [DllImport("user32.dll")] public static extern bool ReleaseCapture();
    [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint MapVirtualKey(uint code, uint mapType);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetKeyNameText(int lParam, StringBuilder text, int size);
    [DllImport("shcore.dll")] public static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out RECT value, int size);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);
    [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] public static extern bool BitBlt(IntPtr dest, int x, int y, int width, int height, IntPtr source, int sx, int sy, int rop);
    [DllImport("gdi32.dll")] public static extern bool StretchBlt(IntPtr dest, int x, int y, int width, int height, IntPtr source, int sx, int sy, int sw, int sh, int rop);
    [DllImport("gdi32.dll")] public static extern int SetStretchBltMode(IntPtr hdc, int mode);
    [DllImport("gdi32.dll")] public static extern bool SetBrushOrgEx(IntPtr hdc, int x, int y, IntPtr previous);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER info, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] public static extern bool GdiFlush();
    [DllImport("winmm.dll")] public static extern uint timeBeginPeriod(uint period);
    [DllImport("winmm.dll")] public static extern uint timeEndPeriod(uint period);

    public static uint ExStyle(IntPtr hwnd) => (uint)GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();

    public static void SetExStyle(IntPtr hwnd, uint add, uint remove = 0)
    {
        var style = (ExStyle(hwnd) | add) & ~remove;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(style));
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
    }

    public static double DpiScale(IntPtr monitor) =>
        GetDpiForMonitor(monitor, 0, out var x, out _) == 0 && x > 0 ? x / 96.0 : 1;

    public static POINT CursorPosition()
    {
        GetCursorPos(out var p);
        return p;
    }

    /// <summary>Brings a window to the front even when Windows' foreground lock would refuse.</summary>
    public static void ForceForeground(IntPtr hwnd)
    {
        if (GetForegroundWindow() == hwnd) return;
        SetForegroundWindow(hwnd);
        if (GetForegroundWindow() == hwnd) return;
        var foreground = GetForegroundWindow();
        var foregroundThread = GetWindowThreadProcessId(foreground, out _);
        var thread = GetCurrentThreadId();
        if (foregroundThread != thread) AttachThreadInput(thread, foregroundThread, true);
        BringWindowToTop(hwnd);
        SetForegroundWindow(hwnd);
        if (foregroundThread != thread) AttachThreadInput(thread, foregroundThread, false);
    }

    public static void DisableTransitions(IntPtr hwnd)
    {
        var on = 1;
        DwmSetWindowAttribute(hwnd, DWMWA_TRANSITIONS_FORCEDISABLED, ref on, sizeof(int));
    }
}

/// <summary>Screen DC snapshot into a top-down 32-bit DIB whose bits stay mapped; reused for every frame of a recording.
/// When the output size differs from the source it is scaled with HALFTONE filtering.</summary>
public sealed unsafe class GdiCapture : IDisposable
{
    public int SourceWidth { get; }
    public int SourceHeight { get; }
    public int Width { get; }
    public int Height { get; }
    public int Stride => Width * 4;
    public IntPtr Bits { get; private set; }

    readonly IntPtr memoryDc;
    readonly IntPtr bitmap;
    readonly IntPtr previous;

    public GdiCapture(int width, int height) : this(width, height, width, height) { }

    public GdiCapture(int sourceWidth, int sourceHeight, int width, int height)
    {
        SourceWidth = sourceWidth;
        SourceHeight = sourceHeight;
        Width = width;
        Height = height;
        var screen = Native.GetDC(IntPtr.Zero);
        memoryDc = Native.CreateCompatibleDC(screen);
        var header = new Native.BITMAPINFOHEADER
        {
            Size = (uint)Marshal.SizeOf<Native.BITMAPINFOHEADER>(), Width = width, Height = -height, Planes = 1, BitCount = 32, Compression = 0,
        };
        bitmap = Native.CreateDIBSection(memoryDc, ref header, 0, out var bits, IntPtr.Zero, 0);
        Bits = bits;
        previous = Native.SelectObject(memoryDc, bitmap);
        Native.ReleaseDC(IntPtr.Zero, screen);
        if (bitmap == IntPtr.Zero) throw new InvalidOperationException("Ekran tamponu oluşturulamadı.");
        if (width != sourceWidth || height != sourceHeight)
        {
            Native.SetStretchBltMode(memoryDc, Native.HALFTONE);
            Native.SetBrushOrgEx(memoryDc, 0, 0, IntPtr.Zero);
        }
    }

    /// <summary>Copies the screen region at (x, y) in physical pixels; optionally paints the cursor.
    /// <paramref name="layered"/> adds CAPTUREBLT so menus and tooltips come along (it can make the cursor flicker).</summary>
    public bool Grab(int x, int y, bool cursor, bool layered = false)
    {
        var screen = Native.GetDC(IntPtr.Zero);
        try
        {
            var rop = Native.SRCCOPY | (layered ? Native.CAPTUREBLT : 0);
            var ok = Width == SourceWidth && Height == SourceHeight
                ? Native.BitBlt(memoryDc, 0, 0, Width, Height, screen, x, y, rop)
                : Native.StretchBlt(memoryDc, 0, 0, Width, Height, screen, x, y, SourceWidth, SourceHeight, rop);
            if (ok && cursor) DrawCursor(x, y);
            Native.GdiFlush();
            return ok;
        }
        finally { Native.ReleaseDC(IntPtr.Zero, screen); }
    }

    void DrawCursor(int originX, int originY)
    {
        var info = new Native.CURSORINFO { Size = Marshal.SizeOf<Native.CURSORINFO>() };
        if (!Native.GetCursorInfo(ref info) || info.Flags != Native.CURSOR_SHOWING || info.Cursor == IntPtr.Zero) return;
        int hotX = 0, hotY = 0;
        if (Native.GetIconInfo(info.Cursor, out var icon))
        {
            hotX = icon.XHotspot;
            hotY = icon.YHotspot;
            if (icon.Mask != IntPtr.Zero) Native.DeleteObject(icon.Mask);
            if (icon.Color != IntPtr.Zero) Native.DeleteObject(icon.Color);
        }
        var sx = Width / (double)SourceWidth;
        var sy = Height / (double)SourceHeight;
        var x = (int)Math.Round((info.ScreenPos.X - originX) * sx) - hotX;
        var y = (int)Math.Round((info.ScreenPos.Y - originY) * sy) - hotY;
        if (x > -64 && y > -64 && x < Width && y < Height) Native.DrawIconEx(memoryDc, x, y, info.Cursor, 0, 0, 0, IntPtr.Zero, Native.DI_NORMAL);
    }

    /// <summary>A copy of the pixels with alpha forced opaque (GDI leaves it at zero).</summary>
    public byte[] ToArray()
    {
        var pixels = new byte[Stride * Height];
        fixed (byte* dst = pixels) Buffer.MemoryCopy((void*)Bits, dst, pixels.Length, pixels.Length);
        for (var i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
        return pixels;
    }

    public void Dispose()
    {
        if (Bits == IntPtr.Zero) return;
        Native.SelectObject(memoryDc, previous);
        Native.DeleteObject(bitmap);
        Native.DeleteDC(memoryDc);
        Bits = IntPtr.Zero;
    }
}
