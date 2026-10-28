using System.Windows.Threading;
using AFMediaBar.Classes.Interop;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Custom.Settings;
using AFMediaBar.Custom.Translation;

namespace AFMediaBar.Custom.DualOverlay;

/// <summary>
/// Điều phối logic hiển thị đa dạng vị trí cho Lời dịch và Pinyin:
/// - Tính toán tiến độ Karaoke (chạy chữ) siêu mượt 25 FPS (40ms).
/// - Hỗ trợ bù trừ thời gian (Time Offset sớm/trễ) độc lập cho Lời dịch và Pinyin.
/// - Tự động phát hiện màn hình thứ 2 và gộp an toàn về Taskbar chính nếu chỉ có 1 màn hình.
/// - Đồng bộ bộ nhớ đệm giữ nguyên Pinyin không bị biến mất.
/// </summary>
public sealed class DualOverlayCoordinator : IDisposable
{
    private const int SM_CMONITORS = 80;

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

        // Chu kỳ 40ms (~25 FPS) để hiệu ứng Karaoke chạy chữ mượt mà nhất
        _syncTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(40)
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

    private static LyricLine? MatchLineAt(IReadOnlyList<LyricLine> lines, double seconds)
    {
        if (lines.Count == 0) return null;

        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (seconds >= line.Start && seconds <= line.End)
            {
                return line;
            }
            if (seconds < line.Start)
            {
                return i > 0 ? lines[i - 1] : null;
            }
        }
        return seconds >= lines[^1].Start ? lines[^1] : null;
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

        // Áp dụng bù trừ thời gian hiển thị (Offset ms) cho Lời dịch và Pinyin
        double transSeconds = currentSeconds + (settings.TranslationOffsetMs / 1000.0);
        double pinyinSeconds = currentSeconds + (settings.RomanizationOffsetMs / 1000.0);

        var transLine = MatchLineAt(lines, transSeconds);
        var pinyinLine = MatchLineAt(lines, pinyinSeconds);

        if (transLine == null && pinyinLine == null)
        {
            HideAllWindows();
            return;
        }

        string? translation = transLine?.Translation;
        double transProgress = transLine != null ? CalculateKaraokeProgress(transLine, transSeconds) : 1.0;

        // Bỏ qua dịch nếu bài hát là Tiếng Việt
        if (_isCurrentTrackVietnamese && settings.AutoSkipVietnamese)
        {
            translation = null;
        }

        string? pinyin = pinyinLine?.Romanization;
        if (pinyinLine != null && string.IsNullOrWhiteSpace(pinyin) && PinyinGeneratorService.HasChinese(pinyinLine.Text))
        {
            pinyin = PinyinGeneratorService.ToPinyin(pinyinLine.Text);
        }
        double pinyinProgress = pinyinLine != null ? CalculateKaraokeProgress(pinyinLine, pinyinSeconds) : 1.0;

