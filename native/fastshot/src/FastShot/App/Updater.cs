using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Threading;

namespace FastShot;

/// <summary>Keeps every installed FastShot on the newest release (tags windows-vX.Y.Z on github.com/Mericsdc/FastShot, built by
/// .github/workflows/windows.yml). It checks right after Windows starts and then every six hours, downloads a newer version in
/// the background and installs it on its own (its setup runs with --update and starts the new FastShot) as soon as nothing is
/// being captured, recorded, edited or converted. Only the installed copy updates itself.</summary>
public sealed class Updater
{
    public static Updater Shared { get; } = new();

    const string ReleasesApi = "https://api.github.com/repos/Mericsdc/FastShot/releases?per_page=30";
    public const string ReleasesPage = "https://github.com/Mericsdc/FastShot/releases";

    public sealed record Release(Version Version, string DownloadUrl, long Size, string Page);

    /// <summary>Ready: downloaded, waiting until FastShot is not in use to install.</summary>
    public enum Status { Idle, Checking, UpToDate, Available, Downloading, Ready, Failed }

    static readonly HttpClient http = CreateClient();
    readonly DispatcherTimer periodic = new() { Interval = TimeSpan.FromHours(6) };
    readonly DispatcherTimer soon = new();
    bool installing, installNow;
    int failedChecks, failedInstalls;

    public Status State { get; private set; } = Status.Idle;
    public Release? Available { get; private set; }
    public string? Error { get; private set; }
    public DateTime? LastChecked { get; private set; }
    public event Action? Changed;
    /// <summary>True while the user is in the middle of something; an automatic install waits for it.</summary>
    public Func<bool>? IsBusy { get; set; }

    Updater()
    {
        periodic.Tick += (_, _) => _ = Check(userAsked: false);
        soon.Tick += (_, _) =>
        {
            soon.Stop();
            _ = Check(userAsked: false);
        };
    }

    static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"FastShot/{Installer.CurrentVersion.ToString(3)}");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    /// <summary>Checks a few seconds after FastShot starts (with Windows), then every six hours.</summary>
    public void Start()
    {
        CheckIn(TimeSpan.FromSeconds(5));
        periodic.Start();
    }

    void CheckIn(TimeSpan delay)
    {
        soon.Stop();
        soon.Interval = delay;
        soon.Start();
    }

    void Set(Status state)
    {
        State = state;
        Changed?.Invoke();
    }

    public async Task Check(bool userAsked)
    {
        if (installing || State is Status.Checking) return;
        Set(Status.Checking);
        try
        {
            Available = await Newest();
            Error = null;
            LastChecked = DateTime.Now;
            failedChecks = 0;
            Set(Available is null ? Status.UpToDate : Status.Available);
            if (Available is not null) _ = Download(userAsked);
            else if (userAsked) Toast.Show("FastShot güncel", Icons.Check, $"Sürüm {Installer.CurrentVersion.ToString(3)}");
        }
        catch (Exception error)
        {
            Error = error is HttpRequestException or TaskCanceledException ? "GitHub'a ulaşılamadı. İnternet bağlantınızı denetleyin." : error.Message;
            Set(Status.Failed);
            if (userAsked) Toast.Show("Güncellemeler denetlenemedi", Icons.Clear, Error);
            // Right after Windows starts the network may not be up yet: try again every minute for a while.
            else if (++failedChecks <= 10) CheckIn(TimeSpan.FromMinutes(1));
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

    /// <summary>Installs the newer version now (the "Güncelle" buttons), without waiting for FastShot to be free.</summary>
    public async Task Install()
    {
        if (Available is null) return;
        installNow = true;
        if (!installing) await Download(userAsked: true);
    }

    /// <summary>Downloads the setup, waits until FastShot is not in use (unless asked to install now), starts the setup with
    /// --update and quits; the setup replaces this exe and starts the new one.</summary>
    async Task Download(bool userAsked)
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
            if (!installNow && IsBusy?.Invoke() == true)
            {
                Set(Status.Ready);
                Toast.Show($"FastShot {release.Version.ToString(3)} indirildi", "", "İşiniz bitince kendiliğinden kurulacak.", "Şimdi güncelle", () => _ = Install());
                while (!installNow && IsBusy?.Invoke() == true) await Task.Delay(TimeSpan.FromSeconds(5));
            }
            await AppController.Shared.PrepareToQuit();
            Process.Start(new ProcessStartInfo(file) { UseShellExecute = false, ArgumentList = { Installer.UpdateArgument } });
            AppController.Shared.Quit();
        }
        catch (Exception error)
        {
            installing = installNow = false;
            AppController.Shared.ResumeAfterFailedUpdate();
            Error = error.Message;
            Set(Status.Failed);
            if (userAsked) Output.ShowError(error, "Güncelleme indirilemedi");
            else
            {
                Program.Log(error);
                if (++failedInstalls < 3) CheckIn(TimeSpan.FromMinutes(10));
            }
        }
    }
}
