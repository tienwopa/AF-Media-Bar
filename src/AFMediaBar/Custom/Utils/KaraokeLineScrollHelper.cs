using System;
using System.Windows;
using System.Windows.Controls;

namespace AFMediaBar.Custom.Utils;

/// <summary>Updates one clipped lyric row independently from the other row.</summary>
internal static class KaraokeLineScrollHelper
{
    public static void Update(
        ScrollViewer viewport,
        TextBlock text,
        double progress,
        HorizontalAlignment alignment,
        bool refreshLayout)
    {
        // Lấy chiều rộng khả dụng thực tế của Viewport (tránh dùng viewport.Width vốn là NaN)
        double availableWidth = viewport.ActualWidth > 0 
            ? viewport.ActualWidth 
            : (double.IsFinite(viewport.Width) ? viewport.Width : double.NaN);

        var canScroll = double.IsFinite(progress);
        var maxWidth = canScroll ? double.PositiveInfinity : availableWidth;
        if (double.IsFinite(maxWidth) && (text.MaxWidth != maxWidth ||
            (double.IsFinite(text.Width) && Math.Abs(text.Width - availableWidth) > 0.5)))
        {
            text.MaxWidth = maxWidth;
            refreshLayout = true;
        }

        var trimming = canScroll ? TextTrimming.None : TextTrimming.CharacterEllipsis;
        if (text.TextTrimming != trimming)
        {
            text.TextTrimming = trimming;
            refreshLayout = true;
        }

        if (refreshLayout)
        {
            text.Width = double.NaN;
            viewport.UpdateLayout();
        }

        var overflow = canScroll && viewport.ScrollableWidth > 1;
        var targetWidth = overflow ? double.NaN : availableWidth;
        var targetAlignment = overflow ? TextAlignment.Left : alignment switch
        {
            HorizontalAlignment.Right => TextAlignment.Right,
            HorizontalAlignment.Center => TextAlignment.Center,
            _ => TextAlignment.Left
        };

        if (double.IsNaN(text.Width) != double.IsNaN(targetWidth) ||
            (double.IsFinite(targetWidth) && Math.Abs(text.Width - targetWidth) > 0.5) ||
            text.TextAlignment != targetAlignment)
        {
            text.Width = targetWidth;
            text.TextAlignment = targetAlignment;
            viewport.UpdateLayout();
        }

        if (!overflow)
        {
            viewport.ScrollToHorizontalOffset(0);
            return;
        }

        var head = Math.Clamp(progress, 0, 1) * viewport.ExtentWidth;
        var offset = Math.Clamp(head - viewport.ViewportWidth * 0.65, 0, viewport.ScrollableWidth);
        viewport.ScrollToHorizontalOffset(offset);
    }
}
