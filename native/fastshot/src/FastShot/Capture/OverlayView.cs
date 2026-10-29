using System.Globalization;

namespace FastShot;

/// <summary>Full-screen view over one monitor's frozen image. Handles choosing and adjusting the
/// region, hosts the annotation canvas inside it and places the two toolbars around it.</summary>
public sealed class OverlayView : Canvas
{
    readonly CaptureSession session;
    public ScreenShot Shot { get; }
    /// <summary>Device pixels per DIP on this monitor.</summary>
    public double Scale { get; private set; }
    public List<Rect> WindowRects { get; set; } = [];
    public Rect? Selection { get; private set; }
    public CanvasView? CanvasView { get; private set; }
    FrameworkElement? palette, actionBar;

    enum Phase { Idle, Creating, Selected, Moving, Resizing }
    Phase phase;
    Point dragStart;
    bool moved;
    Rect dragOriginal;
    AnnotationHandle dragHandle;
    Point? mouse;
    Rect? hover;
    bool overridingCursor;

    static readonly Brush dim = Theme.Frozen(Color.FromArgb(0x73, 0, 0, 0));
    static readonly Brush pill = Theme.Frozen(Color.FromArgb(0xDB, 0x14, 0x14, 0x14));
    static readonly Typeface pillFont = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    public OverlayView(CaptureSession session, ScreenShot shot, double scale)
    {
        this.session = session;
        Shot = shot;
        Background = Brushes.Black;
        Focusable = true;
        FocusVisualStyle = null;
        ClipToBounds = true;
        Cursor = Cursors.Cross;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
        SetScale(scale);
    }

    public void SetScale(double scale)
    {
        Scale = scale;
        Width = Shot.Width / scale;
        Height = Shot.Height / scale;
        InvalidateVisual();
    }

    bool IsVideo => session.Mode == CaptureSession.CaptureMode.Video;
    bool IsIdle => phase == Phase.Idle;
    bool IsChoosing => phase is Phase.Idle or Phase.Creating;
    bool IsOtherScreenActive => session.ActiveView is not null && session.ActiveView != this;
    Rect Bounds => new(0, 0, Width, Height);
    /// <summary>One device pixel in DIPs.</summary>
    double Px => 1 / Scale;

    Int32Rect PixelRect(Rect r)
    {
        var x = (int)Math.Round(r.X * Scale);
        var y = (int)Math.Round(r.Y * Scale);
        var right = Math.Min(Shot.Width, (int)Math.Round(r.Right * Scale));
        var bottom = Math.Min(Shot.Height, (int)Math.Round(r.Bottom * Scale));
        x = Math.Clamp(x, 0, Shot.Width - 1);
        y = Math.Clamp(y, 0, Shot.Height - 1);
        return new Int32Rect(x, y, Math.Max(1, right - x), Math.Max(1, bottom - y));
    }

    public BitmapSource? CroppedImage()
    {
        if (Selection is not { } selection) return null;
        var crop = new CroppedBitmap(Shot.Image, PixelRect(selection));
        crop.Freeze();
        return crop;
    }

    public void ResetSelection()
    {
        RemoveChrome();
        Selection = null;
        phase = Phase.Idle;
        InvalidateVisual();
    }

    // MARK: Drawing

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawImage(Shot.Image, Bounds);
        var showHover = IsIdle && !IsOtherScreenActive;
        var highlight = Selection ?? (showHover ? hover : null);
        System.Windows.Media.Geometry shade = new RectangleGeometry(Bounds);
        if (highlight is { } h) shade = new CombinedGeometry(GeometryCombineMode.Exclude, shade, new RectangleGeometry(h));
        dc.DrawGeometry(dim, null, shade);

