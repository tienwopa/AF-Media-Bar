using Microsoft.Extensions.DependencyInjection;
using AFMediaBar.Custom.DualOverlay;
using AFMediaBar.Custom.FloatingLyrics;
using AFMediaBar.Custom.Settings;
using AFMediaBar.Custom.Translation;

namespace AFMediaBar.Custom;

/// <summary>
/// Điểm neo cấu hình mở rộng (Custom Extension Bootstrap).
/// </summary>
public static class CustomFeatureBootstrap
{
    public static IServiceCollection AddCustomDualOverlayFeature(this IServiceCollection services)
    {
        // 1. Cấu hình
        services.AddSingleton(CustomSettingsManager.Instance);

        // 2. Dịch thuật Đa nguồn & Cache
        services.AddSingleton<TranslationCacheService>();
        services.AddSingleton<GoogleTranslateService>();
        services.AddSingleton<BingTranslateService>();
        services.AddSingleton<GeminiTranslateService>();
        services.AddSingleton<OpenAITranslateService>();
        services.AddSingleton<MultiProviderTranslationService>();
        services.AddSingleton<ITranslationService>(sp => sp.GetRequiredService<MultiProviderTranslationService>());

        services.AddSingleton<BilingualLyricsManager>();

        // 3. Dual Taskbar Windows
        services.AddSingleton<DualTaskbarPrimaryWindow>();
        services.AddSingleton<DualTaskbarSecondaryWindow>();
        services.AddSingleton<DualOverlayCoordinator>();

        // 4. Bảng lời bài hát nổi (Floating Lyrics Window)
        services.AddSingleton<FloatingLyricsWindow>();
        services.AddSingleton<FloatingLyricsCoordinator>();

        return services;
    }

    public static void UseCustomDualOverlayFeature(this IServiceProvider serviceProvider)
    {
        try
        {
            _ = serviceProvider.GetRequiredService<DualOverlayCoordinator>();
            _ = serviceProvider.GetRequiredService<FloatingLyricsCoordinator>();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CustomFeatureBootstrap] Khong the khoi dong Custom Features: {ex.Message}");
        }
    }
}