        // Điều phối nội dung tới Taskbar chính hoặc Màn hình 2
        DispatchContent(translation, transProgress, pinyin, pinyinProgress, settings);
    }

    /// <summary>
    /// Tính toán tiến độ quét Karaoke (từ 0.0 đến 1.0) dựa trên âm tiết (Words) hoặc thời lượng câu.
    /// </summary>
    private static double CalculateKaraokeProgress(LyricLine line, double currentSeconds)
    {
        if (currentSeconds <= line.Start)
        {
            return 0.0;
        }
        if (currentSeconds >= line.End)
        {
            return 1.0;
        }

        // Nếu bài hát có dữ liệu từng từ/âm tiết (SyllableSynced / QRC / YRC)
        if (line.Words != null && line.Words.Count > 0)
        {
            var words = line.Words;
            int n = words.Count;

            if (currentSeconds <= words[0].Start)
            {
                return 0.0;
            }
            if (currentSeconds >= words[^1].End)
            {
                return 1.0;
            }

            for (int i = 0; i < n; i++)
            {
                var w = words[i];
                if (currentSeconds >= w.Start && currentSeconds < w.End)
                {
                    double wDur = Math.Max(0.01, w.End - w.Start);
                    double wFrac = Math.Clamp((currentSeconds - w.Start) / wDur, 0.0, 1.0);
                    return (i + wFrac) / n;
                }
                if (currentSeconds >= w.End)
                {
                    if (i == n - 1 || currentSeconds < words[i + 1].Start)
                    {
                        return (i + 1.0) / n;
                    }
                }
            }
        }

        // Dự phòng: Nội suy mượt theo tỷ lệ thời lượng dòng hát (LineSynced)
        double lineDuration = Math.Max(0.5, line.End - line.Start);
        return Math.Clamp((currentSeconds - line.Start) / lineDuration, 0.0, 1.0);
    }

    private void DispatchContent(
        string? translation, double transProgress,
        string? pinyin, double pinyinProgress,
        CustomSettings settings)
    {
        // Kiểm tra xem máy tính có màn hình thứ 2 hay không
        bool hasSecondaryScreen = NativeMethods.GetSystemMetrics(SM_CMONITORS) > 1;

        var transPlacement = settings.TranslationPlacement;
        var pinyinPlacement = settings.RomanizationPlacement;

        // Nếu KHÔNG có màn hình thứ 2, tự động gộp các thành phần về Taskbar chính
        if (!hasSecondaryScreen)
        {
            if (transPlacement == LyricElementPlacement.SecondaryScreen)
            {
                transPlacement = LyricElementPlacement.PrimaryTaskbar;
            }
            if (pinyinPlacement == LyricElementPlacement.SecondaryScreen)
            {
                pinyinPlacement = LyricElementPlacement.PrimaryTaskbar;
            }
        }

        // 1. Phân bổ cho Primary Taskbar Window
        bool showTransOnPrimary = transPlacement == LyricElementPlacement.PrimaryTaskbar ||
                                  transPlacement == LyricElementPlacement.BothScreens;

        bool showPinyinOnPrimary = pinyinPlacement == LyricElementPlacement.PrimaryTaskbar ||
                                   pinyinPlacement == LyricElementPlacement.BothScreens;

        string? primaryLine1 = null;
        string? primaryLine2 = null;
        double primaryProgress1 = 1.0;
        double primaryProgress2 = 1.0;

        if (showTransOnPrimary && !string.IsNullOrWhiteSpace(translation))
        {
            primaryLine1 = translation;
            primaryProgress1 = transProgress;
            if (showPinyinOnPrimary && !string.IsNullOrWhiteSpace(pinyin))
            {
                primaryLine2 = pinyin;
                primaryProgress2 = pinyinProgress;
            }
        }
        else if (showPinyinOnPrimary && !string.IsNullOrWhiteSpace(pinyin))
        {
            primaryLine1 = pinyin;
            primaryProgress1 = pinyinProgress;
        }

        if (!string.IsNullOrWhiteSpace(primaryLine1))
        {
            _primaryWindow.UpdateContent(
                primaryLine1, primaryLine2,
                isPrimaryPinyin: (primaryLine1 == pinyin),
                primaryProgress: primaryProgress1,
                secondaryProgress: primaryProgress2);
        }
        else
        {
            _primaryWindow.HideContent();
        }

        // 2. Phân bổ cho Secondary Screen Window (nếu có màn hình 2)
        if (hasSecondaryScreen)
        {
            bool showTransOnSec = transPlacement == LyricElementPlacement.SecondaryScreen ||
                                  transPlacement == LyricElementPlacement.BothScreens;

            bool showPinyinOnSec = pinyinPlacement == LyricElementPlacement.SecondaryScreen ||
                                   pinyinPlacement == LyricElementPlacement.BothScreens;

            string? secLine1 = null;
            string? secLine2 = null;
            double secProgress1 = 1.0;
            double secProgress2 = 1.0;

            if (showTransOnSec && !string.IsNullOrWhiteSpace(translation))
            {
                secLine1 = translation;
                secProgress1 = transProgress;
                if (showPinyinOnSec && !string.IsNullOrWhiteSpace(pinyin))
                {
                    secLine2 = pinyin;
                    secProgress2 = pinyinProgress;
                }
            }
            else if (showPinyinOnSec && !string.IsNullOrWhiteSpace(pinyin))
            {
                secLine1 = pinyin;
                secProgress1 = pinyinProgress;
            }

            if (!string.IsNullOrWhiteSpace(secLine1))
            {
                _secondaryWindow.UpdateContent(
                    secLine1, secLine2,
                    isPrimaryPinyin: (secLine1 == pinyin),
                    primaryProgress: secProgress1,
                    secondaryProgress: secProgress2);
            }
            else
            {
                _secondaryWindow.HideContent();
            }
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
