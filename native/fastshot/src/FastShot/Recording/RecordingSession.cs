using System.Diagnostics;
using System.Windows.Shapes;
using System.Windows.Threading;
using FastShot.Core.Media;

namespace FastShot;

/// <summary>Records one screen region. FastShot's recording chrome (frame, controls, live drawing
/// layer) is excluded from the capture, so marks drawn live during the recording are kept as
/// timed, editable annotations instead of being baked into the pixels.</summary>
public sealed class RecordingSession
{
    public RecordRequest Request { get; }
    public ToolState Tools { get; } = new(EditorTool.Pen);
    public AnnotationController Controller { get; }
    public Action<CaptureProject?>? OnFinish { get; set; }
    public Action<double>? OnTick { get; set; }

    readonly ProjectLibrary library = AppController.Shared.Library;
    readonly Preferences prefs = Preferences.Shared;
    readonly Stopwatch clock = new();
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    readonly List<AudioSource> audioSources = [];
    PanelWindow? border, drawPanel, hud, countdown;
    Rectangle? borderShape;
    CanvasView? canvas;
    RecordingHud? hudView;
    CaptureProject? project;
    VideoWriter? writer;
    AudioMixer? mixer;
    Thread? captureThread, audioThread;
    volatile bool stopRequested;
    bool stopping, cancelled, finished, recording, drawing;
    double lastFrameTime, frameInterval;
    IntPtr previousWindow;
    string? captureError;

    public RecordingSession(RecordRequest request)
    {
        Request = request;
        Controller = new AnnotationController(Tools)
        {
            AllowedTools = EditorTools.Live,
            Time = new TimeContext { Live = true },
            Clock = () => Elapsed,
        };
        timer.Tick += (_, _) =>
        {
            hudView?.SetElapsed(recording ? Elapsed : null);
            if (recording) OnTick?.Invoke(Elapsed);
            if (captureError is { } error && !stopping)
            {
                captureError = null;
                Output.ShowError(new InvalidOperationException(error), "Kayıt durdu");
                Stop();
            }
        };
    }

    public double Elapsed => clock.Elapsed.TotalSeconds;

    // MARK: Lifecycle

    public async Task Start()
    {
        ShowChrome();
        timer.Start();
        if (prefs.Countdown > 0) await RunCountdown(prefs.Countdown);
        if (cancelled) return;
        try
        {
            BeginCapture();
        }
        catch (Exception error)
        {
            StopAudio();
            writer?.Dispose();
            writer = null;
            if (project is not null) try { library.Delete(project); } catch { /* ignore */ }
            Teardown();
            Output.ShowError(error, "Kayıt başlatılamadı");
            Finish(null);
        }
    }

    static int Even(double value) => Math.Max(2, (int)value & ~1);

    void BeginCapture()
    {
        var region = Request.PixelRect;
        double width = region.Width, height = region.Height;
        // Hardware encoders top out around 4096 × 2304; larger regions are scaled down to fit.
        const double longLimit = 4096, shortLimit = 2304;
        var factor = width >= height ? Math.Min(1, Math.Min(longLimit / width, shortLimit / height)) : Math.Min(1, Math.Min(shortLimit / width, longLimit / height));
        // Encoders also refuse tiny frames.
        factor = Math.Max(factor, Math.Max(64 / width, 64 / height));
        var outWidth = Even(width * factor);
        var outHeight = Even(height * factor);
        var fps = Math.Clamp(prefs.FramesPerSecond, 10, 60);
        frameInterval = 1.0 / fps;

        project = library.MakeProject(CaptureKind.video, Request.Rect.Size, outWidth / Request.Rect.Width, "mp4");
        project.Duration = 0;

        var wantsAudio = prefs.RecordSystemAudio || prefs.RecordMicrophone;
        writer = new VideoWriter(library.MediaPath(project), outWidth, outHeight, fps, prefs.VideoCodec == VideoCodec.hevc,
                                 audio: wantsAudio ? AudioFormat.Standard : null);

        var start100ns = AudioSource.Now100ns();
        clock.Restart();
        if (writer.Audio is not null)
        {
            mixer = new AudioMixer();
            if (prefs.RecordSystemAudio)
            {
                if (AudioSource.TryStart(AudioSource.Kind.System, mixer, start100ns, out var error) is { } source) audioSources.Add(source);
                else Toast.Show("Sistem sesi kaydedilemiyor", Icons.Mute, error);
            }
            if (prefs.RecordMicrophone)
            {
                if (AudioSource.TryStart(AudioSource.Kind.Microphone, mixer, start100ns, out var error) is { } source) audioSources.Add(source);
                else Toast.Show("Mikrofon kullanılamıyor, sessiz kaydediliyor", Icons.MicOff, error);
            }
            audioThread = new Thread(AudioLoop) { IsBackground = true, Name = "FastShot audio mix" };
            audioThread.Start();
        }

        captureThread = new Thread(() => CaptureLoop(region, outWidth, outHeight)) { IsBackground = true, Name = "FastShot capture", Priority = ThreadPriority.AboveNormal };
        captureThread.Start();
        recording = true;
        Sounds.Play(Sound.RecordStart);
        hudView?.SetRecording(true);
    }

