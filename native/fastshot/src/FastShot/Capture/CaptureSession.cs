namespace FastShot;

/// <summary>A region of one monitor chosen for recording: DIPs relative to the monitor's top-left corner.</summary>
public sealed record RecordRequest(MonitorInfo Monitor, Rect Rect, double Scale)
{
    /// <summary>The region in physical screen pixels.</summary>
    public Native.RECT PixelRect
    {
        get
        {
            var left = Monitor.Bounds.Left + (int)Math.Round(Rect.X * Scale);
            var top = Monitor.Bounds.Top + (int)Math.Round(Rect.Y * Scale);
            return new Native.RECT(left, top, left + (int)Math.Round(Rect.Width * Scale), top + (int)Math.Round(Rect.Height * Scale));
        }
    }
}

/// <summary>One press of the capture shortcut: frozen screens, a selection, optional annotations,
/// then copy / save / pin / OCR / edit / record.</summary>
public sealed class CaptureSession
{
    public enum CaptureMode { Screenshot, Video }

    public CaptureMode Mode { get; }
    public ToolState Tools { get; } = new();
    public AnnotationController Controller { get; }
    public Action<CaptureSession>? OnFinish { get; set; }
    public Action<RecordRequest>? OnRecord { get; set; }
    public Action<CaptureProject>? OnOpenEditor { get; set; }

    readonly List<ScreenShot> shots;
    readonly List<Native.RECT> windowRects;
    readonly List<(PanelWindow window, OverlayView view)> overlays = [];
    readonly IntPtr previousWindow = Native.GetForegroundWindow();
    CaptureProject? project;
    bool finished;

    public OverlayView? ActiveView { get; private set; }

    public CaptureSession(CaptureMode mode, List<ScreenShot> shots, List<Native.RECT> windowRects)
    {
        Mode = mode;
        this.shots = shots;
        this.windowRects = windowRects;
        Controller = new AnnotationController(Tools);
    }

    public void Begin()
    {
        foreach (var shot in shots)
        {
            var view = new OverlayView(this, shot, shot.Monitor.Scale);
            var window = new PanelWindow(view, shot.Monitor.Bounds, transparent: false);
            var scale = window.Scale;
            if (Math.Abs(scale - view.Scale) > 0.001) view.SetScale(scale);
            var bounds = shot.Monitor.Bounds;
            view.WindowRects = windowRects.Select(r =>
            {
                var local = new Rect((r.Left - bounds.Left) / scale, (r.Top - bounds.Top) / scale, r.Width / scale, r.Height / scale);
                return Rect.Intersect(local, new Rect(0, 0, view.Width, view.Height));
            }).Where(r => !r.IsEmpty && r.Width >= 20 && r.Height >= 20).ToList();
            overlays.Add((window, view));
        }
        foreach (var (window, _) in overlays) window.Show(activate: false);
        var cursor = Native.CursorPosition();
        var front = overlays.FirstOrDefault(o => o.view.Shot.Monitor.Bounds.Contains(cursor.X, cursor.Y));
        if (front.window is null && overlays.Count > 0) front = overlays[0];
        if (front.window is null) return;
        front.window.Activate();
        front.view.Focus();
        Keyboard.Focus(front.view);
    }

    public void SelectionBegan(OverlayView view)
    {
        if (ActiveView == view) return;
        ActiveView?.ResetSelection();
        ActiveView = view;
        Controller.Load([]);
        var window = overlays.FirstOrDefault(o => o.view == view).window;
        window?.Activate();
        foreach (var (_, other) in overlays) other.InvalidateVisual();
    }

    public bool HasAnnotations => Controller.Annotations.Count > 0;

    double PointScale => ActiveView?.Scale ?? 1;

    /// <summary>The selection with annotations burned in.</summary>
    BitmapSource? RenderedImage()
    {
        if (ActiveView is not { } view || view.Selection is not { } rect || view.CroppedImage() is not { } crop) return null;
        view.CanvasView?.EndTextEditing();
        return ImageExporter.Flatten(crop, rect.Size, Controller.Annotations);
    }

    // MARK: Actions

    public void Copy()
    {
        if (RenderedImage() is not { } image) return;
        Output.CopyImage(image, PointScale);
        StoreInLibrary(image);
        Sounds.Play(Sound.Shutter);
        Finish();
        Toast.Show("Panoya kopyalandı", Icons.Copy);
    }

    public void Save()
    {
        if (RenderedImage() is not { } image) return;
        try
        {
            var path = Output.QuickSave(image, PointScale);
            if (Preferences.Shared.CopyWhenSaving) Output.CopyImage(image, PointScale);
            StoreInLibrary(image);
            Sounds.Play(Sound.Shutter);
            Finish();
            Toast.Show("Kaydedildi", Icons.Save, Path.GetFileName(path), "Göster", () => Output.Reveal(path));
        }
        catch (Exception error)
        {
            Finish();
            Output.ShowError(error, "Görüntü kaydedilemedi");
        }
    }

