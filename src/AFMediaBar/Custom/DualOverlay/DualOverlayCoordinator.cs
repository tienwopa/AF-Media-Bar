using System.Windows.Threading;
using AFMediaBar.Classes.Interop;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Custom.Settings;
using AFMediaBar.Custom.Translation;

namespace AFMediaBar.Custom.DualOverlay;

/// <summary>
/// Điều phối logic hiển thị song ngữ & Dual Taskbar Native.
/// Tự động tách hoặc gộp lời dịch tiếng Việt và Pinyin trên thanh Taskbar theo cấu hình và số lượng màn hình.
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

        // 1. Trich xuat ban dich va pinyin
        string? translation = matchedLine.Translation;
        string? pinyin = matchedLine.Romanization;

        // Neu bai hat goc da la tieng Viet va co bat AutoSkipVietnamese -> An ban dich
        if (_isCurrentTrackVietnamese && settings.AutoSkipVietnamese)
        {
            translation = null;
        }

        // 2. Kiem tra xem may co man hinh thu 2 khong
        bool hasSecondaryScreen = NativeMethods.FindWindow("Shell_SecondaryTrayWnd", null) != IntPtr.Zero;

        // 3. Ap dung che do hien thi
        switch (settings.ContentMode)
        {
            case SecondaryLyricsContentMode.TranslationOnly:
                _primaryWindow.UpdateContent(translation ?? string.Empty, null);
                _secondaryWindow.HideContent();
                break;

            case SecondaryLyricsContentMode.RomanizationOnly:
                _primaryWindow.UpdateContent(pinyin ?? string.Empty, null);
                _secondaryWindow.HideContent();
                break;

            case SecondaryLyricsContentMode.Both:
            default:
                DispatchBoth(translation, pinyin, hasSecondaryScreen, settings.PlacementStrategy);
                break;
        }
    }

    private void DispatchBoth(
        string? translation,
        string? pinyin,
        bool hasSecondaryScreen,
        DualLyricsPlacementStrategy strategy)
    {
        bool hasTrans = !string.IsNullOrWhiteSpace(translation);
        bool hasPin = !string.IsNullOrWhiteSpace(pinyin);

        // Truong hop chi co 1 trong 2 noi dung
        if (hasTrans && !hasPin)
        {
            _primaryWindow.UpdateContent(translation!, null);
            _secondaryWindow.HideContent();
            return;
        }
        if (!hasTrans && hasPin)
        {
            _primaryWindow.UpdateContent(pinyin!, null);
            _secondaryWindow.HideContent();
            return;
        }
        if (!hasTrans && !hasPin)
        {
            HideAllWindows();
            return;
        }

        // Truong hop co ca ban dich va pinyin:
        if (strategy == DualLyricsPlacementStrategy.AutoSplit && hasSecondaryScreen)
        {
            // TACH: Taskbar chinh hien loi dich, Taskbar man hinh 2 hien Pinyin
            _primaryWindow.UpdateContent(translation!, null);
            _secondaryWindow.UpdateContent(pinyin!);
        }
        else
        {
            // GOP LAI tai Taskbar chinh (Dòng trên: Lời dịch, Dòng dưới: Pinyin)
            _primaryWindow.UpdateContent(translation!, pinyin);
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
