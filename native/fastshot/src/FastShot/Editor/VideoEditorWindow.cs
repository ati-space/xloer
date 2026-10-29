using System.Diagnostics;
using FastShot.Core.Media;

namespace FastShot;

/// <summary>Plays a recording under an annotation canvas. Marks are drawn at the playhead and live
/// on the timeline, where their start and end can be dragged.</summary>
public sealed class VideoEditorWindow : EditorWindow
{
    readonly VideoPlayback playback;
    readonly TimelineView timeline = new();
    readonly Stopwatch clock = new();
    double duration, current, trimStart, trimEnd, playFrom;
    bool isPlaying, resumeAfterScrub;

    IconButton playButton = null!;
    readonly TextBlock timeText = new() { VerticalAlignment = VerticalAlignment.Center, FontSize = 12, Margin = new Thickness(6, 0, 6, 0) };
    readonly TextBlock trimText = new() { VerticalAlignment = VerticalAlignment.Center, FontSize = 11, Margin = new Thickness(8, 0, 0, 0) };
    readonly TextBlock selectedText = new() { VerticalAlignment = VerticalAlignment.Center, FontSize = 11, Margin = new Thickness(0, 0, 6, 0) };
    readonly StackPanel selectedPanel = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    readonly StackPanel lengthPanel = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    Button resetTrim = null!;

    public static VideoEditorWindow? Create(CaptureProject project, ProjectLibrary library)
    {
        try
        {
            var probe = VideoExporter.Probe(library.MediaPath(project));
            return new VideoEditorWindow(project, library, probe.duration, probe.audio);
        }
        catch (Exception error)
        {
            Debug.WriteLine($"FastShot: cannot open video: {error}");
            return null;
        }
    }

    VideoEditorWindow(CaptureProject project, ProjectLibrary library, double probedDuration, bool hasAudio)
        : base(project, library, "FastShot — Video düzenleyici")
    {
        duration = probedDuration > 0 ? probedDuration : project.Duration ?? 0;
        if (Math.Abs(duration - (project.Duration ?? 0)) > 0.01)
        {
            Project.Duration = duration;
            ScheduleSave();
        }
        var prefs = Preferences.Shared;
        Canvas.Letterbox = Theme.Frozen(Color.FromRgb(0x14, 0x14, 0x14));
        Controller.Time = new TimeContext { Duration = duration, DefaultLength = prefs.AnnotationLength > 0 ? prefs.AnnotationLength : null };
        timeline.Controller = Controller;
        timeline.Palette = Palette;
        ApplyDuration();
        Bottom = BuildBottom();
        UpdateBottomHeight();

        timeline.OnSeek = Seek;
        timeline.OnTrim = SetTrim;
        timeline.OnScrubbing = scrubbing =>
        {
            if (scrubbing)
            {
                resumeAfterScrub = isPlaying;
                Pause();
            }
            else if (resumeAfterScrub)
            {
                Play();
            }
        };
        var canvasDisplay = Controller.OnDisplay;
        Controller.OnDisplay = () =>
        {
            canvasDisplay?.Invoke();
            timeline.InvalidateVisual();
            RefreshSelectedTiming();
        };
        var saveOnChange = Controller.OnChange;
        Controller.OnChange = () =>
        {
            saveOnChange?.Invoke();
            UpdateBottomHeight();
        };
        Controller.OnSelectionChange = RefreshSelectedTiming;

        playback = new VideoPlayback(library.MediaPath(project), hasAudio) { OnFrame = (image, _) => Canvas.BackgroundImage = image };
        playback.Seek(0);
        LoadThumbnails();
        CompositionTarget.Rendering += Tick;
        PreviewTextInput += (_, e) =>
        {
            if (e.OriginalSource is TextBox) return;
            if (e.Text == "[") { SetSelectedTiming(start: true); e.Handled = true; }
            else if (e.Text == "]") { SetSelectedTiming(start: false); e.Handled = true; }
        };
        Closed += (_, _) =>
        {
            CompositionTarget.Rendering -= Tick;
            playback.Dispose();
        };
        RefreshSelectedTiming();
        UpdateTexts();
    }

