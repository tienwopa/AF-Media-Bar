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
using AFMediaBar.Custom.Settings;
using AFMediaBar.Custom.Translation;
using AFMediaBar.Custom.Utils;

namespace AFMediaBar.Custom.FloatingLyrics;

/// <summary>
/// Cửa sổ Bảng lời bài hát nổi (Floating Lyrics Window) đồng bộ theo nhạc,
/// tự do di chuyển, thay đổi kích thước và tùy chỉnh độ trong suốt.
/// </summary>
public sealed partial class FloatingLyricsWindow : Window
{
    private static readonly Brush ActiveBrush = new SolidColorBrush(Color.FromRgb(255, 255, 255));
    private static readonly Brush ActiveAccentBrush = new SolidColorBrush(Color.FromRgb(76, 194, 255));
    private static readonly Brush ActiveSecondaryBrush = new SolidColorBrush(Color.FromRgb(255, 213, 79)); // Vàng Gold
    private static readonly Brush PastBrush = new SolidColorBrush(Color.FromArgb(135, 255, 255, 255));
    private static readonly Brush PastSecondaryBrush = new SolidColorBrush(Color.FromArgb(110, 255, 213, 79));
    private static readonly Brush FutureBrush = new SolidColorBrush(Color.FromArgb(110, 255, 255, 255));
    private static readonly Brush FutureSecondaryBrush = new SolidColorBrush(Color.FromArgb(90, 255, 213, 79));

    static FloatingLyricsWindow()
    {
        ActiveBrush.Freeze();
        ActiveAccentBrush.Freeze();
        ActiveSecondaryBrush.Freeze();
        PastBrush.Freeze();
        PastSecondaryBrush.Freeze();
        FutureBrush.Freeze();
        FutureSecondaryBrush.Freeze();
    }

    private readonly CustomSettingsManager _settingsManager;
    public ObservableCollection<FloatingLyricsItem> Items { get; } = new();

    private LyricDocument? _currentDocument;
    private bool _isVietnameseTrack;
    private int _lastActiveIndex = -1;
    private bool _isUserScrolling;
    private readonly DispatcherTimer _userScrollCooldownTimer;
    private readonly DispatcherTimer _saveGeometryTimer;

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

        ApplySettings();
        RestoreGeometrySettings();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var source = PresentationSource.FromVisual(this) as HwndSource;
        source?.AddHook(WndProc);
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
        if (msg == WM_NCHITTEST)
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
    #endregion

    #region Geometry and Settings Persistence
    private void ApplySettings()
    {
        var settings = _settingsManager.Current;

        // Áp dụng độ trong suốt
        double opacity = Math.Clamp(settings.FloatingLyricsOpacity, 0.2, 1.0);
        MainSurface.Opacity = opacity;

        // Áp dụng Topmost
        Topmost = settings.FloatingLyricsTopmost;
        PinGlyph.Text = Topmost ? "\uE718" : "\uE77A";
        PinButton.ToolTip = Topmost ? "Bỏ ghim (Hiện đang ghim trên cùng)" : "Ghim luôn hiển thị trên cùng";
    }

    private void RestoreGeometrySettings()
    {
        var settings = _settingsManager.Current;
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
            // Kiểm tra xem vị trí có nằm trong vùng nhìn thấy của các màn hình hay không
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
        settings.FloatingLyricsLeft = Left;
        settings.FloatingLyricsTop = Top;
        settings.FloatingLyricsWidth = Width;
        settings.FloatingLyricsHeight = Height;
        _settingsManager.SaveSettings();
    }

    protected override void OnLocationChanged(EventArgs e)
    {
        base.OnLocationChanged(e);
        if (IsLoaded)
        {
            _saveGeometryTimer.Stop();
            _saveGeometryTimer.Start();
        }
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        if (IsLoaded)
        {
            _saveGeometryTimer.Stop();
            _saveGeometryTimer.Start();
        }
    }
    #endregion

