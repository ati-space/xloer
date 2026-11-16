using System.Text.Json;
using System.Text.Json.Serialization;

namespace FastShot;

public enum VideoCodec { h264, hevc }

/// <summary>Key that opens a wheel while files are being dragged.</summary>
public enum ConvertModifier { AltGr, Shift, Control, Alt }

public enum ConvertDestination { Beside, Downloads, Custom }

public static class ConvertOptionTitles
{
    public static string Title(this ConvertModifier modifier) => modifier switch { ConvertModifier.AltGr => "AltGr", ConvertModifier.Control => "Ctrl", ConvertModifier.Alt => "Alt", _ => "Shift" };

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
    // Before Shared: static fields start in this order, and Load() needs the options (without them no field is read).
    static readonly JsonSerializerOptions jsonOptions = new()
    {
        WriteIndented = true,
        IncludeFields = true,
        Converters = { new JsonStringEnumConverter() },
    };
    public static Preferences Shared { get; } = Load(Path);

    public static string Path => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FastShot", "settings.json");

    /// <summary>Raised on the UI thread after any setting changes.</summary>
    public event Action? Changed;


    sealed class Data
    {
        public KeyCombo? ScreenshotHotkey = KeyCombo.ControlShift2;
        public KeyCombo? ScreenshotHotkeyAlt = KeyCombo.PrintScreen;
        public KeyCombo? RecordHotkey = KeyCombo.ControlShift7;
        public KeyCombo? RecordHotkeyAlt = KeyCombo.ShiftPrintScreen;
        public bool ConvertEnabled = true;
        // Was "ConvertModifier" (Shift) until 1.2.0; the new name starts everyone on AltGr.
        public ConvertModifier WheelKey = ConvertModifier.AltGr;
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
        public bool HighlightClicks;
        public VideoCodec VideoCodec = VideoCodec.h264;
        public int Countdown;
        public bool OpenEditorAfterRecording = true;
        public bool RecordSystemAudio;
        public bool RecordMicrophone;
        public bool PrintScreenTakenOver;
        public bool ShowTrayIcon = true;
        public TranslateEngine TranslateEngine = TranslateEngine.Offline;
        public string TranslateTarget = "tr";
        public double AnnotationLength = 3;
        public string LastColor = RGBAColor.Red.Hex;
        public int LastSize = 1;
        public bool Onboarded;
    }

    readonly Data data;
    readonly string file;
    readonly object gate = new();
    /// <summary>The file was there but could not be read: it is copied aside before anything replaces it.</summary>
    bool unread;

    Preferences(Data data, string file, bool unread)
    {
        this.data = data;
        this.file = file;
        this.unread = unread;
    }

    /// <summary>Where a settings file that could not be read is kept, instead of being replaced by the defaults.</summary>
    string UnreadablePath => System.IO.Path.ChangeExtension(file, ".unreadable.json");

    /// <summary>Reads the settings at <paramref name="file"/>. At sign-in another program (antivirus, backup) can hold the file
    /// for a moment, so a locked file is tried again for <paramref name="retryFor"/> (2 s by default).</summary>
    internal static Preferences Load(string file, TimeSpan? retryFor = null)
    {
        if (!File.Exists(file)) return new Preferences(new Data(), file, unread: false);
        var deadline = DateTime.UtcNow + (retryFor ?? TimeSpan.FromSeconds(2));
        while (true)
        {
            try
            {
                var data = Parse(File.ReadAllText(file));
                if (data is not null) return new Preferences(data, file, unread: false);
                Program.Log(new InvalidDataException($"Ayarlar okunamadı, varsayılanlarla açıldı: {file}"));
                break;
            }
            catch (FileNotFoundException) { return new Preferences(new Data(), file, unread: false); }
            catch (DirectoryNotFoundException) { return new Preferences(new Data(), file, unread: false); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                if (DateTime.UtcNow >= deadline)
                {
                    Program.Log(new IOException($"Ayarlar dosyası açılamadı, varsayılanlarla açıldı: {file}", error));
                    break;
                }
                Thread.Sleep(100);
            }
        }
        var preferences = new Preferences(new Data(), file, unread: true);
        preferences.KeepUnreadFile();
        return preferences;
    }

