using System.Globalization;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using FastShot.Core.Convert;

namespace FastShot;

/// <summary>The ring that opens around the pointer: convert, tools or actions for the dragged files.
/// Tap another wheel's key (or use the bar above the ring) to switch wheels; drop on a segment, or click one, to run it.</summary>
public static class ConversionWheel
{
    static PanelWindow? panel;
    static WheelView? view;
    static DispatcherTimer? hideTimer;

    public static bool IsVisible => panel is not null;

    /// <param name="cursor">Physical screen pixels.</param>
    /// <param name="interactive">Interactive wheels are clicked; the others wait for a drop.</param>
    public static void Show(IReadOnlyList<string> files, Native.POINT cursor, bool interactive, WheelMode mode = WheelMode.Convert)
    {
        var wheel = new WheelView(files.ToList(), interactive);
        if (wheel.FirstAvailable(mode) is not { } start)
        {
            if (interactive) Toast.Show("Bu dosyalar için seçenek yok", Icons.Clear, "Dönüştürme ve araçlar için aynı türden dosyalar seçin");
            return;
        }
        Dismiss(animated: false);
        wheel.Mode = start;
        wheel.Chosen = (item, paths) =>
        {
            Dismiss();
            ConversionCenter.Shared.Perform(item, paths);
        };
        wheel.Cancelled = () => Dismiss();

        var monitor = MonitorInfo.At(cursor.X, cursor.Y);
        var window = new PanelWindow(wheel, Bounds(cursor, monitor, monitor.Scale), transparent: true, activatable: interactive);
        // Let WPF lay the wheel out at the window's real DPI.
        if (Math.Abs(window.Scale - monitor.Scale) > 0.01) window.SetBounds(Bounds(cursor, monitor, window.Scale));
        window.Show(activate: interactive);
        if (interactive)
        {
            wheel.Focus();
            Keyboard.Focus(wheel);
            window.Source.AddHook((IntPtr _, int message, IntPtr wParam, IntPtr _, ref bool _) =>
            {
                const int WM_ACTIVATE = 0x0006;
                if (message == WM_ACTIVATE && (wParam.ToInt64() & 0xFFFF) == 0) wheel.Dispatcher.BeginInvoke(() => { if (view == wheel) Dismiss(); });
                return IntPtr.Zero;
            });
        }
        wheel.Appear();
        panel = window;
        view = wheel;
    }

    /// <summary>The ring is centred just below the pointer, with the mode bar above it; the window stays on the screen.</summary>
    static Native.RECT Bounds(Native.POINT cursor, MonitorInfo monitor, double scale)
    {
        var width = (int)Math.Ceiling(WheelView.WindowWidth * scale);
        var height = (int)Math.Ceiling(WheelView.WindowHeight * scale);
        var slack = (int)(WheelView.Rim * scale);
        var work = monitor.Work;
        var left = Math.Min(Math.Max(work.Left - slack, cursor.X - width / 2), work.Right - width + slack);
        var top = Math.Min(Math.Max(work.Top, cursor.Y - (int)(WheelView.Side / 2 * scale)), work.Bottom - height + slack);
        return new Native.RECT(left, top, left + width, top + height);
    }

    /// <summary>The wheel key was tapped again while the wheel is open.</summary>
    public static void NextMode() => view?.SelectNext();

    /// <summary>The mouse went up; give a drop on the wheel time to arrive before closing it.</summary>
    public static void DragEnded()
    {
        if (panel is null || view is null || view.Interactive) return;
        hideTimer?.Stop();
        hideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        hideTimer.Tick += (_, _) =>
        {
            hideTimer?.Stop();
            Dismiss();
        };
        hideTimer.Start();
    }

    public static void Dismiss(bool animated = true)
    {
        hideTimer?.Stop();
        if (panel is not { } window || view is not { } wheel) return;
        panel = null;
        view = null;
        wheel.Close();
        if (!animated)
        {
            window.Dispose();
            return;
        }
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(100));
        fade.Completed += (_, _) => window.Dispose();
        wheel.BeginAnimation(UIElement.OpacityProperty, fade);
    }
}

