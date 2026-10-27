using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using AFMediaBar.Classes.Interop;
using static AFMediaBar.Classes.Interop.NativeMethods;
using AFMediaBar.Classes.Services;
using AFMediaBar.Custom.Settings;

namespace AFMediaBar.Custom.DualOverlay;

/// <summary>
/// Cửa sổ hiển thị dành riêng cho Màn hình thứ 2.
/// Tự động tìm màn hình phụ và dock vào Shell_SecondaryTrayWnd hoặc neo vào đáy màn hình 2.
/// </summary>
public partial class DualTaskbarSecondaryWindow : Window
{
    private readonly ITaskbarDockService _dockService;
    private readonly CustomSettingsManager _settingsManager;
    private bool _isDocked;
    private IntPtr _lastSecondaryTaskbarHwnd = IntPtr.Zero;

    public DualTaskbarSecondaryWindow(
        ITaskbarDockService dockService,
        CustomSettingsManager settingsManager)
    {
        InitializeComponent();
        _dockService = dockService;
        _settingsManager = settingsManager;

        Loaded += (_, _) =>
        {
            ApplyColors();
            EnsurePositioned();
        };

        _settingsManager.SettingsChanged += _ => Dispatcher.Invoke(ApplyColors);
    }

    public void ApplyColors()
    {
        try
        {
            var settings = _settingsManager.Current;
            var bc = new BrushConverter();

            if (bc.ConvertFromString(settings.TranslationColor) is Brush tb) PrimaryLineText.Foreground = tb;
            if (bc.ConvertFromString(settings.RomanizationColor) is Brush rb) SecondaryLineText.Foreground = rb;
        }
        catch { }
    }

    public void UpdateContent(string primaryText, string? secondaryText = null, bool isPrimaryPinyin = false)
    {
        Dispatcher.Invoke(() =>
        {
            if (string.IsNullOrWhiteSpace(primaryText))
            {
                Visibility = Visibility.Collapsed;
                return;
            }

            var settings = _settingsManager.Current;
            var bc = new BrushConverter();

            if (isPrimaryPinyin)
            {
                if (bc.ConvertFromString(settings.RomanizationColor) is Brush rb) PrimaryLineText.Foreground = rb;
                if (bc.ConvertFromString(settings.TranslationColor) is Brush tb) SecondaryLineText.Foreground = tb;
            }
            else
            {
                if (bc.ConvertFromString(settings.TranslationColor) is Brush tb) PrimaryLineText.Foreground = tb;
                if (bc.ConvertFromString(settings.RomanizationColor) is Brush rb) SecondaryLineText.Foreground = rb;
            }

            PrimaryLineText.Text = primaryText;

            if (!string.IsNullOrWhiteSpace(secondaryText))
            {
                SecondaryLineText.Text = secondaryText;
                SecondaryLineText.Visibility = Visibility.Visible;
            }
            else
            {
                SecondaryLineText.Visibility = Visibility.Collapsed;
            }

            Visibility = Visibility.Visible;
            EnsurePositioned();
        });
    }

    public void HideContent()
    {
        Dispatcher.Invoke(() =>
        {
            Visibility = Visibility.Collapsed;
        });
    }

    public void EnsurePositioned()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            // 1. Thu dock vao Shell_SecondaryTrayWnd neu co
            var secondaryTaskbarHwnd = NativeMethods.FindWindow("Shell_SecondaryTrayWnd", null);
            if (secondaryTaskbarHwnd != IntPtr.Zero)
            {
                if (!_isDocked || _lastSecondaryTaskbarHwnd != secondaryTaskbarHwnd)
                {
                    _dockService.DockWindow(hwnd, secondaryTaskbarHwnd);
                    _isDocked = true;
                    _lastSecondaryTaskbarHwnd = secondaryTaskbarHwnd;
                }

                if (NativeMethods.GetWindowRect(secondaryTaskbarHwnd, out var tbRect))
                {
                    int taskbarWidth = tbRect.Right - tbRect.Left;
                    int taskbarHeight = tbRect.Bottom - tbRect.Top;

                    int targetWidth = 500;
                    int targetHeight = Math.Max(36, taskbarHeight);
                    int targetX = Math.Max(10, (taskbarWidth - targetWidth) / 2);
                    int targetY = (taskbarHeight - targetHeight) / 2;

                    NativeMethods.SetWindowPos(
                        hwnd, 0, targetX, targetY, targetWidth, targetHeight,
                        0x0040 | 0x0010);
                    return;
                }
            }

            // 2. Neu Windows khong bat taskbar tren man hinh 2 -> Tu can toa do vao day man hinh 2
            var secondaryMonitorRect = GetSecondaryMonitorRect();
            if (secondaryMonitorRect.HasValue)
            {
                var rect = secondaryMonitorRect.Value;
                int targetWidth = 500;
                int targetHeight = 44;
                int targetX = rect.Left + (rect.Right - rect.Left - targetWidth) / 2;
                int targetY = rect.Bottom - targetHeight - 4;

                // HWND_TOPMOST = -1
                NativeMethods.SetWindowPos(
                    hwnd, -1, targetX, targetY, targetWidth, targetHeight,
                    0x0040 | 0x0010);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DualTaskbarSecondaryWindow] Loi position: {ex.Message}");
        }
    }

    private static RECT? GetSecondaryMonitorRect()
    {
        var monitors = new List<RECT>();
        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMon, IntPtr hdc, ref RECT r, IntPtr data) =>
        {
            var mi = new MONITORINFOEX();
            mi.cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFOEX>();
            if (NativeMethods.GetMonitorInfo(hMon, ref mi))
            {
                // Neu khong phai Primary monitor (co 1 la primary)
                if ((mi.dwFlags & 1) == 0)
                {
                    monitors.Add(mi.rcMonitor);
                }
            }
            return true;
        }, IntPtr.Zero);

        return monitors.Count > 0 ? monitors[0] : null;
    }
}