    #region Header Interactions
    private void HeaderBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try
            {
                DragMove();
            }
            catch
            {
                // Bỏ qua ngoại lệ nếu chuột nhả quá nhanh
            }
        }
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

    private void OpacityButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        var currentOpacity = _settingsManager.Current.FloatingLyricsOpacity;

        void AddOpacityItem(string label, double value)
        {
            var item = new MenuItem
            {
                Header = label,
                IsChecked = Math.Abs(currentOpacity - value) < 0.05,
                IsCheckable = true
            };
            item.Click += (s, ev) =>
            {
                _settingsManager.Current.FloatingLyricsOpacity = value;
                _settingsManager.SaveSettings();
                MainSurface.Opacity = value;
            };
            menu.Items.Add(item);
        }

        AddOpacityItem("100% (Đậm nhất)", 1.0);
        AddOpacityItem("90% (Tiêu chuẩn)", 0.90);
        AddOpacityItem("80% (Hơi mờ)", 0.80);
        AddOpacityItem("70% (Mờ)", 0.70);
        AddOpacityItem("50% (Trong suốt)", 0.50);
        AddOpacityItem("35% (Rất trong suốt)", 0.35);

        menu.PlacementTarget = OpacityButton;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void FontSizeButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        var currentSize = _settingsManager.Current.FloatingLyricsFontSize;

        var increase = new MenuItem { Header = "Phóng to chữ (+2 pt)" };
        increase.Click += (s, ev) => ChangeFontSize(2.0);
        menu.Items.Add(increase);

        var reset = new MenuItem { Header = "Cỡ chữ mặc định (18 pt)" };
        reset.Click += (s, ev) =>
        {
            _settingsManager.Current.FloatingLyricsFontSize = 18.0;
            _settingsManager.SaveSettings();
            ApplyFontSize(18.0);
        };
        menu.Items.Add(reset);

        var decrease = new MenuItem { Header = "Thu nhỏ chữ (-2 pt)" };
        decrease.Click += (s, ev) => ChangeFontSize(-2.0);
        menu.Items.Add(decrease);

        menu.PlacementTarget = FontSizeButton;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void ChangeFontSize(double delta)
    {
        var newSize = Math.Clamp(_settingsManager.Current.FloatingLyricsFontSize + delta, 12.0, 32.0);
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
        _currentDocument = document;
        _isVietnameseTrack = isVietnamese;
        _lastActiveIndex = -1;
        RebuildLyricsList();
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
                PrimaryForeground = FutureBrush,
                SecondaryForeground = FutureSecondaryBrush
            };

            Items.Add(item);
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
                if (i < activeIndex)
                {
                    item.IsActive = false;
                    item.IsPast = true;
                    item.PrimaryForeground = PastBrush;
                    item.SecondaryForeground = PastSecondaryBrush;
                }
                else if (i == activeIndex)
                {
                    item.IsActive = true;
                    item.IsPast = false;
                    item.PrimaryForeground = ActiveBrush;
                    item.SecondaryForeground = ActiveSecondaryBrush;
                }
                else
                {
                    item.IsActive = false;
                    item.IsPast = false;
                    item.PrimaryForeground = FutureBrush;
                    item.SecondaryForeground = FutureSecondaryBrush;
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
                // Quét màu vàng nổi bật hoặc trắng
                var karaokeBrush = KaraokeBrushHelper.CreateKaraokeBrush(Color.FromRgb(255, 213, 79), progress, false);
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

        // Tính toán khoảng cách để đưa câu đang hát vào khoảng 35-40% chiều cao của khung nhìn
        var transform = container.TransformToAncestor(LyricsScrollViewer);
        var itemRect = transform.TransformBounds(new Rect(0, 0, container.ActualWidth, container.ActualHeight));
        double targetOffset = LyricsScrollViewer.VerticalOffset + itemRect.Top - (LyricsScrollViewer.ViewportHeight * 0.38);

        targetOffset = Math.Clamp(targetOffset, 0, LyricsScrollViewer.ScrollableHeight);

        if (animate)
        {
            AnimateScrollTo(targetOffset);
        }
        else
        {
            LyricsScrollViewer.ScrollToVerticalOffset(targetOffset);
        }
    }

    private void AnimateScrollTo(double targetOffset)
    {
        var animation = new DoubleAnimation
        {
            From = LyricsScrollViewer.VerticalOffset,
            To = targetOffset,
            Duration = TimeSpan.FromMilliseconds(450),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        Storyboard.SetTarget(animation, LyricsScrollViewer);
        Storyboard.SetTargetProperty(animation, new PropertyPath(AnimatedOffsetProperty));
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
