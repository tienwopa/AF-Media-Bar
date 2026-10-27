using System.Text.RegularExpressions;
using AFMediaBar.Classes.Models;
using AFMediaBar.Custom.Settings;

namespace AFMediaBar.Custom.Translation;

/// <summary>
/// Quản lý việc dịch và xử lý song ngữ:
/// - Tự động sinh Pinyin offline 100% trong 0ms.
/// - Nhận diện bài hát Tiếng Việt để tránh dịch thừa.
/// - Cơ chế Fast-Track câu đầu tiên để loại bỏ hoàn toàn độ trễ khi chuyển bài.
/// - Bộ nhớ đệm giữ toàn vẹn Pinyin và Lời dịch, không bị ghi đè.
/// </summary>
public sealed class BilingualLyricsManager
{
    private static readonly Regex VietnameseCharPattern = new(
        @"[àáạảãâầấậẩẫăằắặẳẵèéẹẻẽêềếệểễìíịỉĩòóọỏõôồốộổỗơờớợởỡùúụủũưừứựửữỳýỵỷỹđÀÁẠẢÃÂẦẤẬẨẪĂẰẮẶẲẴÈÉẸẺẼÊỀẾỆỂỄÌÍỊỈĨÒÓỌỎÕÔỒỐỘỔỖƠỜỚỢỞỠÙÚỤỦŨƯỪỨỰỬỮỲÝỴỶỸĐ]",
        RegexOptions.Compiled);

    private readonly ITranslationService _translationService;
    private readonly CustomSettingsManager _settingsManager;
    private readonly Dictionary<string, (LyricDocument doc, bool isVietnamese)> _documentCache = new(StringComparer.Ordinal);
    private CancellationTokenSource? _activeTranslationCts;
    private string _lastProcessedTrack = string.Empty;

    public event Action<LyricDocument, bool>? BilingualLyricsReady;

    public BilingualLyricsManager(
        ITranslationService translationService,
        CustomSettingsManager settingsManager)
    {
        _translationService = translationService;
        _settingsManager = settingsManager;
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

        // Tái sử dụng ngay lập tức nếu bài hát này đã được làm giàu
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

        // 1. Tự động sinh Pinyin cho tất cả các câu tiếng Trung NGAY LẬP TỨC (0ms)
        var preparedLines = new List<LyricLine>(document.Lines.Count);
        for (int i = 0; i < document.Lines.Count; i++)
        {
            var orig = document.Lines[i];
            string? romanization = orig.Romanization;

            // Nếu chưa có Pinyin mà là chữ Hán -> tự sinh Pinyin
            if (string.IsNullOrWhiteSpace(romanization) && PinyinGeneratorService.HasChinese(orig.Text))
            {
                romanization = PinyinGeneratorService.ToPinyin(orig.Text);
            }

            preparedLines.Add(new LyricLine(orig.Start, orig.End, orig.Text)
            {
                Words = orig.Words,
                Translation = orig.Translation,
                Romanization = romanization,
                IsBackground = orig.IsBackground
            });
        }

        var immediateDoc = new LyricDocument(preparedLines, document.SyncType, document.SourceFormat);

        // Kiem tra tieng Viet
        bool isVietnamese = IsVietnameseContent(document.Lines);
        _documentCache[trackKey] = (immediateDoc, isVietnamese);

        if (isVietnamese && _settingsManager.Current.AutoSkipVietnamese)
        {
            // Pinyin vẫn có thể hiện nếu cần, nhưng dịch thì bỏ qua
            BilingualLyricsReady?.Invoke(immediateDoc, true);
            return;
        }

        // Bắn ngay document đã có Pinyin để hiển thị tức thì, không phải đợi dịch mạng!
        BilingualLyricsReady?.Invoke(immediateDoc, false);

        // 2. Chạy dịch luồng nền với Fast-Track cho câu đầu tiên
        _activeTranslationCts?.Cancel();
        _activeTranslationCts?.Dispose();
        _activeTranslationCts = new CancellationTokenSource();

        var token = _activeTranslationCts.Token;
        var targetLang = _settingsManager.Current.TargetLanguage;

        _ = Task.Run(async () =>
        {
            try
            {
                // FAST-TRACK: Dịch ngay câu đầu tiên để người nghe thấy lời dịch tức thì
                if (preparedLines.Count > 0)
                {
                    var firstText = preparedLines[0].Text;
                    var firstTrans = await _translationService.TranslateAsync(firstText, targetLang, token).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(firstTrans) && !token.IsCancellationRequested)
                    {
                        preparedLines[0] = new LyricLine(preparedLines[0].Start, preparedLines[0].End, preparedLines[0].Text)
                        {
                            Words = preparedLines[0].Words,
                            Translation = firstTrans,
                            Romanization = preparedLines[0].Romanization,
                            IsBackground = preparedLines[0].IsBackground
                        };
                        var partialDoc = new LyricDocument(preparedLines, document.SyncType, document.SourceFormat);
                        _documentCache[trackKey] = (partialDoc, false);
                        BilingualLyricsReady?.Invoke(partialDoc, false);
                    }
                }

                // BATCH TOÀN BỘ CÁC CÂU CÒN LẠI
                var originalTexts = preparedLines.Select(l => l.Text).ToList();
                var translatedTexts = await _translationService.TranslateBatchAsync(originalTexts, targetLang, token).ConfigureAwait(false);

                if (token.IsCancellationRequested) return;

                var fullyEnrichedLines = new List<LyricLine>(preparedLines.Count);
                for (int i = 0; i < preparedLines.Count; i++)
                {
                    var orig = preparedLines[i];
                    var trans = (i < translatedTexts.Count && !string.IsNullOrWhiteSpace(translatedTexts[i]))
                        ? translatedTexts[i]
                        : orig.Translation;

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
