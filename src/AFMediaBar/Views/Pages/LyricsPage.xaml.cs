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
        CustomContentModeCombo.SelectedIndex = (int)settings.ContentMode;
        CustomPlacementCombo.SelectedIndex = (int)settings.PlacementStrategy;
        CustomAutoSkipVietnameseSwitch.IsChecked = settings.AutoSkipVietnamese;
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

    private void CustomContentModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CustomContentModeCombo.SelectedIndex >= 0)
        {
            CustomSettings.Current.ContentMode = (SecondaryLyricsContentMode)CustomContentModeCombo.SelectedIndex;
            CustomSettings.SaveSettings();
        }
    }

    private void CustomPlacementCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CustomPlacementCombo.SelectedIndex >= 0)
        {
            CustomSettings.Current.PlacementStrategy = (DualLyricsPlacementStrategy)CustomPlacementCombo.SelectedIndex;
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