    /// <summary>Reads each setting on its own, so a value this version does not understand (one written by another version,
    /// say) only resets that setting. Null when the text is not a JSON object at all (a file cut short, for example).</summary>
    static Data? Parse(string json)
    {
        JsonDocument document;
        try { document = JsonDocument.Parse(json); }
        catch (JsonException) { return null; }
        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
            var data = new Data();
            foreach (var field in typeof(Data).GetFields())
            {
                if (!document.RootElement.TryGetProperty(field.Name, out var value)) continue;
                try { field.SetValue(data, value.Deserialize(field.FieldType, jsonOptions)); }
                catch (Exception error) when (error is JsonException or NotSupportedException or InvalidOperationException or ArgumentException)
                {
                    Program.Log(new InvalidDataException($"Ayar okunamadı, varsayılan kullanılıyor: {field.Name}", error));
                }
            }
            return data;
        }
    }

    /// <summary>Copies a settings file that could not be read to settings.unreadable.json. False while it still cannot be copied.</summary>
    bool KeepUnreadFile()
    {
        if (!unread) return true;
        try
        {
            if (File.Exists(file)) File.Copy(file, UnreadablePath, overwrite: true);
            unread = false;
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    void Set<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        Save();
        Changed?.Invoke();
    }

    /// <summary>Writes a new file and then moves it over the old one, so the settings are never left half written
    /// (when Windows shuts down or FastShot is ended in the middle of a save).</summary>
    void Save()
    {
        lock (gate)
        {
            // Never replace settings that were not read without keeping a copy; until then they stay in memory.
            if (!KeepUnreadFile())
            {
                Program.Log(new IOException($"Ayarlar kaydedilmedi; okunamayan dosyanın üstüne yazılmıyor: {file}"));
                return;
            }
            var temp = file + "." + Environment.ProcessId + ".tmp";
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);
                using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    JsonSerializer.Serialize(stream, data, jsonOptions);
                    stream.Flush(flushToDisk: true);
                }
                for (var attempt = 1; ; attempt++)
                {
                    try
                    {
                        File.Move(temp, file, overwrite: true);
                        break;
                    }
                    catch (Exception error) when (attempt < 5 && error is IOException or UnauthorizedAccessException)
                    {
                        Thread.Sleep(50);
                    }
                }
            }
            catch (Exception error)
            {
                // The settings stay in memory for this session.
                Program.Log(new IOException($"Ayarlar kaydedilemedi: {file}", error));
                try { File.Delete(temp); } catch { /* nothing left to clean up */ }
            }
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
    public bool HighlightClicks { get => data.HighlightClicks; set => Set(ref data.HighlightClicks, value); }
    public VideoCodec VideoCodec { get => data.VideoCodec; set => Set(ref data.VideoCodec, value); }
    public int Countdown { get => data.Countdown; set => Set(ref data.Countdown, value); }
    public bool OpenEditorAfterRecording { get => data.OpenEditorAfterRecording; set => Set(ref data.OpenEditorAfterRecording, value); }
    public bool RecordSystemAudio { get => data.RecordSystemAudio; set => Set(ref data.RecordSystemAudio, value); }
    public bool RecordMicrophone { get => data.RecordMicrophone; set => Set(ref data.RecordMicrophone, value); }
    /// <summary>Set after FastShot turned off "Print Screen opens Snipping Tool", so it can be given back.</summary>
    public bool PrintScreenTakenOver { get => data.PrintScreenTakenOver; set => Set(ref data.PrintScreenTakenOver, value); }
    /// <summary>The notification-area icon. Hidden, FastShot still runs; starting it again opens Settings.</summary>
    public bool ShowTrayIcon { get => data.ShowTrayIcon; set => Set(ref data.ShowTrayIcon, value); }
    /// <summary>DeepL needs the user's own key (see DeepLKey); without one the offline engine is used.</summary>
    public TranslateEngine TranslateEngine { get => data.TranslateEngine; set => Set(ref data.TranslateEngine, value); }
    /// <summary>The language the translation window translates to; it remembers the last choice.</summary>
    public string TranslateTarget { get => data.TranslateTarget; set => Set(ref data.TranslateTarget, value); }
    public bool ConvertEnabled { get => data.ConvertEnabled; set => Set(ref data.ConvertEnabled, value); }
    /// <summary>Opens the file wheel while files are dragged; tapping it again goes to the next wheel.</summary>
    public ConvertModifier WheelKey { get => data.WheelKey; set => Set(ref data.WheelKey, value); }
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
