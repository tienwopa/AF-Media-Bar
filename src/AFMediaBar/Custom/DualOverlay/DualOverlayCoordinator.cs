using System.Windows.Threading;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Media;
using AFMediaBar.Custom.Settings;
using AFMediaBar.Custom.Translation;

namespace AFMediaBar.Custom.DualOverlay;

/// <summary>
/// Điều phối chính cho tính năng Dual Overlay và Song ngữ.
/// Lắng nghe MediaSessionService và BilingualLyricsManager để đồng bộ lời dịch theo thời gian thực.
/// </summary>
public sealed class DualOverlayCoordinator : IDisposable
{
    private readonly MediaSessionService _mediaSessionService;
    private readonly BilingualLyricsManager _bilingualLyricsManager;
    private readonly DualOverlayViewModel _overlayViewModel;
    private readonly DualOverlayWindow _overlayWindow;
    private readonly CustomSettingsManager _settingsManager;

    private LyricDocument? _activeDocument;
    private readonly DispatcherTimer _syncTimer;

    public DualOverlayCoordinator(
        MediaSessionService mediaSessionService,
        BilingualLyricsManager bilingualLyricsManager,
        DualOverlayViewModel overlayViewModel,
        DualOverlayWindow overlayWindow,
        CustomSettingsManager settingsManager)
    {
        _mediaSessionService = mediaSessionService;
        _bilingualLyricsManager = bilingualLyricsManager;
        _overlayViewModel = overlayViewModel;
        _overlayWindow = overlayWindow;
        _settingsManager = settingsManager;

        // Timer dong bo vi tri mili-giay voi do tre cuc thap
        _syncTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _syncTimer.Tick += SyncTimer_Tick;

        _mediaSessionService.SnapshotChanged += MediaSessionService_SnapshotChanged;
        _bilingualLyricsManager.BilingualLyricsReady += BilingualLyricsManager_BilingualLyricsReady;

        // Hien thi cua so
        if (_settingsManager.Current.EnableDualOverlay)
        {
            _overlayWindow.Show();
            _syncTimer.Start();
        }
    }

    private void MediaSessionService_SnapshotChanged(object? sender, MediaSnapshot snapshot)
    {
        if (!snapshot.IsConnected || !snapshot.IsPlaying || snapshot.Lyrics == null || snapshot.Lyrics.Document.Lines.Count == 0)
        {
            _activeDocument = null;
            _overlayViewModel.Hide();
            return;
        }

        // Gui document goc sang BilingualLyricsManager de bat dau dich ngam neu can
        var trackKey = $"{snapshot.Artist}_{snapshot.Title}_{snapshot.Duration.TotalSeconds}";
        _bilingualLyricsManager.ProcessLyrics(trackKey, snapshot.Lyrics.Document);

        // Tam thoi dung document goc neu chua co ban dich
        _activeDocument ??= snapshot.Lyrics.Document;
    }

    private void BilingualLyricsManager_BilingualLyricsReady(LyricDocument enrichedDoc)
    {
        // Khi ban dich song ngu da san sang, cap nhat document hoat dong
        _activeDocument = enrichedDoc;
    }

    private void SyncTimer_Tick(object? sender, EventArgs e)
    {
        if (!_settingsManager.Current.EnableDualOverlay)
        {
            _overlayViewModel.Hide();
            return;
        }

        var snapshot = _mediaSessionService.CurrentSnapshot;
        if (snapshot == null || !snapshot.IsConnected || !snapshot.IsPlaying || _activeDocument == null)
        {
            _overlayViewModel.Hide();
            return;
        }

        var currentSeconds = snapshot.Position.TotalSeconds;
        var lines = _activeDocument.Lines;
        if (lines.Count == 0)
        {
            return;
        }

        // Tim dong lyric phu hop voi thoi diem hien tai
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
                // Neu chua toi dong tiep theo, giu dong truoc do
                if (i > 0)
                {
                    matchedLine = lines[i - 1];
                }
                break;
            }
        }

        matchedLine ??= (currentSeconds >= lines[^1].Start ? lines[^1] : null);

        if (matchedLine != null)
        {
            _overlayViewModel.UpdateLyric(matchedLine.Text, matchedLine.Translation);
        }
        else
        {
            _overlayViewModel.Hide();
        }
    }

    public void Dispose()
    {
        _syncTimer.Stop();
        _mediaSessionService.SnapshotChanged -= MediaSessionService_SnapshotChanged;
        _bilingualLyricsManager.BilingualLyricsReady -= BilingualLyricsManager_BilingualLyricsReady;
        _overlayWindow.Close();
    }
}
