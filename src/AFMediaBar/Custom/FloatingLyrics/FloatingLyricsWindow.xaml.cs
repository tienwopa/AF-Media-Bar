using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Lyrics;
using AFMediaBar.Custom.Effects;
using AFMediaBar.Custom.Settings;
using AFMediaBar.Custom.Translation;
using AFMediaBar.Custom.Utils;

namespace AFMediaBar.Custom.FloatingLyrics;

/// <summary>
/// Cửa sổ Bảng lời bài hát nổi (Floating Lyrics Window) đồng bộ theo nhạc,
/// tự do di chuyển, thay đổi kích thước, tùy chỉnh độ trong suốt, màu nền,
/// màu chữ, tốc độ cuộn và hỗ trợ chế độ gắn liền với Media Bar.
/// </summary>
public sealed partial class FloatingLyricsWindow : Window
{
    private readonly CustomSettingsManager _settingsManager;
    public ObservableCollection<FloatingLyricsItem> Items { get; } = new();

    private LyricDocument? _currentDocument;
    private bool _isVietnameseTrack;
    private int _lastActiveIndex = -1;
    private bool _isUserScrolling;
    private readonly DispatcherTimer _userScrollCooldownTimer;
    private readonly DispatcherTimer _saveGeometryTimer;
    private Storyboard? _currentScrollStoryboard;

    // Cache các Brush màu chữ
    private Brush _activeBrush = Brushes.White;
    private Brush _activeSecondaryBrush = new SolidColorBrush(Color.FromRgb(255, 213, 79)); // Gold
    private Brush _pastBrush = new SolidColorBrush(Color.FromArgb(135, 255, 255, 255));
    private Brush _pastSecondaryBrush = new SolidColorBrush(Color.FromArgb(110, 255, 213, 79));
    private Brush _futureBrush = new SolidColorBrush(Color.FromArgb(110, 255, 255, 255));
    private Brush _futureSecondaryBrush = new SolidColorBrush(Color.FromArgb(90, 255, 213, 79));

    public event Action<double>? SeekRequested;
    public event Action<bool>? VisibilityToggled;

    public FloatingLyricsWindow(CustomSettingsManager settingsManager)
    {
        InitializeComponent();
        _settingsManager = settingsManager;

        LyricsItemsList.ItemsSource = Items;

        _userScrollCooldownTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(3500)
        };
        _userScrollCooldownTimer.Tick += (s, e) =>
        {
            _userScrollCooldownTimer.Stop();
            _isUserScrolling = false;
            ScrollToActiveLine(animate: true);
        };

        _saveGeometryTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _saveGeometryTimer.Tick += (s, e) =>
        {
            _saveGeometryTimer.Stop();
            SaveGeometrySettings();
        };

        ApplyThemeBrushes();
        ApplyBackgroundAppearance();
        WindowBackgroundEffect.IsActive = true;
        WindowBackgroundEffect.ExplicitEffect = _settingsManager.Current.FloatingLyricsBgEffect;
        ApplySettings();
        RestoreGeometrySettings();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var source = PresentationSource.FromVisual(this) as HwndSource;
        source?.AddHook(WndProc);

