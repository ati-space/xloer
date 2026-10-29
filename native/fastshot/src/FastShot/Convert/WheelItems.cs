using FastShot.Core.Convert;

namespace FastShot;

/// <summary>The three wheels, each opened by its own key while dragging.</summary>
public enum WheelMode { Convert, Tools, Actions }

/// <summary>One segment of a wheel: a target format, a tool or an action.</summary>
public sealed record WheelItem(string Id, string Title, string? Detail, string Glyph, Color Tint,
                               ConversionFormat? Format = null, FileTool? Tool = null, FileAction? Action = null)
{
    public static List<WheelItem> For(WheelMode mode, IReadOnlyList<string> files) => mode switch
    {
        WheelMode.Convert => FileConverter.Targets(files).Select(f => new WheelItem($"convert.{f.Id}", f.Title, f.Detail, f.Glyph(), f.Tint(), Format: f)).ToList(),
        WheelMode.Tools => FileTools.Tools(files).Select(t => new WheelItem($"tool.{t}", t.Title(), t.Detail(), t.Glyph(), t.Tint(), Tool: t)).ToList(),
        _ => FileTools.Actions(files).Select(a => new WheelItem($"action.{a}", a.Title(), a.Detail(), a.Glyph(), a.Tint(), Action: a)).ToList(),
    };
}

/// <summary>The macOS system colours the wheel uses.</summary>
static class Tints
{
    public static readonly Color Blue = Color.FromRgb(0x0A, 0x84, 0xFF), Orange = Color.FromRgb(0xFF, 0x9F, 0x0A), Purple = Color.FromRgb(0xBF, 0x5A, 0xF2),
        Green = Color.FromRgb(0x30, 0xD1, 0x58), Red = Color.FromRgb(0xFF, 0x45, 0x3A), Teal = Color.FromRgb(0x40, 0xC8, 0xE0),
        Mint = Color.FromRgb(0x63, 0xE6, 0xE2), Pink = Color.FromRgb(0xFF, 0x37, 0x5F), Indigo = Color.FromRgb(0x5E, 0x5C, 0xE6),
        Cyan = Color.FromRgb(0x64, 0xD2, 0xFF), Brown = Color.FromRgb(0xAC, 0x8E, 0x68), Gray = Color.FromRgb(0x8E, 0x8E, 0x93);
}

public static class WheelTexts
{
    public static string Title(this WheelMode mode) => mode switch
    {
        WheelMode.Tools => "Araçlar",
        WheelMode.Actions => "İşlemler",
        _ => "Dönüştür",
    };

    // MARK: Formats

    public static Color Tint(this ConversionFormat format) => format.Id switch
    {
        "png" or "mp4" or "docx" => Tints.Blue,
        "jpeg" or "m4a" or "html" => Tints.Orange,
        "heic" or "hevc" => Tints.Purple,
        "webp" or "flac" => Tints.Green,
        "pdf" => Tints.Red,
        "tiff" or "wav" or "odt" => Tints.Teal,
        "avif" => Tints.Mint,
        "gif" or "mp3" => Tints.Pink,
        "mov" or "doc" => Tints.Indigo,
        "aiff" or "caf" => Tints.Cyan,
        "rtf" => Tints.Brown,
        _ => Tints.Gray,
    };

    public static string Glyph(this ConversionFormat format) => format.Kind switch
    {
        ConversionKind.Image => "",
        ConversionKind.ImagesToPdf or ConversionKind.DocumentToPdf => "",
        ConversionKind.Video => "",
        ConversionKind.Gif => "",
        ConversionKind.Audio => "",
        ConversionKind.PdfPages => "",
        _ => format.Codec == "html" ? "" : "",
    };

    // MARK: Tools

    public static string Title(this FileTool tool) => tool switch
    {
        FileTool.Edit => "Düzenle",
        FileTool.Crop => "Kırp",
        FileTool.Background => "Arka plan",
        FileTool.Compress => "Sıkıştır",
        FileTool.Metadata => "Meta veri",
        FileTool.RecognizeText => "Metin",
        FileTool.Translate => "Çevir",
        FileTool.RemoveAudio => "Sesi kaldır",
        _ => "Birleştir",
    };

    public static string? Detail(this FileTool tool) => tool switch
    {
        FileTool.Edit => "Çiz · yaz",
        FileTool.Background => "Paylaşıma hazır",
        FileTool.Compress => "Küçült",
        FileTool.Metadata => "Konumu sil",
        FileTool.RecognizeText => "OCR · kopyala",
        FileTool.Translate => "Metni çevir",
        FileTool.MergePdf => "Tek PDF",
        _ => null,
    };

    public static string Glyph(this FileTool tool) => tool switch
    {
        FileTool.Edit => "",
        FileTool.Crop => "",
        FileTool.Background => "",
        FileTool.Compress => "",
        FileTool.Metadata => "\uED1A",
        FileTool.RecognizeText => "",
        FileTool.Translate => Icons.Translate,
        FileTool.RemoveAudio => "",
        _ => "\uE7C4",
    };

    public static Color Tint(this FileTool tool) => tool switch
    {
        FileTool.Edit => Tints.Blue,
        FileTool.Crop => Tints.Orange,
        FileTool.Background => Tints.Purple,
        FileTool.Compress => Tints.Green,
        FileTool.Metadata => Tints.Red,
        FileTool.RecognizeText => Tints.Teal,
        FileTool.Translate => Tints.Blue,
        FileTool.RemoveAudio => Tints.Pink,
        _ => Tints.Indigo,
    };

    /// <summary>Completes the result notice: "foto.jpg sıkıştırıldı", "3 dosya sıkıştırıldı".</summary>
    public static string DoneText(this FileTool tool) => tool switch
    {
        FileTool.Compress => "sıkıştırıldı",
        FileTool.Metadata => "meta verileri temizlendi",
        FileTool.Background => "arka plan eklendi",
        FileTool.RemoveAudio => "sesi kaldırıldı",
        FileTool.MergePdf => "PDF'ler birleştirildi",
        FileTool.Crop => "kırpıldı",
        _ => "",
    };

    // MARK: Actions

    public static string Title(this FileAction action) => action switch
    {
        FileAction.Zip => "ZIP",
        FileAction.Unzip => "ZIP'i aç",
        FileAction.Share => "Paylaş",
        FileAction.CopyToClipboard => "Kopyala",
        FileAction.CopyPath => "Yolu kopyala",
        FileAction.Pin => "Sabitle",
        _ => "Terminal",
    };

    public static string? Detail(this FileAction action) => action switch
    {
        FileAction.Zip => "Arşivle",
        FileAction.Unzip => "Çıkar",
        FileAction.Share => "Yakındaki paylaşım",
        FileAction.CopyToClipboard => "Panoya",
        FileAction.Pin => "Ekrana",
        FileAction.Terminal => "Klasörde aç",
        _ => null,
    };

    public static string Glyph(this FileAction action) => action switch
    {
        FileAction.Zip => "",
        FileAction.Unzip => "",
        FileAction.Share => "",
        FileAction.CopyToClipboard => "",
        FileAction.CopyPath => "",
        FileAction.Pin => "",
        _ => "",
    };

    public static Color Tint(this FileAction action) => action switch
    {
        FileAction.Zip or FileAction.Unzip => Tints.Brown,
        FileAction.Share => Tints.Blue,
        FileAction.CopyToClipboard => Tints.Teal,
        FileAction.CopyPath => Tints.Indigo,
        FileAction.Pin => Tints.Orange,
        _ => Tints.Gray,
    };
}
