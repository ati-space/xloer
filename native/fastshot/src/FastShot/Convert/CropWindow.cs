using System.Globalization;
using System.Windows.Controls.Primitives;
using FastShot.Core.Convert;

namespace FastShot;

public enum CropAspect { Free, Original, Square, FourThree, ThreeTwo, SixteenNine, NineSixteen }

public static class CropAspectExtensions
{
    public static string Title(this CropAspect aspect) => aspect switch
    {
        CropAspect.Free => "Serbest",
        CropAspect.Original => "Orijinal",
        CropAspect.Square => "1:1",
        CropAspect.FourThree => "4:3",
        CropAspect.ThreeTwo => "3:2",
        CropAspect.SixteenNine => "16:9",
        _ => "9:16",
    };

    public static double? Ratio(this CropAspect aspect, Size image) => aspect switch
    {
        CropAspect.Free => null,
        CropAspect.Original => image.Width / Math.Max(1, image.Height),
        CropAspect.Square => 1,
        CropAspect.FourThree => 4.0 / 3,
        CropAspect.ThreeTwo => 3.0 / 2,
        CropAspect.SixteenNine => 16.0 / 9,
        _ => 9.0 / 16,
    };
}

/// <summary>Crops an image file and saves the result as a new file next to it.</summary>
public sealed class CropWindow : Window
{
    const double BarHeight = 52;
    readonly string path;
    readonly CropView crop;
    readonly TextBlock size = new() { VerticalAlignment = VerticalAlignment.Center, FontSize = 12, Margin = new Thickness(12, 0, 0, 0) };

    public static void Show(string path)
    {
        BitmapSource image;
        try { image = ImageConversion.LoadOriented(path, out _); }
        catch
        {
            Toast.Show("Görüntü açılamadı", Icons.Clear, Path.GetFileName(path));
            return;
        }
        var window = new CropWindow(path, image);
        window.Show();
        window.Activate();
        window.crop.Focus();
    }

    CropWindow(string path, BitmapSource image)
    {
        this.path = path;
        crop = new CropView(image);
        Title = $"Kırp — {Path.GetFileName(path)}";
        Icon = AppIcon.WindowIcon;
        var work = SystemParameters.WorkArea;
        var fit = Math.Min(1, Math.Min(work.Width * 0.75 / image.PixelWidth, (work.Height * 0.75 - BarHeight) / image.PixelHeight));
        Width = Math.Max(680, image.PixelWidth * fit + 48 + 16);
        Height = Math.Max(440, image.PixelHeight * fit + 48 + BarHeight + 40);
        MinWidth = 600;
        MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var aspects = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var imageSize = new Size(image.PixelWidth, image.PixelHeight);
        foreach (var aspect in Enum.GetValues<CropAspect>())
        {
            var button = new RadioButton
            {
                Content = aspect.Title(), GroupName = "aspect", IsChecked = aspect == CropAspect.Free,
                Style = (Style)FindResource(typeof(ToggleButton)), Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 4, 0), MinWidth = 0,
            };
            button.Checked += (_, _) => crop.Aspect = aspect.Ratio(imageSize);
            aspects.Children.Add(button);
        }
        var cancel = new Button { Content = "İptal", IsCancel = true, Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(0, 0, 8, 0) };
        cancel.Click += (_, _) => Close();
        var apply = new Button { Content = "Kırp ve Kaydet", IsDefault = true, Padding = new Thickness(14, 5, 14, 5) };
        if (TryFindResource("AccentButtonStyle") is Style accent) apply.Style = accent;
        apply.Click += (_, _) => Apply();
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        actions.Children.Add(cancel);
        actions.Children.Add(apply);

        var bar = new DockPanel { Height = BarHeight, LastChildFill = false, Margin = new Thickness(14, 0, 14, 0) };
        DockPanel.SetDock(actions, Dock.Right);
        bar.Children.Add(actions);
        bar.Children.Add(aspects);
        bar.Children.Add(size);
        var barBorder = new Border { Child = bar, BorderThickness = new Thickness(0, 1, 0, 0), BorderBrush = UiPalette.Editor.Separator };
        var root = new DockPanel();
        DockPanel.SetDock(barBorder, Dock.Bottom);
        root.Children.Add(barBorder);
        root.Children.Add(crop);
        Content = root;

        crop.Changed = rect => size.Text = string.Create(CultureInfo.InvariantCulture, $"{rect.Width:0} × {rect.Height:0} px");
        crop.Commit = Apply;
        crop.Changed(crop.Crop);
    }

    void Apply()
    {
        try
        {
            var folder = ConversionCenter.Destination(path);
            Directory.CreateDirectory(folder);
            var r = crop.Crop;
            var output = FileTools.Crop(path, new Int32Rect((int)r.X, (int)r.Y, (int)r.Width, (int)r.Height), folder);
            Close();
            if (Preferences.Shared.RevealAfterConvert) Output.Reveal(output);
            Toast.Show("Kırpıldı", "", Path.GetFileName(output), "Göster", () => Output.Reveal(output));
        }
        catch (Exception error)
        {
            Output.ShowError(error, "Görüntü kırpılamadı");
        }
    }
}

