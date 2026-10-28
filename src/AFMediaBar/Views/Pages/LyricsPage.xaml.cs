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

    public static readonly DependencyProperty TranslationOffsetMsProperty = DependencyProperty.Register(
        nameof(TranslationOffsetMs), typeof(double), typeof(LyricsPage),
        new FrameworkPropertyMetadata(0.0, (d, e) =>
        {
            if (d is LyricsPage page && e.NewValue is double val)
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
            if (d is LyricsPage page && e.NewValue is double val)
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
        var settings = CustomSettings.Current;
        CustomEnableBilingualSwitch.IsChecked = settings.EnableBilingualLyrics;
        CustomEnableDualTaskbarSwitch.IsChecked = settings.EnableDualTaskbarLyrics;
        CustomEnableKaraokeSwitch.IsChecked = settings.EnableKaraokeHighlight;
        CustomAutoAdaptColorSwitch.IsChecked = settings.AutoAdaptTaskbarColor;
        CustomAutoSkipVietnameseSwitch.IsChecked = settings.AutoSkipVietnamese;

        CustomTransPlacementCombo.SelectedIndex = (int)settings.TranslationPlacement;
        CustomPinyinPlacementCombo.SelectedIndex = (int)settings.RomanizationPlacement;
        CustomSecondaryAlignCombo.SelectedIndex = (int)settings.SecondaryAlignment;

        TranslationOffsetMs = settings.TranslationOffsetMs;
        RomanizationOffsetMs = settings.RomanizationOffsetMs;

        SelectFontSizeInCombo(CustomFontSizeCombo, settings.FontSize);
        SelectColorInCombo(CustomTransColorCombo, settings.TranslationColor);
        SelectColorInCombo(CustomPinyinColorCombo, settings.RomanizationColor);
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
        CustomSettings.Current.EnableBilingualLyrics = CustomEnableBilingualSwitch.IsChecked ?? false;
        CustomSettings.SaveSettings();
    }

    private void CustomEnableDualTaskbarSwitch_Click(object sender, RoutedEventArgs e)
    {
        CustomSettings.Current.EnableDualTaskbarLyrics = CustomEnableDualTaskbarSwitch.IsChecked ?? false;
        CustomSettings.SaveSettings();
    }

    private void CustomEnableKaraokeSwitch_Click(object sender, RoutedEventArgs e)
    {
        CustomSettings.Current.EnableKaraokeHighlight = CustomEnableKaraokeSwitch.IsChecked ?? true;
        CustomSettings.SaveSettings();
    }

    private void CustomAutoAdaptColorSwitch_Click(object sender, RoutedEventArgs e)
    {
        CustomSettings.Current.AutoAdaptTaskbarColor = CustomAutoAdaptColorSwitch.IsChecked ?? true;
        CustomSettings.SaveSettings();
    }

    private void CustomFontSizeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CustomFontSizeCombo.SelectedItem is System.Windows.Controls.ComboBoxItem item &&
            item.Tag is string tagStr &&
            double.TryParse(tagStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double size))
        {
            CustomSettings.Current.FontSize = size;
            CustomSettings.SaveSettings();
        }
    }

    private void CustomSecondaryAlignCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CustomSecondaryAlignCombo.SelectedIndex >= 0)
        {
            CustomSettings.Current.SecondaryAlignment = (SecondaryTaskbarAlignment)CustomSecondaryAlignCombo.SelectedIndex;
            CustomSettings.SaveSettings();
        }
    }

    private void CustomTransPlacementCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CustomTransPlacementCombo.SelectedIndex >= 0)
        {
            CustomSettings.Current.TranslationPlacement = (LyricElementPlacement)CustomTransPlacementCombo.SelectedIndex;
            CustomSettings.SaveSettings();
        }
    }

    private void CustomPinyinPlacementCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CustomPinyinPlacementCombo.SelectedIndex >= 0)
        {
            CustomSettings.Current.RomanizationPlacement = (LyricElementPlacement)CustomPinyinPlacementCombo.SelectedIndex;
            CustomSettings.SaveSettings();
        }
    }

    private void CustomTransColorCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CustomTransColorCombo.SelectedItem is System.Windows.Controls.ComboBoxItem item && item.Tag is string hex)
        {
            CustomSettings.Current.TranslationColor = hex;
            CustomSettings.SaveSettings();
        }
    }

    private void CustomPinyinColorCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CustomPinyinColorCombo.SelectedItem is System.Windows.Controls.ComboBoxItem item && item.Tag is string hex)
        {
            CustomSettings.Current.RomanizationColor = hex;
            CustomSettings.SaveSettings();
        }
    }

    private void CustomAutoSkipVietnameseSwitch_Click(object sender, RoutedEventArgs e)
    {
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
