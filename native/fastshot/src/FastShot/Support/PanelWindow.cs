using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace FastShot;

/// <summary>A borderless top-level window placed in physical pixels. Overlays, HUDs, toasts and
/// pins use this instead of a WPF Window so they land exactly on one monitor at that monitor's
/// DPI and never go through the resize a Window does when it opens on another monitor.</summary>
public sealed class PanelWindow : IDisposable
{
    public HwndSource Source { get; }
    public IntPtr Handle => Source.Handle;
    public FrameworkElement Content { get; }
    public bool IsVisible { get; private set; }
    public bool IsDisposed => Source.IsDisposed;
    public event Action? Closed;

    /// <summary>Physical pixel bounds.</summary>
    public Native.RECT Bounds
    {
        get
        {
            if (!Source.IsDisposed && Native.GetWindowRect(Handle, out var rect)) bounds = rect;
            return bounds;
        }
    }

    Native.RECT bounds;

    public PanelWindow(FrameworkElement content, Native.RECT rect, bool transparent, bool activatable = true, bool clickThrough = false)
    {
        Content = content;
        var exStyle = Native.WS_EX_TOOLWINDOW | Native.WS_EX_TOPMOST | (activatable ? 0 : Native.WS_EX_NOACTIVATE)
                      | (clickThrough ? Native.WS_EX_TRANSPARENT | Native.WS_EX_LAYERED : 0);
        var parameters = new HwndSourceParameters("FastShot")
        {
            PositionX = rect.Left, PositionY = rect.Top, Width = Math.Max(1, rect.Width), Height = Math.Max(1, rect.Height),
            WindowStyle = unchecked((int)(Native.WS_POPUP | Native.WS_CLIPCHILDREN)),
            ExtendedWindowStyle = unchecked((int)exStyle),
            UsesPerPixelOpacity = transparent,
        };
        Source = new HwndSource(parameters) { SizeToContent = SizeToContent.Manual };
        if (!transparent) Source.CompositionTarget.BackgroundColor = Colors.Black;
        Source.RootVisual = content;
        bounds = rect;
        Native.DisableTransitions(Handle);
        Source.AddHook(Hook);
    }

    IntPtr Hook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_MOUSEACTIVATE = 0x0021, MA_NOACTIVATE = 3, WM_CLOSE = 0x0010;
        if (message == WM_MOUSEACTIVATE && (Native.ExStyle(hwnd) & Native.WS_EX_NOACTIVATE) != 0)
        {
            handled = true;
            return MA_NOACTIVATE;
        }
        if (message == WM_CLOSE)
        {
            handled = true;
            Dispose();
        }
        return IntPtr.Zero;
    }

    /// <summary>Device pixels per DIP for this window's monitor.</summary>
    public double Scale => Source.CompositionTarget?.TransformToDevice.M11 ?? 1;

    public void Show(bool activate = true)
    {
        if (Source.IsDisposed) return;
        var b = bounds;
        Native.SetWindowPos(Handle, Native.HWND_TOPMOST, b.Left, b.Top, b.Width, b.Height, Native.SWP_SHOWWINDOW | (activate ? 0 : Native.SWP_NOACTIVATE));
        IsVisible = true;
        if (activate) Activate();
    }

    public void Hide()
    {
        if (Source.IsDisposed) return;
        Native.ShowWindow(Handle, Native.SW_HIDE);
        IsVisible = false;
    }

    public void Activate()
    {
        if (Source.IsDisposed) return;
        Native.ForceForeground(Handle);
        Native.SetFocus(Handle);
    }

    public void SetBounds(Native.RECT rect)
    {
        if (Source.IsDisposed) return;
        bounds = new Native.RECT(rect.Left, rect.Top, rect.Left + Math.Max(1, rect.Width), rect.Top + Math.Max(1, rect.Height));
        Native.SetWindowPos(Handle, Native.HWND_TOPMOST, bounds.Left, bounds.Top, bounds.Width, bounds.Height,
                            Native.SWP_NOACTIVATE | (IsVisible ? Native.SWP_SHOWWINDOW : 0));
    }

    public void SetClickThrough(bool on)
    {
        if (Source.IsDisposed) return;
        if (on) Native.SetExStyle(Handle, Native.WS_EX_TRANSPARENT | Native.WS_EX_LAYERED);
        else Native.SetExStyle(Handle, 0, Native.WS_EX_TRANSPARENT);
    }

    public void SetActivatable(bool on)
    {
        if (Source.IsDisposed) return;
        if (on) Native.SetExStyle(Handle, 0, Native.WS_EX_NOACTIVATE);
        else Native.SetExStyle(Handle, Native.WS_EX_NOACTIVATE);
    }

    /// <summary>Keeps this window out of screenshots and recordings (Windows 10 2004+).</summary>
    public void ExcludeFromCapture()
    {
        if (!Source.IsDisposed) Native.SetWindowDisplayAffinity(Handle, Native.WDA_EXCLUDEFROMCAPTURE);
    }

    /// <summary>Starts a native window drag, as if the title bar had been grabbed.</summary>
    public void BeginDrag()
    {
        Native.ReleaseCapture();
        Native.SendMessage(Handle, Native.WM_NCLBUTTONDOWN, new IntPtr(Native.HTCAPTION), IntPtr.Zero);
    }

    public void Dispose()
    {
        if (Source.IsDisposed) return;
        IsVisible = false;
        Source.RemoveHook(Hook);
        Source.RootVisual = null;
        Source.Dispose();
        Closed?.Invoke();
    }
}

/// <summary>Monitor geometry in physical pixels plus its DPI scale (DIPs = pixels / scale).</summary>
public sealed record MonitorInfo(IntPtr Handle, Native.RECT Bounds, Native.RECT Work, double Scale, bool Primary)
{
    public int Width => Bounds.Width;
    public int Height => Bounds.Height;

    public static List<MonitorInfo> All()
    {
        var monitors = new List<MonitorInfo>();
        Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitor, _, _, _) =>
        {
            var info = new Native.MONITORINFOEX { Size = Marshal.SizeOf<Native.MONITORINFOEX>() };
            if (Native.GetMonitorInfo(monitor, ref info))
                monitors.Add(new MonitorInfo(monitor, info.Monitor, info.Work, Native.DpiScale(monitor), (info.Flags & 1) != 0));
            return true;
        }, IntPtr.Zero);
        return monitors.OrderByDescending(m => m.Primary).ThenBy(m => m.Bounds.Left).ToList();
    }

    public static MonitorInfo AtCursor()
    {
        var cursor = Native.CursorPosition();
        return At(cursor.X, cursor.Y);
    }

    public static MonitorInfo At(int x, int y)
    {
        var all = All();
        return all.FirstOrDefault(m => m.Bounds.Contains(x, y)) ?? all.First();
    }
}
