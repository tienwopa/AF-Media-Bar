using System;
using System.Windows.Threading;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Lyrics;
using AFMediaBar.Custom.Settings;
using AFMediaBar.Custom.Translation;

namespace AFMediaBar.Custom.FloatingLyrics;

/// <summary>
/// Điều phối logic hoạt động của Bảng lời bài hát nổi (Floating Lyrics Window):
/// - Lắng nghe thông tin bài hát từ MediaSessionService.
/// - Nhận diện bài hát Tiếng Việt hoặc làm giàu dữ liệu Pinyin/Dịch từ BilingualLyricsManager.
/// - Đồng bộ vị trí thời gian thực và cập nhật hiệu ứng Karaoke 25 FPS (40ms).
/// - Hỗ trợ nhảy nhanh bài hát khi người dùng nhấp vào từng câu lyrics.
/// </summary>
public sealed class FloatingLyricsCoordinator : IDisposable
{
    private readonly MediaSessionService _mediaSessionService;
    private readonly BilingualLyricsManager _bilingualLyricsManager;
    private readonly FloatingLyricsWindow _window;
    private readonly CustomSettingsManager _settingsManager;
    private readonly DispatcherTimer _syncTimer;

    private LyricDocument? _activeDocument;
    private string? _activeTrackKey;
    private bool _isCurrentTrackVietnamese;

    public static FloatingLyricsCoordinator? Instance { get; private set; }

    public FloatingLyricsWindow Window => _window;

    public FloatingLyricsCoordinator(
        MediaSessionService mediaSessionService,
        BilingualLyricsManager bilingualLyricsManager,
        FloatingLyricsWindow window,
        CustomSettingsManager settingsManager)
    {
        Instance = this;
        _mediaSessionService = mediaSessionService;
        _bilingualLyricsManager = bilingualLyricsManager;
        _window = window;
        _settingsManager = settingsManager;

        _syncTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(40)
        };
        _syncTimer.Tick += SyncTimer_Tick;

        _mediaSessionService.SnapshotChanged += MediaSessionService_SnapshotChanged;
        _bilingualLyricsManager.BilingualLyricsReady += BilingualLyricsManager_BilingualLyricsReady;
        _window.SeekRequested += Window_SeekRequested;

        if (_settingsManager.Current.EnableFloatingLyrics)
        {
            _window.Show();
        }

        // Khởi tạo trạng thái ban đầu nếu đang có nhạc phát
        var snapshot = _mediaSessionService.CurrentSnapshot;
        if (snapshot != null && snapshot.IsConnected)
        {
            HandleSnapshotUpdate(snapshot);
        }
    }

    public void ToggleWindow()
    {
        _window.ToggleVisibility();
        if (_window.IsVisible)
        {
            var snapshot = _mediaSessionService.CurrentSnapshot;
            if (snapshot != null && snapshot.IsConnected && snapshot.IsPlaying)
            {
                if (!_syncTimer.IsEnabled) _syncTimer.Start();
            }
        }
        else
        {
            _syncTimer.Stop();
        }
    }

    private void Window_SeekRequested(double positionSeconds)
    {
        _ = _mediaSessionService.SeekAsync(positionSeconds);
    }

    private void MediaSessionService_SnapshotChanged(object? sender, MediaSnapshot snapshot)
    {
        HandleSnapshotUpdate(snapshot);
    }

    private void HandleSnapshotUpdate(MediaSnapshot snapshot)
    {
        if (!snapshot.IsConnected)
        {
            _activeDocument = null;
            _activeTrackKey = null;
            _isCurrentTrackVietnamese = false;
            _syncTimer.Stop();
            _window.UpdateTrackInfo(string.Empty, string.Empty);
            _window.SetLyrics(null, false);
            return;
        }

        _window.UpdateTrackInfo(snapshot.Title, snapshot.Artist);

        var trackKey = $"{snapshot.Artist}_{snapshot.Title}_{snapshot.Duration}";
        if (!string.Equals(_activeTrackKey, trackKey, StringComparison.Ordinal))
        {
            _activeTrackKey = trackKey;
            _activeDocument = null;
            _isCurrentTrackVietnamese = false;
        }

        if (snapshot.Lyrics?.Document.Lines.Count is not > 0)
        {
            if (_activeDocument == null)
            {
                _window.SetLyrics(null, false);
                if (!snapshot.IsPlaying) _syncTimer.Stop();
            }
            return;
        }

        _bilingualLyricsManager.ProcessLyrics(trackKey, snapshot.Lyrics.Document);

        var newDoc = string.Equals(BilingualLyricsManager.ActiveTrackKey, trackKey, StringComparison.Ordinal)
            ? BilingualLyricsManager.ActiveEnrichedDocument ?? snapshot.Lyrics.Document
            : snapshot.Lyrics.Document;

        bool isVietnamese = (string.Equals(BilingualLyricsManager.ActiveTrackKey, trackKey, StringComparison.Ordinal) && BilingualLyricsManager.ActiveIsVietnamese)
            || (newDoc?.Lines != null && BilingualLyricsManager.IsVietnameseContent(newDoc.Lines));

        if (!ReferenceEquals(_activeDocument, newDoc) || _isCurrentTrackVietnamese != isVietnamese)
        {
            _activeDocument = newDoc;
            _isCurrentTrackVietnamese = isVietnamese;
            _window.SetLyrics(_activeDocument, _isCurrentTrackVietnamese);
        }

        if (snapshot.IsPlaying && _window.IsVisible)
        {
            if (!_syncTimer.IsEnabled)
            {
                _syncTimer.Start();
            }
        }
        else
        {
            _syncTimer.Stop();
        }
    }

    private void BilingualLyricsManager_BilingualLyricsReady(LyricDocument enrichedDoc, bool isVietnamese)
    {
        var snapshot = _mediaSessionService.CurrentSnapshot;
        if (snapshot == null || !string.Equals(_activeTrackKey, BilingualLyricsManager.ActiveTrackKey, StringComparison.Ordinal))
        {
            return;
        }

        _activeDocument = enrichedDoc;
        _isCurrentTrackVietnamese = isVietnamese;
        _window.SetLyrics(_activeDocument, isVietnamese);
    }

    private void SyncTimer_Tick(object? sender, EventArgs e)
    {
        if (!_window.IsVisible)
        {
            _syncTimer.Stop();
            return;
        }

        var snapshot = _mediaSessionService.CurrentSnapshot;
        if (snapshot == null || !snapshot.IsConnected || !snapshot.IsPlaying || _activeDocument == null)
        {
            _syncTimer.Stop();
            return;
        }

        var now = DateTimeOffset.Now;
        var currentSeconds = TaskbarExperiencePolicy.GetPosition(snapshot, now);

        _window.UpdatePlaybackPosition(currentSeconds, snapshot.IsPlaying);
    }

    public void Dispose()
    {
        _syncTimer.Stop();
        _mediaSessionService.SnapshotChanged -= MediaSessionService_SnapshotChanged;
        _bilingualLyricsManager.BilingualLyricsReady -= BilingualLyricsManager_BilingualLyricsReady;
        _window.SeekRequested -= Window_SeekRequested;
    }
}
