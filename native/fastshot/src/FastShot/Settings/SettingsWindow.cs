using System.Media;
using Microsoft.Win32;

namespace FastShot;

public enum SettingsTab { General, Shortcuts, Saving, Video, Convert }

public sealed class SettingsWindow : Window
{
    readonly Preferences prefs = Preferences.Shared;
    readonly TabControl tabs = new() { Margin = new Thickness(14) };
    readonly UiPalette palette = UiPalette.Editor;
    readonly List<Action> refreshers = [];

    public SettingsWindow()
    {
        Title = "FastShot Ayarları";
        Icon = AppIcon.WindowIcon;
        Width = 640;
        Height = 660;
        ResizeMode = ResizeMode.CanMinimize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        tabs.Items.Add(Tab("Genel", General()));
        tabs.Items.Add(Tab("Kısayollar", Shortcuts()));
        tabs.Items.Add(Tab("Kaydetme", Saving()));
        tabs.Items.Add(Tab("Video", Video()));
        tabs.Items.Add(Tab("Dosya çarkı", Convert()));
        Content = tabs;
        Activated += (_, _) => Refresh();
        HotKeyCenter.Shared.FailedChanged += Refresh;
        Closed += (_, _) => HotKeyCenter.Shared.FailedChanged -= Refresh;
    }

    public void Select(SettingsTab tab) => tabs.SelectedIndex = (int)tab;

    void Refresh()
    {
        foreach (var refresh in refreshers) refresh();
    }

