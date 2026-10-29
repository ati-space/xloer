using System.Windows.Threading;
using FastShot.Core.Media;
using Forms = System.Windows.Forms;

namespace FastShot;

/// <summary>Tray app: owns the shortcuts, the notification-area icon and every window. Inside another app
/// (<see cref="Embedding"/>) the icon stays hidden and the host's menu gives the same commands.</summary>
public sealed class AppController
{
    static AppController? shared;
    public static AppController Shared => shared ?? throw new InvalidOperationException("FastShot is not running.");

    public ProjectLibrary Library { get; } = new();
    public event Action? LibraryChanged;

    readonly Preferences prefs = Preferences.Shared;
    readonly string[] launchArguments;
    readonly Embedding? embedding;
    readonly List<EditorWindow> editors = [];
    readonly DispatcherTimer hotkeyDebounce = new() { Interval = TimeSpan.FromMilliseconds(150) };
    Forms.NotifyIcon tray = null!;
    Forms.ContextMenuStrip menu = null!;
    Forms.ToolStripMenuItem screenshotItem = null!, recordItem = null!, lastCaptureItem = null!;
    System.Drawing.Icon idleIcon = null!, recordingIcon = null!;
    CaptureSession? capture;
    RecordingSession? recording;
    SettingsWindow? settings;
    LibraryWindow? libraryWindow;
    bool startingCapture;

    public AppController(string[] arguments, Embedding? embedding = null)
    {
        shared = this;
        launchArguments = arguments;
        this.embedding = embedding;
    }

    /// <summary>Runs inside another app (Xloer) with no tray icon of its own.</summary>
    public bool Embedded => embedding is not null;

    public void Launch()
    {
        BuildTray();
        var center = HotKeyCenter.Shared;
        center.Reregister = RegisterHotkeys;
        center.OnReopen = () => OpenSettings(SettingsTab.Shortcuts);
        center.OnOpenFile = Open;
        center.OnConvertFile = QueueConversion;
        center.OnCommand = RunCommand;
        if (Embedded) TakePrintScreenOnce();
        RegisterHotkeys();
        convertDebounce.Tick += (_, _) =>
        {
            convertDebounce.Stop();
            var files = pendingConversions.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            pendingConversions.Clear();
            if (files.Count > 0) ConversionWheel.Show(files, Native.CursorPosition(), interactive: true);
        };
        hotkeyDebounce.Tick += (_, _) =>
        {
            hotkeyDebounce.Stop();
            RegisterHotkeys();
            UpdateMenuShortcuts();
            ApplyShellIntegration();
            ReportState();
        };
        prefs.Changed += () =>
        {
            hotkeyDebounce.Stop();
            hotkeyDebounce.Start();
        };
        DragMonitor.Shared.Start();
        ApplyShellIntegration();
        WarmUpConverters();
        if (embedding is not null)
        {
            embedding.Attach(this, Application.Current.Dispatcher);
            ReportState();
            // The host explains FastShot in its own words.
            prefs.Onboarded = true;
        }
        var files = launchArguments.Where(a => !a.StartsWith('-')).ToList();
        if (launchArguments.Contains("--convert"))
        {
            // Started by the right-click menu: no welcome, straight to the wheel.
            prefs.Onboarded = true;
            files.ForEach(QueueConversion);
            return;
        }
        if (!prefs.Onboarded)
        {
            prefs.Onboarded = true;
            ShowWelcome();
        }
        foreach (var path in files) Open(path);
    }

    // MARK: Conversion

    readonly List<string> pendingConversions = [];
    readonly DispatcherTimer convertDebounce = new() { Interval = TimeSpan.FromMilliseconds(350) };
    bool? appliedShellIntegration;

    /// <summary>Explorer hands over one file per process; gather the ones that arrive together into one wheel.</summary>
    void QueueConversion(string path)
    {
        pendingConversions.Add(path);
        convertDebounce.Stop();
        convertDebounce.Start();
    }

    void ApplyShellIntegration()
    {
        var enabled = prefs.ConvertContextMenu;
        if (appliedShellIntegration == enabled) return;
        appliedShellIntegration = enabled;
        Task.Run(() => ShellIntegration.Apply(enabled));
    }

