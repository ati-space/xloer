namespace FastShot;

/// <summary>The FastShot icon (a viewfinder with a lightning bolt and a record dot), drawn with WPF at any size:
/// the same artwork as the macOS icon. Also writes the .ico used for the executable.</summary>
public static class AppIcon
{
    /// <summary>Draws on a 1024-unit square, scaled to <paramref name="size"/>.</summary>
    public static void Draw(DrawingContext dc, double size, bool recording = false)
    {
        dc.PushTransform(new ScaleTransform(size / 1024, size / 1024));
        var tile = new Rect(100, 100, 824, 824);
        var tileGeometry = new RectangleGeometry(tile, 185, 185);
        if (recording)
        {
            dc.DrawGeometry(Theme.Frozen(Color.FromRgb(0xE8, 0x2F, 0x34)), null, new RectangleGeometry(tile, 412, 412));
            dc.DrawRoundedRectangle(Brushes.White, null, new Rect(352, 352, 320, 320), 48, 48);
            dc.Pop();
            return;
        }
        var gradient = new LinearGradientBrush(
            [new GradientStop(Color.FromRgb(0x54, 0x33, 0xED), 0), new GradientStop(Color.FromRgb(0x17, 0x78, 0xFF), 0.55), new GradientStop(Color.FromRgb(0x0D, 0xC7, 0xF2), 1)],
            new Point(0, 1), new Point(1, 0));
        dc.DrawGeometry(gradient, null, tileGeometry);
        var shine = new LinearGradientBrush(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF), Color.FromArgb(0, 0xFF, 0xFF, 0xFF), new Point(0, 0), new Point(0, 0.5));
        dc.DrawGeometry(shine, null, tileGeometry);

        // Viewfinder corners.
        var corner = new Pen(Brushes.White, 46) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        var frame = new Rect(250, 250, 524, 524);
        const double arm = 130;
        foreach (var (x, y, dx, dy) in new[] { (frame.Left, frame.Top, 1.0, 1.0), (frame.Right, frame.Top, -1.0, 1.0), (frame.Left, frame.Bottom, 1.0, -1.0), (frame.Right, frame.Bottom, -1.0, -1.0) })
        {
            var path = new StreamGeometry();
            using (var ctx = path.Open())
            {
                ctx.BeginFigure(new Point(x, y + dy * arm), false, false);
                ctx.LineTo(new Point(x, y), true, true);
                ctx.LineTo(new Point(x + dx * arm, y), true, true);
            }
            dc.DrawGeometry(null, corner, path);
        }

        // Lightning bolt for "fast".
        var bolt = new StreamGeometry();
        using (var ctx = bolt.Open())
        {
            ctx.BeginFigure(new Point(560, 334), true, true);
            foreach (var point in new[] { new Point(395, 534), new Point(498, 534), new Point(455, 694), new Point(630, 479), new Point(522, 479) })
                ctx.LineTo(point, true, true);
        }
        dc.DrawGeometry(Theme.Frozen(Color.FromRgb(0xFF, 0xD6, 0x1A)), new Pen(Theme.Frozen(Color.FromRgb(0xFF, 0xF2, 0xBF)), 18) { LineJoin = PenLineJoin.Round }, bolt);

        // Red record dot.
        dc.DrawEllipse(Theme.Frozen(Color.FromRgb(0xFA, 0x38, 0x40)), new Pen(Brushes.White, 14), new Point(666, 378), 48, 48);
        dc.Pop();
    }

    public static BitmapSource Render(int pixels, bool recording = false)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            // Small sizes use more of the square; the macOS tile margin would waste a third of a 16 px tray icon.
            var zoom = pixels <= 32 ? 1.22 : 1.0;
            dc.PushTransform(new ScaleTransform(zoom, zoom, pixels / 2.0, pixels / 2.0));
            Draw(dc, pixels, recording);
            dc.Pop();
        }
        return ImageExporter.Render(visual, pixels, pixels);
    }

    static byte[] Ico(IEnumerable<int> sizes, bool recording)
    {
        var images = sizes.Select(s => (size: s, png: ImageExporter.Encode(Render(s, recording), ImageFormat.Png))).ToList();
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((short)0);
        writer.Write((short)1);
        writer.Write((short)images.Count);
        var offset = 6 + 16 * images.Count;
        foreach (var (size, png) in images)
        {
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((short)1);
            writer.Write((short)32);
            writer.Write(png.Length);
            writer.Write(offset);
            offset += png.Length;
        }
        foreach (var (_, png) in images) writer.Write(png);
        writer.Flush();
        return stream.ToArray();
    }

    public static void WriteIco(string path) => File.WriteAllBytes(path, Ico([16, 20, 24, 32, 40, 48, 64, 128, 256], false));

    /// <summary>A tray icon at the DPI of the primary monitor.</summary>
    public static System.Drawing.Icon TrayIcon(bool recording)
    {
        var scale = MonitorInfo.All().FirstOrDefault(m => m.Primary)?.Scale ?? 1;
        var size = (int)Math.Round(16 * scale);
        using var stream = new MemoryStream(Ico([size], recording));
        return new System.Drawing.Icon(stream);
    }

    public static ImageSource WindowIcon { get; } = Render(64);
}
