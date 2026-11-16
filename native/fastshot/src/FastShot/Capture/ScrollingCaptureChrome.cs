namespace FastShot;

/// <summary>Physical-pixel placement shared by the live HUD and its growing preview.</summary>
public static class ScrollingCaptureLayout
{
    public readonly record struct Placement(Rect Toolbar, Rect Preview);

    public static Placement Place(Rect selection, Rect work, Size toolbar, Size preview, double gap)
    {
        Rect Fit(double x, double y, Size size)
        {
            var w = Math.Min(work.Width, size.Width); var h = Math.Min(work.Height, size.Height);
            return new Rect(Math.Clamp(x, work.Left, work.Right - w), Math.Clamp(y, work.Top, work.Bottom - h), w, h);
        }
        var y = selection.Bottom + gap;
        if (y + toolbar.Height > work.Bottom)
            y = selection.Top - toolbar.Height - gap >= work.Top ? selection.Top - toolbar.Height - gap : work.Bottom - toolbar.Height - gap;
        var bar = Fit(selection.Left + (selection.Width - toolbar.Width) / 2, y, toolbar);
        var x = selection.Left - preview.Width - gap;
        if (x < work.Left) x = selection.Right + gap;
        if (x + preview.Width > work.Right) x = work.Right - preview.Width - gap;
        var image = Fit(x, selection.Bottom - preview.Height, preview);
        if (image.IntersectsWith(bar))
        {
            var available = Math.Max(0, bar.Top - work.Top - gap);
            if (available < 40) return new Placement(bar, Rect.Empty);
            image = Fit(x, Math.Min(selection.Top, bar.Top - preview.Height - gap), new Size(preview.Width, Math.Min(preview.Height, available)));
        }
        return new Placement(bar, image);
    }
}

/// <summary>A small non-activating bar; the target app keeps receiving wheel and keyboard input.</summary>
public sealed class ScrollingCaptureHud
{
    public FrameworkElement Root { get; }
    readonly TextBlock status;
    readonly TextBlock dimensions;
    readonly Button automatic, finish, preview;

    public ScrollingCaptureHud(Action toggleAutomatic, Action finishCapture, Action cancel, Action togglePreview)
    {
        var p = UiPalette.Hud;
        status = new TextBlock { Text = "Hazırlanıyor…", Foreground = p.Foreground, FontSize = 12, TextWrapping = TextWrapping.Wrap, MaxWidth = 440 };
        dimensions = new TextBlock { Foreground = p.Secondary, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, MinWidth = 100 };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        automatic = MakeButton("Otomatik kaydır", toggleAutomatic);
        preview = MakeButton("Önizlemeyi gizle", togglePreview);
        finish = MakeButton("Bitir", finishCapture, true); finish.IsEnabled = false;
        row.Children.Add(automatic); row.Children.Add(preview); row.Children.Add(MakeButton("Vazgeç", cancel)); row.Children.Add(finish);
        var heading = new DockPanel { Margin = new Thickness(3, 0, 3, 6) };
        DockPanel.SetDock(dimensions, Dock.Right); heading.Children.Add(dimensions);
        heading.Children.Add(new TextBlock { Text = "KAYDIRMALI YAKALAMA", Foreground = p.Secondary, FontSize = 10, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 15, 0) });
        var stack = new StackPanel(); stack.Children.Add(heading); stack.Children.Add(status); stack.Children.Add(row);
        Root = new Viewbox { Child = Ui.Hud(stack, 14, 12), Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly };
    }

    static Button MakeButton(string text, Action action, bool primary = false)
    {
        var button = new Button { Content = text, FontFamily = Theme.UiFont, FontSize = 12, Foreground = Brushes.White,
            Background = primary ? Theme.Accent : UiPalette.Hud.Hover, BorderThickness = new Thickness(0),
            Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(3, 0, 3, 0), Cursor = Cursors.Hand };
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(15));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(content); button.Template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        button.Click += (_, _) => action();
        return button;
    }

    public void Update(string message, int width, int height, bool auto, bool ready, bool previewVisible)
    {
        status.Text = message; dimensions.Text = $"{width:N0} × {height:N0} px";
        automatic.Content = auto ? "Otomatiği durdur" : "Otomatik kaydır";
        preview.Content = previewVisible ? "Önizlemeyi gizle" : "Önizlemeyi göster";
        finish.IsEnabled = ready;
    }

    public void SetFinishing()
    {
        status.Text = "Son kare birleştiriliyor…";
        automatic.IsEnabled = finish.IsEnabled = preview.IsEnabled = false;
    }

    public Size Measure(Size available)
    {
        // Read the card's natural size, not the current HWND-constrained root size.
        Root.UpdateLayout();
        var card = (FrameworkElement)((Viewbox)Root).Child;
        card.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var natural = card.DesiredSize;
        var factor = Math.Min(1, Math.Min(available.Width / natural.Width, available.Height / natural.Height));
        var size = new Size(natural.Width * factor, natural.Height * factor);
        Root.Width = size.Width; Root.Height = size.Height;
        return size;
    }
}

/// <summary>Explicit natural size prevents the previous native window height clipping a growing image.</summary>
public sealed class ScrollingCapturePreview
{
    readonly Image image = new() { Stretch = Stretch.Fill, HorizontalAlignment = HorizontalAlignment.Center };
    readonly TextBlock caption = new() { Foreground = UiPalette.Hud.Secondary, FontSize = 11, Height = 16, Margin = new Thickness(0, 0, 0, 7) };
    public FrameworkElement Root { get; }
    public bool HasImage => image.Source is not null;

    public ScrollingCapturePreview()
    {
        var stack = new StackPanel(); stack.Children.Add(caption); stack.Children.Add(image);
        Root = Ui.Hud(stack, 9, 8);
    }

    public Size Update(BitmapSource? source, int width, int height, double maxImageHeight)
    {
        if (source is not null) image.Source = source;
        var imageWidth = Math.Min(184, maxImageHeight * width / height);
        image.Width = imageWidth; image.Height = imageWidth * height / width;
        caption.Text = $"{width:N0} × {height:N0} px";
        var chrome = 2 * (Ui.ChromeMargin + 8 + 1);
        var size = new Size(Math.Max(128, imageWidth) + chrome, image.Height + 23 + chrome);
        Root.Width = size.Width; Root.Height = size.Height;
        return size;
    }
}

/// <summary>A click-through shade with a clear hole: scrolling still reaches the underlying app.</summary>
sealed class ScrollingCaptureShade(Rect selection) : FrameworkElement
{
    protected override void OnRender(DrawingContext dc)
    {
        var outside = new CombinedGeometry(GeometryCombineMode.Exclude,
            new RectangleGeometry(new Rect(RenderSize)), new RectangleGeometry(selection));
        dc.DrawGeometry(Theme.Frozen(Color.FromArgb(95, 0, 0, 0)), null, outside);
        dc.DrawRectangle(null, new Pen(Brushes.White, 1.5), selection);
    }
}
