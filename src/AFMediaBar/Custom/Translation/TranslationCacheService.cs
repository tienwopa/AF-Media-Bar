using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AFMediaBar.Custom.Translation;

/// <summary>
/// Quản lý bộ nhớ đệm (Cache) hai tầng: RAM (ConcurrentDictionary) và Ổ đĩa (%LOCALAPPDATA%).
/// Giúp hiển thị tức thì và giảm thiểu tối đa số lần gửi yêu cầu dịch qua mạng.
/// </summary>
public sealed class TranslationCacheService
{
    private static readonly string CacheDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AFMediaBar",
        "CustomCache",
        "Translations");

    private readonly ConcurrentDictionary<string, string> _memoryCache = new(StringComparer.Ordinal);

    public TranslationCacheService()
    {
        EnsureCacheDirectory();
    }

    private static void EnsureCacheDirectory()
    {
        try
        {
            if (!Directory.Exists(CacheDirectory))
            {
                Directory.CreateDirectory(CacheDirectory);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TranslationCache] Khong the tao thu muc cache: {ex.Message}");
        }
    }

    /// <summary>
    /// Tạo khóa băm SHA256 từ nội dung văn bản và ngôn ngữ đích.
    /// </summary>
    private static string ComputeKey(string text, string targetLanguage)
    {
        var raw = $"{targetLanguage}::{text.Trim()}";
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    /// Tìm bản dịch từ cache (ưu tiên RAM, sau đó đến ổ cứng).
    /// </summary>
    public bool TryGet(string text, string targetLanguage, out string translatedText)
    {
        translatedText = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var key = ComputeKey(text, targetLanguage);

        // 1. Kiem tra tren RAM
        if (_memoryCache.TryGetValue(key, out var memoryHit))
        {
            translatedText = memoryHit;
            return true;
        }

        // 2. Kiem tra tren O dia
        try
        {
            var filePath = Path.Combine(CacheDirectory, $"{key}.cache");
            if (File.Exists(filePath))
            {
                var diskHit = File.ReadAllText(filePath, Encoding.UTF8);
                _memoryCache[key] = diskHit;
                translatedText = diskHit;
                return true;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TranslationCache] Loi doc cache tu dia: {ex.Message}");
        }

        return false;
    }

    /// <summary>
    /// Lưu bản dịch vào cả RAM và ổ cứng.
    /// </summary>
    public void Set(string text, string targetLanguage, string translatedText)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(translatedText))
        {
            return;
        }

        var key = ComputeKey(text, targetLanguage);
        _memoryCache[key] = translatedText;

        try
        {
            EnsureCacheDirectory();
            var filePath = Path.Combine(CacheDirectory, $"{key}.cache");
            File.WriteAllText(filePath, translatedText, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TranslationCache] Loi ghi cache ra dia: {ex.Message}");
        }
    }
}
