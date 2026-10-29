using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Interop;

namespace FastShot;

/// <summary>A global shortcut: a virtual key plus RegisterHotKey modifier flags.</summary>
public sealed record KeyCombo(uint VirtualKey, uint Modifiers)
{
    public const uint VK_SNAPSHOT = 0x2C;
    public static readonly KeyCombo PrintScreen = new(VK_SNAPSHOT, 0);
    public static readonly KeyCombo ShiftPrintScreen = new(VK_SNAPSHOT, Native.MOD_SHIFT);
    public static readonly KeyCombo ControlShift2 = new(0x32, Native.MOD_CONTROL | Native.MOD_SHIFT);
    public static readonly KeyCombo ControlShift7 = new(0x37, Native.MOD_CONTROL | Native.MOD_SHIFT);

    /// <summary>Keys that may be used without a modifier: Print Screen, Insert, Pause, F1–F24.</summary>
    static bool Standalone(uint vk) => vk is VK_SNAPSHOT or 0x2D or 0x13 or (>= 0x70 and <= 0x87);

    /// <summary>A recorded key press, or null for plain typing keys that would hijack text input.</summary>
    public static KeyCombo? FromKey(Key key, ModifierKeys modifiers)
    {
        if (key is Key.None or Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt
            or Key.LWin or Key.RWin or Key.System or Key.ImeProcessed or Key.DeadCharProcessed) return null;
        var vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        if (vk == 0) return null;
        uint flags = 0;
        if (modifiers.HasFlag(ModifierKeys.Control)) flags |= Native.MOD_CONTROL;
        if (modifiers.HasFlag(ModifierKeys.Shift)) flags |= Native.MOD_SHIFT;
        if (modifiers.HasFlag(ModifierKeys.Alt)) flags |= Native.MOD_ALT;
        if (modifiers.HasFlag(ModifierKeys.Windows)) flags |= Native.MOD_WIN;
        if (!Standalone(vk) && (flags == 0 || flags == Native.MOD_SHIFT)) return null;
        return new KeyCombo(vk, flags);
    }

    public string DisplayString
    {
        get
        {
            var text = new StringBuilder();
            if ((Modifiers & Native.MOD_CONTROL) != 0) text.Append("Ctrl+");
            if ((Modifiers & Native.MOD_ALT) != 0) text.Append("Alt+");
            if ((Modifiers & Native.MOD_SHIFT) != 0) text.Append("Shift+");
            if ((Modifiers & Native.MOD_WIN) != 0) text.Append("Win+");
            text.Append(Name(VirtualKey));
            return text.ToString();
        }
    }

    public override string ToString() => DisplayString;

    public static string Name(uint vk)
    {
        switch (vk)
        {
            case VK_SNAPSHOT: return "Print Screen";
            case 0x2D: return "Insert";
            case 0x13: return "Pause";
            case 0x20: return "Space";
            case 0x0D: return "Enter";
            case 0x09: return "Tab";
            case 0x08: return "Backspace";
            case 0x2E: return "Delete";
            case 0x1B: return "Esc";
            case 0x25: return "←";
            case 0x26: return "↑";
            case 0x27: return "→";
            case 0x28: return "↓";
            case 0x24: return "Home";
            case 0x23: return "End";
            case 0x21: return "Page Up";
            case 0x22: return "Page Down";
        }
        if (vk is >= 0x70 and <= 0x87) return $"F{vk - 0x70 + 1}";
        if (vk is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A) return ((char)vk).ToString();
        if (vk is >= 0x60 and <= 0x69) return $"Num {vk - 0x60}";
        var scan = Native.MapVirtualKey(vk, 0);
        var buffer = new StringBuilder(64);
        if (scan != 0 && Native.GetKeyNameText((int)(scan << 16), buffer, buffer.Capacity) > 0) return buffer.ToString();
        return $"#{vk}";
    }
}

/// <summary>Registers system-wide shortcuts on a message-only window. The same window receives
/// "open settings" and "open this file" requests from a second copy of FastShot.</summary>
public sealed class HotKeyCenter
{
    public const string WindowTitle = "FastShot.MessageWindow";
    static HotKeyCenter? shared;
    /// <summary>Created on first use, so a process that only signals another FastShot opens no window of its own.</summary>
    public static HotKeyCenter Shared => shared ??= new();

    /// <summary>Registers the app's shortcuts again after a suspension ends.</summary>
    public Action? Reregister { get; set; }
    public Action? OnReopen { get; set; }
    public Action<string>? OnOpenFile { get; set; }
    /// <summary>A file or folder handed over by "FastShot çarkı" in a second copy of FastShot.</summary>
    public Action<string>? OnConvertFile { get; set; }
    public const string ConvertPrefix = "convert:";
    /// <summary>A menu command (screenshot, record, …) an embedded FastShot passes on while this one runs; see Embedding.</summary>
    public Action<string>? OnCommand { get; set; }
    public const string CommandPrefix = "command:";
    /// <summary>Asks a FastShot to quit (the installer does, before replacing it). One inside Xloer refuses.</summary>
    public const string QuitRequest = CommandPrefix + "quit";
    /// <summary>Runs inside another app, which decides when it quits.</summary>
    public bool Embedded { get; set; }

