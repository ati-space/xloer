using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FastShot.Core.Translate;

/// <summary>Firefox's translation models (github.com/mozilla/translations, MPL-2.0), downloaded the way Firefox gets them:
/// from Mozilla's Remote Settings, one language direction at a time, checked against their SHA-256, then kept in
/// %LOCALAPPDATA%\FastShot\Translation. Only version 1 models are used: the bundled engine (bergamot-translator 0.4.9)
/// predates the version 2 architecture. A pair without English goes through English, as in Firefox.</summary>
public static class TranslationModels
{
    const string RecordsUrl = "https://firefox.settings.services.mozilla.com/v1/buckets/main/collections/translations-models/records";
    const string AttachmentsUrl = "https://firefox-settings-attachments.cdn.mozilla.net/";
    static readonly TimeSpan catalogAge = TimeSpan.FromDays(7);
    static readonly HttpClient http = new() { Timeout = TimeSpan.FromMinutes(5) };
    static readonly SemaphoreSlim gate = new(1, 1);

    public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FastShot", "Translation");
    static string ModelsFolder => Path.Combine(Root, "models");
    static string CatalogPath => Path.Combine(Root, "catalog.json");
    public static string RegistryPath => Path.Combine(Root, "registry.json");

    public sealed record ModelFile(string Part, string Name, string Location, string Sha256, long Size);

    /// <summary>The directions a translation needs: one model, or two through English.</summary>
    public static List<(string from, string to)> Steps(string from, string to) =>
        from == to ? [] : from == "en" || to == "en" ? [(from, to)] : [(from, "en"), ("en", to)];

    public static bool IsDownloaded(string from, string to) => File.Exists(Path.Combine(ModelsFolder, from + to, ".complete"));

    /// <summary>Downloaded directions and the space they use.</summary>
    public static (int pairs, long bytes) Usage()
    {
        if (!Directory.Exists(ModelsFolder)) return (0, 0);
        var folders = Directory.GetDirectories(ModelsFolder).Where(d => File.Exists(Path.Combine(d, ".complete"))).ToList();
        return (folders.Count, folders.Sum(d => Directory.GetFiles(d).Sum(f => new FileInfo(f).Length)));
    }

    public static void DeleteAll()
    {
        if (Directory.Exists(ModelsFolder)) Directory.Delete(ModelsFolder, true);
        WriteRegistry();
    }

    /// <summary>Makes sure every direction of a translation is downloaded. Reports "Türkçe → İngilizce" and a 0–1 fraction.
    /// Returns true when something new arrived (the engine then reloads its registry).</summary>
    public static async Task<bool> Ensure(string from, string to, Action<string, double>? progress = null, CancellationToken cancellation = default)
    {
        var added = false;
        foreach (var (a, b) in Steps(from, to))
        {
            if (IsDownloaded(a, b)) continue;
            await gate.WaitAsync(cancellation);
            try
            {
                if (IsDownloaded(a, b)) continue;
                var files = await Files(a, b, cancellation) ?? throw new InvalidOperationException(
                    $"{Languages.Name(a)} → {Languages.Name(b)} için çevrimdışı model yok. Ayarlar › Çeviri'den DeepL kullanabilirsiniz.");
                await Download(a, b, files, (fraction) => progress?.Invoke($"{Languages.Name(a)} → {Languages.Name(b)}", fraction), cancellation);
                added = true;
            }
            finally { gate.Release(); }
        }
        if (added) WriteRegistry();
        return added;
    }

    /// <summary>The files of the newest version 1 model for a direction, from the cached catalog (refreshed weekly).</summary>
    static async Task<List<ModelFile>?> Files(string from, string to, CancellationToken cancellation)
    {
        var records = await Catalog(cancellation);
        var candidates = records
            // Records with a filter expression are for test channels of Firefox.
            .Where(r => r["fromLang"]?.GetValue<string>() == from && r["toLang"]?.GetValue<string>() == to && string.IsNullOrEmpty(r["filter_expression"]?.ToString()))
            .Select(r => (record: r, version: Version.TryParse(r["version"]?.GetValue<string>(), out var v) ? v : null))
            .Where(x => x.version is { Major: 1 })
            .ToList();
        if (candidates.Count == 0) return null;
        var newest = candidates.Max(x => x.version)!;
        var files = candidates.Where(x => x.version == newest).Select(x => x.record).Select(r => new ModelFile(
            r["fileType"]!.GetValue<string>(), r["name"]!.GetValue<string>(), r["attachment"]!["location"]!.GetValue<string>(),
            r["attachment"]!["hash"]!.GetValue<string>(), r["attachment"]!["size"]!.GetValue<long>())).ToList();
        var parts = files.Select(f => f.Part).ToHashSet();
        var complete = parts.Contains("model") && parts.Contains("lex") && (parts.Contains("vocab") || (parts.Contains("srcvocab") && parts.Contains("trgvocab")));
        return complete ? files : null;
    }

