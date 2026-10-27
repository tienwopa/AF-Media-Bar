using AFMediaBar.Classes.Models;
using AFMediaBar.Custom.Settings;

namespace AFMediaBar.Custom.Translation;

/// <summary>
/// Quản lý việc làm giàu dữ liệu lời bài hát với bản dịch song ngữ.
/// Chạy nền và không bao giờ gây giật lag luồng UI chính.
/// </summary>
public sealed class BilingualLyricsManager
{
    private readonly ITranslationService _translationService;
    private readonly CustomSettingsManager _settingsManager;
    private CancellationTokenSource? _activeTranslationCts;
    private string _lastProcessedTrack = string.Empty;

    /// <summary>
    /// Bắn ra khi lời bài hát song ngữ đã được dịch xong và sẵn sàng.
    /// </summary>
    public event Action<LyricDocument>? BilingualLyricsReady;

    public BilingualLyricsManager(
        ITranslationService translationService,
        CustomSettingsManager settingsManager)
    {
        _translationService = translationService;
        _settingsManager = settingsManager;
    }

    /// <summary>
    /// Xử lý một LyricDocument khi có bài hát mới hoặc cập nhật lời.
    /// </summary>
    public void ProcessLyrics(string trackKey, LyricDocument document)
    {
        if (!_settingsManager.Current.EnableBilingualLyrics)
        {
            return;
        }

        if (document.Lines.Count == 0)
        {
            return;
        }

        if (string.Equals(_lastProcessedTrack, trackKey, StringComparison.Ordinal))
        {
            return;
        }

        _lastProcessedTrack = trackKey;

        // Huy tac vu dich cua bai truoc neu van chua chay xong
        _activeTranslationCts?.Cancel();
        _activeTranslationCts?.Dispose();
        _activeTranslationCts = new CancellationTokenSource();

        var token = _activeTranslationCts.Token;
        var targetLang = _settingsManager.Current.TargetLanguage;

        _ = Task.Run(async () =>
        {
            try
            {
                var originalTexts = document.Lines.Select(l => l.Text).ToList();
                var translatedTexts = await _translationService.TranslateBatchAsync(
                    originalTexts,
                    targetLang,
                    token).ConfigureAwait(false);

                if (token.IsCancellationRequested)
                {
                    return;
                }

                // Tao danh sach dong moi voi Translation tieng Viet
                var enrichedLines = new List<LyricLine>(document.Lines.Count);
                for (int i = 0; i < document.Lines.Count; i++)
                {
                    var orig = document.Lines[i];
                    var trans = (i < translatedTexts.Count && !string.IsNullOrWhiteSpace(translatedTexts[i]))
                        ? translatedTexts[i]
                        : orig.Translation;

                    var enrichedLine = new LyricLine(orig.Start, orig.End, orig.Text)
                    {
                        Words = orig.Words,
                        Translation = trans,
                        Romanization = orig.Romanization,
                        IsBackground = orig.IsBackground
                    };
                    enrichedLines.Add(enrichedLine);
                }

                var enrichedDoc = new LyricDocument(
                    enrichedLines,
                    document.SyncType,
                    document.SourceFormat);

                BilingualLyricsReady?.Invoke(enrichedDoc);
            }
            catch (OperationCanceledException)
            {
                // Bo qua khi doi bai hat nhanh
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BilingualLyricsManager] Loi xu ly dich: {ex.Message}");
            }
        }, token);
    }
}
