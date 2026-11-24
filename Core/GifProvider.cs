using System.Net.Http;
using System.Text.Json;

namespace Clipped.Core;

public sealed record GifItem(string Id, string Title, string GifUrl, string PreviewUrl, int Width, int Height);

/// <summary>
/// GIF search over the KLIPY API (the Tenor-compatible successor Google retired in June 2026).
/// </summary>
public sealed class GifProvider
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Clipped/0.1 (Windows)");
        return client;
    }

    public string BaseUrl { get; }
    public string ApiKey { get; }
    public string CustomerId { get; }

    public GifProvider(string baseUrl, string apiKey, string customerId)
    {
        BaseUrl = (baseUrl ?? "").TrimEnd('/');
        ApiKey = (apiKey ?? "").Trim();
        CustomerId = string.IsNullOrWhiteSpace(customerId) ? "clipped" : customerId.Trim();
    }

    public bool HasKey => ApiKey.Length > 0;

    /// <summary>Builds a provider from stored settings, letting environment variables win.</summary>
    public static GifProvider FromSettings(Settings settings)
    {
        string key = Env("CLIPPED_GIF_API_KEY") ?? settings.GifApiKey;
        string baseUrl = Env("CLIPPED_GIF_API_BASE") ?? settings.GifApiBase;
        string customer = Env("CLIPPED_GIF_CUSTOMER_ID") ?? settings.GifCustomerId;
        return new GifProvider(baseUrl, key, customer);
    }

    private static string? Env(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public Task<IReadOnlyList<GifItem>> TrendingAsync(int limit = 24, CancellationToken ct = default) =>
        GetAsync($"api/v1/{ApiKey}/gifs/trending?customer_id={Uri.EscapeDataString(CustomerId)}&per_page={limit}&locale=en", ct);

    public Task<IReadOnlyList<GifItem>> SearchAsync(string query, int limit = 24, CancellationToken ct = default) =>
        GetAsync($"api/v1/{ApiKey}/gifs/search?q={Uri.EscapeDataString(query)}&customer_id={Uri.EscapeDataString(CustomerId)}&per_page={limit}&locale=en", ct);

    public async Task<byte[]> DownloadAsync(string url, CancellationToken ct = default)
    {
        using var response = await Http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    private async Task<IReadOnlyList<GifItem>> GetAsync(string path, CancellationToken ct)
    {
        using var response = await Http.GetAsync($"{BaseUrl}/{path}", ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"GIF search failed ({(int)response.StatusCode})");
        return Parse(body);
    }

    private sealed record Media(string Url, string Kind, int Width, int Height);

    internal static List<GifItem> Parse(string json)
    {
        var items = new List<GifItem>();
        using var doc = JsonDocument.Parse(json);
        foreach (var element in Results(doc.RootElement))
        {
            var media = new List<Media>();
            Collect(element, null, media);

            var gif = Pick(media, m => m.Kind == "gif" || m.Url.EndsWith(".gif", StringComparison.OrdinalIgnoreCase), preferLarge: true);
            if (gif is null) gif = Pick(media, m => m.Kind == "mp4", preferLarge: true);
            if (gif is null) gif = Pick(media, _ => true, preferLarge: true);
            if (gif is null) continue;

            var preview = Pick(media, m => m.Kind is "tinygif" or "preview_url" or "preview" or "tiny", preferLarge: false)
                         ?? Pick(media, m => m.Kind == "gif", preferLarge: false)
                         ?? gif;

            var title = Text(element, "title") ?? Text(element, "description") ?? Text(element, "name") ?? "";
            var id = Text(element, "id") ?? Text(element, "slug") ?? "";
            items.Add(new GifItem(id, title, gif.Url, preview.Url, gif.Width, gif.Height));
        }
        return items;
    }

    private static IEnumerable<JsonElement> Results(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
            return root.EnumerateArray();

        foreach (var name in new[] { "data", "gifs", "results", "items", "torrents" })
        {
            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty(name, out var array) && array.ValueKind == JsonValueKind.Array)
                return array.EnumerateArray();
        }

        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
            return Results(data);

        return Array.Empty<JsonElement>();
    }

    private static void Collect(JsonElement element, string? kind, List<Media> acc)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.String)
                    {
                        var name = prop.Name.ToLowerInvariant();
                        bool isUrl = name.EndsWith("url", StringComparison.OrdinalIgnoreCase)
                                    || name is "src" or "uri" or "link";
                        if (!isUrl) continue;

                        var url = prop.Value.GetString();
                        if (string.IsNullOrWhiteSpace(url)) continue;

                        // A bare "url" belongs to the bucket it sits in ("gif", "mp4", "tinygif");
                        // a named one ("preview_url") already says what it is.
                        var mediaKind = name is "url" or "src" or "uri" or "link" && kind is not null ? kind : name;
                        acc.Add(new Media(url, mediaKind, Number(element, "width"), Number(element, "height")));
                        continue;
                    }

                    Collect(prop.Value, prop.Name.ToLowerInvariant(), acc);
                }
                break;
            case JsonValueKind.Array:
                foreach (var child in element.EnumerateArray())
                    Collect(child, kind, acc);
                break;
        }
    }

    private static Media? Pick(List<Media> media, Func<Media, bool> match, bool preferLarge)
    {
        Media? best = null;
        foreach (var candidate in media)
        {
            if (!match(candidate)) continue;
            if (best is null) { best = candidate; continue; }
            if (preferLarge && candidate.Width > best.Width) best = candidate;
            if (!preferLarge && candidate.Width < best.Width) best = candidate;
        }
        return best;
    }

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int Number(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object) return 0;
        if (!element.TryGetProperty(name, out var value)) return 0;
        return value.ValueKind == JsonValueKind.Number ? value.GetInt32() : 0;
    }
}