        // Đảm bảo cửa sổ không bao giờ che khuất taskbar khi Maximize và không tràn mép trên
        MaxHeight = SystemParameters.WorkArea.Height;
        MaxWidth = SystemParameters.WorkArea.Width;
    }

    #region Window Resizing via Win32 WM_NCHITTEST
    private const int WM_NCHITTEST = 0x0084;
    private const int HTLEFT = 10;
    private const int HTRIGHT = 11;
    private const int HTTOP = 12;
    private const int HTTOPLEFT = 13;
    private const int HTTOPRIGHT = 14;
    private const int HTBOTTOM = 15;
    private const int HTBOTTOMLEFT = 16;
    private const int HTBOTTOMRIGHT = 17;

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_NCHITTEST && WindowState == WindowState.Normal)
        {
            var pScreen = new Point((short)(lParam.ToInt64() & 0xFFFF), (short)((lParam.ToInt64() >> 16) & 0xFFFF));
            var pWindow = PointFromScreen(pScreen);
            const double border = 12.0;

            bool isLeft = pWindow.X < border;
            bool isRight = pWindow.X > ActualWidth - border;
            bool isTop = pWindow.Y < border;
            bool isBottom = pWindow.Y > ActualHeight - border;

            if (isTop && isLeft) { handled = true; return new IntPtr(HTTOPLEFT); }
            if (isTop && isRight) { handled = true; return new IntPtr(HTTOPRIGHT); }
            if (isBottom && isLeft) { handled = true; return new IntPtr(HTBOTTOMLEFT); }
            if (isBottom && isRight) { handled = true; return new IntPtr(HTBOTTOMRIGHT); }
            if (isLeft) { handled = true; return new IntPtr(HTLEFT); }
            if (isRight) { handled = true; return new IntPtr(HTRIGHT); }
            if (isTop) { handled = true; return new IntPtr(HTTOP); }
            if (isBottom) { handled = true; return new IntPtr(HTBOTTOM); }
        }
        return IntPtr.Zero;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (WindowState == WindowState.Maximized)
            {
                ToggleMaximize();
                e.Handled = true;
            }
        }
        else if (e.Key == Key.F11)
        {
            ToggleMaximize();
            e.Handled = true;
        }
    }
    #endregion

    #region Appearance and Background Customization
    public void ApplyBackgroundAppearance()
    {
        var settings = _settingsManager.Current;
        double opacity = Math.Clamp(settings.FloatingLyricsOpacity, 0.15, 1.0);

        switch (settings.FloatingLyricsBgStyle)
        {
            case FloatingLyricsBackgroundStyle.Transparent:
                // Trong suốt hoàn toàn: chỉ có chữ nổi trên màn hình
                MainSurface.Background = new SolidColorBrush(Color.FromArgb((byte)(opacity * 255 * 0.12), 0, 0, 0));
                MainSurface.BorderBrush = new SolidColorBrush(Color.FromArgb((byte)(opacity * 255 * 0.25), 255, 255, 255));
                MainSurfaceShadow.Opacity = 0.0;
                break;

            case FloatingLyricsBackgroundStyle.SolidDark:
                MainSurface.Background = new SolidColorBrush(Color.FromArgb((byte)(opacity * 255), 15, 15, 18));
                MainSurface.BorderBrush = new SolidColorBrush(Color.FromArgb((byte)(opacity * 255 * 0.3), 255, 255, 255));
                MainSurfaceShadow.Opacity = 0.55 * opacity;
                break;

            case FloatingLyricsBackgroundStyle.DarkSlate:
                MainSurface.Background = new SolidColorBrush(Color.FromArgb((byte)(opacity * 255), 30, 30, 36));
                MainSurface.BorderBrush = new SolidColorBrush(Color.FromArgb((byte)(opacity * 255 * 0.35), 255, 255, 255));
                MainSurfaceShadow.Opacity = 0.55 * opacity;
                break;

            case FloatingLyricsBackgroundStyle.AcrylicGlass:
            default:
                // Kính mờ Acrylic
                MainSurface.Background = new SolidColorBrush(Color.FromArgb((byte)(opacity * 255 * 0.88), 24, 24, 28));
                MainSurface.BorderBrush = new SolidColorBrush(Color.FromArgb((byte)(opacity * 255 * 0.35), 255, 255, 255));
                MainSurfaceShadow.Opacity = 0.55 * opacity;
                break;
        }
    }

    private void ApplyThemeBrushes()
    {
        var settings = _settingsManager.Current;

        Color activeColor = Color.FromRgb(255, 255, 255);
        try
        {
            if (!string.IsNullOrWhiteSpace(settings.FloatingLyricsActiveTextColor))
            {
                activeColor = (Color)ColorConverter.ConvertFromString(settings.FloatingLyricsActiveTextColor);
            }
        }
        catch { }

        _activeBrush = new SolidColorBrush(activeColor);
        _activeBrush.Freeze();

        _activeSecondaryBrush = new SolidColorBrush(Color.FromRgb(255, 213, 79));
        _activeSecondaryBrush.Freeze();

        // Độ tương phản cao hơn rõ rệt (Alpha 200 = 78%, Alpha 175 = 69%)
        _pastBrush = new SolidColorBrush(Color.FromArgb(200, activeColor.R, activeColor.G, activeColor.B));
        _pastBrush.Freeze();

        _futureBrush = new SolidColorBrush(Color.FromArgb(175, activeColor.R, activeColor.G, activeColor.B));
        _futureBrush.Freeze();

        _pastSecondaryBrush = new SolidColorBrush(Color.FromArgb(175, 255, 213, 79));
        _pastSecondaryBrush.Freeze();

        _futureSecondaryBrush = new SolidColorBrush(Color.FromArgb(150, 255, 213, 79));
        _futureSecondaryBrush.Freeze();
    }
    #endregion

    #region Geometry and Settings Persistence
    private void ApplySettings()
    {
        var settings = _settingsManager.Current;

        Topmost = settings.FloatingLyricsTopmost;
        PinGlyph.Text = Topmost ? "\uE718" : "\uE77A";
        PinButton.ToolTip = Topmost ? "Bỏ ghim (Hiện đang ghim trên cùng)" : "Ghim luôn hiển thị trên cùng";

        UpdateDockButtonAppearance();
    }

    private void UpdateDockButtonAppearance()
    {
        var isAttached = _settingsManager.Current.FloatingLyricsAttachedToMediaBar;
        DockGlyph.Text = isAttached ? "\uE8A7" : "\uE748";
        DockButton.ToolTip = isAttached
            ? "Tách thành bảng nổi tự do di chuyển (Hiện đang gắn liền Media Bar)"
            : "Gắn liền phía trên Media Bar (Mở rộng Media Bar)";
    }

    private void RestoreGeometrySettings()
    {
        var settings = _settingsManager.Current;

        if (settings.FloatingLyricsAttachedToMediaBar)
        {
            PositionAttachedToMediaBar();
            return;
        }

        if (!double.IsNaN(settings.FloatingLyricsWidth) && settings.FloatingLyricsWidth >= MinWidth)
        {
            Width = settings.FloatingLyricsWidth;
        }
        if (!double.IsNaN(settings.FloatingLyricsHeight) && settings.FloatingLyricsHeight >= MinHeight)
        {
            Height = settings.FloatingLyricsHeight;
        }

        if (!double.IsNaN(settings.FloatingLyricsLeft) && !double.IsNaN(settings.FloatingLyricsTop))
        {
            double virtualLeft = SystemParameters.VirtualScreenLeft;
            double virtualTop = SystemParameters.VirtualScreenTop;
            double virtualWidth = SystemParameters.VirtualScreenWidth;
            double virtualHeight = SystemParameters.VirtualScreenHeight;

            if (settings.FloatingLyricsLeft >= virtualLeft &&
                settings.FloatingLyricsLeft < virtualLeft + virtualWidth - 100 &&
                settings.FloatingLyricsTop >= virtualTop &&
                settings.FloatingLyricsTop < virtualTop + virtualHeight - 100)
            {
                Left = settings.FloatingLyricsLeft;
                Top = settings.FloatingLyricsTop;
                WindowStartupLocation = WindowStartupLocation.Manual;
                return;
            }
        }

        WindowStartupLocation = WindowStartupLocation.CenterScreen;
    }

    private void SaveGeometrySettings()
    {
        if (WindowState != WindowState.Normal) return;

        var settings = _settingsManager.Current;
        if (!settings.FloatingLyricsAttachedToMediaBar)
        {
            settings.FloatingLyricsLeft = Left;
            settings.FloatingLyricsTop = Top;
            settings.FloatingLyricsWidth = Width;
            settings.FloatingLyricsHeight = Height;
            _settingsManager.SaveSettings();
        }
    }

    protected override void OnLocationChanged(EventArgs e)
    {
        base.OnLocationChanged(e);
        if (IsLoaded && !_settingsManager.Current.FloatingLyricsAttachedToMediaBar)
        {
            _saveGeometryTimer.Stop();
            _saveGeometryTimer.Start();
        }
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        if (IsLoaded && !_settingsManager.Current.FloatingLyricsAttachedToMediaBar)
        {
            _saveGeometryTimer.Stop();
            _saveGeometryTimer.Start();
        }
    }
    #endregion

    #region Attached Mode (Gắn liền Media Bar)
    private Rect? GetTaskBarMediaControlBounds()
    {
        try
        {
            foreach (Window win in Application.Current.Windows)
            {
                if (win.IsVisible && (win.GetType().Name.Contains("TaskbarWindow", StringComparison.OrdinalIgnoreCase) || win.GetType().Name.Contains("FloatingBarWindow", StringComparison.OrdinalIgnoreCase)))
                {
                    var mediaControl = FindVisualChild<AFMediaBar.Components.TaskBarMediaControl>(win);
                    if (mediaControl != null && mediaControl.IsVisible && mediaControl.ActualWidth > 0 && mediaControl.ActualHeight > 0)
                    {
                        var screenPos = mediaControl.PointToScreen(new Point(0, 0));
                        var source = PresentationSource.FromVisual(this);
                        if (source?.CompositionTarget != null)
                        {
                            var transform = source.CompositionTarget.TransformFromDevice;
                            var dipPos = transform.Transform(screenPos);
                            return new Rect(dipPos.X, dipPos.Y, mediaControl.ActualWidth, mediaControl.ActualHeight);
                        }
                        return new Rect(screenPos.X, screenPos.Y, mediaControl.ActualWidth, mediaControl.ActualHeight);
                    }
                    else if (win.ActualWidth > 0 && win.ActualHeight > 0)
                    {
                        var screenPos = win.PointToScreen(new Point(0, 0));
                        var source = PresentationSource.FromVisual(this);
                        if (source?.CompositionTarget != null)
                        {
                            var transform = source.CompositionTarget.TransformFromDevice;
                            var dipPos = transform.Transform(screenPos);
                            return new Rect(dipPos.X, dipPos.Y, win.ActualWidth, win.ActualHeight);
                        }
                        return new Rect(screenPos.X, screenPos.Y, win.ActualWidth, win.ActualHeight);
                    }
                }
            }
        }
        catch { }

        return null;
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent == null) return null;
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed) return typed;
            var desc = FindVisualChild<T>(child);
            if (desc != null) return desc;
        }
        return null;
    }

    public void PositionAttachedToMediaBar()
    {
        double workWidth = SystemParameters.WorkArea.Width;
        double workHeight = SystemParameters.WorkArea.Height;
        double workLeft = SystemParameters.WorkArea.Left;
        double workTop = SystemParameters.WorkArea.Top;

        Width = Math.Clamp(workWidth * 0.45, 460, 680);
        Height = Math.Clamp(workHeight * 0.65, 480, 720);

        var barBounds = GetTaskBarMediaControlBounds();
        if (barBounds.HasValue)
        {
            var bounds = barBounds.Value;
            // Căn giữa bảng nổi theo vị trí ngang của Media Bar thực tế trên Taskbar
            double targetLeft = bounds.Left + ((bounds.Width - Width) / 2.0);
            targetLeft = Math.Clamp(targetLeft, workLeft + 8, workLeft + workWidth - Width - 8);

            // Đặt ngay sát phía trên Media Bar (cách 6px)
            double targetTop = bounds.Top - Height - 6;
            if (targetTop < workTop + 8)
            {
                targetTop = workTop + 8;
            }

            Left = targetLeft;
            Top = targetTop;
        }
        else
        {
            Left = workLeft + ((workWidth - Width) / 2.0);
            Top = workTop + workHeight - Height - 8;
        }

        WindowStartupLocation = WindowStartupLocation.Manual;
    }

    private void DockButton_Click(object sender, RoutedEventArgs e)
    {
        var settings = _settingsManager.Current;
        settings.FloatingLyricsAttachedToMediaBar = !settings.FloatingLyricsAttachedToMediaBar;
        _settingsManager.SaveSettings();

        UpdateDockButtonAppearance();

        if (settings.FloatingLyricsAttachedToMediaBar)
        {
            if (WindowState == WindowState.Maximized)
            {
                ToggleMaximize();
            }
            PositionAttachedToMediaBar();
        }
        else
        {
            RestoreGeometrySettings();
        }
    }
    #endregion

    #region Header Interactions & Maximize
    private void HeaderBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Nhấp đúp vào Header để chuyển đổi Toàn màn hình / Khôi phục
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        if (e.ButtonState == MouseButtonState.Pressed && WindowState == WindowState.Normal)
        {
            // Nếu đang ở chế độ Attached mà người dùng kéo đi, tự động chuyển về chế độ bảng nổi tự do
            if (_settingsManager.Current.FloatingLyricsAttachedToMediaBar)
            {
                _settingsManager.Current.FloatingLyricsAttachedToMediaBar = false;
                _settingsManager.SaveSettings();
                UpdateDockButtonAppearance();
            }

            try
            {
                DragMove();
            }
            catch { }
        }
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleMaximize();
    }

    private void ToggleMaximize()
    {
        if (WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Normal;
            RootContainer.Margin = new Thickness(10);
            MainSurface.CornerRadius = new CornerRadius(16);
            InnerClippingBorder.CornerRadius = new CornerRadius(15);
            ResizeGripIcon.Visibility = Visibility.Visible;
            MaximizeGlyph.Text = "\uE922";
            MaximizeButton.ToolTip = "Phóng to toàn màn hình (F11 / Nhấp đúp tiêu đề)";
        }
        else
        {
            if (_settingsManager.Current.FloatingLyricsAttachedToMediaBar)
            {
                _settingsManager.Current.FloatingLyricsAttachedToMediaBar = false;
                _settingsManager.SaveSettings();
                UpdateDockButtonAppearance();
            }

            WindowState = WindowState.Maximized;
            RootContainer.Margin = new Thickness(0);
            MainSurface.CornerRadius = new CornerRadius(0);
            InnerClippingBorder.CornerRadius = new CornerRadius(0);
            ResizeGripIcon.Visibility = Visibility.Collapsed;
            MaximizeGlyph.Text = "\uE923";
            MaximizeButton.ToolTip = "Khôi phục cửa sổ nổi (ESC / F11 / Nhấp đúp tiêu đề)";
        }

        ScrollToActiveLine(animate: false);
    }

    public void ToggleVisibility()
    {
        if (IsVisible)
        {
            Hide();
            _settingsManager.Current.EnableFloatingLyrics = false;
            _settingsManager.SaveSettings();
            VisibilityToggled?.Invoke(false);
        }
        else
        {
            Show();
            Activate();
            _settingsManager.Current.EnableFloatingLyrics = true;
            _settingsManager.SaveSettings();
            VisibilityToggled?.Invoke(true);

            if (_settingsManager.Current.FloatingLyricsAttachedToMediaBar)
            {
                PositionAttachedToMediaBar();
            }

            ScrollToActiveLine(animate: false);
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        _settingsManager.Current.EnableFloatingLyrics = false;
        _settingsManager.SaveSettings();
        VisibilityToggled?.Invoke(false);
    }

    private void PinButton_Click(object sender, RoutedEventArgs e)
    {
        Topmost = !Topmost;
        _settingsManager.Current.FloatingLyricsTopmost = Topmost;
        _settingsManager.SaveSettings();

        PinGlyph.Text = Topmost ? "\uE718" : "\uE77A";
        PinButton.ToolTip = Topmost ? "Bỏ ghim (Hiện đang ghim trên cùng)" : "Ghim luôn hiển thị trên cùng";
    }

    private void DisplayModeButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        var currentMode = _settingsManager.Current.FloatingLyricsMode;

        void AddModeItem(string header, FloatingLyricsDisplayMode mode)
        {
            var item = new MenuItem
            {
                Header = header,
                IsChecked = currentMode == mode,
                IsCheckable = true
            };
            item.Click += (s, ev) =>
            {
                _settingsManager.Current.FloatingLyricsMode = mode;
                _settingsManager.SaveSettings();
                RebuildLyricsList();
            };
            menu.Items.Add(item);
        }

        AddModeItem("Lời gốc + Pinyin", FloatingLyricsDisplayMode.OriginalAndRomanization);
        AddModeItem("Lời gốc + Lời dịch", FloatingLyricsDisplayMode.OriginalAndTranslation);
        menu.Items.Add(new Separator());
        AddModeItem("Chỉ Pinyin", FloatingLyricsDisplayMode.RomanizationOnly);
        AddModeItem("Chỉ Lời gốc", FloatingLyricsDisplayMode.OriginalOnly);
        AddModeItem("Chỉ Lời dịch", FloatingLyricsDisplayMode.TranslationOnly);
        menu.Items.Add(new Separator());
        AddModeItem("Pinyin + Lời dịch", FloatingLyricsDisplayMode.RomanizationAndTranslation);

        if (_isVietnameseTrack)
        {
            menu.Items.Add(new Separator());
            var vnNotice = new MenuItem
            {
                Header = "(Đang phát bài Tiếng Việt: tự động hiển thị Lời gốc)",
                IsEnabled = false
            };
            menu.Items.Add(vnNotice);
        }

        menu.PlacementTarget = DisplayModeButton;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void BackgroundButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        var settings = _settingsManager.Current;

        // 1. Nhóm Dạng nền
        var bgStyleMenu = new MenuItem { Header = "Dạng nền (Background Style)" };

        void AddStyleItem(string label, FloatingLyricsBackgroundStyle style)
        {
            var mi = new MenuItem
            {
                Header = label,
                IsChecked = settings.FloatingLyricsBgStyle == style,
                IsCheckable = true
            };
            mi.Click += (s, ev) =>
            {
                settings.FloatingLyricsBgStyle = style;
                _settingsManager.SaveSettings();
                ApplyBackgroundAppearance();
            };
            bgStyleMenu.Items.Add(mi);
        }

        AddStyleItem("Kính mờ Fluent / Acrylic (Mặc định)", FloatingLyricsBackgroundStyle.AcrylicGlass);
        AddStyleItem("Trong suốt hoàn toàn (Chỉ nổi chữ lên Desktop)", FloatingLyricsBackgroundStyle.Transparent);
        AddStyleItem("Nền tối đồng màu (Solid Black)", FloatingLyricsBackgroundStyle.SolidDark);
        AddStyleItem("Xám hiện đại (Dark Slate)", FloatingLyricsBackgroundStyle.DarkSlate);
        menu.Items.Add(bgStyleMenu);

        // 2. Nhóm Độ trong suốt (Opacity)
        var opacityMenu = new MenuItem { Header = "Độ trong suốt (Opacity)" };

        void AddOpacityItem(string label, double value)
        {
            var mi = new MenuItem
            {
                Header = label,
                IsChecked = Math.Abs(settings.FloatingLyricsOpacity - value) < 0.05,
                IsCheckable = true
            };
            mi.Click += (s, ev) =>
            {
                settings.FloatingLyricsOpacity = value;
                _settingsManager.SaveSettings();
                ApplyBackgroundAppearance();
            };
            opacityMenu.Items.Add(mi);
        }

        AddOpacityItem("100% (Đậm nhất)", 1.0);
        AddOpacityItem("85% (Tiêu chuẩn)", 0.85);
        AddOpacityItem("70% (Mờ nhẹ)", 0.70);
        AddOpacityItem("50% (Trong suốt)", 0.50);
        AddOpacityItem("30% (Rất trong suốt)", 0.30);
        AddOpacityItem("15% (Cực mờ)", 0.15);
        menu.Items.Add(opacityMenu);

        // 3. Nhóm Hiệu ứng hạt nền
        var effectMenu = new MenuItem { Header = "Hiệu ứng hạt nền (Background Effect)" };

        void AddEffectItem(string label, LyricsBackgroundEffectType effect)
        {
            var mi = new MenuItem
            {
                Header = label,
                IsChecked = settings.FloatingLyricsBgEffect == effect,
                IsCheckable = true
            };
            mi.Click += (s, ev) =>
            {
                settings.FloatingLyricsBgEffect = effect;
                WindowBackgroundEffect.ExplicitEffect = effect;
                _settingsManager.SaveSettings();
            };
            effectMenu.Items.Add(mi);
        }

        AddEffectItem("Tắt hiệu ứng hạt", LyricsBackgroundEffectType.None);
        effectMenu.Items.Add(new Separator());
        AddEffectItem("Hoa anh đào rơi (Sakura)", LyricsBackgroundEffectType.Sakura);
        AddEffectItem("Tuyết rơi êm đềm (Snow)", LyricsBackgroundEffectType.Snow);
        AddEffectItem("Mưa rơi (Rain)", LyricsBackgroundEffectType.Rain);
        AddEffectItem("Lá thu bay (Autumn Leaves)", LyricsBackgroundEffectType.AutumnLeaves);
        AddEffectItem("Đom đóm mùa hè (Fireflies)", LyricsBackgroundEffectType.Fireflies);
        AddEffectItem("Bụi sao tinh tú (Starfield)", LyricsBackgroundEffectType.Starfield);
        AddEffectItem("Bong bóng nổi (Floating Bubbles)", LyricsBackgroundEffectType.FloatingBubbles);
        AddEffectItem("Dải cực quang (Aurora Wave)", LyricsBackgroundEffectType.AuroraWave);
        menu.Items.Add(effectMenu);

        menu.PlacementTarget = BackgroundButton;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void TextStyleButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        var settings = _settingsManager.Current;

        // 1. Màu chữ câu đang hát
        var activeColorMenu = new MenuItem { Header = "Màu chữ câu đang hát (Active Color)" };

        void AddColorItem(string label, string hexColor)
        {
            var mi = new MenuItem
            {
                Header = label,
                IsChecked = string.Equals(settings.FloatingLyricsActiveTextColor, hexColor, StringComparison.OrdinalIgnoreCase),
                IsCheckable = true
            };
            mi.Click += (s, ev) =>
            {
                settings.FloatingLyricsActiveTextColor = hexColor;
                _settingsManager.SaveSettings();
                ApplyThemeBrushes();
                UpdateLyricsBrushes();
            };
            activeColorMenu.Items.Add(mi);
        }

        AddColorItem("Trắng sáng (#FFFFFF)", "#FFFFFF");
        AddColorItem("Vàng Gold (#FFD54F)", "#FFD54F");
        AddColorItem("Xanh Accent (#4CC2FF)", "#4CC2FF");
        AddColorItem("Xanh ngọc Mint (#4ADE80)", "#4ADE80");
        AddColorItem("Tím Pastel (#C084FC)", "#C084FC");
        AddColorItem("Hồng Neon (#F472B6)", "#F472B6");
        menu.Items.Add(activeColorMenu);

        // 2. Tốc độ cuộn lời bài hát
        var scrollSpeedMenu = new MenuItem { Header = "Tốc độ cuộn (Scroll Speed)" };

        void AddSpeedItem(string label, FloatingLyricsScrollSpeed speed)
        {
            var mi = new MenuItem
            {
                Header = label,
                IsChecked = settings.FloatingLyricsScrollSpeed == speed,
                IsCheckable = true
            };
            mi.Click += (s, ev) =>
            {
                settings.FloatingLyricsScrollSpeed = speed;
                _settingsManager.SaveSettings();
            };
            scrollSpeedMenu.Items.Add(mi);
        }

        AddSpeedItem("Nhanh (250ms)", FloatingLyricsScrollSpeed.Fast);
        AddSpeedItem("Vừa phải (450ms - Mặc định)", FloatingLyricsScrollSpeed.Normal);
        AddSpeedItem("Chậm rãi / Mượt mà (700ms)", FloatingLyricsScrollSpeed.Smooth);
        menu.Items.Add(scrollSpeedMenu);

        // 3. Hiệu ứng cuộn (Scroll Easing)
        var scrollEasingMenu = new MenuItem { Header = "Hiệu ứng chuyển động cuộn (Scroll Easing)" };

        void AddEasingItem(string label, FloatingLyricsScrollEasing easing)
        {
            var mi = new MenuItem
            {
                Header = label,
                IsChecked = settings.FloatingLyricsScrollEasing == easing,
                IsCheckable = true
            };
            mi.Click += (s, ev) =>
            {
                settings.FloatingLyricsScrollEasing = easing;
                _settingsManager.SaveSettings();
            };
            scrollEasingMenu.Items.Add(mi);
        }

        AddEasingItem("Mượt mà (CubicEase Out - Apple Music style)", FloatingLyricsScrollEasing.Cubic);
        AddEasingItem("Êm dịu (QuadraticEase Out)", FloatingLyricsScrollEasing.Quadratic);
        AddEasingItem("Đều đặn (Linear)", FloatingLyricsScrollEasing.Linear);
        menu.Items.Add(scrollEasingMenu);

        menu.Items.Add(new Separator());

        // 4. Điều chỉnh cỡ chữ
        var increase = new MenuItem { Header = "Phóng to chữ (+2 pt)" };
        increase.Click += (s, ev) => ChangeFontSize(2.0);
        menu.Items.Add(increase);

        var reset = new MenuItem { Header = "Cỡ chữ mặc định (18 pt)" };
        reset.Click += (s, ev) =>
        {
            settings.FloatingLyricsFontSize = 18.0;
            _settingsManager.SaveSettings();
            ApplyFontSize(18.0);
        };
        menu.Items.Add(reset);

        var decrease = new MenuItem { Header = "Thu nhỏ chữ (-2 pt)" };
        decrease.Click += (s, ev) => ChangeFontSize(-2.0);
        menu.Items.Add(decrease);

        menu.PlacementTarget = TextStyleButton;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void ChangeFontSize(double delta)
    {
        var newSize = Math.Clamp(_settingsManager.Current.FloatingLyricsFontSize + delta, 12.0, 36.0);
        _settingsManager.Current.FloatingLyricsFontSize = newSize;
        _settingsManager.SaveSettings();
        ApplyFontSize(newSize);
    }

    private void ApplyFontSize(double size)
    {
        foreach (var item in Items)
        {
            item.FontSize = size;
        }
    }
    #endregion

    #region Lyrics Data and Synchronization
    public void UpdateTrackInfo(string title, string artist)
    {
        TrackTitleText.Text = string.IsNullOrWhiteSpace(title) ? "Lời bài hát" : title;
        TrackArtistText.Text = string.IsNullOrWhiteSpace(artist) ? "AF Media Bar" : artist;
    }

    public void SetLyrics(LyricDocument? document, bool isVietnamese)
    {
        if (AreDocumentsEquivalent(_currentDocument, document) && _isVietnameseTrack == isVietnamese)
        {
            return;
        }

        _currentDocument = document;
        _isVietnameseTrack = isVietnamese;
        _lastActiveIndex = -1;
        RebuildLyricsList();
    }

    private static bool AreDocumentsEquivalent(LyricDocument? a, LyricDocument? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a == null || b == null) return false;
        if (a.Lines.Count != b.Lines.Count) return false;
        if (a.Lines.Count == 0) return true;
        return string.Equals(a.Lines[0].Text, b.Lines[0].Text, StringComparison.Ordinal)
            && string.Equals(a.Lines[^1].Text, b.Lines[^1].Text, StringComparison.Ordinal);
    }

    private void RebuildLyricsList()
    {
        Items.Clear();

        if (_currentDocument == null || _currentDocument.Lines.Count == 0)
        {
            EmptyLyricsPlaceholder.Visibility = Visibility.Visible;
            return;
        }

        EmptyLyricsPlaceholder.Visibility = Visibility.Collapsed;
        var mode = _settingsManager.Current.FloatingLyricsMode;
        var baseFontSize = _settingsManager.Current.FloatingLyricsFontSize;

        for (int i = 0; i < _currentDocument.Lines.Count; i++)
        {
            var line = _currentDocument.Lines[i];
            string primaryText = line.Text;
            string? secondaryText = null;

            if (_isVietnameseTrack)
            {
                // Riêng với bài hát Tiếng Việt: Luôn hiển thị duy nhất Lời gốc
                primaryText = line.Text;
                secondaryText = null;
            }
            else
            {
                string? pinyin = line.Romanization;
                if (string.IsNullOrWhiteSpace(pinyin) && PinyinGeneratorService.HasChinese(line.Text))
                {
                    pinyin = PinyinGeneratorService.ToPinyin(line.Text);
                }

                string? translation = line.Translation;

                switch (mode)
                {
                    case FloatingLyricsDisplayMode.OriginalAndRomanization:
                        primaryText = line.Text;
                        secondaryText = pinyin;
                        break;
                    case FloatingLyricsDisplayMode.OriginalAndTranslation:
                        primaryText = line.Text;
                        secondaryText = translation;
                        break;
                    case FloatingLyricsDisplayMode.RomanizationOnly:
                        primaryText = !string.IsNullOrWhiteSpace(pinyin) ? pinyin : line.Text;
                        secondaryText = null;
                        break;
                    case FloatingLyricsDisplayMode.OriginalOnly:
                        primaryText = line.Text;
                        secondaryText = null;
                        break;
                    case FloatingLyricsDisplayMode.TranslationOnly:
                        primaryText = !string.IsNullOrWhiteSpace(translation) ? translation : line.Text;
                        secondaryText = null;
                        break;
                    case FloatingLyricsDisplayMode.RomanizationAndTranslation:
                        primaryText = !string.IsNullOrWhiteSpace(pinyin) ? pinyin : line.Text;
                        secondaryText = translation;
                        break;
                }
            }

            var item = new FloatingLyricsItem
            {
                Index = i,
                Line = line,
                PrimaryText = primaryText,
                SecondaryText = secondaryText,
                FontSize = baseFontSize,
                PrimaryForeground = _futureBrush,
                SecondaryForeground = _futureSecondaryBrush
            };

            Items.Add(item);
        }
    }

    private void UpdateLyricsBrushes()
    {
        for (int i = 0; i < Items.Count; i++)
        {
            var item = Items[i];
            if (i < _lastActiveIndex)
            {
                item.PrimaryForeground = _pastBrush;
                item.SecondaryForeground = _pastSecondaryBrush;
            }
            else if (i == _lastActiveIndex)
            {
                item.PrimaryForeground = _activeBrush;
                item.SecondaryForeground = _activeSecondaryBrush;
            }
            else
            {
                item.PrimaryForeground = _futureBrush;
                item.SecondaryForeground = _futureSecondaryBrush;
            }
        }
    }

    public void UpdatePlaybackPosition(double currentSeconds, bool isPlaying)
    {
        if (_currentDocument == null || Items.Count == 0 || !isPlaying) return;

        var activeIndex = LyricLineIndexPolicy.FindIndex(_currentDocument.Lines, currentSeconds);

        if (activeIndex != _lastActiveIndex)
        {
            for (int i = 0; i < Items.Count; i++)
            {
                var item = Items[i];
                if (activeIndex < 0)
                {
                    item.IsActive = false;
                    item.IsPast = false;
                    item.PrimaryForeground = _futureBrush;
                    item.SecondaryForeground = _futureSecondaryBrush;
                }
                else if (i < activeIndex)
                {
                    item.IsActive = false;
                    item.IsPast = true;
                    item.PrimaryForeground = _pastBrush;
                    item.SecondaryForeground = _pastSecondaryBrush;
                }
                else if (i == activeIndex)
                {
                    item.IsActive = true;
                    item.IsPast = false;
                    item.PrimaryForeground = _activeBrush;
                    item.SecondaryForeground = _activeSecondaryBrush;
                }
                else
                {
                    item.IsActive = false;
                    item.IsPast = false;
                    item.PrimaryForeground = _futureBrush;
                    item.SecondaryForeground = _futureSecondaryBrush;
                }
            }

            _lastActiveIndex = activeIndex;

            if (!_isUserScrolling && activeIndex >= 0)
            {
                ScrollToActiveLine(animate: true);
            }
        }

        // Chạy hiệu ứng Karaoke quét từng chữ nếu câu đang hát có dữ liệu Karaoke âm tiết
        if (activeIndex >= 0 && activeIndex < Items.Count)
        {
            var activeItem = Items[activeIndex];
            if (activeItem.Line.Words != null && activeItem.Line.Words.Count > 0)
            {
                double progress = CalculateProgress(activeItem.Line, currentSeconds);
                Color activeColor = (_activeBrush as SolidColorBrush)?.Color ?? Color.FromRgb(255, 213, 79);
                var karaokeBrush = KaraokeBrushHelper.CreateKaraokeBrush(activeColor, progress, false);
                activeItem.PrimaryForeground = karaokeBrush;
            }
        }
    }

    private static double CalculateProgress(LyricLine line, double currentSeconds)
    {
        if (currentSeconds <= line.Start) return 0.0;
        if (currentSeconds >= line.End) return 1.0;

        var words = line.Words;
        int n = words.Count;
        int totalChars = line.Text.Length;

        if (totalChars > 0)
        {
            double sungChars = 0;
            int curCharIndex = 0;
            for (int i = 0; i < n; i++)
            {
                var w = words[i];
                int wIdx = line.Text.IndexOf(w.Text, curCharIndex, StringComparison.Ordinal);
                if (wIdx < 0) wIdx = curCharIndex;
                int wEnd = wIdx + w.Text.Length;

                if (currentSeconds >= w.End)
                {
                    sungChars = wEnd;
                    curCharIndex = wEnd;
                    continue;
                }

                if (currentSeconds > w.Start)
                {
                    double wDur = Math.Max(0.01, w.End - w.Start);
                    double wFrac = Math.Clamp((currentSeconds - w.Start) / wDur, 0.0, 1.0);
                    sungChars = wIdx + (w.Text.Length * wFrac);
                    break;
                }

                sungChars = wIdx;
                break;
            }
            return Math.Clamp(sungChars / totalChars, 0.0, 1.0);
        }

        double duration = Math.Max(0.3, line.End - line.Start);
        return Math.Clamp((currentSeconds - line.Start) / duration, 0.0, 1.0);
    }

    private void ScrollToActiveLine(bool animate)
    {
        if (_lastActiveIndex < 0 || _lastActiveIndex >= Items.Count) return;

        var container = LyricsItemsList.ItemContainerGenerator.ContainerFromIndex(_lastActiveIndex) as FrameworkElement;
        if (container == null)
        {
            LyricsItemsList.UpdateLayout();
            container = LyricsItemsList.ItemContainerGenerator.ContainerFromIndex(_lastActiveIndex) as FrameworkElement;
        }

        if (container == null) return;

        // Tọa độ Y tuyệt đối của dòng hát bên trong LyricsItemsList (bất biến, không bị ảnh hưởng bởi animation đang chạy)
        Point relativePoint;
        try
        {
            relativePoint = container.TransformToAncestor(LyricsItemsList).Transform(new Point(0, 0));
        }
        catch
        {
            return;
        }

        double itemCenterY = relativePoint.Y + (container.ActualHeight * 0.5);
        double targetOffset = itemCenterY - (LyricsScrollViewer.ViewportHeight * 0.38);
        targetOffset = Math.Clamp(targetOffset, 0, LyricsScrollViewer.ScrollableHeight);

        if (animate)
        {
            AnimateScrollTo(targetOffset);
        }
        else
        {
            _currentScrollStoryboard?.Stop();
            _currentScrollStoryboard = null;
            LyricsScrollViewer.ScrollToVerticalOffset(targetOffset);
        }
    }

    private void AnimateScrollTo(double targetOffset)
    {
        _currentScrollStoryboard?.Stop();

        var settings = _settingsManager.Current;
        var durationMs = settings.FloatingLyricsScrollSpeed switch
        {
            FloatingLyricsScrollSpeed.Fast => 250,
            FloatingLyricsScrollSpeed.Smooth => 700,
            _ => 450
        };

        IEasingFunction? easing = settings.FloatingLyricsScrollEasing switch
        {
            FloatingLyricsScrollEasing.Quadratic => new QuadraticEase { EasingMode = EasingMode.EaseOut },
            FloatingLyricsScrollEasing.Linear => null,
            _ => new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        var animation = new DoubleAnimation
        {
            From = LyricsScrollViewer.VerticalOffset,
            To = targetOffset,
            Duration = TimeSpan.FromMilliseconds(durationMs)
        };
        if (easing != null)
        {
            animation.EasingFunction = easing;
        }

        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        Storyboard.SetTarget(animation, LyricsScrollViewer);
        Storyboard.SetTargetProperty(animation, new PropertyPath(AnimatedOffsetProperty));

        storyboard.Completed += (s, e) =>
        {
            _currentScrollStoryboard = null;
        };

        _currentScrollStoryboard = storyboard;
        storyboard.Begin();
    }

    public static readonly DependencyProperty AnimatedOffsetProperty =
        DependencyProperty.RegisterAttached(
            "AnimatedOffset",
            typeof(double),
            typeof(FloatingLyricsWindow),
            new PropertyMetadata(0.0, OnAnimatedOffsetChanged));

    private static void OnAnimatedOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ScrollViewer sv)
        {
            sv.ScrollToVerticalOffset((double)e.NewValue);
        }
    }

    private void LyricsScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Khi người dùng cuộn chuột thủ công, tạm dừng auto-scroll để người dùng đọc lời
        _isUserScrolling = true;
        _userScrollCooldownTimer.Stop();
        _userScrollCooldownTimer.Start();
    }

    private void LyricsScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalChange != 0 && LyricsScrollViewer.IsMouseOver && Mouse.LeftButton == MouseButtonState.Pressed)
        {
            _isUserScrolling = true;
            _userScrollCooldownTimer.Stop();
            _userScrollCooldownTimer.Start();
        }
    }

    private void ItemContainer_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is FloatingLyricsItem item)
        {
            SeekRequested?.Invoke(item.Line.Start);
        }
    }
    #endregion
}
