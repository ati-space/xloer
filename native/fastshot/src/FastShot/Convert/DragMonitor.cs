using System.Runtime.InteropServices;
using System.Windows.Threading;
using FastShot.Core.Convert;

namespace FastShot;

/// <summary>Notices when files are being dragged in Explorer or on the desktop and the trigger key is tapped meanwhile.
/// It polls the mouse button and the key (no hooks, no permissions), then reads the dragged files from Explorer's selection.
/// FastShot and the wheel it runs inside other apps (--wheel-host, e.g. for Xloer) may run at once; a shared mutex lets
/// only one of them watch, so a drag never opens two wheels. The other takes over when the watching one quits.</summary>
public sealed class DragMonitor
{
    public static DragMonitor Shared { get; } = new();

    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Native.POINT point);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string? className, string? title);

    readonly DispatcherTimer timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(40) };
    bool mouseDown, keyWasDown, triggered;
    Native.POINT pressPoint;
    IntPtr pressWindow;

    readonly Mutex watcher = new(false, @"Local\FastShot.Wheel");
    readonly DispatcherTimer claim = new() { Interval = TimeSpan.FromSeconds(2) };
    bool watching;

    DragMonitor()
    {
        timer.Tick += (_, _) => Tick();
        claim.Tick += (_, _) => Claim();
    }

    public void Start()
    {
        Claim();
        if (!watching) claim.Start();
    }

    public void Stop()
    {
        claim.Stop();
        timer.Stop();
        if (!watching) return;
        watching = false;
        try { watcher.ReleaseMutex(); }
        catch (ApplicationException) { /* not held */ }
    }

    /// <summary>Takes over watching when no other FastShot process does. Runs on the UI thread, which then owns the mutex.</summary>
    void Claim()
    {
        if (watching) return;
        try { watching = watcher.WaitOne(0); }
        catch (AbandonedMutexException) { watching = true; }
        if (!watching) return;
        claim.Stop();
        timer.Start();
    }

    static bool Down(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;

    static int VirtualKey(ConvertModifier modifier) => modifier switch
    {
        ConvertModifier.Control => 0x11,
        ConvertModifier.Alt => 0x12,
        _ => 0x10,
    };

    void Tick()
    {
        var button = GetSystemMetrics(23) != 0 ? 0x02 : 0x01; // SM_SWAPBUTTON → VK_RBUTTON / VK_LBUTTON
        var down = Down(button);
        if (!down)
        {
            if (mouseDown)
            {
                mouseDown = false;
                if (triggered) ConversionWheel.DragEnded();
            }
            triggered = false;
            return;
        }
        // Fire on the key going down during a file drag, not on a key held before the drag.
        var key = Down(VirtualKey(Preferences.Shared.ConvertModifier));
        var tapped = key && !keyWasDown;
        keyWasDown = key;
        if (!mouseDown)
        {
            mouseDown = true;
            triggered = false;
            pressPoint = Native.CursorPosition();
            pressWindow = WindowFromPoint(pressPoint);
            return;
        }
        if (!tapped || !Preferences.Shared.ConvertEnabled) return;
        if (triggered)
        {
            // Each later tap goes to the next wheel: Dönüştür → Araçlar → İşlemler.
            ConversionWheel.NextMode();
            return;
        }
        if (ConversionWheel.IsVisible || !IsFileDrag()) return;
        triggered = true;
        // Explorer is inside its drag loop; ask it for the dragged (selected) items off the UI thread so a slow answer never freezes FastShot.
        var window = pressWindow;
        var dispatcher = timer.Dispatcher;
        var reader = new Thread(() =>
        {
            var files = ExplorerSelection.SelectedFiles(window);
            dispatcher.BeginInvoke(() =>
            {
                if (!mouseDown || !triggered || ConversionWheel.IsVisible || files.Count == 0) return;
                ConversionWheel.Show(files, Native.CursorPosition(), interactive: false);
            });
        }) { IsBackground = true, Name = "FastShot drag files" };
        reader.SetApartmentState(ApartmentState.STA);
        reader.Start();
    }

    /// <summary>Explorer shows its drag image window while files are dragged; a drag that started in Explorer and moved also counts.</summary>
    bool IsFileDrag()
    {
        var image = FindWindow("SysDragImage", null);
        if (image != IntPtr.Zero && Native.IsWindowVisible(image)) return true;
        var cursor = Native.CursorPosition();
        var moved = Math.Abs(cursor.X - pressPoint.X) + Math.Abs(cursor.Y - pressPoint.Y) > 12;
        return moved && ExplorerSelection.IsExplorer(pressWindow);
    }
}
