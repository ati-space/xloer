using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FastShot.Core;

/// <summary>Draws annotations with WPF. The same code paints the live canvas, flattened
/// screenshots and every exported video frame, so what you see while editing is exactly
/// what gets saved. Every context is y-down: canvas points, origin top-left.</summary>
public static class AnnotationRenderer
{
    public static readonly FontFamily FontFamily = new("Segoe UI");
    public static readonly Typeface Typeface = new(FontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    public static readonly Typeface BoldTypeface = new(FontFamily, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

    public static Size TextPadding(double fontSize) => new(Math.Ceiling(fontSize * 0.4), Math.Ceiling(fontSize * 0.2));

    public static Size TextSize(string text, double fontSize) => Measure(text, Typeface, fontSize);

    public static Size TextBoxSize(string text, double fontSize)
    {
        var size = TextSize(text, fontSize);
        var pad = TextPadding(fontSize);
        return new Size(size.Width + pad.Width * 2, size.Height + pad.Height * 2);
    }

    public static FormattedText Format(string text, Typeface typeface, double size, Brush brush) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, size, brush, 1.0);

    static Size Measure(string text, Typeface typeface, double size)
    {
        // An empty or newline-terminated string still occupies its last line.
        var sample = text.Length == 0 || text.EndsWith('\n') ? text + " " : text;
        var formatted = Format(sample, typeface, size, Brushes.Black);
        return new Size(Math.Ceiling(formatted.WidthIncludingTrailingWhitespace) + 2, Math.Ceiling(formatted.Height) + 1);
    }

    public static void Draw(IEnumerable<Annotation> annotations, DrawingContext dc, BitmapSource? background, Size canvasSize)
    {
        foreach (var a in annotations) Draw(a, dc, background, canvasSize);
    }

    static Pen MakePen(Brush brush, double width, PenLineJoin join = PenLineJoin.Round)
    {
        var pen = new Pen(brush, width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = join };
        pen.Freeze();
        return pen;
    }

    public static void Draw(Annotation a, DrawingContext dc, BitmapSource? background, Size canvasSize)
    {
        var brush = a.Color.Brush();
        switch (a.Kind)
        {
            case AnnotationKind.pen:
                DrawStroke(a.Points, brush, a.StrokeWidth, dc);
                break;
            case AnnotationKind.marker:
                DrawStroke(a.Points, a.Color.WithAlpha(0.42 * a.Color.A).Brush(), a.StrokeWidth, dc);
                break;
            case AnnotationKind.line:
                if (a.Points.Count >= 2) dc.DrawLine(MakePen(brush, a.StrokeWidth), a.Points[0], a.Points[1]);
                break;
            case AnnotationKind.arrow:
                DrawArrow(a, brush, dc);
                break;
            case AnnotationKind.rectangle:
                if (a.Filled) dc.DrawRectangle(brush, null, a.Box);
                else dc.DrawRectangle(null, MakePen(brush, a.StrokeWidth, PenLineJoin.Miter), a.Box);
                break;
            case AnnotationKind.ellipse:
            {
                var box = a.Box;
                if (a.Filled) dc.DrawEllipse(brush, null, box.Center(), box.Width / 2, box.Height / 2);
                else dc.DrawEllipse(null, MakePen(brush, a.StrokeWidth), box.Center(), box.Width / 2, box.Height / 2);
                break;
            }
            case AnnotationKind.text:
                DrawText(a, dc);
                break;
            case AnnotationKind.pixelate:
                DrawPixelate(a, dc, background, canvasSize);
                break;
            case AnnotationKind.step:
                DrawStep(a, brush, dc);
                break;
        }
    }

    // MARK: Shapes

    static void DrawStroke(List<Point> points, Brush brush, double width, DrawingContext dc)
    {
        if (points.Count == 0) return;
        if (points.Count == 1)
        {
            // A single tap paints a dot.
            dc.DrawEllipse(brush, null, points[0], width / 2, width / 2);
            return;
        }
        dc.DrawGeometry(null, MakePen(brush, width), SmoothPath(points));
    }

    public static StreamGeometry SmoothPath(List<Point> points)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            if (points.Count > 0)
            {
                ctx.BeginFigure(points[0], false, false);
                if (points.Count == 2)
                {
                    ctx.LineTo(points[1], true, true);
                }
                else if (points.Count > 2)
                {
                    for (var i = 1; i < points.Count - 1; i++)
                    {
                        var mid = new Point((points[i].X + points[i + 1].X) / 2, (points[i].Y + points[i + 1].Y) / 2);
                        ctx.QuadraticBezierTo(points[i], mid, true, true);
                    }
                    ctx.LineTo(points[^1], true, true);
                }
            }
        }
        geometry.Freeze();
        return geometry;
    }

    static void DrawArrow(Annotation a, Brush brush, DrawingContext dc)
    {
        if (a.Points.Count < 2) return;
        Point start = a.Points[0], end = a.Points[1];
        double dx = end.X - start.X, dy = end.Y - start.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length <= 0.5) return;
        double ux = dx / length, uy = dy / length;
        var head = Math.Min(length, a.ArrowHeadLength);
        var halfWidth = head * 0.55;
        var basePoint = new Point(end.X - ux * head, end.Y - uy * head);
        if (length > head)
        {
            // Stop the shaft inside the head so the round cap never pokes through the tip.
            dc.DrawLine(MakePen(brush, a.StrokeWidth), start, new Point(end.X - ux * head * 0.6, end.Y - uy * head * 0.6));
        }
        var triangle = new StreamGeometry();
        using (var ctx = triangle.Open())
        {
            ctx.BeginFigure(end, true, true);
            ctx.LineTo(new Point(basePoint.X - uy * halfWidth, basePoint.Y + ux * halfWidth), true, true);
            ctx.LineTo(new Point(basePoint.X + uy * halfWidth, basePoint.Y - ux * halfWidth), true, true);
        }
        triangle.Freeze();
        dc.DrawGeometry(brush, MakePen(brush, Math.Max(1, a.StrokeWidth * 0.5)), triangle);
    }

    static void DrawText(Annotation a, DrawingContext dc)
    {
        var origin = a.Points.Count > 0 ? a.Points[0] : new Point();
        var pad = TextPadding(a.FontSize);
        var size = TextSize(a.Text, a.FontSize);
        var box = new Rect(origin.X, origin.Y, size.Width + pad.Width * 2, size.Height + pad.Height * 2);
        var ink = a.Color;
        if (a.Filled)
        {
            var radius = Math.Min(box.Height / 4, 10);
            dc.DrawRoundedRectangle(a.Color.Brush(), null, box, radius, radius);
            ink = a.Color.Contrasting;
        }
        if (a.Text.Length == 0) return;
        var formatted = Format(a.Text, Typeface, a.FontSize, ink.Brush());
        dc.DrawText(formatted, new Point(origin.X + pad.Width, origin.Y + pad.Height));
    }

    static void DrawStep(Annotation a, Brush brush, DrawingContext dc)
    {
        var c = a.Points.Count > 0 ? a.Points[0] : new Point();
        var r = a.StepRadius;
        dc.DrawEllipse(brush, null, c, r, r);
        var ring = new Pen(new SolidColorBrush(Color.FromArgb(242, 255, 255, 255)), Math.Max(1.5, r * 0.12));
        ring.Freeze();
        var inner = r - r * 0.06;
        dc.DrawEllipse(null, ring, c, inner, inner);
        var label = a.Number.ToString(CultureInfo.InvariantCulture);
        var formatted = Format(label, BoldTypeface, r * 1.05, a.Color.Contrasting.Brush());
        dc.DrawText(formatted, new Point(c.X - formatted.Width / 2, c.Y - formatted.Height / 2));
    }

    static void DrawPixelate(Annotation a, DrawingContext dc, BitmapSource? background, Size canvasSize)
    {
        var rect = a.Box;
        if (rect.Width < 1 || rect.Height < 1) return;
        if (background is null || canvasSize.Width <= 0 || canvasSize.Height <= 0)
        {
            // Nothing to sample (e.g. while recording live): show a neutral placeholder.
            var placeholder = new SolidColorBrush(Color.FromArgb(217, 128, 128, 128));
            placeholder.Freeze();
            dc.DrawRectangle(placeholder, null, rect);
            return;
        }
        var sx = background.PixelWidth / canvasSize.Width;
        var sy = background.PixelHeight / canvasSize.Height;
        var pixelRect = new Rect(rect.X * sx, rect.Y * sy, rect.Width * sx, rect.Height * sy);
        pixelRect = new Rect(Math.Floor(pixelRect.X), Math.Floor(pixelRect.Y), Math.Ceiling(pixelRect.Right) - Math.Floor(pixelRect.X), Math.Ceiling(pixelRect.Bottom) - Math.Floor(pixelRect.Y));
        pixelRect = pixelRect.Clip(new Rect(0, 0, background.PixelWidth, background.PixelHeight));
        if (pixelRect.Width < 1 || pixelRect.Height < 1) return;
        var block = (4 + a.LineWidth * 2) * sx;
        var tiny = Pixelation.Blocked(background, new Int32Rect((int)pixelRect.X, (int)pixelRect.Y, (int)pixelRect.Width, (int)pixelRect.Height), block);
        if (tiny is null) return;
        dc.DrawImage(tiny, new Rect(pixelRect.X / sx, pixelRect.Y / sy, pixelRect.Width / sx, pixelRect.Height / sy));
    }
}

