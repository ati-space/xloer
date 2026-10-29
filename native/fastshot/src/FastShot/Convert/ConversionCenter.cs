using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using FastShot.Core.Convert;

namespace FastShot;

/// <summary>Runs what the wheels start: conversions, tools and actions. Slow work is queued one batch at a time
/// with a progress panel; results are announced with a notice that can reveal them in Explorer.</summary>
public sealed class ConversionCenter
{
    public static ConversionCenter Shared { get; } = new();

    /// <param name="Title">Shown while working, e.g. "JPEG biçimine dönüştürülüyor".</param>
    /// <param name="Done">Completes "… dosya ___", e.g. "JPEG olarak kaydedildi".</param>
    /// <param name="Together">All files form one batch (merging, archiving several items).</param>
    /// <param name="Work">Runs on an STA thread with (batch, folder, progress, cancellation) and returns what it wrote.</param>
    sealed record Job(string Title, string Done, List<string> Paths, bool Together, Func<List<string>, string, Action<double>, CancellationToken, List<string>> Work);

    readonly Queue<Job> queue = new();
    CancellationTokenSource? current;

    public void Perform(WheelItem item, List<string> paths)
    {
        if (item.Format is { } format) Convert(paths, format);
        else if (item.Tool is { } tool) Run(tool, paths);
        else if (item.Action is { } action) Run(action, paths);
    }

    public void Convert(IEnumerable<string> paths, ConversionFormat format)
    {
        var files = paths.Where(p => File.Exists(p) && FileConverter.Family(p) is not null).ToList();
        var prefs = Preferences.Shared;
        WordEngine.Enabled = prefs.ConvertUseWord;
        var quality = prefs.JpegQuality;
        var merges = format.Kind == ConversionKind.ImagesToPdf && files.Count > 1;
        Enqueue(new Job($"{format.Title} biçimine dönüştürülüyor", $"{format.Title} olarak kaydedildi", files, merges,
                        (batch, folder, progress, cancellation) => FileConverter.Convert(batch, format, _ => folder, quality, progress, cancellation)));
    }

    // MARK: Tools

    void Run(FileTool tool, List<string> paths)
    {
        var files = paths.Where(File.Exists).ToList();
        if (files.Count == 0) return;
        switch (tool)
        {
            case FileTool.Edit:
                foreach (var file in files) AppController.Shared.Open(file);
                break;
            case FileTool.Crop:
                CropWindow.Show(files[0]);
                break;
            case FileTool.RecognizeText:
                _ = RecognizeText(files[0]);
                break;
            case FileTool.Compress:
                Enqueue(new Job("Sıkıştırılıyor", tool.DoneText(), files, false, (batch, folder, progress, cancellation) => [FileTools.Compress(batch[0], folder, progress, cancellation)]));
                break;
            case FileTool.Metadata:
                Enqueue(new Job("Meta veriler temizleniyor", tool.DoneText(), files, false, (batch, folder, progress, cancellation) => [FileTools.StripMetadata(batch[0], folder, progress, cancellation)]));
                break;
            case FileTool.Background:
                Enqueue(new Job("Arka plan ekleniyor", tool.DoneText(), files, false, (batch, folder, _, _) => [FileTools.AddBackground(batch[0], folder)]));
                break;
            case FileTool.RemoveAudio:
                Enqueue(new Job("Ses kaldırılıyor", tool.DoneText(), files, false, (batch, folder, progress, cancellation) => [FileTools.RemoveAudio(batch[0], folder, progress, cancellation)]));
                break;
            case FileTool.MergePdf:
                Enqueue(new Job("PDF'ler birleştiriliyor", tool.DoneText(), files, true, (batch, folder, _, _) => [FileTools.MergePdfs(batch, folder)]));
                break;
        }
    }

