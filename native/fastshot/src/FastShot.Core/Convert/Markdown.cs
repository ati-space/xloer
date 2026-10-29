using System.Text.RegularExpressions;

namespace FastShot.Core.Convert;

/// <summary>Markdown in: headings, lists, quotes, code blocks and inline bold/italic/code/strike/links. Line breaks are kept.</summary>
public static partial class Markdown
{
    [GeneratedRegex(@"^(#{1,6})\s+(.*?)\s*#*\s*$")]
    private static partial Regex HeadingPattern();

    [GeneratedRegex(@"^(\s*)([-*+]|\d+[.)])\s+(.*)$")]
    private static partial Regex ListPattern();

    [GeneratedRegex(@"^\s*([-*_])(\s*\1){2,}\s*$")]
    private static partial Regex RulePattern();

    public static TextDocument Read(string source)
    {
        var document = new TextDocument();
        var lines = source.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        DocParagraph? paragraph = null;
        var fence = false;
        var quote = false;

        void Close()
        {
            if (paragraph is not null) document.Paragraphs.Add(paragraph);
            paragraph = null;
            quote = false;
        }

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            if (line.TrimStart().StartsWith("```") || line.TrimStart().StartsWith("~~~"))
            {
                Close();
                fence = !fence;
                continue;
            }
            if (fence)
            {
                var code = new DocParagraph();
                code.Add(raw.Length == 0 ? " " : raw, new RunStyle(Code: true));
                document.Paragraphs.Add(code);
                continue;
            }
            if (line.Length == 0)
            {
                Close();
                continue;
            }
            if (RulePattern().IsMatch(line))
            {
                Close();
                continue;
            }
            if (HeadingPattern().Match(line) is { Success: true } heading)
            {
                Close();
                var p = new DocParagraph { Heading = heading.Groups[1].Length };
                Inline(p, heading.Groups[2].Value, default);
                document.Paragraphs.Add(p);
                continue;
            }
            if (ListPattern().Match(line) is { Success: true } item)
            {
                Close();
                var marker = item.Groups[2].Value;
                var indent = new string(' ', Math.Min(8, item.Groups[1].Value.Length / 2 * 2));
                paragraph = new DocParagraph { Prefix = indent + (char.IsDigit(marker[0]) ? marker.TrimEnd(')', '.') + ". " : "• ") };
                Inline(paragraph, item.Groups[3].Value, default);
                continue;
            }
            var isQuote = line.TrimStart().StartsWith('>');
            var text = isQuote ? line.TrimStart()[1..].TrimStart() : line;
            if (paragraph is null || isQuote != quote)
            {
                Close();
                paragraph = new DocParagraph();
                quote = isQuote;
            }
            else
            {
                paragraph.Add("\n", default);
            }
            Inline(paragraph, text, isQuote ? new RunStyle(Italic: true) : default);
        }
        Close();
        return document;
    }

    /// <summary>Inline markup: **bold**, __bold__, *italic*, _italic_, `code`, ~~strike~~, [text](url), ![alt](src), \escapes.</summary>
    static void Inline(DocParagraph paragraph, string text, RunStyle style)
    {
        var i = 0;
        var buffer = new System.Text.StringBuilder();
        void Flush()
        {
            if (buffer.Length > 0) paragraph.Add(buffer.ToString(), style);
            buffer.Clear();
        }
        while (i < text.Length)
        {
            var c = text[i];
            if (c == '\\' && i + 1 < text.Length && "\\`*_{}[]()#+-.!~>".Contains(text[i + 1]))
            {
                buffer.Append(text[i + 1]);
                i += 2;
                continue;
            }
            if (c == '`')
            {
                var end = text.IndexOf('`', i + 1);
                if (end > i)
                {
                    Flush();
                    paragraph.Add(text[(i + 1)..end], style with { Code = true });
                    i = end + 1;
                    continue;
                }
            }
            if ((c == '!' && i + 1 < text.Length && text[i + 1] == '[') || c == '[')
            {
                var image = c == '!';
                var open = image ? i + 1 : i;
                var close = text.IndexOf(']', open + 1);
                if (close > open && close + 1 < text.Length && text[close + 1] == '(')
                {
                    var end = text.IndexOf(')', close + 2);
                    if (end > close)
                    {
                        Flush();
                        var label = text[(open + 1)..close];
                        if (image) paragraph.Add(label, style);
                        else Inline(paragraph, label, style with { Underline = true, Color = "0563C1" });
                        i = end + 1;
                        continue;
                    }
                }
            }
            foreach (var (marker, apply) in new (string, Func<RunStyle, RunStyle>)[]
                     {
                         ("**", s => s with { Bold = true }), ("__", s => s with { Bold = true }), ("~~", s => s with { Strike = true }),
                         ("*", s => s with { Italic = true }), ("_", s => s with { Italic = true }),
                     })
            {
                if (string.CompareOrdinal(text, i, marker, 0, marker.Length) != 0) continue;
                // Underscores inside words (snake_case) are not emphasis.
                if (marker[0] == '_' && i > 0 && char.IsLetterOrDigit(text[i - 1])) continue;
                var end = text.IndexOf(marker, i + marker.Length, StringComparison.Ordinal);
                if (end <= i + marker.Length - 1 || end == i + marker.Length) continue;
                if (char.IsWhiteSpace(text[i + marker.Length])) continue;
                Flush();
                Inline(paragraph, text[(i + marker.Length)..end], apply(style));
                i = end + marker.Length;
                goto next;
            }
            buffer.Append(c);
            i++;
        next:;
        }
        Flush();
    }
}
