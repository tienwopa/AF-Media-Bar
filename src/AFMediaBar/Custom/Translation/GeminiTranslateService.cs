using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AFMediaBar.Custom.Settings;

namespace AFMediaBar.Custom.Translation;

/// <summary>
/// Dịch vụ dịch lời bài hát sử dụng Google Gemini AI API.
/// Dịch tự nhiên, cảm xúc, hiểu ngữ cảnh âm nhạc, hỗ trợ batching đánh số và fallback từng dòng tự động.
/// </summary>
public sealed class GeminiTranslateService : ITranslationService, IDisposable
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    private static readonly Regex LineNumberRegex = new(@"^(?:\[(\d+)\]|(\d+)[\.:\)\-]\s*)(.*)$", RegexOptions.Compiled);

    private readonly CustomSettingsManager _settingsManager;
    private readonly TranslationCacheService _cacheService;
    private readonly GoogleTranslateService _fallbackService;

    public GeminiTranslateService(
        CustomSettingsManager settingsManager,
        TranslationCacheService cacheService,
        GoogleTranslateService fallbackService)
    {
        _settingsManager = settingsManager;
        _cacheService = cacheService;
        _fallbackService = fallbackService;
    }

    public async Task<string> TranslateAsync(string text, string targetLanguage, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        if (_cacheService.TryGet(text, targetLanguage, out var cached))
        {
            return cached;
        }

        var apiKey = _settingsManager.Current.GeminiApiKey?.Trim();
        if (string.IsNullOrEmpty(apiKey))
        {
            return await _fallbackService.TranslateAsync(text, targetLanguage, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            var model = ResolveModel(_settingsManager.Current.GeminiModel);
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";

            var prompt = $"Bạn là dịch giả lời bài hát chuyên nghiệp. Hãy dịch câu hát sau sang Tiếng Việt ({targetLanguage}) một cách tự nhiên, cảm xúc, phù hợp âm nhạc. Chỉ trả về câu dịch duy nhất, không giải thích gì thêm:\n{text}";

            var requestBody = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new[]
                        {
                            new { text = prompt }
                        }
                    }
                },
                generationConfig = new
                {
                    temperature = 0.3,
                    maxOutputTokens = 256
                }
            };

            var jsonContent = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            using var response = await HttpClient.PostAsync(url, jsonContent, cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                var responseJson = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(responseJson);

                var candidates = doc.RootElement.GetProperty("candidates");
                if (candidates.GetArrayLength() > 0)
                {
                    var content = candidates[0].GetProperty("content");
                    var parts = content.GetProperty("parts");
                    if (parts.GetArrayLength() > 0)
                    {
                        var translated = parts[0].GetProperty("text").GetString()?.Trim();
                        if (TranslationCacheService.IsValidTranslation(text, targetLanguage, translated))
                        {
                            _cacheService.Set(text, targetLanguage, translated!);
                            return translated!;
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GeminiTranslate] Loi goi API, fallback sang Google Translate: {ex.Message}");
        }

        return await _fallbackService.TranslateAsync(text, targetLanguage, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> TranslateBatchAsync(
        IReadOnlyList<string> texts,
        string targetLanguage,
        CancellationToken cancellationToken = default)
    {
        if (texts.Count == 0) return Array.Empty<string>();

        var results = new string[texts.Count];
        var missingIndices = new List<int>();
        var missingTexts = new List<string>();

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

        if (missingIndices.Count == 0) return results;

        var apiKey = _settingsManager.Current.GeminiApiKey?.Trim();
        if (string.IsNullOrEmpty(apiKey))
        {
            var fallbackMissing = await _fallbackService.TranslateBatchAsync(missingTexts, targetLanguage, cancellationToken).ConfigureAwait(false);
            for (int k = 0; k < missingIndices.Count; k++)
            {
                results[missingIndices[k]] = fallbackMissing[k];
            }
            return results;
        }

        // Gom nhóm 50 dòng mỗi lượt để xử lý trọn vẹn trong 1 request duy nhất
        const int chunkSize = 50;
        for (int chunkStart = 0; chunkStart < missingTexts.Count; chunkStart += chunkSize)
        {
            if (cancellationToken.IsCancellationRequested) break;

            var count = Math.Min(chunkSize, missingTexts.Count - chunkStart);
            var chunkLines = missingTexts.GetRange(chunkStart, count);
            var chunkIndices = missingIndices.GetRange(chunkStart, count);

            var model = ResolveModel(_settingsManager.Current.GeminiModel);
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";

            var sbPrompt = new StringBuilder();
            sbPrompt.AppendLine($"Bạn là dịch giả lời bài hát chuyên nghiệp. Hãy dịch chính xác {count} câu lời bài hát sau sang Tiếng Việt ({targetLanguage}) một cách tự nhiên, cảm xúc, phù hợp âm nhạc.");
            sbPrompt.AppendLine("QUY TẮC BẮT BUỘC:");
            sbPrompt.AppendLine($"1. Đánh số thứ tự từ 1 đến {count} ở đầu mỗi câu dịch: 1. [câu 1], 2. [câu 2], v.v.");
            sbPrompt.AppendLine("2. Mỗi câu dịch tương ứng một câu hát gốc theo đúng số thứ tự.");
            sbPrompt.AppendLine("3. Chỉ trả về nội dung lời dịch Tiếng Việt, không thêm lời chào, giải thích hoặc markdown.");
            sbPrompt.AppendLine("\nNỘI DUNG CẦN DỊCH:");
            for (int i = 0; i < chunkLines.Count; i++)
            {
                sbPrompt.AppendLine($"{i + 1}. {chunkLines[i]}");
            }

            var parsedMap = new Dictionary<int, string>();
            try
            {
                var requestBody = new
                {
                    contents = new[]
                    {
                        new
                        {
                            parts = new[]
                            {
                                new { text = sbPrompt.ToString() }
                            }
                        }
                    },
                    generationConfig = new
                    {
                        temperature = 0.3,
                        maxOutputTokens = 2048
                    }
                };

                var jsonContent = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
                using var response = await HttpClient.PostAsync(url, jsonContent, cancellationToken).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(responseJson);

                    var candidates = doc.RootElement.GetProperty("candidates");
                    if (candidates.GetArrayLength() > 0)
                    {
                        var content = candidates[0].GetProperty("content");
                        var parts = content.GetProperty("parts");
                        if (parts.GetArrayLength() > 0)
                        {
                            var fullText = parts[0].GetProperty("text").GetString();
                            if (!string.IsNullOrWhiteSpace(fullText))
                            {
                                var rawLines = fullText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                                foreach (var rawLine in rawLines)
                                {
                                    var match = LineNumberRegex.Match(rawLine.Trim());
                                    if (match.Success)
                                    {
                                        string numStr = !string.IsNullOrEmpty(match.Groups[1].Value) ? match.Groups[1].Value : match.Groups[2].Value;
                                        if (int.TryParse(numStr, out int num) && num >= 1 && num <= count)
                                        {
                                            parsedMap[num - 1] = match.Groups[3].Value.Trim();
                                        }
                                    }
                                }

                                // Fallback nếu Gemini không đánh số nhưng trả về đủ dòng
                                if (parsedMap.Count == 0 && rawLines.Length == count)
                                {
                                    for (int j = 0; j < count; j++)
                                    {
                                        parsedMap[j] = rawLines[j].Trim();
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GeminiTranslate] Chunk loi: {ex.Message}");
            }

            // Gán các dòng đã dịch thành công và gom các dòng còn thiếu
            var unmappedIndices = new List<int>();
            var unmappedTexts = new List<string>();

            for (int j = 0; j < count; j++)
            {
                var original = chunkLines[j];
                if (parsedMap.TryGetValue(j, out var trans) && TranslationCacheService.IsValidTranslation(original, targetLanguage, trans))
                {
                    _cacheService.Set(original, targetLanguage, trans);
                    results[chunkIndices[j]] = trans;
                }
                else
                {
                    unmappedIndices.Add(chunkIndices[j]);
                    unmappedTexts.Add(original);
                }
            }

            // Chỉ fallback sang Google Translate cho những dòng còn thiếu (nếu có)!
            if (unmappedTexts.Count > 0)
            {
                var fallbackChunk = await _fallbackService.TranslateBatchAsync(unmappedTexts, targetLanguage, cancellationToken).ConfigureAwait(false);
                for (int m = 0; m < unmappedTexts.Count; m++)
                {
                    results[unmappedIndices[m]] = fallbackChunk[m];
                }
            }
        }

        for (int i = 0; i < results.Length; i++)
        {
            results[i] ??= string.Empty;
        }

        return results;
    }

    private static string ResolveModel(string? configuredModel)
    {
        if (string.IsNullOrWhiteSpace(configuredModel)) return "gemini-3.5-flash";
        var trimmed = configuredModel.Trim();
        if (trimmed.StartsWith("gemini-1.5", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("gemini-2.0", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "gemini-2.5-flash", StringComparison.OrdinalIgnoreCase))
        {
            return "gemini-3.5-flash";
        }
        return trimmed;
    }

    public void Dispose()
    {
    }
}
