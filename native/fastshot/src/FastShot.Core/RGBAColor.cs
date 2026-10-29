using System.Globalization;
using System.Text.Json.Serialization;
using System.Windows.Media;

namespace FastShot.Core;

/// <summary>An sRGB colour that survives JSON round trips without colour-space surprises.
/// Serialised as {"red","green","blue","alpha"} like the macOS version.</summary>
public readonly record struct RGBAColor
{
    [JsonPropertyName("red")] public double R { get; init; }
    [JsonPropertyName("green")] public double G { get; init; }
    [JsonPropertyName("blue")] public double B { get; init; }
    [JsonPropertyName("alpha")] public double A { get; init; }

    public RGBAColor(double red, double green, double blue, double alpha = 1)
    {
        R = red; G = green; B = blue; A = alpha;
    }

    public static RGBAColor? FromHex(string hex)
    {
        var value = hex.Trim();
        if (value.StartsWith('#')) value = value[1..];
        if (value.Length != 6 || !uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var number)) return null;
        return new RGBAColor(((number >> 16) & 0xFF) / 255.0, ((number >> 8) & 0xFF) / 255.0, (number & 0xFF) / 255.0);
    }



    static byte Byte(double v) => (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);

    [JsonIgnore] public Color Color => Color.FromArgb(Byte(A), Byte(R), Byte(G), Byte(B));

    public SolidColorBrush Brush()
    {
        var brush = new SolidColorBrush(Color);
        brush.Freeze();
        return brush;
    }

    public RGBAColor WithAlpha(double alpha) => new(R, G, B, alpha);

    [JsonIgnore] public string Hex => $"#{Byte(R):X2}{Byte(G):X2}{Byte(B):X2}";

    /// <summary>Relative luminance; picks readable ink for text drawn on a filled shape.</summary>
    [JsonIgnore] public double Luminance => 0.2126 * R + 0.7152 * G + 0.0722 * B;
    [JsonIgnore] public RGBAColor Contrasting => Luminance > 0.6 ? Black : White;

    public static readonly RGBAColor White = new(1, 1, 1);
    public static readonly RGBAColor Black = new(0.07, 0.07, 0.08);
    public static readonly RGBAColor Red = new(0.95, 0.19, 0.19);
    public static readonly RGBAColor Orange = new(1.0, 0.55, 0.05);
    public static readonly RGBAColor Yellow = new(1.0, 0.83, 0.0);
    public static readonly RGBAColor Green = new(0.16, 0.74, 0.33);
    public static readonly RGBAColor Blue = new(0.10, 0.47, 1.0);
    public static readonly RGBAColor Purple = new(0.60, 0.32, 0.96);
    public static readonly RGBAColor Pink = new(1.0, 0.25, 0.62);
    public static readonly RGBAColor Gray = new(0.55, 0.56, 0.58);

    public static readonly RGBAColor[] Palette = [Red, Orange, Yellow, Green, Blue, Purple, Pink, Gray, Black, White];
}
