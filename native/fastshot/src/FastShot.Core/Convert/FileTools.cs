using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using FastShot.Core.Media;

namespace FastShot.Core.Convert;

/// <summary>Changes a file's content without changing its kind.</summary>
public enum FileTool { Edit, Crop, Background, Compress, Metadata, RecognizeText, RemoveAudio, MergePdf }

/// <summary>Does something with files and folders as they are.</summary>
public enum FileAction { Zip, Unzip, Share, CopyToClipboard, CopyPath, Pin, Terminal }

/// <summary>The work of the Tools and Actions wheels. Results are written as new files next to the originals
/// (or in the chosen folder); nothing is changed in place.</summary>
public static class FileTools
{
    // MARK: What applies

    public static List<FileTool> Tools(IReadOnlyCollection<string> paths)
    {
        if (paths.Count == 0) return [];
        var families = paths.Select(p => Directory.Exists(p) ? null : FileConverter.Family(p)).ToList();
        if (families.Any(f => f is null) || families.Distinct().Count() != 1) return [];
        var single = paths.Count == 1;
        switch (families[0]!.Value)
        {
            case FileFamily.Image:
                return single
                    ? [FileTool.Edit, FileTool.Crop, FileTool.Background, FileTool.Compress, FileTool.Metadata, FileTool.RecognizeText]
                    : [FileTool.Edit, FileTool.Background, FileTool.Compress, FileTool.Metadata];
            case FileFamily.Video:
                return [.. single ? [FileTool.Edit] : Array.Empty<FileTool>(), FileTool.Compress, FileTool.RemoveAudio, FileTool.Metadata];
            case FileFamily.Audio:
            {
                // Only MPEG-4 audio can be rewritten without re-encoding.
                var rewritable = paths.All(p => Path.GetExtension(p).ToLowerInvariant() is ".m4a" or ".mp4" or ".m4b" or ".mov");
                return rewritable ? [FileTool.Compress, FileTool.Metadata] : [FileTool.Compress];
            }
            case FileFamily.Pdf:
                return single ? [FileTool.Compress, FileTool.Metadata] : [FileTool.Compress, FileTool.Metadata, FileTool.MergePdf];
            default:
                return [];
        }
    }

    public static List<FileAction> Actions(IReadOnlyCollection<string> paths)
    {
        if (paths.Count == 0) return [];
        var allZip = paths.All(p => !Directory.Exists(p) && Path.GetExtension(p).Equals(".zip", StringComparison.OrdinalIgnoreCase));
        var allImages = paths.Count <= 6 && paths.All(p => !Directory.Exists(p) && FileConverter.Family(p) == FileFamily.Image);
        var list = new List<FileAction> { allZip ? FileAction.Unzip : FileAction.Zip, FileAction.Share, FileAction.CopyToClipboard, FileAction.CopyPath };
        if (allImages) list.Add(FileAction.Pin);
        list.Add(FileAction.Terminal);
        return list;
    }

    /// <summary>"name (suffix).ext" in <paramref name="folder"/>, numbered if taken.</summary>
    public static string Output(string path, string folder, string suffix, string? extension = null) =>
        FileConverter.UniquePath(folder, $"{Path.GetFileNameWithoutExtension(path)} ({suffix})",
                                 extension ?? Path.GetExtension(path).TrimStart('.').ToLowerInvariant());

    /// <summary>The WIC codec for writing an image back in its own format, PNG when Windows cannot write it.</summary>
    static (string codec, string extension) WritableType(string path)
    {
        var extension = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        return FileConverter.Normalized(extension) switch
        {
            "png" => ("png", "png"),
            "jpg" => ("jpeg", extension),
            "tiff" => ("tiff", extension),
            "gif" => ("gif", "gif"),
            "bmp" => ("bmp", "bmp"),
            "heic" when Capabilities.HeifEncoder => ("heif", extension),
            _ => ("png", "png"),
        };
    }

    // MARK: Compress

