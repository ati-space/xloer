using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace FastShot.Core;

/// <summary>Reads text out of an image with the OCR engine built into Windows.</summary>
public static class Ocr
{
    public static OcrEngine Engine()
    {
        return OcrEngine.TryCreateFromUserProfileLanguages()
               ?? OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("en-US"))
               ?? throw new InvalidOperationException(
                   "Windows'ta metin tanıma için yüklü bir dil bulunamadı. Ayarlar › Saat ve dil › Dil ve bölge bölümünden bir dil paketi ekleyin.");
    }

    /// <summary>The recognised lines, top to bottom. Call from an STA thread (it may enlarge the image with WPF).</summary>
    public static async Task<List<string>> Lines(BitmapSource image)
    {
        var engine = Engine();
        var source = image;
        // Small text reads better when enlarged; very large images must fit the engine's limit.
        var longest = Math.Max(source.PixelWidth, source.PixelHeight);
        var limit = (int)OcrEngine.MaxImageDimension;
        if (longest > limit) source = ImageExporter.Scaled(source, limit);
        else if (longest < 900) source = Enlarged(source, Math.Min(3, limit / (double)longest));

        var bgra = ImageExporter.ToBgra32(source);
        var stride = bgra.PixelWidth * 4;
        var pixels = new byte[stride * bgra.PixelHeight];
        bgra.CopyPixels(pixels, stride, 0);
        using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(pixels.AsBuffer(), BitmapPixelFormat.Bgra8, bgra.PixelWidth, bgra.PixelHeight, BitmapAlphaMode.Premultiplied);
        var result = await engine.RecognizeAsync(bitmap);
        return result.Lines.Select(line => line.Text).ToList();
    }

    public static async Task<string> Recognize(BitmapSource image) => string.Join("\n", await Lines(image)).Trim();

    static BitmapSource Enlarged(BitmapSource image, double factor)
    {
        if (factor <= 1.05) return image;
        var width = (int)(image.PixelWidth * factor);
        var height = (int)(image.PixelHeight * factor);
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var dc = visual.RenderOpen()) dc.DrawImage(image, new Rect(0, 0, width, height));
        return ImageExporter.Render(visual, width, height);
    }
}
