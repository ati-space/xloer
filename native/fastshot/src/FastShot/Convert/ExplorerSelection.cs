using System.Runtime.InteropServices;
using System.Text;
using FastShot.Core.Convert;
using Microsoft.Win32;

namespace FastShot;

/// <summary>Reads the files selected in an Explorer window or on the desktop through the Shell.Application automation object.</summary>
public static class ExplorerSelection
{
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int count);

    static string ClassName(IntPtr hwnd)
    {
        var name = new StringBuilder(256);
        Native.GetClassName(hwnd, name, name.Capacity);
        return name.ToString();
    }

    public static bool IsExplorer(IntPtr hwnd)
    {
        var root = GetAncestor(hwnd, 2); // GA_ROOT
        return ClassName(root) is "CabinetWClass" or "ExploreWClass" or "Progman" or "WorkerW";
    }

    public static List<string> SelectedFiles(IntPtr hwnd)
    {
        var files = new List<string>();
        try
        {
            var root = GetAncestor(hwnd, 2);
            var kind = ClassName(root);
            var type = Type.GetTypeFromProgID("Shell.Application");
            if (type is null) return files;
            dynamic shell = Activator.CreateInstance(type)!;
            dynamic windows = shell.Windows();
            dynamic? folderView = null;
            if (kind is "Progman" or "WorkerW")
            {
                object location = 0; // CSIDL_DESKTOP
                object empty = Type.Missing;
                int found;
                dynamic desktop = windows.FindWindowSW(ref location, ref empty, 8 /* SWC_DESKTOP */, out found, 1 /* SWFO_NEEDDISPATCH */);
                folderView = desktop?.Document;
            }
            else if (kind is "CabinetWClass" or "ExploreWClass")
            {
                // Windows 11 tabs share one window; the active tab's folder name is in the window title.
                var title = new StringBuilder(512);
                GetWindowText(root, title, title.Capacity);
                var candidates = new List<dynamic>();
                int count = windows.Count;
                for (var i = 0; i < count; i++)
                {
                    dynamic? window = windows.Item(i);
                    if (window is null) continue;
                    long handle;
                    try { handle = (long)window.HWND; } catch { continue; }
                    if (new IntPtr(handle) == root) candidates.Add(window);
                }
                dynamic? chosen = candidates.FirstOrDefault(w =>
                {
                    try { return title.ToString().StartsWith((string)w.LocationName, StringComparison.CurrentCultureIgnoreCase); }
                    catch { return false; }
                }) ?? candidates.FirstOrDefault();
                folderView = chosen?.Document;
            }
            if (folderView is null) return files;
            dynamic items = folderView.SelectedItems();
            int total = items.Count;
            for (var i = 0; i < total; i++)
            {
                string? path = items.Item(i)?.Path;
                // Files and folders; virtual items (This PC, Control Panel…) have no real path.
                if (path is not null && (File.Exists(path) || Directory.Exists(path))) files.Add(path);
            }
        }
        catch (Exception error)
        {
            System.Diagnostics.Debug.WriteLine($"FastShot: Explorer selection failed: {error.Message}");
        }
        return files;
    }
}

/// <summary>"FastShot çarkı" in Explorer's right-click menu (Windows 11: Daha fazla seçenek göster), per user, for every file and folder.</summary>
public static class ShellIntegration
{
    const string Verb = "FastShot.Wheel";
    /// <summary>Earlier versions added "FastShot ile dönüştür" per supported file type; it is removed in favour of the one verb.</summary>
    const string OldVerb = "FastShot.Convert";

    static readonly string[] keys = [$@"Software\Classes\*\shell\{Verb}", $@"Software\Classes\Directory\shell\{Verb}"];

    public static void Apply(bool enabled)
    {
        foreach (var extension in FileConverter.SupportedExtensions)
        {
            try { Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\SystemFileAssociations\{extension}\shell\{OldVerb}", throwOnMissingSubKey: false); }
            catch { /* a locked key stays */ }
        }
        var exe = Environment.ProcessPath;
        if (exe is null) return;
        var command = $"\"{exe}\" --convert \"%1\"";
        foreach (var path in keys)
        {
            try
            {
                if (!enabled)
                {
                    Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
                    continue;
                }
                using var existing = Registry.CurrentUser.OpenSubKey(path + @"\command");
                if (existing?.GetValue(null) as string == command) continue;
                using var key = Registry.CurrentUser.CreateSubKey(path);
                key.SetValue(null, "FastShot çarkı");
                key.SetValue("Icon", $"\"{exe}\",0");
                // One process per selected item; FastShot gathers them into one wheel.
                key.SetValue("MultiSelectModel", "Player");
                using var commandKey = key.CreateSubKey("command");
                commandKey.SetValue(null, command);
            }
            catch { /* the menu entry is a convenience */ }
        }
    }
}

/// <summary>Explorer's thumbnail (or icon) for a file.</summary>
public static class ShellThumbnail
{
    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr bitmap);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct SIZE { public int Width, Height; }

    [StructLayout(LayoutKind.Sequential)]
    struct BITMAP
    {
        public int Type, Width, Height, WidthBytes;
        public ushort Planes, BitsPixel;
        public IntPtr Bits;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHCreateItemFromParsingName(string path, IntPtr context, [In] ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory factory);

    [DllImport("gdi32.dll")] static extern int GetObject(IntPtr handle, int size, out BITMAP bitmap);

    public static BitmapSource? Get(string path, int size)
    {
        IShellItemImageFactory? factory = null;
        var bitmap = IntPtr.Zero;
        try
        {
            var iid = typeof(IShellItemImageFactory).GUID;
            if (SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out factory) != 0) return null;
            if (factory.GetImage(new SIZE { Width = size, Height = size }, 0, out bitmap) != 0 || bitmap == IntPtr.Zero) return null;
            if (GetObject(bitmap, Marshal.SizeOf<BITMAP>(), out var info) == 0 || info.Bits == IntPtr.Zero || info.BitsPixel != 32) return null;
            var stride = info.WidthBytes;
            var height = Math.Abs(info.Height);
            var pixels = new byte[stride * height];
            Marshal.Copy(info.Bits, pixels, 0, pixels.Length);
            // Shell bitmaps are bottom-up DIBs.
            var flipped = new byte[pixels.Length];
            for (var y = 0; y < height; y++) Buffer.BlockCopy(pixels, y * stride, flipped, (height - 1 - y) * stride, stride);
            var result = BitmapSource.Create(info.Width, height, 96, 96, PixelFormats.Pbgra32, null, flipped, stride);
            result.Freeze();
            return result;
        }
        catch { return null; }
        finally
        {
            if (bitmap != IntPtr.Zero) Native.DeleteObject(bitmap);
            if (factory is not null) Marshal.ReleaseComObject(factory);
        }
    }
}
