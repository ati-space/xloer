namespace FastShot;

public enum EditorTool { Select, Pen, Line, Arrow, Rectangle, Ellipse, Marker, Text, Pixelate, Step }

public static class EditorTools
{
    public static readonly EditorTool[] All = Enum.GetValues<EditorTool>();

    /// <summary>Tools that make sense while recording: pixelation needs a frame to sample.</summary>
    public static readonly EditorTool[] Live =
        [EditorTool.Pen, EditorTool.Arrow, EditorTool.Rectangle, EditorTool.Ellipse, EditorTool.Marker, EditorTool.Text, EditorTool.Step];

    public static AnnotationKind? Kind(this EditorTool tool) => tool switch
    {
        EditorTool.Pen => AnnotationKind.pen,
        EditorTool.Line => AnnotationKind.line,
        EditorTool.Arrow => AnnotationKind.arrow,
        EditorTool.Rectangle => AnnotationKind.rectangle,
        EditorTool.Ellipse => AnnotationKind.ellipse,
        EditorTool.Marker => AnnotationKind.marker,
        EditorTool.Text => AnnotationKind.text,
        EditorTool.Pixelate => AnnotationKind.pixelate,
        EditorTool.Step => AnnotationKind.step,
        _ => null,
    };

    public static string Title(this EditorTool tool) => tool switch
    {
        EditorTool.Select => "Seç ve taşı",
        EditorTool.Pen => "Kalem",
        EditorTool.Line => "Çizgi",
        EditorTool.Arrow => "Ok",
        EditorTool.Rectangle => "Dikdörtgen",
        EditorTool.Ellipse => "Elips",
        EditorTool.Marker => "İşaretleyici",
        EditorTool.Text => "Metin",
        EditorTool.Pixelate => "Bulanıklaştır",
        _ => "Numara",
    };

    public static Key Shortcut(this EditorTool tool) => tool switch
    {
        EditorTool.Select => Key.V,
        EditorTool.Pen => Key.P,
        EditorTool.Line => Key.L,
        EditorTool.Arrow => Key.A,
        EditorTool.Rectangle => Key.R,
        EditorTool.Ellipse => Key.O,
        EditorTool.Marker => Key.M,
        EditorTool.Text => Key.T,
        EditorTool.Pixelate => Key.B,
        _ => Key.N,
    };

    public static string Help(this EditorTool tool) => $"{tool.Title()} ({tool.Shortcut()})";

    public static string Title(AnnotationKind kind) => kind switch
    {
        AnnotationKind.pen => "Kalem",
        AnnotationKind.line => "Çizgi",
        AnnotationKind.arrow => "Ok",
        AnnotationKind.rectangle => "Dikdörtgen",
        AnnotationKind.ellipse => "Elips",
        AnnotationKind.marker => "İşaretleyici",
        AnnotationKind.text => "Metin",
        AnnotationKind.pixelate => "Bulanıklaştır",
        _ => "Numara",
    };
}

public enum StyleChange { Color, Size, Filled }

/// <summary>The current tool and style, shared by a toolbar and its canvas. Choosing a style while
/// a mark is selected restyles that mark.</summary>
public sealed class ToolState
{
    public static readonly double[] LineWidths = [2, 4, 7, 12];
    public static readonly double[] FontSizes = [14, 20, 28, 40];
    public static readonly string[] SizeNames = ["İnce", "Orta", "Kalın", "Çok kalın"];

    /// <summary>Raised after any property changes, for toolbars to refresh.</summary>
    public event Action? Changed;
    public Action<StyleChange>? OnStyleChange { get; set; }

    EditorTool tool;
    RGBAColor color;
    int size;
    bool filled, canUndo, canRedo, hasSelection, syncing;

    public ToolState(EditorTool tool = EditorTool.Select)
    {
        this.tool = tool;
        color = Preferences.Shared.LastColor;
        size = Math.Clamp(Preferences.Shared.LastSize, 0, LineWidths.Length - 1);
    }

    public EditorTool Tool
    {
        get => tool;
        set { if (tool == value) return; tool = value; Changed?.Invoke(); }
    }

    public RGBAColor Color
    {
        get => color;
        set { if (color == value) return; color = value; StyleDidChange(StyleChange.Color); Changed?.Invoke(); }
    }

    public int Size
    {
        get => size;
        set { value = Math.Clamp(value, 0, LineWidths.Length - 1); if (size == value) return; size = value; StyleDidChange(StyleChange.Size); Changed?.Invoke(); }
    }

    public bool Filled
    {
        get => filled;
        set { if (filled == value) return; filled = value; StyleDidChange(StyleChange.Filled); Changed?.Invoke(); }
    }

    public bool CanUndo { get => canUndo; set { if (canUndo == value) return; canUndo = value; Changed?.Invoke(); } }
    public bool CanRedo { get => canRedo; set { if (canRedo == value) return; canRedo = value; Changed?.Invoke(); } }
    public bool HasSelection { get => hasSelection; set { if (hasSelection == value) return; hasSelection = value; Changed?.Invoke(); } }

    public double LineWidth => LineWidths[size];
    public double FontSize => FontSizes[size];

    /// <summary>Mirrors a selected mark's style without restyling it.</summary>
    public void Sync(Annotation annotation)
    {
        syncing = true;
        try
        {
            Color = annotation.Color;
            Filled = annotation.Filled;
            var usesFont = annotation.Kind is AnnotationKind.text or AnnotationKind.step;
            var reference = usesFont ? FontSizes : LineWidths;
            var value = usesFont ? annotation.FontSize : annotation.LineWidth;
            var best = 0;
            for (var i = 1; i < reference.Length; i++)
                if (Math.Abs(reference[i] - value) < Math.Abs(reference[best] - value)) best = i;
            Size = best;
        }
        finally { syncing = false; }
    }

    void StyleDidChange(StyleChange change)
    {
        if (syncing) return;
        Preferences.Shared.LastColor = color;
        Preferences.Shared.LastSize = size;
        OnStyleChange?.Invoke(change);
    }
}
