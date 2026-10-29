using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FastShot.Core.Convert;

/// <summary>Character formatting of a run. Size in points, colour as RRGGBB; null means "document default".</summary>
public readonly record struct RunStyle(bool Bold = false, bool Italic = false, bool Underline = false, bool Strike = false,
                                       double? Size = null, string? Color = null, string? Font = null, bool Code = false);

/// <summary>Text with one style. '\t' is a tab and '\n' a line break inside the paragraph.</summary>
public sealed record DocRun(string Text, RunStyle Style);

public sealed class DocParagraph
{
    public List<DocRun> Runs { get; } = [];
    /// <summary>1–6 for headings, 0 for body text.</summary>
    public int Heading { get; set; }
    /// <summary>List marker shown before the text: "• " or "3. ".</summary>
    public string? Prefix { get; set; }
    public TextAlignment Alignment { get; set; } = TextAlignment.Left;

    public string Text => string.Concat(Runs.Select(r => r.Text));
    public bool IsBullet => Prefix == "• ";
    public bool IsNumbered => Prefix is { Length: > 1 } p && char.IsDigit(p[0]);

    public static DocParagraph Plain(string text)
    {
        var paragraph = new DocParagraph();
        if (text.Length > 0) paragraph.Runs.Add(new DocRun(text, default));
        return paragraph;
    }

    public void Add(string text, RunStyle style)
    {
        if (text.Length == 0) return;
        if (Runs.Count > 0 && Runs[^1].Style == style) Runs[^1] = Runs[^1] with { Text = Runs[^1].Text + text };
        else Runs.Add(new DocRun(text, style));
    }
}

/// <summary>The rich text every document reader produces and every writer consumes (FastShot's NSAttributedString).</summary>
public sealed class TextDocument
{
    public List<DocParagraph> Paragraphs { get; } = [];
    public string? Title { get; set; }

    public const double BodySize = 11;
    public static double HeadingSize(int level) => level switch { 1 => 20, 2 => 16, 3 => 14, 4 => 12, _ => 11 };

    public string PlainText(string newline = "\r\n") =>
        string.Join(newline, Paragraphs.Select(p => (p.Prefix ?? "") + p.Text.Replace("\n", newline)));
}

/// <summary>Reads any supported document into a <see cref="TextDocument"/> and writes it back out in another format.</summary>
public static class DocumentConversion
{
    public static TextDocument Read(string path)
    {
        var name = Path.GetFileName(path);
        try
        {
            return FileConverter.Normalized(Path.GetExtension(path)) switch
            {
                "txt" or "text" => PlainText.Read(path),
                "md" => Markdown.Read(PlainText.Decode(File.ReadAllBytes(path))),
                "html" => Html.Read(PlainText.Decode(File.ReadAllBytes(path))),
                "docx" => Docx.Read(path),
                "odt" => Odt.Read(path),
                "rtf" => Rtf.Read(path),
                "doc" => ReadWithWord(path),
                _ => throw ConversionException.Unsupported(name),
            };
        }
        catch (ConversionException) { throw; }
        catch { throw ConversionException.Unreadable(name); }
    }

    static TextDocument ReadWithWord(string path)
    {
        if (!Capabilities.Word) throw new ConversionException($"{Path.GetFileName(path)}: DOC dosyalarını okumak için Microsoft Word gerekli.");
        var temporary = Path.Combine(Path.GetTempPath(), $"fastshot-{Guid.NewGuid():N}.docx");
        try
        {
            WordEngine.Convert(path, temporary, "docx");
            return Docx.Read(temporary);
        }
        finally { FileConverter.TryDelete(temporary); }
    }

