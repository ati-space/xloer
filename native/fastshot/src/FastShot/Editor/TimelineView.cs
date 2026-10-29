using System.Globalization;

namespace FastShot;

/// <summary>Timeline under the video: thumbnails with trim handles, a playhead, and one bar per
/// annotation whose ends set when the mark appears and disappears.</summary>
public sealed class TimelineView : FrameworkElement
{
    public AnnotationController? Controller { get; set; }
    public UiPalette Palette { get; set; } = UiPalette.Editor;

    double duration, currentTime, trimStart, trimEnd;
    public double Duration { get => duration; set { duration = value; InvalidateVisual(); } }
    public double CurrentTime { get => currentTime; set { if (currentTime == value) return; currentTime = value; InvalidateVisual(); } }
    public double TrimStart { get => trimStart; set { trimStart = value; InvalidateVisual(); } }
    public double TrimEnd { get => trimEnd; set { trimEnd = value; InvalidateVisual(); } }
    List<BitmapSource> thumbnails = [];
    public List<BitmapSource> Thumbnails { get => thumbnails; set { thumbnails = value; InvalidateVisual(); } }

    public Action<double>? OnSeek { get; set; }
    public Action<double, double>? OnTrim { get; set; }
    public Action<bool>? OnScrubbing { get; set; }

    const double Inset = 16, RulerHeight = 18, TrackHeight = 40, RowHeight = 16, RowGap = 3;
    public const int MaxRows = 6;

    enum DragKind { None, Scrub, TrimStart, TrimEnd, Bar }
    enum Edge { Start, End, Body }
    DragKind drag;
    Guid dragId;
    Edge dragEdge;
    double grabTime;
    (double start, double end) dragOriginal;
    bool dragRegistered;

    public static double PreferredHeight(int rows) => 18 + 40 + 10 + Math.Min(Math.Max(rows, 1), MaxRows) * 19 + 10;

    public TimelineView()
    {
        ClipToBounds = true;
        Focusable = false;
    }

    // MARK: Geometry

    double TrackWidth => Math.Max(1, ActualWidth - Inset * 2);
    double X(double time) => Inset + (duration > 0 ? time / duration : 0) * TrackWidth;
    double Time(double x) => Math.Min(Math.Max(0, (x - Inset) / TrackWidth * duration), duration);
    Rect TrackRect => new(Inset, RulerHeight, TrackWidth, TrackHeight);
    List<Annotation> Rows => Controller?.Annotations ?? [];

    Rect RowRect(int index)
    {
        var available = ActualHeight - (RulerHeight + TrackHeight + 10) - 8;
        var count = Math.Max(1, Rows.Count);
        var height = Math.Min(RowHeight, Math.Max(5, available / count - RowGap));
        return new Rect(Inset, RulerHeight + TrackHeight + 10 + index * (height + RowGap), TrackWidth, height);
    }

    Rect BarRect(Annotation a, int row)
    {
        var r = RowRect(row);
        var start = X(a.Start ?? 0);
        var end = X(a.End ?? duration);
        return new Rect(start, r.Y, Math.Max(4, end - start), r.Height);
    }

    // MARK: Drawing

