using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace FastShot.Core.Convert;

/// <summary>A minimal PDF writer: one image per page, JPEG (DCTDecode) or lossless zlib (FlateDecode) RGB.</summary>
public sealed class PdfWriter : IDisposable
{
    readonly Stream stream;
    readonly Dictionary<int, long> offsets = [];
    readonly List<int> pages = [];
    int nextObject = 3; // 1: catalog, 2: page tree
    bool finished;

    public int PageCount => pages.Count;

    public PdfWriter(string path)
    {
        stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        Write("%PDF-1.4\n%âãÏÓ\n");
    }

    static string N(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    void Write(string text) => stream.Write(Encoding.Latin1.GetBytes(text));

    int Begin(int? number = null)
    {
        var id = number ?? nextObject++;
        offsets[id] = stream.Position;
        Write($"{id} 0 obj\n");
        return id;
    }

    void End() => Write("endobj\n");

    /// <summary>Adds a page showing a JPEG file's bytes as-is.</summary>
    public void AddJpegPage(byte[] jpeg, int pixelWidth, int pixelHeight, bool gray, double widthPoints, double heightPoints) =>
        AddImagePage(jpeg, "/DCTDecode", pixelWidth, pixelHeight, gray ? "/DeviceGray" : "/DeviceRGB", widthPoints, heightPoints);

    /// <summary>Adds a page from packed top-down RGB bytes, compressed losslessly.</summary>
    public void AddRgbPage(byte[] rgb, int pixelWidth, int pixelHeight, double widthPoints, double heightPoints)
    {
        using var packed = new MemoryStream();
        using (var zlib = new ZLibStream(packed, CompressionLevel.Optimal, leaveOpen: true)) zlib.Write(rgb);
        AddImagePage(packed.ToArray(), "/FlateDecode", pixelWidth, pixelHeight, "/DeviceRGB", widthPoints, heightPoints);
    }

    void AddImagePage(byte[] data, string filter, int pixelWidth, int pixelHeight, string colorSpace, double widthPoints, double heightPoints)
    {
        var image = Begin();
        Write($"<< /Type /XObject /Subtype /Image /Width {pixelWidth} /Height {pixelHeight} /ColorSpace {colorSpace} /BitsPerComponent 8 /Filter {filter} /Length {data.Length} >>\nstream\n");
        stream.Write(data);
        Write("\nendstream\n");
        End();

        var content = Encoding.Latin1.GetBytes($"q {N(widthPoints)} 0 0 {N(heightPoints)} 0 0 cm /Im0 Do Q\n");
        var contents = Begin();
        Write($"<< /Length {content.Length} >>\nstream\n");
        stream.Write(content);
        Write("endstream\n");
        End();

        var page = Begin();
        Write($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {N(widthPoints)} {N(heightPoints)}] /Resources << /XObject << /Im0 {image} 0 R >> >> /Contents {contents} 0 R >>\n");
        End();
        pages.Add(page);
    }

    public void Finish()
    {
        if (finished) return;
        finished = true;
        Begin(2);
        Write($"<< /Type /Pages /Kids [{string.Join(" ", pages.Select(p => $"{p} 0 R"))}] /Count {pages.Count} >>\n");
        End();
        Begin(1);
        Write("<< /Type /Catalog /Pages 2 0 R >>\n");
        End();
        var info = Begin();
        Write("<< /Producer (FastShot) /Creator (FastShot) >>\n");
        End();
        var xref = stream.Position;
        var count = nextObject;
        Write($"xref\n0 {count}\n0000000000 65535 f \n");
        for (var i = 1; i < count; i++) Write($"{(offsets.TryGetValue(i, out var offset) ? offset : 0):D10} 00000 n \n");
        Write($"trailer\n<< /Size {count} /Root 1 0 R /Info {info} 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        stream.Flush();
    }

    public void Dispose()
    {
        try { Finish(); } finally { stream.Dispose(); }
    }
}