    public static string Compress(string path, string folder, Action<double>? progress = null, CancellationToken cancellation = default)
    {
        progress ??= _ => { };
        switch (FileConverter.Family(path))
        {
            case FileFamily.Image:
                return CompressImage(path, folder);
            case FileFamily.Video:
            {
                var output = Output(path, folder, "küçük", "mp4");
                var probe = VideoExporter.Probe(path);
                var request = new VideoExportRequest
                {
                    Source = path, CanvasSize = new Size(Math.Max(1, probe.width), Math.Max(1, probe.height)), ForceReencode = true,
                    PreferHEVC = Capabilities.HevcEncoder, MaxSize = (1920, 1080), BitrateScale = Capabilities.HevcEncoder ? 0.4 : 0.55,
                };
                VideoExporter.Export(request, output, VideoFormat.Mp4, progress, cancellation);
                return output;
            }
            case FileFamily.Audio:
            {
                var output = Output(path, folder, "küçük", "m4a");
                MediaConversion.ConvertAudio(path, "m4a", output, progress, cancellation, bytesPerSecond: 16000);
                return output;
            }
            case FileFamily.Pdf:
            {
                var output = Output(path, folder, "küçük", "pdf");
                PdfTools.Compress(path, output);
                return output;
            }
            default:
                throw ConversionException.Unsupported(Path.GetFileName(path));
        }
    }

    /// <summary>Lossy formats are re-encoded at a lower quality; lossless ones become JPEG, or HEIC (else a 256-colour PNG) when they have transparency.</summary>
    public static string CompressImage(string path, string folder)
    {
        var image = ImageConversion.LoadOriented(path, out var alpha);
        if (Math.Max(image.PixelWidth, image.PixelHeight) > 3840) image = Retagged(ImageExporter.Scaled(image, 3840), image.DpiX);
        var extension = FileConverter.Normalized(Path.GetExtension(path));
        (string codec, string ext, double quality) choice = extension switch
        {
            "jpg" => ("jpeg", "jpg", 0.68),
            "heic" when Capabilities.HeifEncoder => ("heif", "heic", 0.6),
            _ when alpha && Capabilities.HeifEncoder => ("heif", "heic", 0.65),
            _ when alpha => ("png8", "png", 1),
            _ => ("jpeg", "jpg", 0.75),
        };
        var output = Output(path, folder, "küçük", choice.ext);
        byte[] data;
        if (choice.codec == "png8")
        {
            var palette = new BitmapPalette(image, 256);
            var indexed = new FormatConvertedBitmap(image, PixelFormats.Indexed8, palette, 0);
            data = ImageConversion.Encode(indexed, "png", 1);
        }
        else
        {
            data = ImageConversion.Encode(image, choice.codec, choice.quality, alpha);
        }
        File.WriteAllBytes(output, data);
        return output;
    }

    /// <summary>The pixels at <paramref name="dpi"/>; <paramref name="detach"/> always copies them into a new bitmap with no metadata.</summary>
    static BitmapSource Retagged(BitmapSource image, double dpi, bool detach = false)
    {
        if (!detach && Math.Abs(image.DpiX - dpi) < 0.5) return image;
        var source = ImageExporter.ToBgra32(image);
        var stride = source.PixelWidth * 4;
        var pixels = new byte[stride * source.PixelHeight];
        source.CopyPixels(pixels, stride, 0);
        var result = BitmapSource.Create(source.PixelWidth, source.PixelHeight, dpi, dpi, source.Format, null, pixels, stride);
        result.Freeze();
        return result;
    }

    // MARK: Metadata

