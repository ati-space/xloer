using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FastShot.Core.Convert.Pdf;

namespace FastShot.Core.Convert;

/// <summary>Lossless page-level PDF work on <see cref="PdfFile"/>: merging, removing metadata, recompressing images.
/// Page content streams are copied as they are, so text, links and vectors survive.</summary>
public static class PdfTools
{
    enum Mode { Merge, Clean, Compress }

    public static void Merge(IReadOnlyList<string> paths, string output) => Build(paths.Select(PdfFile.Load).ToList(), output, Mode.Merge);

    /// <summary>Drops the document information (author, producer, dates…), XMP metadata and private application data.</summary>
    public static void Clean(string path, string output) => Build([PdfFile.Load(path)], output, Mode.Clean);

    /// <summary>Recompresses large or uncompressed images as JPEG and compresses uncompressed streams.</summary>
    public static void Compress(string path, string output) => Build([PdfFile.Load(path)], output, Mode.Compress);

    static readonly string[] keptCatalogKeys = ["Outlines", "Names", "Dests", "PageMode", "PageLayout", "ViewerPreferences", "AcroForm", "Lang", "MarkInfo", "StructTreeRoot", "OCProperties"];

    static void Build(List<PdfFile> files, string output, Mode mode)
    {
        var builder = new PdfBuilder();
        var pagesNumber = builder.Reserve();
        var kids = new List<PdfObject>();
        var catalog = new Dictionary<string, PdfObject> { ["Type"] = new PdfName("Catalog"), ["Pages"] = new PdfReference(pagesNumber, 0) };
        int? info = null;
        foreach (var file in files)
        {
            var copier = new PdfCopier(file, builder)
            {
                SkipKeys = mode == Mode.Clean ? ["Metadata", "PieceInfo", "LastModified"] : [],
                TransformStream = mode == Mode.Compress ? Recompress : null,
            };
            var pages = file.Pages();
            if (pages.Count == 0) throw ConversionException.Unreadable(file.Name);
            var targets = pages.Select(_ => builder.Reserve()).ToList();
            for (var i = 0; i < pages.Count; i++)
                if (pages[i].number >= 0) copier.Map(pages[i].number, targets[i]);
            for (var i = 0; i < pages.Count; i++)
            {
                var entries = new Dictionary<string, PdfObject>(pages[i].page.Entries);
                entries.Remove("Parent");
                var copy = (PdfDictionary)copier.Copy(new PdfDictionary(entries));
                copy.Entries["Type"] = new PdfName("Page");
                copy.Entries["Parent"] = new PdfReference(pagesNumber, 0);
                builder.Set(targets[i], copy);
                kids.Add(new PdfReference(targets[i], 0));
            }
            if (mode != Mode.Merge && file.Catalog is { } source)
            {
                foreach (var key in keptCatalogKeys)
                    if (source[key] is { } value) catalog[key] = copier.Copy(value);
                if (mode == Mode.Compress)
                {
                    if (source["Metadata"] is { } metadata) catalog["Metadata"] = copier.Copy(metadata);
                    if (file.Trailer["Info"] is { } original && copier.Copy(original) is PdfReference copied) info = copied.Number;
                }
            }
            copier.Drain();
        }
        builder.Set(pagesNumber, new PdfDictionary(new()
        {
            ["Type"] = new PdfName("Pages"),
            ["Kids"] = new PdfArray(kids),
            ["Count"] = new PdfNumber(kids.Count, true),
        }));
        var root = builder.Add(new PdfDictionary(catalog));
        builder.Save(output, root, info);
    }

    const int MaxImageSide = 1800;

    static PdfStream Recompress(PdfStream source, PdfStream copy, PdfFile file)
    {
        if (source.NameOf("Subtype") != "Image")
        {
            if (source["Filter"] is null && copy.Data.Length > 512)
            {
                var deflated = PdfFile.Deflate(copy.Data);
                if (deflated.Length < copy.Data.Length * 0.9)
                {
                    copy.Entries["Filter"] = new PdfName("FlateDecode");
                    copy.Entries.Remove("DecodeParms");
                    copy.Data = deflated;
                }
            }
            return copy;
        }
        try
        {
            if (file.Resolve(source["ImageMask"]) is PdfBoolean { Value: true } || source["Decode"] is not null) return copy;
            if ((file.Resolve(source["BitsPerComponent"]) as PdfNumber)?.Int != 8) return copy;
            var width = (file.Resolve(source["Width"]) as PdfNumber)?.Int ?? 0;
            var height = (file.Resolve(source["Height"]) as PdfNumber)?.Int ?? 0;
            if (width < 16 || height < 16) return copy;
            var components = Components(file, source["ColorSpace"]);
            if (components is not (1 or 3)) return copy;
            var filters = file.Resolve(source["Filter"]) switch
            {
                PdfName n => [n.Value],
                PdfArray a => a.Items.Select(i => (file.Resolve(i) as PdfName)?.Value ?? "?").ToList(),
                _ => new List<string>(),
            };
            BitmapSource image;
            if (filters is ["DCTDecode"] or ["DCT"])
            {
                var decoder = new JpegBitmapDecoder(new MemoryStream(source.Data), BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
                image = decoder.Frames[0];
                if (image.Format == PixelFormats.Cmyk32) return copy;
            }
            else if (filters.All(f => f is "FlateDecode" or "Fl"))
            {
                var raw = file.Decode(source);
                var stride = width * components;
                if (raw.Length < stride * height) return copy;
                image = BitmapSource.Create(width, height, 96, 96, components == 3 ? PixelFormats.Rgb24 : PixelFormats.Gray8, null, raw, stride);
            }
            else
            {
                return copy;
            }
            if (Math.Max(width, height) > MaxImageSide) image = ImageExporter.Scaled(image, MaxImageSide);
            BitmapSource encoded = components == 1 ? new FormatConvertedBitmap(image, PixelFormats.Gray8, null, 0) : new FormatConvertedBitmap(image, PixelFormats.Bgr24, null, 0);
            var encoder = new JpegBitmapEncoder { QualityLevel = 62 };
            encoder.Frames.Add(BitmapFrame.Create(encoded));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            var jpeg = stream.ToArray();
            if (jpeg.Length >= copy.Data.Length * 0.9) return copy;
            copy.Entries["Width"] = new PdfNumber(encoded.PixelWidth, true);
            copy.Entries["Height"] = new PdfNumber(encoded.PixelHeight, true);
            copy.Entries["Filter"] = new PdfName("DCTDecode");
            copy.Entries["BitsPerComponent"] = new PdfNumber(8, true);
            copy.Entries.Remove("DecodeParms");
            copy.Data = jpeg;
            return copy;
        }
        catch
        {
            return copy;
        }
    }

    static int Components(PdfFile file, PdfObject? colorSpace) => file.Resolve(colorSpace) switch
    {
        PdfName { Value: "DeviceRGB" or "CalRGB" } => 3,
        PdfName { Value: "DeviceGray" or "CalGray" } => 1,
        PdfArray { Items: [PdfName { Value: "ICCBased" }, var profile, ..] } => (file.Resolve(profile) as PdfDictionary)?["N"] is PdfNumber n ? n.Int : 0,
        PdfArray { Items: [PdfName { Value: "CalRGB" }, ..] } => 3,
        PdfArray { Items: [PdfName { Value: "CalGray" }, ..] } => 1,
        _ => 0,
    };
}
