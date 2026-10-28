using System.Text.RegularExpressions;
using AFMediaBar.Classes.Models;
using AFMediaBar.Custom.Settings;

namespace AFMediaBar.Custom.Translation;

/// <summary>
/// Quản lý việc dịch và xử lý song ngữ:
/// - Tự động sinh Pinyin offline 100% trong 0ms.
/// - Nhận diện bài hát Tiếng Việt để tránh dịch thừa.
/// - Cơ chế Debounce thông minh giúp không bị nghẽn API Rate Limit (15 RPM) khi chuyển bài nhanh.
/// - Đọc trước cache để hiển thị tức thì nếu bài hát đã từng được nghe.
/// - Tự động làm mới dữ liệu khi người dùng đổi nhà cung cấp dịch trong Cài đặt.
/// </summary>
public sealed class BilingualLyricsManager
{
    private static readonly Regex VietnameseCharPattern = new(
        @"[àáạảãâầấậẩẫăằắặẳẵèéẹẻẽêềếệểễìíịỉĩòóọỏõôồốộổỗơờớợởỡùúụủũưừứựửữỳýỵỷỹđÀÁẠẢÃÂẦẤẬẨẪĂẰẮẶẲẴÈÉẸẺẼÊỀẾỆỂỄÌÍỊỈĨÒÓỌỎÕÔỒỐỘỔỖƠỜỚỢỞỠÙÚỤỦŨƯỪỨỰỬỮỲÝỴỶỸĐ]",
        RegexOptions.Compiled);

    private readonly ITranslationService _translationService;
    private readonly TranslationCacheService _cacheService;
    private readonly CustomSettingsManager _settingsManager;
    private readonly Dictionary<string, (LyricDocument doc, bool isVietnamese)> _documentCache = new(StringComparer.Ordinal);
    private CancellationTokenSource? _activeTranslationCts;
    private string _lastProcessedTrack = string.Empty;

    public event Action<LyricDocument, bool>? BilingualLyricsReady;

    public BilingualLyricsManager(
        ITranslationService translationService,
        TranslationCacheService cacheService,
        CustomSettingsManager settingsManager)
    {
        _translationService = translationService;
        _cacheService = cacheService;
        _settingsManager = settingsManager;
        _settingsManager.SettingsChanged += SettingsManager_SettingsChanged;
    }

    private void SettingsManager_SettingsChanged(CustomSettings settings)
    {
        // Khi người dùng thay đổi thiết lập dịch (ví dụ đổi Provider sang Google Dịch hoặc Gemini),
        // làm mới bộ nhớ đệm RAM để bài hát hiện tại được lập tức dịch lại theo cấu hình mới.
        _documentCache.Clear();
        _lastProcessedTrack = string.Empty;
    }

    public static bool IsVietnameseContent(IReadOnlyList<LyricLine> lines)
    {
        if (lines.Count == 0) return false;

        int vietnameseMatchCount = 0;
        int sampleSize = Math.Min(lines.Count, 25);

        for (int i = 0; i < sampleSize; i++)
        {
            var text = lines[i].Text;
            if (VietnameseCharPattern.IsMatch(text))
            {
                vietnameseMatchCount++;
            }
        }

        return vietnameseMatchCount >= 3 || (sampleSize > 0 && ((double)vietnameseMatchCount / sampleSize) >= 0.2);
    }

