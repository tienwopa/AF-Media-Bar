using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AFMediaBar.Custom.Settings;

/// <summary>
/// Vị trí hiển thị của từng thành phần (Lời dịch / Pinyin).
/// </summary>
public enum LyricElementPlacement
{
    /// <summary>Hiển thị ở phía đối diện của Taskbar chính.</summary>
    PrimaryTaskbar = 0,

    /// <summary>Hiển thị ở Màn hình thứ 2.</summary>
    SecondaryScreen = 1,

    /// <summary>Hiển thị ở cả hai màn hình.</summary>
    BothScreens = 2,

    /// <summary>Không hiển thị thành phần này.</summary>
    None = 3
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

    /// <summary>Vị trí hiển thị của Lời dịch Tiếng Việt.</summary>
    public LyricElementPlacement TranslationPlacement { get; set; } = LyricElementPlacement.PrimaryTaskbar;

    /// <summary>Vị trí hiển thị của Pinyin / Phiên âm.</summary>
    public LyricElementPlacement RomanizationPlacement { get; set; } = LyricElementPlacement.SecondaryScreen;

    /// <summary>Tự động ẩn bản dịch khi lời bài hát gốc đã là Tiếng Việt.</summary>
    public bool AutoSkipVietnamese { get; set; } = true;

    /// <summary>Màu sắc chữ của Lời dịch Tiếng Việt (mã HEX, ví dụ: #FFFFFF).</summary>
    public string TranslationColor { get; set; } = "#FFFFFF";

    /// <summary>Màu sắc chữ của Pinyin / Phiên âm (mã HEX, ví dụ: #FFD54F).</summary>
    public string RomanizationColor { get; set; } = "#FFD54F";

    /// <summary>Cỡ chữ hiển thị (DIP).</summary>
    public double FontSize { get; set; } = 11.5;

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
