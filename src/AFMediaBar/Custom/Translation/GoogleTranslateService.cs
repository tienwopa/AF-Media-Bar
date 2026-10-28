using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace AFMediaBar.Custom.Translation;

/// <summary>
/// Dịch vụ dịch thuật sử dụng Google Translate Web API công khai.
/// Hỗ trợ phương thức HTTP POST chống chặn, cơ chế fallback đa endpoint và tích hợp sẵn TranslationCacheService.
/// </summary>
public sealed class GoogleTranslateService : ITranslationService, IDisposable
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(8)
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

        // 1. Kiểm tra cache trước
        if (_cacheService.TryGet(text, targetLanguage, out var cached))
        {
            return cached;
        }

        var translated = await TranslateRawAsync(text, targetLanguage, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(translated) && TranslationCacheService.IsValidTranslation(text, targetLanguage, translated))
        {
            _cacheService.Set(text, targetLanguage, translated);
            return translated;
        }

        return string.Empty;
    }

    /// <summary>
    /// Thực hiện dịch văn bản thô qua Google Translate với cơ chế thử lại đa tầng (Multi-tier Fallback).
    /// </summary>
    private static async Task<string> TranslateRawAsync(string text, string targetLanguage, CancellationToken cancellationToken)
    {
        // Tầng 1: Sử dụng HTTP POST tới translate.googleapis.com (không bị giới hạn độ dài URL và tránh chặn WAF)
        try
        {
            const string postUrl = "https://translate.googleapis.com/translate_a/single?client=gtx&dt=t";
            var formFields = new Dictionary<string, string>
            {
                ["sl"] = "auto",
                ["tl"] = targetLanguage,
                ["q"] = text
            };

            using var formContent = new FormUrlEncodedContent(formFields);
            using var response = await HttpClient.PostAsync(postUrl, formContent, cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
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
                        if (TranslationCacheService.IsValidTranslation(text, targetLanguage, result))
                        {
                            return result;
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GoogleTranslate] Post primary loi: {ex.Message}");
        }

        // Tầng 2: Fallback sang clients5.google.com endpoint (Chrome extension translation endpoint)
        try
        {
            var chromeUrl = $"https://clients5.google.com/translate_a/t?client=dict-chrome-ex&sl=auto&tl={Uri.EscapeDataString(targetLanguage)}";
            var formFields = new Dictionary<string, string>
            {
                ["q"] = text
            };

            using var formContent = new FormUrlEncodedContent(formFields);
            using var response = await HttpClient.PostAsync(chromeUrl, formContent, cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                var jsonStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                using var doc = await JsonDocument.ParseAsync(jsonStream, cancellationToken: cancellationToken).ConfigureAwait(false);

                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
                {
                    var firstElem = root[0];
                    string? trans = null;

                    if (firstElem.ValueKind == JsonValueKind.Array && firstElem.GetArrayLength() > 0)
                    {
                        trans = firstElem[0].GetString();
                    }
                    else if (firstElem.ValueKind == JsonValueKind.String)
                    {
                        trans = firstElem.GetString();
                    }

                    if (TranslationCacheService.IsValidTranslation(text, targetLanguage, trans))
                    {
                        return trans!;
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GoogleTranslate] Clients5 fallback loi: {ex.Message}");
        }

        // Tầng 3: Fallback qua HTTP GET truyền thống
        try
        {
            var getUrl = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&tl={targetLanguage}&dt=t&q={Uri.EscapeDataString(text)}";
            using var response = await HttpClient.GetAsync(getUrl, cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
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
                        if (TranslationCacheService.IsValidTranslation(text, targetLanguage, result))
                        {
                            return result;
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GoogleTranslate] Get fallback loi: {ex.Message}");
        }

        return string.Empty;
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

        // 1. Kiểm tra cache cho từng dòng trước
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

        // Nếu tất cả đều đã có trong cache thì trả về ngay!
        if (missingIndices.Count == 0)
        {
            return results;
        }

        // 2. Gom những dòng chưa có cache thành các chunk tối đa 25 dòng
        const int chunkSize = 25;
        for (int chunkStart = 0; chunkStart < missingTexts.Count; chunkStart += chunkSize)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var count = Math.Min(chunkSize, missingTexts.Count - chunkStart);
            var chunkLines = missingTexts.GetRange(chunkStart, count);
            var chunkIndices = missingIndices.GetRange(chunkStart, count);

            var combinedText = string.Join("\n", chunkLines);
            var translatedCombined = await TranslateRawAsync(combinedText, targetLanguage, cancellationToken).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(translatedCombined))
            {
                var translatedLines = translatedCombined.Split('\n');
                for (int j = 0; j < count; j++)
                {
                    var original = chunkLines[j];
                    var trans = j < translatedLines.Length ? translatedLines[j].Trim() : string.Empty;

                    if (TranslationCacheService.IsValidTranslation(original, targetLanguage, trans))
                    {
                        _cacheService.Set(original, targetLanguage, trans);
                        results[chunkIndices[j]] = trans;
                    }
                    else
                    {
                        results[chunkIndices[j]] = string.Empty;
                    }
                }
            }
            else
            {
                for (int j = 0; j < count; j++)
                {
                    results[chunkIndices[j]] = string.Empty;
                }
            }
        }

        // Tuyệt đối không gán lời gốc vào kết quả dịch nếu dịch thất bại
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
