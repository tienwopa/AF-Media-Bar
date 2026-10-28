using System.Windows;
using System.Windows.Controls;
using AFMediaBar.Classes.Utils;
using AFMediaBar.Custom.Settings;
using AFMediaBar.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;
using Wpf.Ui.Controls;

namespace AFMediaBar.Views.Pages;

/// <summary>歌词设置页面。 / Lyric settings page.</summary>
public partial class LyricsPage : INavigableView<LyricsViewModel>
{
    public LyricsViewModel ViewModel { get; }
    public CustomSettingsManager CustomSettings { get; }

    private bool _isInitializing = true;

    public static readonly DependencyProperty TranslationOffsetMsProperty = DependencyProperty.Register(
        nameof(TranslationOffsetMs), typeof(double), typeof(LyricsPage),
        new FrameworkPropertyMetadata(0.0, (d, e) =>
        {
            if (d is LyricsPage page && !page._isInitializing && e.NewValue is double val)
            {
                page.CustomSettings.Current.TranslationOffsetMs = (int)Math.Round(val);
                page.CustomSettings.SaveSettings();
            }
        }));

    public double TranslationOffsetMs
    {
        get => (double)GetValue(TranslationOffsetMsProperty);
        set => SetValue(TranslationOffsetMsProperty, value);
    }

    public static readonly DependencyProperty RomanizationOffsetMsProperty = DependencyProperty.Register(
        nameof(RomanizationOffsetMs), typeof(double), typeof(LyricsPage),
        new FrameworkPropertyMetadata(0.0, (d, e) =>
        {
            if (d is LyricsPage page && !page._isInitializing && e.NewValue is double val)
            {
                page.CustomSettings.Current.RomanizationOffsetMs = (int)Math.Round(val);
                page.CustomSettings.SaveSettings();
            }
        }));

    public double RomanizationOffsetMs
    {
        get => (double)GetValue(RomanizationOffsetMsProperty);
        set => SetValue(RomanizationOffsetMsProperty, value);
    }

    public LyricsPage(LyricsViewModel viewModel, CustomSettingsManager? customSettings = null)
    {
        ViewModel = viewModel;
        CustomSettings = customSettings ?? new CustomSettingsManager();
        DataContext = this;
        InitializeComponent();

        Loaded += LyricsPage_Loaded;
    }

    private void LyricsPage_Loaded(object sender, RoutedEventArgs e)
    {
        _isInitializing = true;
        try
        {
            var settings = CustomSettings.Current;
            CustomEnableBilingualSwitch.IsChecked = settings.EnableBilingualLyrics;
            CustomEnableDualTaskbarSwitch.IsChecked = settings.EnableDualTaskbarLyrics;
            CustomEnableKaraokeSwitch.IsChecked = settings.EnableKaraokeHighlight;
            CustomOnlyKaraokeWhenAvailableSwitch.IsChecked = settings.OnlyKaraokeWhenAvailable;
            CustomAutoAdaptColorSwitch.IsChecked = settings.AutoAdaptTaskbarColor;
            CustomAutoSkipVietnameseSwitch.IsChecked = settings.AutoSkipVietnamese;
            CustomAutoBalanceSingleLineSwitch.IsChecked = settings.AutoBalanceSingleLine;

            CustomTransPlacementCombo.SelectedIndex = (int)settings.TranslationPlacement;
            CustomPinyinPlacementCombo.SelectedIndex = (int)settings.RomanizationPlacement;
            CustomSecondaryAlignCombo.SelectedIndex = (int)settings.SecondaryAlignment;
            CustomPrimaryAlignCombo.SelectedIndex = (int)settings.PrimaryAlignment;
            CustomAnimationModeCombo.SelectedIndex = (int)settings.AnimationMode;

            SelectMaxWidthInCombo(CustomPrimaryMaxWidthCombo, settings.PrimaryMaxWidth);

            TranslationOffsetMs = settings.TranslationOffsetMs;
            RomanizationOffsetMs = settings.RomanizationOffsetMs;

            CustomLineOrderCombo.SelectedIndex = (int)settings.LineOrder;
            SelectFontSizeInCombo(CustomTransFontSizeCombo, settings.TranslationFontSize);
            SelectFontSizeInCombo(CustomPinyinFontSizeCombo, settings.RomanizationFontSize);
            CustomFontWeightCombo.SelectedIndex = (int)settings.WeightEmphasis;
            CustomKaraokeTargetCombo.SelectedIndex = (int)settings.KaraokeTarget;

            SelectColorInCombo(CustomTransColorCombo, settings.TranslationColor);
            SelectColorInCombo(CustomPinyinColorCombo, settings.RomanizationColor);
        }
        finally
        {
            _isInitializing = false;
        }
    }

