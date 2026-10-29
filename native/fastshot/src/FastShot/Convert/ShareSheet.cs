using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace FastShot;

/// <summary>Windows' Share panel (Nearby Sharing, Mail, Teams…) for files and folders, the counterpart of AirDrop on the Mac.
/// A desktop app shares through a window of its own, so a tiny invisible one is opened at the pointer while the panel is up.</summary>
public static class ShareSheet
{
    [ComImport, Guid("3A3DCD6C-3EAB-43DC-BCDE-45671CE800C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDataTransferManagerInterop
    {
        IntPtr GetForWindow(IntPtr appWindow, [In] ref Guid riid);
        void ShowShareUIForWindow(IntPtr appWindow);
    }

    static readonly Guid dataTransferManagerId = new("a5caee9b-8708-49d1-8d36-67d25a8da00c");

    public static void Show(IReadOnlyList<string> paths)
    {
        var cursor = Native.CursorPosition();
        var anchor = new Window
        {
            WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, ShowInTaskbar = false,
            ShowActivated = true, Topmost = true, ResizeMode = ResizeMode.NoResize, Width = 1, Height = 1,
            WindowStartupLocation = WindowStartupLocation.Manual, Left = -32000, Top = -32000, Title = "FastShot paylaş",
        };
        anchor.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(anchor).Handle;
            Native.SetWindowPos(hwnd, IntPtr.Zero, cursor.X, cursor.Y, 1, 1, 0x0010 /* SWP_NOACTIVATE */ | 0x0004 /* SWP_NOZORDER */);
        };
        anchor.Show();
        var handle = new WindowInteropHelper(anchor).Handle;
        // Long enough for any share target; the window is invisible, so leaving it a while costs nothing.
        var close = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
        close.Tick += (_, _) =>
        {
            close.Stop();
            anchor.Close();
        };
        try
        {
            var interop = DataTransferManager.As<IDataTransferManagerInterop>();
            var id = dataTransferManagerId;
            var manager = WinRT.MarshalInterface<DataTransferManager>.FromAbi(interop.GetForWindow(handle, ref id));
            manager.DataRequested += async (_, args) =>
            {
                var deferral = args.Request.GetDeferral();
                try
                {
                    var items = new List<IStorageItem>();
                    foreach (var path in paths)
                        items.Add(Directory.Exists(path) ? await StorageFolder.GetFolderFromPathAsync(path) : await StorageFile.GetFileFromPathAsync(path));
                    var data = args.Request.Data;
                    data.Properties.Title = paths.Count == 1 ? Path.GetFileName(paths[0].TrimEnd('\\')) : $"{paths.Count} öğe";
                    data.Properties.Description = "FastShot ile paylaşılıyor";
                    data.SetStorageItems(items, true);
                    data.ShareCompleted += (_, _) => anchor.Dispatcher.BeginInvoke(anchor.Close);
                    data.ShareCanceled += (_, _) => anchor.Dispatcher.BeginInvoke(anchor.Close);
                }
                catch (Exception error)
                {
                    args.Request.FailWithDisplayText(error.Message);
                }
                finally
                {
                    deferral.Complete();
                }
            };
            interop.ShowShareUIForWindow(handle);
            close.Start();
        }
        catch (Exception error)
        {
            anchor.Close();
            Toast.Show("Paylaşım açılamadı", Icons.Clear, error.Message);
        }
    }
}
