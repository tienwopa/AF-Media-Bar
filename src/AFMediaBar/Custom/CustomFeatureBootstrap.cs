using Microsoft.Extensions.DependencyInjection;
using AFMediaBar.Custom.DualOverlay;
using AFMediaBar.Custom.Settings;
using AFMediaBar.Custom.Translation;

namespace AFMediaBar.Custom;

/// <summary>
/// Điểm neo cấu hình mở rộng (Custom Extension Bootstrap).
/// Đóng gói 100% việc đăng ký DI và khởi tạo của toàn bộ tính năng mới vào 2 extension method.
/// Giúp file App.xaml.cs của tác giả chỉ cần chèn duy nhất 1-2 dòng, tránh xung đột khi merge.
/// </summary>
public static class CustomFeatureBootstrap
{
    /// <summary>
    /// Đăng ký toàn bộ Service, ViewModel, Window của tính năng Song ngữ & Dual Overlay vào DI Container.
    /// </summary>
    public static IServiceCollection AddCustomDualOverlayFeature(this IServiceCollection services)
    {
        // 1. Cấu hình
        services.AddSingleton<CustomSettingsManager>();

        // 2. Dịch thuật & Cache
        services.AddSingleton<TranslationCacheService>();
        services.AddSingleton<ITranslationService, GoogleTranslateService>();
        services.AddSingleton<BilingualLyricsManager>();

        // 3. Dual Overlay
        services.AddSingleton<DualOverlayPlacementCoordinator>();
        services.AddSingleton<DualOverlayViewModel>();
        services.AddSingleton<DualOverlayWindow>();
        services.AddSingleton<DualOverlayCoordinator>();

        return services;
    }

    /// <summary>
    /// Khởi động điều phối viên Dual Overlay khi ứng dụng bắt đầu chạy.
    /// </summary>
    public static void UseCustomDualOverlayFeature(this IServiceProvider serviceProvider)
    {
        try
        {
            // Resolve DualOverlayCoordinator để kích hoạt lắng nghe và khởi tạo cửa sổ
            _ = serviceProvider.GetRequiredService<DualOverlayCoordinator>();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CustomFeatureBootstrap] Khong the khoi dong Custom Features: {ex.Message}");
        }
    }
}
