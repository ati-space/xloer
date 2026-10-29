using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;

namespace FastShot.Core.Convert;

/// <summary>HTML in (a forgiving tag scanner, not a browser) and out (clean semantic markup).</summary>
public static partial class Html
{
    public static string Write(TextDocument document, bool forPrint = false)
    {
        var html = new StringBuilder();
        html.Append("<!DOCTYPE html>\n<html lang=\"tr\">\n<head>\n<meta charset=\"utf-8\">\n");
        html.Append($"<title>{WebUtility.HtmlEncode(document.Title ?? "")}</title>\n<style>\n");
        html.Append("body { font-family: Calibri, 'Segoe UI', sans-serif; font-size: 11pt; line-height: 1.35; color: #111; ");
        html.Append(forPrint ? "margin: 0; }\n@page { size: A4; margin: 20mm; }\n" : "max-width: 46em; margin: 2em auto; padding: 0 1em; }\n");
        html.Append("p, li { white-space: pre-wrap; margin: 0 0 0.45em; }\nh1, h2, h3, h4, h5, h6 { margin: 0.9em 0 0.35em; }\ncode { font-family: Consolas, monospace; }\n</style>\n</head>\n<body>\n");
        string? openList = null;
        foreach (var p in document.Paragraphs)
        {
            var list = p.IsBullet ? "ul" : p.IsNumbered ? "ol" : null;
            if (list != openList)
            {
                if (openList is not null) html.Append($"</{openList}>\n");
                if (list is not null) html.Append($"<{list}>\n");
                openList = list;
            }
            var tag = list is not null ? "li" : p.Heading > 0 ? $"h{p.Heading}" : "p";
            var align = p.Alignment switch
            {
                TextAlignment.Center => " style=\"text-align:center\"",
                TextAlignment.Right => " style=\"text-align:right\"",
                TextAlignment.Justify => " style=\"text-align:justify\"",
                _ => "",
            };
            html.Append($"<{tag}{align}>");
            if (p.Prefix is not null && list is null) html.Append(WebUtility.HtmlEncode(p.Prefix));
            if (p.Runs.Count == 0) html.Append(list is null && p.Heading == 0 ? "<br>" : "");
            foreach (var run in p.Runs) html.Append(Inline(run));
            html.Append($"</{tag}>\n");
        }
        if (openList is not null) html.Append($"</{openList}>\n");
        html.Append("</body>\n</html>\n");
        return html.ToString();
    }

    static string Inline(DocRun run)
    {
        var text = WebUtility.HtmlEncode(run.Text).Replace("\n", "<br>");
        var s = run.Style;
        var css = new List<string>();
        if (s.Size is { } size) css.Add($"font-size:{Xml.N(size)}pt");
        if (s.Color is { } color) css.Add($"color:#{color}");
        if (s.Font is { } font) css.Add($"font-family:'{WebUtility.HtmlEncode(font)}'");
        if (css.Count > 0) text = $"<span style=\"{string.Join(";", css)}\">{text}</span>";
        if (s.Code) text = $"<code>{text}</code>";
        if (s.Strike) text = $"<s>{text}</s>";
        if (s.Underline) text = $"<u>{text}</u>";
        if (s.Italic) text = $"<em>{text}</em>";
        if (s.Bold) text = $"<strong>{text}</strong>";
        return text;
    }

    // MARK: Reading

    static readonly HashSet<string> blocks =
    [
        "p", "div", "h1", "h2", "h3", "h4", "h5", "h6", "li", "tr", "blockquote", "pre", "section", "article", "header", "footer",
        "main", "nav", "aside", "table", "ul", "ol", "dd", "dt", "dl", "figure", "figcaption", "hr", "address", "center", "body",
    ];
    static readonly HashSet<string> skipped = ["head", "script", "style", "noscript", "template", "svg", "iframe", "object"];

    [GeneratedRegex(@"<!--.*?-->|<!\[CDATA\[.*?\]\]>|<(/?)([a-zA-Z][a-zA-Z0-9]*)((?:[^>""']|""[^""]*""|'[^']*')*)>|<![^>]*>|<\?[^>]*>", RegexOptions.Singleline)]
    private static partial Regex TagPattern();

    [GeneratedRegex(@"([a-zA-Z-]+)\s*=\s*(""[^""]*""|'[^']*'|[^\s>]+)")]
    private static partial Regex AttributePattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    sealed class Frame(string tag, RunStyle style)
    {
        public string Tag { get; } = tag;
        public RunStyle Style { get; } = style;
    }

