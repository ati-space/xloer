using System.Windows.Controls.Primitives;

namespace FastShot;

/// <summary>The two floating bars of the capture overlay: drawing tools beside the region, actions below it.</summary>
public static class OverlayToolbars
{
    public static FrameworkElement Palette(ToolState tools, Action undo, Action redo)
    {
        var p = UiPalette.Hud;
        var grid = new UniformGrid { Columns = 2, HorizontalAlignment = HorizontalAlignment.Center };
        var buttons = new Dictionary<EditorTool, IconButton>();
        foreach (var tool in EditorTools.All)
        {
            var chosen = tool;
            var button = new IconButton(new IconView(tool, 17), tool.Help(), () => tools.Tool = chosen, p) { Margin = new Thickness(1.5) };
            buttons[tool] = button;
            grid.Children.Add(button);
        }
        var filled = new IconButton(new IconView(IconShape.SquareFilled, 14), "Dolgulu şekil / etiket", () => tools.Filled = !tools.Filled, p, 24);
        var undoButton = new IconButton(new IconView(Icons.Undo, 13), "Geri al (Ctrl+Z)", undo, p, 24);
        var redoButton = new IconButton(new IconView(Icons.Redo, 13), "Yinele (Ctrl+Y)", redo, p, 24);
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 0) };
        row.Children.Add(filled);
        row.Children.Add(undoButton);
        row.Children.Add(redoButton);

        var stack = new StackPanel();
        stack.Children.Add(grid);
        stack.Children.Add(Ui.HSeparator(p));
        stack.Children.Add(new ColorSwatches(tools, RGBAColor.Palette.Take(8), 4, 10) { HorizontalAlignment = HorizontalAlignment.Center });
        stack.Children.Add(Ui.HSeparator(p));
        stack.Children.Add(new SizePicker(tools, p, 16));
        stack.Children.Add(row);

        void Refresh()
        {
            foreach (var (tool, button) in buttons) button.Selected = tools.Tool == tool;
            filled.Selected = tools.Filled;
            undoButton.IsEnabled = tools.CanUndo;
            redoButton.IsEnabled = tools.CanRedo;
        }
        tools.Changed += Refresh;
        Refresh();
        return Ui.Hud(stack);
    }

    public static FrameworkElement ActionBar(CaptureSession session)
    {
        var p = UiPalette.Hud;
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        void Add(UIElement element) => row.Children.Add(element);
        if (session.Mode == CaptureSession.CaptureMode.Screenshot)
        {
            Add(new IconButton(new IconView(Icons.Copy), "Panoya kopyala (Ctrl+C veya Enter)", session.Copy, p));
            Add(new IconButton(new IconView(Icons.Save), "Kaydet (Ctrl+S)", session.Save, p));
            Add(new IconButton(new IconView(Icons.SaveAs), "Farklı kaydet… (Ctrl+Shift+S)", session.SaveAs, p));
            Add(Ui.VSeparator(p));
            Add(new IconButton(new IconView(Icons.Pin), "Ekrana sabitle (Ctrl+P)", session.Pin, p));
            Add(new IconButton(new IconView(Icons.Scan), "Metni tanı ve kopyala (Ctrl+T)", session.RecognizeText, p));
            Add(new IconButton(new IconView(Icons.Edit), "Düzenleyicide aç (Ctrl+E)", session.OpenEditor, p));
            Add(new IconButton(new IconView(Icons.Record), "Bu alanı videoya kaydet (Ctrl+R)", session.Record, p, tint: Theme.Red));
            Add(Ui.VSeparator(p));
            Add(new IconButton(new IconView(Icons.Close), "Kapat (Esc)", session.Cancel, p));
        }
        else
        {
            var prefs = Preferences.Shared;
            IconButton? mic = null, sound = null, cursor = null;
            void Refresh()
            {
                mic!.SetIcon(new IconView(prefs.RecordMicrophone ? Icons.Mic : Icons.MicOff));
                mic.Selected = prefs.RecordMicrophone;
                sound!.SetIcon(new IconView(prefs.RecordSystemAudio ? Icons.Volume : Icons.Mute));
                sound.Selected = prefs.RecordSystemAudio;
                cursor!.Selected = prefs.ShowCursor;
            }
            mic = new IconButton(new IconView(Icons.MicOff), "Mikrofon", () => { prefs.RecordMicrophone = !prefs.RecordMicrophone; Refresh(); }, p);
            sound = new IconButton(new IconView(Icons.Mute), "Sistem sesi", () => { prefs.RecordSystemAudio = !prefs.RecordSystemAudio; Refresh(); }, p);
            cursor = new IconButton(new IconView(Icons.Mouse), "İmleci göster", () => { prefs.ShowCursor = !prefs.ShowCursor; Refresh(); }, p);
            Refresh();
            Add(mic);
            Add(sound);
            Add(cursor);
            Add(Ui.VSeparator(p));
            Add(new PillButton("Kaydı Başlat", new IconView(IconShape.Dot, 14), "Kaydı başlat (Enter)", Theme.Red, session.Record) { Margin = new Thickness(2, 0, 2, 0) });
            Add(new IconButton(new IconView(Icons.Close), "İptal (Esc)", session.Cancel, p));
        }
        return Ui.Hud(row, 10, 5);
    }
}
