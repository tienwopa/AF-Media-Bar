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
    /// Tạo Brush quét Karaoke sắc nét từ trái sang phải theo tiến độ bài hát.
    /// </summary>
    /// <param name="activeColor">Màu sáng nổi bật khi đang hát.</param>
    /// <param name="progress">Tiến độ chạy chữ từ 0.0 đến 1.0.</param>
    /// <param name="isLightTaskbar">Thanh taskbar đang là Light theme hay Dark theme.</param>
    public static Brush CreateKaraokeBrush(Color activeColor, double progress, bool isLightTaskbar)
    {
        // Phần chưa hát (inactive) sẽ có độ mờ tự nhiên để tương phản
        byte inactiveAlpha = isLightTaskbar ? (byte)95 : (byte)100;
        var inactiveColor = Color.FromArgb(inactiveAlpha, activeColor.R, activeColor.G, activeColor.B);

        if (progress <= 0.005)
        {
            var brushInactive = new SolidColorBrush(inactiveColor);
            brushInactive.Freeze();
            return brushInactive;
        }

        if (progress >= 0.995)
        {
            var brushActive = new SolidColorBrush(activeColor);
            brushActive.Freeze();
            return brushActive;
        }

        double p = Math.Clamp(progress, 0.0, 1.0);

        var lgb = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 0),
            MappingMode = BrushMappingMode.RelativeToBoundingBox
        };

        // Điểm chuyển tiếp sắc nét mô phỏng chuẩn xác hiệu ứng Karaoke phòng thu
        lgb.GradientStops.Add(new GradientStop(activeColor, p));
        lgb.GradientStops.Add(new GradientStop(inactiveColor, p));

        lgb.Freeze();
        return lgb;
    }
}