    protected override double ExtraHeight => 44 + TimelineView.PreferredHeight(Project.Annotations.Count);
    protected override bool SupportsGif => true;

    void ApplyDuration()
    {
        if (Controller.Time is { } time) time.Duration = duration;
        timeline.Duration = duration;
        var range = Project.TrimRange ?? (0, Math.Max(duration, 0));
        trimStart = range.start;
        trimEnd = range.end;
        timeline.TrimStart = trimStart;
        timeline.TrimEnd = trimEnd;
    }

    void UpdateBottomHeight() => timeline.Height = TimelineView.PreferredHeight(Controller.Annotations.Count);

    // MARK: Transport

    FrameworkElement BuildBottom()
    {
        var p = Palette;
        playButton = new IconButton(new IconView(Icons.Play, 15), "Oynat / duraklat (Boşluk)", TogglePlay, p, 32);
        timeText.Foreground = p.Foreground;
        trimText.Foreground = p.Secondary;
        selectedText.Foreground = p.Secondary;

        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(playButton);
        left.Children.Add(timeText);
        left.Children.Add(Ui.VSeparator(p));
        left.Children.Add(Ui.Label("Kırp", p, 11, secondary: true, weight: FontWeights.SemiBold));
        left.Children.Add(SmallButton("Başlangıç", "Kırpma başlangıcını oynatma çubuğuna al (I)", SetTrimStartHere));
        left.Children.Add(SmallButton("Bitiş", "Kırpma bitişini oynatma çubuğuna al (O)", SetTrimEndHere));
        resetTrim = SmallButton("Sıfırla", "Kırpmayı kaldır", () => SetTrim(0, duration));
        left.Children.Add(resetTrim);
        left.Children.Add(trimText);

        selectedPanel.Children.Add(selectedText);
        selectedPanel.Children.Add(SmallButton("Burada başlasın", "Seçili çizim oynatma çubuğunda görünsün ([)", () => SetSelectedTiming(start: true)));
        selectedPanel.Children.Add(SmallButton("Burada bitsin", "Seçili çizim oynatma çubuğunda kaybolsun (])", () => SetSelectedTiming(start: false)));

        lengthPanel.Children.Add(Ui.Label("Yeni çizim süresi", p, 11, secondary: true));
        var picker = new ComboBox { Margin = new Thickness(8, 0, 0, 0), MinWidth = 130, VerticalAlignment = VerticalAlignment.Center };
        foreach (var (title, value) in new[] { ("1 sn", 1.0), ("2 sn", 2.0), ("3 sn", 3.0), ("5 sn", 5.0), ("10 sn", 10.0), ("Sonuna kadar", 0.0) })
        {
            var item = new ComboBoxItem { Content = title, Tag = value };
            picker.Items.Add(item);
            if (Math.Abs(Preferences.Shared.AnnotationLength - value) < 0.01) picker.SelectedItem = item;
        }
        picker.SelectionChanged += (_, _) =>
        {
            if (picker.SelectedItem is not ComboBoxItem { Tag: double length }) return;
            Preferences.Shared.AnnotationLength = length;
            if (Controller.Time is { } time) time.DefaultLength = length > 0 ? length : null;
            Canvas.Focus();
        };
        lengthPanel.Children.Add(picker);

        var right = new Grid { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(selectedPanel);
        right.Children.Add(lengthPanel);

        var transportGrid = new Grid();
        transportGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        transportGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        transportGrid.Children.Add(left);
        Grid.SetColumn(right, 1);
        transportGrid.Children.Add(right);
        var transport = new Border
        {
            Child = transportGrid, Height = 44, Padding = new Thickness(12, 0, 12, 0), Background = p.Panel,
            BorderBrush = p.Border, BorderThickness = new Thickness(0, 1, 0, 0),
        };

        var stack = new DockPanel();
        DockPanel.SetDock(transport, Dock.Top);
        stack.Children.Add(transport);
        stack.Children.Add(timeline);
        return stack;
    }

    static Button SmallButton(string title, string help, Action action)
    {
        var button = new Button { Content = title, ToolTip = help, Focusable = false, Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(9, 2, 9, 3), FontSize = 12, MinHeight = 0 };
        button.Click += (_, _) => action();
        return button;
    }

    void UpdateTexts()
    {
        timeText.Text = $"{Ui.Format(current)} / {Ui.Format(duration)}";
        trimText.Text = $"{Ui.Format(trimStart)} – {Ui.Format(trimEnd)}";
        resetTrim.IsEnabled = !(trimStart < 0.01 && trimEnd > duration - 0.01);
    }

    // MARK: Playback

    void Tick(object? sender, EventArgs e)
    {
        if (!isPlaying) return;
        var now = playFrom + clock.Elapsed.TotalSeconds;
        if (now >= trimEnd - 0.02)
        {
            Pause();
            Seek(trimEnd);
            return;
        }
        SetCurrentTime(now);
    }

    void SetCurrentTime(double time)
    {
        current = time;
        if (Controller.Time is { } context) context.Current = time;
        timeline.CurrentTime = time;
        UpdateTexts();
        Canvas.InvalidateVisual();
    }

    void TogglePlay()
    {
        if (isPlaying) Pause(); else Play();
    }

    void Play()
    {
        if (isPlaying || Exporting) return;
        Canvas.EndTextEditing();
        if (current < trimStart || current >= trimEnd - 0.05) SetCurrentTime(trimStart);
        playFrom = current;
        clock.Restart();
        isPlaying = true;
        playback.Play(current, trimEnd);
        playButton.SetIcon(new IconView(Icons.Pause, 15));
    }

    void Pause()
    {
        if (!isPlaying) return;
        var now = Math.Min(trimEnd, playFrom + clock.Elapsed.TotalSeconds);
        isPlaying = false;
        clock.Stop();
        playback.Pause();
        SetCurrentTime(now);
        playback.Seek(now);
        playButton.SetIcon(new IconView(Icons.Play, 15));
    }

    void Seek(double time)
    {
        var clamped = Math.Min(Math.Max(0, time), Math.Max(0, duration));
        SetCurrentTime(clamped);
        if (!isPlaying) playback.Seek(clamped);
    }

    void LoadThumbnails()
    {
        var path = Library.MediaPath(Project);
        var length = duration;
        var thread = new Thread(() =>
        {
            try
            {
                using var reader = new VideoReader(path);
                var total = reader.Duration > 0 ? reader.Duration : length;
                var images = new List<BitmapSource>();
                const int count = 14;
                for (var i = 0; i < count; i++)
                {
                    if (reader.FrameAt(total * (i + 0.5) / count) is { } frame) images.Add(ImageExporter.Scaled(frame.ToBitmap(), 160));
                }
                Dispatcher.BeginInvoke(() => timeline.Thumbnails = images);
            }
            catch (Exception error)
            {
                Debug.WriteLine($"FastShot: thumbnails failed: {error}");
            }
        }) { IsBackground = true, Name = "FastShot thumbnails" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    // MARK: Trim and timing

    void SetTrim(double start, double end)
    {
        trimStart = start;
        trimEnd = end;
        timeline.TrimStart = start;
        timeline.TrimEnd = end;
        Project.TrimStart = start > 0.01 ? start : null;
        Project.TrimEnd = end < duration - 0.01 ? end : null;
        UpdateTexts();
        ScheduleSave();
    }

    void SetTrimStartHere() => SetTrim(Math.Min(current, trimEnd - 0.1), trimEnd);
    void SetTrimEndHere() => SetTrim(trimStart, Math.Max(current, trimStart + 0.1));

    void SetSelectedTiming(bool start)
    {
        if (Controller.Selected is not { } selected) return;
        var now = current;
        Controller.RegisterUndo();
        Controller.Update(selected.Id, a =>
        {
            if (start)
            {
                a.Start = Math.Min(now, (a.End ?? duration) - 0.1);
            }
            else
            {
                var end = Math.Max(now, (a.Start ?? 0) + 0.1);
                a.End = end >= duration - 0.01 ? null : end;
            }
        });
    }

    void RefreshSelectedTiming()
    {
        if (Controller.Selected is { } a)
        {
            selectedText.Text = $"Seçili çizim: {Ui.Format(a.Start ?? 0)} – {Ui.Format(a.End ?? duration)}";
            selectedPanel.Visibility = Visibility.Visible;
            lengthPanel.Visibility = Visibility.Collapsed;
        }
        else
        {
            selectedPanel.Visibility = Visibility.Collapsed;
            lengthPanel.Visibility = Visibility.Visible;
        }
    }

    // MARK: Keys

    protected override bool HandleKey(KeyEventArgs e)
    {
        var modifiers = Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt);
        var key = EditorShortcuts.KeyOf(e);
        if (modifiers == ModifierKeys.None)
        {
            switch (key)
            {
                case Key.Space: TogglePlay(); return true;
                case Key.I: SetTrimStartHere(); return true;
                case Key.O: SetTrimEndHere(); return true;
            }
        }
        if (Controller.Selected is null && modifiers is ModifierKeys.None or ModifierKeys.Shift)
        {
            var step = modifiers == ModifierKeys.Shift ? 1.0 : 1.0 / 30;
            if (key == Key.Left) { Pause(); Seek(current - step); return true; }
            if (key == Key.Right) { Pause(); Seek(current + step); return true; }
        }
        return base.HandleKey(e);
    }

    // MARK: Export

    async void Export(string path, VideoFormat format, Action<string> completion)
    {
        if (Exporting) return;
        Canvas.EndTextEditing();
        SaveProject();
        Pause();
        SetExporting(true);
        var request = new VideoExportRequest
        {
            Source = Library.MediaPath(Project), CanvasSize = Project.CanvasSize,
            Annotations = Controller.Annotations.Select(a => a.Clone()).ToList(),
            Trim = Project.TrimRange, PreferHEVC = Preferences.Shared.VideoCodec == VideoCodec.hevc,
        };
        try
        {
            await VideoExporter.ExportAsync(request, path, format, fraction => Dispatcher.BeginInvoke(() => SetProgress(fraction)));
            SetExporting(false);
            completion(path);
        }
        catch (Exception error)
        {
            SetExporting(false);
            Output.ShowError(error, "Video dışa aktarılamadı");
        }
    }

    protected override void Save()
    {
        var folder = Preferences.Shared.VideoFolder;
        Export(Output.UniquePath(folder, Output.BaseName(Project.CreatedAt), "mp4"), VideoFormat.Mp4, SavedToast);
    }

    protected override void SaveGif()
    {
        var folder = Preferences.Shared.VideoFolder;
        Export(Output.UniquePath(folder, Output.BaseName(Project.CreatedAt), "gif"), VideoFormat.Gif, SavedToast);
    }

    protected override void SaveAs()
    {
        if (SavePanel.Run(Output.BaseName(Project.CreatedAt), Preferences.Shared.VideoFolder, SavePanel.VideoFormats, owner: this) is not { } choice) return;
        Export(choice.path, choice.format == 0 ? VideoFormat.Mp4 : VideoFormat.Gif, SavedToast);
    }

    protected override void CopyToClipboard()
    {
        var folder = Path.Combine(Path.GetTempPath(), "FastShot");
        var path = Path.Combine(folder, $"{Output.BaseName(Project.CreatedAt)}.mp4");
        Export(path, VideoFormat.Mp4, done =>
        {
            Output.CopyFile(done);
            Toast.Show("Video panoya kopyalandı", Icons.Copy, "Bir klasöre veya sohbete yapıştırabilirsiniz");
        });
    }
}
