namespace FastShot;

/// <summary>Capture several regions, arrange them, then export pages or a vertical image.</summary>
public sealed class CaptureQueueWindow : Window
{
    readonly CaptureSequence sequence = new();
    readonly ListBox list = new() { Margin = new Thickness(12), HorizontalContentAlignment = HorizontalAlignment.Stretch };
    readonly TextBlock status = new() { Margin = new Thickness(14, 0, 14, 12), TextWrapping = TextWrapping.Wrap };
    readonly Action capture;
    readonly Button pdf, png, up, down, remove;
    bool closed;

    public CaptureQueueWindow(Action capture)
    {
        this.capture = capture;
        Title = "FastShot Yakalama Kuyruğu";
        Icon = AppIcon.WindowIcon;
        Width = 650; Height = 620; MinWidth = 480; MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var root = new DockPanel();
        var bar = new WrapPanel { Margin = new Thickness(10) };
        Button Add(string title, Action action)
        {
            var button = new Button { Content = title, Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(3) };
            button.Click += (_, _) => action();
            bar.Children.Add(button);
            return button;
        }
        Add("Sonraki alanı yakala", BeginCapture);
        up = Add("Yukarı", () => Move(-1));
        down = Add("Aşağı", () => Move(1));
        remove = Add("Kaldır", () => { if (list.SelectedIndex is var i && i >= 0) { sequence.RemoveAt(i); Refresh(); } });
        pdf = Add("PDF kaydet…", () => Export(true));
        png = Add("Tek PNG kaydet…", () => Export(false));
        DockPanel.SetDock(bar, Dock.Top); root.Children.Add(bar);
        DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status);
        root.Children.Add(list); Content = root;
        list.SelectionChanged += (_, _) => UpdateButtons();
        Closed += (_, _) => closed = true;
        Refresh();
    }

    public async void BeginCapture()
    {
        if (closed) return;
        Hide();
        await Task.Delay(200); // Let the desktop compositor remove this window before freezing screens.
        if (!closed) capture();
    }

    public void ReturnToQueue()
    {
        if (closed) return;
        Show(); Activate();
    }

    public void Add(BitmapSource image)
    {
        try { sequence.Add(image); Refresh(); }
        catch (Exception error) { Output.ShowError(error, "Kuyruğa eklenemedi"); }
        ReturnToQueue();
    }

    void Move(int delta)
    {
        var i = list.SelectedIndex;
        if (i < 0 || i + delta < 0 || i + delta >= sequence.Images.Count) return;
        sequence.Move(i, i + delta); Refresh(); list.SelectedIndex = i + delta;
    }

    void Refresh()
    {
        list.Items.Clear();
        for (var i = 0; i < sequence.Images.Count; i++)
        {
            var image = sequence.Images[i];
            var row = new DockPanel { Margin = new Thickness(4) };
            row.Children.Add(new Image { Source = ImageExporter.Scaled(image, 160), Width = 160, Height = 100, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 12, 0) });
            row.Children.Add(new TextBlock { Text = $"{i + 1}. alan\n{image.PixelWidth} × {image.PixelHeight}", VerticalAlignment = VerticalAlignment.Center });
            list.Items.Add(row);
        }
        status.Text = sequence.Images.Count == 0 ? "Birkaç alanı peş peşe yakalayın. Seçimden sonra Enter veya Kuyruğa ekle düğmesini kullanın. PDF her alanı ayrı sayfaya, PNG alt alta tek görüntüye kaydeder." : $"{sequence.Images.Count} alan · {sequence.Bytes / 1024 / 1024} MB. Kaydetmek kuyruğu temizlemez.";
        if (list.Items.Count > 0) list.SelectedIndex = list.Items.Count - 1;
        UpdateButtons();
    }

    void UpdateButtons()
    {
        pdf.IsEnabled = png.IsEnabled = sequence.Images.Count > 0;
        remove.IsEnabled = list.SelectedIndex >= 0;
        up.IsEnabled = list.SelectedIndex > 0;
        down.IsEnabled = list.SelectedIndex >= 0 && list.SelectedIndex + 1 < sequence.Images.Count;
    }

    void Export(bool asPdf)
    {
        var formats = asPdf ? new[] { new SavePanel.Format("PDF belgesi", "pdf") } : new[] { SavePanel.ImageFormats[0] };
        if (SavePanel.Run(Output.BaseName() + " kuyruk", Preferences.Shared.ImageFolder, formats, owner: this) is not { } choice) return;
        try
        {
            sequence.Export(choice.path, asPdf);
            Toast.Show("Kuyruk kaydedildi", Icons.Save, Path.GetFileName(choice.path), "Göster", () => Output.Reveal(choice.path));
        }
        catch (Exception error) { Output.ShowError(error, "Kuyruk kaydedilemedi"); }
    }
}
