using System.Windows;
using System.Windows.Interop;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Interop;

namespace AFMediaBar.Custom.DualOverlay;

/// <summary>
/// Cửa sổ hiển thị Pinyin / Lời dịch nhúng chìm vào thanh Taskbar của Màn hình thứ 2 (Shell_SecondaryTrayWnd).
/// </summary>
public partial class DualTaskbarSecondaryWindow : Window
{
    private readonly ITaskbarDockService _dockService;
    private bool _isDocked;
    private IntPtr _lastSecondaryTaskbarHwnd = IntPtr.Zero;

    public DualTaskbarSecondaryWindow(ITaskbarDockService dockService)
    {
        InitializeComponent();
        _dockService = dockService;

        Loaded += (_, _) => EnsureDockedAndPositioned();
    }

    public void UpdateContent(string text)
    {
        Dispatcher.Invoke(() =>
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                Visibility = Visibility.Collapsed;
                return;
            }

            ContentLineText.Text = text;
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
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            var secondaryTaskbarHwnd = NativeMethods.FindWindow("Shell_SecondaryTrayWnd", null);
            if (secondaryTaskbarHwnd == IntPtr.Zero)
            {
                // Khong co man hinh thu 2
                Visibility = Visibility.Collapsed;
                return;
            }

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

                int targetWidth = 450;
                int targetHeight = Math.Max(36, taskbarHeight);
                int targetX = Math.Max(10, (taskbarWidth - targetWidth) / 2); // Can giua thanh taskbar man hinh 2
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
            System.Diagnostics.Debug.WriteLine($"[DualTaskbarSecondaryWindow] Loi dock secondary taskbar: {ex.Message}");
        }
    }
}
