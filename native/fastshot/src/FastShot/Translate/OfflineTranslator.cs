using System.Text.Json.Nodes;
using System.Windows.Interop;
using FastShot.Core.Translate;
using Microsoft.Web.WebView2.Core;

namespace FastShot;

/// <summary>Translates on this PC with Firefox's engine (bergamot-translator 0.4.9, MPL-2.0, WebAssembly; files in
/// Translate/Engine) running in a hidden WebView2, with models from <see cref="TranslationModels"/>. No key, no server:
/// the text never leaves the computer. Everything runs on the UI thread, one request at a time.</summary>
public sealed class OfflineTranslator
{
    static OfflineTranslator? shared;
    public static OfflineTranslator Shared => shared ??= new();

    const string Host = "translate.fastshot";
    /// <summary>Bump with the engine files, so an old copy on disk is replaced.</summary>
    const string EngineVersion = "bergamot-0.4.9";

    readonly SemaphoreSlim turn = new(1, 1);
    readonly Dictionary<int, TaskCompletionSource<string>> pending = [];
    HwndSource? host;
    CoreWebView2Controller? controller;
    Task? starting;
    TaskCompletionSource? ready;
    int nextId;

    /// <summary>Translates <paramref name="text"/> (paragraphs separated by blank lines). Downloads the models it needs
    /// first; <paramref name="status"/> hears about that ("Türkçe → İngilizce modeli indiriliyor %40").</summary>
    public async Task<string> Translate(string text, string from, string to, Action<string>? status = null, CancellationToken cancellation = default)
    {
        if (from == to || string.IsNullOrWhiteSpace(text)) return text;
        await turn.WaitAsync(cancellation);
        try
        {
            var added = await TranslationModels.Ensure(from, to, (pair, fraction) =>
                Application.Current.Dispatcher.BeginInvoke(() => status?.Invoke($"{pair} modeli indiriliyor… %{fraction * 100:0}")), cancellation);
            status?.Invoke("Çevriliyor…");
            await (starting ??= Start());
            if (added) Post(new JsonObject { ["type"] = "reload" });
            var id = ++nextId;
            var answer = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            pending[id] = answer;
            Post(new JsonObject { ["type"] = "translate", ["id"] = id, ["from"] = from, ["to"] = to, ["text"] = text });
            using (cancellation.Register(() => answer.TrySetCanceled()))
                return await answer.Task;
        }
        finally { turn.Release(); }
    }

    void Post(JsonObject message) => controller!.CoreWebView2.PostWebMessageAsJson(message.ToJsonString());

    async Task Start()
    {
        try
        {
            var root = TranslationModels.Root;
            ExtractEngine(root);
            if (!File.Exists(TranslationModels.RegistryPath)) TranslationModels.WriteRegistry();
            // WebView2 needs a window; this one is never shown.
            host = new HwndSource(new HwndSourceParameters("FastShot translator") { WindowStyle = unchecked((int)0x80000000), Width = 1, Height = 1, PositionX = -32000, PositionY = -32000 });
            var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FastShot", "WebView2");
            var environment = await CoreWebView2Environment.CreateAsync(null, data);
            controller = await environment.CreateCoreWebView2ControllerAsync(host.Handle);
            controller.IsVisible = false;
            var web = controller.CoreWebView2;
            web.Settings.AreDevToolsEnabled = false;
            web.Settings.AreDefaultContextMenusEnabled = false;
            web.SetVirtualHostNameToFolderMapping(Host, root, CoreWebView2HostResourceAccessKind.Deny);
            web.WebMessageReceived += Received;
            ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            web.Navigate($"https://{Host}/translate.html");
            if (await Task.WhenAny(ready.Task, Task.Delay(TimeSpan.FromSeconds(30))) != ready.Task)
                throw new TimeoutException("Çeviri motoru açılamadı.");
        }
        catch (WebView2RuntimeNotFoundException)
        {
            starting = null;
            throw new InvalidOperationException("Çevrimdışı çeviri için Microsoft Edge WebView2 gerekiyor ve bu bilgisayarda yok. https://go.microsoft.com/fwlink/p/?LinkId=2124703 adresinden kurabilir ya da Ayarlar › Çeviri'den DeepL anahtarınızı girebilirsiniz.");
        }
        catch
        {
            starting = null;
            Close();
            throw;
        }
    }

    void Received(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        var message = JsonNode.Parse(e.WebMessageAsJson);
        if (message?["type"]?.GetValue<string>() == "ready")
        {
            ready?.TrySetResult();
            return;
        }
        if (message?["id"]?.GetValue<int>() is not { } id || !pending.Remove(id, out var answer)) return;
        if (message["error"]?.GetValue<string>() is { } error) answer.TrySetException(new InvalidOperationException($"Çeviri yapılamadı: {error}"));
        else answer.TrySetResult(message["text"]?.GetValue<string>() ?? "");
    }

    /// <summary>The page and the engine files, from the exe into the translation folder the WebView2 serves.</summary>
    static void ExtractEngine(string root)
    {
        var engine = Path.Combine(root, "engine");
        var stamp = Path.Combine(engine, ".version");
        if (!File.Exists(stamp) || File.ReadAllText(stamp) != EngineVersion)
        {
            if (Directory.Exists(engine)) Directory.Delete(engine, true);
            Directory.CreateDirectory(Path.Combine(engine, "worker"));
            foreach (var name in new[] { "translator.js", "worker/translator-worker.js", "worker/bergamot-translator-worker.js", "worker/bergamot-translator-worker.wasm" })
                Extract($"FastShot.Translate.{name.Replace('/', '.')}", Path.Combine(engine, name));
            File.WriteAllText(stamp, EngineVersion);
        }
        Extract("FastShot.Translate.translate.html", Path.Combine(root, "translate.html"));
    }

    static void Extract(string resource, string path)
    {
        using var stream = typeof(OfflineTranslator).Assembly.GetManifestResourceStream(resource)
                           ?? throw new InvalidOperationException($"{resource} is not embedded.");
        using var file = File.Create(path);
        stream.CopyTo(file);
    }

    public void Close()
    {
        foreach (var answer in pending.Values) answer.TrySetCanceled();
        pending.Clear();
        controller?.Close();
        controller = null;
        host?.Dispose();
        host = null;
        starting = null;
    }
}

