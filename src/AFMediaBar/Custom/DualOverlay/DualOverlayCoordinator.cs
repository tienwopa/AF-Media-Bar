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

        // 1. Tính toán tiến độ Karaoke (chạy chữ) cho câu hát hiện tại
        double progress = CalculateKaraokeProgress(matchedLine, currentSeconds);

        // 2. Điều phối nội dung tới Taskbar chính hoặc Màn hình 2
        DispatchContent(translation, pinyin, progress, settings);
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

    private void DispatchContent(string? translation, string? pinyin, double progress, CustomSettings settings)
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
            _primaryWindow.UpdateContent(primaryLine1, primaryLine2, isPrimaryPinyin: (primaryLine1 == pinyin), progress: progress);
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
                _secondaryWindow.UpdateContent(secLine1, secLine2, isPrimaryPinyin: (secLine1 == pinyin), progress: progress);
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
