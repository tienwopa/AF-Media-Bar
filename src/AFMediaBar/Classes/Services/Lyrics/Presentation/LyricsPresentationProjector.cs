using AFMediaBar.Custom.Settings;
using AFMediaBar.Custom.Translation;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Classes.Services.Lyrics;

/// <summary>
/// 可直接发送给 Web 歌词呈现引擎的目标帧。歌词检索和文本解析已经在上游完成；该帧只表达目标画面。
/// A target frame ready for the web lyrics presentation engine. Retrieval and parsing are complete upstream; this frame only
/// describes the target visual state.
/// </summary>
public sealed record LyricsPresentationFrame(
    bool IsVisible,
    string TrackId,
    int CurrentLineIndex,
    string Current,
    string Next,
    string CurrentTranslation,
    string NextTranslation,
    bool TranslationMode,
    double LineProgress,
    double? WordScanProgress,
    double? ScrollProgress,
    bool IsPlaying)
{
    public static LyricsPresentationFrame Hidden { get; } = new(
        false,
        string.Empty,
        -1,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        false,
        0,
        null,
        null,
        false);
}

/// <summary>
/// 把不可变媒体快照投影成 Web 歌词目标帧。调用者只需要提交快照、设置和当前时间；行选择、第二行语义、
/// 位置外推与真实逐字进度全部封装在本模块中。
/// Projects an immutable media snapshot into a web-lyrics target frame. Callers submit only the snapshot, settings, and current
/// time; line selection, secondary-row semantics, timeline extrapolation, and real syllable progress stay inside this module.
/// </summary>
public static class LyricsPresentationProjector
{
    public static LyricsPresentationFrame Project(
        MediaSnapshot snapshot,
        AppSettings settings,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(settings);

        var lines = snapshot.Lyrics?.Document.Lines;
        if (!settings.LyricsEnabled || !snapshot.IsConnected || lines is not { Count: > 0 })
        {
            return LyricsPresentationFrame.Hidden;
        }

        var position = TaskbarExperiencePolicy.GetPosition(snapshot, now);
        var index = LyricLineIndexPolicy.FindIndex(lines, position);
        if (index < 0)
        {
            return LyricsPresentationFrame.Hidden;
        }

        var currentLine = DisplayLine(lines[index], settings);
        var nextLine = index + 1 < lines.Count ? DisplayLine(lines[index + 1], settings) : null;
        var followingLine = index + 2 < lines.Count ? DisplayLine(lines[index + 2], settings) : null;

        // Can thiệp vị trí hiển thị chính của Media Bar (Lời gốc / Song song 2 dòng / Dịch / Pinyin)
        var customSettings = CustomSettingsManager.Instance.Current;
        var mediaBarMode = customSettings.MediaBarPrimaryContent;

        string primaryDisplayText = currentLine.Text;
        LyricsSecondaryLineSelection? currentSecondary = null;
        LyricsSecondaryLineSelection? nextSecondary = null;
        bool translationMode = false;
        string visibleNext = string.Empty;
        string currentTranslation = string.Empty;
        string nextTranslation = string.Empty;

        var trackKey = $"{snapshot.Artist}_{snapshot.Title}_{snapshot.Duration}";
        bool isTrackMatching = string.Equals(BilingualLyricsManager.ActiveTrackKey, trackKey, StringComparison.Ordinal);
        var enrichedDoc = isTrackMatching ? BilingualLyricsManager.ActiveEnrichedDocument : null;

        var enrichedCurrent = (enrichedDoc != null && index < enrichedDoc.Lines.Count) ? enrichedDoc.Lines[index] : null;
        var enrichedNext = (enrichedDoc != null && index + 1 < enrichedDoc.Lines.Count) ? enrichedDoc.Lines[index + 1] : null;
        var enrichedFollowing = (enrichedDoc != null && index + 2 < enrichedDoc.Lines.Count) ? enrichedDoc.Lines[index + 2] : null;

        string? currentTransText = !string.IsNullOrWhiteSpace(enrichedCurrent?.Translation) ? enrichedCurrent.Translation : currentLine.Translation;
        string? nextTransText = !string.IsNullOrWhiteSpace(enrichedNext?.Translation) ? enrichedNext.Translation : nextLine?.Translation;

        string? currentPinyinText = !string.IsNullOrWhiteSpace(enrichedCurrent?.Romanization) ? enrichedCurrent.Romanization : currentLine.Romanization;
        if (string.IsNullOrWhiteSpace(currentPinyinText) && PinyinGeneratorService.HasChinese(currentLine.Text))
        {
            currentPinyinText = PinyinGeneratorService.ToPinyin(currentLine.Text);
        }

        string? nextPinyinText = !string.IsNullOrWhiteSpace(enrichedNext?.Romanization) ? enrichedNext.Romanization : nextLine?.Romanization;
        if (nextLine != null && string.IsNullOrWhiteSpace(nextPinyinText) && PinyinGeneratorService.HasChinese(nextLine.Text))
        {
            nextPinyinText = PinyinGeneratorService.ToPinyin(nextLine.Text);
        }

        switch (mediaBarMode)
        {
            case MediaBarLyricsDisplayMode.OriginalAndRomanization:
                // 2 dòng song song: Lời gốc + Pinyin
                primaryDisplayText = currentLine.Text;
                if (!string.IsNullOrWhiteSpace(currentPinyinText))
                {
                    currentSecondary = new LyricsSecondaryLineSelection(LyricsSecondaryLineMode.Romanization, currentPinyinText);
                    translationMode = true;
                    currentTranslation = currentPinyinText;
                }
                if (!string.IsNullOrWhiteSpace(nextPinyinText))
                {
                    nextSecondary = new LyricsSecondaryLineSelection(LyricsSecondaryLineMode.Romanization, nextPinyinText);
                    nextTranslation = nextPinyinText;
                }
                break;

            case MediaBarLyricsDisplayMode.OriginalAndTranslation:
                // 2 dòng song song: Lời gốc + Lời dịch Tiếng Việt
                primaryDisplayText = currentLine.Text;
                if (!string.IsNullOrWhiteSpace(currentTransText))
                {
                    currentSecondary = new LyricsSecondaryLineSelection(LyricsSecondaryLineMode.Translation, currentTransText);
                    translationMode = true;
                    currentTranslation = currentTransText;
                }
                if (!string.IsNullOrWhiteSpace(nextTransText))
                {
                    nextSecondary = new LyricsSecondaryLineSelection(LyricsSecondaryLineMode.Translation, nextTransText);
                    nextTranslation = nextTransText;
                }
                break;

            case MediaBarLyricsDisplayMode.OriginalAndNextLine:
                // 2 dòng song song: Lời gốc + Câu kế tiếp
                primaryDisplayText = currentLine.Text;
                if (nextLine != null && !string.IsNullOrWhiteSpace(nextLine.Text))
                {
                    currentSecondary = new LyricsSecondaryLineSelection(LyricsSecondaryLineMode.NextLine, nextLine.Text);
                    visibleNext = nextLine.Text;
                }
                if (followingLine != null && !string.IsNullOrWhiteSpace(followingLine.Text))
                {
                    nextSecondary = new LyricsSecondaryLineSelection(LyricsSecondaryLineMode.NextLine, followingLine.Text);
                }
                translationMode = false;
                break;

            case MediaBarLyricsDisplayMode.Translation:
                // 1 dòng duy nhất: Lời dịch Tiếng Việt
                primaryDisplayText = !string.IsNullOrWhiteSpace(currentTransText) ? currentTransText : currentLine.Text;
                currentSecondary = null;
                translationMode = false;
                visibleNext = string.Empty;
                currentTranslation = string.Empty;
                break;

            case MediaBarLyricsDisplayMode.Romanization:
                // 1 dòng duy nhất: Pinyin / Phiên âm
                primaryDisplayText = !string.IsNullOrWhiteSpace(currentPinyinText) ? currentPinyinText : currentLine.Text;
                currentSecondary = null;
                translationMode = false;
                visibleNext = string.Empty;
                currentTranslation = string.Empty;
                break;

            default: // MediaBarLyricsDisplayMode.Original
                primaryDisplayText = currentLine.Text;
                if (settings.TwoLineLyricsEnabled)
                {
                    currentSecondary = LyricsSecondaryLinePolicy.ResolveSelection(
                        settings.LyricsSecondaryLine,
                        nextLine?.Text,
                        currentTransText,
                        currentPinyinText);
                    nextSecondary = nextLine is not null
                        ? LyricsSecondaryLinePolicy.ResolveSelection(
                            settings.LyricsSecondaryLine,
                            followingLine?.Text,
                            nextTransText,
                            nextPinyinText)
                        : null;
                    translationMode = currentSecondary is { Mode: not LyricsSecondaryLineMode.NextLine };
                    visibleNext = currentSecondary is { Mode: LyricsSecondaryLineMode.NextLine } selection
                        ? selection.Text
                        : string.Empty;
                    currentTranslation = translationMode ? currentSecondary?.Text ?? string.Empty : string.Empty;
                    nextTranslation = nextSecondary is { Mode: not LyricsSecondaryLineMode.NextLine }
                        ? nextSecondary.Value.Text
                        : string.Empty;
                }
                else
                {
                    currentSecondary = null;
                    translationMode = false;
                    visibleNext = string.Empty;
                    currentTranslation = string.Empty;
                    nextSecondary = null;
                    nextTranslation = string.Empty;
                }
                break;
        }
        var lineProgress = ResolveLineProgress(currentLine, position);
        // 逐字时间轴进度同时驱动擦亮与长行横向滚动：擦亮只是叠在同一轨迹上的视觉层，
        // 因此关闭擦亮不得让长歌词失去滚动——时间轴照常推进，只是不再绘制高亮。
        // The syllable timeline drives both the highlight and the long-line scroll: the highlight is only a visual
        // layer over that same trajectory, so turning it off must not stop a long line from scrolling.
        var syllableProgress = LyricHighlightPolicy.ResolveProgress(currentLine, position);
        var wordScanProgress = settings.LyricsSyllableHighlightEnabled ? syllableProgress : null;

        // 繁简转换落在"文本流向呈现层的最后一步"：当前行、下一句、译文与下一句译文都走同一个设置，
        // 因此不会出现"主行转了、第二行没转"的半截画面。逐字擦亮是 CSS 按渲染宽度百分比裁剪的，
        // 不依赖字符索引，所以转换改变字数也不会错位。
        // Conversion happens where text leaves for the presentation layer: the active line, the next line, and both translations all
        // follow the one setting, so there is never a half-converted frame. Syllable highlighting clips by rendered-width percentage in
        // CSS and does not index characters, so a changed character count cannot misalign it.
        var conversion = settings.LyricsChineseConversion;
        var stripBracketSymbols = settings.LyricsBracketSymbolsHidden;

        return new LyricsPresentationFrame(
            true,
            BuildTrackId(snapshot),
            index,
            LyricsChineseConverter.Convert(primaryDisplayText, conversion),
            LyricsChineseConverter.Convert(visibleNext, conversion),
            Present(currentTranslation, stripBracketSymbols, conversion),
            Present(nextTranslation, stripBracketSymbols, conversion),
            translationMode,
            lineProgress,
            wordScanProgress,
            syllableProgress,
            snapshot.IsPlaying);
    }