    static TabItem Tab(string title, UIElement content) => new()
    {
        Header = title,
        Content = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(6, 12, 6, 6) },
    };

    // MARK: Building blocks

    static StackPanel Page() => new() { Margin = new Thickness(8, 0, 8, 8) };

    TextBlock Section(string title) => new() { Text = title, FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 6) };

    TextBlock Note(string text) => new() { Text = text, FontSize = 11.5, Foreground = palette.Secondary, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 8) };

    Grid Row(string title, UIElement control, string? help = null)
    {
        var grid = new Grid { Margin = new Thickness(0, 5, 0, 5) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var label = new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 12, 0) };
        if (help is not null) label.ToolTip = help;
        grid.Children.Add(label);
        Grid.SetColumn(control, 1);
        if (control is FrameworkElement element) element.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(control);
        return grid;
    }

    CheckBox Toggle(string title, Func<bool> get, Action<bool> set)
    {
        var box = new CheckBox { Content = title, IsChecked = get(), Margin = new Thickness(0, 5, 0, 5) };
        box.Checked += (_, _) => set(true);
        box.Unchecked += (_, _) => set(false);
        refreshers.Add(() => box.IsChecked = get());
        return box;
    }

    ComboBox Picker<T>(IEnumerable<(string title, T value)> options, Func<T> get, Action<T> set)
    {
        var box = new ComboBox { MinWidth = 220 };
        var values = new List<T>();
        foreach (var (title, value) in options)
        {
            box.Items.Add(title);
            values.Add(value);
        }
        void Sync() => box.SelectedIndex = Math.Max(0, values.FindIndex(v => EqualityComparer<T>.Default.Equals(v, get())));
        Sync();
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedIndex >= 0) set(values[box.SelectedIndex]);
        };
        refreshers.Add(Sync);
        return box;
    }

    FrameworkElement FolderRow(Func<string> get, Action<string> set)
    {
        var path = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Foreground = palette.Secondary, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 260, Margin = new Thickness(0, 0, 8, 0) };
        void Sync()
        {
            path.Text = get();
            path.ToolTip = get();
        }
        Sync();
        refreshers.Add(Sync);
        var choose = new Button { Content = "Seç…", Padding = new Thickness(12, 3, 12, 4) };
        choose.Click += (_, _) =>
        {
            var dialog = new OpenFolderDialog { InitialDirectory = Directory.Exists(get()) ? get() : null, Multiselect = false };
            if (dialog.ShowDialog(this) == true)
            {
                set(dialog.FolderName);
                Sync();
            }
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(path);
        row.Children.Add(choose);
        return row;
    }

    // MARK: Pages

    UIElement General()
    {
        var page = Page();
        page.Children.Add(Toggle("Windows açıldığında başlat", () => Preferences.LaunchAtLogin, on =>
        {
            try { Preferences.LaunchAtLogin = on; }
            catch (Exception error) { Output.ShowError(error, "Başlangıç ayarı değiştirilemedi"); }
        }));
        page.Children.Add(Toggle("Seçim yaparken büyüteci göster", () => prefs.ShowMagnifier, on => prefs.ShowMagnifier = on));
        page.Children.Add(Toggle("Ses efektlerini çal", () => prefs.PlaySounds, on => prefs.PlaySounds = on));
        var history = new Button { Content = "Geçmiş klasörünü göster", Padding = new Thickness(12, 4, 12, 5) };
        history.Click += (_, _) => Output.OpenFolder(AppController.Shared.Library.Root);
        page.Children.Add(Row("Geçmiş", history));
        page.Children.Add(Note("Kopyaladığınız, kaydettiğiniz veya kaydını aldığınız her yakalama, orijinal medya ve çizimleriyle birlikte bu klasörde saklanır. FastShot'ı Başlat menüsünden yeniden açtığınızda bu ayarlar açılır."));
        return page;
    }

    UIElement Shortcuts()
    {
        var page = Page();
        page.Children.Add(Section("FastShot kısayolları"));
        page.Children.Add(ShortcutRow("Ekran görüntüsü", () => prefs.ScreenshotHotkey, v => prefs.ScreenshotHotkey = v));
        page.Children.Add(ShortcutRow("Ekran görüntüsü (2. kısayol)", () => prefs.ScreenshotHotkeyAlt, v => prefs.ScreenshotHotkeyAlt = v));
        page.Children.Add(ShortcutRow("Video kaydı başlat / bitir", () => prefs.RecordHotkey, v => prefs.RecordHotkey = v));
        page.Children.Add(ShortcutRow("Video kaydı (2. kısayol)", () => prefs.RecordHotkeyAlt, v => prefs.RecordHotkeyAlt = v));
        page.Children.Add(Note("Değiştirmek için kısayola tıklayıp yeni tuş kombinasyonuna basın (Esc: vazgeç). Print Screen, F1–F24, Insert ve Pause tek başına kullanılabilir; diğer tuşlar Ctrl, Alt veya Win ile birlikte."));
        var reset = new Button { Content = "Varsayılanlara dön", Padding = new Thickness(12, 4, 12, 5), HorizontalAlignment = HorizontalAlignment.Left };
        reset.Click += (_, _) => { prefs.ResetShortcuts(); Refresh(); };
        page.Children.Add(reset);

        page.Children.Add(Section("Windows'un Print Screen tuşu"));
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        var button = new Button { Padding = new Thickness(12, 4, 12, 5), HorizontalAlignment = HorizontalAlignment.Left };
        void Sync()
        {
            var snipping = PrintScreenSetting.OpensSnippingTool;
            status.Text = snipping
                ? "Print Screen tuşu şu an Windows'un Ekran Alıntısı Aracı'nı açıyor; bu yüzden FastShot'a ulaşmıyor."
                : prefs.PrintScreenTakenOver ? "Print Screen tuşu FastShot'ı açıyor." : "Print Screen tuşu Windows tarafından kullanılmıyor; FastShot kısayolu olarak çalışır.";
            button.Content = snipping ? "FastShot'a ver" : "Windows'a geri ver";
            button.Visibility = snipping || prefs.PrintScreenTakenOver ? Visibility.Visible : Visibility.Collapsed;
        }
        button.Click += (_, _) =>
        {
            if (PrintScreenSetting.OpensSnippingTool)
            {
                PrintScreenSetting.OpensSnippingTool = false;
                prefs.PrintScreenTakenOver = true;
            }
            else
            {
                PrintScreenSetting.OpensSnippingTool = true;
                prefs.PrintScreenTakenOver = false;
            }
            AppController.Shared.RegisterHotkeys();
            Sync();
        };
        refreshers.Add(Sync);
        Sync();
        page.Children.Add(status);
        page.Children.Add(button);
        page.Children.Add(Note("\"FastShot'a ver\", Ayarlar › Erişilebilirlik › Klavye › \"Ekran alıntısı aracını açmak için Print Screen tuşunu kullan\" seçeneğini kapatır. \"Windows'a geri ver\" eski hâline döndürür. Bazı sistemlerde değişiklik oturumu kapatıp açınca etkinleşir."));
        return page;
    }

    FrameworkElement ShortcutRow(string title, Func<KeyCombo?> get, Action<KeyCombo?> set)
    {
        var recorder = new HotkeyRecorder(get, set);
        var warning = new TextBlock { FontSize = 10.5, Foreground = Theme.Frozen(Color.FromRgb(0xE0, 0x80, 0x10)), HorizontalAlignment = HorizontalAlignment.Right, TextWrapping = TextWrapping.Wrap, MaxWidth = 300 };
        void Sync()
        {
            recorder.Refresh();
            var value = get();
            warning.Text = value is not null && HotKeyCenter.Shared.Failed.Contains(value)
                ? value == KeyCombo.PrintScreen && PrintScreenSetting.OpensSnippingTool
                    ? "Windows bu tuşu Ekran Alıntısı Aracı için kullanıyor (aşağıya bakın)."
                    : "Başka bir uygulama bu kısayolu kullanıyor."
                : "";
            warning.Visibility = warning.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        refreshers.Add(Sync);
        Sync();
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
        stack.Children.Add(recorder);
        stack.Children.Add(warning);
        return Row(title, stack);
    }

    UIElement Saving()
    {
        var page = Page();
        page.Children.Add(Row("Görüntü klasörü", FolderRow(() => prefs.ImageFolder, v => prefs.ImageFolder = v)));
        page.Children.Add(Row("Biçim", Picker([("PNG (kayıpsız)", ImageFormat.Png), ("JPEG (küçük dosya)", ImageFormat.Jpeg)], () => prefs.ImageFormat, v => prefs.ImageFormat = v)));
        var quality = new Slider { Minimum = 0.5, Maximum = 1, Value = prefs.JpegQuality, Width = 220, TickFrequency = 0.05, IsSnapToTickEnabled = true };
        var qualityText = new TextBlock { Width = 44, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        void SyncQuality() => qualityText.Text = $"%{Math.Round(quality.Value * 100)}";
        quality.ValueChanged += (_, _) => { prefs.JpegQuality = quality.Value; SyncQuality(); };
        SyncQuality();
        var qualityRow = new StackPanel { Orientation = Orientation.Horizontal };
        qualityRow.Children.Add(quality);
        qualityRow.Children.Add(qualityText);
        page.Children.Add(Row("JPEG kalitesi", qualityRow));
        page.Children.Add(Toggle("Kaydederken panoya da kopyala", () => prefs.CopyWhenSaving, on => prefs.CopyWhenSaving = on));
        page.Children.Add(Note("Kopyaladığınız veya kaydettiğiniz her yakalama, çizimleri düzenlenebilir hâlde Geçmiş'te saklanır."));
        return page;
    }

    UIElement Convert()
    {
        var page = Page();
        page.Children.Add(Section("Dosya çarkını açmak"));
        page.Children.Add(Toggle("Dosya sürüklerken çarkı aç", () => prefs.ConvertEnabled, on => prefs.ConvertEnabled = on));
        page.Children.Add(Row("Çarkı açan tuş", Picker(Enum.GetValues<ConvertModifier>().Select(m => (m.Title(), m)), () => prefs.ConvertModifier, v => prefs.ConvertModifier = v)));
        page.Children.Add(Toggle("Sağ tık menüsüne \"FastShot çarkı\" ekle", () => prefs.ConvertContextMenu, on => prefs.ConvertContextMenu = on));
        var keyNote = Note("");
        void SyncKeyNote()
        {
            var key = prefs.ConvertModifier.Title();
            keyNote.Text = $"Dosyaları sürüklerken {key} tuşuna bir kez basın; imlecin etrafında Dönüştür çarkı açılır. {key}'e tekrar bastıkça Araçlar ve İşlemler çarklarına geçersiniz; üstteki çubuğun üzerinde bir an beklemek de o çarkı açar. Seçeneğin üzerine bırakın. Sürüklemeden kullanmak için dosyalara sağ tıklayıp \"FastShot çarkı\"nı seçin (Windows 11'de önce \"Daha fazla seçenek göster\") ya da bildirim alanındaki menüden \"Dosya çarkı…\".";
        }
        SyncKeyNote();
        refreshers.Add(SyncKeyNote);
        page.Children.Add(keyNote);

        page.Children.Add(Section("Kaydetme"));
        var custom = FolderRow(() => prefs.ConvertFolder, v => prefs.ConvertFolder = v);
        var customRow = Row("Klasör", custom);
        void SyncCustom() => customRow.Visibility = prefs.ConvertDestination == ConvertDestination.Custom ? Visibility.Visible : Visibility.Collapsed;
        page.Children.Add(Row("Oluşan dosyalar", Picker(Enum.GetValues<ConvertDestination>().Select(d => (d.Title(), d)), () => prefs.ConvertDestination, v =>
        {
            prefs.ConvertDestination = v;
            SyncCustom();
        })));
        SyncCustom();
        refreshers.Add(SyncCustom);
        page.Children.Add(customRow);
        page.Children.Add(Toggle("Bitince Gezgin'de göster", () => prefs.RevealAfterConvert, on => prefs.RevealAfterConvert = on));
        if (FastShot.Core.Convert.Capabilities.WordInstalled)
            page.Children.Add(Toggle("DOC dosyaları ve en doğru PDF'ler için Microsoft Word'ü kullan", () => prefs.ConvertUseWord, on => prefs.ConvertUseWord = on));
        page.Children.Add(Note("Orijinal dosyalar hiçbir zaman değiştirilmez; sonuçlar yeni dosya olarak kaydedilir."));

        TextBlock List(string text) => new() { Text = text, Foreground = palette.Secondary, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, MaxWidth = 380, TextAlignment = TextAlignment.Right };
        page.Children.Add(Section("Dönüştür"));

        FastShot.Core.Convert.WordEngine.Enabled = prefs.ConvertUseWord;
        foreach (var family in Enum.GetValues<FastShot.Core.Convert.FileFamily>())
        {
            var targets = string.Join(" · ", FastShot.Core.Convert.FileConverter.Targets(family).Select(f => f.Detail is "Sayfalar" ? $"{f.Title} (sayfalar)" : f.Title));
            page.Children.Add(Row(FastShot.Core.Convert.FileFamilyInfo.Title(family), List(targets)));
        }
        var missing = new List<string>();
        if (FastShot.Core.Convert.ExternalTools.Ffmpeg is null)
            missing.Add("WebP ve AVIF çıktısı Windows'ta yerleşik değil. ffmpeg kurarsanız (komut satırında: winget install ffmpeg) FastShot bunları çarka otomatik ekler.");
        if (!FastShot.Core.Convert.Capabilities.WordInstalled)
            missing.Add("DOC (Word 97) dönüşümü Microsoft Word ister. Word yokken belgelerin PDF'i Microsoft Edge ile yazdırılır.");
        page.Children.Add(Note(string.Join(" ", missing.Append("PDF'ten metin çıkarmak Word ile yapılır; Word yoksa sayfalar Windows'un metin tanıma özelliğiyle okunur."))));

        page.Children.Add(Section("Araçlar"));

        page.Children.Add(Row("Görüntü", List("Düzenle (çiz, yaz, bulanıklaştır) · Kırp · Arka plan · Sıkıştır · Meta veri sil · Metni kopyala")));
        page.Children.Add(Row("Video", List("Düzenle (çiz, kırp-süre) · Sıkıştır · Sesi kaldır · Meta veri sil")));
        page.Children.Add(Row("Ses · PDF", List("Sıkıştır · Meta veri sil · PDF birleştir (birden çok PDF)")));
        page.Children.Add(Section("İşlemler"));

        page.Children.Add(Row("Her dosya ve klasör", List("ZIP'le / ZIP'i aç · Paylaş (Yakındaki paylaşım) · Panoya kopyala · Yolu kopyala · Ekrana sabitle (görüntü) · Terminal'de aç")));
        return page;
    }

    UIElement Video()
    {
        var page = Page();
        page.Children.Add(Row("Video klasörü", FolderRow(() => prefs.VideoFolder, v => prefs.VideoFolder = v)));
        page.Children.Add(Row("Kare hızı", Picker([("30 fps", 30), ("60 fps", 60)], () => prefs.FramesPerSecond, v => prefs.FramesPerSecond = v)));
        page.Children.Add(Row("Kodek", Picker([(VideoCodec.h264.Title(), VideoCodec.h264), (VideoCodec.hevc.Title(), VideoCodec.hevc)], () => prefs.VideoCodec, v => prefs.VideoCodec = v)));
        page.Children.Add(Row("Geri sayım", Picker([("Yok", 0), ("3 saniye", 3), ("5 saniye", 5)], () => prefs.Countdown, v => prefs.Countdown = v)));
        page.Children.Add(Toggle("İmleci göster", () => prefs.ShowCursor, on => prefs.ShowCursor = on));
        page.Children.Add(Toggle("Sistem sesini kaydet", () => prefs.RecordSystemAudio, on => prefs.RecordSystemAudio = on));
        page.Children.Add(Toggle("Mikrofonu kaydet", () => prefs.RecordMicrophone, on => prefs.RecordMicrophone = on));
        page.Children.Add(Toggle("Kayıttan sonra düzenleyiciyi aç", () => prefs.OpenEditorAfterRecording, on => prefs.OpenEditorAfterRecording = on));
        page.Children.Add(Note("Düzenleyici kapalıyken kayıt doğrudan video klasörüne, kayıt sırasında çizdikleriniz de eklenerek kaydedilir. HEVC kodlayıcısı yoksa H.264 kullanılır."));
        return page;
    }
}

/// <summary>Click, then press the new combination. Global shortcuts pause meanwhile so they can be re-recorded.</summary>
public sealed class HotkeyRecorder : StackPanel
{
    readonly Func<KeyCombo?> get;
    readonly Action<KeyCombo?> set;
    readonly Button button = new() { MinWidth = 160, Padding = new Thickness(10, 4, 10, 5) };
    readonly Button clear = new() { Content = Icons.Close, FontFamily = Theme.IconFont, FontSize = 10, Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(6, 0, 0, 0), ToolTip = "Kısayolu kaldır" };
    bool recording;

    public HotkeyRecorder(Func<KeyCombo?> get, Action<KeyCombo?> set)
    {
        this.get = get;
        this.set = set;
        Orientation = Orientation.Horizontal;
        HorizontalAlignment = HorizontalAlignment.Right;
        Children.Add(button);
        Children.Add(clear);
        button.Click += (_, _) =>
        {
            if (recording) Stop(); else Start();
        };
        clear.Click += (_, _) =>
        {
            set(null);
            Refresh();
        };
        button.PreviewKeyDown += (_, e) =>
        {
            if (!recording) return;
            e.Handled = true;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None)
            {
                Stop();
                return;
            }
            if (key == Key.Snapshot) return; // arrives on key up
            Record(key);
        };
        button.PreviewKeyUp += (_, e) =>
        {
            if (!recording) return;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key != Key.Snapshot) return;
            e.Handled = true;
            Record(key);
        };
        button.LostKeyboardFocus += (_, _) => Stop();
        Refresh();
    }

    void Record(Key key)
    {
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin) return;
        if (KeyCombo.FromKey(key, Keyboard.Modifiers) is { } combo)
        {
            set(combo);
            Stop();
        }
        else
        {
            SystemSounds.Beep.Play();
        }
    }

    void Start()
    {
        recording = true;
        HotKeyCenter.Shared.Suspend();
        Refresh();
        button.Focus();
    }

    void Stop()
    {
        if (!recording) return;
        recording = false;
        HotKeyCenter.Shared.Resume();
        Refresh();
    }

    public void Refresh()
    {
        button.Content = recording ? "Tuşlara basın…" : get()?.DisplayString ?? "Yok";
        clear.Visibility = get() is not null && !recording ? Visibility.Visible : Visibility.Hidden;
    }
}

/// <summary>Windows 11's "Use the Print screen key to open screen capture" (Snipping Tool) setting.</summary>
public static class PrintScreenSetting
{
    const string KeyPath = @"Control Panel\Keyboard";
    const string ValueName = "PrintScreenKeyForSnippingEnabled";

    public static bool OpensSnippingTool
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
                return key?.GetValue(ValueName) is int value && value != 0;
            }
            catch { return false; }
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
            key.SetValue(ValueName, value ? 1 : 0, RegistryValueKind.DWord);
        }
    }
}
