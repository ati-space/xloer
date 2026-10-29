using System.Text.Json.Serialization;
using System.Windows;

namespace FastShot.Core;

[JsonConverter(typeof(JsonStringEnumConverter<AnnotationKind>))]
public enum AnnotationKind { pen, line, arrow, rectangle, ellipse, marker, text, pixelate, step }

public enum AnnotationHandle { Start, End, TopLeft, Top, TopRight, Right, BottomRight, Bottom, BottomLeft, Left }

/// <summary>One editable mark on a capture. Coordinates are canvas points with a top-left origin;
/// a project's pixel scale maps them onto the captured pixels. Reference type: undo history keeps clones.</summary>
public sealed class Annotation
{
    [JsonPropertyName("id")] public Guid Id { get; set; } = Guid.NewGuid();
    [JsonPropertyName("kind")] public AnnotationKind Kind { get; set; }
    /// <summary>pen/marker: the stroke; line/arrow: start and end; boxes: two opposite corners;
    /// text: top-left corner of the label; step: centre of the badge.</summary>
    [JsonPropertyName("points")] public List<Point> Points { get; set; } = [];
    [JsonPropertyName("color")] public RGBAColor Color { get; set; } = RGBAColor.Red;
    [JsonPropertyName("lineWidth")] public double LineWidth { get; set; } = 4;
    [JsonPropertyName("fontSize")] public double FontSize { get; set; } = 20;
    [JsonPropertyName("filled")] public bool Filled { get; set; }
    [JsonPropertyName("text")] public string Text { get; set; } = "";
    [JsonPropertyName("number")] public int Number { get; set; } = 1;
    /// <summary>Video timing, in seconds of the source recording. Null start means from the beginning,
    /// null end means until the end of the recording.</summary>
    [JsonPropertyName("start")] public double? Start { get; set; }
    [JsonPropertyName("end")] public double? End { get; set; }

    public Annotation() { }

    public Annotation(AnnotationKind kind, IEnumerable<Point> points, RGBAColor? color = null, double lineWidth = 4,
                      double fontSize = 20, bool filled = false, string text = "", int number = 1,
                      double? start = null, double? end = null)
    {
        Kind = kind;
        Points = points.ToList();
        Color = color ?? RGBAColor.Red;
        LineWidth = lineWidth;
        FontSize = fontSize;
        Filled = filled;
        Text = text;
        Number = number;
        Start = start;
        End = end;
    }

    public Annotation Clone() => new()
    {
        Id = Id, Kind = Kind, Points = [.. Points], Color = Color, LineWidth = LineWidth, FontSize = FontSize,
        Filled = Filled, Text = Text, Number = Number, Start = Start, End = End,
    };

    public bool ValueEquals(Annotation? other) =>
        other is not null && Id == other.Id && Kind == other.Kind && Points.SequenceEqual(other.Points) && Color == other.Color &&
        LineWidth == other.LineWidth && FontSize == other.FontSize && Filled == other.Filled && Text == other.Text &&
        Number == other.Number && Start == other.Start && End == other.End;

    [JsonIgnore] public bool UsesEndpoints => Kind is AnnotationKind.line or AnnotationKind.arrow;
    [JsonIgnore] public bool UsesBox => Kind is AnnotationKind.rectangle or AnnotationKind.ellipse or AnnotationKind.pixelate;
    [JsonIgnore] public bool UsesPath => Kind is AnnotationKind.pen or AnnotationKind.marker;

    /// <summary>Width of the painted stroke; the marker is a broad translucent nib.</summary>
    [JsonIgnore] public double StrokeWidth => Kind == AnnotationKind.marker ? Math.Max(10, LineWidth * 3.5) : LineWidth;
    [JsonIgnore] public double StepRadius => Math.Max(11, FontSize * 0.75);
    [JsonIgnore] public double ArrowHeadLength => Math.Max(12, StrokeWidth * 3 + 8);

    [JsonIgnore]
    public Rect Box
    {
        get
        {
            if (Points.Count < 2) return new Rect(Points.Count > 0 ? Points[0] : new Point(), new Size(0, 0));
            return Geo.RectBetween(Points[0], Points[1]);
        }
    }