        if (Selection is { } selection)
        {
            DrawFrame(dc, selection, handles: !IsChoosing);
            DrawSizeLabel(dc, selection);
        }
        else if (showHover && hover is { } hovered)
        {
            dc.DrawRectangle(null, new Pen(Theme.Accent, 3 * Px), hovered.Inset(1.5 * Px, 1.5 * Px));
            DrawSizeLabel(dc, hovered);
        }
        if (IsChoosing && !IsOtherScreenActive && Preferences.Shared.ShowMagnifier && mouse is { } m) DrawLoupe(dc, m);
        if (IsIdle && !IsOtherScreenActive) DrawHint(dc);
    }

    void DrawFrame(DrawingContext dc, Rect rect, bool handles)
    {
        dc.DrawRectangle(null, new Pen(Theme.Frozen(Color.FromArgb(0x99, 0, 0, 0)), Px), rect.Inset(-1.5 * Px, -1.5 * Px));
        dc.DrawRectangle(null, new Pen(Brushes.White, Px), rect.Inset(-0.5 * Px, -0.5 * Px));
        if (!handles) return;
        var border = new Pen(Theme.Frozen(Color.FromArgb(0xB3, 0, 0, 0)), Px);
        foreach (var (_, point) in SelectionHandles(rect))
            dc.DrawRectangle(Brushes.White, border, new Rect(point.X - 4, point.Y - 4, 8, 8));
    }

    void DrawSizeLabel(DrawingContext dc, Rect rect)
    {
        var text = $"{Math.Round(rect.Width * Scale)} × {Math.Round(rect.Height * Scale)}";
        var origin = new Point(rect.X, rect.Y - 26);
        if (origin.Y < 4) origin = new Point(rect.X + 6, rect.Y + 6);
        DrawPill(dc, text, origin, 11.5);
    }

    void DrawHint(DrawingContext dc)
    {
        var text = IsVideo
            ? "Kaydedilecek alanı sürükleyerek seçin  ·  Pencere için tıklayın  ·  Tam ekran: Enter  ·  Çıkış: Esc"
            : "Alanı sürükleyerek seçin  ·  Pencere için tıklayın  ·  Tam ekran: Enter  ·  Çıkış: Esc";
        var formatted = Format(text, 13);
        var width = formatted.Width + 28;
        DrawPill(dc, text, new Point((Width - width) / 2, 46), 13, padding: 14, height: 30);
    }

    FormattedText Format(string text, double size) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, pillFont, size, Brushes.White, Scale);

    Rect DrawPill(DrawingContext dc, string text, Point origin, double fontSize, double padding = 7, double height = 20)
    {
        var formatted = Format(text, fontSize);
        var box = new Rect(origin.X, origin.Y, formatted.Width + padding * 2, height);
        box.X = Math.Min(Math.Max(4, box.X), Width - box.Width - 4);
        var radius = height / 2.6;
        dc.DrawRoundedRectangle(pill, null, box, radius, radius);
        dc.DrawText(formatted, new Point(box.X + padding, box.Y + (height - formatted.Height) / 2));
        return box;
    }

    /// <summary>A magnified view of the pixels under the pointer with its coordinates and colour.</summary>
    void DrawLoupe(DrawingContext dc, Point p)
    {
        const int cells = 15;
        const double zoom = 8;
        var side = cells * zoom;
        var origin = new Point(p.X + 20, p.Y + 20);
        if (origin.X + side > Width - 4) origin.X = p.X - 20 - side;
        if (origin.Y + side + 44 > Height - 4) origin.Y = p.Y - 20 - side - 44;
        var loupe = new Rect(origin, new Size(side, side));
        var px = (int)(p.X * Scale);
        var py = (int)(p.Y * Scale);
        const int half = cells / 2;
        var wanted = new Int32Rect(px - half, py - half, cells, cells);
        var x0 = Math.Max(0, wanted.X);
        var y0 = Math.Max(0, wanted.Y);
        var x1 = Math.Min(Shot.Width, wanted.X + cells);
        var y1 = Math.Min(Shot.Height, wanted.Y + cells);

        var clip = new RectangleGeometry(loupe, 8, 8);
        dc.PushClip(clip);
        dc.DrawRectangle(Brushes.Black, null, loupe);
        if (x1 > x0 && y1 > y0)
        {
            var crop = new CroppedBitmap(Shot.Image, new Int32Rect(x0, y0, x1 - x0, y1 - y0));
            var target = new Rect(loupe.X + (x0 - wanted.X) * zoom, loupe.Y + (y0 - wanted.Y) * zoom, (x1 - x0) * zoom, (y1 - y0) * zoom);
            dc.DrawImage(crop, target);
        }
        var grid = new Pen(Theme.Frozen(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)), Px);
        for (var i = 1; i < cells; i++)
        {
            var offset = i * zoom;
            dc.DrawLine(grid, new Point(loupe.X + offset, loupe.Y), new Point(loupe.X + offset, loupe.Bottom));
            dc.DrawLine(grid, new Point(loupe.X, loupe.Y + offset), new Point(loupe.Right, loupe.Y + offset));
        }
        dc.DrawRectangle(null, new Pen(Brushes.White, 1.5), new Rect(loupe.X + half * zoom, loupe.Y + half * zoom, zoom, zoom));
        dc.Pop();
        dc.DrawGeometry(null, new Pen(Theme.Frozen(Color.FromArgb(0xD9, 0xFF, 0xFF, 0xFF)), 1.5), clip);

        var color = PixelColor(px, py);
        var label = $"{px}, {py}   {color?.Hex ?? ""}";
        var box = DrawPill(dc, label, new Point(loupe.X, loupe.Bottom + 6), 11);
        if (color is { } c) dc.DrawRectangle(c.Brush(), new Pen(Brushes.White, Px), new Rect(box.Right + 6, box.Y + 3, 14, 14));
    }

    RGBAColor? PixelColor(int x, int y)
    {
        if (x < 0 || y < 0 || x >= Shot.Width || y >= Shot.Height) return null;
        var o = (y * Shot.Width + x) * 4;
        return new RGBAColor(Shot.Pixels[o + 2] / 255.0, Shot.Pixels[o + 1] / 255.0, Shot.Pixels[o] / 255.0);
    }

    // MARK: Selection geometry

    static List<(AnnotationHandle, Point)> SelectionHandles(Rect r) =>
    [
        (AnnotationHandle.TopLeft, r.TopLeft), (AnnotationHandle.Top, new Point(r.Center().X, r.Top)), (AnnotationHandle.TopRight, r.TopRight),
        (AnnotationHandle.Right, new Point(r.Right, r.Center().Y)), (AnnotationHandle.BottomRight, r.BottomRight),
        (AnnotationHandle.Bottom, new Point(r.Center().X, r.Bottom)), (AnnotationHandle.BottomLeft, r.BottomLeft), (AnnotationHandle.Left, new Point(r.Left, r.Center().Y)),
    ];

    /// <summary>Knobs first, then the edges themselves.</summary>
    AnnotationHandle? SelectionHandle(Point p)
    {
        if (Selection is not { } r) return null;
        foreach (var (handle, point) in SelectionHandles(r))
            if (Geo.Distance(point, p) <= 8) return handle;
        const double edge = 4;
        var withinX = p.X > r.Left && p.X < r.Right;
        var withinY = p.Y > r.Top && p.Y < r.Bottom;
        if (withinX && Math.Abs(p.Y - r.Top) <= edge) return AnnotationHandle.Top;
        if (withinX && Math.Abs(p.Y - r.Bottom) <= edge) return AnnotationHandle.Bottom;
        if (withinY && Math.Abs(p.X - r.Left) <= edge) return AnnotationHandle.Left;
        if (withinY && Math.Abs(p.X - r.Right) <= edge) return AnnotationHandle.Right;
        return null;
    }

    static Rect Resized(Rect r, AnnotationHandle handle, Point p)
    {
        double minX = r.Left, maxX = r.Right, minY = r.Top, maxY = r.Bottom;
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
        return Geo.RectBetween(new Point(minX, minY), new Point(maxX, maxY));
    }

    /// <summary>Clips to the screen and snaps every edge to a whole device pixel.</summary>
    Rect Snap(Rect rect)
    {
        var r = Rect.Intersect(rect, Bounds);
        if (r.IsEmpty) r = new Rect(Math.Clamp(rect.X, 0, Width), Math.Clamp(rect.Y, 0, Height), 0, 0);
        var s = Scale;
        var x0 = Math.Round(r.Left * s) / s;
        var y0 = Math.Round(r.Top * s) / s;
        var x1 = Math.Max(x0 + Px, Math.Round(r.Right * s) / s);
        var y1 = Math.Max(y0 + Px, Math.Round(r.Bottom * s) / s);
        return new Rect(x0, y0, x1 - x0, y1 - y0);
    }

    void SetSelection(Rect rect)
    {
        var r = Snap(rect);
        var old = Selection;
        Selection = r;
        if (CanvasView is { } canvas && old is { } previous)
        {
            // Annotations belong to the screen content, so they stay put while the frame moves.
            var dx = previous.X - r.X;
            var dy = previous.Y - r.Y;
            if (dx != 0 || dy != 0) session.Controller.TranslateAll(dx, dy);
            canvas.Width = r.Width;
            canvas.Height = r.Height;
            SetLeft(canvas, r.X);
            SetTop(canvas, r.Y);
            canvas.CanvasSize = r.Size;
            canvas.BackgroundImage = CroppedImage();
        }
        LayoutChrome();
        InvalidateVisual();
    }

    void SelectWholeScreen()
    {
        session.SelectionBegan(this);
        Selection = Snap(Bounds);
        phase = Phase.Selected;
        InstallChrome();
        InvalidateVisual();
    }

    // MARK: Mouse

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonDown(e);
        // Selection handles win over the canvas inside the selection.
        var p = e.GetPosition(this);
        if (phase == Phase.Selected && Selection is { } selection && SelectionHandle(p) is { } handle && !(CanvasView?.IsEditingText ?? false))
        {
            phase = Phase.Resizing;
            dragHandle = handle;
            dragOriginal = selection;
            CaptureMouse();
            e.Handled = true;
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (e.Handled) return;
        var p = e.GetPosition(this);
        Focus();
        if (phase == Phase.Selected && Selection is { } selection)
        {
            if (selection.ContainsPoint(p))
            {
                BeginMove(p, selection);
                CaptureMouse();
                e.Handled = true;
                return;
            }
            // Clicking outside never throws away drawings by accident.
            if (session.HasAnnotations) return;
        }
        session.SelectionBegan(this);
        RemoveChrome();
        Selection = null;
        phase = Phase.Creating;
        dragStart = p;
        moved = false;
        mouse = p;
        CaptureMouse();
        InvalidateVisual();
        e.Handled = true;
    }

    void BeginMove(Point p, Rect selection)
    {
        phase = Phase.Moving;
        dragStart = p;
        dragOriginal = selection;
        Cursor = Cursors.SizeAll;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var p = e.GetPosition(this);
        if (IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed)
        {
            Drag(p);
            return;
        }
        if (Mouse.Captured is not null && !IsMouseCaptured) return;
        mouse = p;
        if (IsIdle)
        {
            var newHover = WindowRects.Cast<Rect?>().FirstOrDefault(r => r!.Value.ContainsPoint(p));
            hover = newHover;
        }
        if (IsChoosing || IsIdle) InvalidateVisual();
        UpdateCursor(p);
    }

    void Drag(Point p)
    {
        mouse = p;
        switch (phase)
        {
            case Phase.Creating:
                if (moved || Geo.Distance(dragStart, p) > 3)
                {
                    moved = true;
                    SetSelection(Geo.RectBetween(dragStart, p));
                }
                break;
            case Phase.Moving:
            {
                var r = dragOriginal.Shifted(p.X - dragStart.X, p.Y - dragStart.Y);
                r.X = Math.Min(Math.Max(0, r.X), Width - r.Width);
                r.Y = Math.Min(Math.Max(0, r.Y), Height - r.Height);
                SetSelection(r);
                break;
            }
            case Phase.Resizing:
                SetSelection(Resized(dragOriginal, dragHandle, p));
                break;
        }
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!IsMouseCaptured) return;
        ReleaseMouseCapture();
        EndDrag(e.GetPosition(this));
        e.Handled = true;
    }

    void EndDrag(Point p)
    {
        switch (phase)
        {
            case Phase.Creating:
                if (!moved || (Selection?.Width ?? 0) < 4 || (Selection?.Height ?? 0) < 4)
                {
                    // A click picks the window under the pointer, or the whole screen.
                    Selection = Snap(hover ?? Bounds);
                }
                phase = Phase.Selected;
                InstallChrome();
                break;
            case Phase.Moving or Phase.Resizing:
                phase = Phase.Selected;
                break;
        }
        ClearCursorOverride();
        UpdateCursor(p);
        InvalidateVisual();
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonDown(e);
        if (IsChoosing)
        {
            e.Handled = true;
            session.Cancel();
        }
    }

    protected override void OnPreviewMouseMove(MouseEventArgs e)
    {
        base.OnPreviewMouseMove(e);
        if (phase == Phase.Resizing) return;
        var p = e.GetPosition(this);
        if (phase == Phase.Selected && Mouse.Captured is null && SelectionHandle(p) is { } handle)
        {
            Mouse.OverrideCursor = AnnotationController.CursorFor(handle);
            overridingCursor = true;
        }
        else
        {
            ClearCursorOverride();
        }
    }

    void ClearCursorOverride()
    {
        if (!overridingCursor) return;
        Mouse.OverrideCursor = null;
        overridingCursor = false;
    }

    void UpdateCursor(Point p)
    {
        if (Selection is { } selection && !IsChoosing)
            Cursor = selection.ContainsPoint(p) ? Cursors.SizeAll : session.HasAnnotations ? Cursors.Arrow : Cursors.Cross;
        else
            Cursor = Cursors.Cross;
    }

    /// <summary>Drags that start on empty canvas move the selection.</summary>
    void BackgroundMouse(MouseEventArgs e, BackgroundMouse kind)
    {
        var p = e.GetPosition(this);
        switch (kind)
        {
            case FastShot.BackgroundMouse.Down:
                if (Selection is { } selection) BeginMove(p, selection);
                break;
            case FastShot.BackgroundMouse.Drag:
                if (phase == Phase.Moving) Drag(p);
                break;
            case FastShot.BackgroundMouse.Up:
                if (phase == Phase.Moving) EndDrag(p);
                break;
        }
    }

    // MARK: Keyboard

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && HandleKey(e)) e.Handled = true;
    }

    public bool HandleKey(KeyEventArgs e)
    {
        var modifiers = Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt);
        var key = EditorShortcuts.KeyOf(e);
        switch (key)
        {
            case Key.Escape:
                if (session.Controller.Selected is not null) session.Controller.Select(null);
                else session.Cancel();
                return true;
            case Key.Enter:
                if (IsChoosing) SelectWholeScreen();
                else if (IsVideo) session.Record();
                else session.Copy();
                return true;
        }
        if (IsChoosing) return false;
        if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && key == Key.T)
        {
            if (!IsVideo) session.TranslateText();
            return true;
        }
        if (modifiers == ModifierKeys.Control)
        {
            switch (key)
            {
                case Key.C: if (!IsVideo) session.Copy(); return true;
                case Key.S: if (!IsVideo) session.Save(); return true;
                case Key.E: if (!IsVideo) session.OpenEditor(); return true;
                case Key.P: if (!IsVideo) session.Pin(); return true;
                case Key.T: if (!IsVideo) session.RecognizeText(); return true;
                case Key.R: session.Record(); return true;
            }
        }
        if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && key == Key.S && !IsVideo)
        {
            session.SaveAs();
            return true;
        }
        if (session.Controller.Selected is null && Selection is { } selection && modifiers is ModifierKeys.None or ModifierKeys.Shift)
        {
            var step = (modifiers == ModifierKeys.Shift ? 10 : 1) * Px;
            Vector? offset = key switch
            {
                Key.Left => new Vector(-step, 0),
                Key.Right => new Vector(step, 0),
                Key.Down => new Vector(0, step),
                Key.Up => new Vector(0, -step),
                _ => null,
            };
            if (offset is { } o)
            {
                var r = selection.Shifted(o.X, o.Y);
                r.X = Math.Min(Math.Max(0, r.X), Width - r.Width);
                r.Y = Math.Min(Math.Max(0, r.Y), Height - r.Height);
                SetSelection(r);
                return true;
            }
        }
        return false;
    }

    // MARK: Canvas and toolbars

    void InstallChrome()
    {
        RemoveChrome();
        if (Selection is not { } selection) return;
        if (!IsVideo)
        {
            var canvas = new CanvasView(session.Controller, selection.Size)
            {
                DrawsBackground = false,
                FitsToBounds = false,
                Width = selection.Width,
                Height = selection.Height,
                IdleCursor = () => Cursors.SizeAll,
                OnBackgroundMouse = BackgroundMouse,
                OnKey = HandleKey,
            };
            canvas.BackgroundImage = CroppedImage();
            SetLeft(canvas, selection.X);
            SetTop(canvas, selection.Y);
            Children.Add(canvas);
            CanvasView = canvas;

            palette = OverlayToolbars.Palette(session.Tools, session.Controller.Undo, session.Controller.Redo);
            Children.Add(palette);
        }
        actionBar = OverlayToolbars.ActionBar(session);
        Children.Add(actionBar);
        LayoutChrome();
        var target = (IInputElement?)CanvasView ?? this;
        Keyboard.Focus(target);
    }

    void RemoveChrome()
    {
        CanvasView?.EndTextEditing();
        if (CanvasView is not null) Children.Remove(CanvasView);
        if (palette is not null) Children.Remove(palette);
        if (actionBar is not null) Children.Remove(actionBar);
        CanvasView = null;
        palette = null;
        actionBar = null;
    }

    /// <summary>Tools to the right of the region (or left, or inside), actions below it (or above, or inside).</summary>
    void LayoutChrome()
    {
        if (Selection is not { } selection) return;
        const double margin = 8 - Ui.ChromeMargin, inset = 6 - Ui.ChromeMargin;
        if (palette is not null)
        {
            palette.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var size = palette.DesiredSize;
            var x = selection.Right + margin;
            if (x + size.Width > Width - inset)
            {
                x = selection.Left - margin - size.Width;
                if (x < inset) x = selection.Right - size.Width - margin;
            }
            var y = Math.Min(Math.Max(inset, selection.Top), Height - size.Height - inset);
            SetLeft(palette, Math.Max(inset, x));
            SetTop(palette, y);
        }
        if (actionBar is not null)
        {
            actionBar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var size = actionBar.DesiredSize;
            var y = selection.Bottom + margin;
            if (y + size.Height > Height - inset)
            {
                y = selection.Top - margin - size.Height;
                if (y < inset) y = selection.Bottom - size.Height - margin;
            }
            var x = Math.Min(Math.Max(inset, selection.Right - size.Width), Width - size.Width - inset);
            SetLeft(actionBar, x);
            SetTop(actionBar, y);
        }
    }
}
