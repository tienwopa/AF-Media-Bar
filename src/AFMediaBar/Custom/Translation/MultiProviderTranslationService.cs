using AFMediaBar.Custom.Settings;

namespace AFMediaBar.Custom.Translation;

/// <summary>
/// Bộ điều phối dịch thuật đa nguồn (Multi-Provider Translation Orchestrator).
/// Cho phép người dùng chuyển đổi linh hoạt giữa Google Dịch Free, Nguồn Free dự phòng, Google Gemini AI và OpenAI.
/// </summary>
public sealed class MultiProviderTranslationService : ITranslationService
{
    private readonly CustomSettingsManager _settingsManager;
    private readonly GoogleTranslateService _googleService;
    private readonly BingTranslateService _bingService;
    private readonly GeminiTranslateService _geminiService;
    private readonly OpenAITranslateService _openaiService;

    public MultiProviderTranslationService(
        CustomSettingsManager settingsManager,
        GoogleTranslateService googleService,
        BingTranslateService bingService,
        GeminiTranslateService geminiService,
        OpenAITranslateService openaiService)
    {
        _settingsManager = settingsManager;
        _googleService = googleService;
        _bingService = bingService;
        _geminiService = geminiService;
        _openaiService = openaiService;
    }

    public ITranslationService GetActiveService()
    {
        return _settingsManager.Current.TranslationProvider switch
        {
            TranslationProviderType.BingFree => _bingService,
            TranslationProviderType.GeminiAI => _geminiService,
            TranslationProviderType.OpenAICompatible => _openaiService,
            _ => _googleService
        };
    }

    public Task<string> TranslateAsync(string text, string targetLanguage, CancellationToken cancellationToken = default)
    {
        return GetActiveService().TranslateAsync(text, targetLanguage, cancellationToken);
    }

    public Task<IReadOnlyList<string>> TranslateBatchAsync(IReadOnlyList<string> texts, string targetLanguage, CancellationToken cancellationToken = default)
    {
        return GetActiveService().TranslateBatchAsync(texts, targetLanguage, cancellationToken);
    }
}
