using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AFMediaBar.Custom.Settings;

/// <summary>
/// Chế độ nội dung lời hiển thị phụ.
/// </summary>
public enum SecondaryLyricsContentMode
{
    /// <summary>Hiển thị song song cả Lời dịch và Pinyin/Phiên âm (nếu có).</summary>
    Both = 0,

    /// <summary>Chỉ hiển thị Lời dịch Tiếng Việt.</summary>
    TranslationOnly = 1,

    /// <summary>Chỉ hiển thị Pinyin / Phiên âm.</summary>
    RomanizationOnly = 2
}

/// <summary>
/// Chiến lược phân bổ vị trí hiển thị song song.
/// </summary>
public enum DualLyricsPlacementStrategy
{
    /// <summary>
    /// Tự động tách biệt: Lời dịch ở Taskbar chính bên đối diện, Pinyin ở Taskbar Màn hình 2.
    /// Nếu máy chỉ có 1 màn hình: Tự động gộp 2 dòng tại Taskbar chính.
    /// </summary>
    AutoSplit = 0,

    /// <summary>Luôn gộp 2 dòng (dịch + pinyin) tại phía đối diện của Taskbar chính.</summary>
    CombinedOppositeTaskbar = 1,

    /// <summary>Luôn gộp 2 dòng tại Taskbar Màn hình thứ 2.</summary>
    CombinedSecondaryMonitor = 2
}

/// <summary>
/// Cấu hình tùy biến dành riêng cho tính năng Song ngữ & Dual Taskbar Lyrics.
/// </summary>
public sealed class CustomSettings
{
    /// <summary>Bật/tắt tính năng dịch và hiển thị song ngữ.</summary>
    public bool EnableBilingualLyrics { get; set; } = true;

    /// <summary>Mã ngôn ngữ đích cần dịch sang (mặc định: "vi" - Tiếng Việt).</summary>
    public string TargetLanguage { get; set; } = "vi";

    /// <summary>Bật/tắt thanh Taskbar phụ hiển thị lời dịch / pinyin.</summary>
    public bool EnableDualTaskbarLyrics { get; set; } = true;

    /// <summary>Chế độ nội dung lời bài hát hiển thị.</summary>
    public SecondaryLyricsContentMode ContentMode { get; set; } = SecondaryLyricsContentMode.Both;

    /// <summary>Chiến lược phân bổ vị trí.</summary>
    public DualLyricsPlacementStrategy PlacementStrategy { get; set; } = DualLyricsPlacementStrategy.AutoSplit;

    /// <summary>Tự động ẩn bản dịch khi lời bài hát gốc đã là Tiếng Việt.</summary>
    public bool AutoSkipVietnamese { get; set; } = true;

    /// <summary>Cỡ chữ hiển thị (DIP).</summary>
    public double FontSize { get; set; } = 12.0;

    /// <summary>Độ mờ đục của chữ (0.2 -> 1.0).</summary>
    public double Opacity { get; set; } = 0.95;
}

/// <summary>
/// Quản lý đọc/ghi cấu hình tùy biến.
/// </summary>
public sealed class CustomSettingsManager
{
    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AFMediaBar");

    private static readonly string SettingsFilePath = Path.Combine(SettingsDirectory, "custom_settings.json");

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public CustomSettings Current { get; private set; } = new();

    public event Action<CustomSettings>? SettingsChanged;

    public CustomSettingsManager()
    {
        LoadSettings();
    }

    /// <summary>
    /// Đọc cấu hình từ ổ đĩa với try-catch an toàn.
    /// </summary>
    public void LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                var json = File.ReadAllText(SettingsFilePath);
                var loaded = JsonSerializer.Deserialize<CustomSettings>(json, _jsonOptions);
                if (loaded != null)
                {
                    Current = loaded;
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CustomSettings] Loi doc settings: {ex.Message}");
        }

        Current = new CustomSettings();
        SaveSettings();
    }

    /// <summary>
    /// Ghi cấu hình xuống ổ đĩa an toàn.
    /// </summary>
    public void SaveSettings()
    {
        try
        {
            if (!Directory.Exists(SettingsDirectory))
            {
                Directory.CreateDirectory(SettingsDirectory);
            }

            var json = JsonSerializer.Serialize(Current, _jsonOptions);
            File.WriteAllText(SettingsFilePath, json);
            SettingsChanged?.Invoke(Current);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CustomSettings] Loi ghi settings: {ex.Message}");
        }
    }
}
