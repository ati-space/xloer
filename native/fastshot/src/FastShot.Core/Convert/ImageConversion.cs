using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FastShot.Core.Convert;

/// <summary>Image conversions through WIC (the codecs Windows has installed).</summary>
public static class ImageConversion
{
    static readonly Guid heif = new("e1e62521-6787-405b-a339-500715b5763f");
    static readonly Guid webp = new("e094b0e2-67f2-45b3-b0ea-115337ca7cf3");

    /// <summary>Decodes the first frame upright (EXIF orientation applied), keeping its DPI.</summary>
    public static BitmapSource LoadOriented(string path, out bool hasAlpha)
    {
        BitmapFrame frame;
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var decoder = BitmapDecoder.Create(file, BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count == 0) throw ConversionException.Unreadable(Path.GetFileName(path));
            frame = decoder.Frames[0];
        }
        catch (ConversionException) { throw; }
        catch { throw ConversionException.Unreadable(Path.GetFileName(path)); }

        hasAlpha = frame.Format.Masks.Count == 4 || frame.Format == PixelFormats.Bgra32 || frame.Format == PixelFormats.Pbgra32
                   || frame.Format == PixelFormats.Rgba64 || frame.Format == PixelFormats.Prgba64 || frame.Format == PixelFormats.Rgba128Float
                   || (frame.Palette?.Colors.Any(c => c.A < 255) ?? false);
        BitmapSource image = frame;
        var orientation = Orientation(frame);
        if (orientation > 1)
        {
            Transform transform = orientation switch
            {
                2 => new ScaleTransform(-1, 1),
                3 => new RotateTransform(180),
                4 => new ScaleTransform(1, -1),
                5 => new TransformGroup { Children = { new RotateTransform(90), new ScaleTransform(-1, 1) } },
                6 => new RotateTransform(90),
                7 => new TransformGroup { Children = { new RotateTransform(270), new ScaleTransform(-1, 1) } },
                8 => new RotateTransform(270),
                _ => Transform.Identity,
            };
            image = new TransformedBitmap(frame, transform);
        }
        if (image.CanFreeze) image.Freeze();
        return image;
    }

    static int Orientation(BitmapFrame frame)
    {
        try
        {
            if (frame.Metadata is BitmapMetadata metadata && metadata.GetQuery("System.Photo.Orientation") is ushort value) return value;
        }
        catch { /* formats without metadata support */ }
        return 1;
    }

    static double Dpi(BitmapSource image) => image.DpiX is > 1 and < 10000 ? image.DpiX : 96;

    /// <summary>Puts transparent pixels on white, for formats that cannot hold alpha.</summary>
    public static BitmapSource FlattenOnWhite(BitmapSource image)
    {
        var width = image.PixelWidth;
        var height = image.PixelHeight;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
            dc.DrawImage(image, new Rect(0, 0, width, height));
        }
        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        var opaque = new FormatConvertedBitmap(target, PixelFormats.Bgr24, null, 0);
        return Retag(opaque, Dpi(image));
    }

    /// <summary>The same pixels with another DPI, frozen.</summary>
    static BitmapSource Retag(BitmapSource image, double dpi)
    {
        var stride = (image.PixelWidth * image.Format.BitsPerPixel + 7) / 8;
        var pixels = new byte[stride * image.PixelHeight];
        image.CopyPixels(pixels, stride, 0);
        var result = BitmapSource.Create(image.PixelWidth, image.PixelHeight, dpi, dpi, image.Format, image.Palette, pixels, stride);
        result.Freeze();
        return result;
    }

    public static bool CanEncode(string codec)
    {
        try
        {
            return FileConverter.OnSta(() =>
            {
                var pixels = new byte[16 * 16 * 4];
                var image = BitmapSource.Create(16, 16, 96, 96, PixelFormats.Bgra32, null, pixels, 64);
                return Encode(image, codec, 0.9).Length > 0;
            });
        }
        catch { return false; }
    }

    public static byte[] Encode(BitmapSource image, string codec, double quality, bool hasAlpha = true)
    {
        BitmapEncoder encoder;
        var source = image;
        switch (codec)
        {
            case "png":
                encoder = new PngBitmapEncoder();
                break;
            case "jpeg":
                encoder = new JpegBitmapEncoder { QualityLevel = (int)Math.Round(Math.Clamp(quality, 0.05, 1) * 100) };
                if (hasAlpha) source = FlattenOnWhite(image);
                break;
            case "bmp":
                encoder = new BmpBitmapEncoder();
                if (hasAlpha) source = FlattenOnWhite(image);
                break;
            case "tiff":
                encoder = new TiffBitmapEncoder { Compression = TiffCompressOption.Lzw };
                break;
            case "gif":
            {
                encoder = new GifBitmapEncoder();
                // An optimal 256-colour palette from the image itself, not the default web palette.
                var flat = hasAlpha ? FlattenOnWhite(image) : image;
                var palette = new BitmapPalette(flat, 256);
                source = new FormatConvertedBitmap(flat, PixelFormats.Indexed8, palette, 0);
                break;
            }
            case "heif":
                encoder = BitmapEncoder.Create(heif);
                break;
            case "webp":
                encoder = BitmapEncoder.Create(webp);
                break;
            default:
                throw ConversionException.Unsupported(codec);
        }
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    public static void Convert(string input, string codec, string output, double quality)
    {
        var name = Path.GetFileName(input);
        if (codec == "avif" || (codec == "webp" && !Capabilities.NativeWebp))
        {
            ConvertWithFfmpeg(input, codec, output, quality);
            return;
        }
        var image = LoadOriented(input, out var alpha);
        byte[] data;
        try { data = Encode(image, codec, quality, alpha); }
        catch (ConversionException) { throw; }
        catch { throw ConversionException.Unsupported(name); }
        File.WriteAllBytes(output, data);
    }

    static void ConvertWithFfmpeg(string input, string codec, string output, double quality)
    {
        var tool = ExternalTools.Ffmpeg ?? throw ConversionException.Unsupported(Path.GetFileName(input));
        var temporary = Path.Combine(Path.GetTempPath(), $"fastshot-{Guid.NewGuid():N}.png");
        try
        {
            File.WriteAllBytes(temporary, Encode(LoadOriented(input, out _), "png", 1));
            var q = ((int)Math.Round(quality * 100)).ToString();
            var arguments = codec == "webp"
                ? new[] { "-y", "-loglevel", "error", "-i", temporary, "-quality", q, output }
                : ["-y", "-loglevel", "error", "-i", temporary, "-c:v", "libaom-av1", "-still-picture", "1", "-crf", ((int)Math.Round((1 - quality) * 50 + 15)).ToString(), output];
            ExternalTools.Run(tool, arguments, Path.GetFileName(input));
        }
        finally { FileConverter.TryDelete(temporary); }
    }

    static readonly HashSet<string> photographic = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".jpe", ".jfif", ".heic", ".heif", ".hif", ".avif", ".webp", ".dng" };

    /// <summary>One page per image, sized from the image's DPI. JPEGs go in untouched; photos are stored as JPEG, everything else losslessly.</summary>
    public static void ImagesToPdf(IReadOnlyList<string> paths, string output, double quality, CancellationToken cancellation = default)
    {
        using (var pdf = new PdfWriter(output))
        {
            foreach (var path in paths)
            {
                cancellation.ThrowIfCancellationRequested();
                BitmapSource image;
                bool alpha;
                try { image = LoadOriented(path, out alpha); }
                catch { continue; }
                var dpi = Dpi(image);
                var width = image.PixelWidth * 72 / dpi;
                var height = image.PixelHeight * 72 / dpi;
                var extension = Path.GetExtension(path);
                var isJpeg = FileConverter.Normalized(extension) == "jpg";
                if (isJpeg && image is BitmapFrame frame && (frame.Format == PixelFormats.Bgr24 || frame.Format == PixelFormats.Bgr32 || frame.Format == PixelFormats.Gray8))
                {
                    pdf.AddJpegPage(File.ReadAllBytes(path), image.PixelWidth, image.PixelHeight, frame.Format == PixelFormats.Gray8, width, height);
                }
                else if (photographic.Contains(extension))
                {
                    var jpeg = Encode(image, "jpeg", Math.Max(quality, 0.85), alpha);
                    pdf.AddJpegPage(jpeg, image.PixelWidth, image.PixelHeight, false, width, height);
                }
                else
                {
                    var flat = alpha ? FlattenOnWhite(image) : image;
                    var rgb = new FormatConvertedBitmap(flat, PixelFormats.Rgb24, null, 0);
                    var stride = rgb.PixelWidth * 3;
                    var pixels = new byte[stride * rgb.PixelHeight];
                    rgb.CopyPixels(pixels, stride, 0);
                    pdf.AddRgbPage(pixels, rgb.PixelWidth, rgb.PixelHeight, width, height);
                }
            }
            if (pdf.PageCount == 0)
            {
                pdf.Dispose();
                FileConverter.TryDelete(output);
                throw ConversionException.Unreadable(paths.Count > 0 ? Path.GetFileName(paths[0]) : "");
            }
        }
    }
}
