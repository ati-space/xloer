namespace FastShot;

/// <summary>Reopens a screenshot with its marks still editable.</summary>
public sealed class ImageEditorWindow : EditorWindow
{
    readonly BitmapSource image;

    public static ImageEditorWindow? Create(CaptureProject project, ProjectLibrary library)
    {
        var image = ImageExporter.Load(library.MediaPath(project));
        return image is null ? null : new ImageEditorWindow(project, library, image);
    }

    ImageEditorWindow(CaptureProject project, ProjectLibrary library, BitmapSource image)
        : base(project, library, "FastShot — Görüntü düzenleyici")
    {
        this.image = image;
        Canvas.BackgroundImage = image;
    }

    BitmapSource Rendered()
    {
        Canvas.EndTextEditing();
        return ImageExporter.Flatten(image, Project.CanvasSize, Controller.Annotations);
    }

    protected override void ProjectDidSave() => Library.WriteThumbnail(ImageExporter.Flatten(image, Project.CanvasSize, Controller.Annotations), Project);

    protected override void CopyToClipboard()
    {
        Output.CopyImage(Rendered(), Project.PixelScale);
        Toast.Show("Panoya kopyalandı", Icons.Copy);
    }

    protected override void Save()
    {
        var rendered = Rendered();
        try
        {
            var path = Output.QuickSave(rendered, Project.PixelScale);
            if (Preferences.Shared.CopyWhenSaving) Output.CopyImage(rendered, Project.PixelScale);
            SavedToast(path);
        }
        catch (Exception error)
        {
            Output.ShowError(error, "Görüntü kaydedilemedi");
        }
    }

    protected override void SaveAs()
    {
        var rendered = Rendered();
        var prefs = Preferences.Shared;
        if (SavePanel.Run(Output.BaseName(Project.CreatedAt), prefs.ImageFolder, SavePanel.ImageFormats, prefs.ImageFormat == ImageFormat.Png ? 0 : 1, this) is not { } choice) return;
        try
        {
            Output.WriteImage(rendered, choice.path, choice.format == 0 ? ImageFormat.Png : ImageFormat.Jpeg, Project.PixelScale);
            SavedToast(choice.path);
        }
        catch (Exception error)
        {
            Output.ShowError(error, "Görüntü kaydedilemedi");
        }
    }
}
