namespace FastShot;

/// <summary>Keeps a capture floating above other windows, e.g. as a reference while you work.</summary>
public sealed class PinWindow
{
    static readonly List<PinWindow> open = [];
    readonly BitmapSource image;
    readonly double pointScale;
    readonly double aspect;
    readonly PanelWindow panel;

    public static void Show(BitmapSource image, double pointScale, Native.RECT frame)
    {
        var pin = new PinWindow(image, pointScale, frame);
        open.Add(pin);
        pin.panel.Show(activate: true);
        Keyboard.Focus(pin.panel.Content);
    }

    PinWindow(BitmapSource image, double pointScale, Native.RECT frame)
    {
        this.image = image;
        this.pointScale = pointScale;
        aspect = frame.Height > 0 ? frame.Width / (double)frame.Height : 1;
        var picture = new Image { Source = image, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.HighQuality);
        var view = new Border
        {
            Child = picture,
            BorderBrush = Theme.Frozen(Color.FromArgb(0xCC, Theme.AccentColor.R, Theme.AccentColor.G, Theme.AccentColor.B)),
            BorderThickness = new Thickness(1),
            Focusable = true,
            FocusVisualStyle = null,
            ToolTip = "Çift tıklayın veya Esc ile kapatın · Tekerlekle boyutlandırın · Sağ tık: seçenekler",
            ContextMenu = MakeMenu(),
        };
        view.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2)
            {
                Close();
                return;
            }
            panel!.BeginDrag();
        };
        view.MouseWheel += (_, e) => Zoom(e.Delta);
        view.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
            else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control) CopyImage();
        };
        panel = new PanelWindow(view, frame, transparent: false);
        panel.Closed += () => open.Remove(this);
    }

    ContextMenu MakeMenu()
    {
        var menu = new ContextMenu();
        void Item(string title, Action action)
        {
            var item = new MenuItem { Header = title };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }
        Item("Kopyala", CopyImage);
        Item("Kaydet…", SaveImage);
        Item("Gerçek boyut", ActualSize);
        menu.Items.Add(new Separator());
        Item("Kapat", Close);
        return menu;
    }

    void CopyImage()
    {
        Output.CopyImage(image, pointScale);
        Toast.Show("Panoya kopyalandı", Icons.Copy);
    }

    void SaveImage()
    {
        var prefs = Preferences.Shared;
        if (SavePanel.Run(Output.BaseName(), prefs.ImageFolder, SavePanel.ImageFormats, prefs.ImageFormat == ImageFormat.Png ? 0 : 1) is not { } choice) return;
        try { Output.WriteImage(image, choice.path, choice.format == 0 ? ImageFormat.Png : ImageFormat.Jpeg, pointScale); }
        catch (Exception error) { Output.ShowError(error); }
    }

    void ActualSize()
    {
        var b = panel.Bounds;
        var scale = panel.Scale / pointScale;
        var width = (int)Math.Round(image.PixelWidth * scale);
        var height = (int)Math.Round(image.PixelHeight * scale);
        panel.SetBounds(new Native.RECT(b.Left, b.Top, b.Left + width, b.Top + height));
    }

    void Zoom(int delta)
    {
        var b = panel.Bounds;
        var factor = 1 + delta / 120.0 * 0.08;
        var width = Math.Clamp(b.Width * factor, 80, 6000);
        var height = width / aspect;
        var left = b.Left + (b.Width - width) / 2;
        var top = b.Top + (b.Height - height) / 2;
        panel.SetBounds(new Native.RECT((int)left, (int)top, (int)(left + width), (int)(top + height)));
    }

    void Close() => panel.Dispose();
}