    [JsonIgnore]
    public Rect PathBounds
    {
        get
        {
            if (Points.Count == 0) return new Rect();
            double minX = Points[0].X, maxX = minX, minY = Points[0].Y, maxY = minY;
            foreach (var p in Points)
            {
                minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
                minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
            }
            return new Rect(minX, minY, maxX - minX, maxY - minY);
        }
    }

    /// <summary>The geometry handles attach to, without stroke thickness.</summary>
    [JsonIgnore]
    public Rect Frame => Kind switch
    {
        AnnotationKind.rectangle or AnnotationKind.ellipse or AnnotationKind.pixelate => Box,
        AnnotationKind.pen or AnnotationKind.marker or AnnotationKind.line or AnnotationKind.arrow => PathBounds,
        _ => Bounds,
    };

    /// <summary>Everything the mark paints, stroke included.</summary>
    [JsonIgnore]
    public Rect Bounds
    {
        get
        {
            switch (Kind)
            {
                case AnnotationKind.text:
                    return new Rect(Points.Count > 0 ? Points[0] : new Point(), AnnotationRenderer.TextBoxSize(Text, FontSize));
                case AnnotationKind.step:
                {
                    var c = Points.Count > 0 ? Points[0] : new Point();
                    var r = StepRadius;
                    return new Rect(c.X - r, c.Y - r, r * 2, r * 2);
                }
                case AnnotationKind.pixelate:
                    return Box;
                case AnnotationKind.rectangle or AnnotationKind.ellipse:
                    return Box.Inset(-StrokeWidth / 2, -StrokeWidth / 2);
                case AnnotationKind.arrow:
                    return PathBounds.Inset(-ArrowHeadLength, -ArrowHeadLength);
                default:
                    return PathBounds.Inset(-StrokeWidth / 2, -StrokeWidth / 2);
            }
        }
    }

    public bool IsVisible(double time)
    {
        if (Start is { } start && time + 0.0005 < start) return false;
        if (End is { } end && time > end + 0.0005) return false;
        return true;
    }

    public bool Contains(Point p, double tolerance)
    {
        var reach = StrokeWidth / 2 + tolerance;
        switch (Kind)
        {
            case AnnotationKind.pen or AnnotationKind.marker:
                if (Points.Count == 0) return false;
                if (Points.Count == 1) return Geo.Distance(p, Points[0]) <= reach;
                for (var i = 1; i < Points.Count; i++)
                    if (Geo.DistanceToSegment(p, Points[i - 1], Points[i]) <= reach) return true;
                return false;
            case AnnotationKind.line or AnnotationKind.arrow:
            {
                if (Points.Count < 2) return false;
                var extra = Kind == AnnotationKind.arrow ? ArrowHeadLength * 0.4 : 0;
                return Geo.DistanceToSegment(p, Points[0], Points[1]) <= reach + extra;
            }
            case AnnotationKind.rectangle:
            {
                var r = Box;
                var outer = r.Inset(-reach, -reach);
                if (Filled) return outer.ContainsPoint(p);
                double innerW = r.Width - reach * 2, innerH = r.Height - reach * 2;
                var insideInner = innerW > 0 && innerH > 0 && new Rect(r.X + reach, r.Y + reach, innerW, innerH).ContainsPoint(p);
                return outer.ContainsPoint(p) && !insideInner;
            }
            case AnnotationKind.ellipse:
            {
                var r = Box;
                if (r.Width <= 0.5 || r.Height <= 0.5) return Geo.Distance(p, r.Center()) <= reach;
                double rx = r.Width / 2, ry = r.Height / 2;
                double dx = p.X - r.Center().X, dy = p.Y - r.Center().Y;
                var n = Math.Sqrt(dx * dx / (rx * rx) + dy * dy / (ry * ry));
                if (Filled) return n <= 1 + reach / Math.Min(rx, ry);
                return Math.Abs(n - 1) * Math.Min(rx, ry) <= reach;
            }
            default:
                return Bounds.Inset(-tolerance, -tolerance).ContainsPoint(p);
        }
    }

