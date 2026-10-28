using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using AFMediaBar.Classes.Interop;
using AFMediaBar.Classes.Utils;
using AFMediaBar.Custom.Settings;
using AFMediaBar.Custom.Utils;

namespace AFMediaBar.Custom.DualOverlay;

/// <summary>
/// Cửa sổ hiển thị lời dịch / pinyin nổi trên Màn hình thứ 2 (Secondary Monitor).
/// </summary>
public partial class DualTaskbarSecondaryWindow : Window
{
    private readonly CustomSettingsManager _settingsManager;

    private string _currentPrimaryText = string.Empty;
    private string? _currentSecondaryText;
    private bool _currentIsPrimaryPinyin;
    private double _currentPrimaryProgress = 1.0;
    private double _currentSecondaryProgress = 1.0;

    public DualTaskbarSecondaryWindow(CustomSettingsManager settingsManager)
    {
        InitializeComponent();
        _settingsManager = settingsManager;

        Loaded += (_, _) =>
        {
            ApplyAppearance();
            PositionOnSecondaryScreen();
        };

        _settingsManager.SettingsChanged += _ => Dispatcher.Invoke(() =>
        {
            ApplyAppearance();
            PositionOnSecondaryScreen();
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
                PrimaryLineText.FontSize = pSize;
                SecondaryLineText.FontSize = sSize;
                SecondaryLineText.Text = secondaryText!;
                SecondaryLineText.Visibility = Visibility.Visible;
            }
            else
            {
                PrimaryLineText.FontSize = pSize;
                SecondaryLineText.Visibility = Visibility.Collapsed;
            }

            // 2. Phân bổ độ đậm chữ (FontWeight)
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

            // 3. Áp dụng Brush màu (Karaoke Wipe hoặc Solid Color)
            bool enableKaraoke = settings.EnableKaraokeHighlight;
            bool pinyinWipe = enableKaraoke && (settings.KaraokeTarget == KaraokeTargetMode.Both || settings.KaraokeTarget == KaraokeTargetMode.RomanizationOnly);
            bool transWipe = enableKaraoke && (settings.KaraokeTarget == KaraokeTargetMode.Both || settings.KaraokeTarget == KaraokeTargetMode.TranslationOnly);

            bool primaryWipe = isPrimaryPinyin ? pinyinWipe : transWipe;
            bool secondaryWipe = isPrimaryPinyin ? transWipe : pinyinWipe;

            PrimaryLineText.Foreground = primaryWipe
                ? KaraokeBrushHelper.CreateKaraokeBrush(pCol, primaryProgress, isLight)
                : new SolidColorBrush(pCol);

            SecondaryLineText.Foreground = secondaryWipe
                ? KaraokeBrushHelper.CreateKaraokeBrush(sCol, secondaryProgress, isLight)
                : new SolidColorBrush(sCol);

            // 4. Căn lề hiển thị
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
            PrimaryLineText.HorizontalAlignment = horizAlignment;
            SecondaryLineText.HorizontalAlignment = horizAlignment;
            ContentStack.HorizontalAlignment = horizAlignment;

            PrimaryLineText.Text = primaryText;
            Visibility = Visibility.Visible;
            PositionOnSecondaryScreen();
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

    public void PositionOnSecondaryScreen()
    {
        try
        {
            var monitors = MonitorUtil.GetMonitors();
            var secondary = monitors.FirstOrDefault(m => !m.isPrimary);
            if (secondary.monitorArea.Width > 0)
            {
                var wa = secondary.workArea;

                Width = Math.Min(800, wa.Width * 0.7);
                Height = 56;

                var settings = _settingsManager.Current;
                if (settings.SecondaryAlignment == SecondaryTaskbarAlignment.Left)
                {
                    Left = wa.X + 40;
                }
                else if (settings.SecondaryAlignment == SecondaryTaskbarAlignment.Right)
                {
                    Left = wa.Right - Width - 40;
                }
                else
                {
                    Left = wa.X + (wa.Width - Width) / 2;
                }

                Top = wa.Bottom - Height - 12;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DualTaskbarSecondaryWindow] Loi dinh vi: {ex.Message}");
        }
    }
}
