using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace FastShot.Core.Translate;

/// <summary>DeepL's API with the user's own key (https://www.deepl.com/pro-api): free keys (ending in ":fx") use the free
/// endpoint. Nothing is shared between users; each pays nothing or their own plan.</summary>
public static class DeepL
{
    static readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(30) };

    /// <summary>DeepL names English and Portuguese targets by their variant.</summary>
    static string Target(string code) => code switch
    {
        "en" => "EN-US",
        "pt" => "PT-PT",
        _ => code.ToUpperInvariant(),
    };

    /// <returns>The translation and the source language DeepL detected (lower case).</returns>
    public static async Task<(string text, string? detected)> Translate(string text, string? from, string to, string key, CancellationToken cancellation = default)
    {
        var host = key.Trim().EndsWith(":fx", StringComparison.Ordinal) ? "api-free.deepl.com" : "api.deepl.com";
        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://{host}/v2/translate");
        request.Headers.TryAddWithoutValidation("Authorization", $"DeepL-Auth-Key {key.Trim()}");
        var body = new JsonObject { ["text"] = new JsonArray(text), ["target_lang"] = Target(to), ["preserve_formatting"] = true };
        if (from is not null) body["source_lang"] = from.ToUpperInvariant();
        request.Content = JsonContent.Create(body);
        using var response = await http.SendAsync(request, cancellation);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(response.StatusCode switch
            {
                HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized => "DeepL anahtarı geçersiz. Ayarlar › Çeviri'den denetleyin.",
                (HttpStatusCode)456 => "DeepL'in bu ayki karakter hakkı doldu.",
                HttpStatusCode.TooManyRequests => "DeepL'e çok sık istek gönderildi; biraz sonra yeniden deneyin.",
                HttpStatusCode.BadRequest => "DeepL bu dil çiftini çeviremedi.",
                _ => $"DeepL yanıt vermedi ({(int)response.StatusCode}).",
            });
        }
        var json = await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken: cancellation);
        var first = json?["translations"]?[0];
        return (first?["text"]?.GetValue<string>() ?? "", first?["detected_source_language"]?.GetValue<string>()?.ToLowerInvariant());
    }
}