    static async Task RecognizeText(string path)
    {
        try
        {
            var image = ImageExporter.Load(path) ?? throw new InvalidDataException("Bu dosya bir görüntü olarak okunamadı.");
            var text = await TextRecognizer.Recognize(image);
            if (string.IsNullOrWhiteSpace(text))
            {
                Toast.Show("Metin bulunamadı", Icons.Scan);
                return;
            }
            Output.CopyText(text);
            Toast.Show("Metin panoya kopyalandı", Icons.Scan, $"{text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length} satır");
        }
        catch (Exception error)
        {
            Output.ShowError(error, "Metin tanınamadı");
        }
    }

    // MARK: Actions

    void Run(FileAction action, List<string> paths)
    {
        var items = paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
        if (items.Count == 0) return;
        switch (action)
        {
            case FileAction.Zip:
                Enqueue(new Job("ZIP arşivi oluşturuluyor", "ZIP olarak arşivlendi", items, true, (batch, folder, _, _) => [FileTools.Zip(batch, folder)]));
                break;
            case FileAction.Unzip:
                Enqueue(new Job("Arşiv açılıyor", "arşivden çıkarıldı", items, false, (batch, folder, _, _) => [FileTools.Unzip(batch[0], folder)]));
                break;
            case FileAction.Share:
                ShareSheet.Show(items);
                break;
            case FileAction.CopyToClipboard:
                if (items.Count == 1 && File.Exists(items[0]) && FileConverter.Family(items[0]) == FileFamily.Image && ImageExporter.Load(items[0]) is { } image)
                {
                    Output.CopyImage(image, ImageExporter.PointScale(items[0]) ?? 1);
                    Toast.Show("Görüntü panoya kopyalandı", Icons.Copy, "Mesaja veya belgeye yapıştırabilirsiniz");
                }
                else
                {
                    Output.CopyFiles(items);
                    Toast.Show(items.Count == 1 ? "Dosya panoya kopyalandı" : $"{items.Count} öğe panoya kopyalandı", Icons.Copy, "Gezgin'de Ctrl+V ile yapıştırın");
                }
                break;
            case FileAction.CopyPath:
                Output.CopyText(string.Join(Environment.NewLine, items));
                Toast.Show(items.Count == 1 ? "Yol kopyalandı" : $"{items.Count} yol kopyalandı", "", items[0]);
                break;
            case FileAction.Pin:
                Pin(items);
                break;
            case FileAction.Terminal:
                OpenTerminal(items[0]);
                break;
        }
    }

    /// <summary>Pins the images around the pointer, each at most 45 % of the screen, cascading.</summary>
    static void Pin(List<string> paths)
    {
        var cursor = Native.CursorPosition();
        var monitor = MonitorInfo.At(cursor.X, cursor.Y);
        var work = monitor.Work;
        for (var index = 0; index < paths.Count; index++)
        {
            if (ImageExporter.Load(paths[index]) is not { } image) continue;
            var pointScale = ImageExporter.PointScale(paths[index]) ?? 1;
            var width = image.PixelWidth / pointScale * monitor.Scale;
            var height = image.PixelHeight / pointScale * monitor.Scale;
            var fit = Math.Min(1, Math.Min(work.Width * 0.45 / width, work.Height * 0.45 / height));
            width *= fit;
            height *= fit;
            var offset = (int)(index * 28 * monitor.Scale);
            var left = Math.Min(Math.Max(work.Left, cursor.X - (int)(width / 2) + offset), work.Right - (int)width);
            var top = Math.Min(Math.Max(work.Top, cursor.Y - (int)(height / 2) + offset), work.Bottom - (int)height);
            PinWindow.Show(image, pointScale / fit, new Native.RECT(left, top, left + (int)Math.Round(width), top + (int)Math.Round(height)));
        }
    }

