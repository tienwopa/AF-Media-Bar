using System.Windows;
using System.Windows.Interop;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Interop;
using AFMediaBar.Custom.Settings;

namespace AFMediaBar.Custom.DualOverlay;

/// <summary>
/// Cửa sổ hiển thị lời dịch / pinyin nhúng chìm trực tiếp vào thanh Taskbar chính của Windows (Shell_TrayWnd).
/// Nằm ở góc bên phải (đối diện với thanh media chính), không bao giờ đè lên cửa sổ ứng dụng khác.
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

        Loaded += (_, _) => EnsureDockedAndPositioned();
    }

    /// <summary>
    /// Cập nhật nội dung hiển thị theo 1 hoặc 2 dòng.
    /// </summary>
    public void UpdateContent(string primaryText, string? secondaryText = null)
    {
        Dispatcher.Invoke(() =>
        {
            if (string.IsNullOrWhiteSpace(primaryText))
            {
                Visibility = Visibility.Collapsed;
                return;
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

    /// <summary>
    /// Nhúng trực tiếp vào Taskbar và căn chỉnh tọa độ bên phải trước khay đồng hồ.
    /// </summary>
    public void EnsureDockedAndPositioned()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            var taskbarHwnd = NativeMethods.FindWindow("Shell_TrayWnd", null);
            if (taskbarHwnd == IntPtr.Zero)
            {
                return;
            }

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

                // Tim khay he thong de tinh toan khoang cach
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

                // Di chuyen va dat vi tri chinh xac ben trong Taskbar
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
