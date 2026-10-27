using System;
using Microsoft.Win32;

namespace AFMediaBar.Custom.Utils;

/// <summary>
/// Tiện ích nhận diện giao diện và màu sắc nền thanh Taskbar của Windows.
/// Tự động phát hiện Light mode hay Dark mode để điều chỉnh độ tương phản màu chữ.
/// </summary>
public static class TaskbarThemeHelper
{
    private const string PersonalizeKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string DwmKeyPath = @"Software\Microsoft\Windows\DWM";

    public static event Action? TaskbarThemeChanged;

    static TaskbarThemeHelper()
    {
        try
        {
            SystemEvents.UserPreferenceChanged += (s, e) =>
            {
                if (e.Category == UserPreferenceCategory.General || e.Category == UserPreferenceCategory.Color)
                {
                    TaskbarThemeChanged?.Invoke();
                }
            };
        }
        catch
        {
            // Tránh văng ngoại lệ trong môi trường không hỗ trợ SystemEvents
        }
    }

    /// <summary>
    /// Kiểm tra thanh Taskbar hiện tại của Windows đang có màu nền Sáng (Light) hay Tối (Dark).
    /// </summary>
    /// <returns>True nếu thanh Taskbar là màu Sáng (Cần dùng chữ màu Đen/Tối); False nếu Taskbar màu Tối (Cần dùng chữ Sáng/Trắng).</returns>
    public static bool IsTaskbarLight()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKeyPath);
            if (key != null)
            {
                // SystemUsesLightTheme: 1 = Light Taskbar, 0 = Dark Taskbar (Windows 10/11)
                var sysThemeVal = key.GetValue("SystemUsesLightTheme");
                var colorPrevalenceVal = key.GetValue("ColorPrevalence");

                bool systemIsLight = (sysThemeVal is int sysInt && sysInt == 1);
                bool useAccentOnTaskbar = (colorPrevalenceVal is int cpInt && cpInt == 1);

                // Nếu người dùng bật "Show accent color on Start and taskbar"
                if (useAccentOnTaskbar)
                {
                    return IsAccentColorLight();
                }

                return systemIsLight;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TaskbarThemeHelper] Loi doc registry: {ex.Message}");
        }

        // Mặc định an toàn cho Windows 10/11 là Dark taskbar
        return false;
    }

    /// <summary>
    /// Kiểm tra xem màu Accent của Windows có phải là màu sáng hay không.
    /// </summary>
    private static bool IsAccentColorLight()
    {
        try
        {
            using var dwmKey = Registry.CurrentUser.OpenSubKey(DwmKeyPath);
            if (dwmKey != null)
            {
                var colorVal = dwmKey.GetValue("ColorizationColor");
                if (colorVal is int colorInt)
                {
                    byte r = (byte)((colorInt >> 16) & 0xFF);
                    byte g = (byte)((colorInt >> 8) & 0xFF);
                    byte b = (byte)(colorInt & 0xFF);

                    // Công thức tính độ sáng cảm nhận (Luminance) theo chuẩn ITU-R BT.601
                    double luminance = (0.299 * r + 0.587 * g + 0.114 * b);
                    return luminance > 128.0;
                }
            }
        }
        catch { }

        return false;
    }
}