    /// <summary>Shortcuts that could not be registered because another app already owns them.</summary>
    public HashSet<KeyCombo> Failed { get; } = [];
    public event Action? FailedChanged;

    readonly HwndSource source;
    readonly Dictionary<int, Action> handlers = [];
    int nextId = 1;
    int suspensions;

    public bool IsSuspended => suspensions > 0;

    HotKeyCenter()
    {
        source = new HwndSource(new HwndSourceParameters(WindowTitle) { ParentWindow = Native.HWND_MESSAGE, WindowStyle = 0, Width = 0, Height = 0 });
        source.AddHook(Hook);
    }

    public static uint ReopenMessage { get; } = Native.RegisterWindowMessage("FastShot.Reopen");

    IntPtr Hook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == Native.WM_HOTKEY && handlers.TryGetValue(wParam.ToInt32(), out var handler))
        {
            handled = true;
            handler();
        }
        else if (message == (int)ReopenMessage)
        {
            handled = true;
            OnReopen?.Invoke();
        }
        else if (message == Native.WM_COPYDATA)
        {
            handled = true;
            var data = Marshal.PtrToStructure<Native.COPYDATASTRUCT>(lParam);
            if (data.Pointer != IntPtr.Zero && data.Length > 0)
            {
                var path = Marshal.PtrToStringUni(data.Pointer, data.Length / 2).TrimEnd('\0');
                if (path == QuitRequest && Embedded) return new IntPtr(2);
                if (path.StartsWith(ConvertPrefix, StringComparison.Ordinal))
                    Application.Current.Dispatcher.BeginInvoke(() => OnConvertFile?.Invoke(path[ConvertPrefix.Length..]));
                else if (path.StartsWith(CommandPrefix, StringComparison.Ordinal))
                    Application.Current.Dispatcher.BeginInvoke(() => OnCommand?.Invoke(path[CommandPrefix.Length..]));
                else
                    Application.Current.Dispatcher.BeginInvoke(() => OnOpenFile?.Invoke(path));
            }
            return new IntPtr(1);
        }
        return IntPtr.Zero;
    }

    /// <summary>Returns false when another app already owns the combination.</summary>
    public bool Register(KeyCombo combo, Action handler)
    {
        var id = nextId++;
        if (!Native.RegisterHotKey(source.Handle, id, combo.Modifiers | Native.MOD_NOREPEAT, combo.VirtualKey)) return false;
        handlers[id] = handler;
        return true;
    }

    public void UnregisterAll()
    {
        foreach (var id in handlers.Keys) Native.UnregisterHotKey(source.Handle, id);
        handlers.Clear();
    }

    public void SetFailed(IEnumerable<KeyCombo> failed)
    {
        Failed.Clear();
        foreach (var combo in failed) Failed.Add(combo);
        FailedChanged?.Invoke();
    }

    /// <summary>Releases every shortcut so its keys reach the app normally: while a shortcut is being
    /// recorded, or while the capture overlay uses shortcuts of its own. Calls nest.</summary>
    public void Suspend()
    {
        suspensions++;
        if (suspensions == 1) UnregisterAll();
    }

    public void Resume()
    {
        if (suspensions == 0) return;
        suspensions--;
        if (suspensions == 0) Reregister?.Invoke();
    }

    /// <summary>Hands an open request to a FastShot that is already running. Returns false when none is.</summary>
    public static bool SignalRunningInstance(string? path)
    {
        var (pid, window) = RunningInstances().FirstOrDefault(i => i.pid != Environment.ProcessId);
        if (window == IntPtr.Zero) return false;
        Native.AllowSetForegroundWindow((uint)pid);
        if (string.IsNullOrEmpty(path))
        {
            Native.PostMessage(window, ReopenMessage, IntPtr.Zero, IntPtr.Zero);
            return true;
        }
        Send(window, path);
        return true;
    }

    /// <summary>Every running FastShot (with its message window): process id and window.</summary>
    public static List<(int pid, IntPtr window)> RunningInstances()
    {
        var found = new List<(int, IntPtr)>();
        for (var window = Native.FindWindowEx(Native.HWND_MESSAGE, IntPtr.Zero, null, WindowTitle); window != IntPtr.Zero;
             window = Native.FindWindowEx(Native.HWND_MESSAGE, window, null, WindowTitle))
        {
            Native.GetWindowThreadProcessId(window, out var owner);
            found.Add(((int)owner, window));
        }
        return found;
    }

    /// <summary>Sends a request to one FastShot's window; false when it refused (a FastShot inside Xloer refuses to quit).</summary>
    public static bool Signal(IntPtr window, string text) => Send(window, text) != 2;

    static long Send(IntPtr window, string path)
    {
        var text = Marshal.StringToHGlobalUni(path + "\0");
        try
        {
            var data = new Native.COPYDATASTRUCT { Data = new IntPtr(1), Length = (path.Length + 1) * 2, Pointer = text };
            return Native.SendMessage(window, Native.WM_COPYDATA, IntPtr.Zero, ref data).ToInt64();
        }
        finally { Marshal.FreeHGlobal(text); }
    }
}