    /// <summary>Writes a copy without location, camera and author details. Image pixels keep their orientation.</summary>
    public static string StripMetadata(string path, string folder, Action<double>? progress = null, CancellationToken cancellation = default)
    {
        progress ??= _ => { };
        switch (FileConverter.Family(path))
        {
            case FileFamily.Image:
            {
                var image = ImageConversion.LoadOriented(path, out var alpha);
                var (codec, extension) = WritableType(path);
                var output = Output(path, folder, "temiz", extension);
                // A new frame from the pixels alone carries no EXIF, GPS, XMP or comments.
                File.WriteAllBytes(output, ImageConversion.Encode(Retagged(image, image.DpiX, detach: true), codec, 0.95, alpha));
                return output;
            }
            case FileFamily.Video or FileFamily.Audio:
            {
                // Copying the tracks into a fresh MP4 keeps the picture and sound bit for bit and leaves the metadata behind.
                var video = FileConverter.Family(path) == FileFamily.Video;
                var output = Output(path, folder, "temiz", video ? "mp4" : "m4a");
                Remuxer.ToMp4(path, output, progress, cancellation, keepVideo: video);
                return output;
            }
            case FileFamily.Pdf:
            {
                var output = Output(path, folder, "temiz", "pdf");
                PdfTools.Clean(path, output);
                return output;
            }
            default:
                throw ConversionException.Unsupported(Path.GetFileName(path));
        }
    }

    // MARK: Images

    /// <summary>Crops to <paramref name="rect"/>, given in pixels of the upright image from its top-left corner.</summary>
    public static string Crop(string path, Int32Rect rect, string folder)
    {
        var image = ImageConversion.LoadOriented(path, out var alpha);
        var x = Math.Clamp(rect.X, 0, image.PixelWidth - 1);
        var y = Math.Clamp(rect.Y, 0, image.PixelHeight - 1);
        var width = Math.Min(rect.Width, image.PixelWidth - x);
        var height = Math.Min(rect.Height, image.PixelHeight - y);
        if (width < 1 || height < 1) throw ConversionException.Unsupported(Path.GetFileName(path));
        var cropped = Retagged(ImageExporter.Crop(image, new Int32Rect(x, y, width, height)), image.DpiX);
        var (codec, extension) = WritableType(path);
        var output = Output(path, folder, "kırpılmış", extension);
        File.WriteAllBytes(output, ImageConversion.Encode(cropped, codec, 0.92, alpha));
        return output;
    }

    /// <summary>Puts the image on a soft gradient with padding, rounded corners and a shadow, ready to share.</summary>
    public static string AddBackground(string path, string folder)
    {
        var image = ImageConversion.LoadOriented(path, out _);
        double w = image.PixelWidth, h = image.PixelHeight;
        var padding = Math.Max(48, Math.Round(Math.Max(w, h) * 0.08));
        var radius = Math.Max(8, Math.Min(w, h) * 0.02);
        var gradient = new LinearGradientBrush(
            [new GradientStop(Color.FromRgb(0x6B, 0x5C, 0xFA), 0), new GradientStop(Color.FromRgb(0x29, 0x9E, 0xFF), 0.55), new GradientStop(Color.FromRgb(0x5C, 0xDB, 0xDB), 1)],
            new Point(0, 0), new Point(1, 1));
        var picture = new Border
        {
            Width = w, Height = h, CornerRadius = new CornerRadius(radius), Background = Brushes.White,
            Effect = new DropShadowEffect { BlurRadius = padding * 0.55, ShadowDepth = padding * 0.18, Direction = 270, Opacity = 0.35, Color = Colors.Black, RenderingBias = RenderingBias.Quality },
            Child = new Border
            {
                CornerRadius = new CornerRadius(radius),
                Background = new ImageBrush(image) { Stretch = Stretch.Fill },
            },
            Margin = new Thickness(padding),
        };
        var canvas = new Border { Background = gradient, Child = picture, Width = w + padding * 2, Height = h + padding * 2 };
        RenderOptions.SetBitmapScalingMode(canvas, BitmapScalingMode.HighQuality);
        canvas.Measure(new Size(canvas.Width, canvas.Height));
        canvas.Arrange(new Rect(0, 0, canvas.Width, canvas.Height));
        canvas.UpdateLayout();
        var target = new RenderTargetBitmap((int)canvas.Width, (int)canvas.Height, 96, 96, PixelFormats.Pbgra32);
        target.Render(canvas);
        var output = Output(path, folder, "arka plan", "png");
        File.WriteAllBytes(output, ImageConversion.Encode(Retagged(target, image.DpiX), "png", 1));
        return output;
    }

