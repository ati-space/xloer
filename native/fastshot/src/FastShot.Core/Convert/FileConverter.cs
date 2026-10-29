using System.IO;
using System.Runtime.ExceptionServices;

namespace FastShot.Core.Convert;

/// <summary>Converts files between formats with Windows' own engines: WIC for images, Media Foundation
/// for video and audio, Windows.Data.Pdf and OCR for PDFs, WPF's text engine for documents. Word (when
/// installed) handles DOC and the most faithful document PDFs, Edge prints the rest; ffmpeg, when it is
/// on the PATH, adds WebP, AVIF and any audio format Windows cannot write.</summary>
public static class FileConverter
{
    static readonly Dictionary<string, FileFamily> families = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = FileFamily.Image, [".jpg"] = FileFamily.Image, [".jpeg"] = FileFamily.Image, [".jpe"] = FileFamily.Image, [".jfif"] = FileFamily.Image,
        [".bmp"] = FileFamily.Image, [".dib"] = FileFamily.Image, [".gif"] = FileFamily.Image, [".tif"] = FileFamily.Image, [".tiff"] = FileFamily.Image,
        [".ico"] = FileFamily.Image, [".heic"] = FileFamily.Image, [".heif"] = FileFamily.Image, [".hif"] = FileFamily.Image, [".avif"] = FileFamily.Image,
        [".webp"] = FileFamily.Image, [".jxr"] = FileFamily.Image, [".wdp"] = FileFamily.Image, [".dng"] = FileFamily.Image,
        [".mp4"] = FileFamily.Video, [".m4v"] = FileFamily.Video, [".mov"] = FileFamily.Video, [".mkv"] = FileFamily.Video, [".avi"] = FileFamily.Video,
        [".wmv"] = FileFamily.Video, [".webm"] = FileFamily.Video, [".3gp"] = FileFamily.Video, [".mpg"] = FileFamily.Video, [".mpeg"] = FileFamily.Video,
        [".ts"] = FileFamily.Video, [".mts"] = FileFamily.Video, [".m2ts"] = FileFamily.Video,
        [".mp3"] = FileFamily.Audio, [".m4a"] = FileFamily.Audio, [".aac"] = FileFamily.Audio, [".wav"] = FileFamily.Audio, [".wma"] = FileFamily.Audio,
        [".flac"] = FileFamily.Audio, [".aiff"] = FileFamily.Audio, [".aif"] = FileFamily.Audio, [".aifc"] = FileFamily.Audio, [".caf"] = FileFamily.Audio,
        [".ogg"] = FileFamily.Audio, [".opus"] = FileFamily.Audio, [".amr"] = FileFamily.Audio,
        [".pdf"] = FileFamily.Pdf,
        [".docx"] = FileFamily.Document, [".doc"] = FileFamily.Document, [".odt"] = FileFamily.Document, [".rtf"] = FileFamily.Document,
        [".txt"] = FileFamily.Document, [".text"] = FileFamily.Document, [".html"] = FileFamily.Document, [".htm"] = FileFamily.Document,
        [".md"] = FileFamily.Document, [".markdown"] = FileFamily.Document,
    };

    /// <summary>Every extension FastShot can read, for the Explorer right-click menu.</summary>
    public static IEnumerable<string> SupportedExtensions => families.Keys;

    public static FileFamily? Family(string path) => families.TryGetValue(Path.GetExtension(path), out var family) ? family : null;

    static ConversionFormat F(string id, string title, string ext, string? detail, ConversionKind kind, string codec) => new(id, title, ext, detail, kind, codec);

    public static List<ConversionFormat> Targets(FileFamily family)
    {
        switch (family)
        {
            case FileFamily.Image:
            {
                var list = new List<ConversionFormat>
                {
                    F("png", "PNG", "png", "Kayıpsız", ConversionKind.Image, "png"),
                    F("jpeg", "JPEG", "jpg", null, ConversionKind.Image, "jpeg"),
                };
                if (Capabilities.HeifEncoder) list.Add(F("heic", "HEIC", "heic", "Küçük", ConversionKind.Image, "heif"));
                if (Capabilities.WebpEncoder) list.Add(F("webp", "WEBP", "webp", "Web", ConversionKind.Image, "webp"));
                list.Add(F("pdf", "PDF", "pdf", null, ConversionKind.ImagesToPdf, "pdf"));
                list.Add(F("tiff", "TIFF", "tiff", null, ConversionKind.Image, "tiff"));
                if (Capabilities.AvifEncoder) list.Add(F("avif", "AVIF", "avif", null, ConversionKind.Image, "avif"));
                list.Add(F("gif", "GIF", "gif", null, ConversionKind.Image, "gif"));
                list.Add(F("bmp", "BMP", "bmp", null, ConversionKind.Image, "bmp"));
                return list;
            }
            case FileFamily.Video:
            {
                var list = new List<ConversionFormat> { F("mp4", "MP4", "mp4", "H.264", ConversionKind.Video, "h264") };
                if (Capabilities.HevcEncoder) list.Add(F("hevc", "HEVC", "mp4", "Küçük", ConversionKind.Video, "hevc"));
                list.Add(F("gif", "GIF", "gif", null, ConversionKind.Gif, "gif"));
                list.Add(F("m4a", "M4A", "m4a", "Ses", ConversionKind.Audio, "m4a"));
                list.Add(F("wav", "WAV", "wav", "Ses", ConversionKind.Audio, "wav"));
                if (Capabilities.Mp3Encoder) list.Add(F("mp3", "MP3", "mp3", "Ses", ConversionKind.Audio, "mp3"));
                return list;
            }
            case FileFamily.Audio:
            {
                var list = new List<ConversionFormat> { F("m4a", "M4A", "m4a", "AAC", ConversionKind.Audio, "m4a") };
                if (Capabilities.Mp3Encoder) list.Add(F("mp3", "MP3", "mp3", null, ConversionKind.Audio, "mp3"));
                list.Add(F("wav", "WAV", "wav", null, ConversionKind.Audio, "wav"));
                list.Add(F("aiff", "AIFF", "aiff", null, ConversionKind.Audio, "aiff"));
                if (Capabilities.FlacEncoder) list.Add(F("flac", "FLAC", "flac", "Kayıpsız", ConversionKind.Audio, "flac"));
                list.Add(F("caf", "CAF", "caf", null, ConversionKind.Audio, "caf"));
                return list;
            }
            case FileFamily.Pdf:
            {
                var list = new List<ConversionFormat>
                {
                    F("png", "PNG", "png", "Sayfalar", ConversionKind.PdfPages, "png"),
                    F("jpeg", "JPEG", "jpg", "Sayfalar", ConversionKind.PdfPages, "jpeg"),
                };
                if (Capabilities.HeifEncoder) list.Add(F("heic", "HEIC", "heic", "Sayfalar", ConversionKind.PdfPages, "heif"));
                list.Add(F("tiff", "TIFF", "tiff", "Sayfalar", ConversionKind.PdfPages, "tiff"));
                list.Add(F("txt", "TXT", "txt", "Metin", ConversionKind.PdfText, "txt"));
                list.Add(F("rtf", "RTF", "rtf", null, ConversionKind.PdfText, "rtf"));
                list.Add(F("docx", "DOCX", "docx", "Word", ConversionKind.PdfText, "docx"));
                return list;
            }
            default:
            {
                var list = new List<ConversionFormat>
                {
                    F("pdf", "PDF", "pdf", null, ConversionKind.DocumentToPdf, "pdf"),
                    F("docx", "DOCX", "docx", "Word", ConversionKind.Document, "docx"),
                    F("rtf", "RTF", "rtf", null, ConversionKind.Document, "rtf"),
                    F("txt", "TXT", "txt", null, ConversionKind.Document, "txt"),
                    F("html", "HTML", "html", null, ConversionKind.Document, "html"),
                    F("odt", "ODT", "odt", null, ConversionKind.Document, "odt"),
                };
                if (Capabilities.Word) list.Add(F("doc", "DOC", "doc", "Word 97", ConversionKind.Document, "doc"));
                return list;
            }
        }
    }

    /// <summary>Formats every given file can become, minus the format they already share.</summary>
    public static List<ConversionFormat> Targets(IReadOnlyCollection<string> paths)
    {
        if (paths.Count == 0) return [];
        var found = paths.Select(Family).ToList();
        if (found.Any(f => f is null)) return [];
        var distinct = found.Distinct().ToList();
        if (distinct.Count != 1) return [];
        var extensions = paths.Select(p => Normalized(Path.GetExtension(p))).Distinct().ToList();
        return Targets(distinct[0]!.Value)
            .Where(f => !(extensions.Count == 1 && extensions[0] == Normalized(f.Extension) && f.Id != "hevc"))
            .ToList();
    }

    public static string Normalized(string extension) => extension.TrimStart('.').ToLowerInvariant() switch
    {
        "jpeg" or "jpe" or "jfif" => "jpg",
        "tif" => "tiff",
        "aif" => "aiff",
        "htm" => "html",
        "heif" or "hif" => "heic",
        "markdown" => "md",
        var other => other,
    };

    // MARK: Converting


    /// <summary>Converts the files, writing results into <paramref name="folder"/>(source). Several images going to PDF become one document.</summary>
    public static List<string> Convert(IReadOnlyList<string> paths, ConversionFormat format, Func<string, string> folder, double quality,
                                       Action<double> progress, CancellationToken cancellation = default)
    {
        if (format.Kind == ConversionKind.ImagesToPdf && paths.Count > 1)
        {
            var output = UniquePath(folder(paths[0]), Path.GetFileNameWithoutExtension(paths[0]), "pdf");
            ImageConversion.ImagesToPdf(paths, output, quality, cancellation);
            progress(1);
            return [output];
        }
        var outputs = new List<string>();
        for (var i = 0; i < paths.Count; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            var start = i / (double)paths.Count;
            var share = 1.0 / paths.Count;
            outputs.AddRange(ConvertOne(paths[i], format, folder(paths[i]), quality, fraction => progress(start + fraction * share), cancellation));
        }
        progress(1);
        return outputs;
    }

    public static List<string> ConvertOne(string path, ConversionFormat format, string folder, double quality, Action<double> progress,
                                          CancellationToken cancellation = default)
    {
        if (!File.Exists(path)) throw ConversionException.Unreadable(Path.GetFileName(path));
        Directory.CreateDirectory(folder);
        var name = Path.GetFileNameWithoutExtension(path);
        var output = UniquePath(folder, name, format.Extension);
        try
        {
            switch (format.Kind)
            {
                case ConversionKind.Image:
                    ImageConversion.Convert(path, format.Codec, output, quality);
                    break;
                case ConversionKind.ImagesToPdf:
                    ImageConversion.ImagesToPdf([path], output, quality, cancellation);
                    break;
                case ConversionKind.Video:
                    MediaConversion.ConvertVideo(path, output, hevc: format.Codec == "hevc", progress, cancellation);
                    break;
                case ConversionKind.Gif:
                    MediaConversion.ConvertToGif(path, output, progress, cancellation);
                    break;
                case ConversionKind.Audio:
                    MediaConversion.ConvertAudio(path, format.Codec, output, progress, cancellation);
                    break;
                case ConversionKind.PdfPages:
                    return PdfConversion.ToImages(path, format.Codec, format.Extension, folder, quality, progress, cancellation);
                case ConversionKind.PdfText:
                    PdfConversion.ToDocument(path, format.Codec, output, progress, cancellation);
                    break;
                case ConversionKind.Document:
                    DocumentConversion.Convert(path, format.Codec, output);
                    break;
                case ConversionKind.DocumentToPdf:
                    DocumentConversion.ToPdf(path, output, cancellation);
                    break;
            }
        }
        catch
        {
            TryDelete(output);
            throw;
        }
        if (!File.Exists(output)) throw ConversionException.WriteFailed(Path.GetFileName(output));
        progress(1);
        return [output];
    }

    public static string UniquePath(string folder, string name, string extension)
    {
        var path = Path.Combine(folder, $"{name}.{extension}");
        var counter = 2;
        while (File.Exists(path) || Directory.Exists(path))
        {
            path = Path.Combine(folder, $"{name} ({counter}).{extension}");
            counter++;
        }
        return path;
    }

    public static string UniqueFolder(string folder, string name)
    {
        var path = Path.Combine(folder, name);
        var counter = 2;
        while (File.Exists(path) || Directory.Exists(path))
        {
            path = Path.Combine(folder, $"{name} {counter}");
            counter++;
        }
        return path;
    }

    internal static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* leave it */ }
    }

    /// <summary>Runs work on a fresh STA thread and waits for it, for engines that insist on one (Word, OCR callers).</summary>
    internal static T OnSta<T>(Func<T> work)
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA) return work();
        T result = default!;
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try { result = work(); }
            catch (Exception error) { failure = ExceptionDispatchInfo.Capture(error); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
        return result;
    }
}
