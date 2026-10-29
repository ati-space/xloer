using System.Diagnostics;
using System.Windows.Threading;

namespace FastShot;

/// <summary>FastShot.exe --wheel-host &lt;pid&gt;: only the file wheel, for another app to run beside itself (Xloer starts it).
/// No tray icon, shortcuts or capture; it quits when the process that started it ends. "Düzenle" opens the file in the full FastShot.</summary>
public static class WheelHost
{
    public const string Argument = "--wheel-host";

    public static void Run(Application app, int parent)
    {
        Process? owner = null;
        try { owner = Process.GetProcessById(parent); }
        catch (ArgumentException) { /* already gone */ }
        if (owner is null || owner.HasExited)
        {
            app.Shutdown();
            return;
        }
        DragMonitor.Shared.Start();
        FollowSettings(app.Dispatcher);
        var watch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        watch.Tick += (_, _) =>
        {
            owner.Refresh();
            if (!owner.HasExited) return;
            watch.Stop();
            ConversionCenter.Shared.Cancel();
            DragMonitor.Shared.Stop();
            app.Shutdown();
        };
        watch.Start();
    }

    static FileSystemWatcher? settingsWatcher;

    /// <summary>The wheel key and the output folder are set in FastShot's Settings; reload them when FastShot saves.</summary>
    static void FollowSettings(Dispatcher dispatcher)
    {
        var folder = Path.GetDirectoryName(Preferences.Path);
        if (folder is null) return;
        Directory.CreateDirectory(folder);
        var reload = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromMilliseconds(300) };
        reload.Tick += (_, _) =>
        {
            reload.Stop();
            Preferences.Shared.Reload();
        };
        settingsWatcher = new FileSystemWatcher(folder, Path.GetFileName(Preferences.Path)) { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName };
        void Changed() => dispatcher.BeginInvoke(() =>
        {
            reload.Stop();
            reload.Start();
        });
        settingsWatcher.Changed += (_, _) => Changed();
        settingsWatcher.Created += (_, _) => Changed();
        settingsWatcher.Renamed += (_, _) => Changed();
        settingsWatcher.EnableRaisingEvents = true;
    }

    /// <summary>Opens a file in FastShot's editor: in this process when it is the full app, otherwise in FastShot itself
    /// (a running FastShot takes it over, or one starts).</summary>
    public static void OpenInEditor(string path)
    {
        if (AppController.Current is { } controller)
        {
            controller.Open(path);
            return;
        }
        try
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
            start.ArgumentList.Add(path);
            Process.Start(start);
        }
        catch (Exception error)
        {
            Output.ShowError(error, "FastShot açılamadı");
        }
    }
}
