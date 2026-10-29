using System.Globalization;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;

namespace FastShot;

public enum IconShape { Square, SquareFilled, Dot, DrawToggle }

/// <summary>Segoe Fluent Icons / MDL2 glyphs plus hand-drawn tool icons, in one brush.</summary>
public sealed class IconView : FrameworkElement
{
    readonly string? glyph;
    readonly EditorTool? tool;
    readonly IconShape? shape;
    readonly double size;
    Brush brush = Brushes.White;

    public IconView(string glyph, double size = 15) { this.glyph = glyph; this.size = size; Init(); }
    public IconView(EditorTool tool, double size = 18) { this.tool = tool; this.size = size; Init(); }
    public IconView(IconShape shape, double size = 16) { this.shape = shape; this.size = size; Init(); }

    void Init()
    {
        IsHitTestVisible = false;
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Center;
        SnapsToDevicePixels = true;
    }

    public Brush Brush
    {
        get => brush;
        set { if (brush == value) return; brush = value; InvalidateVisual(); }
    }

    protected override Size MeasureOverride(Size availableSize) => new(size, size);

    protected override void OnRender(DrawingContext dc)
    {
        if (glyph is not null)
        {
            var text = new FormattedText(glyph, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(Theme.IconFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(text, new Point((size - text.Width) / 2, (size - text.Height) / 2));
            return;
        }
        var s = size;
        var pen = new Pen(brush, Math.Max(1.2, s * 0.085)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        Point P(double x, double y) => new(x * s, y * s);
        if (shape is { } kind)
        {
            switch (kind)
            {
                case IconShape.Square:
                    dc.DrawRoundedRectangle(null, pen, new Rect(P(0.2, 0.2), P(0.8, 0.8)), s * 0.08, s * 0.08);
                    break;
                case IconShape.SquareFilled:
                    dc.DrawRoundedRectangle(brush, pen, new Rect(P(0.2, 0.2), P(0.8, 0.8)), s * 0.08, s * 0.08);
                    break;
                case IconShape.Dot:
                    dc.DrawEllipse(brush, null, P(0.5, 0.5), s * 0.3, s * 0.3);
                    break;
                case IconShape.DrawToggle:
                    dc.DrawEllipse(null, pen, P(0.5, 0.5), s * 0.42, s * 0.42);
                    DrawPencil(dc, pen, s, 0.72);
                    break;
            }
            return;
        }
        switch (tool)
        {
            case EditorTool.Select:
                dc.DrawGeometry(null, pen, Polygon(s, (0.3, 0.14), (0.3, 0.8), (0.46, 0.65), (0.57, 0.88), (0.66, 0.84), (0.55, 0.61), (0.76, 0.61)));
                break;
            case EditorTool.Pen:
                DrawPencil(dc, pen, s, 1);
                break;
            case EditorTool.Line:
                dc.DrawLine(pen, P(0.2, 0.8), P(0.8, 0.2));
                break;
            case EditorTool.Arrow:
                dc.DrawLine(pen, P(0.22, 0.78), P(0.78, 0.22));
                dc.DrawLine(pen, P(0.78, 0.22), P(0.44, 0.22));
                dc.DrawLine(pen, P(0.78, 0.22), P(0.78, 0.56));
                break;
            case EditorTool.Rectangle:
                dc.DrawRoundedRectangle(null, pen, new Rect(P(0.16, 0.26), P(0.84, 0.74)), s * 0.05, s * 0.05);
                break;
            case EditorTool.Ellipse:
                dc.DrawEllipse(null, pen, P(0.5, 0.5), s * 0.33, s * 0.33);
                break;
            case EditorTool.Marker:
            {
                var color = brush is SolidColorBrush solid ? solid.Color : Colors.White;
                var wide = new Pen(Theme.Frozen(Color.FromArgb(0x80, color.R, color.G, color.B)), s * 0.26) { StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat };
                dc.DrawLine(wide, P(0.14, 0.66), P(0.86, 0.66));
                dc.DrawGeometry(null, pen, Polygon(s, (0.36, 0.52), (0.58, 0.18), (0.74, 0.28), (0.52, 0.62)));
                break;
            }
            case EditorTool.Text:
            {
                var text = new FormattedText("T", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, AnnotationRenderer.Typeface, s * 0.9, brush,
                                             VisualTreeHelper.GetDpi(this).PixelsPerDip);
                dc.DrawText(text, new Point((s - text.Width) / 2, (s - text.Height) / 2));
                break;
            }
            case EditorTool.Pixelate:
            {
                var cell = s * 0.6 / 3;
                for (var row = 0; row < 3; row++)
                    for (var column = 0; column < 3; column++)
                        if ((row + column) % 2 == 0) dc.DrawRectangle(brush, null, new Rect(s * 0.2 + column * cell, s * 0.2 + row * cell, cell, cell));
                dc.DrawRectangle(null, new Pen(brush, Math.Max(1, s * 0.05)), new Rect(P(0.2, 0.2), P(0.8, 0.8)));
                break;
            }
            case EditorTool.Step:
            {
                dc.DrawEllipse(null, pen, P(0.5, 0.5), s * 0.38, s * 0.38);
                var text = new FormattedText("1", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, AnnotationRenderer.BoldTypeface, s * 0.5, brush,
                                             VisualTreeHelper.GetDpi(this).PixelsPerDip);
                dc.DrawText(text, new Point((s - text.Width) / 2, (s - text.Height) / 2));
                break;
            }
        }
    }

    static void DrawPencil(DrawingContext dc, Pen pen, double s, double scale)
    {
        var o = (1 - scale) / 2;
        Point P(double x, double y) => new((o + x * scale) * s, (o + y * scale) * s);
        var body = new StreamGeometry();
        using (var ctx = body.Open())
        {
            ctx.BeginFigure(P(0.2, 0.8), false, true);
            ctx.LineTo(P(0.26, 0.6), true, true);
            ctx.LineTo(P(0.66, 0.2), true, true);
            ctx.LineTo(P(0.8, 0.34), true, true);
            ctx.LineTo(P(0.4, 0.74), true, true);
        }
        dc.DrawGeometry(null, pen, body);
        dc.DrawLine(pen, P(0.58, 0.28), P(0.72, 0.42));
    }

    static StreamGeometry Polygon(double s, params (double x, double y)[] points)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(points[0].x * s, points[0].y * s), false, true);
            foreach (var (x, y) in points.Skip(1)) ctx.LineTo(new Point(x * s, y * s), true, true);
        }
        geometry.Freeze();
        return geometry;
    }
}