    private static void SelectMaxWidthInCombo(System.Windows.Controls.ComboBox combo, double maxWidth)
    {
        string target = ((int)maxWidth).ToString(System.Globalization.CultureInfo.InvariantCulture);
        for (int i = 0; i < combo.Items.Count; i++)
        {
            if (combo.Items[i] is System.Windows.Controls.ComboBoxItem item &&
                string.Equals(item.Tag?.ToString(), target, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedIndex = i;
                return;
            }
        }
        if (combo.Items.Count > 2) combo.SelectedIndex = 2; // Default 480 px
    }

    private static void SelectFontSizeInCombo(System.Windows.Controls.ComboBox combo, double size)
    {
        string sizeStr = size.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        for (int i = 0; i < combo.Items.Count; i++)
        {
            if (combo.Items[i] is System.Windows.Controls.ComboBoxItem item &&
                string.Equals(item.Tag?.ToString(), sizeStr, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedIndex = i;
                return;
            }
        }
        if (combo.Items.Count > 2) combo.SelectedIndex = 2; // Tiêu chuẩn 11.5 pt
    }

    private static void SelectColorInCombo(System.Windows.Controls.ComboBox combo, string hexColor)
    {
        for (int i = 0; i < combo.Items.Count; i++)
        {
            if (combo.Items[i] is System.Windows.Controls.ComboBoxItem item &&
                string.Equals(item.Tag?.ToString(), hexColor, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedIndex = i;
                return;
            }
        }
        if (combo.Items.Count > 0 && combo.SelectedIndex < 0) combo.SelectedIndex = 0;
    }

    private void CustomEnableBilingualSwitch_Click(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        CustomSettings.Current.EnableBilingualLyrics = CustomEnableBilingualSwitch.IsChecked ?? false;
        CustomSettings.SaveSettings();
    }

    private void CustomEnableDualTaskbarSwitch_Click(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        CustomSettings.Current.EnableDualTaskbarLyrics = CustomEnableDualTaskbarSwitch.IsChecked ?? false;
        CustomSettings.SaveSettings();
    }

    private void CustomEnableKaraokeSwitch_Click(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        CustomSettings.Current.EnableKaraokeHighlight = CustomEnableKaraokeSwitch.IsChecked ?? true;
        CustomSettings.SaveSettings();
    }

    private void CustomOnlyKaraokeWhenAvailableSwitch_Click(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        CustomSettings.Current.OnlyKaraokeWhenAvailable = CustomOnlyKaraokeWhenAvailableSwitch.IsChecked ?? true;
        CustomSettings.SaveSettings();
    }

    private void CustomAutoBalanceSingleLineSwitch_Click(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        CustomSettings.Current.AutoBalanceSingleLine = CustomAutoBalanceSingleLineSwitch.IsChecked ?? true;
        CustomSettings.SaveSettings();
    }

    private void CustomAutoAdaptColorSwitch_Click(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        CustomSettings.Current.AutoAdaptTaskbarColor = CustomAutoAdaptColorSwitch.IsChecked ?? true;
        CustomSettings.SaveSettings();
    }

    private void CustomLineOrderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        if (CustomLineOrderCombo.SelectedIndex >= 0)
        {
            CustomSettings.Current.LineOrder = (BilingualLineOrder)CustomLineOrderCombo.SelectedIndex;
            CustomSettings.SaveSettings();
        }
    }

    private void CustomTransFontSizeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        if (CustomTransFontSizeCombo.SelectedItem is System.Windows.Controls.ComboBoxItem item &&
            item.Tag is string tagStr &&
            double.TryParse(tagStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double size))
        {
            CustomSettings.Current.TranslationFontSize = size;
            CustomSettings.SaveSettings();
        }
    }

    private void CustomPinyinFontSizeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        if (CustomPinyinFontSizeCombo.SelectedItem is System.Windows.Controls.ComboBoxItem item &&
            item.Tag is string tagStr &&
            double.TryParse(tagStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double size))
        {
            CustomSettings.Current.RomanizationFontSize = size;
            CustomSettings.SaveSettings();
        }
    }

