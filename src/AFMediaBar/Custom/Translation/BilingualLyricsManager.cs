using System.Text.RegularExpressions;
using AFMediaBar.Classes.Models;
using AFMediaBar.Custom.Settings;

namespace AFMediaBar.Custom.Translation;

/// <summary>
/// Quản lý việc dịch và xử lý song ngữ, tích hợp bộ nhận diện Tiếng Việt thông minh.
/// </summary>
public sealed class BilingualLyricsManager
{
    private static readonly Regex VietnameseCharPattern = new(
        @"[àáạảãâầấậẩẫăằắặẳẵèéẹẻẽêềếệểễìíịỉĩòóọỏõôồốộổỗơờớợởỡùúụủũưừứựửữỳýỵỷỹđÀÁẠẢÃÂẦẤẬẨẪĂẰẮẶẲẴÈÉẸẺẼÊỀẾỆỂỄÌÍỊỈĨÒÓỌỎÕÔỒỐỘỔỖƠỜỚỢỞỠÙÚỤỦŨƯỪỨỰỬỮỲÝỴỶỸĐ]",
        RegexOptions.Compiled);

    private readonly ITranslationService _translationService;
    private readonly CustomSettingsManager _settingsManager;
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

    /// <summary>
    /// Nhận diện xem nội dung bài hát có phải là Tiếng Việt hay không.
    /// </summary>
    public static bool IsVietnameseContent(IReadOnlyList<LyricLine> lines)
    {
        if (lines.Count == 0)
        {
            return false;
        }

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

        // Neu it nhat 3 dong hoac > 20% so dong lay mau co ky tu tieng Viet
        return vietnameseMatchCount >= 3 || (sampleSize > 0 && ((double)vietnameseMatchCount / sampleSize) >= 0.2);
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

        // Kiem tra xem bai hat co phai goc la tieng Viet khong
        bool isVietnamese = IsVietnameseContent(document.Lines);
        if (isVietnamese && _settingsManager.Current.AutoSkipVietnamese)
        {
            // Bai hat goc da la tieng Viet -> Khong can dich, ban document goc voi co isVietnamese = true
            BilingualLyricsReady?.Invoke(document, true);
            return;
        }

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

                BilingualLyricsReady?.Invoke(enrichedDoc, false);
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