    public static void Write(TextDocument document, string codec, string output)
    {
        document.Title ??= Path.GetFileNameWithoutExtension(output);
        switch (codec)
        {
            case "txt": PlainText.Write(document, output); break;
            case "rtf": Rtf.Write(document, output); break;
            case "html": File.WriteAllText(output, Html.Write(document), new UTF8Encoding(false)); break;
            case "docx": Docx.Write(document, output); break;
            case "odt": Odt.Write(document, output); break;
            case "doc":
            {
                var temporary = Path.Combine(Path.GetTempPath(), $"fastshot-{Guid.NewGuid():N}.docx");
                try
                {
                    Docx.Write(document, temporary);
                    WordEngine.Convert(temporary, output, "doc");
                }
                finally { FileConverter.TryDelete(temporary); }
                break;
            }
            default: throw ConversionException.Unsupported(Path.GetFileName(output));
        }
    }

    public static void Convert(string input, string codec, string output)
    {
        var document = Read(input);
        document.Title = Path.GetFileNameWithoutExtension(input);
        Write(document, codec, output);
    }

    static readonly HashSet<string> wordOpens = ["doc", "docx", "rtf", "odt", "txt", "html"];

    /// <summary>A4 PDF: Word keeps the document's own layout; otherwise Edge prints it (HTML as-is, the rest via HTML);
    /// without either, pages are laid out with WPF and stored as images.</summary>
    public static void ToPdf(string input, string output, CancellationToken cancellation)
    {
        var extension = FileConverter.Normalized(Path.GetExtension(input));
        if (Capabilities.Word && wordOpens.Contains(extension))
        {
            try
            {
                WordEngine.Convert(input, output, "pdf");
                if (File.Exists(output)) return;
            }
            catch (OperationCanceledException) { throw; }
            catch { FileConverter.TryDelete(output); }
        }
        TextDocument? document = null;
        if (Capabilities.Edge)
        {
            var html = input;
            string? temporary = null;
            try
            {
                if (extension != "html")
                {
                    document = Read(input);
                    document.Title = Path.GetFileNameWithoutExtension(input);
                    temporary = Path.Combine(Path.GetTempPath(), $"fastshot-{Guid.NewGuid():N}.html");
                    File.WriteAllText(temporary, Html.Write(document, forPrint: true), new UTF8Encoding(false));
                    html = temporary;
                }
                EdgeEngine.PrintToPdf(html, output, cancellation);
                return;
            }
            catch (OperationCanceledException) { throw; }
            catch (ConversionException) when (document is null && extension != "html") { throw; }
            catch { FileConverter.TryDelete(output); }
            finally { if (temporary is not null) FileConverter.TryDelete(temporary); }
        }
        document ??= Read(input);
        RasterPdf(document, output, cancellation);
    }

    /// <summary>Paginates with WPF's FlowDocument on A4 and stores each page as a 200 dpi image.</summary>
    public static void RasterPdf(TextDocument document, string output, CancellationToken cancellation)
    {
        var flow = Rtf.ToFlow(document);
        var page = new Size(793.7, 1122.5);
        flow.PageWidth = page.Width;
        flow.PageHeight = page.Height;
        flow.PagePadding = new Thickness(75);
        flow.ColumnWidth = double.PositiveInfinity;
        var paginator = ((IDocumentPaginatorSource)flow).DocumentPaginator;
        paginator.PageSize = page;
        paginator.ComputePageCount();
        const double dpi = 200;
        using var pdf = new PdfWriter(output);
        for (var i = 0; i < Math.Max(1, paginator.PageCount); i++)
        {
            cancellation.ThrowIfCancellationRequested();
            using var documentPage = paginator.GetPage(i);
            var width = (int)(page.Width * dpi / 96);
            var height = (int)(page.Height * dpi / 96);
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(page));
                dc.DrawRectangle(new VisualBrush(documentPage.Visual), null, new Rect(page));
            }
            var bitmap = new RenderTargetBitmap(width, height, dpi, dpi, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var jpeg = ImageConversion.Encode(bitmap, "jpeg", 0.9, hasAlpha: false);
            pdf.AddJpegPage(jpeg, width, height, false, 595.28, 841.89);
        }
    }
}

