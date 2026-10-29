using System.Globalization;
using System.Windows.Documents;

namespace FastShot;

/// <summary>Every capture kept with its marks; double-click one to keep editing it.</summary>
public sealed class LibraryWindow : Window
{
    readonly ProjectLibrary library;
    readonly Action<CaptureProject> open;
    readonly WrapPanel grid = new() { Margin = new Thickness(10) };
    readonly TextBlock count = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
    readonly ComboBox filter = new() { Width = 160, VerticalAlignment = VerticalAlignment.Center };
    readonly ScrollViewer scroller;
    readonly FrameworkElement empty;
    readonly UiPalette palette = UiPalette.Editor;
    static readonly CultureInfo turkish = new("tr-TR");

    public LibraryWindow(ProjectLibrary library, Action<CaptureProject> open)
    {
        this.library = library;
        this.open = open;
        Title = "FastShot Geçmişi";
        Icon = AppIcon.WindowIcon;
        Width = 900;
        Height = 620;
        MinWidth = 620;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        foreach (var title in new[] { "Tümü", "Görüntüler", "Videolar" }) filter.Items.Add(title);
        filter.SelectedIndex = 0;
        filter.SelectionChanged += (_, _) => Reload();
        count.Foreground = palette.Secondary;
        var folder = new Button { Content = "Klasörü aç", Padding = new Thickness(12, 4, 12, 5) };
        folder.Click += (_, _) => Output.OpenFolder(library.Root);

        var header = new DockPanel { Margin = new Thickness(14, 12, 14, 12), LastChildFill = false };
        header.Children.Add(filter);
        DockPanel.SetDock(folder, Dock.Right);
        DockPanel.SetDock(count, Dock.Right);
        header.Children.Add(folder);
        header.Children.Add(count);

        scroller = new ScrollViewer { Content = grid, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var emptyStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 400 };
        emptyStack.Children.Add(new IconView(Icons.Camera, 40) { Brush = palette.Secondary, Margin = new Thickness(0, 0, 0, 10) });
        emptyStack.Children.Add(new TextBlock { Text = "Henüz yakalama yok", FontSize = 16, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center });
        emptyStack.Children.Add(new TextBlock
        {
            Text = "Print Screen veya ayarladığınız kısayolla ekran görüntüsü alın; kopyaladığınız ya da kaydettiğiniz her yakalama burada düzenlenebilir olarak saklanır.",
            TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Foreground = palette.Secondary, Margin = new Thickness(0, 6, 0, 0),
        });
        empty = emptyStack;

        var body = new Grid();
        body.Children.Add(scroller);
        body.Children.Add(empty);
        var root = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        root.Children.Add(new Border { Height = 1, Background = palette.Separator, VerticalAlignment = VerticalAlignment.Top });
        root.Children.Add(body);
        Content = root;
        Reload();
    }

    public void Reload()
    {
        var projects = library.AllProjects();
        var visible = filter.SelectedIndex switch
        {
            1 => projects.Where(p => p.Kind == CaptureKind.image).ToList(),
            2 => projects.Where(p => p.Kind == CaptureKind.video).ToList(),
            _ => projects,
        };
        count.Text = $"{visible.Count} yakalama";
        grid.Children.Clear();
        foreach (var project in visible) grid.Children.Add(Card(project));
        empty.Visibility = visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        scroller.Visibility = visible.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    BitmapSource? Thumbnail(CaptureProject project)
    {
        foreach (var path in new[] { library.ThumbnailPath(project), project.Kind == CaptureKind.image ? library.MediaPath(project) : null })
        {
            if (path is null || !File.Exists(path)) continue;
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.UriSource = new Uri(path);
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                image.DecodePixelWidth = 360;
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch { /* try the next source */ }
        }
        return null;
    }

    FrameworkElement Card(CaptureProject project)
    {
        var picture = new Grid { Height = 130, Background = palette.Hover };
        if (Thumbnail(project) is { } thumbnail)
        {
            var image = new Image { Source = thumbnail, Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            picture.Children.Add(image);
        }
        else
        {
            picture.Children.Add(new IconView(project.Kind == CaptureKind.video ? Icons.Video : Icons.Camera, 30) { Brush = palette.Secondary });
        }
        var badgeText = project.Kind == CaptureKind.video ? "Video" : "Görüntü";
        if (project.Kind == CaptureKind.video && project.Duration is { } length) badgeText += " · " + Ui.Format(length, precise: false);
        if (project.Annotations.Count > 0) badgeText += $" · {project.Annotations.Count} çizim";
        var badge = new Border
        {
            Background = Theme.Frozen(Color.FromArgb(0x99, 0, 0, 0)), CornerRadius = new CornerRadius(8), Padding = new Thickness(6, 2, 6, 2),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(6),
            Child = new TextBlock { Text = badgeText, Foreground = Brushes.White, FontSize = 10, FontWeight = FontWeights.SemiBold },
        };
        picture.Children.Add(badge);
        var clip = new Border { CornerRadius = new CornerRadius(8), Child = picture, ClipToBounds = true };
        clip.Clip = new RectangleGeometry(new Rect(0, 0, 224, 130), 8, 8);

        var stack = new StackPanel();
        stack.Children.Add(clip);
        stack.Children.Add(new TextBlock { Text = project.CreatedAt.ToLocalTime().ToString("d MMM yyyy HH:mm", turkish), FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(2, 6, 0, 0) });
        stack.Children.Add(new TextBlock { Text = $"{project.PixelWidth} × {project.PixelHeight}", FontSize = 11, Foreground = palette.Secondary, Margin = new Thickness(2, 1, 0, 0) });

        var card = new Border
        {
            Child = stack, Width = 240, Padding = new Thickness(8), Margin = new Thickness(6), CornerRadius = new CornerRadius(12),
            Background = palette.Hover, ToolTip = "Düzenlemek için çift tıklayın", Cursor = Cursors.Hand,
        };
        card.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2) open(project);
        };
        var menu = new ContextMenu();
        void Item(string title, Action action)
        {
            var item = new MenuItem { Header = title };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }
        Item("Düzenle", () => open(project));
        if (project.Kind == CaptureKind.image) Item("Panoya kopyala", () => Copy(project));
        Item("Orijinali Gezgin'de göster", () => Output.Reveal(library.MediaPath(project)));
        menu.Items.Add(new Separator());
        Item("Sil…", () => Delete(project));
        card.ContextMenu = menu;
        return card;
    }

    void Copy(CaptureProject project)
    {
        if (ImageExporter.Load(library.MediaPath(project)) is not { } image)
        {
            open(project);
            return;
        }
        Output.CopyImage(ImageExporter.Flatten(image, project.CanvasSize, project.Annotations), project.PixelScale);
        Toast.Show("Panoya kopyalandı", Icons.Copy);
    }

    void Delete(CaptureProject project)
    {
        var answer = MessageBox.Show(this,
            "Orijinal dosya ve çizimler geçmişten kalıcı olarak silinir. Daha önce dışa aktardığınız dosyalar etkilenmez.",
            "Bu yakalama silinsin mi?", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;
        try { library.Delete(project); }
        catch (Exception error) { Output.ShowError(error, "Silinemedi"); }
        Reload();
    }
}
