using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Threading;

namespace FastShot;

/// <summary>Looks for newer FastShot for Windows releases (tags windows-vX.Y.Z on github.com/Mericsdc/FastShot, built by
/// .github/workflows/windows.yml) and installs one by running its setup with --update. Only the installed copy checks.</summary>
public sealed class Updater
{
    public static Updater Shared { get; } = new();

    const string ReleasesApi = "https://api.github.com/repos/Mericsdc/FastShot/releases?per_page=30";
    public const string ReleasesPage = "https://github.com/Mericsdc/FastShot/releases";

    public sealed record Release(Version Version, string DownloadUrl, long Size, string Page);

    public enum Status { Idle, Checking, UpToDate, Available, Downloading, Failed }

    static readonly HttpClient http = CreateClient();
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromHours(6) };
    bool installing;

    public Status State { get; private set; } = Status.Idle;
    public Release? Available { get; private set; }
    public string? Error { get; private set; }
    public DateTime? LastChecked { get; private set; }
    public event Action? Changed;

    Updater() => timer.Tick += (_, _) => _ = Check(userAsked: false);

    static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"FastShot/{Installer.CurrentVersion.ToString(3)}");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    /// <summary>Checks a little after the start, then every six hours, while automatic checks are on.</summary>
    public void Start()
    {
        var first = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        first.Tick += (_, _) =>
        {
            first.Stop();
            if (Preferences.Shared.CheckForUpdates) _ = Check(userAsked: false);
        };
        first.Start();
        timer.Start();
    }

    void Set(Status state)
    {
        State = state;
        Changed?.Invoke();
    }

    public async Task Check(bool userAsked)
    {
        if (State is Status.Checking or Status.Downloading) return;
        if (!userAsked && !Preferences.Shared.CheckForUpdates) return;
        Set(Status.Checking);
        try
        {
            Available = await Newest();
            Error = null;
            LastChecked = DateTime.Now;
            Set(Available is null ? Status.UpToDate : Status.Available);
            if (Available is { } release)
                Toast.Show($"FastShot {release.Version.ToString(3)} hazır", "", $"Yüklü sürüm: {Installer.CurrentVersion.ToString(3)}", "Güncelle", () => _ = Install());
            else if (userAsked)
                Toast.Show("FastShot güncel", Icons.Check, $"Sürüm {Installer.CurrentVersion.ToString(3)}");
        }
        catch (Exception error)
        {
            Error = error is HttpRequestException or TaskCanceledException ? "GitHub'a ulaşılamadı. İnternet bağlantınızı denetleyin." : error.Message;
            Set(Status.Failed);
            if (userAsked) Toast.Show("Güncellemeler denetlenemedi", Icons.Clear, Error);
        }
    }

    /// <summary>The newest release with a setup that is newer than this copy, or null.</summary>
    static async Task<Release?> Newest()
    {
        using var response = await http.GetAsync(ReleasesApi);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Release? best = null;
        foreach (var release in document.RootElement.EnumerateArray())
        {
            if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean()) continue;
            var tag = release.GetProperty("tag_name").GetString() ?? "";
            if (!tag.StartsWith("windows-v", StringComparison.Ordinal) || !Version.TryParse(tag["windows-v".Length..], out var version)) continue;
            foreach (var asset in release.GetProperty("assets").EnumerateArray())
            {
                if (asset.GetProperty("name").GetString() != Installer.SetupName) continue;
                if (best is null || version > best.Version)
                    best = new Release(version, asset.GetProperty("browser_download_url").GetString()!, asset.GetProperty("size").GetInt64(),
                                       release.GetProperty("html_url").GetString() ?? ReleasesPage);
            }
        }
        return best is not null && best.Version > Installer.CurrentVersion ? best : null;
    }

    /// <summary>Downloads the setup, starts it with --update and quits; the setup replaces this exe and starts the new one.</summary>
    public async Task Install()
    {
        if (Available is not { } release || installing) return;
        installing = true;
        Set(Status.Downloading);
        var file = Path.Combine(Path.GetTempPath(), $"FastShot-Setup-{release.Version.ToString(3)}.exe");
        try
        {
            using (var response = await http.GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                await using var output = File.Create(file);
                await response.Content.CopyToAsync(output);
            }
            if (new FileInfo(file).Length != release.Size) throw new IOException("İndirilen dosya eksik.");
            Process.Start(new ProcessStartInfo(file) { UseShellExecute = false, ArgumentList = { Installer.UpdateArgument } });
            AppController.Shared.Quit();
        }
        catch (Exception error)
        {
            installing = false;
            Error = error.Message;
            Set(Status.Failed);
            Output.ShowError(error, "Güncelleme indirilemedi");
        }
    }
}