public static class PlainText
{
    static PlainText()
    {
        try { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); } catch { /* already registered */ }
    }

    /// <summary>UTF-8/UTF-16 by BOM, then strict UTF-8, then the Turkish Windows code page.</summary>
    public static string Decode(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        try { return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes); }
        catch (DecoderFallbackException)
        {
            try { return Encoding.GetEncoding(1254).GetString(bytes); }
            catch { return Encoding.Latin1.GetString(bytes); }
        }
    }

    public static TextDocument Read(string path)
    {
        var text = Decode(File.ReadAllBytes(path)).Replace("\r\n", "\n").Replace('\r', '\n');
        var document = new TextDocument();
        foreach (var line in text.Split('\n')) document.Paragraphs.Add(DocParagraph.Plain(line));
        if (document.Paragraphs.Count > 1 && document.Paragraphs[^1].Runs.Count == 0) document.Paragraphs.RemoveAt(document.Paragraphs.Count - 1);
        return document;
    }

    public static void Write(TextDocument document, string path) => File.WriteAllText(path, document.PlainText(), new UTF8Encoding(false));
}

/// <summary>RTF through WPF's own RTF engine, by way of a FlowDocument.</summary>
public static class Rtf
{
    public static TextDocument Read(string path)
    {
        var flow = new FlowDocument();
        using (var stream = File.OpenRead(path)) new TextRange(flow.ContentStart, flow.ContentEnd).Load(stream, DataFormats.Rtf);
        return FromFlow(flow);
    }

    public static void Write(TextDocument document, string path)
    {
        var flow = ToFlow(document);
        using var stream = File.Create(path);
        new TextRange(flow.ContentStart, flow.ContentEnd).Save(stream, DataFormats.Rtf);
    }

    static readonly FontFamily bodyFont = new("Calibri");
    static readonly FontFamily codeFont = new("Consolas");

    public static FlowDocument ToFlow(TextDocument document)
    {
        var flow = new FlowDocument { FontFamily = bodyFont, FontSize = Pt(TextDocument.BodySize), PagePadding = new Thickness(0) };
        foreach (var p in document.Paragraphs)
        {
            var paragraph = new Paragraph { Margin = new Thickness(0, p.Heading > 0 ? Pt(10) : 0, 0, Pt(6)), TextAlignment = p.Alignment };
            if (p.Heading > 0)
            {
                paragraph.FontSize = Pt(TextDocument.HeadingSize(p.Heading));
                paragraph.FontWeight = FontWeights.Bold;
            }
            if (p.Prefix is not null) paragraph.Inlines.Add(new Run(p.Prefix));
            foreach (var run in p.Runs)
            {
                var parts = run.Text.Split('\n');
                for (var i = 0; i < parts.Length; i++)
                {
                    if (i > 0) paragraph.Inlines.Add(new LineBreak());
                    if (parts[i].Length == 0) continue;
                    var inline = new Run(parts[i]);
                    Apply(inline, run.Style);
                    paragraph.Inlines.Add(inline);
                }
            }
            flow.Blocks.Add(paragraph);
        }
        return flow;
    }

    static double Pt(double points) => points * 96 / 72;

    static void Apply(Run inline, RunStyle style)
    {
        if (style.Bold) inline.FontWeight = FontWeights.Bold;
        if (style.Italic) inline.FontStyle = FontStyles.Italic;
        var decorations = new TextDecorationCollection();
        if (style.Underline) decorations.Add(TextDecorations.Underline);
        if (style.Strike) decorations.Add(TextDecorations.Strikethrough);
        if (decorations.Count > 0) inline.TextDecorations = decorations;
        if (style.Size is { } size) inline.FontSize = Pt(size);
        if (style.Color is { } hex && RGBAColor.FromHex(hex) is { } color) inline.Foreground = color.Brush();
        if (style.Code) inline.FontFamily = codeFont;
        else if (style.Font is { } font) inline.FontFamily = new FontFamily(font);
    }

    public static TextDocument FromFlow(FlowDocument flow)
    {
        var document = new TextDocument();
        foreach (var block in flow.Blocks) AddBlock(document, block, flow, null);
        return document;
    }