    static async Task<List<JsonNode>> Catalog(CancellationToken cancellation)
    {
        Directory.CreateDirectory(Root);
        var fresh = File.Exists(CatalogPath) && DateTime.UtcNow - File.GetLastWriteTimeUtc(CatalogPath) < catalogAge;
        if (!fresh)
        {
            try
            {
                var json = await http.GetStringAsync(RecordsUrl, cancellation);
                if (JsonNode.Parse(json)?["data"] is JsonArray) await File.WriteAllTextAsync(CatalogPath, json, cancellation);
            }
            catch (Exception) when (File.Exists(CatalogPath))
            {
                // Offline: the older catalog still lists the models.
            }
        }
        if (!File.Exists(CatalogPath)) throw new IOException("Çeviri modelleri listesi indirilemedi. İnternet bağlantınızı denetleyin.");
        var data = JsonNode.Parse(await File.ReadAllTextAsync(CatalogPath, cancellation))?["data"] as JsonArray;
        return data?.Where(n => n is not null).Select(n => n!).ToList() ?? [];
    }

    static async Task Download(string from, string to, List<ModelFile> files, Action<double> progress, CancellationToken cancellation)
    {
        var folder = Path.Combine(ModelsFolder, from + to);
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
        Directory.CreateDirectory(folder);
        var total = files.Sum(f => f.Size);
        long done = 0;
        var reported = -1;
        foreach (var file in files)
        {
            var path = Path.Combine(folder, file.Name);
            using (var response = await http.GetAsync(AttachmentsUrl + file.Location, HttpCompletionOption.ResponseHeadersRead, cancellation))
            {
                response.EnsureSuccessStatusCode();
                await using var input = await response.Content.ReadAsStreamAsync(cancellation);
                await using var output = File.Create(path);
                var buffer = new byte[81920];
                int read;
                while ((read = await input.ReadAsync(buffer, cancellation)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellation);
                    done += read;
                    var percent = total > 0 ? (int)(done * 100 / total) : 0;
                    if (percent == reported) continue;
                    reported = percent;
                    progress(percent / 100.0);
                }
            }
            await using (var check = File.OpenRead(path))
            {
                var hash = System.Convert.ToHexString(await SHA256.HashDataAsync(check, cancellation));
                if (!hash.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    Directory.Delete(folder, true);
                    throw new IOException("İndirilen çeviri modeli bozuk geldi. Yeniden deneyin.");
                }
            }
        }
        await File.WriteAllTextAsync(Path.Combine(folder, "files.json"), JsonSerializer.Serialize(files), cancellation);
        await File.WriteAllTextAsync(Path.Combine(folder, ".complete"), "", cancellation);
    }

    /// <summary>The engine's model list (bergamot-translator's registry format) for the downloaded directions.</summary>
    public static void WriteRegistry()
    {
        Directory.CreateDirectory(Root);
        var registry = new JsonObject();
        if (Directory.Exists(ModelsFolder))
        {
            foreach (var folder in Directory.GetDirectories(ModelsFolder))
            {
                var list = Path.Combine(folder, "files.json");
                if (!File.Exists(Path.Combine(folder, ".complete")) || !File.Exists(list)) continue;
                var pair = Path.GetFileName(folder);
                var entry = new JsonObject();
                foreach (var file in JsonSerializer.Deserialize<List<ModelFile>>(File.ReadAllText(list)) ?? [])
                    entry[file.Part] = new JsonObject { ["name"] = $"models/{pair}/{file.Name}", ["size"] = file.Size, ["expectedSha256Hash"] = file.Sha256 };
                registry[pair] = entry;
            }
        }
        File.WriteAllText(RegistryPath, registry.ToJsonString());
    }
}
