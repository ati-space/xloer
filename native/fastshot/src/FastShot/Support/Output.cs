using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace FastShot;

/// <summary>Naming, saving, clipboard and error reporting.</summary>
public static class Output
{
    static readonly CultureInfo turkish = new("tr-TR");

    public static string BaseName(DateTime? date = null) =>
        "FastShot " + (date ?? DateTime.Now).ToLocalTime().ToString("yyyy-MM-dd 'saat' HH.mm.ss", turkish);

    public static string UniquePath(string folder, string baseName, string extension)
    {
        var path = Path.Combine(folder, $"{baseName}.{extension}");
        var counter = 2;
        while (File.Exists(path) || Directory.Exists(path))
        {
            path = Path.Combine(folder, $"{baseName} ({counter}).{extension}");
            counter++;
        }
        return path;
    }

    static void WithClipboard(Action action)
    {
        // Another app may hold the clipboard for a moment; retry briefly.
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (COMException) when (attempt < 8)
            {
                Thread.Sleep(40);
            }
            catch (ExternalException) when (attempt < 8)
            {
                Thread.Sleep(40);
            }
        }
    }

    /// <summary>Puts the image on the clipboard as PNG (lossless, for apps that read it) and as a bitmap (for the rest).</summary>
    public static void CopyImage(BitmapSource image, double pointScale)
    {
        var data = new DataObject();
        data.SetData("PNG", new MemoryStream(ImageExporter.Encode(image, ImageFormat.Png, pointScale: pointScale)), false);
        data.SetImage(ImageExporter.ToBgra32(image));
        WithClipboard(() => Clipboard.SetDataObject(data, true));
    }

    public static void CopyFile(string path) => CopyFiles([path]);

    /// <summary>Files and folders, ready to paste in Explorer (or attach in a mail).</summary>
    public static void CopyFiles(IEnumerable<string> paths)
    {
        var files = new StringCollection();
        foreach (var path in paths) files.Add(path);
        WithClipboard(() => Clipboard.SetFileDropList(files));
    }

    public static void CopyText(string text) => WithClipboard(() => Clipboard.SetText(text));

    public static void WriteImage(BitmapSource image, string path, ImageFormat format, double pointScale)
    {
        var data = ImageExporter.Encode(image, format, Preferences.Shared.JpegQuality, pointScale);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, data);
    }

    /// <summary>Saves to the default folder without asking.</summary>
    public static string QuickSave(BitmapSource image, double pointScale)
    {
        var prefs = Preferences.Shared;
        Directory.CreateDirectory(prefs.ImageFolder);
        var path = UniquePath(prefs.ImageFolder, BaseName(), prefs.ImageFormat.FileExtension());
        WriteImage(image, path, prefs.ImageFormat, pointScale);
        return path;
    }

    public static void OpenUrl(string url)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception error) { ShowError(error, "Bağlantı açılamadı"); }
    }

    public static void Reveal(string path)
    {
        try
        {
            if (File.Exists(path) || Directory.Exists(path)) Process.Start("explorer.exe", $"/select,\"{path}\"");
            else if (Path.GetDirectoryName(path) is { } folder && Directory.Exists(folder)) Process.Start("explorer.exe", $"\"{folder}\"");
        }
        catch { /* Explorer missing: nothing useful to do */ }
    }

    public static void OpenFolder(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            Process.Start("explorer.exe", $"\"{folder}\"");
        }
        catch { /* ignore */ }
    }

    public static void ShowError(Exception error, string title = "İşlem tamamlanamadı")
    {
        void Show() => MessageBox.Show(error.Message, title, MessageBoxButton.OK, MessageBoxImage.Warning, MessageBoxResult.OK, MessageBoxOptions.None);
        if (Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess()) dispatcher.BeginInvoke(Show);
        else Show();
    }
}

/// <summary>A save dialog whose format list keeps the file extension in step.</summary>
public static class SavePanel
{
    public sealed record Format(string Title, string Extension);

    public static readonly Format[] ImageFormats = [new("PNG görüntüsü", "png"), new("JPEG görüntüsü", "jpg")];
    public static readonly Format[] VideoFormats = [new("MP4 video", "mp4"), new("Animasyonlu GIF", "gif")];
    public static readonly Format[] ProjectFormat = [new("FastShot projesi", ProjectLibrary.PackageExtension)];

    public static (string path, int format)? Run(string name, string directory, Format[] formats, int initial = 0, Window? owner = null)
    {
        try { Directory.CreateDirectory(directory); } catch { /* the dialog falls back to its own folder */ }
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = name,
            InitialDirectory = directory,
            Filter = string.Join("|", formats.Select(f => $"{f.Title} (*.{f.Extension})|*.{f.Extension}")),
            FilterIndex = initial + 1,
            AddExtension = true,
            DefaultExt = formats[initial].Extension,
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog(owner) != true) return null;
        var extension = Path.GetExtension(dialog.FileName).TrimStart('.').ToLowerInvariant();
        var index = Array.FindIndex(formats, f => f.Extension == extension || (f.Extension == "jpg" && extension == "jpeg"));
        if (index < 0)
        {
            index = Math.Clamp(dialog.FilterIndex - 1, 0, formats.Length - 1);
            return (Path.ChangeExtension(dialog.FileName, formats[index].Extension), index);
        }
        return (dialog.FileName, index);
    }
}
