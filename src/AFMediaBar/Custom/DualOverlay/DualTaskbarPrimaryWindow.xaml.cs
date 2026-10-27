using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using AFMediaBar.Classes.Interop;
using AFMediaBar.Classes.Services;
using AFMediaBar.Custom.Settings;

namespace AFMediaBar.Custom.DualOverlay;

/// <summary>
/// Cửa sổ hiển thị nhúng chìm vào thanh Taskbar chính của Windows (Shell_TrayWnd).
/// </summary>
public partial class DualTaskbarPrimaryWindow : Window
{
    private readonly ITaskbarDockService _dockService;
    private readonly CustomSettingsManager _settingsManager;
    private bool _isDocked;
    private IntPtr _lastTaskbarHwnd = IntPtr.Zero;

    public DualTaskbarPrimaryWindow(
        ITaskbarDockService dockService,
        CustomSettingsManager settingsManager)
    {
        InitializeComponent();
        _dockService = dockService;
        _settingsManager = settingsManager;

        Loaded += (_, _) =>
        {
            ApplyColors();
            EnsureDockedAndPositioned();
        };

        _settingsManager.SettingsChanged += _ => Dispatcher.Invoke(ApplyColors);
    }

    public void ApplyColors()
    {
        try
        {
            var settings = _settingsManager.Current;
            var bc = new BrushConverter();

            if (bc.ConvertFromString(settings.TranslationColor) is Brush tb)
            {
                PrimaryLineText.Foreground = tb;
            }
            if (bc.ConvertFromString(settings.RomanizationColor) is Brush rb)
            {
                SecondaryLineText.Foreground = rb;
            }
        }
        catch { }
    }

    /// <summary>
    /// Cập nhật nội dung hiển thị theo 1 hoặc 2 dòng.
    /// </summary>
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

            // Áp dụng đúng màu cho dòng 1 và dòng 2
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
            EnsureDockedAndPositioned();
        });
    }

    public void HideContent()
    {
        Dispatcher.Invoke(() =>
        {
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

                int targetWidth = 340;
                int targetHeight = Math.Max(36, taskbarHeight);
                int targetX = Math.Max(10, taskbarWidth - trayWidth - targetWidth - 20);
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
