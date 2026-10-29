namespace FastShot;

/// <summary>The FastShot icon: the logo in Assets/AppIcon.png (a white pointer in a viewfinder on a dark tile) at any size,
/// and a red tile with a stop square while recording. Also writes the .ico used for the executable.</summary>
public static class AppIcon
{
    static readonly Lazy<BitmapSource> logo = new(() =>
    {
        using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream("FastShot.AppIcon.png")
                           ?? throw new InvalidOperationException("AppIcon.png is not embedded.");
        var frame = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        frame.Freeze();
        return frame;
    });

    /// <summary>Draws the icon on a square of <paramref name="size"/>.</summary>
    public static void Draw(DrawingContext dc, double size, bool recording = false)
    {
        if (!recording)
        {
            dc.DrawImage(logo.Value, new Rect(0, 0, size, size));
            return;
        }
        dc.PushTransform(new ScaleTransform(size / 1024, size / 1024));
        var tile = new Rect(100, 100, 824, 824);
        dc.DrawGeometry(Theme.Frozen(Color.FromRgb(0xE8, 0x2F, 0x34)), null, new RectangleGeometry(tile, 412, 412));
        dc.DrawRoundedRectangle(Brushes.White, null, new Rect(352, 352, 320, 320), 48, 48);
        dc.Pop();
    }

    public static BitmapSource Render(int pixels, bool recording = false)
    {
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var dc = visual.RenderOpen())
        {
            // The recording tile has a margin that would waste a third of a 16 px tray icon; the logo fills its square.
            var zoom = recording && pixels <= 32 ? 1.22 : 1.0;
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