/// <summary>A flat square button with an icon; never takes keyboard focus, so text editing on the canvas continues.</summary>
public sealed class IconButton : Border
{
    readonly UiPalette palette;
    IconView icon;
    readonly Brush? tint;
    bool selected, pressed;

    public IconButton(IconView icon, string help, Action action, UiPalette palette, double size = 30, Brush? tint = null)
    {
        this.icon = icon;
        this.palette = palette;
        this.tint = tint;
        Width = size;
        Height = size;
        CornerRadius = new CornerRadius(6);
        Child = icon;
        ToolTip = help;
        Focusable = false;
        Background = Brushes.Transparent;
        Cursor = Cursors.Arrow;
        MouseEnter += (_, _) => Refresh();
        MouseLeave += (_, _) => { pressed = false; Refresh(); };
        MouseLeftButtonDown += (_, e) => { pressed = true; CaptureMouse(); e.Handled = true; };
        MouseLeftButtonUp += (_, e) =>
        {
            var fire = pressed && IsMouseOver;
            pressed = false;
            ReleaseMouseCapture();
            e.Handled = true;
            if (fire) action();
        };
        IsEnabledChanged += (_, _) => { Opacity = IsEnabled ? 1 : 0.35; Refresh(); };
        Refresh();
    }

    public void SetIcon(IconView value)
    {
        icon = value;
        Child = value;
        Refresh();
    }

    public bool Selected
    {
        get => selected;
        set { if (selected == value) return; selected = value; Refresh(); }
    }

    void Refresh()
    {
        Background = selected ? palette.Accent : IsMouseOver && IsEnabled ? palette.Hover : Brushes.Transparent;
        icon.Brush = selected ? palette.AccentForeground : tint ?? palette.Foreground;
    }
}

/// <summary>Rounded text button for the floating bars ("Kaydı Başlat", "Bitir").</summary>
public sealed class PillButton : Border
{
    public PillButton(string title, IconView? icon, string help, Brush background, Action action)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (icon is not null)
        {
            icon.Brush = Brushes.White;
            icon.Margin = new Thickness(0, 0, 6, 0);
            row.Children.Add(icon);
        }
        row.Children.Add(new TextBlock { Text = title, Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center });
        Child = row;
        Background = background;
        CornerRadius = new CornerRadius(14);
        Padding = new Thickness(12, 5, 12, 5);
        ToolTip = help;
        Focusable = false;
        Cursor = Cursors.Hand;
        VerticalAlignment = VerticalAlignment.Center;
        MouseEnter += (_, _) => Opacity = 0.88;
        MouseLeave += (_, _) => Opacity = 1;
        MouseLeftButtonUp += (_, e) => { e.Handled = true; action(); };
        MouseLeftButtonDown += (_, e) => e.Handled = true;
    }
}

public sealed class ColorSwatches : UniformGrid
{
    readonly ToolState tools;
    readonly List<(RGBAColor color, Border ring)> swatches = [];