    private void CustomFontWeightCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        if (CustomFontWeightCombo.SelectedIndex >= 0)
        {
            CustomSettings.Current.WeightEmphasis = (BilingualWeightEmphasis)CustomFontWeightCombo.SelectedIndex;
            CustomSettings.SaveSettings();
        }
    }

    private void CustomPrimaryAlignCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        if (CustomPrimaryAlignCombo.SelectedIndex >= 0)
        {
            CustomSettings.Current.PrimaryAlignment = (PrimaryTaskbarAlignment)CustomPrimaryAlignCombo.SelectedIndex;
            CustomSettings.SaveSettings();
        }
    }

    private void CustomPrimaryMaxWidthCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        if (CustomPrimaryMaxWidthCombo.SelectedItem is System.Windows.Controls.ComboBoxItem item &&
            item.Tag is string tagStr &&
            double.TryParse(tagStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double width))
        {
            CustomSettings.Current.PrimaryMaxWidth = width;
            CustomSettings.SaveSettings();
        }
    }

    private void CustomAnimationModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        if (CustomAnimationModeCombo.SelectedIndex >= 0)
        {
            CustomSettings.Current.AnimationMode = (LyricsAnimationMode)CustomAnimationModeCombo.SelectedIndex;
            CustomSettings.SaveSettings();
        }
    }

    private void CustomKaraokeTargetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        if (CustomKaraokeTargetCombo.SelectedIndex >= 0)
        {
            CustomSettings.Current.KaraokeTarget = (KaraokeTargetMode)CustomKaraokeTargetCombo.SelectedIndex;
            CustomSettings.SaveSettings();
        }
    }

    private void CustomSecondaryAlignCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        if (CustomSecondaryAlignCombo.SelectedIndex >= 0)
        {
            CustomSettings.Current.SecondaryAlignment = (SecondaryTaskbarAlignment)CustomSecondaryAlignCombo.SelectedIndex;
            CustomSettings.SaveSettings();
        }
    }

    private void CustomTransPlacementCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        if (CustomTransPlacementCombo.SelectedIndex >= 0)
        {
            CustomSettings.Current.TranslationPlacement = (LyricElementPlacement)CustomTransPlacementCombo.SelectedIndex;
            CustomSettings.SaveSettings();
        }
    }

    private void CustomPinyinPlacementCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        if (CustomPinyinPlacementCombo.SelectedIndex >= 0)
        {
            CustomSettings.Current.RomanizationPlacement = (LyricElementPlacement)CustomPinyinPlacementCombo.SelectedIndex;
            CustomSettings.SaveSettings();
        }
    }

    private void CustomTransColorCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        if (CustomTransColorCombo.SelectedItem is System.Windows.Controls.ComboBoxItem item && item.Tag is string hex)
        {
            CustomSettings.Current.TranslationColor = hex;
            CustomSettings.SaveSettings();
        }
    }

    private void CustomPinyinColorCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        if (CustomPinyinColorCombo.SelectedItem is System.Windows.Controls.ComboBoxItem item && item.Tag is string hex)
        {
            CustomSettings.Current.RomanizationColor = hex;
            CustomSettings.SaveSettings();
        }
    }

    private void CustomAutoSkipVietnameseSwitch_Click(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        CustomSettings.Current.AutoSkipVietnamese = CustomAutoSkipVietnameseSwitch.IsChecked ?? true;
        CustomSettings.SaveSettings();
    }

    /// <summary>页面首次加载时执行入场揭示。/ Reveals the page on first load.</summary>
    private void OnPageLoaded(object sender, RoutedEventArgs e) => SettingsRevealAnimator.Play(sender as Panel);

    private async void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        if (await SettingsResetDialog.ConfirmAsync("Common.Page.Lyrics", cancellationToken: ViewModel.ContextCancellationToken))
            ViewModel.ResetLyrics();
    }

    private async void ManageArtistSeparators_Click(object sender, RoutedEventArgs e)
    {
        var separators = await ArtistSeparatorsEditor.EditAsync(ViewModel.ArtistSeparatorsText, ViewModel.DefaultArtistSeparatorsText, ViewModel.ContextCancellationToken);
        if (separators is not null) ViewModel.ApplyArtistSeparators(separators);
    }
}
