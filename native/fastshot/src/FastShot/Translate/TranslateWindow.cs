using System.Windows.Threading;
using FastShot.Core.Translate;

namespace FastShot;

/// <summary>Recognised (or pasted) text next to its translation. Translates on open, when a language changes and a moment
/// after typing; both sides can be copied. One window, reused.</summary>
public sealed class TranslateWindow : Window
{
    static TranslateWindow? open;

    readonly TextBox source = Area(editable: true);
    readonly TextBox result = Area(editable: false);
    readonly ComboBox from = new() { MinWidth = 150 };
    readonly ComboBox to = new() { MinWidth = 150 };
    readonly TextBlock status = new() { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 12, 0) };
    readonly DispatcherTimer typing = new() { Interval = TimeSpan.FromMilliseconds(700) };
    CancellationTokenSource? running;
    bool loading;

    /// <summary>Shows <paramref name="text"/> and translates it to the language chosen last (Türkçe at first).</summary>
    public static void Show(string text)
    {
        open ??= new TranslateWindow();
        open.SetSource(text);
        open.Show();
        if (open.WindowState == WindowState.Minimized) open.WindowState = WindowState.Normal;
        open.Activate();
    }

    static TextBox Area(bool editable) => new()
    {
        AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        IsReadOnly = !editable, FontSize = 14, Padding = new Thickness(8), MinHeight = 160,
    };

    TranslateWindow()
    {
        Title = "Çeviri";
        Icon = AppIcon.WindowIcon;
        Width = 860;
        Height = 520;
        MinWidth = 560;
        MinHeight = 340;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        loading = true;
        from.Items.Add(new ComboBoxItem { Content = "Dili algıla", Tag = null });
        foreach (var language in Languages.All)
        {
            from.Items.Add(new ComboBoxItem { Content = language.Name, Tag = language.Code });
            to.Items.Add(new ComboBoxItem { Content = language.Name, Tag = language.Code });
        }
        from.SelectedIndex = 0;
        to.SelectedItem = to.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string?)i.Tag == Preferences.Shared.TranslateTarget) ?? to.Items[0];
        loading = false;
        from.SelectionChanged += (_, _) => Retranslate();
        to.SelectionChanged += (_, _) =>
        {
            if (loading) return;
            if (to.SelectedItem is ComboBoxItem { Tag: string code }) Preferences.Shared.TranslateTarget = code;
            Retranslate();
        };
        var swap = new Button { Content = "", FontFamily = Theme.IconFont, Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(8, 0, 8, 0), ToolTip = "Dilleri ve metinleri değiştir" };
        swap.Click += (_, _) => Swap();

        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var languages = new StackPanel { Orientation = Orientation.Horizontal };
        languages.Children.Add(from);
        languages.Children.Add(swap);
        languages.Children.Add(to);
        top.Children.Add(languages);

        var texts = new Grid();
        texts.ColumnDefinitions.Add(new ColumnDefinition());
        texts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        texts.ColumnDefinitions.Add(new ColumnDefinition());
        texts.Children.Add(source);
        Grid.SetColumn(result, 2);
        texts.Children.Add(result);
        source.TextChanged += (_, _) =>
        {
            if (loading) return;
            typing.Stop();
            typing.Start();
        };
        typing.Tick += (_, _) =>
        {
            typing.Stop();
            Retranslate();
        };

        var copySource = new Button { Content = "Metni kopyala", Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 0, 8, 0) };
        copySource.Click += (_, _) => Copy(source.Text, "Metin panoya kopyalandı");
        var copyResult = new Button { Content = "Çeviriyi kopyala", Padding = new Thickness(12, 5, 12, 5) };
        if (TryFindResource("AccentButtonStyle") is Style accent) copyResult.Style = accent;
        copyResult.Click += (_, _) => Copy(result.Text, "Çeviri panoya kopyalandı");
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(copySource);
        buttons.Children.Add(copyResult);
        var bottom = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(buttons, Dock.Right);
        bottom.Children.Add(buttons);
        bottom.Children.Add(status);

        var root = new DockPanel { Margin = new Thickness(16) };
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(top);
        root.Children.Add(bottom);
        root.Children.Add(texts);
        Content = root;
        Closed += (_, _) =>
        {
            running?.Cancel();
            open = null;
        };
    }

    void SetSource(string text)
    {
        loading = true;
        source.Text = text;
        from.SelectedIndex = 0;
        // Text already in the target language goes the other way: Turkish to English, anything else to Turkish.
        if (Languages.Guess(text) is { } language && to.SelectedItem is ComboBoxItem { Tag: string target } && language == target)
            Select(to, language == "tr" ? "en" : "tr");
        loading = false;
        Retranslate();
    }

    static void Select(ComboBox box, string code) =>
        box.SelectedItem = box.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string?)i.Tag == code) ?? box.SelectedItem;

    void Swap()
    {
        if (to.SelectedItem is not ComboBoxItem { Tag: string target }) return;
        var detected = (from.SelectedItem as ComboBoxItem)?.Tag as string ?? Languages.Guess(source.Text);
        loading = true;
        var translated = result.Text;
        Select(from, target);
        if (detected is not null) Select(to, detected);
        if (to.SelectedItem is ComboBoxItem { Tag: string code }) Preferences.Shared.TranslateTarget = code;
        source.Text = translated;
        loading = false;
        Retranslate();
    }

    async void Retranslate()
    {
        if (loading) return;
        running?.Cancel();
        var text = source.Text;
        if (string.IsNullOrWhiteSpace(text) || to.SelectedItem is not ComboBoxItem { Tag: string target })
        {
            result.Text = "";
            status.Text = "";
            return;
        }
        var cancel = running = new CancellationTokenSource();
        var chosen = (from.SelectedItem as ComboBoxItem)?.Tag as string;
        try
        {
            var translation = await Translator.Translate(TextReflow.Join(text), chosen, target, message => { if (!cancel.IsCancellationRequested) status.Text = message; }, cancel.Token);
            if (cancel.IsCancellationRequested) return;
            result.Text = translation.Text;
            if (translation.From == target)
            {
                status.Text = $"Metin zaten {Languages.Name(target)}; üstten başka bir hedef dil seçin.";
                return;
            }
            var detected = chosen is null && translation.From.Length > 0 ? $"{Languages.Name(translation.From)} algılandı · " : "";
            status.Text = $"{detected}{translation.Engine}";
        }
        catch (OperationCanceledException) { /* a newer request took over */ }
        catch (Exception error)
        {
            if (cancel.IsCancellationRequested) return;
            result.Text = "";
            status.Text = error.Message;
        }
    }

    static void Copy(string text, string message)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        Output.CopyText(text);
        Toast.Show(message, Icons.Copy);
    }
}
