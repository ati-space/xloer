using System.Diagnostics;
using System.Windows.Threading;
using FastShot.Core.Media;

namespace FastShot;

/// <summary>Decodes a recording on its own thread: exact stills for a paused playhead (requests
/// coalesce while scrubbing) and real-time frames while playing. Sound comes from a MediaPlayer
/// started at the same position.</summary>
public sealed class VideoPlayback : IDisposable
{
    readonly string path;
    readonly Dispatcher dispatcher;
    readonly Thread thread;
    readonly AutoResetEvent signal = new(false);
    readonly object gate = new();
    readonly MediaPlayer? audio;
    Command? pending;
    bool disposed;
    (BitmapSource image, double time)? latest;
    bool posted;

    sealed record Command(bool Play, double Time, double Until, bool Stop = false);

    /// <summary>A decoded frame and its time, on the UI thread.</summary>
    public Action<BitmapSource, double>? OnFrame { get; set; }

    public VideoPlayback(string path, bool hasAudio)
    {
        this.path = path;
        dispatcher = Dispatcher.CurrentDispatcher;
        thread = new Thread(Run) { IsBackground = true, Name = "FastShot playback" };
        thread.Start();
        if (hasAudio)
        {
            audio = new MediaPlayer { Volume = 1 };
            audio.Open(new Uri(path));
        }
    }

    public void Seek(double time) => Post(new Command(false, time, 0));

    public void Play(double from, double until)
    {
        Post(new Command(true, from, until));
        if (audio is null) return;
        audio.Position = TimeSpan.FromSeconds(from);
        audio.Play();
    }

    public void Pause()
    {
        Post(new Command(false, 0, 0, Stop: true));
        audio?.Pause();
    }

    void Post(Command command)
    {
        lock (gate) pending = command;
        signal.Set();
    }

    Command? Take()
    {
        lock (gate)
        {
            var command = pending;
            pending = null;
            return command;
        }
    }

    bool HasPending
    {
        get { lock (gate) return pending is not null; }
    }

    void Run()
    {
        VideoReader? reader = null;
        try
        {
            reader = new VideoReader(path);
            while (!disposed)
            {
                signal.WaitOne();
                if (disposed) break;
                var command = Take();
                while (command is not null && !disposed)
                {
                    if (command.Stop)
                    {
                        command = Take();
                        continue;
                    }
                    if (!command.Play)
                    {
                        if (reader.FrameAt(command.Time) is { } frame) Deliver(frame);
                        command = Take();
                        continue;
                    }
                    PlayFrom(reader, command.Time, command.Until);
                    command = Take();
                }
            }
        }
        catch (Exception error)
        {
            Debug.WriteLine($"FastShot: playback failed: {error}");
        }
        finally
        {
            reader?.Dispose();
        }
    }

    void PlayFrom(VideoReader reader, double from, double until)
    {
        reader.Seek(from);
        var clock = Stopwatch.StartNew();
        while (!disposed && !HasPending)
        {
            var frame = reader.ReadFrame();
            if (frame is null) return;
            if (frame.Time + frame.Duration <= from) continue;
            var due = frame.Time - from;
            while (!disposed && !HasPending)
            {
                var wait = due - clock.Elapsed.TotalSeconds;
                if (wait <= 0.002) break;
                Thread.Sleep(Math.Max(1, (int)(wait * 1000) - 1));
            }
            if (HasPending || disposed) return;
            // Drop frames we are already late for, unless it is the last one.
            if (clock.Elapsed.TotalSeconds - due > frame.Duration * 2) continue;
            Deliver(frame);
            if (frame.Time >= until) return;
        }
    }

    void Deliver(VideoFrame frame)
    {
        var image = frame.ToBitmap();
        lock (gate)
        {
            latest = (image, frame.Time);
            if (posted) return;
            posted = true;
        }
        dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
        {
            (BitmapSource image, double time)? next;
            lock (gate)
            {
                next = latest;
                latest = null;
                posted = false;
            }
            if (next is { } value && !disposed) OnFrame?.Invoke(value.image, value.time);
        });
    }

    public void Dispose()
    {
        disposed = true;
        signal.Set();
        audio?.Stop();
        audio?.Close();
    }
}
