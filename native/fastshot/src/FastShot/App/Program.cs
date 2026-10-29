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

        // Run inside another app (Xloer), which passes its process id; see Embedding.
        var embedding = Embedding.From(ref args);
        if (embedding is null)
        {
            if (args.Contains(Installer.UninstallArgument)) return Installer.Uninstall();
            if (Installer.ShouldInstall(args)) return Installer.Install(quiet: args.Contains(Installer.UpdateArgument));
        }
        using var mutex = new Mutex(true, @"Local\FastShot.SingleInstance", out var firstInstance);
        embedding?.Listen();
        if (!firstInstance && embedding is not null)
        {
            // A standalone FastShot is running and does everything already; take over once it quits.
            if (!WaitForTurn(mutex, embedding)) return 0;
        }
        else if (!firstInstance)
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
        var controller = new AppController(args, embedding);
        app.Startup += (_, _) => controller.Launch();
        return app.Run();
    }

    /// <summary>Waits for the running FastShot to quit, then owns the single-instance mutex. False when the host went first.</summary>
    static bool WaitForTurn(Mutex mutex, Embedding embedding)
    {
        while (!embedding.HostGone)
        {
            try
            {
                if (mutex.WaitOne(1000)) return true;
            }
            catch (AbandonedMutexException)
            {
                return true;
            }
        }
        return false;
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
