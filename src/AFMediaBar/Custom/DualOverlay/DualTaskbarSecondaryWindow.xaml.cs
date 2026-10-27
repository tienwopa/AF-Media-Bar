using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using AFMediaBar.Classes.Interop;
using static AFMediaBar.Classes.Interop.NativeMethods;
using AFMediaBar.Classes.Services;
using AFMediaBar.Custom.Settings;
using AFMediaBar.Custom.Utils;

namespace AFMediaBar.Custom.DualOverlay;

/// <summary>
/// Cửa sổ hiển thị dành riêng cho Màn hình thứ 2.
/// Tự động tìm màn hình phụ và dock vào Shell_SecondaryTrayWnd hoặc neo vào đáy màn hình 2.
/// Hỗ trợ căn lề Trái, Giữa, Phải và tự điều chỉnh màu sắc theo Taskbar.
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
            ApplyAppearance();
            EnsurePositioned();
        };

        _settingsManager.SettingsChanged += _ => Dispatcher.Invoke(() =>
        {
            ApplyAppearance();
            EnsurePositioned();
        });

        TaskbarThemeHelper.TaskbarThemeChanged += () => Dispatcher.Invoke(() => ApplyAppearance());
    }

    public void ApplyAppearance(bool isPrimaryPinyin = false)
    {
        try
        {
            var settings = _settingsManager.Current;
            ResolveTextBrushes(isPrimaryPinyin, out var pb, out var sb);
            PrimaryLineText.Foreground = pb;
            SecondaryLineText.Foreground = sb;

            double baseSize = settings.FontSize > 0 ? settings.FontSize : 11.5;
            PrimaryLineText.FontSize = baseSize;
            SecondaryLineText.FontSize = Math.Max(9.0, baseSize - 1.5);

            var textAlignment = settings.SecondaryAlignment switch
            {
                SecondaryTaskbarAlignment.Left => TextAlignment.Left,
                SecondaryTaskbarAlignment.Right => TextAlignment.Right,
                _ => TextAlignment.Center
            };
            var horizAlignment = settings.SecondaryAlignment switch
            {
                SecondaryTaskbarAlignment.Left => HorizontalAlignment.Left,
                SecondaryTaskbarAlignment.Right => HorizontalAlignment.Right,
                _ => HorizontalAlignment.Center
            };

            PrimaryLineText.TextAlignment = textAlignment;
            SecondaryLineText.TextAlignment = textAlignment;
            ContentStack.HorizontalAlignment = horizAlignment;
        }
        catch { }
    }

    private void ResolveTextBrushes(bool isPrimaryPinyin, out Brush primaryBrush, out Brush secondaryBrush)
    {
        var settings = _settingsManager.Current;
        var bc = new BrushConverter();

        string transColor = settings.TranslationColor;
        string pinyinColor = settings.RomanizationColor;

        if (settings.AutoAdaptTaskbarColor)
        {
            bool isLight = TaskbarThemeHelper.IsTaskbarLight();
            if (isLight)
            {
                transColor = "#18181B";
                pinyinColor = "#374151";

                if (PrimaryShadow != null)
                {
                    PrimaryShadow.Color = Colors.White;
                    PrimaryShadow.Opacity = 0.4;
                }
                if (SecondaryShadow != null)
                {
                    SecondaryShadow.Color = Colors.White;
                    SecondaryShadow.Opacity = 0.3;
                }
            }
            else
            {
                if (transColor.Equals("#000000", StringComparison.OrdinalIgnoreCase) ||
                    transColor.Equals("#18181B", StringComparison.OrdinalIgnoreCase))
                {
                    transColor = "#FFFFFF";
                }
                if (pinyinColor.Equals("#000000", StringComparison.OrdinalIgnoreCase) ||
                    pinyinColor.Equals("#18181B", StringComparison.OrdinalIgnoreCase))
                {
                    pinyinColor = "#FFD54F";
                }

                if (PrimaryShadow != null)
                {
                    PrimaryShadow.Color = Colors.Black;
                    PrimaryShadow.Opacity = 0.6;
                }
                if (SecondaryShadow != null)
                {
                    SecondaryShadow.Color = Colors.Black;
                    SecondaryShadow.Opacity = 0.5;
                }
            }
        }
        else
        {
            bool isTransBlack = transColor.Equals("#000000", StringComparison.OrdinalIgnoreCase) ||
                                transColor.Equals("#18181B", StringComparison.OrdinalIgnoreCase);
            if (isTransBlack)
            {
                if (PrimaryShadow != null)
                {
                    PrimaryShadow.Color = Colors.White;
                    PrimaryShadow.Opacity = 0.3;
                }
            }
            else
            {
                if (PrimaryShadow != null)
                {
                    PrimaryShadow.Color = Colors.Black;
                    PrimaryShadow.Opacity = 0.6;
                }
            }
        }

        string pColor = isPrimaryPinyin ? pinyinColor : transColor;
        string sColor = isPrimaryPinyin ? transColor : pinyinColor;

        primaryBrush = bc.ConvertFromString(pColor) as Brush ?? Brushes.White;
        secondaryBrush = bc.ConvertFromString(sColor) as Brush ?? Brushes.LightGray;
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

            ApplyAppearance(isPrimaryPinyin);
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

            var settings = _settingsManager.Current;
            int targetWidth = 500;

            // 1. Thử dock vào Shell_SecondaryTrayWnd nếu có
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
                    int targetHeight = Math.Max(36, taskbarHeight);

                    int targetX = settings.SecondaryAlignment switch
                    {
                        SecondaryTaskbarAlignment.Left => 16,
                        SecondaryTaskbarAlignment.Right => Math.Max(10, taskbarWidth - targetWidth - 24),
                        _ => Math.Max(10, (taskbarWidth - targetWidth) / 2)
                    };
                    int targetY = (taskbarHeight - targetHeight) / 2;

                    NativeMethods.SetWindowPos(
                        hwnd, 0, targetX, targetY, targetWidth, targetHeight,
                        0x0040 | 0x0010);
                    return;
                }
            }

            // 2. Nếu Windows không bật taskbar trên màn hình 2 -> Tự căn tọa độ vào đáy màn hình 2
            var secondaryMonitorRect = GetSecondaryMonitorRect();
            if (secondaryMonitorRect.HasValue)
            {
                var rect = secondaryMonitorRect.Value;
                int monWidth = rect.Right - rect.Left;
                int targetHeight = 44;

                int targetX = settings.SecondaryAlignment switch
                {
                    SecondaryTaskbarAlignment.Left => rect.Left + 24,
                    SecondaryTaskbarAlignment.Right => rect.Right - targetWidth - 24,
                    _ => rect.Left + (monWidth - targetWidth) / 2
                };
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
                // Nếu không phải Primary monitor (Primary có cờ 1)
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
