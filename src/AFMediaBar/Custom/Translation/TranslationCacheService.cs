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
        PurgeCorruptedCacheInBackground();
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
    /// Kiểm tra tính hợp lệ của bản dịch: không được rỗng, không được trùng hoàn toàn câu gốc,
    /// và không được chứa chữ Hán nếu ngôn ngữ đích là Tiếng Việt.
    /// </summary>
    public static bool IsValidTranslation(string originalText, string targetLanguage, string? translatedText)
    {
        if (string.IsNullOrWhiteSpace(translatedText))
        {
            return false;
        }

        var trimmedOriginal = originalText.Trim();
        var trimmedTranslated = translatedText.Trim();

        // Không hợp lệ nếu bản dịch y hệt câu gốc
        if (string.Equals(trimmedOriginal, trimmedTranslated, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Không hợp lệ nếu ngôn ngữ đích là Tiếng Việt ("vi") mà kết quả vẫn chứa ký tự Hán tự tiếng Trung
        if (targetLanguage.StartsWith("vi", StringComparison.OrdinalIgnoreCase) &&
            PinyinGeneratorService.HasChinese(trimmedTranslated))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Quét dọn các tệp cache bị nhiễm độc văn bản gốc tiếng Trung ở chế độ chạy ngầm.
    /// </summary>
    private static void PurgeCorruptedCacheInBackground()
    {
        _ = Task.Run(() =>
        {
            try
            {
                if (!Directory.Exists(CacheDirectory)) return;
                var files = Directory.GetFiles(CacheDirectory, "*.cache");
                foreach (var file in files)
                {
                    try
                    {
                        var content = File.ReadAllText(file, Encoding.UTF8);
                        if (PinyinGeneratorService.HasChinese(content))
                        {
                            File.Delete(file);
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TranslationCache] Loi don dep cache: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Xóa toàn bộ bộ nhớ đệm cả trên RAM lẫn ổ đĩa.
    /// </summary>
    public void ClearCache()
    {
        _memoryCache.Clear();
        try
        {
            if (Directory.Exists(CacheDirectory))
            {
                var files = Directory.GetFiles(CacheDirectory, "*.cache");
                foreach (var f in files)
                {
                    try { File.Delete(f); } catch { }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TranslationCache] Loi clear cache: {ex.Message}");
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
            if (IsValidTranslation(text, targetLanguage, memoryHit))
            {
                translatedText = memoryHit;
                return true;
            }
            // Cache tren RAM bi nhiem doc -> loai bo
            _memoryCache.TryRemove(key, out _);
        }

        // 2. Kiem tra tren O dia
        try
        {
            var filePath = Path.Combine(CacheDirectory, $"{key}.cache");
            if (File.Exists(filePath))
            {
                var diskHit = File.ReadAllText(filePath, Encoding.UTF8);
                if (IsValidTranslation(text, targetLanguage, diskHit))
                {
                    _memoryCache[key] = diskHit;
                    translatedText = diskHit;
                    return true;
                }

                // File cache tren dia khong hop le -> xoa bo tranh nhiem doc tiep
                try
                {
                    File.Delete(filePath);
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TranslationCache] Loi doc cache tu dia: {ex.Message}");
        }

        return false;
    }

    /// <summary>
    /// Lưu bản dịch vào cả RAM và ổ cứng nếu bản dịch hợp lệ.
    /// </summary>
    public void Set(string text, string targetLanguage, string translatedText)
    {
        if (string.IsNullOrWhiteSpace(text) || !IsValidTranslation(text, targetLanguage, translatedText))
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