    public static TextDocument Read(string source)
    {
        var document = new TextDocument();
        var stack = new List<Frame>();
        var lists = new Stack<(bool ordered, int counter)>();
        DocParagraph? paragraph = null;
        var heading = 0;
        var align = TextAlignment.Left;
        var pre = 0;
        var skipping = 0;
        var cellStarted = false;
        string? title = null;
        var inTitle = false;

        RunStyle Current() => stack.Count == 0 ? default : stack[^1].Style;

        void Close()
        {
            if (paragraph is null) return;
            // Trim the trailing space left by collapsed whitespace.
            if (paragraph.Runs.Count > 0 && pre == 0)
            {
                var last = paragraph.Runs[^1];
                paragraph.Runs[^1] = last with { Text = last.Text.TrimEnd(' ') };
                if (paragraph.Runs[^1].Text.Length == 0) paragraph.Runs.RemoveAt(paragraph.Runs.Count - 1);
            }
            if (paragraph.Runs.Count > 0 || paragraph.Prefix is not null) document.Paragraphs.Add(paragraph);
            paragraph = null;
        }

        DocParagraph Open()
        {
            paragraph ??= new DocParagraph { Heading = heading, Alignment = align };
            return paragraph;
        }

        void Text(string raw)
        {
            var text = WebUtility.HtmlDecode(raw);
            if (pre == 0)
            {
                text = Whitespace().Replace(text, " ");
                if (paragraph is null || paragraph.Runs.Count == 0 || paragraph.Text.EndsWith(' ') || paragraph.Text.EndsWith('\n') || paragraph.Text.EndsWith('\t'))
                    text = text.TrimStart(' ');
                if (text.Length == 0) return;
            }
            else
            {
                text = text.Replace("\r\n", "\n");
                var lines = text.Split('\n');
                for (var i = 0; i < lines.Length; i++)
                {
                    if (i > 0)
                    {
                        Close();
                    }
                    if (lines[i].Length > 0) Open().Add(lines[i], Current() with { Code = true });
                }
                return;
            }
            Open().Add(text, Current());
        }

        var position = 0;
        foreach (Match match in TagPattern().Matches(source))
        {
            var between = source[position..match.Index];
            position = match.Index + match.Length;
            if (between.Length > 0)
            {
                if (inTitle) title = (title ?? "") + WebUtility.HtmlDecode(between);
                else if (skipping == 0) Text(between);
            }
            if (!match.Groups[2].Success) continue;
            var closing = match.Groups[1].Value == "/";
            var tag = match.Groups[2].Value.ToLowerInvariant();
            var attributes = match.Groups[3].Value;
            var selfClosing = attributes.TrimEnd().EndsWith('/');

            if (tag == "title") { inTitle = !closing; continue; }
            if (skipped.Contains(tag))
            {
                if (!selfClosing) skipping += closing ? -1 : 1;
                skipping = Math.Max(0, skipping);
                continue;
            }
            if (skipping > 0) continue;

            if (tag == "br") { Open().Add("\n", Current()); continue; }
            if (tag is "td" or "th")
            {
                if (!closing)
                {
                    if (cellStarted) Open().Add("\t", Current());
                    cellStarted = true;
                }
                continue;
            }
            if (tag == "img" && !closing)
            {
                var alt = Attribute(attributes, "alt");
                if (!string.IsNullOrWhiteSpace(alt)) Open().Add(alt, Current());
                continue;
            }

            if (blocks.Contains(tag))
            {
                Close();
                if (tag is "ul" or "ol")
                {
                    if (closing) { if (lists.Count > 0) lists.Pop(); }
                    else lists.Push((tag == "ol", 0));
                    continue;
                }
                if (tag == "tr") cellStarted = false;
                if (tag == "pre") pre = Math.Max(0, pre + (closing ? -1 : 1));
                if (tag.Length == 2 && tag[0] == 'h' && char.IsDigit(tag[1])) heading = closing ? 0 : tag[1] - '0';
                if (!closing)
                {
                    var style = Attribute(attributes, "style") ?? "";
                    align = AlignmentOf(style, Attribute(attributes, "align")) ?? (tag == "center" ? TextAlignment.Center : TextAlignment.Left);
                    if (tag == "li")
                    {
                        var (ordered, counter) = lists.Count > 0 ? lists.Pop() : (false, 0);
                        counter++;
                        lists.Push((ordered, counter));
                        Open().Prefix = ordered ? $"{counter}. " : "• ";
                    }
                }
                else
                {
                    align = TextAlignment.Left;
                }
                if (tag is "blockquote") Push(stack, tag, closing, s => s with { Italic = true });
                continue;
            }

            var cssText = Attribute(attributes, "style");
            switch (tag)
            {
                case "b" or "strong": Push(stack, tag, closing, s => s with { Bold = true }); break;
                case "i" or "em" or "cite" or "var" or "dfn": Push(stack, tag, closing, s => s with { Italic = true }); break;
                case "u" or "ins": Push(stack, tag, closing, s => s with { Underline = true }); break;
                case "s" or "strike" or "del": Push(stack, tag, closing, s => s with { Strike = true }); break;
                case "code" or "kbd" or "samp" or "tt": Push(stack, tag, closing, s => s with { Code = true }); break;
                case "a": Push(stack, tag, closing, s => s with { Underline = true, Color = "0563C1" }); break;
                case "font":
                {
                    var color = ColorOf(Attribute(attributes, "color"));
                    Push(stack, tag, closing, s => color is null ? s : s with { Color = color });
                    break;
                }
                default:
                    if (cssText is not null || closing) Push(stack, tag, closing, s => ApplyCss(s, cssText ?? ""));
                    break;
            }
        }
        if (position < source.Length && skipping == 0) Text(source[position..]);
        Close();
        document.Title = title?.Trim();
        return document;
    }