    /// <summary>Windows Terminal in the item's folder, else PowerShell.</summary>
    static void OpenTerminal(string path)
    {
        var folder = Directory.Exists(path) ? path : Path.GetDirectoryName(path) ?? path;
        try
        {
            var terminal = new ProcessStartInfo("wt.exe") { UseShellExecute = true };
            terminal.ArgumentList.Add("-d");
            terminal.ArgumentList.Add(folder);
            Process.Start(terminal);
        }
        catch
        {
            try
            {
                Process.Start(new ProcessStartInfo("powershell.exe") { UseShellExecute = true, WorkingDirectory = folder });
            }
            catch (Exception error)
            {
                Output.ShowError(error, "Terminal açılamadı");
            }
        }
    }

    // MARK: Queue

    public void Cancel()
    {
        queue.Clear();
        current?.Cancel();
    }

    void Enqueue(Job job)
    {
        if (job.Paths.Count == 0) return;
        queue.Enqueue(job);
        if (current is null) _ = RunNext();
    }

    async Task RunNext()
    {
        while (queue.Count > 0)
        {
            var job = queue.Dequeue();
            current = new CancellationTokenSource();
            try { await Run(job, current.Token); }
            finally
            {
                current.Dispose();
                current = null;
            }
        }
    }

    async Task Run(Job job, CancellationToken cancellation)
    {
        var paths = job.Paths;
        var progress = new ProgressHud(job.Title, paths.Count == 1 ? Path.GetFileName(paths[0].TrimEnd('\\')) : $"{paths.Count} öğe", Cancel);
        // Quick work finishes before the progress panel would even appear.
        var reveal = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        reveal.Tick += (_, _) =>
        {
            reveal.Stop();
            progress.Show();
        };
        reveal.Start();

        var outputs = new List<string>();
        var failures = new List<string>();
        var batches = job.Together ? [paths] : paths.Select(p => new List<string> { p }).ToList();
        try
        {
            for (var i = 0; i < batches.Count; i++)
            {
                if (cancellation.IsCancellationRequested) break;
                var batch = batches[i];
                var name = Path.GetFileName(batch[0].TrimEnd('\\'));
                if (batches.Count > 1) progress.Detail = $"{i + 1}/{batches.Count} · {name}";
                var share = 1.0 / batches.Count;
                var start = i * share;
                try
                {
                    var folder = Destination(batch[0]);
                    Directory.CreateDirectory(folder);
                    outputs.AddRange(await OnSta(() => job.Work(batch, folder, fraction => progress.Report(start + fraction * share), cancellation)));
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception error)
                {
                    failures.Add($"{name}: {error.Message}");
                }
            }
        }
        finally
        {
            reveal.Stop();
            progress.Close();
        }

        if (outputs.Count > 0)
        {
            if (Preferences.Shared.RevealAfterConvert) Output.Reveal(outputs[0]);
            var summary = outputs.Count == 1 ? char.ToUpper(job.Done[0], CultureInfo.CurrentCulture) + job.Done[1..] : $"{outputs.Count} dosya {job.Done}";
            var first = outputs[0];
            Toast.Show(summary, Icons.Check, Path.GetFileName(first), "Göster", () => Output.Reveal(first));
        }
        if (failures.Count > 0)
        {
            var title = failures.Count == 1 ? "Bir dosya işlenemedi" : $"{failures.Count} dosya işlenemedi";
            MessageBox.Show(string.Join("\n", failures.Take(8)), title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>WPF imaging, Media Foundation and the PDF engines want an STA thread of their own.</summary>
    static Task<T> OnSta<T>(Func<T> work)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { completion.SetResult(work()); }
            catch (Exception error) { completion.SetException(error); }
        }) { IsBackground = true, Name = "FastShot file work" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    public static string Destination(string path)
    {
        var prefs = Preferences.Shared;
        return prefs.ConvertDestination switch
        {
            ConvertDestination.Downloads => DownloadsFolder,
            ConvertDestination.Custom => prefs.ConvertFolder,
            _ => Path.GetDirectoryName(path.TrimEnd('\\')) is { Length: > 0 } folder && IsWritable(folder) ? folder : DownloadsFolder,
        };
    }

    static bool IsWritable(string folder)
    {
        try
        {
            var probe = Path.Combine(folder, $".fastshot-{Guid.NewGuid():N}");
            File.WriteAllBytes(probe, []);
            File.Delete(probe);
            return true;
        }
        catch { return false; }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);

    public static string DownloadsFolder
    {
        get
        {
            try
            {
                if (SHGetKnownFolderPath(new Guid("374DE290-123F-4565-9164-39C4925E467B"), 0, IntPtr.Zero, out var pointer) == 0)
                {
                    var path = Marshal.PtrToStringUni(pointer);
                    Marshal.FreeCoTaskMem(pointer);
                    if (!string.IsNullOrEmpty(path)) return path;
                }
            }
            catch { /* fall back below */ }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }
    }

    /// <summary>Lets the user pick files, then offers the wheels for them.</summary>
    public void ChooseFiles()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Dosyaları seçin: dönüştürme, araçlar ve işlemler çarkı açılacak",
            Multiselect = true,
            Filter = "Tüm dosyalar|*.*|Dönüştürülebilir dosyalar|" + string.Join(";", FileConverter.SupportedExtensions.Select(e => "*" + e)),
        };
        if (dialog.ShowDialog() != true || dialog.FileNames.Length == 0) return;
        var monitor = MonitorInfo.AtCursor();
        var center = new Native.POINT { X = monitor.Work.Left + monitor.Work.Width / 2, Y = monitor.Work.Top + monitor.Work.Height / 2 };
        ConversionWheel.Show(dialog.FileNames, center, interactive: true);
    }
}

