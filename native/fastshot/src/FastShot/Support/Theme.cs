using System.Windows.Media.Effects;
using Microsoft.Win32;

namespace FastShot;

/// <summary>Brushes for one surface: the dark floating HUDs, or the editor in light or dark mode.</summary>
public sealed class UiPalette
{
    public required Brush Foreground { get; init; }
    public required Brush Secondary { get; init; }
    public required Brush Hover { get; init; }
    public required Brush Separator { get; init; }
    public required Brush Background { get; init; }
    public required Brush Panel { get; init; }
    public required Brush Border { get; init; }
    public Brush Accent => Theme.Accent;
    public Brush AccentForeground { get; } = Brushes.White;

    public static UiPalette Hud { get; } = new()
    {
        Foreground = Theme.Frozen(Color.FromRgb(0xF2, 0xF2, 0xF2)),
        Secondary = Theme.Frozen(Color.FromArgb(0xA0, 0xFF, 0xFF, 0xFF)),
        Hover = Theme.Frozen(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)),
        Separator = Theme.Frozen(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
        Background = Theme.Frozen(Color.FromArgb(0xF0, 0x1A, 0x1A, 0x1C)),
        Panel = Theme.Frozen(Color.FromArgb(0xF0, 0x1A, 0x1A, 0x1C)),
        Border = Theme.Frozen(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF)),
    };

    static readonly UiPalette light = new()
    {
        Foreground = Theme.Frozen(Color.FromRgb(0x1B, 0x1B, 0x1F)),
        Secondary = Theme.Frozen(Color.FromRgb(0x6B, 0x6B, 0x72)),
        Hover = Theme.Frozen(Color.FromArgb(0x16, 0x00, 0x00, 0x00)),
        Separator = Theme.Frozen(Color.FromArgb(0x22, 0x00, 0x00, 0x00)),
        Background = Theme.Frozen(Color.FromRgb(0xDB, 0xDB, 0xDE)),
        Panel = Theme.Frozen(Color.FromRgb(0xF7, 0xF7, 0xF8)),
        Border = Theme.Frozen(Color.FromArgb(0x1A, 0x00, 0x00, 0x00)),
    };

    static readonly UiPalette dark = new()
    {
        Foreground = Theme.Frozen(Color.FromRgb(0xF0, 0xF0, 0xF2)),
        Secondary = Theme.Frozen(Color.FromRgb(0xA0, 0xA0, 0xA8)),
        Hover = Theme.Frozen(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
        Separator = Theme.Frozen(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)),
        Background = Theme.Frozen(Color.FromRgb(0x1C, 0x1C, 0x1E)),
        Panel = Theme.Frozen(Color.FromRgb(0x2A, 0x2A, 0x2D)),
        Border = Theme.Frozen(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF)),
    };

    public static UiPalette Editor => Theme.IsDark ? dark : light;
}

public static class Theme
{
    public static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");
    public static readonly FontFamily UiFont = new("Segoe UI Variable Text, Segoe UI");

    public static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public static bool IsDark
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
            }
            catch { return false; }
        }
    }

    static Color? accent;

    public static Color AccentColor
    {
        get
        {
            if (accent is { } cached) return cached;
            var color = Color.FromRgb(0x00, 0x67, 0xC0);
            try
            {
                var value = new Windows.UI.ViewManagement.UISettings().GetColorValue(Windows.UI.ViewManagement.UIColorType.Accent);
                // A grey accent would make the selected tool look like a hovered one; keep the blue then.
                int max = Math.Max(value.R, Math.Max(value.G, value.B)), min = Math.Min(value.R, Math.Min(value.G, value.B));
                var saturation = max == 0 ? 0 : (max - min) / (double)max;
                if (saturation >= 0.3 && max >= 60) color = Color.FromRgb(value.R, value.G, value.B);
            }
            catch { /* keep the default blue */ }
            accent = color;
            return color;
        }
    }

    public static Brush Accent { get; } = Frozen(AccentColor);
    public static Brush Red { get; } = Frozen(Color.FromRgb(0xE8, 0x2F, 0x34));
    public static Brush Yellow { get; } = Frozen(Color.FromRgb(0xFF, 0xCC, 0x00));

    /// <summary>Windows 11 look for standard controls; follows the system light/dark setting.</summary>
    public static void Apply(Application app)
    {
        try { app.ThemeMode = ThemeMode.System; }
        catch { /* older WPF: classic look */ }
    }

    public static Effect Shadow()
    {
        var effect = new DropShadowEffect { BlurRadius = 12, ShadowDepth = 2, Direction = 270, Opacity = 0.35, Color = Colors.Black };
        effect.Freeze();
        return effect;
    }
}
