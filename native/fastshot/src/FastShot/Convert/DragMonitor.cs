using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace FastShot;

/// <summary>Notices when files are being dragged in Explorer or on the desktop and the trigger key is tapped meanwhile.
/// It polls the mouse button and the key (no hooks, no permissions), then reads the dragged files from Explorer's selection.</summary>
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

    DragMonitor() => timer.Tick += (_, _) => Tick();

    public void Start() => timer.Start();
    public void Stop() => timer.Stop();

    static bool Down(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;

    static int VirtualKey(ConvertModifier modifier) => modifier switch
    {
        ConvertModifier.AltGr => 0xA5, // VK_RMENU: AltGr is the right Alt key
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
        var key = Down(VirtualKey(Preferences.Shared.WheelKey));
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
