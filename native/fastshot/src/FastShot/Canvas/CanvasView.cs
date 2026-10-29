using System.Windows.Markup;

namespace FastShot;

public enum BackgroundMouse { Down, Drag, Up }

/// <summary>Paints a background plus its annotations and feeds mouse and keyboard input to an
/// <see cref="AnnotationController"/>. Used by the capture overlay (1:1, transparent), the image
/// editor and the video editor (scaled to fit, with a frame behind), and the live recording layer.</summary>
public sealed class CanvasView : Canvas
{
    public AnnotationController Controller { get; }

    Size canvasSize;
    public Size CanvasSize
    {
        get => canvasSize;
        set { canvasSize = value; InvalidateVisual(); StyleTextBox(); }
    }

    BitmapSource? backgroundImage;
    /// <summary>Pixels behind the canvas; also what pixelation samples.</summary>
    public BitmapSource? BackgroundImage
    {
        get => backgroundImage;
        set { backgroundImage = value; InvalidateVisual(); }
    }

    public bool DrawsBackground { get; set; } = true;
    public bool FitsToBounds { get; set; } = true;

    Brush? letterbox;
    public Brush? Letterbox
    {
        get => letterbox;
        set { letterbox = value; InvalidateVisual(); }
    }

    /// <summary>Mouse events that land on empty canvas while the select tool is active.</summary>
    public Action<MouseEventArgs, BackgroundMouse>? OnBackgroundMouse { get; set; }
    /// <summary>Gets keys before the canvas does; return true to consume.</summary>
    public Func<KeyEventArgs, bool>? OnKey { get; set; }
    /// <summary>Cursor to use where the controller has no opinion.</summary>
    public Func<Cursor> IdleCursor { get; set; } = () => Cursors.Arrow;

    bool routingToBackground;
    TextBox? textBox;
    Guid? editingId;

    static readonly ControlTemplate plainTextBox = (ControlTemplate)XamlReader.Parse("""
        <ControlTemplate TargetType="TextBox" xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <Border Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}">
            <ScrollViewer x:Name="PART_ContentHost" Focusable="False" HorizontalScrollBarVisibility="Hidden" VerticalScrollBarVisibility="Hidden" Padding="0" Margin="0"/>
          </Border>
        </ControlTemplate>
        """);

    public CanvasView(AnnotationController controller, Size canvasSize)
    {
        Controller = controller;
        this.canvasSize = canvasSize;
        Background = Brushes.Transparent;
        Focusable = true;
        FocusVisualStyle = null;
        ClipToBounds = true;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        controller.OnBeginTextEditing = BeginTextEditing;
        controller.OnDisplay = () =>
        {
            InvalidateVisual();
            StyleTextBox();
        };
    }

    // MARK: Geometry

    public double Scale
    {
        get
        {
            if (!FitsToBounds || canvasSize.Width <= 0 || canvasSize.Height <= 0) return 1;
            return Math.Max(0.05, Math.Min(ActualWidth / canvasSize.Width, ActualHeight / canvasSize.Height));
        }
    }

    public Point CanvasOrigin
    {
        get
        {
            if (!FitsToBounds) return new Point();
            var s = Scale;
            return new Point(Math.Round((ActualWidth - canvasSize.Width * s) / 2), Math.Round((ActualHeight - canvasSize.Height * s) / 2));
        }
    }

    public Point ToCanvas(Point p)
    {
        var o = CanvasOrigin;
        var s = Scale;
        return new Point((p.X - o.X) / s, (p.Y - o.Y) / s);
    }

    public Point ToView(Point p)
    {
        var o = CanvasOrigin;
        var s = Scale;
        return new Point(o.X + p.X * s, o.Y + p.Y * s);
    }

    public Rect ToView(Rect r)
    {
        var origin = ToView(r.TopLeft);
        return new Rect(origin.X, origin.Y, r.Width * Scale, r.Height * Scale);
    }

    public Rect CanvasRectInView => ToView(new Rect(canvasSize));

    double Tolerance => 8 / Scale;