    FormattedText Text(string text, double size, Brush brush, bool bold = false) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(Theme.UiFont, FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal), size, brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Palette.Panel, null, new Rect(RenderSize));
        DrawRuler(dc);

        var track = TrackRect;
        var trackClip = new RectangleGeometry(track, 6, 6);
        dc.PushClip(trackClip);
        dc.DrawRectangle(Palette.Hover, null, track);
        if (thumbnails.Count > 0)
        {
            var width = track.Width / thumbnails.Count;
            for (var i = 0; i < thumbnails.Count; i++)
            {
                var thumb = thumbnails[i];
                var aspect = thumb.PixelWidth / (double)Math.Max(1, thumb.PixelHeight);
                var drawWidth = Math.Max(width, track.Height * aspect);
                var cell = new Rect(track.X + i * width, track.Y, width, track.Height);
                dc.PushClip(new RectangleGeometry(cell));
                dc.DrawImage(thumb, new Rect(cell.X + cell.Width / 2 - drawWidth / 2, cell.Y, drawWidth, cell.Height));
                dc.Pop();
            }
        }
        // Dim what the trim leaves out.
        var shade = Theme.Frozen(Color.FromArgb(0x8C, 0, 0, 0));
        dc.DrawRectangle(shade, null, new Rect(track.X, track.Y, Math.Max(0, X(trimStart) - track.X), track.Height));
        dc.DrawRectangle(shade, null, new Rect(X(trimEnd), track.Y, Math.Max(0, track.Right - X(trimEnd)), track.Height));
        dc.Pop();

        // Trim bracket.
        double left = X(trimStart), right = X(trimEnd);
        dc.DrawRectangle(null, new Pen(Theme.Yellow, 3), new Rect(left, track.Y + 1.5, Math.Max(0, right - left), track.Height - 3));
        var grip = new Pen(Theme.Frozen(Color.FromArgb(0x80, 0, 0, 0)), 1);
        foreach (var edge in new[] { left, right })
        {
            dc.DrawRoundedRectangle(Theme.Yellow, null, new Rect(edge - 5, track.Y, 10, track.Height), 3, 3);
            dc.DrawLine(grip, new Point(edge, track.Center().Y - 7), new Point(edge, track.Center().Y + 7));
        }

        // Annotation bars.
        var selected = Controller?.SelectedId;
        if (Rows.Count == 0)
        {
            dc.DrawText(Text("Videoya çizim eklemek için oynatma çubuğunu istediğiniz ana getirip üstteki araçlarla çizin.", 11, Palette.Secondary),
                        new Point(Inset, RowRect(0).Y));
        }
        for (var i = 0; i < Rows.Count; i++)
        {
            var a = Rows[i];
            var bar = BarRect(a, i);
            var isSelected = a.Id == selected;
            dc.DrawRoundedRectangle(a.Color.WithAlpha(isSelected ? 0.95 : 0.6).Brush(), null, bar, 4, 4);
            if (isSelected) dc.DrawRoundedRectangle(null, new Pen(Theme.Accent, 2), bar.Inset(-1.5, -1.5), 5, 5);
            if (bar.Height >= 12 && bar.Width > 28)
            {
                var ink = a.Color.Luminance > 0.6 ? Brushes.Black : Brushes.White;
                dc.PushClip(new RectangleGeometry(bar.Inset(4, 0)));
                var label = Text(Label(a), 10, ink, bold: true);
                dc.DrawText(label, new Point(bar.X + 6, bar.Center().Y - label.Height / 2));
                dc.Pop();
            }
        }

        // Playhead.
        var head = X(currentTime);
        dc.DrawRectangle(Theme.Accent, null, new Rect(head - 1, RulerHeight - 4, 2, Math.Max(0, ActualHeight - RulerHeight)));
        var triangle = new StreamGeometry();
        using (var ctx = triangle.Open())
        {
            ctx.BeginFigure(new Point(head - 6, 2), true, true);
            ctx.LineTo(new Point(head + 6, 2), true, true);
            ctx.LineTo(new Point(head, RulerHeight - 2), true, true);
        }
        dc.DrawGeometry(Theme.Accent, null, triangle);
    }

    void DrawRuler(DrawingContext dc)
    {
        if (duration <= 0) return;
        double[] steps = [0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300];
        var step = steps.FirstOrDefault(s => s / duration * TrackWidth >= 70, 600);
        var tick = new Pen(Palette.Separator, 1);
        for (var t = 0.0; t <= duration + 0.001; t += step)
        {
            var px = Math.Round(X(t)) + 0.5;
            dc.DrawLine(tick, new Point(px, RulerHeight - 6), new Point(px, RulerHeight));
            dc.DrawText(Text(Ui.Format(t, step < 1), 9, Palette.Secondary), new Point(px + 3, 2));
        }
    }

    public static string Label(Annotation a) => a.Kind switch
    {
        AnnotationKind.text => a.Text.Replace('\n', ' '),
        AnnotationKind.step => $"Numara {a.Number}",
        _ => EditorTools.Title(a.Kind),
    };

    // MARK: Mouse

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (Controller is null) return;
        var p = e.GetPosition(this);
        CaptureMouse();
        e.Handled = true;
        var track = TrackRect;
        if (track.Inset(-8, 0).ContainsPoint(p))
        {
            if (Math.Abs(p.X - X(trimStart)) <= 7) { drag = DragKind.TrimStart; return; }
            if (Math.Abs(p.X - X(trimEnd)) <= 7) { drag = DragKind.TrimEnd; return; }
        }
        for (var i = Rows.Count - 1; i >= 0; i--)
        {
            var a = Rows[i];
            var bar = BarRect(a, i);
            if (!bar.Inset(-4, -1).ContainsPoint(p)) continue;
            Controller.Select(a.Id);
            dragEdge = Math.Abs(p.X - bar.Left) <= 6 ? Edge.Start : Math.Abs(p.X - bar.Right) <= 6 ? Edge.End : Edge.Body;
            drag = DragKind.Bar;
            dragId = a.Id;
            grabTime = Time(p.X);
            dragOriginal = (a.Start ?? 0, a.End ?? duration);
            dragRegistered = false;
            if (dragEdge == Edge.Body && !a.IsVisible(currentTime)) OnSeek?.Invoke(a.Start ?? 0);
            InvalidateVisual();
            return;
        }
        if (p.Y >= RulerHeight + TrackHeight + 4) Controller.Select(null);
        drag = DragKind.Scrub;
        OnScrubbing?.Invoke(true);
        OnSeek?.Invoke(Time(p.X));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var p = e.GetPosition(this);
        if (!IsMouseCaptured || e.LeftButton != MouseButtonState.Pressed)
        {
            UpdateCursor(p);
            return;
        }
        var t = Time(p.X);
        const double minimum = 0.1;
        switch (drag)
        {
            case DragKind.Scrub:
                OnSeek?.Invoke(t);
                break;
            case DragKind.TrimStart:
                OnTrim?.Invoke(Math.Min(t, trimEnd - minimum), trimEnd);
                OnSeek?.Invoke(Math.Min(t, trimEnd - minimum));
                break;
            case DragKind.TrimEnd:
                OnTrim?.Invoke(trimStart, Math.Max(t, trimStart + minimum));
                OnSeek?.Invoke(Math.Max(t, trimStart + minimum));
                break;
            case DragKind.Bar when Controller is not null:
            {
                if (!dragRegistered)
                {
                    Controller.RegisterUndo();
                    dragRegistered = true;
                }
                var (start, end) = dragOriginal;
                switch (dragEdge)
                {
                    case Edge.Start: start = Math.Min(Math.Max(0, t), end - minimum); break;
                    case Edge.End: end = Math.Max(Math.Min(duration, t), start + minimum); break;
                    case Edge.Body:
                        var length = end - start;
                        start = Math.Min(Math.Max(0, dragOriginal.start + t - grabTime), duration - length);
                        end = start + length;
                        break;
                }
                Controller.Update(dragId, a =>
                {
                    a.Start = start;
                    a.End = end >= duration - 0.01 ? null : end;
                }, notify: false);
                OnSeek?.Invoke(dragEdge == Edge.End ? end : start);
                break;
            }
        }
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (IsMouseCaptured) ReleaseMouseCapture();
        if (drag == DragKind.Bar && dragRegistered) Controller?.Update(dragId, _ => { });
        if (drag == DragKind.Scrub) OnScrubbing?.Invoke(false);
        drag = DragKind.None;
        UpdateCursor(e.GetPosition(this));
    }

    void UpdateCursor(Point p)
    {
        var track = TrackRect;
        var resize = false;
        if (p.Y >= track.Top && p.Y <= track.Bottom && (Math.Abs(p.X - X(trimStart)) <= 6 || Math.Abs(p.X - X(trimEnd)) <= 6)) resize = true;
        for (var i = 0; i < Rows.Count && !resize; i++)
        {
            var bar = BarRect(Rows[i], i);
            if (p.Y >= bar.Top && p.Y <= bar.Bottom && (Math.Abs(p.X - bar.Left) <= 4 || Math.Abs(p.X - bar.Right) <= 4)) resize = true;
        }
        Cursor = resize ? Cursors.SizeWE : Cursors.Arrow;
    }
}
