using System.Diagnostics;
using System.IO;

namespace FastShot.Core.Convert;

/// <summary>Optional command-line encoders for formats Windows cannot write itself (WebP, AVIF; MP3/FLAC where missing).
/// Found on the PATH or in the usual winget / scoop / chocolatey locations.</summary>
public static class ExternalTools
{
    static readonly Lazy<string?> ffmpeg = new(() => Find("ffmpeg.exe"));

    public static string? Ffmpeg => ffmpeg.Value;

    static string? Find(string name)
    {
        var folders = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        folders.Add(Path.Combine(local, "Microsoft", "WinGet", "Links"));
        folders.Add(Path.Combine(profile, "scoop", "shims"));
        folders.Add(@"C:\ProgramData\chocolatey\bin");
        folders.Add(@"C:\ffmpeg\bin");
        foreach (var folder in folders)
        {
            try
            {
                var candidate = Path.Combine(folder.Trim('"'), name);
                if (File.Exists(candidate)) return candidate;
            }
            catch { /* malformed PATH entry */ }
        }
        // winget installs ffmpeg into a versioned package folder.
        try
        {
            var packages = Path.Combine(local, "Microsoft", "WinGet", "Packages");
            if (Directory.Exists(packages))
                return Directory.EnumerateFiles(packages, name, SearchOption.AllDirectories).FirstOrDefault();
        }
        catch { /* not readable */ }
        return null;
    }

    public static void Run(string tool, IEnumerable<string> arguments, string name, TimeSpan? timeout = null, CancellationToken cancellation = default)
    {
        var info = new ProcessStartInfo(tool) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw ConversionException.WriteFailed(name);
        process.OutputDataReceived += (_, _) => { };
        process.ErrorDataReceived += (_, _) => { };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        var limit = timeout ?? TimeSpan.FromMinutes(30);
        var clock = Stopwatch.StartNew();
        while (!process.WaitForExit(200))
        {
            if (cancellation.IsCancellationRequested || clock.Elapsed > limit)
            {
                try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
                cancellation.ThrowIfCancellationRequested();
                throw ConversionException.WriteFailed(name);
            }
        }
        if (process.ExitCode != 0) throw ConversionException.WriteFailed(name);
    }
}
