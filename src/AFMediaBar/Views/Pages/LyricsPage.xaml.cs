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
        CustomSettings = customSettings ?? CustomSettingsManager.Instance;
        DataContext = this;
        InitializeComponent();

        CustomGeminiApiKeyBox.TextChanged += CustomGeminiApiKeyBox_TextChanged;
        CustomOpenAIBaseUrlBox.TextChanged += CustomOpenAIBaseUrlBox_TextChanged;
        CustomOpenAIApiKeyBox.TextChanged += CustomOpenAIApiKeyBox_TextChanged;
        CustomOpenAIModelBox.TextChanged += CustomOpenAIModelBox_TextChanged;

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

            CustomBackgroundEffectCombo.SelectedIndex = (int)settings.BackgroundEffect;
            CustomBackgroundScopeCombo.SelectedIndex = (int)settings.BackgroundEffectScope;
            CustomBackgroundDensityCombo.SelectedIndex = (int)settings.BackgroundEffectDensity;
            CustomBackgroundSpeedCombo.SelectedIndex = (int)settings.BackgroundEffectSpeed;
            SelectOpacityInCombo(CustomBackgroundOpacityCombo, settings.BackgroundEffectOpacity);
            CustomBackgroundEffectSubPanel.Visibility = settings.BackgroundEffect == LyricsBackgroundEffectType.None
                ? Visibility.Collapsed
                : Visibility.Visible;

            SelectMaxWidthInCombo(CustomPrimaryMaxWidthCombo, settings.PrimaryMaxWidth);

            TranslationOffsetMs = settings.TranslationOffsetMs;
            RomanizationOffsetMs = settings.RomanizationOffsetMs;

            CustomMediaBarPrimaryCombo.SelectedIndex = (int)settings.MediaBarPrimaryContent;
            CustomVietnameseSecondaryCombo.SelectedIndex = (int)settings.VietnameseSecondaryMode;
            CustomForeignSecondaryCombo.SelectedIndex = (int)settings.ForeignSecondaryMode;
            CustomForeignCompanionCombo.SelectedIndex = (int)settings.ForeignNextLineCompanion;
            CustomLineOrderCombo.SelectedIndex = (int)settings.LineOrder;
            SelectFontSizeInCombo(CustomTransFontSizeCombo, settings.TranslationFontSize);
            SelectFontSizeInCombo(CustomPinyinFontSizeCombo, settings.RomanizationFontSize);
            CustomFontWeightCombo.SelectedIndex = (int)settings.WeightEmphasis;
            CustomKaraokeTargetCombo.SelectedIndex = (int)settings.KaraokeTarget;

            SelectColorInCombo(CustomTransColorCombo, settings.TranslationColor);
            SelectColorInCombo(CustomPinyinColorCombo, settings.RomanizationColor);

            CustomTranslationProviderCombo.SelectedIndex = (int)settings.TranslationProvider;
            CustomGeminiApiKeyBox.Text = settings.GeminiApiKey ?? string.Empty;
            SelectGeminiModelInCombo(CustomGeminiModelCombo, settings.GeminiModel);
            CustomOpenAIBaseUrlBox.Text = settings.OpenAIBaseUrl ?? string.Empty;
            CustomOpenAIApiKeyBox.Text = settings.OpenAIApiKey ?? string.Empty;
            CustomOpenAIModelBox.Text = settings.OpenAIModel ?? string.Empty;

            UpdateProviderPanelsVisibility(settings.TranslationProvider);
        }
        finally
        {
            _isInitializing = false;
        }
    }

    private void UpdateProviderPanelsVisibility(TranslationProviderType provider)
    {
        CustomGeminiPanel.Visibility = provider == TranslationProviderType.GeminiAI ? Visibility.Visible : Visibility.Collapsed;
        CustomOpenAIPanel.Visibility = provider == TranslationProviderType.OpenAICompatible ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void SelectGeminiModelInCombo(System.Windows.Controls.ComboBox combo, string? model)
    {
        if (string.IsNullOrEmpty(model)) { combo.SelectedIndex = 0; return; }
        for (int i = 0; i < combo.Items.Count; i++)
        {
            if (combo.Items[i] is System.Windows.Controls.ComboBoxItem item &&
                string.Equals(item.Tag?.ToString(), model, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedIndex = i;
                return;
            }
        }
        if (combo.Items.Count > 0) combo.SelectedIndex = 0;
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

    private static void SelectOpacityInCombo(System.Windows.Controls.ComboBox combo, double opacity)
    {
        string target = opacity.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        string shortTarget = opacity.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        for (int i = 0; i < combo.Items.Count; i++)
        {
            if (combo.Items[i] is System.Windows.Controls.ComboBoxItem item && item.Tag is string tagStr &&
                (string.Equals(tagStr, target, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(tagStr, shortTarget, StringComparison.OrdinalIgnoreCase)))
            {
                combo.SelectedIndex = i;
                return;
            }
        }
        if (combo.Items.Count > 2) combo.SelectedIndex = 2; // Default 65%
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

    private void CustomBackgroundEffectCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        if (CustomBackgroundEffectCombo.SelectedItem is System.Windows.Controls.ComboBoxItem item &&
            int.TryParse(item.Tag?.ToString(), out int val))
        {
            CustomSettings.Current.BackgroundEffect = (LyricsBackgroundEffectType)val;
            CustomBackgroundEffectSubPanel.Visibility = CustomSettings.Current.BackgroundEffect == LyricsBackgroundEffectType.None
                ? Visibility.Collapsed
                : Visibility.Visible;
            CustomSettings.SaveSettings();
        }
    }

    private void CustomBackgroundScopeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        if (CustomBackgroundScopeCombo.SelectedItem is System.Windows.Controls.ComboBoxItem item &&
            int.TryParse(item.Tag?.ToString(), out int val))
        {
            CustomSettings.Current.BackgroundEffectScope = (LyricsEffectScope)val;
            CustomSettings.SaveSettings();
        }
    }

    private void CustomBackgroundDensityCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        if (CustomBackgroundDensityCombo.SelectedItem is System.Windows.Controls.ComboBoxItem item &&
            int.TryParse(item.Tag?.ToString(), out int val))
        {
            CustomSettings.Current.BackgroundEffectDensity = (LyricsEffectDensity)val;
            CustomSettings.SaveSettings();
        }
    }

    private void CustomBackgroundSpeedCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        if (CustomBackgroundSpeedCombo.SelectedItem is System.Windows.Controls.ComboBoxItem item &&
            int.TryParse(item.Tag?.ToString(), out int val))
        {
            CustomSettings.Current.BackgroundEffectSpeed = (LyricsEffectSpeed)val;
            CustomSettings.SaveSettings();
        }
    }

    private void CustomBackgroundOpacityCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        if (CustomBackgroundOpacityCombo.SelectedItem is System.Windows.Controls.ComboBoxItem item &&
            double.TryParse(item.Tag?.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double val))
        {
            CustomSettings.Current.BackgroundEffectOpacity = val;
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

    
    private void CustomMediaBarPrimaryCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        if (CustomMediaBarPrimaryCombo.SelectedIndex >= 0)
        {
            CustomSettings.Current.MediaBarPrimaryContent = (MediaBarLyricsDisplayMode)CustomMediaBarPrimaryCombo.SelectedIndex;
            CustomSettings.SaveSettings();
        }
    }

    private void CustomVietnameseSecondaryCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        if (CustomVietnameseSecondaryCombo.SelectedIndex >= 0)
        {
            CustomSettings.Current.VietnameseSecondaryMode = (VietnameseSecondaryMode)CustomVietnameseSecondaryCombo.SelectedIndex;
            CustomSettings.SaveSettings();
        }
    }

    private void CustomForeignSecondaryCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        if (CustomForeignSecondaryCombo.SelectedIndex >= 0)
        {
            CustomSettings.Current.ForeignSecondaryMode = (ForeignSecondaryMode)CustomForeignSecondaryCombo.SelectedIndex;
            CustomSettings.SaveSettings();
        }
    }

    private void CustomForeignCompanionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        if (CustomForeignCompanionCombo.SelectedIndex >= 0)
        {
            CustomSettings.Current.ForeignNextLineCompanion = (ForeignNextLineCompanion)CustomForeignCompanionCombo.SelectedIndex;
            CustomSettings.SaveSettings();
        }
    }

    private void CustomAutoSkipVietnameseSwitch_Click(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        CustomSettings.Current.AutoSkipVietnamese = CustomAutoSkipVietnameseSwitch.IsChecked ?? true;
        CustomSettings.SaveSettings();
    }

    private void CustomTranslationProviderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        if (CustomTranslationProviderCombo.SelectedIndex >= 0)
        {
            var provider = (TranslationProviderType)CustomTranslationProviderCombo.SelectedIndex;
            CustomSettings.Current.TranslationProvider = provider;
            CustomSettings.SaveSettings();
            UpdateProviderPanelsVisibility(provider);
        }
    }

    private void CustomGeminiApiKeyBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isInitializing) return;
        CustomSettings.Current.GeminiApiKey = CustomGeminiApiKeyBox.Text?.Trim() ?? string.Empty;
        CustomSettings.SaveSettings();
    }

    private void CustomGeminiModelCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        if (CustomGeminiModelCombo.SelectedItem is System.Windows.Controls.ComboBoxItem item && item.Tag is string model)
        {
            CustomSettings.Current.GeminiModel = model;
            CustomSettings.SaveSettings();
        }
    }

    private void CustomOpenAIBaseUrlBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isInitializing) return;
        CustomSettings.Current.OpenAIBaseUrl = CustomOpenAIBaseUrlBox.Text?.Trim() ?? string.Empty;
        CustomSettings.SaveSettings();
    }

    private void CustomOpenAIApiKeyBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isInitializing) return;
        CustomSettings.Current.OpenAIApiKey = CustomOpenAIApiKeyBox.Text?.Trim() ?? string.Empty;
        CustomSettings.SaveSettings();
    }

    private void CustomOpenAIModelBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isInitializing) return;
        CustomSettings.Current.OpenAIModel = CustomOpenAIModelBox.Text?.Trim() ?? string.Empty;
        CustomSettings.SaveSettings();
    }

    private async void CustomTestGeminiBtn_Click(object sender, RoutedEventArgs e)
    {
        var apiKey = CustomGeminiApiKeyBox.Text?.Trim();
        if (string.IsNullOrEmpty(apiKey))
        {
            CustomTestGeminiBtn.Content = "Chưa nhập API Key!";
            return;
        }

        CustomTestGeminiBtn.IsEnabled = false;
        CustomTestGeminiBtn.Content = "Đang kiểm tra...";

        try
        {
            var selectedTag = CustomGeminiModelCombo.SelectedItem is System.Windows.Controls.ComboBoxItem item && item.Tag is string m ? m : "gemini-3.5-flash";
            var model = (selectedTag.StartsWith("gemini-1.5", StringComparison.OrdinalIgnoreCase) ||
                         selectedTag.StartsWith("gemini-2.0", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(selectedTag, "gemini-2.5-flash", StringComparison.OrdinalIgnoreCase))
                ? "gemini-3.5-flash"
                : selectedTag;
            using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";
            var req = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new[]
                        {
                            new { text = "Translate 'Hello' to Vietnamese. Return only the single word translation without punctuation or markdown." }
                        }
                    }
                }
            };
            var content = new System.Net.Http.StringContent(System.Text.Json.JsonSerializer.Serialize(req), System.Text.Encoding.UTF8, "application/json");
            using var resp = await client.PostAsync(url, content);
            if (resp.IsSuccessStatusCode)
            {
                CustomTestGeminiBtn.Content = "✓ Kết nối thành công!";
            }
            else
            {
                CustomTestGeminiBtn.Content = $"Lỗi {(int)resp.StatusCode}: {resp.ReasonPhrase}";
            }
        }
        catch (Exception ex)
        {
            CustomTestGeminiBtn.Content = $"Thất bại: {ex.Message}";
        }
        finally
        {
            CustomTestGeminiBtn.IsEnabled = true;
        }
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
