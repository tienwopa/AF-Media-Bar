using System;
using System.Collections.Generic;
using System.Windows.Threading;
using AFMediaBar.Classes.Interop;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Custom.Settings;
using AFMediaBar.Custom.Translation;

namespace AFMediaBar.Custom.DualOverlay;

/// <summary>
/// Điều phối logic hiển thị đa dạng vị trí cho Lời dịch và Pinyin:
/// - Ngoại suy thời gian thực GetPosition để hiệu ứng Karaoke chạy chữ siêu mượt 25 FPS (40ms).
/// - Tính toán tiến độ Karaoke riêng biệt cho Lời dịch và từng âm tiết Pinyin.
/// - Hỗ trợ tùy chỉnh thứ tự dòng (Lời dịch trên hay Pinyin trên).
/// - Tự động phát hiện màn hình thứ 2 và gộp an toàn về Taskbar chính nếu chỉ có 1 màn hình.
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

        // Chu kỳ 40ms (~25 FPS) để hiệu ứng Karaoke quét chữ mượt mà nhất
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

        // Lấy vị trí thời gian thực ngoại suy theo mili-giây (TaskbarExperiencePolicy.GetPosition)
        var now = DateTimeOffset.Now;
        var currentSeconds = TaskbarExperiencePolicy.GetPosition(snapshot, now);

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
        double transProgress = transLine != null ? CalculateTranslationProgress(transLine, transSeconds) : 1.0;

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
        double pinyinProgress = pinyinLine != null ? CalculatePinyinProgress(pinyinLine, pinyin, pinyinSeconds) : 1.0;

        // Điều phối nội dung tới Taskbar chính hoặc Màn hình 2
        DispatchContent(translation, transProgress, pinyin, pinyinProgress, settings);
    }

    /// <summary>
    /// Tính toán tiến độ Karaoke cho dòng Pinyin khớp chuẩn xác với từng âm tiết tiếng Trung.
    /// </summary>
    private static double CalculatePinyinProgress(LyricLine line, string? pinyin, double currentSeconds)
    {
        if (string.IsNullOrWhiteSpace(pinyin) || currentSeconds <= line.Start)
        {
            return 0.0;
        }
        if (currentSeconds >= line.End)
        {
            return 1.0;
        }

        // Nếu câu hát có danh sách âm tiết/từ (Words)
        if (line.Words != null && line.Words.Count > 0)
        {
            var words = line.Words;
            int wordCount = words.Count;
            var pinyinWords = pinyin.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (pinyinWords.Length == wordCount && pinyin.Length > 0)
            {
                int totalPinyinChars = pinyin.Length;
                double sungPinyinChars = 0;
                int currentWordCharIndex = 0;

                for (int i = 0; i < wordCount; i++)
                {
                    var w = words[i];
                    string pw = pinyinWords[i];

                    int pwIndex = pinyin.IndexOf(pw, currentWordCharIndex, StringComparison.Ordinal);
                    if (pwIndex < 0) pwIndex = currentWordCharIndex;
                    int pwEnd = pwIndex + pw.Length;

                    if (currentSeconds >= w.End)
                    {
                        sungPinyinChars = pwEnd;
                        currentWordCharIndex = pwEnd;
                        continue;
                    }

                    if (currentSeconds > w.Start)
                    {
                        double wDur = Math.Max(0.01, w.End - w.Start);
                        double wFrac = Math.Clamp((currentSeconds - w.Start) / wDur, 0.0, 1.0);
                        sungPinyinChars = pwIndex + (pw.Length * wFrac);
                        break;
                    }

                    sungPinyinChars = pwIndex;
                    break;
                }

                return Math.Clamp(sungPinyinChars / totalPinyinChars, 0.0, 1.0);
            }
            else
            {
                for (int i = 0; i < wordCount; i++)
                {
                    var w = words[i];
                    if (currentSeconds >= w.Start && currentSeconds < w.End)
                    {
                        double wDur = Math.Max(0.01, w.End - w.Start);
                        double wFrac = Math.Clamp((currentSeconds - w.Start) / wDur, 0.0, 1.0);
                        return Math.Clamp((i + wFrac) / wordCount, 0.0, 1.0);
                    }
                    if (currentSeconds >= w.End)
                    {
                        if (i == wordCount - 1 || currentSeconds < words[i + 1].Start)
                        {
                            return Math.Clamp((i + 1.0) / wordCount, 0.0, 1.0);
                        }
                    }
                }
            }
        }

        double lineDuration = Math.Max(0.3, line.End - line.Start);
        return Math.Clamp((currentSeconds - line.Start) / lineDuration, 0.0, 1.0);
    }

    /// <summary>
    /// Tính toán tiến độ Karaoke cho Lời dịch Tiếng Việt (chạy mượt theo thời gian câu hát).
    /// </summary>
    private static double CalculateTranslationProgress(LyricLine line, double currentSeconds)
    {
        if (currentSeconds <= line.Start) return 0.0;
        if (currentSeconds >= line.End) return 1.0;

        double lineDuration = Math.Max(0.3, line.End - line.Start);
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
        bool isPrimaryPinyin = false;
        double primaryProgress1 = 1.0;
        double primaryProgress2 = 1.0;

        bool hasTrans = showTransOnPrimary && !string.IsNullOrWhiteSpace(translation);
        bool hasPinyin = showPinyinOnPrimary && !string.IsNullOrWhiteSpace(pinyin);

        if (hasTrans && hasPinyin)
        {
            if (settings.LineOrder == BilingualLineOrder.RomanizationTop)
            {
                // Pinyin ở trên, Lời dịch ở dưới
                primaryLine1 = pinyin;
                primaryProgress1 = pinyinProgress;
                primaryLine2 = translation;
                primaryProgress2 = transProgress;
                isPrimaryPinyin = true;
            }
            else
            {
                // Lời dịch ở trên, Pinyin ở dưới (Mặc định)
                primaryLine1 = translation;
                primaryProgress1 = transProgress;
                primaryLine2 = pinyin;
                primaryProgress2 = pinyinProgress;
                isPrimaryPinyin = false;
            }
        }
        else if (hasTrans)
        {
            primaryLine1 = translation;
            primaryProgress1 = transProgress;
            isPrimaryPinyin = false;
        }
        else if (hasPinyin)
        {
            primaryLine1 = pinyin;
            primaryProgress1 = pinyinProgress;
            isPrimaryPinyin = true;
        }

        if (!string.IsNullOrWhiteSpace(primaryLine1))
        {
            _primaryWindow.UpdateContent(
                primaryLine1, primaryLine2,
                isPrimaryPinyin: isPrimaryPinyin,
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
            bool isSecPinyin = false;
            double secProgress1 = 1.0;
            double secProgress2 = 1.0;

            bool hasSecTrans = showTransOnSec && !string.IsNullOrWhiteSpace(translation);
            bool hasSecPinyin = showPinyinOnSec && !string.IsNullOrWhiteSpace(pinyin);

            if (hasSecTrans && hasSecPinyin)
            {
                if (settings.LineOrder == BilingualLineOrder.RomanizationTop)
                {
                    secLine1 = pinyin;
                    secProgress1 = pinyinProgress;
                    secLine2 = translation;
                    secProgress2 = transProgress;
                    isSecPinyin = true;
                }
                else
                {
                    secLine1 = translation;
                    secProgress1 = transProgress;
                    secLine2 = pinyin;
                    secProgress2 = pinyinProgress;
                    isSecPinyin = false;
                }
            }
            else if (hasSecTrans)
            {
                secLine1 = translation;
                secProgress1 = transProgress;
                isSecPinyin = false;
            }
            else if (hasSecPinyin)
            {
                secLine1 = pinyin;
                secProgress1 = pinyinProgress;
                isSecPinyin = true;
            }

            if (!string.IsNullOrWhiteSpace(secLine1))
            {
                _secondaryWindow.UpdateContent(
                    secLine1, secLine2,
                    isPrimaryPinyin: isSecPinyin,
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
