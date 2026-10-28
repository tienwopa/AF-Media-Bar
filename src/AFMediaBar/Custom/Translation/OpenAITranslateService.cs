using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AFMediaBar.Custom.Settings;

namespace AFMediaBar.Custom.Translation;

/// <summary>
/// Dịch vụ dịch lời bài hát sử dụng API tương thích OpenAI (OpenAI GPT-4o-mini, DeepSeek V3, Groq...).
/// </summary>
public sealed class OpenAITranslateService : ITranslationService, IDisposable
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    private readonly CustomSettingsManager _settingsManager;
    private readonly TranslationCacheService _cacheService;
    private readonly GoogleTranslateService _fallbackService;

    public OpenAITranslateService(
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

        var apiKey = _settingsManager.Current.OpenAIApiKey?.Trim();
        if (string.IsNullOrEmpty(apiKey))
        {
            return await _fallbackService.TranslateAsync(text, targetLanguage, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            var baseUrl = string.IsNullOrWhiteSpace(_settingsManager.Current.OpenAIBaseUrl)
                ? "https://api.openai.com/v1"
                : _settingsManager.Current.OpenAIBaseUrl.Trim().TrimEnd('/');

            var model = string.IsNullOrWhiteSpace(_settingsManager.Current.OpenAIModel)
                ? "gpt-4o-mini"
                : _settingsManager.Current.OpenAIModel.Trim();

            var url = $"{baseUrl}/chat/completions";

            var requestBody = new
            {
                model,
                messages = new[]
                {
                    new { role = "system", content = "Bạn là dịch giả lời bài hát. Hãy dịch câu hát sang Tiếng Việt cảm xúc, phù hợp âm nhạc. Chỉ trả về duy nhất câu dịch, không giải thích." },
                    new { role = "user", content = text }
                },
                temperature = 0.3
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            using var response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                var responseJson = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(responseJson);

                var choices = doc.RootElement.GetProperty("choices");
                if (choices.GetArrayLength() > 0)
                {
                    var content = choices[0].GetProperty("message").GetProperty("content").GetString()?.Trim();
                    if (TranslationCacheService.IsValidTranslation(text, targetLanguage, content))
                    {
                        _cacheService.Set(text, targetLanguage, content!);
                        return content!;
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[OpenAITranslate] Loi goi API, fallback sang Google Translate: {ex.Message}");
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

        var apiKey = _settingsManager.Current.OpenAIApiKey?.Trim();
        if (string.IsNullOrEmpty(apiKey))
        {
            var fallback = await _fallbackService.TranslateBatchAsync(missingTexts, targetLanguage, cancellationToken).ConfigureAwait(false);
            for (int k = 0; k < missingIndices.Count; k++)
            {
                results[missingIndices[k]] = fallback[k];
            }
            return results;
        }

        const int chunkSize = 50;
        for (int chunkStart = 0; chunkStart < missingTexts.Count; chunkStart += chunkSize)
        {
            if (cancellationToken.IsCancellationRequested) break;

            var count = Math.Min(chunkSize, missingTexts.Count - chunkStart);
            var chunkLines = missingTexts.GetRange(chunkStart, count);
            var chunkIndices = missingIndices.GetRange(chunkStart, count);

            var baseUrl = string.IsNullOrWhiteSpace(_settingsManager.Current.OpenAIBaseUrl)
                ? "https://api.openai.com/v1"
                : _settingsManager.Current.OpenAIBaseUrl.Trim().TrimEnd('/');

            var model = string.IsNullOrWhiteSpace(_settingsManager.Current.OpenAIModel)
                ? "gpt-4o-mini"
                : _settingsManager.Current.OpenAIModel.Trim();

            var url = $"{baseUrl}/chat/completions";

            var sbPrompt = new StringBuilder();
            sbPrompt.AppendLine($"Bạn là dịch giả lời bài hát chuyên nghiệp. Hãy dịch chính xác {count} câu lời bài hát sau sang Tiếng Việt ({targetLanguage}) một cách tự nhiên, cảm xúc.");
            sbPrompt.AppendLine("QUY TẮC:");
            sbPrompt.AppendLine($"1. Phải trả về đúng chính xác {count} dòng kết quả.");
            sbPrompt.AppendLine("2. Mỗi dòng kết quả tương ứng một câu hát gốc theo thứ tự.");
            sbPrompt.AppendLine("3. Chỉ trả về nội dung lời dịch Tiếng Việt, không thêm đánh số thứ tự hoặc markdown.");
            sbPrompt.AppendLine("\nNỘI DUNG CẦN DỊCH:");
            for (int i = 0; i < chunkLines.Count; i++)
            {
                sbPrompt.AppendLine(chunkLines[i]);
            }

            bool chunkSuccess = false;
            try
            {
                var requestBody = new
                {
                    model,
                    messages = new[]
                    {
                        new { role = "user", content = sbPrompt.ToString() }
                    },
                    temperature = 0.3
                };

                using var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json")
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

                using var response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(responseJson);

                    var choices = doc.RootElement.GetProperty("choices");
                    if (choices.GetArrayLength() > 0)
                    {
                        var content = choices[0].GetProperty("message").GetProperty("content").GetString();
                        if (!string.IsNullOrWhiteSpace(content))
                        {
                            var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None)
                                .Select(l => l.Trim())
                                .Where(l => !string.IsNullOrEmpty(l))
                                .ToList();

                            if (lines.Count == count)
                            {
                                for (int j = 0; j < count; j++)
                                {
                                    var orig = chunkLines[j];
                                    var trans = lines[j];
                                    if (TranslationCacheService.IsValidTranslation(orig, targetLanguage, trans))
                                    {
                                        _cacheService.Set(orig, targetLanguage, trans);
                                        results[chunkIndices[j]] = trans;
                                    }
                                    else
                                    {
                                        results[chunkIndices[j]] = string.Empty;
                                    }
                                }
                                chunkSuccess = true;
                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OpenAITranslate] Chunk loi: {ex.Message}");
            }

            if (!chunkSuccess)
            {
                var fallbackChunk = await _fallbackService.TranslateBatchAsync(chunkLines, targetLanguage, cancellationToken).ConfigureAwait(false);
                for (int j = 0; j < count; j++)
                {
                    results[chunkIndices[j]] = fallbackChunk[j];
                }
            }
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
