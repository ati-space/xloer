using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using FastShot.Core;

namespace FastShot;

public enum VideoCodec { h264, hevc }

/// <summary>Key that opens a wheel while files are being dragged.</summary>
public enum ConvertModifier { Shift, Control, Alt }

public enum ConvertDestination { Beside, Downloads, Custom }

public static class ConvertOptionTitles
{
    public static string Title(this ConvertModifier modifier) => modifier switch { ConvertModifier.Control => "Ctrl", ConvertModifier.Alt => "Alt", _ => "Shift" };

    public static string Title(this ConvertDestination destination) => destination switch
    {
        ConvertDestination.Downloads => "İndirilenler klasörüne",
        ConvertDestination.Custom => "Seçtiğim klasöre",
        _ => "Kaynak dosyanın yanına",
    };
}

public static class VideoCodecExtensions
{
    public static string Title(this VideoCodec codec) => codec == VideoCodec.h264 ? "H.264 (en uyumlu)" : "HEVC (daha küçük dosya)";
}

/// <summary>User settings, persisted as JSON under %APPDATA%\FastShot.</summary>
public sealed class Preferences
{
    public static Preferences Shared { get; } = Load();

    public static string Path => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FastShot", "settings.json");

    /// <summary>Raised on the UI thread after any setting changes.</summary>
    public event Action? Changed;

    static readonly JsonSerializerOptions jsonOptions = new()
    {
        WriteIndented = true,
        IncludeFields = true,
        Converters = { new JsonStringEnumConverter() },
    };

