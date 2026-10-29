using System.Text;

namespace FastShot;

/// <summary>A frozen image of one monitor, taken the moment the shortcut fired.</summary>
public sealed class ScreenShot
{
    public required MonitorInfo Monitor { get; init; }
    public required BitmapSource Image { get; init; }
    /// <summary>The same pixels as <see cref="Image"/>, top-down BGRA, for the magnifier's colour readout.</summary>
    public required byte[] Pixels { get; init; }
    public int Width => Image.PixelWidth;
    public int Height => Image.PixelHeight;
}

public static class ScreenGrabber
{
    public static List<ScreenShot> CaptureAll()
    {
        var shots = new List<ScreenShot>();
        foreach (var monitor in MonitorInfo.All())
        {
            using var capture = new GdiCapture(monitor.Width, monitor.Height);
            if (!capture.Grab(monitor.Bounds.Left, monitor.Bounds.Top, cursor: false, layered: true)) continue;
            var pixels = capture.ToArray();
            shots.Add(new ScreenShot
            {
                Monitor = monitor, Pixels = pixels,
                Image = ImageExporter.FromBgra(pixels, monitor.Width, monitor.Height, monitor.Width * 4),
            });
        }
        return shots;
    }

    static readonly HashSet<string> shellClasses =
        ["Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Windows.UI.Core.CoreWindow", "XamlExplorerHostIslandWindow"];

    /// <summary>Normal app windows, front to back, in physical screen pixels (without the invisible resize borders).</summary>
    public static List<Native.RECT> WindowRects()
    {
        var own = (uint)Environment.ProcessId;
        var rects = new List<Native.RECT>();
        var className = new StringBuilder(256);
        Native.EnumWindows((hwnd, _) =>
        {
            if (!Native.IsWindowVisible(hwnd) || Native.IsIconic(hwnd)) return true;
            Native.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == own) return true;
            if ((Native.ExStyle(hwnd) & Native.WS_EX_TOOLWINDOW) != 0) return true;
            if (Native.DwmGetWindowAttribute(hwnd, Native.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return true;
            className.Clear();
            Native.GetClassName(hwnd, className, className.Capacity);
            if (shellClasses.Contains(className.ToString())) return true;
            if (Native.GetWindowTextLength(hwnd) == 0) return true;
            if (Native.DwmGetWindowAttribute(hwnd, Native.DWMWA_EXTENDED_FRAME_BOUNDS, out Native.RECT rect, System.Runtime.InteropServices.Marshal.SizeOf<Native.RECT>()) != 0)
                Native.GetWindowRect(hwnd, out rect);
            if (rect.Width > 40 && rect.Height > 40) rects.Add(rect);
            return true;
        }, IntPtr.Zero);
        return rects;
    }
}
