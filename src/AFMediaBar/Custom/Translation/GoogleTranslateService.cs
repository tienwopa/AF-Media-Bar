using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace AFMediaBar.Custom.Translation;

/// <summary>
/// Dịch vụ dịch thuật sử dụng Google Translate Web API công khai.
/// Hỗ trợ gom lô (batching) và tích hợp sẵn TranslationCacheService.
/// </summary>
public sealed class GoogleTranslateService : ITranslationService, IDisposable
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(6)
    };

    private readonly TranslationCacheService _cacheService;

    static GoogleTranslateService()
    {
        HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
    }

    public GoogleTranslateService(TranslationCacheService cacheService)
    {
        _cacheService = cacheService;
    }

    public async Task<string> TranslateAsync(string text, string targetLanguage, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        // 1. Kiem tra cache
        if (_cacheService.TryGet(text, targetLanguage, out var cached))
        {
            return cached;
        }

        try
        {
            var url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&tl={targetLanguage}&dt=t&q={Uri.EscapeDataString(text)}";
            using var response = await HttpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return text; // Fallback giu nguyen text neu mang loi
            }

            var jsonStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(jsonStream, cancellationToken: cancellationToken).ConfigureAwait(false);

            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
            {
                var sentencesArray = root[0];
                if (sentencesArray.ValueKind == JsonValueKind.Array)
                {
                    var sb = new StringBuilder();
                    foreach (var sentence in sentencesArray.EnumerateArray())
                    {
                        if (sentence.ValueKind == JsonValueKind.Array && sentence.GetArrayLength() > 0)
                        {
                            var translatedPart = sentence[0].GetString();
                            if (!string.IsNullOrEmpty(translatedPart))
                            {
                                sb.Append(translatedPart);
                            }
                        }
                    }

                    var result = sb.ToString();
                    if (!string.IsNullOrWhiteSpace(result))
                    {
                        _cacheService.Set(text, targetLanguage, result);
                        return result;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Tac vu bi huy
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GoogleTranslate] Loi goi API: {ex.Message}");
        }

        return text;
    }

    public async Task<IReadOnlyList<string>> TranslateBatchAsync(
        IReadOnlyList<string> texts,
        string targetLanguage,
        CancellationToken cancellationToken = default)
    {
        if (texts.Count == 0)
        {
            return Array.Empty<string>();
        }

        var results = new string[texts.Count];
        var missingIndices = new List<int>();
        var missingTexts = new List<string>();

        // 1. Kiem tra cache cho tung dong truoc
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
                missingIndices.Add(i);
                missingTexts.Add(t);
            }
        }

        // Neu tat ca deu da co trong cache thi tra ve luon!
        if (missingIndices.Count == 0)
        {
            return results;
        }

        // 2. Gom nhung dong chua co cache thanh cac chunk toi da 30 dong de dich 1 luot
        const int chunkSize = 30;
        for (int chunkStart = 0; chunkStart < missingTexts.Count; chunkStart += chunkSize)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var count = Math.Min(chunkSize, missingTexts.Count - chunkStart);
            var chunkLines = missingTexts.GetRange(chunkStart, count);
            var chunkIndices = missingIndices.GetRange(chunkStart, count);

            // Dung delimiter ky tu xuong dong
            var combinedText = string.Join("\n", chunkLines);
            var translatedCombined = await TranslateAsync(combinedText, targetLanguage, cancellationToken).ConfigureAwait(false);

            var translatedLines = translatedCombined.Split('\n');
            for (int j = 0; j < count; j++)
            {
                var original = chunkLines[j];
                var translated = j < translatedLines.Length ? translatedLines[j].Trim() : original;

                if (!string.IsNullOrWhiteSpace(translated))
                {
                    _cacheService.Set(original, targetLanguage, translated);
                }

                results[chunkIndices[j]] = translated;
            }
        }

        // Dien lai gia tri mac dinh cho cac dong bi bo sot neu co
        for (int i = 0; i < results.Length; i++)
        {
            results[i] ??= texts[i];
        }

        return results;
    }

    public void Dispose()
    {
        // Khong dispose static HttpClient
    }
}