    public void ProcessLyrics(string trackKey, LyricDocument document)
    {
        if (!_settingsManager.Current.EnableBilingualLyrics || document.Lines.Count == 0)
        {
            return;
        }

        // Tái sử dụng ngay lập tức nếu bài hát này đã được làm giàu trong phiên hiện tại
        if (_documentCache.TryGetValue(trackKey, out var cached))
        {
            _lastProcessedTrack = trackKey;
            BilingualLyricsReady?.Invoke(cached.doc, cached.isVietnamese);
            return;
        }

        if (string.Equals(_lastProcessedTrack, trackKey, StringComparison.Ordinal))
        {
            return;
        }

        _lastProcessedTrack = trackKey;
        var targetLang = _settingsManager.Current.TargetLanguage;

        // 1. Tự động sinh Pinyin (0ms) và đọc trước Cache ổ đĩa nếu đã có bản dịch
        var preparedLines = new List<LyricLine>(document.Lines.Count);
        bool hasAnyMissingTranslation = false;

        for (int i = 0; i < document.Lines.Count; i++)
        {
            var orig = document.Lines[i];
            string? romanization = orig.Romanization;
            string? translation = orig.Translation;

            // Nếu chưa có Pinyin mà là chữ Hán -> tự sinh Pinyin
            if (string.IsNullOrWhiteSpace(romanization) && PinyinGeneratorService.HasChinese(orig.Text))
            {
                romanization = PinyinGeneratorService.ToPinyin(orig.Text);
            }

            // Kiểm tra cache ổ đĩa để hiển thị tức thì 0ms nếu từng dịch
            if (string.IsNullOrWhiteSpace(translation) && _cacheService.TryGet(orig.Text, targetLang, out var cachedTrans))
            {
                translation = cachedTrans;
            }

            if (string.IsNullOrWhiteSpace(translation))
            {
                hasAnyMissingTranslation = true;
            }

            preparedLines.Add(new LyricLine(orig.Start, orig.End, orig.Text)
            {
                Words = orig.Words,
                Translation = translation,
                Romanization = romanization,
                IsBackground = orig.IsBackground
            });
        }

        var immediateDoc = new LyricDocument(preparedLines, document.SyncType, document.SourceFormat);

        // Kiểm tra tiếng Việt
        bool isVietnamese = IsVietnameseContent(document.Lines);
        _documentCache[trackKey] = (immediateDoc, isVietnamese);

        if (isVietnamese && _settingsManager.Current.AutoSkipVietnamese)
        {
            BilingualLyricsReady?.Invoke(immediateDoc, true);
            return;
        }

        // Bắn ngay document đã có Pinyin (và cache nếu có) để hiển thị tức thì, không để người dùng chờ đợi!
        BilingualLyricsReady?.Invoke(immediateDoc, false);

        // Nếu tất cả các câu đều đã có sẵn trong cache thì không cần gọi mạng
        if (!hasAnyMissingTranslation)
        {
            return;
        }

        // 2. Chạy dịch luồng nền với cơ chế Debounce 350ms
        _activeTranslationCts?.Cancel();
        _activeTranslationCts?.Dispose();
        _activeTranslationCts = new CancellationTokenSource();

        var token = _activeTranslationCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                // DEBOUNCE: Nếu người dùng chuyển bài liên tục trong 350ms, tác vụ cũ sẽ bị hủy
                // trước khi thực sự gửi yêu cầu mạng, tránh lãng phí hạn ngạch API (15 RPM)
                await Task.Delay(350, token).ConfigureAwait(false);
                if (token.IsCancellationRequested) return;

                var originalTexts = preparedLines.Select(l => l.Text).ToList();
                var translatedTexts = await _translationService.TranslateBatchAsync(originalTexts, targetLang, token).ConfigureAwait(false);

                if (token.IsCancellationRequested) return;

                var fullyEnrichedLines = new List<LyricLine>(preparedLines.Count);
                for (int i = 0; i < preparedLines.Count; i++)
                {
                    var orig = preparedLines[i];
                    string? trans = orig.Translation;

                    if (i < translatedTexts.Count &&
                        !string.IsNullOrWhiteSpace(translatedTexts[i]) &&
                        TranslationCacheService.IsValidTranslation(orig.Text, targetLang, translatedTexts[i]))
                    {
                        trans = translatedTexts[i];
                    }

                    fullyEnrichedLines.Add(new LyricLine(orig.Start, orig.End, orig.Text)
                    {
                        Words = orig.Words,
                        Translation = trans,
                        Romanization = orig.Romanization,
                        IsBackground = orig.IsBackground
                    });
                }

                var fullDoc = new LyricDocument(fullyEnrichedLines, document.SyncType, document.SourceFormat);
                _documentCache[trackKey] = (fullDoc, false);
                BilingualLyricsReady?.Invoke(fullDoc, false);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BilingualLyricsManager] Loi dich: {ex.Message}");
            }
        }, token);
    }
}
