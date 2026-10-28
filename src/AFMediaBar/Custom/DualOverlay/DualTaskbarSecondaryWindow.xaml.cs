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
/// Hỗ trợ căn lề Trái, Giữa, Phải, chạy chữ Karaoke và tự co dãn kích thước chống tràn chữ.
/// </summary>
public partial class DualTaskbarSecondaryWindow : Window
{
    private readonly ITaskbarDockService _dockService;
    private readonly CustomSettingsManager _settingsManager;
    private bool _isDocked;
    private IntPtr _lastSecondaryTaskbarHwnd = IntPtr.Zero;

    private string _currentPrimaryText = string.Empty;
    private string? _currentSecondaryText;
    private bool _currentIsPrimaryPinyin;
    private double _currentPrimaryProgress = 1.0;
    private double _currentSecondaryProgress = 1.0;

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

    public void ApplyAppearance()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_currentPrimaryText))
            {
                return;
            }

            UpdateContent(_currentPrimaryText, _currentSecondaryText, _currentIsPrimaryPinyin, _currentPrimaryProgress, _currentSecondaryProgress);
        }
        catch { }
    }

    private void ResolveColors(bool isPrimaryPinyin, out Color primaryColor, out Color secondaryColor, out bool isLight)
    {
        var settings = _settingsManager.Current;
        isLight = false;

        string transHex = settings.TranslationColor;
        string pinyinHex = settings.RomanizationColor;

        if (settings.AutoAdaptTaskbarColor)
        {
            isLight = TaskbarThemeHelper.IsTaskbarLight();
            if (isLight)
            {
                transHex = "#18181B";
                pinyinHex = "#374151";

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
                if (transHex.Equals("#000000", StringComparison.OrdinalIgnoreCase) ||
                    transHex.Equals("#18181B", StringComparison.OrdinalIgnoreCase))
                {
                    transHex = "#FFFFFF";
                }
                if (pinyinHex.Equals("#000000", StringComparison.OrdinalIgnoreCase) ||
                    pinyinHex.Equals("#18181B", StringComparison.OrdinalIgnoreCase))
                {
                    pinyinHex = "#FFD54F";
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
            bool isTransBlack = transHex.Equals("#000000", StringComparison.OrdinalIgnoreCase) ||
                                transHex.Equals("#18181B", StringComparison.OrdinalIgnoreCase);
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

        string pColorHex = isPrimaryPinyin ? pinyinHex : transHex;
        string sColorHex = isPrimaryPinyin ? transHex : pinyinHex;

        primaryColor = ParseColor(pColorHex, Colors.White);
        secondaryColor = ParseColor(sColorHex, Color.FromRgb(255, 213, 79));
    }

    private static Color ParseColor(string hex, Color fallback)
    {
        try
        {
            if (ColorConverter.ConvertFromString(hex) is Color c) return c;
        }
        catch { }
        return fallback;
    }

    public void UpdateContent(
        string primaryText,
        string? secondaryText = null,
        bool isPrimaryPinyin = false,
        double primaryProgress = 1.0,
        double secondaryProgress = 1.0)
    {
        Dispatcher.Invoke(() =>
        {
            if (string.IsNullOrWhiteSpace(primaryText))
            {
                Visibility = Visibility.Collapsed;
                return;
            }

            _currentPrimaryText = primaryText;
            _currentSecondaryText = secondaryText;
            _currentIsPrimaryPinyin = isPrimaryPinyin;
            _currentPrimaryProgress = primaryProgress;
            _currentSecondaryProgress = secondaryProgress;

            var settings = _settingsManager.Current;
            ResolveColors(isPrimaryPinyin, out var pCol, out var sCol, out bool isLight);

            // 1. Áp dụng Brush màu (Chạy chữ Karaoke hoặc màu tĩnh)
            if (settings.EnableKaraokeHighlight)
            {
                PrimaryLineText.Foreground = KaraokeBrushHelper.CreateKaraokeBrush(pCol, primaryProgress, isLight);
                SecondaryLineText.Foreground = KaraokeBrushHelper.CreateKaraokeBrush(sCol, secondaryProgress, isLight);
            }
            else
            {
                PrimaryLineText.Foreground = new SolidColorBrush(pCol);
                SecondaryLineText.Foreground = new SolidColorBrush(sCol);
            }

            // 2. Kích thước chữ và căn lề
            double baseSize = settings.FontSize > 0 ? settings.FontSize : 11.5;
            bool hasTwoLines = !string.IsNullOrWhiteSpace(secondaryText);

            if (hasTwoLines)
            {
                PrimaryLineText.FontSize = baseSize;
                SecondaryLineText.FontSize = Math.Max(9.0, baseSize - 1.5);
                SecondaryLineText.Text = secondaryText!;
                SecondaryLineText.Visibility = Visibility.Visible;
            }
            else
            {
                PrimaryLineText.FontSize = baseSize;
                SecondaryLineText.Visibility = Visibility.Collapsed;
            }

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

            PrimaryLineText.Text = primaryText;
            Visibility = Visibility.Visible;
            EnsurePositioned();
        });
    }

    public void HideContent()
    {
        Dispatcher.Invoke(() =>
        {
            _currentPrimaryText = string.Empty;
            _currentSecondaryText = null;
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
            int targetWidth = 750;
            double baseSize = settings.FontSize > 0 ? settings.FontSize : 11.5;

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
                    int targetHeight = Math.Max(38, taskbarHeight);

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

            // 2. Nếu Windows không bật taskbar trên màn hình 2 -> Neo nổi ở cạnh dưới màn hình 2
            var secondaryMonitorRect = GetSecondaryMonitorRect();
            if (secondaryMonitorRect.HasValue)
            {
                var rect = secondaryMonitorRect.Value;
                int monWidth = rect.Right - rect.Left;
                int targetHeight = Math.Max(56, (int)(baseSize * 3.6));

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