/// <summary>Draws the mode bar, the disc, the segments and the hub, and does its own hit testing so it matches the drawing exactly.</summary>
public sealed class WheelView : FrameworkElement
{
    public const double OuterRadius = 166, InnerRadius = 66, Rim = 34, Gap = 7;
    public const double BarHeight = 58, PillWidth = 118, PillHeight = 34, PillSpacing = 6, BarPadding = 4;
    public static double Side => (OuterRadius + Rim) * 2;
    public static double WindowWidth => Side;
    public static double WindowHeight => Side + BarHeight;
    static Point Center => new(Side / 2, BarHeight + Side / 2);
    static double BarWidth => PillWidth * 3 + PillSpacing * 2 + BarPadding * 2;

    static Rect PillFrame(int index) =>
        new((Side - BarWidth) / 2 + BarPadding + index * (PillWidth + PillSpacing), (BarHeight - PillHeight) / 2 + 2, PillWidth, PillHeight);

    static readonly WheelMode[] modes = Enum.GetValues<WheelMode>();

    public List<string> Files { get; }
    public bool Interactive { get; }
    public Action<WheelItem, List<string>>? Chosen { get; set; }
    public Action? Cancelled { get; set; }

    readonly Dictionary<WheelMode, List<WheelItem>> itemsByMode;
    readonly FileFamily? family;
    readonly bool singleFolder;
    WheelMode mode;
    int? hovered;
    BitmapSource? thumbnail;
    readonly ScaleTransform zoom = new(0.82, 0.82, Side / 2, BarHeight + Side / 2);
    readonly bool dark = Theme.IsDark;
    readonly DispatcherTimer spring = new() { Interval = TimeSpan.FromMilliseconds(350) };
    int? springPill;
    bool closed;

