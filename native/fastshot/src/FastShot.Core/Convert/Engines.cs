using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace FastShot.Core.Convert;

/// <summary>Microsoft Word through COM automation, when it is installed: DOC in and out, faithful document PDFs, PDF reflow.
/// Runs in Word's own hidden instance and never touches documents the user has open.</summary>
public static class WordEngine
{
    public static bool IsInstalled()
    {
        try { return Type.GetTypeFromProgID("Word.Application") is not null; }
        catch { return false; }
    }

    static readonly object gate = new();

    /// <summary>Lets the app turn Word off (Settings) and tests run without it.</summary>
    public static bool Enabled { get; set; } = true;

    public static TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(3);

    /// <param name="codec">docx, doc, rtf, txt, html, odt or pdf.</param>
    public static void Convert(string input, string output, string codec)
    {
        lock (gate)
        {
            // Word can stop on a licence or first-run dialog nobody sees; give up after a while and end the instance we started.
            var before = Process.GetProcessesByName("WINWORD").Select(p => p.Id).ToHashSet();
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try { Run(input, output, codec); }
                catch (Exception error) { failure = error; }
            }) { IsBackground = true, Name = "FastShot Word" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            if (!thread.Join(Timeout))
            {
                foreach (var process in Process.GetProcessesByName("WINWORD").Where(p => !before.Contains(p.Id)))
                {
                    try { process.Kill(); } catch { /* already gone */ }
                }
                throw new ConversionException("Microsoft Word yanıt vermedi.");
            }
            if (failure is not null) throw failure is ConversionException ? failure : new ConversionException($"Word dosyayı dönüştüremedi: {failure.Message}");
        }
    }

    static void Run(string input, string output, string codec)
    {
        var type = Type.GetTypeFromProgID("Word.Application") ?? throw new InvalidOperationException("Microsoft Word yüklü değil.");
        dynamic? word = null;
        dynamic? document = null;
        var ownsInstance = true;
        try
        {
            word = Activator.CreateInstance(type)!;
            ownsInstance = (int)word.Documents.Count == 0;
            if (ownsInstance) word.Visible = false;
            word.DisplayAlerts = 0; // wdAlertsNone
            var full = Path.GetFullPath(input);
            var isText = Path.GetExtension(full).Equals(".txt", StringComparison.OrdinalIgnoreCase);
            document = isText
                ? word.Documents.Open(FileName: full, ConfirmConversions: false, ReadOnly: true, AddToRecentFiles: false, Visible: false, Encoding: 65001)
                : word.Documents.Open(FileName: full, ConfirmConversions: false, ReadOnly: true, AddToRecentFiles: false, Visible: false);
            var target = Path.GetFullPath(output);
            if (codec == "pdf")
            {
                document.ExportAsFixedFormat(OutputFileName: target, ExportFormat: 17); // wdExportFormatPDF
            }
            else
            {
                var format = codec switch
                {
                    "docx" => 16, // wdFormatDocumentDefault
                    "doc" => 0,   // wdFormatDocument97
                    "rtf" => 6,
                    "txt" => 2,   // wdFormatText (with the UTF-8 encoding below)
                    "html" => 10, // wdFormatFilteredHTML
                    "odt" => 23,
                    _ => throw ConversionException.Unsupported(Path.GetFileName(input)),
                };
                if (codec == "txt") document.SaveAs2(FileName: target, FileFormat: format, AddToRecentFiles: false, Encoding: 65001);
                else document.SaveAs2(FileName: target, FileFormat: format, AddToRecentFiles: false);
            }
        }
        catch (COMException error)
        {
            throw new ConversionException($"Word dosyayı dönüştüremedi: {error.Message}");
        }
        finally
        {
            try { document?.Close(SaveChanges: 0); } catch { /* already closed */ }
            if (word is not null)
            {
                try { if (ownsInstance) word.Quit(SaveChanges: 0); } catch { /* gone */ }
                Marshal.FinalReleaseComObject(word);
            }
            if (document is not null) Marshal.FinalReleaseComObject(document);
        }
    }
}

/// <summary>Microsoft Edge (present on every Windows 10/11) printing HTML to PDF headlessly, with its own throwaway profile.</summary>
public static class EdgeEngine
{
    public static string? Path => new[]
    {
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Microsoft\Edge\Application\msedge.exe"),
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Microsoft\Edge\Application\msedge.exe"),
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Edge\Application\msedge.exe"),
    }.FirstOrDefault(File.Exists);

    public static void PrintToPdf(string html, string output, CancellationToken cancellation)
    {
        var edge = Path ?? throw new InvalidOperationException("Microsoft Edge bulunamadı.");
        var profile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"fastshot-edge-{Guid.NewGuid():N}");
        try
        {
            ExternalTools.Run(edge,
            [
                "--headless=new", "--disable-gpu", "--no-first-run", "--no-default-browser-check", "--disable-extensions",
                $"--user-data-dir={profile}", "--no-pdf-header-footer", "--print-to-pdf-no-header",
                $"--print-to-pdf={System.IO.Path.GetFullPath(output)}", new Uri(System.IO.Path.GetFullPath(html)).AbsoluteUri,
            ], System.IO.Path.GetFileName(output), TimeSpan.FromSeconds(90), cancellation);
        }
        finally
        {
            for (var attempt = 0; attempt < 10 && Directory.Exists(profile); attempt++)
            {
                try { Directory.Delete(profile, recursive: true); }
                catch { Thread.Sleep(200); }
            }
        }
        if (!File.Exists(output) || new FileInfo(output).Length == 0) throw ConversionException.WriteFailed(System.IO.Path.GetFileName(output));
    }
}