    static void Push(List<Frame> stack, string tag, bool closing, Func<RunStyle, RunStyle> change)
    {
        if (closing)
        {
            var index = stack.FindLastIndex(f => f.Tag == tag);
            if (index >= 0) stack.RemoveRange(index, stack.Count - index);
            return;
        }
        var current = stack.Count == 0 ? default : stack[^1].Style;
        stack.Add(new Frame(tag, change(current)));
    }

    static string? Attribute(string attributes, string name)
    {
        foreach (Match match in AttributePattern().Matches(attributes))
        {
            if (!match.Groups[1].Value.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
            var value = match.Groups[2].Value;
            if (value.Length >= 2 && (value[0] == '"' || value[0] == '\'')) value = value[1..^1];
            return WebUtility.HtmlDecode(value);
        }
        return null;
    }

    static TextAlignment? AlignmentOf(string css, string? align)
    {
        var value = (Css(css, "text-align") ?? align ?? "").ToLowerInvariant();
        return value switch
        {
            "center" => TextAlignment.Center,
            "right" => TextAlignment.Right,
            "justify" => TextAlignment.Justify,
            _ => null,
        };
    }

    static string? Css(string css, string property)
    {
        foreach (var declaration in css.Split(';'))
        {
            var parts = declaration.Split(':', 2);
            if (parts.Length == 2 && parts[0].Trim().Equals(property, StringComparison.OrdinalIgnoreCase)) return parts[1].Trim();
        }
        return null;
    }

    static RunStyle ApplyCss(RunStyle style, string css)
    {
        if (Css(css, "font-weight") is { } weight && (weight.Contains("bold") || (int.TryParse(weight, out var w) && w >= 600))) style = style with { Bold = true };
        if (Css(css, "font-style") is { } italic && italic.Contains("italic")) style = style with { Italic = true };
        if (Css(css, "text-decoration") is { } decoration)
        {
            if (decoration.Contains("underline")) style = style with { Underline = true };
            if (decoration.Contains("line-through")) style = style with { Strike = true };
        }
        if (ColorOf(Css(css, "color")) is { } color) style = style with { Color = color };
        if (Css(css, "font-size") is { } size && SizeOf(size) is { } points) style = style with { Size = points };
        return style;
    }

    static double? SizeOf(string value)
    {
        value = value.Trim().ToLowerInvariant();
        double Number(string text) => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : 0;
        if (value.EndsWith("pt")) return Number(value[..^2]);
        if (value.EndsWith("px")) return Math.Round(Number(value[..^2]) * 0.75 * 2) / 2;
        if (value.EndsWith("em") || value.EndsWith("rem")) return Math.Round(Number(value.TrimEnd('m', 'e', 'r')) * TextDocument.BodySize * 2) / 2;
        return null;
    }

    static string? ColorOf(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        if (value.StartsWith('#'))
        {
            var hex = value[1..];
            if (hex.Length == 3) hex = string.Concat(hex.Select(c => $"{c}{c}"));
            return hex.Length >= 6 ? hex[..6].ToUpperInvariant() : null;
        }
        if (value.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
        {
            var numbers = value[(value.IndexOf('(') + 1)..].TrimEnd(')').Split(',').Select(n => int.TryParse(n.Trim(), out var v) ? Math.Clamp(v, 0, 255) : 0).ToArray();
            return numbers.Length >= 3 ? $"{numbers[0]:X2}{numbers[1]:X2}{numbers[2]:X2}" : null;
        }
        try
        {
            if (System.Windows.Media.ColorConverter.ConvertFromString(value) is System.Windows.Media.Color named) return $"{named.R:X2}{named.G:X2}{named.B:X2}";
        }
        catch { /* unknown colour name */ }
        return null;
    }
}