    void CaptureLoop(Native.RECT region, int width, int height)
    {
        Native.timeBeginPeriod(1);
        try
        {
            using var capture = new GdiCapture(region.Width, region.Height, width, height);
            var next = 0.0;
            while (!stopRequested)
            {
                var now = Elapsed;
                if (now < next)
                {
                    var wait = next - now;
                    if (wait > 0.002) Thread.Sleep(Math.Max(1, (int)(wait * 1000) - 1));
                    else Thread.SpinWait(200);
                    continue;
                }
                var time = Elapsed;
                capture.Grab(region.Left, region.Top, prefs.ShowCursor);
                writer!.WriteFrame(capture.Bits, capture.Stride, time, frameInterval);
                lastFrameTime = time;
                next += frameInterval;
                // Fell far behind (system busy): skip ahead instead of bursting.
                if (Elapsed - next > 0.5) next = Elapsed + frameInterval;
            }
        }
        catch (Exception error)
        {
            captureError = error.Message;
        }
        finally
        {
            Native.timeEndPeriod(1);
        }
    }

    void AudioLoop()
    {
        try
        {
            while (!stopRequested)
            {
                Thread.Sleep(50);
                // Stay a little behind real time so late packets still land in place.
                var until = (long)((Elapsed - 0.3) * AudioMixer.Rate);
                if (until <= mixer!.Flushed) continue;
                var (pcm, time) = mixer.Flush(until);
                writer!.WriteAudio(pcm, time);
            }
        }
        catch (Exception error)
        {
            captureError ??= error.Message;
        }
    }

    void StopAudio()
    {
        foreach (var source in audioSources) source.Dispose();
        audioSources.Clear();
    }

    public void Stop()
    {
        if (stopping || finished) return;
        if (writer is null)
        {
            cancelled = true;
            Teardown();
            Finish(null);
            return;
        }
        stopping = true;
        canvas?.EndTextEditing();
        var recordedFor = Elapsed;
        stopRequested = true;
        hudView?.SetStopping();
        var dispatcher = Dispatcher.CurrentDispatcher;
        var thread = new Thread(() =>
        {
            Exception? failure = null;
            try
            {
                captureThread?.Join();
                audioThread?.Join();
                StopAudio();
                if (mixer is not null && writer!.Audio is not null)
                {
                    var end = (long)((lastFrameTime + frameInterval) * AudioMixer.Rate);
                    var (pcm, time) = mixer.Flush(end);
                    writer.WriteAudio(pcm, time);
                }
                writer!.Finish();
            }
            catch (Exception error)
            {
                failure = error;
            }
            dispatcher.BeginInvoke(() => Finalize(recordedFor, failure));
        }) { IsBackground = true, Name = "FastShot finish recording" };
        thread.Start();
    }

    public void Cancel()
    {
        cancelled = true;
        if (writer is not null) Stop();
        else
        {
            Teardown();
            Finish(null);
        }
    }

    void Finalize(double recordedFor, Exception? failure)
    {
        Teardown();
        Sounds.Play(Sound.RecordStop);
        if (project is not { } stored)
        {
            Finish(null);
            return;
        }
        var path = library.MediaPath(stored);
        if (cancelled || failure is not null || !File.Exists(path))
        {
            try { library.Delete(stored); } catch { /* ignore */ }
            if (!cancelled) Output.ShowError(failure ?? new IOException("Kayıt dosyası oluşturulamadı."), "Kayıt kaydedilemedi");
            Finish(null);
            return;
        }
        try
        {
            var probe = VideoExporter.Probe(path);
            var duration = probe.duration > 0 ? probe.duration : recordedFor;
            stored.Duration = duration;
            if (probe.width > 0) stored.PixelScale = probe.width / stored.CanvasSize.Width;
            stored.Annotations = Controller.Annotations.Select(mark =>
            {
                var copy = mark.Clone();
                copy.Start = Math.Min(mark.Start ?? 0, duration);
                return copy;
            }).ToList();
            library.Save(stored);
            if (VideoExporter.Frame(path, Math.Min(0.5, duration / 2), 800) is { } frame) library.WriteThumbnail(frame, stored);
            AppController.Shared.NotifyLibraryChanged();
            Finish(stored);
        }
        catch (Exception error)
        {
            Output.ShowError(error, "Kayıt kaydedilemedi");
            Finish(null);
        }
    }

