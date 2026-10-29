using System.Diagnostics;

namespace FastShot;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--make-icon")
        {
            AppIcon.WriteIco(args[1]);
            return 0;
        }

        // Only the file wheel, for another app (Xloer) that passes its process id; see WheelHost.
        var host = Array.IndexOf(args, WheelHost.Argument);
        if (host >= 0)
        {
            if (host + 1 >= args.Length || !int.TryParse(args[host + 1], out var parent)) return 2;
            using var hostMutex = new Mutex(true, $@"Local\FastShot.WheelHost.{parent}", out var firstHost);
            if (!firstHost) return 0;
            var hostApp = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            Theme.Apply(hostApp);
            hostApp.DispatcherUnhandledException += (_, e) =>
            {
                Log(e.Exception);
                e.Handled = true;
            };
            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                Log(e.Exception);
                e.SetObserved();
            };
            hostApp.Startup += (_, _) => WheelHost.Run(hostApp, parent);
            return hostApp.Run();
        }

        using var mutex = new Mutex(true, @"Local\FastShot.SingleInstance", out var firstInstance);
        if (!firstInstance)
        {
            // Opening FastShot again shows its settings (or opens the file it was given) in the copy already running.
            // Explorer's "FastShot çarkı" starts one copy per selected item; each hands its path over.
            var files = args.Where(a => !a.StartsWith('-')).Select(Path.GetFullPath).ToList();
            if (args.Contains("--convert"))
            {
                foreach (var file in files) HotKeyCenter.SignalRunningInstance(HotKeyCenter.ConvertPrefix + file);
                return 0;
            }
            HotKeyCenter.SignalRunningInstance(files.FirstOrDefault());
            return 0;
        }

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Theme.Apply(app);
        app.DispatcherUnhandledException += (_, e) =>
        {
            Log(e.Exception);
            e.Handled = true;
            Output.ShowError(e.Exception, "Beklenmeyen bir hata oluştu");
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => { if (e.ExceptionObject is Exception error) Log(error); };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log(e.Exception);
            e.SetObserved();
        };
        var controller = new AppController(args);
        app.Startup += (_, _) => controller.Launch();
        return app.Run();
    }

    static void Log(Exception error)
    {
        try
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FastShot");
            Directory.CreateDirectory(folder);
            File.AppendAllText(Path.Combine(folder, "errors.log"), $"{DateTime.Now:O} {error}\n\n");
        }
        catch { /* nowhere to report */ }
        Debug.WriteLine(error);
    }
}
