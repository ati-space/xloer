using System.Text;
using System.Text.RegularExpressions;
using System.Windows;

namespace FastShot.Core.Translate;

/// <summary>Turns recognised lines back into paragraphs before translating. Text recognition returns the lines as they sit
/// on screen, so a sentence that wraps arrives in pieces, and a translator treats each piece as a sentence of its own.</summary>
public static partial class TextReflow
{
    public readonly record struct Line(string Text, Rect Box);

    [GeneratedRegex(@"^\s*(?:[•·▪◦‣*–—-]|\d{1,3}[.)]|[a-zA-Z][.)])\s+")]
    private static partial Regex ListItem();

    /// <summary>Lines with their place on the image: a wide gap, a list item or a short line that ends a sentence starts a
    /// new paragraph; the rest join, and a word split with a hyphen at the end of a line is put back together.</summary>
    public static string Join(IReadOnlyList<Line> lines)
    {
        var useful = lines.Where(l => !string.IsNullOrWhiteSpace(l.Text)).ToList();
        if (useful.Count == 0) return "";
        var heights = useful.Select(l => l.Box.Height).Where(h => h > 0).Order().ToList();
        var lineHeight = heights.Count > 0 ? heights[heights.Count / 2] : 0;
        var right = useful.Max(l => l.Box.Right);
        var paragraphs = new List<StringBuilder> { new(useful[0].Text.Trim()) };
        // A list item that wraps continues indented under its text; a line back at the item's left edge is new text.
        double? itemLeft = ListItem().IsMatch(useful[0].Text) ? useful[0].Box.Left : null;
        for (var i = 1; i < useful.Count; i++)
        {
            var previous = useful[i - 1];
            var line = useful[i];
            var gap = line.Box.Top - previous.Box.Bottom;
            var endsSentence = EndsSentence(previous.Text);
            var shortLine = previous.Box.Right < right - Math.Max(lineHeight * 3, (right - previous.Box.Left) * 0.2);
            var item = ListItem().IsMatch(line.Text);
            var newParagraph = (lineHeight > 0 && gap > lineHeight * 0.6)
                               || item
                               || (itemLeft is { } left && line.Box.Left <= left + lineHeight * 0.5)
                               || (endsSentence && shortLine)
                               || (lineHeight > 0 && Math.Abs(line.Box.Left - previous.Box.Left) > lineHeight * 4);
            if (newParagraph)
            {
                paragraphs.Add(new StringBuilder(line.Text.Trim()));
                itemLeft = item ? line.Box.Left : null;
            }
            else Append(paragraphs[^1], line.Text);
        }
        return string.Join("\n\n", paragraphs.Select(p => p.ToString()));
    }

    /// <summary>Plain text (typed or pasted): blank lines and list items separate paragraphs, other line breaks are wrapping.</summary>
    public static string Join(string text)
    {
        var paragraphs = new List<StringBuilder>();
        var blank = true;
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                blank = true;
                continue;
            }
            if (blank || ListItem().IsMatch(raw)) paragraphs.Add(new StringBuilder(raw.Trim()));
            else Append(paragraphs[^1], raw);
            blank = false;
        }
        return string.Join("\n\n", paragraphs.Select(p => p.ToString()));
    }

    static bool EndsSentence(string text)
    {
        var trimmed = text.TrimEnd();
        return trimmed.Length > 0 && ".!?:;…\"”»)".Contains(trimmed[^1]);
    }

    static void Append(StringBuilder paragraph, string line)
    {
        var next = line.Trim();
        if (next.Length == 0) return;
        // "çevi-" + "rilir" → "çevirilir"; a dash before a capital or a digit is a real dash.
        if (paragraph.Length > 1 && paragraph[^1] == '-' && char.IsLetter(paragraph[^2]) && char.IsLower(next[0]))
        {
            paragraph.Length--;
            paragraph.Append(next);
            return;
        }
        paragraph.Append(' ').Append(next);
    }
}
