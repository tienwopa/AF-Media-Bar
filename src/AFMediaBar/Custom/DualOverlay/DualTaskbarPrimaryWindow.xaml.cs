using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using AFMediaBar.Classes.Interop;
using AFMediaBar.Classes.Services;
using AFMediaBar.Custom.Settings;
using AFMediaBar.Custom.Utils;

namespace AFMediaBar.Custom.DualOverlay;

/// <summary>
/// Cửa sổ hiển thị nhúng chìm vào thanh Taskbar chính của Windows (Shell_TrayWnd).
/// Hỗ trợ hiển thị 1 dòng hoặc 2 dòng gộp, chạy chữ Karaoke mượt mà, và tự điều chỉnh tỷ lệ phông chữ chống tràn.
/// </summary>
public partial class DualTaskbarPrimaryWindow : Window
{
    private readonly ITaskbarDockService _dockService;
    private readonly CustomSettingsManager _settingsManager;
    private bool _isDocked;
    private IntPtr _lastTaskbarHwnd = IntPtr.Zero;

    // Lưu trữ trạng thái hiển thị gần nhất để cập nhật tức thì khi đổi cài đặt
    private string _currentPrimaryText = string.Empty;
    private string? _currentSecondaryText;
    private bool _currentIsPrimaryPinyin;
    private double _currentPrimaryProgress = 1.0;
    private double _currentSecondaryProgress = 1.0;

    public DualTaskbarPrimaryWindow(
        ITaskbarDockService dockService,
        CustomSettingsManager settingsManager)
    {
        InitializeComponent();
        _dockService = dockService;
        _settingsManager = settingsManager;

        Loaded += (_, _) =>
        {
            ApplyAppearance();
            EnsureDockedAndPositioned();
        };

        _settingsManager.SettingsChanged += _ => Dispatcher.Invoke(() =>
        {
            ApplyAppearance();
            EnsureDockedAndPositioned();
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

    /// <summary>
    /// Cập nhật nội dung hiển thị theo 1 hoặc 2 dòng kèm hiệu ứng Karaoke tiến độ độc lập cho từng dòng.
    /// </summary>
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

            // 2. Tự điều chỉnh kích thước cỡ chữ thông minh để Pinyin không bao giờ bị cắt cụt
            double baseSize = settings.FontSize > 0 ? settings.FontSize : 11.5;
            bool hasTwoLines = !string.IsNullOrWhiteSpace(secondaryText);

            if (hasTwoLines)
            {
                // Khi hiển thị 2 dòng gộp, co dãn tỷ lệ để luôn vừa vặn 100% trong khung Taskbar
                double line1Size;
                double line2Size;

                if (baseSize <= 12.0)
                {
                    line1Size = baseSize;
                    line2Size = Math.Max(8.5, baseSize - 2.0);
                }
                else
                {
                    line1Size = Math.Min(baseSize, 12.5);
                    line2Size = Math.Max(8.5, Math.Min(baseSize - 2.5, 10.5));
                }

                PrimaryLineText.FontSize = line1Size;
                SecondaryLineText.FontSize = line2Size;
                PrimaryLineText.LineHeight = line1Size * 1.15;
                SecondaryLineText.LineHeight = line2Size * 1.15;

                SecondaryLineText.Text = secondaryText!;
                SecondaryLineText.Visibility = Visibility.Visible;
            }
            else
            {
                // Khi hiển thị 1 dòng duy nhất, áp dụng đầy đủ kích thước người dùng đã chọn
                PrimaryLineText.FontSize = baseSize;
                PrimaryLineText.LineHeight = double.NaN;
                SecondaryLineText.Visibility = Visibility.Collapsed;
            }

            PrimaryLineText.Text = primaryText;
            Visibility = Visibility.Visible;
            EnsureDockedAndPositioned();
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

    public void EnsureDockedAndPositioned()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            var taskbarHwnd = NativeMethods.FindWindow("Shell_TrayWnd", null);
            if (taskbarHwnd == IntPtr.Zero) return;

            if (!_isDocked || _lastTaskbarHwnd != taskbarHwnd)
            {
                _dockService.DockWindow(hwnd, taskbarHwnd);
                _isDocked = true;
                _lastTaskbarHwnd = taskbarHwnd;
            }

            if (NativeMethods.GetWindowRect(taskbarHwnd, out var tbRect))
            {
                int taskbarWidth = tbRect.Right - tbRect.Left;
                int taskbarHeight = tbRect.Bottom - tbRect.Top;

                int trayWidth = 240;
                var trayHwnd = NativeMethods.FindWindowEx(taskbarHwnd, IntPtr.Zero, "TrayNotifyWnd", null);
                if (trayHwnd != IntPtr.Zero && NativeMethods.GetWindowRect(trayHwnd, out var trayRect))
                {
                    trayWidth = Math.Max(160, trayRect.Right - trayRect.Left);
                }

                int targetWidth = 480;
                int targetHeight = Math.Max(38, taskbarHeight);
                int targetX = Math.Max(10, taskbarWidth - trayWidth - targetWidth - 16);
                int targetY = (taskbarHeight - targetHeight) / 2;

                NativeMethods.SetWindowPos(
                    hwnd,
                    0,
                    targetX,
                    targetY,
                    targetWidth,
                    targetHeight,
                    0x0040 | 0x0010); // SWP_SHOWWINDOW | SWP_NOACTIVATE
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DualTaskbarPrimaryWindow] Loi dock taskbar: {ex.Message}");
        }
    }
}
