using System.Diagnostics;
using System.Text.Json;
using System.Windows.Threading;

namespace FastShot;

/// <summary>FastShot.exe --embedded &lt;pid&gt;: the whole of FastShot run inside another app (Xloer starts it), with no tray
/// icon of its own. Shortcuts, capture, recording, editors, history, settings and the file wheel all work as usual.
///
/// The host talks to it over the standard streams, one line each:
/// - stdin: commands (screenshot, record, edit-last, history, open, wheel, settings). End of input means the host closed.
/// - stdout: JSON state for the host's menu: {"type":"state","screenshot":"Print Screen","record":"…","recording":false}.
/// FastShot also quits when the host's process ends. While a standalone FastShot runs, the embedded one waits and
/// passes the host's commands on to it; it takes over once that one quits.</summary>
public sealed class Embedding
{
    public const string Argument = "--embedded";

    readonly Process? parent;
    readonly StreamWriter? output;
    readonly object gate = new();
    Action<string> handler;
    Action? ended;
    bool closed;

    Embedding(int parentId)
    {
        try { parent = Process.GetProcessById(parentId); }
        catch (ArgumentException) { parent = null; }
        if (Console.IsOutputRedirected)
        {
            try { output = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true, NewLine = "\n" }; }
            catch (IOException) { output = null; }
        }
        // Until FastShot runs here, commands go to the FastShot that does.
        handler = line => HotKeyCenter.SignalRunningInstance(HotKeyCenter.CommandPrefix + line);
    }

    /// <summary>Takes --embedded &lt;pid&gt; out of the arguments; null when it is not there.</summary>
    public static Embedding? From(ref string[] args)
    {
        var index = Array.IndexOf(args, Argument);
        if (index < 0) return null;
        var pid = index + 1 < args.Length && int.TryParse(args[index + 1], out var value) ? value : -1;
        args = [.. args.Take(index), .. args.Skip(index + 2)];
        return new Embedding(pid);
    }

    public bool HostGone => closed || parent is null || HasExited(parent);

    static bool HasExited(Process process)
    {
        try
        {
            process.Refresh();
            return process.HasExited;
        }
        catch (InvalidOperationException) { return true; }
    }

    /// <summary>Starts reading the host's commands. A host that gives no stdin is watched only by its process.</summary>
    public void Listen()
    {
        if (!Console.IsInputRedirected) return;
        var reader = new Thread(() =>
        {
            try
            {
                using var input = new StreamReader(Console.OpenStandardInput());
                while (input.ReadLine() is { } line)
                {
                    var command = line.Trim();
                    if (command.Length == 0) continue;
                    Action<string> target;
                    lock (gate) target = handler;
                    target(command);
                }
            }
            catch (IOException) { /* the pipe broke: the host is gone */ }
            End();
        }) { IsBackground = true, Name = "FastShot host commands" };
        reader.Start();
    }

    void End()
    {
        Action? callback;
        lock (gate)
        {
            closed = true;
            callback = ended;
        }
        callback?.Invoke();
    }

    /// <summary>FastShot runs here now: commands go to <paramref name="app"/>, and it quits when the host does.</summary>
    public void Attach(AppController app, Dispatcher dispatcher)
    {
        lock (gate)
        {
            handler = command => dispatcher.BeginInvoke(() => app.RunCommand(command));
            ended = () => dispatcher.BeginInvoke(app.Quit);
        }
        if (closed) dispatcher.BeginInvoke(app.Quit);
        var watch = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromSeconds(1) };
        watch.Tick += (_, _) =>
        {
            if (parent is not null && !HasExited(parent)) return;
            watch.Stop();
            app.Quit();
        };
        watch.Start();
    }

    /// <summary>Tells the host what its FastShot menu should show.</summary>
    public void Report(string? screenshot, string? record, bool recording)
    {
        if (output is null) return;
        var line = JsonSerializer.Serialize(new { type = "state", screenshot, record, recording });
        try
        {
            lock (gate) output.WriteLine(line);
        }
        catch (IOException) { /* the host stopped listening */ }
        catch (ObjectDisposedException) { /* closing */ }
    }
}