    void Finish(CaptureProject? result)
    {
        if (finished) return;
        finished = true;
        timer.Stop();
        OnFinish?.Invoke(result);
    }

    // MARK: Windows

    void ShowChrome()
    {
        var region = Request.PixelRect;
        var s = Request.Scale;
        var pad = (int)Math.Ceiling(4 * s);

        borderShape = new Rectangle { Stroke = Theme.Red, StrokeThickness = 2, StrokeDashArray = [4, 2.5], Margin = new Thickness(1) };
        border = new PanelWindow(new Grid { Children = { borderShape } },
                                 new Native.RECT(region.Left - pad, region.Top - pad, region.Right + pad, region.Bottom + pad),
                                 transparent: true, activatable: false, clickThrough: true);
        border.ExcludeFromCapture();
        border.Show(activate: false);

        canvas = new CanvasView(Controller, Request.Rect.Size)
        {
            DrawsBackground = false,
            FitsToBounds = false,
            Width = Request.Rect.Width,
            Height = Request.Rect.Height,
            OnKey = e =>
            {
                if (e.Key != Key.Escape) return false;
                SetDrawing(false);
                return true;
            },
        };
        drawPanel = new PanelWindow(canvas, region, transparent: true, activatable: true, clickThrough: true);
        drawPanel.ExcludeFromCapture();
        drawPanel.Show(activate: false);

        hudView = new RecordingHud(this);
        hud = new PanelWindow(hudView.Root, new Native.RECT(0, 0, 10, 10), transparent: true, activatable: false);
        hud.ExcludeFromCapture();
        PlaceHud();
        hud.Show(activate: false);
    }

    /// <summary>Below the region when there's room, otherwise above, otherwise inside its bottom edge.</summary>
    public void PlaceHud()
    {
        if (hud is null || hudView is null) return;
        hudView.Root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var s = hud.Scale;
        var width = (int)Math.Ceiling(hudView.Root.DesiredSize.Width * s);
        var height = (int)Math.Ceiling(hudView.Root.DesiredSize.Height * s);
        var region = Request.PixelRect;
        var work = Request.Monitor.Work;
        var gap = (int)(6 * s);
        var x = region.Left + (region.Width - width) / 2;
        var y = region.Bottom + gap;
        if (y + height > work.Bottom)
        {
            y = region.Top - gap - height;
            if (y < work.Top) y = region.Bottom - gap - height;
        }
        x = Math.Min(Math.Max(work.Left, x), work.Right - width);
        hud.SetBounds(new Native.RECT(x, y, x + width, y + height));
    }


    public void ToggleDrawing() => SetDrawing(!drawing);

    void SetDrawing(bool on)
    {
        if (on && !recording) return;
        drawing = on;
        if (borderShape is not null) borderShape.Stroke = on ? Theme.Accent : Theme.Red;
        hudView?.SetDrawing(on);
        if (drawPanel is null || canvas is null) return;
        drawPanel.SetClickThrough(!on);
        // A nearly invisible fill makes the layered window catch clicks; it is excluded from the recording anyway.
        canvas.Letterbox = on ? Theme.Frozen(Color.FromArgb(1, 0, 0, 0)) : null;
        if (on)
        {
            previousWindow = Native.GetForegroundWindow();
            drawPanel.Activate();
            canvas.Focus();
            Keyboard.Focus(canvas);
            canvas.Cursor = Cursors.Cross;
        }
        else
        {
            canvas.EndTextEditing();
            Controller.Select(null);
            if (previousWindow != IntPtr.Zero && Native.IsWindow(previousWindow)) Native.SetForegroundWindow(previousWindow);
        }
    }

    public void ClearMarks() => Controller.CloseOpenAnnotations(Elapsed);

