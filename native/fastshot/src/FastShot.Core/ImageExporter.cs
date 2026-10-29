using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FastShot.Core;

public enum ImageFormat { Png, Jpeg }

public static class ImageFormatExtensions
{
    public static string FileExtension(this ImageFormat format) => format == ImageFormat.Png ? "png" : "jpg";
}

public static class ImageExporter
{
    /// <summary>Burns annotations into the capture at its full pixel resolution.</summary>
    public static BitmapSource Flatten(BitmapSource background, Size canvasSize, IEnumerable<Annotation> annotations)
    {
        int width = background.PixelWidth, height = background.PixelHeight;
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(width / canvasSize.Width, height / canvasSize.Height));
            dc.DrawImage(background, new Rect(0, 0, canvasSize.Width, canvasSize.Height));
            AnnotationRenderer.Draw(annotations, dc, background, canvasSize);
            dc.Pop();
        }
        return Render(visual, width, height);
    }

    /// <summary>Annotations alone on a transparent layer, for compositing over video frames.</summary>
    public static BitmapSource Overlay(int pixelWidth, int pixelHeight, Size canvasSize, IEnumerable<Annotation> annotations)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(pixelWidth / canvasSize.Width, pixelHeight / canvasSize.Height));
            AnnotationRenderer.Draw(annotations, dc, null, canvasSize);
            dc.Pop();
        }
        return Render(visual, pixelWidth, pixelHeight);
    }

    public static BitmapSource Render(Visual visual, int width, int height)
    {
        var target = new RenderTargetBitmap(Math.Max(1, width), Math.Max(1, height), 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        target.Freeze();
        return target;
    }

    /// <summary>Shrinks an image so its longer side is at most <paramref name="maxSide"/> pixels.</summary>
    public static BitmapSource Scaled(BitmapSource image, int maxSide)
    {
        var longest = Math.Max(image.PixelWidth, image.PixelHeight);
        if (longest <= maxSide) return image;
        var factor = (double)maxSide / longest;
        var width = Math.Max(1, (int)(image.PixelWidth * factor));
        var height = Math.Max(1, (int)(image.PixelHeight * factor));
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var dc = visual.RenderOpen()) dc.DrawImage(image, new Rect(0, 0, width, height));
        return Render(visual, width, height);
    }

    /// <summary>Crops in pixel coordinates; the result is a frozen Bgra32 copy.</summary>
    public static BitmapSource Crop(BitmapSource image, Int32Rect rect)
    {
        var bounds = new Int32Rect(0, 0, image.PixelWidth, image.PixelHeight);
        int x = Math.Max(rect.X, 0), y = Math.Max(rect.Y, 0);
        int right = Math.Min(rect.X + rect.Width, bounds.Width), bottom = Math.Min(rect.Y + rect.Height, bounds.Height);
        var clipped = new Int32Rect(x, y, Math.Max(1, right - x), Math.Max(1, bottom - y));
        var source = ToBgra32(image);
        var stride = clipped.Width * 4;
        var pixels = new byte[stride * clipped.Height];
        source.CopyPixels(clipped, pixels, stride, 0);
        var result = BitmapSource.Create(clipped.Width, clipped.Height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        result.Freeze();
        return result;
    }

    public static BitmapSource ToBgra32(BitmapSource image)
    {
        if (image.Format == PixelFormats.Bgra32 || image.Format == PixelFormats.Pbgra32) return image;
        var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        converted.Freeze();
        return converted;
    }

    /// <summary>Encodes an image; <paramref name="pointScale"/> writes matching DPI so apps paste it at its on-screen size.</summary>
    public static byte[] Encode(BitmapSource image, ImageFormat format, double quality = 0.9, double pointScale = 1)
    {
        BitmapEncoder encoder = format == ImageFormat.Png
            ? new PngBitmapEncoder()
            : new JpegBitmapEncoder { QualityLevel = (int)Math.Round(Math.Clamp(quality, 0.05, 1) * 100) };
        var source = format == ImageFormat.Jpeg ? Opaque(image) : image;
        encoder.Frames.Add(BitmapFrame.Create(WithDpi(source, 96 * pointScale)));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    /// <summary>Returns the same pixels tagged with another DPI.</summary>
    public static BitmapSource WithDpi(BitmapSource image, double dpi)
    {
        if (Math.Abs(image.DpiX - dpi) < 0.01 && Math.Abs(image.DpiY - dpi) < 0.01) return image;
        var source = ToBgra32(image);
        var stride = source.PixelWidth * 4;
        var pixels = new byte[stride * source.PixelHeight];
        source.CopyPixels(pixels, stride, 0);
        var result = BitmapSource.Create(source.PixelWidth, source.PixelHeight, dpi, dpi, source.Format, null, pixels, stride);
        result.Freeze();
        return result;
    }

    /// <summary>JPEG has no alpha: composite onto white.</summary>
    static BitmapSource Opaque(BitmapSource image)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, image.PixelWidth, image.PixelHeight));
            dc.DrawImage(image, new Rect(0, 0, image.PixelWidth, image.PixelHeight));
        }
        return Render(visual, image.PixelWidth, image.PixelHeight);
    }

    public static BitmapSource? Load(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count == 0) return null;
            var frame = decoder.Frames[0];
            var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            converted.Freeze();
            // Copy out of the decoder so the file handle can close.
            var stride = converted.PixelWidth * 4;
            var pixels = new byte[stride * converted.PixelHeight];
            converted.CopyPixels(pixels, stride, 0);
            var result = BitmapSource.Create(converted.PixelWidth, converted.PixelHeight, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
            result.Freeze();
            return result;
        }
        catch
        {
            return null;
        }
    }

    public static BitmapSource FromBgra(byte[] pixels, int width, int height, int stride)
    {
        var result = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        result.Freeze();
        return result;
    }

    /// <summary>Points-per-pixel hint read from an image file's DPI (192 dpi ⇒ 2).</summary>
    public static double? PointScale(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            var dpi = decoder.Frames[0].DpiX;
            // Windows writes 96 dpi per 100 % scale, macOS 72 dpi per point; keep quarter steps (1.25, 1.5…).
            return dpi > 100 ? Math.Round(dpi / 96 * 4) / 4 : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Reads one pixel addressed from the image's top-left corner.</summary>
    public static (byte r, byte g, byte b, byte a) Pixel(BitmapSource image, int x, int y)
    {
        var source = ToBgra32(image);
        var pixel = new byte[4];
        source.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return (pixel[2], pixel[1], pixel[0], pixel[3]);
    }
}