    sealed class Data
    {
        public KeyCombo? ScreenshotHotkey = KeyCombo.ControlShift2;
        public KeyCombo? ScreenshotHotkeyAlt = KeyCombo.PrintScreen;
        public KeyCombo? RecordHotkey = KeyCombo.ControlShift7;
        public KeyCombo? RecordHotkeyAlt = KeyCombo.ShiftPrintScreen;
        public bool ConvertEnabled = true;
        public ConvertModifier ConvertModifier = ConvertModifier.Shift;
        public ConvertDestination ConvertDestination = ConvertDestination.Beside;
        public string ConvertFolder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        public bool RevealAfterConvert;
        public bool ConvertContextMenu = true;
        public bool ConvertUseWord = true;
        public string ImageFolder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "FastShot");
        public string VideoFolder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "FastShot");
        public ImageFormat ImageFormat = ImageFormat.Png;
        public double JpegQuality = 0.9;
        public bool CopyWhenSaving;
        public bool ShowMagnifier = true;
        public bool PlaySounds = true;
        public int FramesPerSecond = 30;
        public bool ShowCursor = true;
        public VideoCodec VideoCodec = VideoCodec.h264;
        public int Countdown;
        public bool OpenEditorAfterRecording = true;
        public bool RecordSystemAudio;
        public bool RecordMicrophone;
        public bool PrintScreenTakenOver;
        public double AnnotationLength = 3;
        public string LastColor = RGBAColor.Red.Hex;
        public int LastSize = 1;
        public bool Onboarded;
    }

    readonly Data data;
    readonly object gate = new();

    Preferences(Data data) { this.data = data; }

    static Preferences Load()
    {
        try
        {
            if (File.Exists(Path))
            {
                var loaded = JsonSerializer.Deserialize<Data>(File.ReadAllText(Path), jsonOptions);
                if (loaded is not null) return new Preferences(loaded);
            }
        }
        catch { /* corrupt settings fall back to defaults */ }
        return new Preferences(new Data());
    }

    void Set<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        Save();
        Changed?.Invoke();
    }

    void Save()
    {
        lock (gate)
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                File.WriteAllText(Path, JsonSerializer.Serialize(data, jsonOptions));
            }
            catch { /* settings stay in memory for this session */ }
        }
    }

    public KeyCombo? ScreenshotHotkey { get => data.ScreenshotHotkey; set => Set(ref data.ScreenshotHotkey, value); }
    public KeyCombo? ScreenshotHotkeyAlt { get => data.ScreenshotHotkeyAlt; set => Set(ref data.ScreenshotHotkeyAlt, value); }
    public KeyCombo? RecordHotkey { get => data.RecordHotkey; set => Set(ref data.RecordHotkey, value); }
    public KeyCombo? RecordHotkeyAlt { get => data.RecordHotkeyAlt; set => Set(ref data.RecordHotkeyAlt, value); }
    public string ImageFolder { get => data.ImageFolder; set => Set(ref data.ImageFolder, value); }
    public string VideoFolder { get => data.VideoFolder; set => Set(ref data.VideoFolder, value); }
    public ImageFormat ImageFormat { get => data.ImageFormat; set => Set(ref data.ImageFormat, value); }
    public double JpegQuality { get => data.JpegQuality; set => Set(ref data.JpegQuality, value); }
    public bool CopyWhenSaving { get => data.CopyWhenSaving; set => Set(ref data.CopyWhenSaving, value); }
    public bool ShowMagnifier { get => data.ShowMagnifier; set => Set(ref data.ShowMagnifier, value); }
    public bool PlaySounds { get => data.PlaySounds; set => Set(ref data.PlaySounds, value); }
    public int FramesPerSecond { get => data.FramesPerSecond; set => Set(ref data.FramesPerSecond, value); }
    public bool ShowCursor { get => data.ShowCursor; set => Set(ref data.ShowCursor, value); }
    public VideoCodec VideoCodec { get => data.VideoCodec; set => Set(ref data.VideoCodec, value); }
    public int Countdown { get => data.Countdown; set => Set(ref data.Countdown, value); }
    public bool OpenEditorAfterRecording { get => data.OpenEditorAfterRecording; set => Set(ref data.OpenEditorAfterRecording, value); }
    public bool RecordSystemAudio { get => data.RecordSystemAudio; set => Set(ref data.RecordSystemAudio, value); }
    public bool RecordMicrophone { get => data.RecordMicrophone; set => Set(ref data.RecordMicrophone, value); }
    /// <summary>Set after FastShot turned off "Print Screen opens Snipping Tool", so it can be given back.</summary>
    public bool PrintScreenTakenOver { get => data.PrintScreenTakenOver; set => Set(ref data.PrintScreenTakenOver, value); }
    public bool ConvertEnabled { get => data.ConvertEnabled; set => Set(ref data.ConvertEnabled, value); }
    /// <summary>Opens the file wheel while files are dragged; tapping it again goes to the next wheel.</summary>
    public ConvertModifier ConvertModifier { get => data.ConvertModifier; set => Set(ref data.ConvertModifier, value); }
    public ConvertDestination ConvertDestination { get => data.ConvertDestination; set => Set(ref data.ConvertDestination, value); }
    public string ConvertFolder { get => data.ConvertFolder; set => Set(ref data.ConvertFolder, value); }
    public bool RevealAfterConvert { get => data.RevealAfterConvert; set => Set(ref data.RevealAfterConvert, value); }
    /// <summary>"FastShot ile dönüştür" in Explorer's right-click menu.</summary>
    public bool ConvertContextMenu { get => data.ConvertContextMenu; set => Set(ref data.ConvertContextMenu, value); }
    /// <summary>Use Microsoft Word, when installed, for DOC and the most faithful document PDFs.</summary>
    public bool ConvertUseWord { get => data.ConvertUseWord; set => Set(ref data.ConvertUseWord, value); }
    /// <summary>Seconds a mark drawn in the video editor stays visible; 0 means until the end.</summary>
    public double AnnotationLength { get => data.AnnotationLength; set => Set(ref data.AnnotationLength, value); }

    public RGBAColor LastColor
    {
        get => RGBAColor.FromHex(data.LastColor) ?? RGBAColor.Red;
        set { if (data.LastColor != value.Hex) { data.LastColor = value.Hex; Save(); } }
    }

    public int LastSize { get => data.LastSize; set { if (data.LastSize != value) { data.LastSize = value; Save(); } } }
    public bool Onboarded { get => data.Onboarded; set { data.Onboarded = value; Save(); } }

    public void ResetShortcuts()
    {
        ScreenshotHotkey = KeyCombo.ControlShift2;
        ScreenshotHotkeyAlt = KeyCombo.PrintScreen;
        RecordHotkey = KeyCombo.ControlShift7;
        RecordHotkeyAlt = KeyCombo.ShiftPrintScreen;
    }

    public static bool LaunchAtLogin
    {
        get
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            return key?.GetValue("FastShot") is string;
        }
        set
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            if (value) key.SetValue("FastShot", $"\"{Environment.ProcessPath}\"");
            else key.DeleteValue("FastShot", throwOnMissingValue: false);
        }
    }
}
