using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Threading;
using FastShot.Core.Translate;

namespace FastShot;

/// <summary>A floating card in FastShot's HUD style, next to the pointer: the translation large and selectable, the
/// recognised text below it (editable, re-translates as it changes), language pills to switch, copy buttons.
/// Drag it by its header; Esc or × closes it. One card, reused.</summary>
public sealed class TranslateCard : Window
{
    static TranslateCard? open;

    const double CardWidth = 460;
    static readonly UiPalette p = UiPalette.Hud;

    static readonly ControlTemplate plainTextBox = (ControlTemplate)XamlReader.Parse("""
        <ControlTemplate TargetType="TextBox" xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <ScrollViewer x:Name="PART_ContentHost" Focusable="False" HorizontalScrollBarVisibility="Disabled" VerticalScrollBarVisibility="Auto" Padding="0" Margin="0"/>
        </ControlTemplate>
        """);

    readonly TextBox translation = Text(15.5, p.Foreground, editable: false, maxHeight: 300);
    readonly TextBox original = Text(12.5, p.Secondary, editable: true, maxHeight: 120);
    readonly TextBlock fromLabel = PillText(), toLabel = PillText();
    readonly Border fromPill, toPill, originalPanel;
    readonly TextBlock engine = new() { FontSize = 10.5, Foreground = p.Secondary, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock status = new() { FontSize = 11.5, Foreground = p.Secondary, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly ProgressBar busy = new() { IsIndeterminate = true, Height = 2, Foreground = Theme.Accent, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Visibility = Visibility.Hidden };
    readonly TextBlock originalChevron = new() { FontFamily = Theme.IconFont, FontSize = 9, Foreground = p.Secondary, Text = "", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
    readonly DispatcherTimer typing = new() { Interval = TimeSpan.FromMilliseconds(700) };
    string? from;
    string to;
    /// <summary>The user picked the target in this card; a same-language result then is not switched for them.</summary>
    bool targetChosen;
    bool loading;
    CancellationTokenSource? running;

    /// <summary>Shows <paramref name="text"/> (empty: an empty card to type or paste into) and translates it.</summary>
    public static void Show(string text)
    {
        var fresh = open is null;
        open ??= new TranslateCard();
        open.SetSource(text);
        if (fresh) open.ShowNearPointer();
        else open.Activate();
    }

    static TextBox Text(double size, Brush brush, bool editable, double maxHeight) => new()
    {
        Template = plainTextBox, IsReadOnly = !editable, AcceptsReturn = editable, TextWrapping = TextWrapping.Wrap, FontSize = size,
        Foreground = brush, CaretBrush = Brushes.White, Background = Brushes.Transparent, BorderThickness = new Thickness(0),
        SelectionBrush = Theme.Accent, MaxHeight = maxHeight, FontFamily = Theme.UiFont,
    };

    static TextBlock PillText() => new() { FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = p.Foreground, VerticalAlignment = VerticalAlignment.Center };

    TranslateCard()
    {
        Title = "Çeviri";
        Icon = AppIcon.WindowIcon;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Height;
        Width = CardWidth + Ui.ChromeMargin * 2;
        Topmost = true;
        ShowInTaskbar = false;
        to = Preferences.Shared.TranslateTarget;

        // Header: icon, language pills, engine, close. Dragging it moves the card.
        fromPill = Pill(fromLabel, () => LanguageMenu(fromPill!, detect: true));
        toPill = Pill(toLabel, () => LanguageMenu(toPill!, detect: false));
        var arrow = new IconView("", 11) { Brush = p.Secondary, Margin = new Thickness(6, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        var swap = new IconButton(new IconView("", 13), "Dilleri değiştir", Swap, p, 26);
        var close = new IconButton(new IconView(Icons.Close, 12), "Kapat (Esc)", Close, p, 26);
        var header = new DockPanel { Background = Brushes.Transparent, Margin = new Thickness(14, 10, 8, 8) };
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var badge = new Border { Background = p.Hover, CornerRadius = new CornerRadius(8), Padding = new Thickness(7, 2, 7, 2), Child = engine, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(badge);
        right.Children.Add(close);
        DockPanel.SetDock(right, Dock.Right);
        header.Children.Add(right);
        var left = new StackPanel { Orientation = Orientation.Horizontal };
        left.Children.Add(new IconView(Icons.Translate, 15) { Brush = Theme.Accent, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
        left.Children.Add(fromPill);
        left.Children.Add(arrow);
        left.Children.Add(toPill);
        left.Children.Add(new Border { Width = 4 });
        left.Children.Add(swap);
        header.Children.Add(left);
        header.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        };

        // The translation, then the recognised text (collapsible).
        translation.Margin = new Thickness(16, 6, 16, 12);
        var originalHeader = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 10, 16, 4), Cursor = Cursors.Hand, Background = Brushes.Transparent };
        originalHeader.Children.Add(new TextBlock { Text = "ORİJİNAL METİN", FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = p.Secondary, VerticalAlignment = VerticalAlignment.Center });
        originalHeader.Children.Add(originalChevron);
        original.Margin = new Thickness(16, 0, 16, 10);
        originalPanel = new Border { Child = original };
        originalHeader.MouseLeftButtonUp += (_, _) => ToggleOriginal();

        // Footer: status, copy buttons.
        var copyOriginal = new PillButton("Orijinali kopyala", null, "Tanınan metni panoya kopyala", p.Hover, () => Copy(original.Text, "Metin kopyalandı"));
        var copy = new PillButton("Kopyala", new IconView(Icons.Copy, 12), "Çeviriyi panoya kopyala", Theme.Accent, () => Copy(translation.Text, "Çeviri kopyalandı")) { Margin = new Thickness(8, 0, 0, 0) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(copyOriginal);
        buttons.Children.Add(copy);
        var footer = new DockPanel { Margin = new Thickness(16, 2, 12, 12) };
        DockPanel.SetDock(buttons, Dock.Right);
        footer.Children.Add(buttons);
        footer.Children.Add(status);

        var stack = new StackPanel { Width = CardWidth };
        stack.Children.Add(header);
        stack.Children.Add(busy);
        stack.Children.Add(translation);
        stack.Children.Add(new Border { Height = 1, Background = p.Separator, Margin = new Thickness(16, 0, 16, 0) });
        stack.Children.Add(originalHeader);
        stack.Children.Add(originalPanel);
        stack.Children.Add(footer);
        var card = Ui.Hud(stack, 14, 0);
        Content = card;

        original.TextChanged += (_, _) =>
        {
            if (loading) return;
            typing.Stop();
            typing.Start();
        };
        typing.Tick += (_, _) =>
        {
            typing.Stop();
            Translate();
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            Close();
        };
        SizeChanged += (_, _) => KeepOnScreen();
        Closed += (_, _) =>
        {
            running?.Cancel();
            open = null;
        };
        SyncPills();
    }

    static Border Pill(TextBlock label, Action click)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(label);
        row.Children.Add(new TextBlock { FontFamily = Theme.IconFont, FontSize = 9, Text = "", Foreground = p.Secondary, Margin = new Thickness(6, 1, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        var pill = new Border { Child = row, Background = p.Hover, CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 4, 9, 4), Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center };
        pill.MouseEnter += (_, _) => pill.Opacity = 0.85;
        pill.MouseLeave += (_, _) => pill.Opacity = 1;
        pill.MouseLeftButtonDown += (_, e) => e.Handled = true;
        pill.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            click();
        };
        return pill;
    }

    void LanguageMenu(FrameworkElement anchor, bool detect)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom, MaxHeight = 420 };
        if (detect) menu.Items.Add(MenuItem("Dili algıla", from is null, () => { from = null; Translate(); }));
        if (detect) menu.Items.Add(new Separator());
        foreach (var language in Languages.All)
        {
            var code = language.Code;
            menu.Items.Add(detect
                ? MenuItem(language.Name, from == code, () => { from = code; Translate(); })
                : MenuItem(language.Name, to == code, () => { SetTarget(code, chosen: true); Translate(); }));
        }
        menu.IsOpen = true;
    }

    static MenuItem MenuItem(string title, bool selected, Action action)
    {
        var item = new MenuItem { Header = title, IsChecked = selected };
        item.Click += (_, _) => action();
        return item;
    }

    void SetTarget(string code, bool chosen)
    {
        to = code;
        if (chosen)
        {
            targetChosen = true;
            Preferences.Shared.TranslateTarget = code;
        }
        SyncPills();
    }

    void SyncPills(string? detected = null)
    {
        fromLabel.Text = from is not null ? Languages.Name(from) : detected is not null ? $"{Languages.Name(detected)} (algılandı)" : "Dili algıla";
        toLabel.Text = Languages.Name(to);
    }

    void SetSource(string text)
    {
        loading = true;
        original.Text = text;
        loading = false;
        from = null;
        targetChosen = false;
        to = Preferences.Shared.TranslateTarget;
        // Text already in the target language goes the other way: Turkish to English, anything else to Turkish.
        if (Languages.Guess(text) == to) to = to == "tr" ? "en" : "tr";
        SyncPills();
        ShowOriginal(true);
        Translate();
        if (string.IsNullOrWhiteSpace(text)) original.Focus();
    }

    void ToggleOriginal() => ShowOriginal(originalPanel.Visibility != Visibility.Visible);

    void ShowOriginal(bool show)
    {
        originalPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        originalChevron.Text = show ? "" : "";
    }

    void Swap()
    {
        var source = from ?? Languages.Guess(original.Text);
        if (source is null) return;
        loading = true;
        if (!string.IsNullOrWhiteSpace(translation.Text)) original.Text = translation.Text;
        loading = false;
        from = to;
        SetTarget(source, chosen: true);
        Translate();
    }

    async void Translate()
    {
        running?.Cancel();
        var text = original.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            translation.Text = "";
            status.Text = "Çevrilecek metni aşağıya yazın ya da yapıştırın.";
            engine.Text = Translator.UsesDeepL ? "DeepL" : "Çevrimdışı";
            busy.Visibility = Visibility.Hidden;
            return;
        }
        var cancel = running = new CancellationTokenSource();
        busy.Visibility = Visibility.Visible;
        engine.Text = Translator.UsesDeepL ? "DeepL" : "Çevrimdışı";
        status.Text = "Çevriliyor…";
        try
        {
            var result = await Translator.Translate(TextReflow.Join(text), from, to, message => { if (!cancel.IsCancellationRequested) status.Text = message; }, cancel.Token);
            if (cancel.IsCancellationRequested) return;
            // Already in the target language (the engine knows better than the first guess): switch, unless the user chose it.
            if (result.From == to && !targetChosen)
            {
                SetTarget(to == "tr" ? "en" : "tr", chosen: false);
                Translate();
                return;
            }
            translation.Text = result.Text;
            SyncPills(from is null && result.From.Length > 0 ? result.From : null);
            status.Text = result.From == to ? $"Metin zaten {Languages.Name(to)}." : "";
        }
        catch (OperationCanceledException) { /* a newer request took over */ }
        catch (Exception error)
        {
            if (cancel.IsCancellationRequested) return;
            translation.Text = "";
            status.Text = error.Message;
        }
        finally
        {
            if (running == cancel) busy.Visibility = Visibility.Hidden;
        }
    }

    void Copy(string text, string message)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        Output.CopyText(text);
        status.Text = message;
    }

    // MARK: Placement

    void ShowNearPointer()
    {
        Show();
        Activate();
        var cursor = Native.CursorPosition();
        var hwnd = new WindowInteropHelper(this).Handle;
        Native.SetWindowPos(hwnd, IntPtr.Zero, cursor.X + 16, cursor.Y + 16, 0, 0, 0x0001 /* SWP_NOSIZE */ | 0x0004 /* SWP_NOZORDER */);
        KeepOnScreen();
        if (string.IsNullOrWhiteSpace(original.Text)) original.Focus();
    }

    /// <summary>Keeps the whole card on the monitor's work area as it grows.</summary>
    void KeepOnScreen()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !Native.GetWindowRect(hwnd, out var rect)) return;
        var work = MonitorInfo.At(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2).Work;
        var x = Math.Max(work.Left, Math.Min(rect.Left, work.Right - rect.Width));
        var y = Math.Max(work.Top, Math.Min(rect.Top, work.Bottom - rect.Height));
        if (x != rect.Left || y != rect.Top) Native.SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, 0x0001 | 0x0004);
    }
}
