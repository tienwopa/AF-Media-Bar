using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AFMediaBar.Custom.Settings;

/// <summary>
/// Chế độ hiển thị vị trí của Dual Overlay.
/// </summary>
public enum DualOverlayPlacementMode
{
    /// <summary>Phía đối diện trên cùng thanh Taskbar.</summary>
    TaskbarOpposite = 0,

    /// <summary>Đặt trên màn hình thứ 2 (Secondary Monitor).</summary>
    SecondaryMonitor = 1,

    /// <summary>Ghim nổi tự do trên Desktop (kéo thả).</summary>
    Floating = 2
}

/// <summary>
/// Cấu hình tùy biến dành riêng cho tính năng Song ngữ & Dual Overlay.
/// Được lưu độc lập tại %LOCALAPPDATA%\AFMediaBar\custom_settings.json để không ảnh hưởng
/// đến hệ thống settings gốc của tác giả (tránh vỡ settings-contracts.json).
/// </summary>
public sealed class CustomSettings
{
    /// <summary>Bật/tắt tính năng dịch và hiển thị song ngữ.</summary>
    public bool EnableBilingualLyrics { get; set; } = true;

    /// <summary>Mã ngôn ngữ đích cần dịch sang (mặc định: "vi" - Tiếng Việt).</summary>
    public string TargetLanguage { get; set; } = "vi";

    /// <summary>Bật/tắt cửa sổ Dual Overlay độc lập.</summary>
    public bool EnableDualOverlay { get; set; } = true;

    /// <summary>Vị trí đặt cửa sổ Dual Overlay.</summary>
    public DualOverlayPlacementMode PlacementMode { get; set; } = DualOverlayPlacementMode.TaskbarOpposite;

    /// <summary>Cỡ chữ hiển thị trên Dual Overlay (DIP).</summary>
    public double FontSize { get; set; } = 12.0;

    /// <summary>Độ mờ đục của cửa sổ Dual Overlay (0.2 -> 1.0).</summary>
    public double Opacity { get; set; } = 0.95;

    /// <summary>Tọa độ X khi ở chế độ Floating.</summary>
    public double FloatingPositionX { get; set; } = 200.0;

    /// <summary>Tọa độ Y khi ở chế độ Floating.</summary>
    public double FloatingPositionY { get; set; } = 200.0;

    /// <summary>Chỉ hiển thị dòng dịch trên Dual Overlay (true) hoặc hiển thị cả 2 dòng (false).</summary>
    public bool DualOverlayOnlyShowsTranslation { get; set; } = true;
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

        // Neu chua co file hoac loi, tao file mac dinh
        Current = new CustomSettings();
        SaveSettings();
    }

    /// <summary>
    /// Ghi cấu hình xuống ổ đĩa bất đồng bộ hoặc đồng bộ an toàn.
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