/// <summary>Shows the image with a movable, resizable crop frame. <see cref="Crop"/> is in image pixels from the top-left.</summary>
public sealed class CropView : FrameworkElement
{
    readonly BitmapSource image;
    Rect crop;
    double? aspect;
    enum DragKind { None, Move, Resize, Create }
    DragKind drag;
    AnnotationHandle dragHandle;
    Point dragStart;
    Rect dragOriginal;

    public Action<Rect>? Changed { get; set; }
    public Action? Commit { get; set; }

    public CropView(BitmapSource image)
    {
        this.image = image;
        crop = ImageBounds;
        Focusable = true;
        FocusVisualStyle = null;
        ClipToBounds = true;
    }

    public Rect Crop
    {
        get => crop;
        private set
        {
            crop = value;
            Changed?.Invoke(crop);
            InvalidateVisual();
        }
    }

    public double? Aspect
    {
        get => aspect;
        set
        {
            aspect = value;
            ApplyAspect();
        }
    }

    Rect ImageBounds => new(0, 0, image.PixelWidth, image.PixelHeight);

    /// <summary>Where the whole image is drawn, fitted with a margin.</summary>
    Rect ImageRect
    {
        get
        {
            var area = new Rect(RenderSize).Inset(24, 24);
            if (area.IsEmpty || area.Width <= 0 || area.Height <= 0) return new Rect(0, 0, 1, 1);
            var s = Math.Min(area.Width / image.PixelWidth, area.Height / image.PixelHeight);
            var w = image.PixelWidth * s;
            var h = image.PixelHeight * s;
            return new Rect(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h);
        }
    }

    double Scale => ImageRect.Width / image.PixelWidth;

    Rect ToView(Rect r)
    {
        var b = ImageRect;
        var s = Scale;
        return new Rect(b.X + r.X * s, b.Y + r.Y * s, r.Width * s, r.Height * s);
    }

    Point ToImage(Point p)
    {
        var b = ImageRect;
        var s = Scale;
        return new Point((p.X - b.X) / s, (p.Y - b.Y) / s);
    }

