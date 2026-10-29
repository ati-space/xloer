using System.Windows.Threading;

namespace FastShot;

/// <summary>Shared parts of the image and video editors: the canvas, its toolbar, autosaving the
/// project and exporting it as an editable .fastshot package.</summary>
public abstract class EditorWindow : Window
{
    public CaptureProject Project { get; protected set; }
    protected ProjectLibrary Library { get; }
    protected ToolState Tools { get; } = new(EditorTool.Arrow);
    protected AnnotationController Controller { get; }
    protected CanvasView Canvas { get; }
    protected UiPalette Palette { get; } = UiPalette.Editor;
    public Action<EditorWindow>? OnClose { get; set; }

    readonly Border bottomHost = new();
    readonly ProgressBar progress = new() { Width = 90, Height = 4, Minimum = 0, Maximum = 1, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock progressText = new() { Margin = new Thickness(6, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center, FontSize = 11 };
    readonly StackPanel progressRow = new() { Orientation = Orientation.Horizontal, Visibility = Visibility.Collapsed };
    readonly List<UIElement> exportLocked = [];
    readonly DispatcherTimer saveTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    protected bool Exporting { get; private set; }

    protected EditorWindow(CaptureProject project, ProjectLibrary library, string title)
    {
        Project = project;
        Library = library;
        Title = title;
        Icon = AppIcon.WindowIcon;
        Controller = new AnnotationController(Tools);
        Canvas = new CanvasView(Controller, project.CanvasSize) { Letterbox = Palette.Background, IdleCursor = () => Cursors.Arrow };
        Canvas.OnKey = HandleKey;
        Controller.Load(project.Annotations);
        Controller.OnChange = ScheduleSave;
        saveTimer.Tick += (_, _) => SaveProject();

        var toolbar = BuildToolbar();
        var root = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(toolbar, Dock.Top);
        DockPanel.SetDock(bottomHost, Dock.Bottom);
        root.Children.Add(toolbar);
        root.Children.Add(bottomHost);
        root.Children.Add(Canvas);
        Content = root;
        Background = Palette.Background;

        // Open at the capture's size when it fits, leaving room for chrome.
        var work = SystemParameters.WorkArea;
        var chrome = new Size(40, 56 + ExtraHeight + 60);
        var size = project.CanvasSize;
        var fit = Math.Min(1, Math.Min((work.Width * 0.9 - chrome.Width) / Math.Max(1, size.Width), (work.Height * 0.9 - chrome.Height) / Math.Max(1, size.Height)));
        MinWidth = 900;
        MinHeight = 480;
        Width = Math.Max(MinWidth, size.Width * fit + chrome.Width);
        Height = Math.Max(MinHeight, size.Height * fit + chrome.Height);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        KeyDown += (_, e) =>
        {
            if (e.Handled || e.OriginalSource is TextBox || e.OriginalSource is ComboBox || e.OriginalSource is ComboBoxItem) return;
            if (Canvas.ProcessKey(e)) e.Handled = true;
        };
        Closing += (_, _) =>
        {
            Canvas.EndTextEditing();
            SaveProject();
        };
        Closed += (_, _) => OnClose?.Invoke(this);
    }

    /// <summary>Extra vertical space subclasses add below the canvas.</summary>
    protected virtual double ExtraHeight => 0;
    protected virtual bool SupportsGif => false;

    protected UIElement? Bottom
    {
        get => bottomHost.Child;
        set => bottomHost.Child = value;
    }

    public void Present()
    {
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Canvas.Focus();
        Keyboard.Focus(Canvas);
    }

    // MARK: Toolbar

    FrameworkElement BuildToolbar()
    {
        var p = Palette;
        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var toolButtons = new Dictionary<EditorTool, IconButton>();
        foreach (var tool in EditorTools.All)
        {
            var chosen = tool;
            var button = new IconButton(new IconView(tool, 17), tool.Help(), () => Tools.Tool = chosen, p, 32) { Margin = new Thickness(1, 0, 1, 0) };
            toolButtons[tool] = button;
            left.Children.Add(button);
        }
        left.Children.Add(Ui.VSeparator(p, 22));
        left.Children.Add(new ColorSwatches(Tools, RGBAColor.Palette.Take(8), 8, 12) { VerticalAlignment = VerticalAlignment.Center });
        left.Children.Add(new IconButton(new IconView(Icons.Palette, 15), "Özel renk", ChooseColor, p, 30));
        left.Children.Add(new SizePicker(Tools, p, 18) { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) });
        var filled = new IconButton(new IconView(IconShape.SquareFilled, 15), "Dolgulu şekil / etiket", () => Tools.Filled = !Tools.Filled, p, 30);
        left.Children.Add(filled);
        left.Children.Add(Ui.VSeparator(p, 22));
        var undo = new IconButton(new IconView(Icons.Undo, 14), "Geri al (Ctrl+Z)", Controller.Undo, p, 30);
        var redo = new IconButton(new IconView(Icons.Redo, 14), "Yinele (Ctrl+Y)", Controller.Redo, p, 30);
        var delete = new IconButton(new IconView(Icons.Delete, 14), "Seçili çizimi sil (Delete)", Controller.DeleteSelected, p, 30);
        left.Children.Add(undo);
        left.Children.Add(redo);
        left.Children.Add(delete);
        exportLocked.Add(left);

        void Refresh()
        {
            foreach (var (tool, button) in toolButtons) button.Selected = Tools.Tool == tool;
            filled.Selected = Tools.Filled;
            undo.IsEnabled = Tools.CanUndo;
            redo.IsEnabled = Tools.CanRedo;
            delete.IsEnabled = Tools.HasSelection;
        }
        Tools.Changed += Refresh;
        Refresh();

        progressRow.Children.Add(progress);
        progressText.Foreground = p.Secondary;
        progressRow.Children.Add(progressText);

        var copy = TextButton(Icons.Copy, "Kopyala", "Panoya kopyala (Ctrl+C)", CopyToClipboard);
        var save = TextButton(Icons.Save, "Kaydet", "Varsayılan klasöre kaydet (Ctrl+S)", Save);
        if (TryFindResource("AccentButtonStyle") is Style accent) save.Style = accent;
        var more = TextButton(Icons.More, null, "Diğer seçenekler", () => { });
        more.Click += (_, _) => ShowMoreMenu(more);
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(progressRow);
        right.Children.Add(copy);
        right.Children.Add(save);
        right.Children.Add(more);
        exportLocked.Add(right);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var scroller = new ScrollViewer { Content = left, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
        grid.Children.Add(scroller);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        return new Border
        {
            Child = grid,
            Background = p.Panel,
            BorderBrush = p.Border,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(10, 8, 10, 8),
        };
    }

    protected static Button TextButton(string glyph, string? title, string help, Action action)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new TextBlock { Text = glyph, FontFamily = Theme.IconFont, FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
        if (title is not null) row.Children.Add(new TextBlock { Text = title, Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        var button = new Button { Content = row, ToolTip = help, Focusable = false, Margin = new Thickness(4, 0, 0, 0), Padding = new Thickness(11, 5, 11, 6) };
        button.Click += (_, _) => action();
        return button;
    }

    void ShowMoreMenu(FrameworkElement anchor)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        void Item(string title, Action action)
        {
            var item = new MenuItem { Header = title };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }
        Item("Farklı kaydet…", SaveAs);
        if (SupportsGif) Item("GIF olarak kaydet", SaveGif);
        menu.Items.Add(new Separator());
        Item("Düzenlenebilir proje olarak dışa aktar (.fastshot)…", ExportProject);
        Item("Orijinali Gezgin'de göster", () => Output.Reveal(Library.MediaPath(Project)));
        menu.IsOpen = true;
    }

    void ChooseColor()
    {
        var c = Tools.Color.Color;
        using var dialog = new System.Windows.Forms.ColorDialog { FullOpen = true, AnyColor = true, Color = System.Drawing.Color.FromArgb(c.R, c.G, c.B) };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        Tools.Color = new RGBAColor(dialog.Color.R / 255.0, dialog.Color.G / 255.0, dialog.Color.B / 255.0);
    }

    protected void SetExporting(bool on)
    {
        Exporting = on;
        progressRow.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        foreach (var element in exportLocked) element.IsEnabled = !on;
        progressRow.IsEnabled = true;
        if (on) SetProgress(0);
    }

    protected void SetProgress(double fraction)
    {
        progress.Value = Math.Clamp(fraction, 0, 1);
        progressText.Text = $"%{(int)(fraction * 100)}";
    }

    // MARK: Saving the project

    protected void ScheduleSave()
    {
        saveTimer.Stop();
        saveTimer.Start();
    }

    protected void SaveProject()
    {
        saveTimer.Stop();
        Project.Annotations = Controller.Annotations.Select(a => a.Clone()).ToList();
        try
        {
            Library.Save(Project);
            ProjectDidSave();
            AppController.Shared.NotifyLibraryChanged();
        }
        catch (Exception error)
        {
            System.Diagnostics.Debug.WriteLine($"FastShot: could not save project: {error}");
        }
    }

    protected virtual void ProjectDidSave() { }

    // MARK: Actions for subclasses

    protected abstract void CopyToClipboard();
    protected abstract void Save();
    protected abstract void SaveAs();
    protected virtual void SaveGif() { }

    protected virtual bool HandleKey(KeyEventArgs e)
    {
        var modifiers = Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt);
        var key = EditorShortcuts.KeyOf(e);
        if (key == Key.Escape && Controller.Selected is not null)
        {
            Controller.Select(null);
            return true;
        }
        if (Exporting) return false;
        if (modifiers == ModifierKeys.Control && key == Key.C) { CopyToClipboard(); return true; }
        if (modifiers == ModifierKeys.Control && key == Key.S) { Save(); return true; }
        if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && key == Key.S) { SaveAs(); return true; }
        return false;
    }

    void ExportProject()
    {
        Canvas.EndTextEditing();
        SaveProject();
        if (SavePanel.Run(Output.BaseName(Project.CreatedAt), Preferences.Shared.ImageFolder, SavePanel.ProjectFormat, owner: this) is not { } choice) return;
        try
        {
            Library.ExportPackage(Project, choice.path);
            Toast.Show("Düzenlenebilir proje kaydedildi", Icons.Save, Path.GetFileName(choice.path), "Göster", () => Output.Reveal(choice.path));
        }
        catch (Exception error)
        {
            Output.ShowError(error, "Proje kaydedilemedi");
        }
    }

    protected void SavedToast(string path) =>
        Toast.Show("Kaydedildi", Icons.Save, Path.GetFileName(path), "Göster", () => Output.Reveal(path));
}
