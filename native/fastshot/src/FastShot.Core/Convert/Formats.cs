namespace FastShot.Core.Convert;

public enum FileFamily { Image, Video, Audio, Pdf, Document }

public static class FileFamilyInfo
{
    public static string Title(this FileFamily family) => family switch
    {
        FileFamily.Image => "Görüntü",
        FileFamily.Video => "Video",
        FileFamily.Audio => "Ses",
        FileFamily.Pdf => "PDF",
        _ => "Belge",
    };
}

public enum ConversionKind { Image, ImagesToPdf, Video, Gif, Audio, PdfPages, PdfText, Document, DocumentToPdf }

/// <summary>One choice on the conversion wheel. <see cref="Codec"/> names the encoder or document format behind it.</summary>
public sealed record ConversionFormat(string Id, string Title, string Extension, string? Detail, ConversionKind Kind, string Codec);

public sealed class ConversionException(string message) : Exception(message)
{
    public static ConversionException Unreadable(string name) => new($"{name} okunamadı.");
    public static ConversionException WriteFailed(string name) => new($"{name} yazılamadı.");
    public static ConversionException NoAudio(string name) => new($"{name} içinde ses yok.");
    public static ConversionException Unsupported(string name) => new($"{name} bu biçime dönüştürülemiyor.");
}

/// <summary>Encoders that may or may not exist on this PC. Each is probed once, by actually encoding a tiny sample.</summary>
public static class Capabilities
{
    static readonly Lazy<bool> heif = new(() => ImageConversion.CanEncode("heif"));
    static readonly Lazy<bool> wicWebp = new(() => ImageConversion.CanEncode("webp"));
    static readonly Lazy<bool> hevc = new(Media.MediaProbe.CanEncodeHevc);
    static readonly Lazy<bool> mp3 = new(() => Media.AudioWriter.CanEncode("mp3"));
    static readonly Lazy<bool> flac = new(() => Media.AudioWriter.CanEncode("flac"));
    static readonly Lazy<bool> word = new(WordEngine.IsInstalled);

    public static bool HeifEncoder => heif.Value;
    public static bool WebpEncoder => wicWebp.Value || ExternalTools.Ffmpeg is not null;
    public static bool AvifEncoder => ExternalTools.Ffmpeg is not null;
    public static bool HevcEncoder => hevc.Value;
    public static bool Mp3Encoder => mp3.Value || ExternalTools.Ffmpeg is not null;
    public static bool FlacEncoder => flac.Value || ExternalTools.Ffmpeg is not null;
    public static bool Word => WordEngine.Enabled && word.Value;
    public static bool WordInstalled => word.Value;
    public static bool Edge => EdgeEngine.Path is not null;
    public static bool NativeMp3 => mp3.Value;
    public static bool NativeFlac => flac.Value;
    public static bool NativeWebp => wicWebp.Value;
}