/// <summary>Block-averages a region of a bitmap into a full-resolution mosaic.</summary>
public static class Pixelation
{
    public static BitmapSource? Blocked(BitmapSource source, Int32Rect region, double blockSize)
    {
        if (region.Width < 1 || region.Height < 1) return null;
        var bgra = ImageExporter.ToBgra32(source);
        var stride = region.Width * 4;
        var pixels = new byte[stride * region.Height];
        bgra.CopyPixels(region, pixels, stride, 0);
        var block = Math.Max(1, (int)Math.Round(blockSize));
        var columns = Math.Max(1, (region.Width + block - 1) / block);
        var rows = Math.Max(1, (region.Height + block - 1) / block);
        var output = new byte[stride * region.Height];
        // Blocks share the region evenly, as when the macOS version shrinks it to columns × rows.
        for (var row = 0; row < rows; row++)
        {
            var y0 = (int)Math.Round(row * (double)region.Height / rows);
            var y1 = (int)Math.Round((row + 1) * (double)region.Height / rows);
            for (var column = 0; column < columns; column++)
            {
                var x0 = (int)Math.Round(column * (double)region.Width / columns);
                var x1 = (int)Math.Round((column + 1) * (double)region.Width / columns);
                long b = 0, g = 0, r = 0, count = 0;
                for (var y = y0; y < y1; y++)
                {
                    var offset = y * stride + x0 * 4;
                    for (var x = x0; x < x1; x++, offset += 4)
                    {
                        b += pixels[offset]; g += pixels[offset + 1]; r += pixels[offset + 2];
                        count++;
                    }
                }
                if (count == 0) continue;
                byte bb = (byte)(b / count), gg = (byte)(g / count), rr = (byte)(r / count);
                for (var y = y0; y < y1; y++)
                {
                    var offset = y * stride + x0 * 4;
                    for (var x = x0; x < x1; x++, offset += 4)
                    {
                        output[offset] = bb; output[offset + 1] = gg; output[offset + 2] = rr; output[offset + 3] = 255;
                    }
                }
            }
        }
        var result = BitmapSource.Create(region.Width, region.Height, 96, 96, PixelFormats.Bgra32, null, output, stride);
        result.Freeze();
        return result;
    }
}