    [JsonIgnore]
    public List<(AnnotationHandle handle, Point point)> Handles
    {
        get
        {
            switch (Kind)
            {
                case AnnotationKind.line or AnnotationKind.arrow:
                    return Points.Count < 2 ? [] : [(AnnotationHandle.Start, Points[0]), (AnnotationHandle.End, Points[1])];
                case AnnotationKind.text or AnnotationKind.step:
                    return [];
                default:
                {
                    var r = Frame;
                    return
                    [
                        (AnnotationHandle.TopLeft, new Point(r.Left, r.Top)), (AnnotationHandle.Top, new Point(r.Center().X, r.Top)),
                        (AnnotationHandle.TopRight, new Point(r.Right, r.Top)), (AnnotationHandle.Right, new Point(r.Right, r.Center().Y)),
                        (AnnotationHandle.BottomRight, new Point(r.Right, r.Bottom)), (AnnotationHandle.Bottom, new Point(r.Center().X, r.Bottom)),
                        (AnnotationHandle.BottomLeft, new Point(r.Left, r.Bottom)), (AnnotationHandle.Left, new Point(r.Left, r.Center().Y)),
                    ];
                }
            }
        }
    }

    public void Translate(double dx, double dy)
    {
        for (var i = 0; i < Points.Count; i++) Points[i] = new Point(Points[i].X + dx, Points[i].Y + dy);
    }

    /// <summary>Reshapes <paramref name="original"/> so that <paramref name="handle"/> follows <paramref name="p"/>.
    /// Boxes may be dragged through themselves; strokes scale with their bounding box.</summary>
    public static Annotation Resized(Annotation original, AnnotationHandle handle, Point p, bool constrain)
    {
        var a = original.Clone();
        if (handle is AnnotationHandle.Start or AnnotationHandle.End)
        {
            if (a.Points.Count < 2) return a;
            var index = handle == AnnotationHandle.Start ? 0 : 1;
            var anchor = a.Points[1 - index];
            a.Points[index] = constrain ? Geo.SnapAngle(anchor, p) : p;
            return a;
        }
        var old = original.Frame;
        double minX = old.Left, maxX = old.Right, minY = old.Top, maxY = old.Bottom;
        switch (handle)
        {
            case AnnotationHandle.TopLeft: minX = p.X; minY = p.Y; break;
            case AnnotationHandle.Top: minY = p.Y; break;
            case AnnotationHandle.TopRight: maxX = p.X; minY = p.Y; break;
            case AnnotationHandle.Right: maxX = p.X; break;
            case AnnotationHandle.BottomRight: maxX = p.X; maxY = p.Y; break;
            case AnnotationHandle.Bottom: maxY = p.Y; break;
            case AnnotationHandle.BottomLeft: minX = p.X; maxY = p.Y; break;
            case AnnotationHandle.Left: minX = p.X; break;
        }
        if (a.UsesBox)
        {
            a.Points = [new Point(minX, minY), new Point(maxX, maxY)];
        }
        else if (a.UsesPath)
        {
            static double Map(double v, double oldMin, double oldLength, double newMin, double newMax) =>
                oldLength > 0.5 ? newMin + (v - oldMin) * (newMax - newMin) / oldLength : v + (newMin - oldMin);
            a.Points = original.Points.Select(q => new Point(Map(q.X, old.Left, old.Width, minX, maxX), Map(q.Y, old.Top, old.Height, minY, maxY))).ToList();
        }
        return a;
    }

    /// <summary>Too small to be anything but an accidental click.</summary>
    [JsonIgnore]
    public bool IsDegenerate => Kind switch
    {
        AnnotationKind.line or AnnotationKind.arrow => Points.Count < 2 || Geo.Distance(Points[0], Points[1]) < 3,
        AnnotationKind.rectangle or AnnotationKind.ellipse or AnnotationKind.pixelate => Box.Width < 3 || Box.Height < 3,
        AnnotationKind.pen or AnnotationKind.marker => Points.Count == 0,
        AnnotationKind.text => string.IsNullOrWhiteSpace(Text),
        _ => Points.Count == 0,
    };
}