    async Task RunCountdown(int seconds)
    {
        var number = new TextBlock { FontSize = 64, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var content = new Grid { Width = 140, Height = 140 };
        content.Children.Add(new Ellipse { Fill = Theme.Frozen(Color.FromArgb(0xA6, 0, 0, 0)), Margin = new Thickness(5) });
        content.Children.Add(number);
        var region = Request.PixelRect;
        var size = (int)Math.Ceiling(140 * Request.Scale);
        var x = region.Left + (region.Width - size) / 2;
        var y = region.Top + (region.Height - size) / 2;
        countdown = new PanelWindow(content, new Native.RECT(x, y, x + size, y + size), transparent: true, activatable: false, clickThrough: true);
        countdown.ExcludeFromCapture();
        countdown.Show(activate: false);
        for (var value = seconds; value > 0 && !cancelled; value--)
        {
            number.Text = value.ToString();
            await Task.Delay(1000);
        }
        countdown?.Dispose();
        countdown = null;
    }

    void Teardown()
    {
        timer.Stop();
        canvas?.EndTextEditing();
        foreach (var window in new[] { border, drawPanel, hud, countdown }) window?.Dispose();
        border = drawPanel = hud = countdown = null;
        canvas = null;
        recording = false;
    }
}

/// <summary>The control bar under the region: elapsed time, stop, live drawing tools, cancel.</summary>
public sealed class RecordingHud
{
    public FrameworkElement Root { get; }
    readonly RecordingSession session;
    readonly TextBlock time = new() { FontSize = 13, FontWeight = FontWeights.SemiBold, MinWidth = 58, VerticalAlignment = VerticalAlignment.Center, Foreground = UiPalette.Hud.Foreground };
    readonly Border dot = new() { Width = 9, Height = 9, CornerRadius = new CornerRadius(4.5), Background = Theme.Red, Opacity = 0.35, Margin = new Thickness(8, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
    readonly IconButton drawToggle;
    readonly StackPanel drawingTools = new() { Orientation = Orientation.Horizontal, Visibility = Visibility.Collapsed };
    readonly PillButton stop;

    public RecordingHud(RecordingSession session)
    {
        this.session = session;
        var p = UiPalette.Hud;
        var tools = session.Tools;
        time.Text = "Hazırlanıyor…";
        stop = new PillButton("Bitir", new IconView(Icons.Stop, 12), "Kaydı bitir (video kısayoluna tekrar basabilirsiniz)", Theme.Red, session.Stop) { Margin = new Thickness(2, 0, 2, 0) };
        drawToggle = new IconButton(new IconView(IconShape.DrawToggle, 18), "Ekrana çiz", session.ToggleDrawing, p, 28) { IsEnabled = false };

        var buttons = new Dictionary<EditorTool, IconButton>();
        foreach (var tool in EditorTools.Live)
        {
            var chosen = tool;
            var button = new IconButton(new IconView(tool, 16), tool.Help(), () => tools.Tool = chosen, p, 26);
            buttons[tool] = button;
            drawingTools.Children.Add(button);
        }
        drawingTools.Children.Add(Ui.VSeparator(p));
        drawingTools.Children.Add(new ColorSwatches(tools, [RGBAColor.Red, RGBAColor.Orange, RGBAColor.Yellow, RGBAColor.Green, RGBAColor.Blue, RGBAColor.White], 6, 11) { VerticalAlignment = VerticalAlignment.Center });
        drawingTools.Children.Add(new SizePicker(tools, p, 16) { VerticalAlignment = VerticalAlignment.Center });
        var undo = new IconButton(new IconView(Icons.Undo, 13), "Geri al (Ctrl+Z)", session.Controller.Undo, p, 26);
        drawingTools.Children.Add(undo);
        drawingTools.Children.Add(new IconButton(new IconView(Icons.Delete, 13), "Ekrandaki çizimleri kaldır", session.ClearMarks, p, 26));
        void Refresh()
        {
            foreach (var (tool, button) in buttons) button.Selected = tools.Tool == tool;
            undo.IsEnabled = tools.CanUndo;
        }
        tools.Changed += Refresh;
        Refresh();

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(dot);
        row.Children.Add(time);
        row.Children.Add(stop);
        row.Children.Add(Ui.VSeparator(p));
        row.Children.Add(drawToggle);
        row.Children.Add(drawingTools);
        row.Children.Add(Ui.VSeparator(p));
        row.Children.Add(new IconButton(new IconView(Icons.Close, 13), "Kaydı iptal et", session.Cancel, p, 26));
        Root = Ui.Hud(row, 12, 5);
    }

    public void SetRecording(bool on)
    {
        dot.Opacity = on ? 1 : 0.35;
        drawToggle.IsEnabled = on;
    }

    public void SetElapsed(double? seconds)
    {
        if (seconds is { } value)
        {
            var total = (int)value;
            time.Text = $"{total / 60:00}:{total % 60:00}";
        }
    }

    public void SetStopping()
    {
        time.Text = "Kaydediliyor…";
        drawToggle.IsEnabled = false;
    }

    public void SetDrawing(bool on)
    {
        drawToggle.Selected = on;
        drawToggle.ToolTip = on ? "Çizimi kapat (Esc)" : "Ekrana çiz";
        drawingTools.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        Root.Dispatcher.BeginInvoke(session.PlaceHud, DispatcherPriority.Loaded);
    }
}