    public ColorSwatches(ToolState tools, IEnumerable<RGBAColor> colors, int columns, double swatch)
    {
        this.tools = tools;
        Columns = columns;
        foreach (var color in colors)
        {
            var dot = new Border
            {
                Width = swatch, Height = swatch, CornerRadius = new CornerRadius(swatch / 2), Background = color.Brush(),
                BorderBrush = Theme.Frozen(Color.FromArgb((byte)(color.Luminance > 0.9 ? 0x99 : 0x2E), 0xFF, 0xFF, 0xFF)), BorderThickness = new Thickness(1),
            };
            if (color.Luminance > 0.9) dot.BorderBrush = Theme.Frozen(Color.FromArgb(0x55, 0, 0, 0));
            var ring = new Border
            {
                Padding = new Thickness(2), CornerRadius = new CornerRadius(swatch / 2 + 3), BorderThickness = new Thickness(2),
                BorderBrush = Brushes.Transparent, Child = dot, Margin = new Thickness(1.5), Background = Brushes.Transparent,
                ToolTip = color.Hex, Focusable = false, Cursor = Cursors.Arrow,
            };
            var chosen = color;
            ring.MouseLeftButtonDown += (_, e) => { e.Handled = true; tools.Color = chosen; };
            swatches.Add((color, ring));
            Children.Add(ring);
        }
        tools.Changed += Refresh;
        Refresh();
    }

    void Refresh()
    {
        foreach (var (color, ring) in swatches) ring.BorderBrush = color.Hex == tools.Color.Hex ? Theme.Accent : Brushes.Transparent;
    }
}

public sealed class SizePicker : StackPanel
{
    readonly ToolState tools;
    readonly UiPalette palette;
    readonly List<Border> dots = [];

    public SizePicker(ToolState tools, UiPalette palette, double dotWidth = 18)
    {
        this.tools = tools;
        this.palette = palette;
        Orientation = Orientation.Horizontal;
        HorizontalAlignment = HorizontalAlignment.Center;
        for (var i = 0; i < ToolState.LineWidths.Length; i++)
        {
            var diameter = 4 + i * 3;
            var dot = new Border { Width = diameter, Height = diameter, CornerRadius = new CornerRadius(diameter / 2.0) };
            var cell = new Border { Width = dotWidth, Height = 20, Background = Brushes.Transparent, Child = dot, ToolTip = ToolState.SizeNames[i], Focusable = false };
            var index = i;
            cell.MouseLeftButtonDown += (_, e) => { e.Handled = true; tools.Size = index; };
            dots.Add(dot);
            Children.Add(cell);
        }
        tools.Changed += Refresh;
        Refresh();
    }

    void Refresh()
    {
        for (var i = 0; i < dots.Count; i++)
        {
            dots[i].Background = tools.Size == i ? palette.Accent : palette.Foreground;
            dots[i].Opacity = tools.Size == i ? 1 : 0.75;
        }
    }
}

public static class Ui
{
    /// <summary>Transparent space around floating chrome so its shadow has room.</summary>
    public const double ChromeMargin = 8;

    /// <summary>Dark floating panel look shared by the overlay and recording bars.</summary>
    public static Border Hud(UIElement content, double radius = 12, double padding = 6)
    {
        var border = new Border
        {
            Child = content,
            Background = UiPalette.Hud.Background,
            BorderBrush = UiPalette.Hud.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(radius),
            Padding = new Thickness(padding),
            Margin = new Thickness(ChromeMargin),
            Effect = Theme.Shadow(),
        };
        TextElement.SetForeground(border, UiPalette.Hud.Foreground);
        return border;
    }

    public static Border VSeparator(UiPalette palette, double height = 20) =>
        new() { Width = 1, Height = height, Background = palette.Separator, Margin = new Thickness(5, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center };

    public static Border HSeparator(UiPalette palette, double width = 56) =>
        new() { Height = 1, Width = width, Background = palette.Separator, Margin = new Thickness(0, 5, 0, 5), HorizontalAlignment = HorizontalAlignment.Center };

    public static TextBlock Label(string text, UiPalette palette, double size = 12, bool secondary = false, FontWeight? weight = null) => new()
    {
        Text = text,
        FontSize = size,
        Foreground = secondary ? palette.Secondary : palette.Foreground,
        FontWeight = weight ?? FontWeights.Normal,
        VerticalAlignment = VerticalAlignment.Center,
    };

    public static string Format(double seconds, bool precise = true)
    {
        var clamped = Math.Max(0, seconds);
        var minutes = (int)clamped / 60;
        var rest = clamped - minutes * 60;
        return precise
            ? string.Create(CultureInfo.InvariantCulture, $"{minutes:00}:{rest:00.0}")
            : string.Create(CultureInfo.InvariantCulture, $"{minutes:00}:{(int)rest:00}");
    }
}
