using CommunityToolkit.Mvvm.ComponentModel;
using AFMediaBar.Custom.Settings;

namespace AFMediaBar.Custom.DualOverlay;

/// <summary>
/// ViewModel quản lý dữ liệu hiển thị cho cửa sổ Dual Overlay.
/// </summary>
public partial class DualOverlayViewModel : ObservableObject
{
    private readonly CustomSettingsManager _settingsManager;

    [ObservableProperty]
    private string _originalLyric = string.Empty;

    [ObservableProperty]
    private string _translatedLyric = string.Empty;

    [ObservableProperty]
    private bool _isVisible = false;

    [ObservableProperty]
    private bool _onlyShowsTranslation = true;

    [ObservableProperty]
    private double _fontSize = 12.0;

    [ObservableProperty]
    private double _opacity = 0.95;

    public DualOverlayViewModel(CustomSettingsManager settingsManager)
    {
        _settingsManager = settingsManager;
        UpdateFromSettings(settingsManager.Current);
        _settingsManager.SettingsChanged += UpdateFromSettings;
    }

    private void UpdateFromSettings(CustomSettings settings)
    {
        FontSize = settings.FontSize;
        Opacity = settings.Opacity;
        OnlyShowsTranslation = settings.DualOverlayOnlyShowsTranslation;

        if (!settings.EnableDualOverlay)
        {
            IsVisible = false;
        }
    }

    /// <summary>
    /// Cập nhật nội dung câu lyric đang phát.
    /// </summary>
    public void UpdateLyric(string original, string? translated)
    {
        OriginalLyric = original;
        TranslatedLyric = string.IsNullOrWhiteSpace(translated) ? original : translated;
        IsVisible = _settingsManager.Current.EnableDualOverlay && !string.IsNullOrWhiteSpace(TranslatedLyric);
    }

    public void Hide()
    {
        IsVisible = false;
        OriginalLyric = string.Empty;
        TranslatedLyric = string.Empty;
    }
}