    /// <summary>Probes the optional encoders in the background, so the wheel and Settings open instantly.</summary>
    static void WarmUpConverters()
    {
        var thread = new Thread(() =>
        {
            Thread.Sleep(1500);
            try
            {
                foreach (var family in Enum.GetValues<FastShot.Core.Convert.FileFamily>()) FastShot.Core.Convert.FileConverter.Targets(family);
            }
            catch { /* probes are best effort */ }
        }) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "FastShot probes" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    public void NotifyLibraryChanged()
    {
        LibraryChanged?.Invoke();
        libraryWindow?.Reload();
    }

    // MARK: Shortcuts

    public void RegisterHotkeys()
    {
        var center = HotKeyCenter.Shared;
        if (center.IsSuspended) return;
        center.UnregisterAll();
        var bindings = new (KeyCombo? combo, Action action)[]
        {
            (prefs.ScreenshotHotkey, TakeScreenshot),
            (prefs.ScreenshotHotkeyAlt, TakeScreenshot),
            (prefs.RecordHotkey, RecordVideo),
            (prefs.RecordHotkeyAlt, RecordVideo),
        };
        var failed = new List<KeyCombo>();
        foreach (var (combo, action) in bindings)
        {
            if (combo is null) continue;
            if (!center.Register(combo, action)) failed.Add(combo);
        }
        center.SetFailed(failed);
    }

    // MARK: Capture

    public void TakeScreenshot() => StartCapture(CaptureSession.CaptureMode.Screenshot);

    public void RecordVideo()
    {
        if (recording is not null)
        {
            recording.Stop();
            return;
        }
        StartCapture(CaptureSession.CaptureMode.Video);
    }

    void StartCapture(CaptureSession.CaptureMode mode)
    {
        if (capture is not null || startingCapture) return;
        if (mode == CaptureSession.CaptureMode.Video && recording is not null) return;
        startingCapture = true;
        try
        {
            var rects = ScreenGrabber.WindowRects();
            var shots = ScreenGrabber.CaptureAll();
            if (shots.Count == 0) return;
            var session = new CaptureSession(mode, shots, rects);
            session.OnFinish = finished =>
            {
                if (capture == finished) capture = null;
                HotKeyCenter.Shared.Resume();
            };
            session.OnRecord = StartRecording;
            session.OnOpenEditor = OpenEditor;
            capture = session;
            // The overlay's own shortcuts (Ctrl+C, Ctrl+S…) must not be swallowed by global ones.
            HotKeyCenter.Shared.Suspend();
            session.Begin();
        }
        catch (Exception error)
        {
            capture = null;
            Output.ShowError(error, "Ekran görüntüsü alınamadı");
        }
        finally
        {
            startingCapture = false;
        }
    }

    void StartRecording(RecordRequest request)
    {
        if (recording is not null) return;
        var session = new RecordingSession(request);
        recording = session;
        SetRecordingStatus(true);
        session.OnTick = seconds =>
        {
            var total = (int)seconds;
            tray.Text = $"FastShot · Kayıt {total / 60:00}:{total % 60:00} · bitirmek için tıklayın";
        };
        session.OnFinish = project =>
        {
            recording = null;
            SetRecordingStatus(false);
            if (project is null) return;
            if (prefs.OpenEditorAfterRecording) OpenEditor(project);
            else ExportRecording(project);
        };
        _ = session.Start();
    }

    /// <summary>Without the editor, the recording goes straight to the video folder with its live marks burned in.</summary>
    async void ExportRecording(CaptureProject project)
    {
        var path = Output.UniquePath(prefs.VideoFolder, Output.BaseName(project.CreatedAt), "mp4");
        var request = new VideoExportRequest
        {
            Source = Library.MediaPath(project), CanvasSize = project.CanvasSize, Annotations = project.Annotations,
            PreferHEVC = prefs.VideoCodec == VideoCodec.hevc,
        };
        try
        {
            await VideoExporter.ExportAsync(request, path, VideoFormat.Mp4, _ => { });
            Toast.Show("Video kaydedildi", Icons.Video, Path.GetFileName(path), "Göster", () => Output.Reveal(path));
        }
        catch (Exception error)
        {
            Output.ShowError(error, "Video kaydedilemedi");
        }
    }

    void SetRecordingStatus(bool active)
    {
        tray.Icon = active ? recordingIcon : idleIcon;
        tray.Text = active ? "FastShot · Kaydı bitirmek için tıklayın" : "FastShot";
        ReportState();
    }

    // MARK: Inside another app

    /// <summary>The host's menu items: the same commands as the tray menu.</summary>
    public void RunCommand(string command)
    {
        switch (command)
        {
            case "screenshot": TakeScreenshot(); break;
            case "record": RecordVideo(); break;
            case "edit-last": EditLastCapture(); break;
            case "history": ShowLibrary(); break;
            case "open": OpenFile(); break;
            case "wheel": ConversionCenter.Shared.ChooseFiles(); break;
            case "settings": OpenSettings(); break;
        }
    }

    void ReportState() => embedding?.Report(Shortcuts(prefs.ScreenshotHotkeyAlt, prefs.ScreenshotHotkey), Shortcuts(prefs.RecordHotkeyAlt, prefs.RecordHotkey), recording is not null);

    /// <summary>"Print Screen / Ctrl+Shift+2", or null when neither is set.</summary>
    static string? Shortcuts(params KeyCombo?[] combos)
    {
        var text = string.Join(" / ", combos.Where(c => c is not null).Select(c => c!.DisplayString));
        return text.Length > 0 ? text : null;
    }

    /// <summary>Windows 11 gives Print Screen to the Snipping Tool. Inside Xloer, Print Screen should simply take a FastShot
    /// screenshot, so it is taken over once; Settings › Kısayollar gives it back ("Windows'a geri ver").</summary>
    void TakePrintScreenOnce()
    {
        if (prefs.PrintScreenTakenOver || !PrintScreenSetting.OpensSnippingTool) return;
        try
        {
            PrintScreenSetting.OpensSnippingTool = false;
            prefs.PrintScreenTakenOver = true;
        }
        catch { /* the shortcut warning in Settings explains it */ }
    }

    // MARK: Editors and windows

    public void OpenEditor(CaptureProject project)
    {
        if (editors.FirstOrDefault(e => e.Project.Id == project.Id) is { } existing)
        {
            existing.Present();
            return;
        }
        EditorWindow? editor = project.Kind == CaptureKind.image ? ImageEditorWindow.Create(project, Library) : VideoEditorWindow.Create(project, Library);
        if (editor is null)
        {
            Output.ShowError(new InvalidDataException("Orijinal dosya okunamadı ya da bozuk."), "Yakalama açılamadı");
            return;
        }
        editor.OnClose = closed => editors.Remove(closed);
        editors.Add(editor);
        editor.Present();
    }

    public void OpenSettings(SettingsTab? tab = null)
    {
        if (settings is null)
        {
            settings = new SettingsWindow();
            settings.Closed += (_, _) => settings = null;
        }
        if (tab is { } selected) settings.Select(selected);
        Present(settings);
    }

    public void ShowLibrary()
    {
        if (libraryWindow is null)
        {
            libraryWindow = new LibraryWindow(Library, OpenEditor);
            libraryWindow.Closed += (_, _) => libraryWindow = null;
        }
        else
        {
            libraryWindow.Reload();
        }
        Present(libraryWindow);
    }

    static void Present(Window window)
    {
        if (!window.IsVisible) window.Show();
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
        var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        if (handle != IntPtr.Zero) Native.ForceForeground(handle);
    }

    void EditLastCapture()
    {
        if (Library.AllProjects().FirstOrDefault() is { } project) OpenEditor(project);
    }

    void OpenFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Düzenlemek için bir görüntü, video veya FastShot projesi seçin",
            Filter = "Görüntü, video veya proje|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.webp;*.heic;*.mp4;*.mov;*.m4v;*.wmv;*.avi;*.mkv;project.json|"
                     + "Görüntüler|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.webp;*.heic|Videolar|*.mp4;*.mov;*.m4v;*.wmv;*.avi;*.mkv|FastShot projesi (project.json)|project.json",
            Multiselect = false,
        };
        if (dialog.ShowDialog() == true) Open(dialog.FileName);
    }

    void OpenProjectFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Bir .fastshot proje klasörü seçin" };
        if (dialog.ShowDialog() == true) Open(dialog.FolderName);
    }

    public void Open(string path)
    {
        try
        {
            var project = Import(path);
            NotifyLibraryChanged();
            OpenEditor(project);
        }
        catch (Exception error)
        {
            Output.ShowError(error, "Dosya açılamadı");
        }
    }

    static readonly HashSet<string> videoExtensions = [".mp4", ".mov", ".m4v", ".wmv", ".avi", ".mkv", ".webm"];

    CaptureProject Import(string path)
    {
        if (Directory.Exists(path)) return Library.ImportPackage(path);
        if (Path.GetFileName(path).Equals("project.json", StringComparison.OrdinalIgnoreCase)) return Library.ImportPackage(Path.GetDirectoryName(path)!);
        if (!File.Exists(path)) throw new FileNotFoundException("Dosya bulunamadı.", path);
        if (videoExtensions.Contains(Path.GetExtension(path).ToLowerInvariant()))
        {
            var probe = VideoExporter.Probe(path);
            var scale = probe.width > 2400 ? 2.0 : 1.0;
            var project = Library.ImportMedia(path, CaptureKind.video, new Size(probe.width / scale, probe.height / scale), scale, probe.duration);
            if (VideoExporter.Frame(path, Math.Min(0.5, probe.duration / 2), 800) is { } frame) Library.WriteThumbnail(frame, project);
            return project;
        }
        var image = ImageExporter.Load(path) ?? throw new InvalidDataException("Bu dosya bir görüntü olarak okunamadı.");
        var pointScale = ImageExporter.PointScale(path) ?? (image.PixelWidth > 3000 ? 2 : 1);
        var imported = Library.ImportMedia(path, CaptureKind.image, new Size(image.PixelWidth / pointScale, image.PixelHeight / pointScale), pointScale);
        Library.WriteThumbnail(image, imported);
        return imported;
    }

    // MARK: Tray

    void BuildTray()
    {
        Forms.Application.EnableVisualStyles();
        idleIcon = AppIcon.TrayIcon(false);
        recordingIcon = AppIcon.TrayIcon(true);
        menu = new Forms.ContextMenuStrip { ShowImageMargin = false };
        Forms.ToolStripMenuItem Item(string title, Action action)
        {
            var item = new Forms.ToolStripMenuItem(title);
            item.Click += (_, _) => Application.Current.Dispatcher.BeginInvoke(action);
            menu.Items.Add(item);
            return item;
        }
        screenshotItem = Item("Ekran görüntüsü al", TakeScreenshot);
        recordItem = Item("Alanı videoya kaydet", RecordVideo);
        menu.Items.Add(new Forms.ToolStripSeparator());
        lastCaptureItem = Item("Son yakalamayı düzenle", EditLastCapture);
        Item("Geçmiş…", ShowLibrary);
        Item("Görüntü veya video aç…", OpenFile);
        Item("FastShot projesi aç…", OpenProjectFolder);
        Item("Dosya çarkı…", ConversionCenter.Shared.ChooseFiles);
        menu.Items.Add(new Forms.ToolStripSeparator());
        Item("Ayarlar…", () => OpenSettings());
        menu.Items.Add(new Forms.ToolStripSeparator());
        Item("FastShot'tan çık", Quit);
        menu.Opening += (_, _) => lastCaptureItem.Enabled = Library.AllProjects().Count > 0;
        UpdateMenuShortcuts();

        tray = new Forms.NotifyIcon { Icon = idleIcon, Text = "FastShot", Visible = !Embedded, ContextMenuStrip = menu };
        tray.MouseUp += (_, e) =>
        {
            if (e.Button != Forms.MouseButtons.Left) return;
            if (recording is not null)
            {
                recording.Stop();
                return;
            }
            // Left click shows the same menu as right click, like the macOS menu bar item.
            typeof(Forms.NotifyIcon).GetMethod("ShowContextMenu", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.Invoke(tray, null);
        };
    }

    void UpdateMenuShortcuts()
    {
        screenshotItem.ShortcutKeyDisplayString = (prefs.ScreenshotHotkey ?? prefs.ScreenshotHotkeyAlt)?.DisplayString ?? "";
        recordItem.ShortcutKeyDisplayString = (prefs.RecordHotkey ?? prefs.RecordHotkeyAlt)?.DisplayString ?? "";
    }

    void ShowWelcome()
    {
        var shot = string.Join(" veya ", new[] { prefs.ScreenshotHotkey, prefs.ScreenshotHotkeyAlt }.Where(c => c is not null).Select(c => c!.DisplayString));
        var video = string.Join(" veya ", new[] { prefs.RecordHotkey, prefs.RecordHotkeyAlt }.Where(c => c is not null).Select(c => c!.DisplayString));
        var text = $"""
            • {shot}: alan seç, üzerine çiz, kopyala veya kaydet
            • {video}: seçtiğin alanı videoya kaydet, kayıt sırasında ekrana çiz

            Yakalamaların çizimleriyle birlikte Geçmiş'te saklanır; istediğin zaman yeniden açıp düzenleyebilirsin.

            FastShot görev çubuğunun bildirim alanında (saatin yanındaki simgeler) çalışır. Kısayolları değiştirmek için oradaki simgeye tıklayıp Ayarlar'ı açın.
            """;
        if (PrintScreenSetting.OpensSnippingTool)
            text += "\n\nPrint Screen tuşu şu an Windows'un Ekran Alıntısı Aracı'nı açıyor. FastShot'a vermek için: Ayarlar › Kısayollar.";
        MessageBox.Show(text, "FastShot hazır", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public void Quit()
    {
        ConversionCenter.Shared.Cancel();
        DragMonitor.Shared.Stop();
        recording?.Cancel();
        capture?.Cancel();
        foreach (var editor in editors.ToList()) editor.Close();
        HotKeyCenter.Shared.UnregisterAll();
        tray.Visible = false;
        tray.Dispose();
        Application.Current.Shutdown();
    }
}
