using System.Windows.Threading;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Custom.Settings;
using AFMediaBar.Custom.Translation;

namespace AFMediaBar.Custom.DualOverlay;

/// <summary>
/// Điều phối logic hiển thị đa dạng vị trí cho Lời dịch và Pinyin:
/// Cho phép linh hoạt cấu hình độc lập vị trí trên Taskbar chính hoặc Màn hình thứ 2.
/// </summary>
public sealed class DualOverlayCoordinator : IDisposable
{
    private readonly MediaSessionService _mediaSessionService;
    private readonly BilingualLyricsManager _bilingualLyricsManager;
    private readonly DualTaskbarPrimaryWindow _primaryWindow;
    private readonly DualTaskbarSecondaryWindow _secondaryWindow;
    private readonly CustomSettingsManager _settingsManager;

    private LyricDocument? _activeDocument;
    private bool _isCurrentTrackVietnamese;
    private readonly DispatcherTimer _syncTimer;

    public DualOverlayCoordinator(
        MediaSessionService mediaSessionService,
        BilingualLyricsManager bilingualLyricsManager,
        DualTaskbarPrimaryWindow primaryWindow,
        DualTaskbarSecondaryWindow secondaryWindow,
        CustomSettingsManager settingsManager)
    {
        _mediaSessionService = mediaSessionService;
        _bilingualLyricsManager = bilingualLyricsManager;
        _primaryWindow = primaryWindow;
        _secondaryWindow = secondaryWindow;
        _settingsManager = settingsManager;

        _syncTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _syncTimer.Tick += SyncTimer_Tick;

        _mediaSessionService.SnapshotChanged += MediaSessionService_SnapshotChanged;
        _bilingualLyricsManager.BilingualLyricsReady += BilingualLyricsManager_BilingualLyricsReady;

        _primaryWindow.Show();
        _secondaryWindow.Show();
        _syncTimer.Start();
    }

    private void MediaSessionService_SnapshotChanged(object? sender, MediaSnapshot snapshot)
    {
        if (!snapshot.IsConnected || !snapshot.IsPlaying || snapshot.Lyrics == null || snapshot.Lyrics.Document.Lines.Count == 0)
        {
            _activeDocument = null;
            _isCurrentTrackVietnamese = false;
            HideAllWindows();
            return;
        }

        var trackKey = $"{snapshot.Artist}_{snapshot.Title}_{snapshot.Duration}";
        _bilingualLyricsManager.ProcessLyrics(trackKey, snapshot.Lyrics.Document);

        _activeDocument ??= snapshot.Lyrics.Document;
    }

    private void BilingualLyricsManager_BilingualLyricsReady(LyricDocument enrichedDoc, bool isVietnamese)
    {
        _activeDocument = enrichedDoc;
        _isCurrentTrackVietnamese = isVietnamese;
    }

    private void SyncTimer_Tick(object? sender, EventArgs e)
    {
        var settings = _settingsManager.Current;
        if (!settings.EnableDualTaskbarLyrics)
        {
            HideAllWindows();
            return;
        }

        var snapshot = _mediaSessionService.CurrentSnapshot;
        if (snapshot == null || !snapshot.IsConnected || !snapshot.IsPlaying || _activeDocument == null)
        {
            HideAllWindows();
            return;
        }

        var currentSeconds = snapshot.Position;
        var lines = _activeDocument.Lines;
        if (lines.Count == 0)
        {
            HideAllWindows();
            return;
        }

        LyricLine? matchedLine = null;
        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (currentSeconds >= line.Start && currentSeconds <= line.End)
            {
                matchedLine = line;
                break;
            }
            if (currentSeconds < line.Start)
            {
                if (i > 0) matchedLine = lines[i - 1];
                break;
            }
        }
        matchedLine ??= (currentSeconds >= lines[^1].Start ? lines[^1] : null);

        if (matchedLine == null)
        {
            HideAllWindows();
            return;
        }

        string? translation = matchedLine.Translation;
        string? pinyin = matchedLine.Romanization;

        // Bỏ qua dịch nếu bài hát là Tiếng Việt
        if (_isCurrentTrackVietnamese && settings.AutoSkipVietnamese)
        {
            translation = null;
        }

        // Tự sinh pinyin dự phòng nếu dòng này chưa có
        if (string.IsNullOrWhiteSpace(pinyin) && PinyinGeneratorService.HasChinese(matchedLine.Text))
        {
            pinyin = PinyinGeneratorService.ToPinyin(matchedLine.Text);
        }

        DispatchContent(translation, pinyin, settings);
    }

    private void DispatchContent(string? translation, string? pinyin, CustomSettings settings)
    {
        // 1. Phân bổ cho Primary Taskbar Window
        bool showTransOnPrimary = settings.TranslationPlacement == LyricElementPlacement.PrimaryTaskbar ||
                                  settings.TranslationPlacement == LyricElementPlacement.BothScreens;

        bool showPinyinOnPrimary = settings.RomanizationPlacement == LyricElementPlacement.PrimaryTaskbar ||
                                   settings.RomanizationPlacement == LyricElementPlacement.BothScreens;

        string? primaryLine1 = null;
        string? primaryLine2 = null;

        if (showTransOnPrimary && !string.IsNullOrWhiteSpace(translation))
        {
            primaryLine1 = translation;
            if (showPinyinOnPrimary && !string.IsNullOrWhiteSpace(pinyin))
            {
                primaryLine2 = pinyin;
            }
        }
        else if (showPinyinOnPrimary && !string.IsNullOrWhiteSpace(pinyin))
        {
            primaryLine1 = pinyin;
        }

        if (!string.IsNullOrWhiteSpace(primaryLine1))
        {
            _primaryWindow.UpdateContent(primaryLine1, primaryLine2, isPrimaryPinyin: (primaryLine1 == pinyin));
        }
        else
        {
            _primaryWindow.HideContent();
        }

        // 2. Phân bổ cho Secondary Screen Window
        bool showTransOnSec = settings.TranslationPlacement == LyricElementPlacement.SecondaryScreen ||
                              settings.TranslationPlacement == LyricElementPlacement.BothScreens;

        bool showPinyinOnSec = settings.RomanizationPlacement == LyricElementPlacement.SecondaryScreen ||
                               settings.RomanizationPlacement == LyricElementPlacement.BothScreens;

        string? secLine1 = null;
        string? secLine2 = null;

        if (showTransOnSec && !string.IsNullOrWhiteSpace(translation))
        {
            secLine1 = translation;
            if (showPinyinOnSec && !string.IsNullOrWhiteSpace(pinyin))
            {
                secLine2 = pinyin;
            }
        }
        else if (showPinyinOnSec && !string.IsNullOrWhiteSpace(pinyin))
        {
            secLine1 = pinyin;
        }

        if (!string.IsNullOrWhiteSpace(secLine1))
        {
            _secondaryWindow.UpdateContent(secLine1, secLine2, isPrimaryPinyin: (secLine1 == pinyin));
        }
        else
        {
            _secondaryWindow.HideContent();
        }
    }

    private void HideAllWindows()
    {
        _primaryWindow.HideContent();
        _secondaryWindow.HideContent();
    }

    public void Dispose()
    {
        _syncTimer.Stop();
        _mediaSessionService.SnapshotChanged -= MediaSessionService_SnapshotChanged;
        _bilingualLyricsManager.BilingualLyricsReady -= BilingualLyricsManager_BilingualLyricsReady;
        _primaryWindow.Close();
        _secondaryWindow.Close();
    }
}