/// <summary>A small panel at the bottom of the screen for work that takes a while.</summary>
public sealed class ProgressHud
{
    readonly TextBlock detail;
    readonly ProgressBar bar = new() { Width = 220, Height = 4, Minimum = 0, Maximum = 1, Margin = new Thickness(0, 5, 0, 0) };
    readonly FrameworkElement content;
    PanelWindow? panel;
    bool closed;

    public ProgressHud(string title, string detailText, Action cancel)
    {
        var p = UiPalette.Hud;
        detail = Ui.Label(detailText, p, 11, secondary: true);
        detail.TextTrimming = TextTrimming.CharacterEllipsis;
        detail.MaxWidth = 220;
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(Ui.Label(title, p, 13, weight: FontWeights.SemiBold));
        texts.Children.Add(detail);
        texts.Children.Add(bar);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new IconView("", 18) { Brush = Theme.Accent, Margin = new Thickness(0, 0, 12, 0) });
        row.Children.Add(texts);
        row.Children.Add(new PillButton("İptal", null, "Durdur", Theme.Frozen(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)), cancel)
        {
            Margin = new Thickness(12, 0, 0, 0), Padding = new Thickness(10, 3, 10, 3),
        });
        var hud = Ui.Hud(row, 14, 10);
        hud.Padding = new Thickness(14, 10, 14, 10);
        hud.Margin = new Thickness(14);
        content = hud;
    }

    public string Detail { set => Dispatch(() => detail.Text = value); }

    public void Report(double value) => Dispatch(() => bar.Value = Math.Clamp(value, 0, 1));

    void Dispatch(Action action)
    {
        if (content.Dispatcher.CheckAccess()) action();
        else content.Dispatcher.BeginInvoke(action);
    }

    public void Show()
    {
        if (closed || panel is not null) return;
        content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var monitor = MonitorInfo.AtCursor();
        var width = (int)Math.Ceiling(content.DesiredSize.Width * monitor.Scale);
        var height = (int)Math.Ceiling(content.DesiredSize.Height * monitor.Scale);
        var work = monitor.Work;
        var left = work.Left + (work.Width - width) / 2;
        var top = work.Bottom - (int)(56 * monitor.Scale) - height;
        panel = new PanelWindow(content, new Native.RECT(left, top, left + width, top + height), transparent: true, activatable: false);
        panel.Show(activate: false);
    }

    public void Close()
    {
        closed = true;
        panel?.Dispose();
        panel = null;
    }
}
