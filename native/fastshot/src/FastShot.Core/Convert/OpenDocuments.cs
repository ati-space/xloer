using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows;
using System.Xml.Linq;

namespace FastShot.Core.Convert;

/// <summary>Word .docx (Office Open XML) read and written directly: paragraphs, headings, runs with bold/italic/underline/strike,
/// size, colour and font, tabs, line breaks, lists (as markers) and tables (as tab-separated rows).</summary>
public static class Docx
{
    static readonly XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    public static void Write(TextDocument document, string path)
    {
        using var file = new FileStream(path, FileMode.Create);
        using var zip = new ZipArchive(file, ZipArchiveMode.Create);
        void Entry(string name, string content)
        {
            using var writer = new StreamWriter(zip.CreateEntry(name, CompressionLevel.Optimal).Open(), new UTF8Encoding(false));
            writer.Write(content);
        }
        Entry("[Content_Types].xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
            <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
            <Default Extension="xml" ContentType="application/xml"/>
            <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
            <Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>
            <Override PartName="/docProps/core.xml" ContentType="application/vnd.openxmlformats-package.core-properties+xml"/>
            </Types>
            """);
        Entry("_rels/.rels", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
            <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
            <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties" Target="docProps/core.xml"/>
            </Relationships>
            """);
        Entry("word/_rels/document.xml.rels", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
            <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
            </Relationships>
            """);
        Entry("docProps/core.xml", $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <cp:coreProperties xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties" xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:dcterms="http://purl.org/dc/terms/" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
            <dc:title>{Xml.Escape(document.Title ?? "")}</dc:title><dc:creator>FastShot</dc:creator>
            <dcterms:created xsi:type="dcterms:W3CDTF">{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}</dcterms:created>
            </cp:coreProperties>
            """);
        var styles = new StringBuilder("""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
            <w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii="Calibri" w:hAnsi="Calibri" w:eastAsia="Calibri" w:cs="Calibri"/><w:sz w:val="22"/><w:szCs w:val="22"/><w:lang w:val="tr-TR"/></w:rPr></w:rPrDefault>
            <w:pPrDefault><w:pPr><w:spacing w:after="120" w:line="276" w:lineRule="auto"/></w:pPr></w:pPrDefault></w:docDefaults>
            <w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/><w:qFormat/></w:style>
            """);
        for (var level = 1; level <= 6; level++)
        {
            var size = (int)(TextDocument.HeadingSize(level) * 2);
            styles.Append($"""<w:style w:type="paragraph" w:styleId="Heading{level}"><w:name w:val="heading {level}"/><w:basedOn w:val="Normal"/><w:next w:val="Normal"/><w:qFormat/><w:pPr><w:keepNext/><w:spacing w:before="240" w:after="80"/><w:outlineLvl w:val="{level - 1}"/></w:pPr><w:rPr><w:b/><w:sz w:val="{size}"/><w:szCs w:val="{size}"/></w:rPr></w:style>""");
        }
        styles.Append("</w:styles>");
        Entry("word/styles.xml", styles.ToString());

        var body = new StringBuilder();
        foreach (var p in document.Paragraphs)
        {
            body.Append("<w:p>");
            var properties = new StringBuilder();
            if (p.Heading > 0) properties.Append($"<w:pStyle w:val=\"Heading{p.Heading}\"/>");
            var jc = p.Alignment switch { TextAlignment.Center => "center", TextAlignment.Right => "right", TextAlignment.Justify => "both", _ => null };
            if (jc is not null) properties.Append($"<w:jc w:val=\"{jc}\"/>");
            if (properties.Length > 0) body.Append("<w:pPr>").Append(properties).Append("</w:pPr>");
            if (p.Prefix is not null) body.Append(Run(p.Prefix, default));
            foreach (var run in p.Runs) body.Append(Run(run.Text, run.Style));
            body.Append("</w:p>");
        }
        Entry("word/document.xml", $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body>{body}<w:sectPr><w:pgSz w:w="11906" w:h="16838"/><w:pgMar w:top="1134" w:right="1134" w:bottom="1134" w:left="1134" w:header="709" w:footer="709" w:gutter="0"/></w:sectPr></w:body></w:document>
            """);
    }

    static string Run(string text, RunStyle s)
    {
        var properties = new StringBuilder();
        if (s.Code) properties.Append("<w:rFonts w:ascii=\"Consolas\" w:hAnsi=\"Consolas\" w:cs=\"Consolas\"/>");
        else if (s.Font is { } font) properties.Append($"<w:rFonts w:ascii=\"{Xml.Escape(font)}\" w:hAnsi=\"{Xml.Escape(font)}\" w:cs=\"{Xml.Escape(font)}\"/>");
        if (s.Bold) properties.Append("<w:b/>");
        if (s.Italic) properties.Append("<w:i/>");
        if (s.Strike) properties.Append("<w:strike/>");
        if (s.Color is { } color) properties.Append($"<w:color w:val=\"{color}\"/>");
        if (s.Size is { } size) properties.Append($"<w:sz w:val=\"{(int)Math.Round(size * 2)}\"/><w:szCs w:val=\"{(int)Math.Round(size * 2)}\"/>");
        if (s.Underline) properties.Append("<w:u w:val=\"single\"/>");
        var content = new StringBuilder();
        var buffer = new StringBuilder();
        void Flush()
        {
            if (buffer.Length == 0) return;
            content.Append($"<w:t xml:space=\"preserve\">{Xml.Escape(buffer.ToString())}</w:t>");
            buffer.Clear();
        }
        foreach (var c in text)
        {
            if (c == '\t') { Flush(); content.Append("<w:tab/>"); }
            else if (c == '\n') { Flush(); content.Append("<w:br/>"); }
            else buffer.Append(c);
        }
        Flush();
        return properties.Length > 0 ? $"<w:r><w:rPr>{properties}</w:rPr>{content}</w:r>" : $"<w:r>{content}</w:r>";
    }

    public static TextDocument Read(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var main = zip.GetEntry("word/document.xml") ?? throw ConversionException.Unreadable(Path.GetFileName(path));
        XDocument xml;
        using (var stream = main.Open()) xml = XDocument.Load(stream);
        var headings = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (zip.GetEntry("word/styles.xml") is { } stylesEntry)
        {
            using var stream = stylesEntry.Open();
            foreach (var style in XDocument.Load(stream).Descendants(w + "style"))
            {
                var id = (string?)style.Attribute(w + "styleId");
                var name = ((string?)style.Element(w + "name")?.Attribute(w + "val") ?? "").ToLowerInvariant();
                if (id is null) continue;
                var outline = (string?)style.Element(w + "pPr")?.Element(w + "outlineLvl")?.Attribute(w + "val");
                if (name.StartsWith("heading ") && int.TryParse(name[8..], out var level)) headings[id] = Math.Clamp(level, 1, 6);
                else if (name == "title") headings[id] = 1;
                else if (outline is not null && int.TryParse(outline, out var lvl) && lvl < 6) headings[id] = lvl + 1;
            }
        }
        var document = new TextDocument();
        var body = xml.Root?.Element(w + "body");
        if (body is not null) ReadBlocks(document, body, headings);
        return document;
    }

    static void ReadBlocks(TextDocument document, XElement container, Dictionary<string, int> headings)
    {
        foreach (var element in container.Elements())
        {
            if (element.Name == w + "p")
            {
                document.Paragraphs.Add(ReadParagraph(element, headings));
            }
            else if (element.Name == w + "tbl")
            {
                foreach (var row in element.Elements(w + "tr"))
                {
                    var p = new DocParagraph();
                    var first = true;
                    foreach (var cell in row.Elements(w + "tc"))
                    {
                        if (!first) p.Add("\t", default);
                        first = false;
                        var cellParagraphs = cell.Elements(w + "p").Select(e => ReadParagraph(e, headings)).ToList();
                        for (var i = 0; i < cellParagraphs.Count; i++)
                        {
                            if (i > 0) p.Add(" ", default);
                            foreach (var run in cellParagraphs[i].Runs) p.Add(run.Text, run.Style);
                        }
                    }
                    document.Paragraphs.Add(p);
                }
            }
            else if (element.Name == w + "sdt")
            {
                if (element.Element(w + "sdtContent") is { } content) ReadBlocks(document, content, headings);
            }
        }
    }

    static bool On(XElement? toggle)
    {
        if (toggle is null) return false;
        var value = (string?)toggle.Attribute(w + "val");
        return value is null || (value != "0" && value != "false" && value != "none");
    }

    static DocParagraph ReadParagraph(XElement element, Dictionary<string, int> headings)
    {
        var p = new DocParagraph();
        var properties = element.Element(w + "pPr");
        if (properties is not null)
        {
            var style = (string?)properties.Element(w + "pStyle")?.Attribute(w + "val");
            if (style is not null && headings.TryGetValue(style, out var level)) p.Heading = level;
            p.Alignment = (string?)properties.Element(w + "jc")?.Attribute(w + "val") switch
            {
                "center" => TextAlignment.Center,
                "right" or "end" => TextAlignment.Right,
                "both" or "distribute" => TextAlignment.Justify,
                _ => TextAlignment.Left,
            };
            if (properties.Element(w + "numPr") is not null) p.Prefix = "• ";
        }
        ReadInlines(p, element);
        return p;
    }

    static void ReadInlines(DocParagraph p, XElement container)
    {
        foreach (var child in container.Elements())
        {
            var name = child.Name.LocalName;
            if (child.Name.Namespace != w) continue;
            if (name == "r") ReadRun(p, child);
            else if (name is "hyperlink" or "ins" or "smartTag" or "fldSimple" or "customXml" or "sdtContent") ReadInlines(p, child);
            else if (name == "sdt" && child.Element(w + "sdtContent") is { } content) ReadInlines(p, content);
        }
    }

    static void ReadRun(DocParagraph p, XElement run)
    {
        var rPr = run.Element(w + "rPr");
        var style = default(RunStyle);
        if (rPr is not null)
        {
            var size = (string?)rPr.Element(w + "sz")?.Attribute(w + "val");
            var color = (string?)rPr.Element(w + "color")?.Attribute(w + "val");
            var font = (string?)rPr.Element(w + "rFonts")?.Attribute(w + "ascii");
            var code = font is not null && (font.Contains("Consolas", StringComparison.OrdinalIgnoreCase) || font.Contains("Courier", StringComparison.OrdinalIgnoreCase));
            style = new RunStyle(On(rPr.Element(w + "b")), On(rPr.Element(w + "i")), On(rPr.Element(w + "u")), On(rPr.Element(w + "strike")) || On(rPr.Element(w + "dstrike")),
                                 size is not null && double.TryParse(size, NumberStyles.Float, CultureInfo.InvariantCulture, out var half) ? half / 2 : null,
                                 color is not null && color.Length == 6 && !color.Equals("000000") && !color.Equals("auto", StringComparison.OrdinalIgnoreCase) ? color.ToUpperInvariant() : null,
                                 code ? null : font, code);
            if (style.Size is { } s && Math.Abs(s - TextDocument.BodySize) < 0.25) style = style with { Size = null };
        }
        foreach (var child in run.Elements())
        {
            switch (child.Name.LocalName)
            {
                case "t": p.Add(child.Value, style); break;
                case "tab": p.Add("\t", style); break;
                case "br" or "cr": p.Add("\n", style); break;
                case "noBreakHyphen": p.Add("-", style); break;
            }
        }
    }
}

/// <summary>OpenDocument .odt read and written directly, with the same features as <see cref="Docx"/>.</summary>
public static class Odt
{
    static readonly XNamespace office = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
    static readonly XNamespace style = "urn:oasis:names:tc:opendocument:xmlns:style:1.0";
    static readonly XNamespace text = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
    static readonly XNamespace fo = "urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0";
    static readonly XNamespace table = "urn:oasis:names:tc:opendocument:xmlns:table:1.0";

    const string Namespaces = "xmlns:office=\"urn:oasis:names:tc:opendocument:xmlns:office:1.0\" xmlns:style=\"urn:oasis:names:tc:opendocument:xmlns:style:1.0\" " +
                              "xmlns:text=\"urn:oasis:names:tc:opendocument:xmlns:text:1.0\" xmlns:table=\"urn:oasis:names:tc:opendocument:xmlns:table:1.0\" " +
                              "xmlns:fo=\"urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0\" office:version=\"1.3\"";

    public static void Write(TextDocument document, string path)
    {
        using var file = new FileStream(path, FileMode.Create);
        using var zip = new ZipArchive(file, ZipArchiveMode.Create);
        void Entry(string name, string content, CompressionLevel level = CompressionLevel.Optimal)
        {
            using var writer = new StreamWriter(zip.CreateEntry(name, level).Open(), new UTF8Encoding(false));
            writer.Write(content);
        }
        // The mimetype entry must come first and be stored uncompressed.
        Entry("mimetype", "application/vnd.oasis.opendocument.text", CompressionLevel.NoCompression);
        Entry("META-INF/manifest.xml", """
            <?xml version="1.0" encoding="UTF-8"?>
            <manifest:manifest xmlns:manifest="urn:oasis:names:tc:opendocument:xmlns:manifest:1.0" manifest:version="1.3">
            <manifest:file-entry manifest:full-path="/" manifest:media-type="application/vnd.oasis.opendocument.text"/>
            <manifest:file-entry manifest:full-path="content.xml" manifest:media-type="text/xml"/>
            <manifest:file-entry manifest:full-path="styles.xml" manifest:media-type="text/xml"/>
            </manifest:manifest>
            """);
        var headingStyles = new StringBuilder();
        for (var level = 1; level <= 6; level++)
            headingStyles.Append($"""<style:style style:name="Heading_20_{level}" style:display-name="Heading {level}" style:family="paragraph" style:default-outline-level="{level}"><style:paragraph-properties fo:margin-top="0.42cm" fo:margin-bottom="0.14cm" fo:keep-with-next="always"/><style:text-properties fo:font-size="{Xml.N(TextDocument.HeadingSize(level))}pt" fo:font-weight="bold"/></style:style>""");
        Entry("styles.xml", $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <office:document-styles {Namespaces}><office:styles>
            <style:default-style style:family="paragraph"><style:paragraph-properties fo:margin-bottom="0.21cm"/><style:text-properties style:font-name="Calibri" fo:font-family="Calibri" fo:font-size="11pt" fo:language="tr" fo:country="TR"/></style:default-style>
            <style:style style:name="Standard" style:family="paragraph"/>
            {headingStyles}
            </office:styles></office:document-styles>
            """);

        var textStyles = new Dictionary<RunStyle, string>();
        var paragraphStyles = new Dictionary<TextAlignment, string>();
        var body = new StringBuilder();
        foreach (var p in document.Paragraphs)
        {
            var alignName = "";
            if (p.Alignment != TextAlignment.Left)
            {
                if (!paragraphStyles.TryGetValue(p.Alignment, out alignName))
                {
                    alignName = $"P{paragraphStyles.Count + 1}";
                    paragraphStyles[p.Alignment] = alignName;
                }
            }
            var tag = p.Heading > 0 ? "text:h" : "text:p";
            var styleName = alignName.Length > 0 ? alignName : p.Heading > 0 ? $"Heading_20_{p.Heading}" : "Standard";
            body.Append($"<{tag} text:style-name=\"{styleName}\"{(p.Heading > 0 ? $" text:outline-level=\"{p.Heading}\"" : "")}>");
            if (p.Prefix is not null) body.Append(Content(p.Prefix));
            foreach (var run in p.Runs)
            {
                if (run.Style == default)
                {
                    body.Append(Content(run.Text));
                    continue;
                }
                if (!textStyles.TryGetValue(run.Style, out var name))
                {
                    name = $"T{textStyles.Count + 1}";
                    textStyles[run.Style] = name;
                }
                body.Append($"<text:span text:style-name=\"{name}\">{Content(run.Text)}</text:span>");
            }
            body.Append($"</{tag}>");
        }
        var automatic = new StringBuilder();
        foreach (var (alignment, name) in paragraphStyles)
        {
            var value = alignment switch { TextAlignment.Center => "center", TextAlignment.Right => "end", _ => "justify" };
            automatic.Append($"<style:style style:name=\"{name}\" style:family=\"paragraph\" style:parent-style-name=\"Standard\"><style:paragraph-properties fo:text-align=\"{value}\"/></style:style>");
        }
        foreach (var (runStyle, name) in textStyles)
        {
            var properties = new StringBuilder();
            if (runStyle.Bold) properties.Append(" fo:font-weight=\"bold\"");
            if (runStyle.Italic) properties.Append(" fo:font-style=\"italic\"");
            if (runStyle.Underline) properties.Append(" style:text-underline-style=\"solid\" style:text-underline-width=\"auto\" style:text-underline-color=\"font-color\"");
            if (runStyle.Strike) properties.Append(" style:text-line-through-style=\"solid\"");
            if (runStyle.Size is { } size) properties.Append($" fo:font-size=\"{Xml.N(size)}pt\"");
            if (runStyle.Color is { } color) properties.Append($" fo:color=\"#{color}\"");
            var font = runStyle.Code ? "Consolas" : runStyle.Font;
            if (font is not null) properties.Append($" style:font-name=\"{Xml.Escape(font)}\" fo:font-family=\"{Xml.Escape(font)}\"");
            automatic.Append($"<style:style style:name=\"{name}\" style:family=\"text\"><style:text-properties{properties}/></style:style>");
        }
        Entry("content.xml", $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <office:document-content {Namespaces}><office:automatic-styles>{automatic}</office:automatic-styles><office:body><office:text>{body}</office:text></office:body></office:document-content>
            """);
    }

    /// <summary>Text with ODF's whitespace elements: repeated spaces, tabs and line breaks.</summary>
    static string Content(string value)
    {
        var output = new StringBuilder();
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c == '\t') output.Append("<text:tab/>");
            else if (c == '\n') output.Append("<text:line-break/>");
            else if (c == ' ' && (i == 0 || value[i - 1] == ' ' || i == value.Length - 1))
            {
                var count = 1;
                while (i + 1 < value.Length && value[i + 1] == ' ') { count++; i++; }
                output.Append(count == 1 ? "<text:s/>" : $"<text:s text:c=\"{count}\"/>");
            }
            else output.Append(Xml.Escape(c.ToString()));
        }
        return output.ToString();
    }

    public static TextDocument Read(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var entry = zip.GetEntry("content.xml") ?? throw ConversionException.Unreadable(Path.GetFileName(path));
        XDocument content;
        using (var stream = entry.Open()) content = XDocument.Load(stream);
        var styles = new Dictionary<string, (RunStyle run, TextAlignment align, int heading)>();
        void Collect(XElement? root)
        {
            if (root is null) return;
            foreach (var element in root.Descendants(style + "style"))
            {
                var name = (string?)element.Attribute(style + "name");
                if (name is null) continue;
                var parent = (string?)element.Attribute(style + "parent-style-name");
                var inherited = parent is not null && styles.TryGetValue(parent, out var p) ? p : (default(RunStyle), TextAlignment.Left, 0);
                var heading = (int?)element.Attribute(style + "default-outline-level") ?? inherited.Item3;
                if (heading == 0 && name.StartsWith("Heading_20_") && int.TryParse(name[11..], out var level)) heading = level;
                var t = element.Element(style + "text-properties");
                var run = inherited.Item1;
                if (t is not null)
                {
                    var size = (string?)t.Attribute(fo + "font-size");
                    double? points = size is not null && size.EndsWith("pt") && double.TryParse(size[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : run.Size;
                    var color = ((string?)t.Attribute(fo + "color"))?.TrimStart('#');
                    var font = (string?)t.Attribute(style + "font-name") ?? (string?)t.Attribute(fo + "font-family");
                    var code = font is not null && (font.Contains("Consolas") || font.Contains("Courier") || font.Contains("Mono"));
                    run = new RunStyle(
                        ((string?)t.Attribute(fo + "font-weight")) is { } weight ? weight is "bold" or "600" or "700" or "800" or "900" : run.Bold,
                        ((string?)t.Attribute(fo + "font-style")) is { } italic ? italic is "italic" or "oblique" : run.Italic,
                        ((string?)t.Attribute(style + "text-underline-style")) is { } underline ? underline != "none" : run.Underline,
                        ((string?)t.Attribute(style + "text-line-through-style")) is { } strike ? strike != "none" : run.Strike,
                        points is { } pt && Math.Abs(pt - TextDocument.BodySize) < 0.25 ? null : points,
                        color is { Length: 6 } && color != "000000" ? color.ToUpperInvariant() : run.Color,
                        code ? null : font?.Trim('\'') ?? run.Font, code || run.Code);
                }
                var align = (string?)element.Element(style + "paragraph-properties")?.Attribute(fo + "text-align") switch
                {
                    "center" => TextAlignment.Center,
                    "end" or "right" => TextAlignment.Right,
                    "justify" => TextAlignment.Justify,
                    null => inherited.Item2,
                    _ => TextAlignment.Left,
                };
                styles[name] = (run, align, heading);
            }
        }
        if (zip.GetEntry("styles.xml") is { } stylesEntry)
        {
            using var stream = stylesEntry.Open();
            Collect(XDocument.Load(stream).Root);
        }
        Collect(content.Root?.Element(office + "automatic-styles"));
        var document = new TextDocument();
        var body = content.Root?.Element(office + "body")?.Element(office + "text");
        if (body is not null) ReadBlocks(document, body, styles, null);
        return document;
    }

    static void ReadBlocks(TextDocument document, XElement container, Dictionary<string, (RunStyle run, TextAlignment align, int heading)> styles, string? prefix)
    {
        foreach (var element in container.Elements())
        {
            if (element.Name == text + "p" || element.Name == text + "h")
            {
                var name = (string?)element.Attribute(text + "style-name");
                var found = name is not null && styles.TryGetValue(name, out var s) ? s : (default(RunStyle), TextAlignment.Left, 0);
                var p = new DocParagraph { Alignment = found.Item2, Prefix = prefix };
                if (element.Name == text + "h") p.Heading = Math.Clamp((int?)element.Attribute(text + "outline-level") ?? (found.Item3 > 0 ? found.Item3 : 1), 1, 6);
                else if (found.Item3 > 0) p.Heading = found.Item3;
                ReadInlines(p, element, found.Item1 with { Bold = p.Heading > 0 ? false : found.Item1.Bold, Size = p.Heading > 0 ? null : found.Item1.Size }, styles);
                document.Paragraphs.Add(p);
                prefix = null;
            }
            else if (element.Name == text + "list")
            {
                foreach (var item in element.Elements(text + "list-item")) ReadBlocks(document, item, styles, "• ");
            }
            else if (element.Name == text + "section")
            {
                ReadBlocks(document, element, styles, prefix);
            }
            else if (element.Name == table + "table")
            {
                foreach (var row in element.Descendants(table + "table-row"))
                {
                    var p = new DocParagraph();
                    var first = true;
                    foreach (var cell in row.Elements(table + "table-cell"))
                    {
                        if (!first) p.Add("\t", default);
                        first = false;
                        var cellDocument = new TextDocument();
                        ReadBlocks(cellDocument, cell, styles, null);
                        for (var i = 0; i < cellDocument.Paragraphs.Count; i++)
                        {
                            if (i > 0) p.Add(" ", default);
                            foreach (var run in cellDocument.Paragraphs[i].Runs) p.Add(run.Text, run.Style);
                        }
                    }
                    document.Paragraphs.Add(p);
                }
            }
        }
    }

    static void ReadInlines(DocParagraph p, XElement container, RunStyle current, Dictionary<string, (RunStyle run, TextAlignment align, int heading)> styles)
    {
        foreach (var node in container.Nodes())
        {
            if (node is XText value)
            {
                p.Add(value.Value, current);
                continue;
            }
            if (node is not XElement element) continue;
            if (element.Name == text + "span" || element.Name == text + "a")
            {
                var name = (string?)element.Attribute(text + "style-name");
                var next = name is not null && styles.TryGetValue(name, out var s) ? Merge(current, s.run) : current;
                if (element.Name == text + "a") next = next with { Underline = true, Color = next.Color ?? "0563C1" };
                ReadInlines(p, element, next, styles);
            }
            else if (element.Name == text + "s")
            {
                p.Add(new string(' ', (int?)element.Attribute(text + "c") ?? 1), current);
            }
            else if (element.Name == text + "tab")
            {
                p.Add("\t", current);
            }
            else if (element.Name == text + "line-break")
            {
                p.Add("\n", current);
            }
        }
    }

    static RunStyle Merge(RunStyle outer, RunStyle inner) => new(
        outer.Bold || inner.Bold, outer.Italic || inner.Italic, outer.Underline || inner.Underline, outer.Strike || inner.Strike,
        inner.Size ?? outer.Size, inner.Color ?? outer.Color, inner.Font ?? outer.Font, outer.Code || inner.Code);
}
