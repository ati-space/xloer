using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace FastShot;

/// <summary>FastShot is one exe that installs itself. The downloaded FastShot-Setup-x64.exe (or any copy started with --install)
/// copies itself to %LOCALAPPDATA%\Programs\FastShot, adds a Start menu shortcut and an entry in Settings › Apps, and starts the
/// installed copy. Per user, so neither installing nor updating asks for administrator rights. The updater runs a newer setup
/// with --update, which does the same without asking. --uninstall (from Settings › Apps) removes it again and keeps the history.</summary>
public static class Installer
{
    public const string InstallArgument = "--install", UpdateArgument = "--update", UninstallArgument = "--uninstall";
    /// <summary>Passed to the installed copy after an update, so it can say so.</summary>
    public const string UpdatedArgument = "--updated";
    public const string SetupName = "FastShot-Setup-x64.exe";

    const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\FastShot";

    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "FastShot");
    public static string InstalledExe => Path.Combine(Folder, "FastShot.exe");
    static string Shortcut => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "FastShot.lnk");

    /// <summary>This process is the installed copy (not a download, a dev build or Xloer's copy).</summary>
    public static bool RunningInstalled => string.Equals(Environment.ProcessPath, InstalledExe, StringComparison.OrdinalIgnoreCase);

    public static Version CurrentVersion
    {
        get
        {
            var version = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);
            return new Version(version.Major, version.Minor, Math.Max(0, version.Build));
        }
    }

    /// <summary>A setup file, or --install / --update, that is not already the installed copy.</summary>
    public static bool ShouldInstall(string[] args)
    {
        if (RunningInstalled) return false;
        if (args.Contains(InstallArgument) || args.Contains(UpdateArgument)) return true;
        return Path.GetFileName(Environment.ProcessPath ?? "").StartsWith("FastShot-Setup", StringComparison.OrdinalIgnoreCase);
    }

    public static int Install(bool quiet)
    {
        var source = Environment.ProcessPath!;
        var updating = File.Exists(InstalledExe);
        if (!quiet)
        {
            var question = updating
                ? $"FastShot {CurrentVersion.ToString(3)} kurulu FastShot'ın yerine geçecek. Ayarlarınız ve Geçmiş'iniz korunur."
                : $"FastShot {CurrentVersion.ToString(3)} bu bilgisayara kurulacak:\n{Folder}\n\nBaşlat menüsüne eklenir, Windows açılınca başlar ve yeni sürümleri kendisi denetler. Kaldırmak için: Ayarlar › Uygulamalar › FastShot.";
            if (MessageBox.Show(question, updating ? "FastShot'ı güncelle" : "FastShot'ı kur", MessageBoxButton.OKCancel, MessageBoxImage.Information) != MessageBoxResult.OK)
                return 1;
        }
        try
        {
            QuitRunningCopies();
            Directory.CreateDirectory(Folder);
            // A copy that would not quit still lets itself be renamed; the new one goes in its place.
            if (File.Exists(InstalledExe))
            {
                var old = InstalledExe + ".old";
                try { File.Delete(old); } catch { /* still in use: leave it */ }
                try { File.Move(InstalledExe, old, overwrite: true); } catch { File.Delete(InstalledExe); }
            }
            File.Copy(source, InstalledExe);
            // The download's "from the internet" mark would bring SmartScreen back on every start.
            DeleteFile(InstalledExe + ":Zone.Identifier");
            CreateShortcut();
            Register();
            // A tray tool starts with Windows; the setting stays as the user left it on an update.
            if (!updating || Preferences.LaunchAtLogin) Preferences.LaunchAtLogin = true;
            Process.Start(new ProcessStartInfo(InstalledExe) { UseShellExecute = false, ArgumentList = { updating ? UpdatedArgument : "--installed" } });
            return 0;
        }
        catch (Exception error)
        {
            MessageBox.Show($"FastShot kurulamadı:\n{error.Message}", "FastShot", MessageBoxButton.OK, MessageBoxImage.Error);
            return 1;
        }
    }

    public static int Uninstall()
    {
        var question = "FastShot bu bilgisayardan kaldırılsın mı?\n\nGeçmiş'teki yakalamalarınız ve ayarlarınız silinmez:\n"
                       + Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FastShot");
        if (MessageBox.Show(question, "FastShot'ı kaldır", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return 1;
        QuitRunningCopies();
        try { File.Delete(Shortcut); } catch { /* already gone */ }
        if (Preferences.LaunchAtLogin) Preferences.LaunchAtLogin = false;
        ShellIntegration.Apply(false);
        if (Preferences.Shared.PrintScreenTakenOver)
        {
            try { PrintScreenSetting.OpensSnippingTool = true; } catch { /* the user can switch it back in Windows' settings */ }
        }
        Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false);
        // This exe is in the folder; a detached shell removes it once this process has ended.
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c timeout /t 2 /nobreak >nul & rmdir /s /q \"{Folder}\"")
        {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
        });
        return 0;
    }

    /// <summary>Asks every other standalone FastShot (the installed one, a downloaded copy…) to quit and ends one that does not,
    /// so the new copy can take over. FastShot inside Xloer refuses and keeps running.</summary>
    static void QuitRunningCopies()
    {
        var self = Environment.ProcessId;
        var quitting = new List<Process>();
        foreach (var (pid, window) in HotKeyCenter.RunningInstances())
        {
            if (pid == self || !HotKeyCenter.Signal(window, HotKeyCenter.QuitRequest)) continue;
            try { quitting.Add(Process.GetProcessById(pid)); }
            catch (ArgumentException) { /* already ended */ }
        }
        foreach (var process in quitting)
        {
            try
            {
                // Versions before 1.1.0 do not know the request.
                if (!process.WaitForExit(10_000)) process.Kill();
                process.WaitForExit(5_000);
            }
            catch (InvalidOperationException) { /* already ended */ }
        }
    }

    static void CreateShortcut()
    {
        var type = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("Kısayol oluşturulamadı.");
        dynamic shell = Activator.CreateInstance(type)!;
        dynamic link = shell.CreateShortcut(Shortcut);
        link.TargetPath = InstalledExe;
        link.WorkingDirectory = Folder;
        link.Description = "Ekran görüntüsü, ekran kaydı ve dosya çarkı";
        link.IconLocation = $"{InstalledExe},0";
        link.Save();
        Marshal.FinalReleaseComObject(link);
        Marshal.FinalReleaseComObject(shell);
    }

    /// <summary>The entry in Settings › Apps (and the old Control Panel), with the uninstaller.</summary>
    static void Register()
    {
        using var key = Registry.CurrentUser.CreateSubKey(UninstallKey);
        key.SetValue("DisplayName", "FastShot");
        key.SetValue("DisplayVersion", CurrentVersion.ToString(3));
        key.SetValue("Publisher", "Mericsdc");
        key.SetValue("DisplayIcon", $"\"{InstalledExe}\",0");
        key.SetValue("InstallLocation", Folder);
        key.SetValue("UninstallString", $"\"{InstalledExe}\" {UninstallArgument}");
        key.SetValue("URLInfoAbout", "https://github.com/Mericsdc/FastShot");
        key.SetValue("EstimatedSize", (int)(new FileInfo(InstalledExe).Length / 1024), RegistryValueKind.DWord);
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool DeleteFile(string path);
}