    // MARK: Drawing

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Theme.Frozen(Theme.IsDark ? Color.FromRgb(0x14, 0x14, 0x16) : Color.FromRgb(0x9A, 0x9A, 0x9E)), null, new Rect(RenderSize));
        var imageRect = ImageRect;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        dc.DrawImage(image, imageRect);
        var frame = ToView(crop);
        var shade = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(imageRect), new RectangleGeometry(frame));
        dc.DrawGeometry(Theme.Frozen(Color.FromArgb(0x80, 0, 0, 0)), null, shade);
        var thirds = new Pen(Theme.Frozen(Color.FromArgb(0x59, 0xFF, 0xFF, 0xFF)), 1);
        for (var i = 1; i <= 2; i++)
        {
            var x = frame.X + frame.Width * i / 3;
            var y = frame.Y + frame.Height * i / 3;
            dc.DrawLine(thirds, new Point(x, frame.Top), new Point(x, frame.Bottom));
            dc.DrawLine(thirds, new Point(frame.Left, y), new Point(frame.Right, y));
        }
        dc.DrawRectangle(null, new Pen(Brushes.White, 1.5), frame);
        var knobPen = new Pen(Theme.Accent, 1);
        foreach (var (_, point) in Handles(frame)) dc.DrawEllipse(Brushes.White, knobPen, point, 5, 5);
    }

    static (AnnotationHandle handle, Point point)[] Handles(Rect r) =>
    [
        (AnnotationHandle.TopLeft, r.TopLeft), (AnnotationHandle.Top, new Point(r.X + r.Width / 2, r.Top)), (AnnotationHandle.TopRight, r.TopRight),
        (AnnotationHandle.Right, new Point(r.Right, r.Y + r.Height / 2)), (AnnotationHandle.BottomRight, r.BottomRight),
        (AnnotationHandle.Bottom, new Point(r.X + r.Width / 2, r.Bottom)), (AnnotationHandle.BottomLeft, r.BottomLeft), (AnnotationHandle.Left, new Point(r.Left, r.Y + r.Height / 2)),
    ];

    // MARK: Editing

    void SetCrop(Rect rect)
    {
        var r = Rect.Intersect(rect, ImageBounds);
        if (r.IsEmpty || r.Width < 8 || r.Height < 8) return;
        Crop = new Rect(Math.Round(r.X), Math.Round(r.Y), Math.Round(r.Width), Math.Round(r.Height));
    }

    /// <summary>Largest frame of the chosen ratio that fits inside the current one, kept centred.</summary>
    void ApplyAspect()
    {
        if (aspect is not { } ratio) return;
        var r = crop;
        if (r.Width / r.Height > ratio) r.Width = r.Height * ratio;
        else r.Height = r.Width / ratio;
        r.X = crop.X + (crop.Width - r.Width) / 2;
        r.Y = crop.Y + (crop.Height - r.Height) / 2;
        SetCrop(r);
    }

    Rect Resized(Rect original, AnnotationHandle handle, Point p)
    {
        double minX = original.Left, maxX = original.Right, minY = original.Top, maxY = original.Bottom;
        switch (handle)
        {
            case AnnotationHandle.TopLeft: minX = p.X; minY = p.Y; break;
            case AnnotationHandle.Top: minY = p.Y; break;
            case AnnotationHandle.TopRight: maxX = p.X; minY = p.Y; break;
            case AnnotationHandle.Right: maxX = p.X; break;
            case AnnotationHandle.BottomRight: maxX = p.X; maxY = p.Y; break;
            case AnnotationHandle.Bottom: maxY = p.Y; break;
            case AnnotationHandle.BottomLeft: minX = p.X; maxY = p.Y; break;
            case AnnotationHandle.Left: minX = p.X; break;
        }
        var r = Geo.RectBetween(new Point(minX, minY), new Point(maxX, maxY));
        if (aspect is not { } ratio) return r;
        // Keep the ratio: edges drive the other side around the centre, corners stay anchored opposite.
        switch (handle)
        {
            case AnnotationHandle.Top or AnnotationHandle.Bottom:
            {
                var width = r.Height * ratio;
                r = new Rect(original.X + original.Width / 2 - width / 2, r.Y, width, r.Height);
                break;
            }
            case AnnotationHandle.Left or AnnotationHandle.Right:
            {
                var height = r.Width / ratio;
                r = new Rect(r.X, original.Y + original.Height / 2 - height / 2, r.Width, height);
                break;
            }
            default:
            {
                var width = Math.Max(r.Width, r.Height * ratio);
                var height = width / ratio;
                var left = handle is AnnotationHandle.TopLeft or AnnotationHandle.BottomLeft;
                var top = handle is AnnotationHandle.TopLeft or AnnotationHandle.TopRight;
                r = new Rect(left ? original.Right - width : original.Left, top ? original.Bottom - height : original.Top, width, height);
                break;
            }
        }
        return r.Width <= ImageBounds.Width && r.Height <= ImageBounds.Height ? r : original;
    }

    AnnotationHandle? HandleAt(Point p) => Handles(ToView(crop)).Where(h => Geo.Distance(h.point, p) <= 9).Select(h => (AnnotationHandle?)h.handle).FirstOrDefault();

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus();
        var p = e.GetPosition(this);
        if (HandleAt(p) is { } handle)
        {
            drag = DragKind.Resize;
            dragHandle = handle;
            dragOriginal = crop;
        }
        else if (ToView(crop).Contains(p))
        {
            if (e.ClickCount == 2)
            {
                Commit?.Invoke();
                return;
            }
            drag = DragKind.Move;
            dragStart = ToImage(p);
            dragOriginal = crop;
            Cursor = Cursors.SizeAll;
        }
        else
        {
            drag = DragKind.Create;
            dragStart = ToImage(p);
        }
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var view = e.GetPosition(this);
        var p = ToImage(view);
        switch (drag)
        {
            case DragKind.Move:
            {
                var r = dragOriginal.Shifted(p.X - dragStart.X, p.Y - dragStart.Y);
                r.X = Math.Min(Math.Max(0, r.X), ImageBounds.Width - r.Width);
                r.Y = Math.Min(Math.Max(0, r.Y), ImageBounds.Height - r.Height);
                SetCrop(r);
                break;
            }
            case DragKind.Resize:
                SetCrop(Resized(dragOriginal, dragHandle, p));
                break;
            case DragKind.Create:
            {
                var r = Geo.RectBetween(dragStart, p);
                if (aspect is { } ratio)
                {
                    r.Height = r.Width / ratio;
                    if (p.Y < dragStart.Y) r.Y = dragStart.Y - r.Height;
                }
                SetCrop(r);
                break;
            }
            default:
                Cursor = HandleAt(view) is { } handle ? AnnotationController.CursorFor(handle) : ToView(crop).Contains(view) ? Cursors.SizeAll : Cursors.Cross;
                break;
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        drag = DragKind.None;
        ReleaseMouseCapture();
    }
}
