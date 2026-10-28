using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AFMediaBar.Custom.Settings;

namespace AFMediaBar.Custom.Translation;

/// <summary>
/// Dịch vụ dịch lời bài hát sử dụng API tương thích OpenAI (OpenAI GPT-4o-mini, DeepSeek V3, Groq...).
/// Hỗ trợ xử lý batching đánh số và fallback từng dòng độc lập.
/// </summary>
public sealed class OpenAITranslateService : ITranslationService, IDisposable
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    private static readonly Regex LineNumberRegex = new(@"^(?:\[(\d+)\]|(\d+)[\.:\)\-]\s*)(.*)$", RegexOptions.Compiled);

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
            sbPrompt.AppendLine("QUY TẮC BẮT BUỘC:");
            sbPrompt.AppendLine($"1. Đánh số thứ tự từ 1 đến {count} ở đầu mỗi câu dịch: 1. [câu 1], 2. [câu 2], v.v.");
            sbPrompt.AppendLine("2. Mỗi câu dịch tương ứng một câu hát gốc theo thứ tự.");
            sbPrompt.AppendLine("3. Không thêm lời chào, giải thích hoặc markdown.");
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
                            var rawLines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
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
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OpenAITranslate] Chunk loi: {ex.Message}");
            }

            var unmappedIndices = new List<int>();
            var unmappedTexts = new List<string>();

            for (int j = 0; j < count; j++)
            {
                var orig = chunkLines[j];
                if (parsedMap.TryGetValue(j, out var trans) && TranslationCacheService.IsValidTranslation(orig, targetLanguage, trans))
                {
                    _cacheService.Set(orig, targetLanguage, trans);
                    results[chunkIndices[j]] = trans;
                }
                else
                {
                    unmappedIndices.Add(chunkIndices[j]);
                    unmappedTexts.Add(orig);
                }
            }

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

    public void Dispose()
    {
    }
}