    /// <summary>0 → 1 as a newly chosen wheel's ring fades and grows in.</summary>
    public static readonly DependencyProperty RingProgressProperty =
        DependencyProperty.Register(nameof(RingProgress), typeof(double), typeof(WheelView), new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public double RingProgress
    {
        get => (double)GetValue(RingProgressProperty);
        set => SetValue(RingProgressProperty, value);
    }

    public WheelView(List<string> files, bool interactive)
    {
        Files = files;
        Interactive = interactive;
        itemsByMode = modes.ToDictionary(m => m, m => WheelItem.For(m, files));
        family = files.Select(FileConverter.Family).FirstOrDefault(f => f is not null);
        singleFolder = files.Count == 1 && Directory.Exists(files[0]);
        Width = WindowWidth;
        Height = WindowHeight;
        Focusable = true;
        FocusVisualStyle = null;
        AllowDrop = true;
        RenderTransform = zoom;
        Opacity = 0;
        spring.Tick += (_, _) =>
        {
            spring.Stop();
            if (springPill is { } index) Select(modes[index]);
        };
        _ = LoadThumbnail();
    }

    public WheelMode Mode
    {
        get => mode;
        set
        {
            mode = value;
            hovered = null;
            InvalidateVisual();
        }
    }

    List<WheelItem> Items => itemsByMode[mode];
    bool IsAvailable(WheelMode m) => itemsByMode[m].Count > 0;

    /// <summary>The requested wheel, or the next one that has something to offer.</summary>
    public WheelMode? FirstAvailable(WheelMode from)
    {
        var start = Array.IndexOf(modes, from);
        for (var i = 0; i < modes.Length; i++)
        {
            var candidate = modes[(start + i) % modes.Length];
            if (IsAvailable(candidate)) return candidate;
        }
        return null;
    }

    /// <summary>The next wheel that has something for these files, round the three.</summary>
    public void SelectNext()
    {
        var start = Array.IndexOf(modes, mode);
        for (var i = 1; i < modes.Length; i++)
        {
            var candidate = modes[(start + i) % modes.Length];
            if (IsAvailable(candidate))
            {
                Select(candidate);
                return;
            }
        }
    }

    public void Select(WheelMode m)
    {
        if (closed || m == mode || !IsAvailable(m)) return;
        Mode = m;
        BeginAnimation(RingProgressProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    public void Close()
    {
        closed = true;
        spring.Stop();
    }

    async Task LoadThumbnail()
    {
        var path = Files[0];
        thumbnail = await Task.Run(() => ShellThumbnail.Get(path, 96));
        InvalidateVisual();
    }

    public void Appear()
    {
        var bounce = new ElasticEase { Oscillations = 1, Springiness = 6, EasingMode = EasingMode.EaseOut };
        zoom.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.82, 1, TimeSpan.FromMilliseconds(280)) { EasingFunction = bounce });
        zoom.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.82, 1, TimeSpan.FromMilliseconds(280)) { EasingFunction = bounce });
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120)));
    }

    double Step => 2 * Math.PI / Math.Max(1, Items.Count);
    /// <summary>The first segment is centred at the top; segments run clockwise.</summary>
    double Start => -Math.PI / 2 - Step / 2;
    double MidAngle(int index) => Start + (index + 0.5) * Step;

    int? Segment(Point p)
    {
        var dx = p.X - Center.X;
        var dy = p.Y - Center.Y;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        if (Items.Count == 0 || distance < InnerRadius - 4 || distance > OuterRadius + 14) return null;
        var angle = Math.Atan2(dy, dx) - Start;
        while (angle < 0) angle += 2 * Math.PI;
        return (int)(angle / Step) % Items.Count;
    }

    static int? Pill(Point p)
    {
        for (var i = 0; i < modes.Length; i++)
        {
            var frame = PillFrame(i);
            frame.Inflate(3, 6);
            if (frame.Contains(p)) return i;
        }
        return null;
    }

    void SetHovered(int? index)
    {
        if (hovered == index) return;
        hovered = index;
        InvalidateVisual();
    }

    // MARK: Drawing

    string FamilyGlyph => singleFolder ? Icons.Folder : family switch
    {
        FileFamily.Image => "",
        FileFamily.Video => "",
        FileFamily.Audio => "",
        FileFamily.Pdf => "",
        _ => "",
    };

    string SourceLabel
    {
        get
        {
            if (singleFolder) return "Klasör";
            var extensions = Files.Select(f => Path.GetExtension(f).TrimStart('.').ToUpperInvariant()).Distinct().ToList();
            if (extensions is [{ Length: > 0 } only]) return only;
            return family?.Title() ?? "Öğe";
        }
    }

    FormattedText Text(string text, double size, Brush brush, FontWeight weight, FontFamily? fontFamily = null) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(fontFamily ?? Theme.UiFont, FontStyles.Normal, weight, FontStretches.Normal),
            size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    /// <summary>Shortens <paramref name="text"/> with an ellipsis until it fits <paramref name="width"/>.</summary>
    FormattedText Fitted(string text, double size, Brush brush, FontWeight weight, double width)
    {
        var result = Text(text, size, brush, weight);
        for (var length = text.Length - 1; result.Width > width && length > 1; length--)
            result = Text(text[..length].TrimEnd() + "…", size, brush, weight);
        return result;
    }

    static StreamGeometry Sector(double start, double end, double inner, double outer, Point center)
    {
        Point At(double angle, double radius) => new(center.X + Math.Cos(angle) * radius, center.Y + Math.Sin(angle) * radius);
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            var large = end - start > Math.PI;
            ctx.BeginFigure(At(start, outer), true, true);
            ctx.ArcTo(At(end, outer), new Size(outer, outer), 0, large, SweepDirection.Clockwise, true, true);
            ctx.LineTo(At(end, inner), true, true);
            ctx.ArcTo(At(start, inner), new Size(inner, inner), 0, large, SweepDirection.Counterclockwise, true, true);
        }
        geometry.Freeze();
        return geometry;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var foreground = dark ? Brushes.White : Theme.Frozen(Color.FromRgb(0x1C, 0x1C, 0x1E));
        var secondary = Theme.Frozen(dark ? Color.FromArgb(0xA6, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x99, 0, 0, 0));
        var disc = Theme.Frozen(dark ? Color.FromArgb(0xEB, 0x26, 0x26, 0x29) : Color.FromArgb(0xF0, 0xF7, 0xF7, 0xF8));
        var hairline = new Pen(Theme.Frozen(dark ? Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x1F, 0, 0, 0)), 1);

        DrawBar(dc, foreground, disc, hairline);

        // Soft shadow under the disc.
        for (var i = 6; i >= 1; i--)
            dc.DrawEllipse(Theme.Frozen(Color.FromArgb((byte)(10 + (6 - i) * 3), 0, 0, 0)), null, new Point(Center.X, Center.Y + 7), OuterRadius + i * 3.2, OuterRadius + i * 3.2);
        dc.DrawEllipse(disc, hairline, Center, OuterRadius, OuterRadius);

        var progress = RingProgress;
        var grow = 0.94 + 0.06 * progress;
        dc.PushTransform(new ScaleTransform(grow, grow, Center.X, Center.Y));
        dc.PushOpacity(progress);
        var items = Items;
        var count = items.Count;
        var midRadius = (InnerRadius + OuterRadius) / 2;
        var gapAngle = Gap / midRadius / 2;
        for (var i = 0; i < count; i++)
        {
            var item = items[i];
            var tint = item.Tint;
            var isHovered = hovered == i;
            var start = Start + i * Step;
            var mid = MidAngle(i);
            var lift = isHovered ? 7.0 : 0;
            var center = new Point(Center.X + Math.Cos(mid) * lift, Center.Y + Math.Sin(mid) * lift);
            var sector = Sector(start + gapAngle, start + Step - gapAngle, InnerRadius + 8, OuterRadius - 8, center);
            if (isHovered)
            {
                var glow = new Pen(Theme.Frozen(Color.FromArgb(0x55, tint.R, tint.G, tint.B)), 8) { LineJoin = PenLineJoin.Round };
                dc.DrawGeometry(null, glow, sector);
                var fill = new LinearGradientBrush(tint, Color.FromArgb(0xC8, tint.R, tint.G, tint.B), 90);
                fill.Freeze();
                dc.DrawGeometry(fill, null, sector);
            }
            else
            {
                dc.DrawGeometry(Theme.Frozen(Color.FromArgb(dark ? (byte)0x2E : (byte)0x24, tint.R, tint.G, tint.B)), null, sector);
            }

            var compact = count > 7;
            var maxWidth = compact ? 82.0 : 96;
            var labelCenter = new Point(center.X + Math.Cos(mid) * (midRadius + 2), center.Y + Math.Sin(mid) * (midRadius + 2));
            var icon = Text(item.Glyph, compact ? 12 : 13, isHovered ? Theme.Frozen(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF)) : Theme.Frozen(tint), FontWeights.Normal, Theme.IconFont);
            var title = Fitted(item.Title, compact ? 13 : 14.5, isHovered ? Brushes.White : foreground, FontWeights.Bold, maxWidth);
            var detail = item.Detail is { } d ? Fitted(d, 9.5, isHovered ? Theme.Frozen(Color.FromArgb(0xD9, 0xFF, 0xFF, 0xFF)) : secondary, FontWeights.SemiBold, maxWidth) : null;
            var height = icon.Height + title.Height + (detail?.Height ?? 0) - 4;
            var y = labelCenter.Y - height / 2;
            dc.DrawText(icon, new Point(labelCenter.X - icon.Width / 2, y));
            y += icon.Height - 3;
            dc.DrawText(title, new Point(labelCenter.X - title.Width / 2, y));
            y += title.Height - 2;
            if (detail is not null) dc.DrawText(detail, new Point(labelCenter.X - detail.Width / 2, y));
        }
        dc.Pop();
        dc.Pop();

        DrawHub(dc, foreground, secondary);
    }

    void DrawBar(DrawingContext dc, Brush foreground, Brush disc, Pen hairline)
    {
        var bar = Rect.Union(PillFrame(0), PillFrame(modes.Length - 1));
        bar.Inflate(BarPadding, BarPadding);
        for (var i = 3; i >= 1; i--)
        {
            var shadow = bar;
            shadow.Inflate(i * 2, i * 2);
            shadow.Offset(0, 3);
            dc.DrawRoundedRectangle(Theme.Frozen(Color.FromArgb((byte)(8 + (3 - i) * 4), 0, 0, 0)), null, shadow, shadow.Height / 2, shadow.Height / 2);
        }
        dc.DrawRoundedRectangle(disc, hairline, bar, bar.Height / 2, bar.Height / 2);
        for (var i = 0; i < modes.Length; i++)
        {
            var m = modes[i];
            var frame = PillFrame(i);
            var active = m == mode;
            dc.PushOpacity(IsAvailable(m) ? 1 : 0.35);
            if (active) dc.DrawRoundedRectangle(Theme.Accent, null, frame, frame.Height / 2, frame.Height / 2);
            var color = active ? Brushes.White : foreground;
            var glyph = Text(ModeGlyph(m), 13, color, FontWeights.Normal, Theme.IconFont);
            var title = Text(m.Title(), 12.5, color, FontWeights.SemiBold);
            var x = frame.X + (frame.Width - (glyph.Width + 7 + title.Width)) / 2;
            dc.DrawText(glyph, new Point(x, frame.Y + (frame.Height - glyph.Height) / 2));
            dc.DrawText(title, new Point(x + glyph.Width + 7, frame.Y + (frame.Height - title.Height) / 2));
            dc.Pop();
        }
    }

    static string ModeGlyph(WheelMode m) => m switch
    {
        WheelMode.Tools => "\uE90F",
        WheelMode.Actions => "\uE945",
        _ => "\uE8AB",
    };

    void DrawHub(DrawingContext dc, Brush foreground, Brush secondary)
    {
        var radius = InnerRadius - 4;
        var item = hovered is { } index && index < Items.Count ? Items[index] : null;
        var ring = item is null
            ? new Pen(Theme.Frozen(dark ? Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x1F, 0, 0, 0)), 1)
            : new Pen(Theme.Frozen(Color.FromArgb(0xB3, item.Tint.R, item.Tint.G, item.Tint.B)), 2);
        dc.DrawEllipse(Theme.Frozen(dark ? Color.FromRgb(0x1E, 0x1E, 0x20) : Colors.White), ring, Center, radius, radius);

        var previewTop = Center.Y - 34;
        if (thumbnail is not null)
        {
            var scale = Math.Min(46 / thumbnail.Width, 40 / thumbnail.Height);
            var size = new Size(thumbnail.Width * scale, thumbnail.Height * scale);
            var rect = new Rect(Center.X - size.Width / 2, previewTop + (40 - size.Height) / 2, size.Width, size.Height);
            dc.PushClip(new RectangleGeometry(rect, 5, 5));
            dc.DrawImage(thumbnail, rect);
            dc.Pop();
        }
        else
        {
            var glyph = Text(FamilyGlyph, 22, secondary, FontWeights.Normal, Theme.IconFont);
            dc.DrawText(glyph, new Point(Center.X - glyph.Width / 2, previewTop + (40 - glyph.Height) / 2));
        }
        if (Files.Count > 1)
        {
            var badge = Text(Files.Count.ToString(CultureInfo.InvariantCulture), 9.5, Brushes.White, FontWeights.Bold);
            var box = new Rect(Center.X + 18, previewTop - 6, Math.Max(16, badge.Width + 9), 15);
            dc.DrawRoundedRectangle(Theme.Accent, null, box, 7.5, 7.5);
            dc.DrawText(badge, new Point(box.X + (box.Width - badge.Width) / 2, box.Y + (box.Height - badge.Height) / 2));
        }

        var line = Center.Y + 12;
        var width = (radius - 7) * 2;
        if (item is { Format: not null })
        {
            var source = Text(SourceLabel, 11, secondary, FontWeights.Bold);
            var arrow = Text(" → ", 11, secondary, FontWeights.Bold);
            var to = Text(item.Title, 11, Theme.Frozen(item.Tint), FontWeights.Bold);
            var total = source.Width + arrow.WidthIncludingTrailingWhitespace + to.Width;
            var x = Center.X - total / 2;
            dc.DrawText(source, new Point(x, line));
            dc.DrawText(arrow, new Point(x + source.Width, line));
            dc.DrawText(to, new Point(x + source.Width + arrow.WidthIncludingTrailingWhitespace, line));
        }
        else if (item is not null)
        {
            var title = Fitted(item.Title, 11, Theme.Frozen(item.Tint), FontWeights.Bold, width);
            dc.DrawText(title, new Point(Center.X - title.Width / 2, line));
        }
        else
        {
            var label = Fitted(Files.Count == 1 ? SourceLabel : $"{Files.Count} öğe", 11, foreground, FontWeights.Bold, width);
            dc.DrawText(label, new Point(Center.X - label.Width / 2, line));
            var hint = Text(Interactive ? "Seçin" : "Üzerine bırakın", 9.5, secondary, FontWeights.SemiBold);
            dc.DrawText(hint, new Point(Center.X - hint.Width / 2, line + label.Height));
        }
    }

    // MARK: Input

    protected override HitTestResult HitTestCore(PointHitTestParameters parameters)
    {
        // The whole window catches the mouse so a click beside the ring cancels instead of falling through.
        return new PointHitTestResult(this, parameters.HitPoint);
    }

    protected override void OnMouseMove(MouseEventArgs e) => SetHovered(Segment(e.GetPosition(this)));

    protected override void OnMouseLeave(MouseEventArgs e) => SetHovered(null);

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        e.Handled = true;
        var p = e.GetPosition(this);
        if (Pill(p) is { } pill)
        {
            Select(modes[pill]);
            return;
        }
        if (Segment(p) is { } index && index < Items.Count) Chosen?.Invoke(Items[index], Files);
        else Cancelled?.Invoke();
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        e.Handled = true;
        Cancelled?.Invoke();
    }

    /// <summary>In a clicked wheel, tapping the wheel key goes to the next wheel, just like during a drag.</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape)
        {
            e.Handled = true;
            Cancelled?.Invoke();
            return;
        }
        if (e.IsRepeat) return;
        var wheelKey = Preferences.Shared.WheelKey switch
        {
            // AltGr arrives as the right Alt key (after a left Ctrl Windows adds for it).
            ConvertModifier.AltGr => key == Key.RightAlt,
            ConvertModifier.Shift => key is Key.LeftShift or Key.RightShift,
            ConvertModifier.Control => key is Key.LeftCtrl or Key.RightCtrl,
            _ => key is Key.LeftAlt or Key.RightAlt,
        };
        if (!wheelKey) return;
        e.Handled = true;
        SelectNext();
    }

    protected override void OnDragEnter(DragEventArgs e) => DragOverWheel(e);
    protected override void OnDragOver(DragEventArgs e) => DragOverWheel(e);

    protected override void OnDragLeave(DragEventArgs e)
    {
        SetHovered(null);
        SpringLoad(null);
        e.Handled = true;
    }

    /// <summary>Hovering a mode pill with the files for a moment switches to that wheel.</summary>
    void SpringLoad(int? pill)
    {
        if (pill == springPill) return;
        springPill = pill;
        spring.Stop();
        if (pill is not null) spring.Start();
    }

    void DragOverWheel(DragEventArgs e)
    {
        var p = e.GetPosition(this);
        SpringLoad(Pill(p));
        SetHovered(Segment(p));
        // Always copy: the source files must never be moved, whatever modifier is held.
        e.Effects = hovered is not null && e.Data.GetDataPresent(DataFormats.FileDrop) && e.AllowedEffects.HasFlag(DragDropEffects.Copy)
            ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    protected override void OnDrop(DragEventArgs e)
    {
        e.Handled = true;
        SpringLoad(null);
        var index = Segment(e.GetPosition(this));
        if (index is null || index >= Items.Count)
        {
            e.Effects = DragDropEffects.None;
            return;
        }
        e.Effects = DragDropEffects.Copy;
        var item = Items[index.Value];
        var dropped = (e.Data.GetData(DataFormats.FileDrop) as string[] ?? []).Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
        Dispatcher.BeginInvoke(() => Chosen?.Invoke(item, dropped.Count > 0 ? dropped : Files));
    }
}
