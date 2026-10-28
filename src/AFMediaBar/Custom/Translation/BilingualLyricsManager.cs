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
/// - Đảm bảo thread-safe 100% khi bắn sự kiện về UI thread.
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

    public static BilingualLyricsManager? Instance { get; private set; }
    public static LyricDocument? ActiveEnrichedDocument { get; private set; }
    public static bool ActiveIsVietnamese { get; private set; }
    public static string ActiveTrackKey { get; private set; } = string.Empty;

    public static void ResetActiveTrack()
    {
        ActiveEnrichedDocument = null;
        ActiveTrackKey = string.Empty;
        ActiveIsVietnamese = false;
    }

    public static event Action<LyricDocument, bool>? EnrichedLyricsUpdated;

    public BilingualLyricsManager(
        ITranslationService translationService,
        TranslationCacheService cacheService,
        CustomSettingsManager settingsManager)
    {
        Instance = this;
        _translationService = translationService;
        _cacheService = cacheService;
        _settingsManager = settingsManager;
        _settingsManager.SettingsChanged += SettingsManager_SettingsChanged;
    }

    private void SettingsManager_SettingsChanged(CustomSettings settings)
    {
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

    private void FireEvents(LyricDocument doc, bool isVietnamese)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.HasShutdownStarted && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(() =>
            {
                BilingualLyricsReady?.Invoke(doc, isVietnamese);
                EnrichedLyricsUpdated?.Invoke(doc, isVietnamese);
            });
        }
        else
        {
            BilingualLyricsReady?.Invoke(doc, isVietnamese);
            EnrichedLyricsUpdated?.Invoke(doc, isVietnamese);
        }
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
            if (string.Equals(_lastProcessedTrack, trackKey, StringComparison.Ordinal) &&
                ReferenceEquals(ActiveEnrichedDocument, cached.doc)) return;
            _lastProcessedTrack = trackKey;
            ActiveTrackKey = trackKey;
            ActiveEnrichedDocument = cached.doc;
            ActiveIsVietnamese = cached.isVietnamese;
            FireEvents(cached.doc, cached.isVietnamese);
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

            if (string.IsNullOrWhiteSpace(romanization) && PinyinGeneratorService.HasChinese(orig.Text))
            {
                romanization = PinyinGeneratorService.ToPinyin(orig.Text);
            }

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

        bool isVietnamese = IsVietnameseContent(document.Lines);
        _documentCache[trackKey] = (immediateDoc, isVietnamese);

        ActiveTrackKey = trackKey;
        ActiveEnrichedDocument = immediateDoc;
        ActiveIsVietnamese = isVietnamese;
        if (isVietnamese && _settingsManager.Current.AutoSkipVietnamese)
        {
            FireEvents(immediateDoc, true);
            return;
        }

        // Bắn ngay document đã có Pinyin (và cache nếu có) để hiển thị tức thì 0ms
        FireEvents(immediateDoc, false);

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
                ActiveTrackKey = trackKey;
                ActiveEnrichedDocument = fullDoc;
                ActiveIsVietnamese = false;
                FireEvents(fullDoc, false);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BilingualLyricsManager] Loi dich: {ex.Message}");
            }
        }, token);
    }
}