    // MARK: Drawing

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(letterbox ?? Brushes.Transparent, null, new Rect(RenderSize));
        var origin = CanvasOrigin;
        var s = Scale;
        var canvasRect = new Rect(canvasSize);
        dc.PushTransform(new TranslateTransform(origin.X, origin.Y));
        dc.PushTransform(new ScaleTransform(s, s));
        dc.PushClip(new RectangleGeometry(canvasRect));
        if (DrawsBackground)
        {
            if (backgroundImage is not null) dc.DrawImage(backgroundImage, canvasRect);
            else dc.DrawRectangle(Brushes.Black, null, canvasRect);
        }
        var marks = Controller.Visible.Where(a => a.Id != editingId);
        AnnotationRenderer.Draw(marks, dc, backgroundImage, canvasSize);
        dc.Pop();
        dc.Pop();
        dc.Pop();
        DrawSelectionChrome(dc);
    }

    static readonly Pen outlineShadow = FrozenPen(Theme.Frozen(Color.FromArgb(0x80, 0, 0, 0)), 1);
    static readonly Pen outlineDash = FrozenPen(Theme.Accent, 1, new DashStyle([4, 3], 0));
    static readonly Pen knobPen = FrozenPen(Theme.Accent, 1.5);

    static Pen FrozenPen(Brush brush, double width, DashStyle? dash = null)
    {
        var pen = new Pen(brush, width);
        if (dash is not null) pen.DashStyle = dash;
        pen.Freeze();
        return pen;
    }

    void DrawSelectionChrome(DrawingContext dc)
    {
        if (Controller.Selected is not { } a || a.Id == editingId || !Controller.Visible.Any(v => v.Id == a.Id)) return;
        if (!a.UsesEndpoints)
        {
            var outline = ToView(a.Frame);
            if (a.Kind is AnnotationKind.text or AnnotationKind.step) outline = outline.Inset(-3, -3);
            outline = outline.Inset(-0.5, -0.5);
            dc.DrawRectangle(null, outlineShadow, outline);
            dc.DrawRectangle(null, outlineDash, outline);
        }
        foreach (var (_, point) in a.Handles)
        {
            var c = ToView(point);
            dc.DrawEllipse(Brushes.White, knobPen, c, 4.5, 4.5);
        }
    }

    // MARK: Mouse

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (editingId is not null)
        {
            if (textBox is not null && textBox.IsMouseOver) return;
            EndTextEditing();
        }
        Focus();
        Keyboard.Focus(this);
        var p = ToCanvas(e.GetPosition(this));
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        var result = Controller.MouseDown(p, e.ClickCount, shift, Tolerance);
        routingToBackground = result == AnnotationController.MouseResult.Background;
        if (editingId is null) CaptureMouse();
        if (routingToBackground) OnBackgroundMouse?.Invoke(e, BackgroundMouse.Down);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed)
        {
            if (routingToBackground)
            {
                OnBackgroundMouse?.Invoke(e, BackgroundMouse.Drag);
                return;
            }
            var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            Controller.MouseDragged(ToCanvas(e.GetPosition(this)), shift);
            InvalidateVisual();
            return;
        }
        UpdateCursor(e.GetPosition(this));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        var captured = IsMouseCaptured;
        if (captured) ReleaseMouseCapture();
        if (routingToBackground)
        {
            routingToBackground = false;
            if (captured) OnBackgroundMouse?.Invoke(e, BackgroundMouse.Up);
            return;
        }
        Controller.MouseUp();
        UpdateCursor(e.GetPosition(this));
        InvalidateVisual();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (Controller.IsDragging) Controller.MouseUp();
    }

    void UpdateCursor(Point viewPoint)
    {
        Cursor = Controller.CursorAt(ToCanvas(viewPoint), Tolerance) ?? IdleCursor();
    }

    // MARK: Keyboard

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || e.OriginalSource is TextBox) return;
        if (ProcessKey(e)) e.Handled = true;
    }

    /// <summary>Runs a key through the host's handler and the shared editing shortcuts.</summary>
    public bool ProcessKey(KeyEventArgs e)
    {
        if (editingId is not null) return false;
        if (OnKey?.Invoke(e) == true) return true;
        if (EditorShortcuts.Handle(e, Controller))
        {
            InvalidateVisual();
            return true;
        }
        return false;
    }

    // MARK: Text editing

    public bool IsEditingText => editingId is not null;

    void BeginTextEditing(Guid id)
    {
        if (editingId is not null) EndTextEditing();
        if (Controller.Find(id) is not { } a) return;
        editingId = id;
        var field = new TextBox
        {
            Template = plainTextBox,
            AcceptsReturn = true,
            AcceptsTab = false,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = AnnotationRenderer.FontFamily,
            FontWeight = FontWeights.SemiBold,
            MinHeight = 0,
            MinWidth = 0,
            Text = a.Text,
            FocusVisualStyle = null,
        };
        SpellCheck.SetIsEnabled(field, false);
        field.TextChanged += (_, _) =>
        {
            if (editingId is { } current) Controller.Update(current, x => x.Text = field.Text, notify: false);
            StyleTextBox();
        };
        field.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape || (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)))
            {
                e.Handled = true;
                EndTextEditing();
            }
        };
        field.LostKeyboardFocus += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            if (textBox == field && !field.IsKeyboardFocusWithin) EndTextEditing();
        });
        Children.Add(field);
        textBox = field;
        StyleTextBox();
        Dispatcher.BeginInvoke(() =>
        {
            if (textBox != field) return;
            field.Focus();
            Keyboard.Focus(field);
            field.CaretIndex = field.Text.Length;
        }, System.Windows.Threading.DispatcherPriority.Input);
        InvalidateVisual();
    }

    /// <summary>Keeps the live text field matching the mark it edits (font, colours, position).</summary>
    void StyleTextBox()
    {
        if (editingId is not { } id || Controller.Find(id) is not { } a || textBox is not { } field) return;
        var s = Scale;
        var pad = AnnotationRenderer.TextPadding(a.FontSize);
        var ink = a.Filled ? a.Color.Contrasting : a.Color;
        field.FontSize = Math.Max(1, a.FontSize * s);
        field.Foreground = ink.Brush();
        field.CaretBrush = ink.Brush();
        field.Background = a.Filled ? a.Color.Brush() : Brushes.Transparent;
        field.BorderThickness = new Thickness(a.Filled ? 0 : 1);
        field.BorderBrush = Theme.Frozen(Color.FromArgb(0xB3, Theme.AccentColor.R, Theme.AccentColor.G, Theme.AccentColor.B));
        // The text view inside a TextBox adds 2 px on each side; take it out of the padding.
        var border = a.Filled ? 0 : 1;
        field.Padding = new Thickness(Math.Max(0, pad.Width * s - 2 - border), Math.Max(0, pad.Height * s - border), 0, 0);
        var frame = ToView(a.Bounds);
        field.Width = Math.Ceiling(frame.Width + 6);
        field.Height = Math.Ceiling(frame.Height);
        SetLeft(field, Math.Round(frame.X));
        SetTop(field, Math.Round(frame.Y));
    }

    public void EndTextEditing()
    {
        if (editingId is not { } id || textBox is not { } field) return;
        editingId = null;
        textBox = null;
        var text = field.Text;
        Children.Remove(field);
        Controller.FinishTextEditing(id, text);
        Focus();
        Keyboard.Focus(this);
        InvalidateVisual();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        StyleTextBox();
    }
}