    private static string BuildTrackId(MediaSnapshot snapshot) => string.Join(
        "\u001f",
        snapshot.SourceId.Trim().ToUpperInvariant(),
        snapshot.Title.Trim().ToUpperInvariant(),
        snapshot.Artist.Trim().ToUpperInvariant());

    private static LyricLine DisplayLine(LyricLine line, AppSettings settings)
    {
        var display = settings.LyricsFuriganaFilterEnabled ? LyricsFuriganaFilter.Apply(line) : line;
        return settings.LyricsBracketSymbolsHidden ? LyricsTranslationFormatFilter.Apply(display) : display;
    }

    /// <summary>
    /// 把一段歌词文本送到呈现层：先按设置清理来源附加的格式标记（行首斜杠、整行括号），再做繁简转换。
    /// Sends one piece of lyric text to the presentation layer: cleans the source's format markers first (a leading
    /// slash, enclosing brackets), then applies the Chinese conversion.
    /// </summary>
    private static string Present(string text, bool stripBracketSymbols, LyricsChineseConversionMode conversion) =>
        LyricsChineseConverter.Convert(stripBracketSymbols ? LyricsTranslationFormatFilter.Clean(text) : text, conversion);

    private static double ResolveLineProgress(LyricLine line, double position)
    {
        if (!double.IsFinite(line.Start) || !double.IsFinite(line.End) || line.End <= line.Start)
        {
            return 0;
        }

        return Math.Clamp((position - line.Start) / (line.End - line.Start), 0, 1);
    }
}
