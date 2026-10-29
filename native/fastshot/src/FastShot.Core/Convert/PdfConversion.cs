using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace FastShot.Core.Convert;

/// <summary>PDF pages to images with Windows' PDF engine; PDF text through Word (when installed) or OCR.</summary>
public static class PdfConversion
{
    public static PdfDocument Open(string path)
    {
        try
        {
            var file = StorageFile.GetFileFromPathAsync(Path.GetFullPath(path)).AsTask().GetAwaiter().GetResult();
            return PdfDocument.LoadFromFileAsync(file).AsTask().GetAwaiter().GetResult();
        }
        catch (Exception error) when (error.HResult == unchecked((int)0x8007052B))
        {
            throw new ConversionException($"{Path.GetFileName(path)} parola korumalı.");
        }
        catch
        {
            throw ConversionException.Unreadable(Path.GetFileName(path));
        }
    }

    public static int PageCount(string path) => (int)Open(path).PageCount;

    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern uint GetDpiForSystem();

    static double SystemScale
    {
        get
        {
            try { return Math.Max(1, GetDpiForSystem()) / 96.0; }
            catch { return 1; }
        }
    }

    /// <summary>Renders a page on white at <paramref name="scale"/> × its size in DIPs (1.5 = 144 dpi).</summary>
    public static BitmapSource RenderPage(PdfDocument document, uint index, double scale)
    {
        using var page = document.GetPage(index);
        // Windows reports page sizes in DIPs times the system scale when the process is DPI aware; undo that.
        var width = (uint)Math.Max(1, Math.Round(page.Size.Width / SystemScale * scale));
        using var stream = new InMemoryRandomAccessStream();
        var options = new PdfPageRenderOptions { DestinationWidth = width, BackgroundColor = new Windows.UI.Color { A = 255, R = 255, G = 255, B = 255 } };
        page.RenderToStreamAsync(stream, options).AsTask().GetAwaiter().GetResult();
        stream.Seek(0);
        using var managed = stream.AsStreamForRead();
        var decoder = BitmapDecoder.Create(managed, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var frame = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgr24, null, 0);
        var stride = frame.PixelWidth * 3;
        var pixels = new byte[stride * frame.PixelHeight];
        frame.CopyPixels(pixels, stride, 0);
        var dpi = 96 * scale;
        var result = BitmapSource.Create(frame.PixelWidth, frame.PixelHeight, dpi, dpi, PixelFormats.Bgr24, null, pixels, stride);
        result.Freeze();
        return result;
    }

    /// <summary>One image per page; several pages go into a "name (PNG)" folder, which is what gets returned.</summary>
    public static List<string> ToImages(string path, string codec, string extension, string folder, double quality, Action<double> progress, CancellationToken cancellation)
    {
        var document = Open(path);
        var pages = document.PageCount;
        if (pages == 0) throw ConversionException.Unreadable(Path.GetFileName(path));
        var name = Path.GetFileNameWithoutExtension(path);
        var target = folder;
        if (pages > 1)
        {
            target = FileConverter.UniqueFolder(folder, $"{name} ({extension.ToUpperInvariant()})");
            Directory.CreateDirectory(target);
        }
        var outputs = new List<string>();
        for (uint i = 0; i < pages; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            var image = RenderPage(document, i, 1.5);
            var output = FileConverter.UniquePath(target, pages > 1 ? $"{name} - {i + 1}" : name, extension);
            File.WriteAllBytes(output, ImageConversion.Encode(image, codec, quality, hasAlpha: false));
            outputs.Add(output);
            progress((i + 1) / (double)pages);
        }
        return pages > 1 ? [target] : outputs;
    }

    /// <summary>Text out of a PDF as TXT, RTF or DOCX. Word reflows the PDF with its layout; without Word every page is read with OCR.</summary>
    public static void ToDocument(string path, string codec, string output, Action<double> progress, CancellationToken cancellation)
    {
        if (Capabilities.Word)
        {
            try
            {
                WordEngine.Convert(path, output, codec);
                if (File.Exists(output)) return;
            }
            catch (OperationCanceledException) { throw; }
            catch { FileConverter.TryDelete(output); }
        }
        var document = Open(path);
        var text = new TextDocument();
        for (uint i = 0; i < document.PageCount; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            var page = RenderPage(document, i, 2);
            var lines = Ocr.Lines(page).GetAwaiter().GetResult();
            if (i > 0) text.Paragraphs.Add(new DocParagraph());
            foreach (var line in lines) text.Paragraphs.Add(DocParagraph.Plain(line));
            progress((i + 1) / (double)document.PageCount * 0.95);
        }
        DocumentConversion.Write(text, codec, output);
    }
}
