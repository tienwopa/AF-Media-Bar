using System.Windows;
using AFMediaBar.Custom.Settings;

namespace AFMediaBar.Custom.DualOverlay;

/// <summary>
/// Điều phối vị trí và kích thước của cửa sổ Dual Overlay theo cài đặt.
/// </summary>
public sealed class DualOverlayPlacementCoordinator
{
    private readonly CustomSettingsManager _settingsManager;

    public DualOverlayPlacementCoordinator(CustomSettingsManager settingsManager)
    {
        _settingsManager = settingsManager;
    }

    /// <summary>
    /// Áp dụng vị trí chuẩn lên cửa sổ Dual Overlay.
    /// </summary>
    public void ApplyPlacement(Window window)
    {
        var settings = _settingsManager.Current;

        switch (settings.PlacementMode)
        {
            case DualOverlayPlacementMode.Floating:
                window.Left = settings.FloatingPositionX;
                window.Top = settings.FloatingPositionY;
                break;

            case DualOverlayPlacementMode.SecondaryMonitor:
                PlaceOnSecondaryMonitor(window);
                break;

            case DualOverlayPlacementMode.TaskbarOpposite:
            default:
                PlaceOnTaskbarOpposite(window);
                break;
        }
    }

    private static void PlaceOnTaskbarOpposite(Window window)
    {
        // Tinh toan dua tren man hinh chinh va chieu cao taskbar
        var workArea = SystemParameters.WorkArea;
        var screenWidth = SystemParameters.PrimaryScreenWidth;
        var screenHeight = SystemParameters.PrimaryScreenHeight;

        var taskbarHeight = Math.Max(40, screenHeight - workArea.Height);

        // Canh goc phai taskbar (phia tren khay he thong) hoac phia doi dien
        var overlayWidth = 360.0;
        var overlayHeight = 44.0;

        window.Width = overlayWidth;
        window.Height = overlayHeight;

        // Dat o ben phai taskbar, cach mep phai 180px (khu vuc dong ho / system tray)
        window.Left = Math.Max(0, screenWidth - overlayWidth - 200);
        window.Top = Math.Max(0, screenHeight - taskbarHeight + ((taskbarHeight - overlayHeight) / 2));
    }

    private static void PlaceOnSecondaryMonitor(Window window)
    {
        // Neu he thong co man hinh phu
        var virtualLeft = SystemParameters.VirtualScreenLeft;
        var virtualTop = SystemParameters.VirtualScreenTop;
        var virtualWidth = SystemParameters.VirtualScreenWidth;
        var primaryWidth = SystemParameters.PrimaryScreenWidth;

        var overlayWidth = 400.0;
        var overlayHeight = 50.0;

        window.Width = overlayWidth;
        window.Height = overlayHeight;

        if (virtualWidth > primaryWidth)
        {
            // Co it nhat 2 man hinh, dat sang man hinh ben canh
            var secondaryLeft = (virtualLeft < 0) ? virtualLeft : primaryWidth;
            window.Left = secondaryLeft + 100;
            window.Top = 100;
        }
        else
        {
            // Fallback ve man hinh chinh neu khong co man hinh thu 2
            window.Left = (primaryWidth - overlayWidth) / 2;
            window.Top = 80;
        }
    }
}
