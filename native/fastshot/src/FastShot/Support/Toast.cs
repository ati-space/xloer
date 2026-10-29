using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace FastShot;

/// <summary>A short confirmation near the bottom of the screen under the pointer.</summary>
public static class Toast
{
    public const string Check = Icons.Check;
    static PanelWindow? panel;
    static DispatcherTimer? timer;

    public static void Show(string message, string glyph = Check, string? detail = null, string? actionTitle = null, Action? action = null)
    {
        timer?.Stop();
        panel?.Dispose();
        panel = null;

        var p = UiPalette.Hud;
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new IconView(glyph, 18) { Brush = Theme.Accent, Margin = new Thickness(0, 0, 10, 0) });
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, MaxWidth = 300 };
        texts.Children.Add(Ui.Label(message, p, 13, weight: FontWeights.SemiBold));
        if (detail is not null)
        {
            var label = Ui.Label(detail, p, 11, secondary: true);
            label.TextTrimming = TextTrimming.CharacterEllipsis;
            texts.Children.Add(label);
        }
        row.Children.Add(texts);
        PanelWindow? shown = null;
        if (actionTitle is not null && action is not null)
        {
            row.Children.Add(new PillButton(actionTitle, null, actionTitle, Theme.Accent, () =>
            {
                action();
                Dismiss(shown);
            }) { Margin = new Thickness(12, 0, 0, 0), Padding = new Thickness(10, 3, 10, 3) });
        }
        var content = Ui.Hud(row, 14, 10);
        content.Padding = new Thickness(14, 10, 14, 10);
        content.Margin = new Thickness(14);
        content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = content.DesiredSize;

        var monitor = MonitorInfo.AtCursor();
        var scale = monitor.Scale;
        var width = (int)Math.Ceiling(size.Width * scale);
        var height = (int)Math.Ceiling(size.Height * scale);
        var work = monitor.Work;
        var left = work.Left + (work.Width - width) / 2;
        var top = work.Bottom - (int)(56 * scale) - height;
        shown = new PanelWindow(content, new Native.RECT(left, top, left + width, top + height), transparent: true, activatable: false,
                                clickThrough: action is null);
        content.Opacity = 0;
        shown.Show(activate: false);
        content.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));
        panel = shown;

        timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(action is null ? 2.2 : 5) };
        timer.Tick += (_, _) =>
        {
            timer?.Stop();
            Dismiss(shown);
        };
        timer.Start();
    }

    static void Dismiss(PanelWindow? target)
    {
        if (target is null || target.IsDisposed) return;
        if (panel == target) panel = null;
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
        fade.Completed += (_, _) => target.Dispose();
        target.Content.BeginAnimation(UIElement.OpacityProperty, fade);
    }
}