    static void AddBlock(TextDocument document, Block block, FlowDocument flow, string? prefix)
    {
        switch (block)
        {
            case Paragraph paragraph:
            {
                var p = new DocParagraph { Prefix = prefix, Alignment = paragraph.TextAlignment };
                foreach (var inline in paragraph.Inlines) AddInline(p, inline, flow);
                document.Paragraphs.Add(p);
                break;
            }
            case List list:
            {
                var number = list.StartIndex;
                foreach (var item in list.ListItems)
                {
                    var marker = list.MarkerStyle is TextMarkerStyle.Decimal or TextMarkerStyle.LowerLatin or TextMarkerStyle.UpperLatin
                        or TextMarkerStyle.LowerRoman or TextMarkerStyle.UpperRoman ? $"{number}. " : "• ";
                    number++;
                    var first = true;
                    foreach (var child in item.Blocks)
                    {
                        AddBlock(document, child, flow, first ? marker : null);
                        first = false;
                    }
                }
                break;
            }
            case Section section:
                foreach (var child in section.Blocks) AddBlock(document, child, flow, prefix);
                break;
            case Table table:
                foreach (var group in table.RowGroups)
                    foreach (var row in group.Rows)
                    {
                        var p = new DocParagraph();
                        for (var c = 0; c < row.Cells.Count; c++)
                        {
                            if (c > 0) p.Add("\t", default);
                            var cell = new TextDocument();
                            foreach (var child in row.Cells[c].Blocks) AddBlock(cell, child, flow, null);
                            for (var i = 0; i < cell.Paragraphs.Count; i++)
                            {
                                if (i > 0) p.Add(" ", default);
                                foreach (var run in cell.Paragraphs[i].Runs) p.Add(run.Text, run.Style);
                            }
                        }
                        document.Paragraphs.Add(p);
                    }
                break;
        }
    }

    static void AddInline(DocParagraph paragraph, Inline inline, FlowDocument flow)
    {
        switch (inline)
        {
            case Run run:
                paragraph.Add(run.Text.Replace("\r\n", "\n").Replace('\r', '\n'), StyleOf(run, flow));
                break;
            case LineBreak:
                paragraph.Add("\n", default);
                break;
            case Span span:
                foreach (var child in span.Inlines) AddInline(paragraph, child, flow);
                break;
        }
    }

    static RunStyle StyleOf(Run run, FlowDocument flow)
    {
        bool underline = false, strike = false;
        for (DependencyObject? node = run; node is Inline or Paragraph; node = (node as FrameworkContentElement)?.Parent)
        {
            var decorations = node is Inline i ? i.TextDecorations : ((Paragraph)node).TextDecorations;
            if (decorations is null) continue;
            foreach (var decoration in decorations)
            {
                if (decoration.Location == TextDecorationLocation.Underline) underline = true;
                if (decoration.Location == TextDecorationLocation.Strikethrough) strike = true;
            }
        }
        string? color = null;
        if (run.Foreground is SolidColorBrush brush && (brush.Color.R | brush.Color.G | brush.Color.B) != 0 && brush.Color.A > 0)
            color = $"{brush.Color.R:X2}{brush.Color.G:X2}{brush.Color.B:X2}";
        var font = run.FontFamily.Source;
        var code = font.Contains("Consolas", StringComparison.OrdinalIgnoreCase) || font.Contains("Courier", StringComparison.OrdinalIgnoreCase);
        var size = Math.Round(run.FontSize * 72 / 96 * 2) / 2;
        return new RunStyle(run.FontWeight.ToOpenTypeWeight() >= 600, run.FontStyle != FontStyles.Normal, underline, strike,
                            Math.Abs(size - TextDocument.BodySize) < 0.25 ? null : size, color,
                            font == flow.FontFamily.Source || code ? null : font.Split(',')[0].Trim(), code);
    }
}

static class Xml
{
    public static string Escape(string text) => System.Security.SecurityElement.Escape(text) ?? "";
    public static string N(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
