using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FastShot.Core.Convert;

namespace FastShot.Core;

/// <summary>An ordered, bounded set of full-resolution captures. Exports never modify source images.</summary>
public sealed class CaptureSequence
{
    public const long MaxBytes = 256L * 1024 * 1024;
    readonly List<BitmapSource> images = [];
    public IReadOnlyList<BitmapSource> Images => images.AsReadOnly();
    public long Bytes { get; private set; }

    public void Add(BitmapSource image)
    {
        var bytes = checked((long)image.PixelWidth * image.PixelHeight * 4);
        if (images.Count >= 100 || Bytes + bytes > MaxBytes)
            throw new InvalidOperationException("Yakalama kuyruğu doldu. Önce dışa aktarın veya birkaç görüntüyü kaldırın.");
        var copy = ImageExporter.ToBgra32(image);
        if (!copy.IsFrozen) { copy = copy.Clone(); copy.Freeze(); }
        images.Add(copy);
        Bytes += bytes;
    }

    public void RemoveAt(int index)
    {
        var image = images[index];
        Bytes -= (long)image.PixelWidth * image.PixelHeight * 4;
        images.RemoveAt(index);
    }

    public void Move(int from, int to)
    {
        if (from < 0 || to < 0 || from >= images.Count || to >= images.Count) throw new ArgumentOutOfRangeException(nameof(to));
        var image = images[from];
        images.RemoveAt(from);
        images.Insert(to, image);
    }

    public BitmapSource Combine()
    {
        RequireImages();
        var width = images.Max(i => i.PixelWidth);
        var height = checked(images.Sum(i => i.PixelHeight));
        if ((long)width * height * 4 > MaxBytes || height > 32767 || width > 32767)
            throw new InvalidOperationException("Birleşik görüntü çok büyük. PDF olarak dışa aktarın veya görüntü sayısını azaltın.");
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
            var y = 0;
            foreach (var image in images)
            {
                dc.DrawImage(image, new Rect(0, y, image.PixelWidth, image.PixelHeight));
                y += image.PixelHeight;
            }
        }
        return ImageExporter.Render(visual, width, height);
    }

    public void Export(string path, bool pdf)
    {
        RequireImages();
        var full = Path.GetFullPath(path);
        var temp = Path.Combine(Path.GetDirectoryName(full)!, $".{Guid.NewGuid():N}.tmp");
        try
        {
            if (pdf)
            {
                using var writer = new PdfWriter(temp);
                foreach (var image in images)
                {
                    var bgra = new byte[image.PixelWidth * image.PixelHeight * 4];
                    image.CopyPixels(bgra, image.PixelWidth * 4, 0);
                    var rgb = new byte[image.PixelWidth * image.PixelHeight * 3];
                    for (int s = 0, d = 0; s < bgra.Length; s += 4, d += 3)
                    {
                        var alpha = bgra[s + 3];
                        rgb[d] = (byte)((bgra[s + 2] * alpha + 255 * (255 - alpha) + 127) / 255);
                        rgb[d + 1] = (byte)((bgra[s + 1] * alpha + 255 * (255 - alpha) + 127) / 255);
                        rgb[d + 2] = (byte)((bgra[s] * alpha + 255 * (255 - alpha) + 127) / 255);
                    }
                    // Fit each capture on an A4 page without changing its aspect ratio.
                    var scale = Math.Min(595.28 / image.PixelWidth, 841.89 / image.PixelHeight);
                    writer.AddRgbPage(rgb, image.PixelWidth, image.PixelHeight, image.PixelWidth * scale, image.PixelHeight * scale);
                }
                writer.Finish();
            }
            else File.WriteAllBytes(temp, ImageExporter.Encode(Combine(), ImageFormat.Png));
            File.Move(temp, full, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    void RequireImages()
    {
        if (images.Count == 0) throw new InvalidOperationException("Önce kuyruğa bir alan ekleyin.");
    }
}
