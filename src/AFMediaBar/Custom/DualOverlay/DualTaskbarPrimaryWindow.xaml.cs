using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using AFMediaBar.Classes.Interop;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Utils;
using AFMediaBar.Custom.Settings;
using AFMediaBar.Custom.Utils;

namespace AFMediaBar.Custom.DualOverlay;

/// <summary>
/// Cửa sổ hiển thị nhúng chìm vào thanh Taskbar chính của Windows (Shell_TrayWnd).
/// Hỗ trợ căn lề (Phải/Giữa/Trái), giới hạn khu vực, animation chuyển câu, và tự động căn bằng 1 dòng.
/// </summary>
public partial class DualTaskbarPrimaryWindow : Window
{
    private readonly ITaskbarDockService _dockService;
    private readonly CustomSettingsManager _settingsManager;
    private bool _isDocked;
    private IntPtr _lastTaskbarHwnd = IntPtr.Zero;

    // Lưu trữ trạng thái hiển thị gần nhất
    private string _currentPrimaryText = string.Empty;
    private string? _currentSecondaryText;
    private bool _currentIsPrimaryPinyin;
    private double _currentPrimaryProgress = 1.0;
    private double _currentSecondaryProgress = 1.0;
    private double _lastAppliedPrimaryProgress = -1;
    private double _lastAppliedSecondaryProgress = -1;
    private Color _lastPrimaryColor;
    private Color _lastSecondaryColor;

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
            EnsureDockedAndPositioned(forceReposition: true);
        };

        _settingsManager.SettingsChanged += _ =>
        {
            if (Dispatcher.HasShutdownStarted) return;
            Dispatcher.Invoke(() =>
            {
                ApplyAppearance();
                EnsureDockedAndPositioned(forceReposition: true);
            });
        };

        TaskbarThemeHelper.TaskbarThemeChanged += () =>
        {
            if (Dispatcher.HasShutdownStarted) return;
            Dispatcher.Invoke(() => ApplyAppearance());
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowHelper.SetNoActivate(this);
        WindowHelper.SetInputTransparent(this, true);

        if (PresentationSource.FromDependencyObject(this) is HwndSource source)
        {
            source.AddHook(WndProc);
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        DetachFromTaskbar();
        base.OnClosing(e);
    }

    public void DetachFromTaskbar()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero && _isDocked)
            {
                _dockService.UndockWindow(hwnd);
                _isDocked = false;
                _lastTaskbarHwnd = IntPtr.Zero;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DualTaskbarPrimaryWindow] Detach loi: {ex.Message}");
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_DISPLAYCHANGE = 0x007E;
        const int WM_SETTINGCHANGE = 0x001A;
        if (msg == WM_DISPLAYCHANGE || msg == WM_SETTINGCHANGE)
        {
            EnsureDockedAndPositioned(forceReposition: true);
        }
        return IntPtr.Zero;
    }

    public void ApplyAppearance()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_currentPrimaryText))
            {
                return;
            }

            UpdateContent(
                _currentPrimaryText,
                _currentSecondaryText,
                _currentIsPrimaryPinyin,
                _currentPrimaryProgress,
                _currentSecondaryProgress);
        }
        catch { }
    }

    private void ResolveColors(bool isPrimaryPinyin, out Color primaryColor, out Color secondaryColor, out bool isLight)
    {
        var settings = _settingsManager.Current;
        isLight = TaskbarThemeHelper.IsTaskbarLight();

        string transHex = settings.TranslationColor;
        string pinyinHex = settings.RomanizationColor;

        if (settings.AutoAdaptTaskbarColor)
        {
            if (isLight)
            {
                transHex = "#18181B";
                pinyinHex = "#374151";
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
            }
        }

        string pColorHex = isPrimaryPinyin ? pinyinHex : transHex;
        string sColorHex = isPrimaryPinyin ? transHex : pinyinHex;

        primaryColor = ParseColor(pColorHex, Colors.White);
        secondaryColor = ParseColor(sColorHex, Color.FromRgb(255, 213, 79));

        // Cập nhật hiệu ứng bóng đổ (DropShadow) đồng bộ và chính xác dựa trên màu chữ từng dòng
        UpdateTextShadow(PrimaryShadow, primaryColor);
        UpdateTextShadow(SecondaryShadow, secondaryColor);
    }

    private static void UpdateTextShadow(DropShadowEffect? shadow, Color textColor)
    {
        if (shadow == null) return;

        bool isDark = IsColorDark(textColor);
        if (isDark)
        {
            shadow.Color = Colors.White;
            shadow.Opacity = 0.25;
            shadow.BlurRadius = 2.0;
            shadow.ShadowDepth = 0.0;
        }
        else
        {
            shadow.Color = Colors.Black;
            shadow.Opacity = 0.5;
            shadow.BlurRadius = 3.0;
            shadow.ShadowDepth = 1.0;
        }
    }

    private static bool IsColorDark(Color color)
    {
        double luminance = 0.299 * color.R + 0.587 * color.G + 0.114 * color.B;
        return luminance < 128.0;
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

    private void TriggerEntranceAnimation(LyricsAnimationMode mode)
    {
        if (mode == LyricsAnimationMode.None) return;

        var duration = TimeSpan.FromMilliseconds(220);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var opacityAnim = new DoubleAnimation(0.2, 1.0, duration) { EasingFunction = ease };
        LinesStack.BeginAnimation(OpacityProperty, opacityAnim);

        switch (mode)
        {
            case LyricsAnimationMode.SlideUp:
                EntranceTranslate.Y = 6;
                var slideUpAnim = new DoubleAnimation(6, 0, duration) { EasingFunction = ease };
                EntranceTranslate.BeginAnimation(TranslateTransform.YProperty, slideUpAnim);
                break;

            case LyricsAnimationMode.SlideHorizontal:
                double startX = _settingsManager.Current.PrimaryAlignment == PrimaryTaskbarAlignment.Right ? 8 : -8;
                EntranceTranslate.X = startX;
                var slideXAnim = new DoubleAnimation(startX, 0, duration) { EasingFunction = ease };
                EntranceTranslate.BeginAnimation(TranslateTransform.XProperty, slideXAnim);
                break;

            case LyricsAnimationMode.ZoomIn:
                EntranceScale.ScaleX = 0.94;
                EntranceScale.ScaleY = 0.94;
                var scaleAnim = new DoubleAnimation(0.94, 1.0, duration) { EasingFunction = ease };
                EntranceScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
                EntranceScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
                break;
        }
    }

    /// <summary>
    /// Cập nhật nội dung hiển thị theo 1 hoặc 2 dòng kèm tiến độ chạy chữ Karaoke độc lập.
    /// Tối ưu 0% overhead: Chỉ tính toán lại Layout và Text khi câu hát đổi hoặc mới hiển thị.
    /// </summary>
    public void UpdateContent(
        string primaryText,
        string? secondaryText = null,
        bool isPrimaryPinyin = false,
        double primaryProgress = 1.0,
        double secondaryProgress = 1.0)
    {
        if (Dispatcher.HasShutdownStarted) return;

        Dispatcher.Invoke(() =>
        {
            if (string.IsNullOrWhiteSpace(primaryText))
            {
                BackgroundEffectCanvas.IsActive = false;
                Visibility = Visibility.Collapsed;
                return;
            }

            bool textChanged = _currentPrimaryText != primaryText || _currentSecondaryText != secondaryText;
            bool isFirstVisible = Visibility != Visibility.Visible;

            _currentPrimaryText = primaryText;
            _currentSecondaryText = secondaryText;
            _currentIsPrimaryPinyin = isPrimaryPinyin;
            _currentPrimaryProgress = primaryProgress;
            _currentSecondaryProgress = secondaryProgress;

            var settings = _settingsManager.Current;
            ResolveColors(isPrimaryPinyin, out var pCol, out var sCol, out bool isLight);

            if (textChanged || isFirstVisible)
            {
                // 1. Phân bổ cỡ chữ độc lập
                double pSize = isPrimaryPinyin
                    ? (settings.RomanizationFontSize > 0 ? settings.RomanizationFontSize : 11.0)
                    : (settings.TranslationFontSize > 0 ? settings.TranslationFontSize : 11.5);

                double sSize = isPrimaryPinyin
                    ? (settings.TranslationFontSize > 0 ? settings.TranslationFontSize : 11.5)
                    : (settings.RomanizationFontSize > 0 ? settings.RomanizationFontSize : 11.0);

                bool hasTwoLines = !string.IsNullOrWhiteSpace(secondaryText);

                if (hasTwoLines)
                {
                    pSize = Math.Min(pSize, 13.0);
                    sSize = Math.Min(sSize, 12.0);

                    PrimaryLineText.FontSize = pSize;
                    SecondaryLineText.FontSize = sSize;
                    PrimaryLineText.LineHeight = pSize * 1.15;
                    SecondaryLineText.LineHeight = sSize * 1.15;

                    SecondaryLineText.Text = secondaryText!;
                    SecondaryLineText.Visibility = Visibility.Visible;
                    LinesStack.VerticalAlignment = VerticalAlignment.Center;
                }
                else
                {
                    if (settings.AutoBalanceSingleLine)
                    {
                        double singleSize = Math.Max(pSize, 13.0);
                        PrimaryLineText.FontSize = singleSize;
                    }
                    else
                    {
                        PrimaryLineText.FontSize = pSize;
                    }

                    PrimaryLineText.LineHeight = double.NaN;
                    SecondaryLineText.Visibility = Visibility.Collapsed;
                    LinesStack.VerticalAlignment = VerticalAlignment.Center;
                }

                // 2. Giới hạn độ rộng tối đa (Max Width)
                double maxW = settings.PrimaryMaxWidth > 100 ? settings.PrimaryMaxWidth : 480;
                PrimaryLineText.MaxWidth = maxW;
                SecondaryLineText.MaxWidth = maxW;

                // 3. Phân bổ Căn lề Taskbar chính
                var textAlignment = settings.PrimaryAlignment switch
                {
                    PrimaryTaskbarAlignment.Left => TextAlignment.Left,
                    PrimaryTaskbarAlignment.Center => TextAlignment.Center,
                    _ => TextAlignment.Right
                };
                var horizAlignment = settings.PrimaryAlignment switch
                {
                    PrimaryTaskbarAlignment.Left => HorizontalAlignment.Left,
                    PrimaryTaskbarAlignment.Center => HorizontalAlignment.Center,
                    _ => HorizontalAlignment.Right
                };

                PrimaryLineText.TextAlignment = textAlignment;
                SecondaryLineText.TextAlignment = textAlignment;
                PrimaryLineText.HorizontalAlignment = horizAlignment;
                SecondaryLineText.HorizontalAlignment = horizAlignment;
                LinesStack.HorizontalAlignment = horizAlignment;

                // 4. Phân bổ độ đậm chữ (FontWeight)
                FontWeight pWeight = FontWeights.Normal;
                FontWeight sWeight = FontWeights.Normal;

                switch (settings.WeightEmphasis)
                {
                    case BilingualWeightEmphasis.Balanced:
                        pWeight = FontWeights.Normal;
                        sWeight = FontWeights.Normal;
                        break;
                    case BilingualWeightEmphasis.EmphasisTranslation:
                        if (isPrimaryPinyin) { pWeight = FontWeights.Normal; sWeight = FontWeights.SemiBold; }
                        else { pWeight = FontWeights.SemiBold; sWeight = FontWeights.Normal; }
                        break;
                    case BilingualWeightEmphasis.EmphasisRomanization:
                        if (isPrimaryPinyin) { pWeight = FontWeights.SemiBold; sWeight = FontWeights.Normal; }
                        else { pWeight = FontWeights.Normal; sWeight = FontWeights.SemiBold; }
                        break;
                    case BilingualWeightEmphasis.BothSemiBold:
                        pWeight = FontWeights.SemiBold;
                        sWeight = FontWeights.SemiBold;
                        break;
                }

                PrimaryLineText.FontWeight = pWeight;
                SecondaryLineText.FontWeight = sWeight;

                PrimaryLineText.Text = primaryText;
                BackgroundEffectCanvas.IsActive = true;
                Visibility = Visibility.Visible;

                EnsureDockedAndPositioned(forceReposition: isFirstVisible);

                if (textChanged)
                {
                    TriggerEntranceAnimation(settings.AnimationMode);
                }
            }

            // 5. Áp dụng Brush màu Karaoke Wipe theo nhịp
            bool enableKaraoke = settings.EnableKaraokeHighlight;
            bool pinyinWipe = enableKaraoke && (settings.KaraokeTarget == KaraokeTargetMode.Both || settings.KaraokeTarget == KaraokeTargetMode.RomanizationOnly);
            bool transWipe = enableKaraoke && (settings.KaraokeTarget == KaraokeTargetMode.Both || settings.KaraokeTarget == KaraokeTargetMode.TranslationOnly);

            bool primaryWipe = isPrimaryPinyin ? pinyinWipe : transWipe;
            bool secondaryWipe = isPrimaryPinyin ? transWipe : pinyinWipe;

            if (primaryWipe)
            {
                if (textChanged || isFirstVisible || Math.Abs(_lastAppliedPrimaryProgress - primaryProgress) >= 0.005 || _lastPrimaryColor != pCol)
                {
                    PrimaryLineText.Foreground = KaraokeBrushHelper.CreateKaraokeBrush(pCol, primaryProgress, isLight);
                    _lastAppliedPrimaryProgress = primaryProgress;
                    _lastPrimaryColor = pCol;
                }
            }
            else if (textChanged || isFirstVisible || _lastPrimaryColor != pCol)
            {
                var b = new SolidColorBrush(pCol);
                b.Freeze();
                PrimaryLineText.Foreground = b;
                _lastPrimaryColor = pCol;
                _lastAppliedPrimaryProgress = -1;
            }

            if (!string.IsNullOrWhiteSpace(secondaryText))
            {
                if (secondaryWipe)
                {
                    if (textChanged || isFirstVisible || Math.Abs(_lastAppliedSecondaryProgress - secondaryProgress) >= 0.005 || _lastSecondaryColor != sCol)
                    {
                        SecondaryLineText.Foreground = KaraokeBrushHelper.CreateKaraokeBrush(sCol, secondaryProgress, isLight);
                        _lastAppliedSecondaryProgress = secondaryProgress;
                        _lastSecondaryColor = sCol;
                    }
                }
                else if (textChanged || isFirstVisible || _lastSecondaryColor != sCol)
                {
                    var b = new SolidColorBrush(sCol);
                    b.Freeze();
                    SecondaryLineText.Foreground = b;
                    _lastSecondaryColor = sCol;
                    _lastAppliedSecondaryProgress = -1;
                }
            }
        });
    }

    public void HideContent()
    {
        if (Dispatcher.HasShutdownStarted) return;
        Dispatcher.Invoke(() =>
        {
            BackgroundEffectCanvas.IsActive = false;
            _currentPrimaryText = string.Empty;
            _currentSecondaryText = null;
            Visibility = Visibility.Collapsed;
        });
    }

    public void EnsureDockedAndPositioned(bool forceReposition = false)
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
                forceReposition = true;
            }

            if (!forceReposition) return;

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

                var settings = _settingsManager.Current;
                int targetWidth = (int)Math.Max(260, Math.Min(settings.PrimaryMaxWidth + 20, taskbarWidth - trayWidth - 60));
                int targetHeight = Math.Max(38, taskbarHeight);

                int targetX;
                switch (settings.PrimaryAlignment)
                {
                    case PrimaryTaskbarAlignment.Left:
                        targetX = 60;
                        break;
                    case PrimaryTaskbarAlignment.Center:
                        targetX = Math.Max(20, (taskbarWidth - trayWidth - targetWidth) / 2);
                        break;
                    default: // Right
                        targetX = Math.Max(10, taskbarWidth - trayWidth - targetWidth - 16);
                        break;
                }

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