    public void SaveAs()
    {
        if (RenderedImage() is not { } image) return;
        var prefs = Preferences.Shared;
        SetOverlaysHidden(true);
        var choice = SavePanel.Run(Output.BaseName(), prefs.ImageFolder, SavePanel.ImageFormats, prefs.ImageFormat == ImageFormat.Png ? 0 : 1);
        if (choice is not { } chosen)
        {
            SetOverlaysHidden(false);
            return;
        }
        try
        {
            Output.WriteImage(image, chosen.path, chosen.format == 0 ? ImageFormat.Png : ImageFormat.Jpeg, PointScale);
            StoreInLibrary(image);
            Finish();
            Toast.Show("Kaydedildi", Icons.Save, Path.GetFileName(chosen.path), "Göster", () => Output.Reveal(chosen.path));
        }
        catch (Exception error)
        {
            SetOverlaysHidden(false);
            Output.ShowError(error, "Görüntü kaydedilemedi");
        }
    }

    public void OpenEditor()
    {
        if (RenderedImage() is not { } image || StoreInLibrary(image) is not { } stored) return;
        Finish(restoreFocus: false);
        OnOpenEditor?.Invoke(stored);
    }

    public void Pin()
    {
        if (ActiveView is not { } view || view.Selection is not { } rect || RenderedImage() is not { } image) return;
        var monitor = view.Shot.Monitor.Bounds;
        var s = view.Scale;
        var left = monitor.Left + (int)Math.Round(rect.X * s);
        var top = monitor.Top + (int)Math.Round(rect.Y * s);
        var frame = new Native.RECT(left, top, left + image.PixelWidth, top + image.PixelHeight);
        StoreInLibrary(image);
        Finish(restoreFocus: false);
        PinWindow.Show(image, s, frame);
    }

    public void RecognizeText()
    {
        if (ActiveView?.CroppedImage() is not { } crop) return;
        Finish();
        _ = RecognizeAsync(crop);
    }

    /// <summary>Recognises the selection's text as paragraphs and opens it in the translation window.</summary>
    public void TranslateText()
    {
        if (ActiveView?.CroppedImage() is not { } crop) return;
        Finish();
        _ = TranslateAsync(crop);
    }

    public static async Task TranslateAsync(BitmapSource image)
    {
        try
        {
            var text = await Ocr.Paragraphs(image);
            if (string.IsNullOrWhiteSpace(text)) Toast.Show("Metin bulunamadı", Icons.Scan);
            else TranslateWindow.Show(text);
        }
        catch (Exception error)
        {
            Output.ShowError(error, "Metin tanınamadı");
        }
    }

    static async Task RecognizeAsync(BitmapSource crop)
    {
        try
        {
            var text = await TextRecognizer.Recognize(crop);
            if (string.IsNullOrWhiteSpace(text))
            {
                Toast.Show("Metin bulunamadı", Icons.Scan);
            }
            else
            {
                Output.CopyText(text);
                var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
                Toast.Show("Metin panoya kopyalandı", Icons.Scan, $"{lines} satır");
            }
        }
        catch (Exception error)
        {
            Output.ShowError(error, "Metin tanınamadı");
        }
    }

    public void Record()
    {
        if (ActiveView is not { } view || view.Selection is not { } rect) return;
        var request = new RecordRequest(view.Shot.Monitor, rect, view.Scale);
        Finish(restoreFocus: true);
        OnRecord?.Invoke(request);
    }

    public void Cancel() => Finish();

    void SetOverlaysHidden(bool hidden)
    {
        foreach (var (window, _) in overlays)
        {
            if (hidden) window.Hide(); else window.Show(activate: false);
        }
        if (!hidden && ActiveView is { } view)
        {
            overlays.FirstOrDefault(o => o.view == view).window?.Activate();
            Keyboard.Focus(view.CanvasView ?? (IInputElement)view);
        }
    }

    public void Finish(bool restoreFocus = true)
    {
        if (finished) return;
        finished = true;
        ActiveView?.CanvasView?.EndTextEditing();
        Mouse.OverrideCursor = null;
        foreach (var (window, _) in overlays) window.Dispose();
        overlays.Clear();
        OnFinish?.Invoke(this);
        // Give the keyboard back to the window that was in front before the overlay.
        if (restoreFocus && previousWindow != IntPtr.Zero && Native.IsWindow(previousWindow)) Native.SetForegroundWindow(previousWindow);
    }

    /// <summary>Keeps the capture and its marks so it can be reopened and edited later.</summary>
    CaptureProject? StoreInLibrary(BitmapSource rendered)
    {
        if (ActiveView is not { } view || view.Selection is not { } rect || view.CroppedImage() is not { } crop) return null;
        var library = AppController.Shared.Library;
        try
        {
            var stored = project ?? library.MakeProject(CaptureKind.image, rect.Size, PointScale, "png");
            if (project is null) File.WriteAllBytes(library.MediaPath(stored), ImageExporter.Encode(crop, ImageFormat.Png, pointScale: PointScale));
            stored.CanvasSize = rect.Size;
            stored.Annotations = Controller.Annotations.Select(a => a.Clone()).ToList();
            library.Save(stored);
            library.WriteThumbnail(rendered, stored);
            project = stored;
            AppController.Shared.NotifyLibraryChanged();
            return stored;
        }
        catch (Exception error)
        {
            System.Diagnostics.Debug.WriteLine($"FastShot: could not store capture: {error}");
            return null;
        }
    }
}
