using System.Net.Http;
using System.Text.Json;

namespace AFMediaBar.Custom.Translation;

/// <summary>
/// Dịch vụ dịch thuật miễn phí dự phòng sử dụng MyMemory / Web Translation API.
/// Hoạt động độc lập không cần API Key, hỗ trợ đệm cache và xử lý song song để tăng tốc độ.
/// </summary>
public sealed class BingTranslateService : ITranslationService, IDisposable
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(6)
    };

    private readonly TranslationCacheService _cacheService;

    static BingTranslateService()
    {
        HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
    }

    public BingTranslateService(TranslationCacheService cacheService)
    {
        _cacheService = cacheService;
    }

    public async Task<string> TranslateAsync(string text, string targetLanguage, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        if (_cacheService.TryGet(text, targetLanguage, out var cached))
        {
            return cached;
        }

        try
        {
            var url = $"https://api.mymemory.translated.net/get?q={Uri.EscapeDataString(text)}&langpair=autodetect|{targetLanguage}";
            using var response = await HttpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                var jsonStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                using var doc = await JsonDocument.ParseAsync(jsonStream, cancellationToken: cancellationToken).ConfigureAwait(false);

                if (doc.RootElement.TryGetProperty("responseData", out var respData) &&
                    respData.TryGetProperty("translatedText", out var transElem))
                {
                    var result = transElem.GetString();
                    if (!string.IsNullOrWhiteSpace(result) &&
                        !result.Contains("MYMEMORY WARNING") &&
                        TranslationCacheService.IsValidTranslation(text, targetLanguage, result))
                    {
                        _cacheService.Set(text, targetLanguage, result);
                        return result;
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BingTranslateService] Loi goi API: {ex.Message}");
        }

        return string.Empty;
    }

    public async Task<IReadOnlyList<string>> TranslateBatchAsync(
        IReadOnlyList<string> texts,
        string targetLanguage,
        CancellationToken cancellationToken = default)
    {
        if (texts.Count == 0) return Array.Empty<string>();

        var results = new string[texts.Count];
        var missing = new List<(int index, string text)>();

        for (int i = 0; i < texts.Count; i++)
        {
            var t = texts[i];
            if (string.IsNullOrWhiteSpace(t))
            {
                results[i] = string.Empty;
            }
            else if (_cacheService.TryGet(t, targetLanguage, out var cached))
            {
                results[i] = cached;
            }
            else
            {
                missing.Add((i, t));
            }
        }

        if (missing.Count > 0)
        {
            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = 4,
                CancellationToken = cancellationToken
            };

            await Parallel.ForEachAsync(missing, parallelOptions, async (item, ct) =>
            {
                results[item.index] = await TranslateAsync(item.text, targetLanguage, ct).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }

        for (int i = 0; i < results.Length; i++)
        {
            results[i] ??= string.Empty;
        }

        return results;
    }

    public void Dispose()
    {
    }
}
