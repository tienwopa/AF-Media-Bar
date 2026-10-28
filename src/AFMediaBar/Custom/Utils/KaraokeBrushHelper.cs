using System;
using System.Windows;
using System.Windows.Media;

namespace AFMediaBar.Custom.Utils;

/// <summary>
/// Tiện ích tạo hiệu ứng quét màu chạy chữ Karaoke (Karaoke Wipe Highlight)
/// dựa trên tiến độ thực tế của câu hát.
/// </summary>
public static class KaraokeBrushHelper
{
    /// <summary>
    /// Tạo Brush quét Karaoke sắc nét và mượt mà từ trái sang phải theo tiến độ bài hát.
    /// </summary>
    /// <param name="activeColor">Màu sáng nổi bật khi đang hát.</param>
    /// <param name="progress">Tiến độ chạy chữ từ 0.0 đến 1.0.</param>
    /// <param name="isLightTaskbar">Thanh taskbar đang là Light theme hay Dark theme.</param>
    public static Brush CreateKaraokeBrush(Color activeColor, double progress, bool isLightTaskbar)
    {
        progress = Math.Clamp(progress, 0.0, 1.0);

        // Màu chưa hát (inactive)
        Color inactiveColor;
        if (isLightTaskbar)
        {
            // Nền sáng: màu chữ chưa hát là xám rõ nét (dễ đọc, không bị chìm)
            inactiveColor = Color.FromArgb(140, 113, 113, 122);
        }
        else
        {
            // Nền tối: màu chữ chưa hát có độ sáng vừa phải, dễ đọc nhưng phân biệt rõ với active
            inactiveColor = Color.FromArgb(115, activeColor.R, activeColor.G, activeColor.B);
        }

        if (progress <= 0.001)
        {
            var brushInactive = new SolidColorBrush(inactiveColor);
            brushInactive.Freeze();
            return brushInactive;
        }

        if (progress >= 0.999)
        {
            var brushActive = new SolidColorBrush(activeColor);
            brushActive.Freeze();
            return brushActive;
        }

        var lgb = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 0),
            MappingMode = BrushMappingMode.RelativeToBoundingBox
        };

        // Điểm chuyển tiếp dải quét với feathering nhẹ (0.012) để mép chuyển màu mượt mà
        double feather = 0.012;
        double stopActive = Math.Max(0.0, progress - feather);
        double stopInactive = Math.Min(1.0, progress + feather);

        lgb.GradientStops.Add(new GradientStop(activeColor, stopActive));
        lgb.GradientStops.Add(new GradientStop(activeColor, progress));
        lgb.GradientStops.Add(new GradientStop(inactiveColor, stopInactive));

        lgb.Freeze();
        return lgb;
    }
}