    // MARK: Video and PDF

    public static string RemoveAudio(string path, string folder, Action<double>? progress = null, CancellationToken cancellation = default)
    {
        progress ??= _ => { };
        var output = Output(path, folder, "sessiz", "mp4");
        try
        {
            Remuxer.ToMp4(path, output, progress, cancellation, keepAudio: false);
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            // Codecs MP4 cannot carry as they are: re-encode the picture instead.
            FileConverter.TryDelete(output);
            var probe = VideoExporter.Probe(path);
            VideoExporter.Export(new VideoExportRequest
            {
                Source = path, CanvasSize = new Size(Math.Max(1, probe.width), Math.Max(1, probe.height)), ForceReencode = true, IncludeAudio = false,
            }, output, VideoFormat.Mp4, progress, cancellation);
        }
        return output;
    }

    public static string MergePdfs(IReadOnlyList<string> paths, string folder)
    {
        if (paths.Count == 0) throw ConversionException.Unsupported("");
        var output = Output(paths[0], folder, "birleşik", "pdf");
        PdfTools.Merge(paths, output);
        return output;
    }

    // MARK: Archives

    /// <summary>One item keeps its name (Rapor.zip, a folder stays a folder inside); several become Arşiv.zip with the items at the top.</summary>
    public static string Zip(IReadOnlyList<string> paths, string folder)
    {
        if (paths.Count == 0) throw ConversionException.Unsupported("");
        var first = paths[0].TrimEnd('\\', '/');
        var name = paths.Count == 1 ? (Directory.Exists(first) ? Path.GetFileName(first) : Path.GetFileNameWithoutExtension(first)) : "Arşiv";
        var output = FileConverter.UniquePath(folder, name, "zip");
        var temporary = output + ".part";
        try
        {
            using (var file = new FileStream(temporary, FileMode.Create))
            using (var zip = new ZipArchive(file, ZipArchiveMode.Create, false, System.Text.Encoding.UTF8))
            {
                var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var raw in paths)
                {
                    var path = raw.TrimEnd('\\', '/');
                    var entry = Unique(used, Path.GetFileName(path));
                    if (Directory.Exists(path)) AddFolder(zip, path, entry);
                    else zip.CreateEntryFromFile(path, entry, CompressionLevel.Optimal);
                }
            }
            File.Move(temporary, output);
        }
        catch
        {
            FileConverter.TryDelete(temporary);
            throw;
        }
        return output;
    }

    static string Unique(HashSet<string> used, string name)
    {
        var candidate = name;
        for (var counter = 2; !used.Add(candidate); counter++)
            candidate = $"{Path.GetFileNameWithoutExtension(name)} {counter}{Path.GetExtension(name)}";
        return candidate;
    }

    static void AddFolder(ZipArchive zip, string folder, string root)
    {
        var files = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).ToList();
        if (files.Count == 0) zip.CreateEntry(root + "/");
        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(folder, file).Replace('\\', '/');
            zip.CreateEntryFromFile(file, $"{root}/{relative}", CompressionLevel.Optimal);
        }
        foreach (var directory in Directory.EnumerateDirectories(folder, "*", SearchOption.AllDirectories))
        {
            if (Directory.EnumerateFileSystemEntries(directory).Any()) continue;
            zip.CreateEntry($"{root}/{Path.GetRelativePath(folder, directory).Replace('\\', '/')}/");
        }
    }

    public static string Unzip(string path, string folder)
    {
        var output = FileConverter.UniqueFolder(folder, Path.GetFileNameWithoutExtension(path));
        Directory.CreateDirectory(output);
        try
        {
            // ExtractToDirectory refuses entries that would land outside the folder.
            ZipFile.ExtractToDirectory(path, output, System.Text.Encoding.UTF8, overwriteFiles: false);
        }
        catch (InvalidDataException)
        {
            try { Directory.Delete(output, true); } catch { /* leave it */ }
            throw ConversionException.Unreadable(Path.GetFileName(path));
        }
        return output;
    }
}
