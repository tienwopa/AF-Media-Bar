using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AFMediaBar.Custom.Settings;

/// <summary>
/// Vị trí hiển thị của từng thành phần (Lời dịch / Pinyin).
/// </summary>
public enum LyricElementPlacement
{
    /// <summary>Hiển thị ở phía đối diện của Taskbar chính.</summary>
    PrimaryTaskbar = 0,

    /// <summary>Hiển thị ở Màn hình thứ 2.</summary>
    SecondaryScreen = 1,

    /// <summary>Hiển thị ở cả hai màn hình.</summary>
    BothScreens = 2,

    /// <summary>Không hiển thị thành phần này.</summary>
    None = 3
}

/// <summary>
/// Căn lề hiển thị trên Taskbar chính.
/// </summary>
public enum PrimaryTaskbarAlignment
{
    /// <summary>Căn lề phải (sát khay hệ thống Tray).</summary>
    Right = 0,

    /// <summary>Căn giữa khu vực trống của Taskbar.</summary>
    Center = 1,

    /// <summary>Căn lề trái (sát bên phải các icon ứng dụng).</summary>
    Left = 2
}

/// <summary>
/// Căn lề hiển thị trên Màn hình thứ 2.
/// </summary>
public enum SecondaryTaskbarAlignment
{
    /// <summary>Căn lề trái.</summary>
    Left = 0,

    /// <summary>Căn lề giữa.</summary>
    Center = 1,

    /// <summary>Căn lề phải.</summary>
    Right = 2
}

/// <summary>
/// Thứ tự hiển thị giữa Lời dịch và Pinyin khi hiển thị song song 2 dòng.
/// </summary>
public enum BilingualLineOrder
{
    /// <summary>Lời dịch Tiếng Việt ở trên, Pinyin ở dưới.</summary>
    TranslationTop = 0,

    /// <summary>Pinyin / Phiên âm ở trên, Lời dịch Tiếng Việt ở dưới.</summary>
    RomanizationTop = 1
}

/// <summary>
/// Độ đậm nhạt của phông chữ khi hiển thị song song 2 dòng.
/// </summary>
public enum BilingualWeightEmphasis
{
    /// <summary>Cân bằng (cả hai dòng đều có độ đậm bình thường - Normal).</summary>
    Balanced = 0,

    /// <summary>Ưu tiên Lời dịch (Lời dịch in đậm SemiBold, Pinyin bình thường).</summary>
    EmphasisTranslation = 1,

    /// <summary>Ưu tiên Pinyin (Pinyin in đậm SemiBold, Lời dịch bình thường).</summary>
    EmphasisRomanization = 2,

    /// <summary>In đậm cả hai dòng (SemiBold).</summary>
    BothSemiBold = 3
}

/// <summary>
/// Phạm vi áp dụng hiệu ứng chạy chữ Karaoke (Karaoke Highlight).
/// </summary>
public enum KaraokeTargetMode
{
    /// <summary>Áp dụng chạy chữ cho cả Lời dịch và Pinyin.</summary>
    Both = 0,

    /// <summary>Chỉ áp dụng cho Pinyin (khớp chuẩn xác theo từng âm tiết).</summary>
    RomanizationOnly = 1,

    /// <summary>Chỉ áp dụng cho Lời dịch Tiếng Việt.</summary>
    TranslationOnly = 2
}

/// <summary>
/// Hiệu ứng chuyển cảnh (Animation) khi xuất hiện câu hát mới.
/// </summary>
public enum LyricsAnimationMode
{
    /// <summary>Cuộn lên mượt mà và làm mờ (Slide Up &amp; Fade - Giống lời gốc).</summary>
    SlideUp = 0,

    /// <summary>Mờ dần êm ái (Fade Only).</summary>
    FadeOnly = 1,

    /// <summary>Trượt ngang nhẹ (Slide Horizontal).</summary>
    SlideHorizontal = 2,

    /// <summary>Phóng to nhẹ (Zoom In 96% -> 100%).</summary>
    ZoomIn = 3,

    /// <summary>Không hiệu ứng (Tức thì).</summary>
    None = 4
}

/// <summary>
/// Kiểu hiệu ứng hạt nền hiển thị phía sau lời bài hát.
/// </summary>
public enum LyricsBackgroundEffectType
{
    /// <summary>Tắt hiệu ứng nền (Mặc định).</summary>
    None = 0,

    /// <summary>Hoa anh đào rơi &amp; bay lượn (Sakura Petals).</summary>
    Sakura = 1,

    /// <summary>Tuyết rơi êm đềm (Snow Falling).</summary>
    Snow = 2,

    /// <summary>Mưa rơi xiên nhanh (Rain Falling).</summary>
    Rain = 3,

    /// <summary>Lá thu vàng / phong đỏ rơi chao liệng (Autumn Leaves).</summary>
    AutumnLeaves = 4,

    /// <summary>Đom đóm mùa hè phát sáng lung linh (Glowing Fireflies).</summary>
    Fireflies = 5,

    /// <summary>Bụi sao tinh tú lấp lánh (Twinkling Starfield).</summary>
    Starfield = 6,

    /// <summary>Bong bóng nước bồng bềnh nổi (Floating Bubbles).</summary>
    FloatingBubbles = 7,

    /// <summary>Dải cực quang gợn sóng mềm mại (Aurora Waves).</summary>
    AuroraWave = 8
}

/// <summary>
/// Phạm vi áp dụng hiệu ứng nền.
/// </summary>
public enum LyricsEffectScope
{
    /// <summary>Áp dụng cho cả Lời gốc Media Bar và Lời dịch/Pinyin Taskbar.</summary>
    Both = 0,

    /// <summary>Chỉ áp dụng cho khu vực Lời dịch &amp; Pinyin.</summary>
    DualTaskbarOnly = 1,

    /// <summary>Chỉ áp dụng cho khu vực Lời gốc Media Bar.</summary>
    MediaBarOriginalOnly = 2
}

/// <summary>
/// Mật độ số lượng hạt hiệu ứng.
/// </summary>
public enum LyricsEffectDensity
{
    /// <summary>Thưa thớt, nhẹ nhàng (Tiết kiệm tài nguyên tối đa).</summary>
    Low = 0,

    /// <summary>Vừa phải, hài hòa (Mặc định).</summary>
    Medium = 1,

    /// <summary>Dày đặc, sống động.</summary>
    High = 2
}

/// <summary>
/// Tốc độ chuyển động của hiệu ứng nền.
/// </summary>
public enum LyricsEffectSpeed
{
    /// <summary>Chậm rãi, êm dịu.</summary>
    Slow = 0,

    /// <summary>Tiêu chuẩn, tự nhiên (Mặc định).</summary>
    Normal = 1,

    /// <summary>Nhanh, sinh động.</summary>
    Fast = 2
}

/// <summary>
/// Nhà cung cấp dịch thuật (Translation Provider).
/// </summary>
public enum TranslationProviderType
{
    /// <summary>Google Dịch công khai (Miễn phí, nhanh chóng, không cần API Key - Mặc định).</summary>
    GoogleFree = 0,

    /// <summary>Nguồn dịch miễn phí dự phòng (MyMemory / Lingva Free - Không cần API Key).</summary>
    BingFree = 1,

    /// <summary>Google Gemini AI API (Dịch chất lượng cao, có vần điệu cảm xúc, cần API Key).</summary>
    GeminiAI = 2,

    /// <summary>OpenAI / DeepSeek / Groq (Tương thích chuẩn OpenAI API, cần API Key).</summary>
    OpenAICompatible = 3
}

/// <summary>
/// Cấu hình tùy biến dành riêng cho tính năng Song ngữ &amp; Dual Taskbar Lyrics.
/// </summary>
public sealed class CustomSettings
{
    /// <summary>Bật/tắt tính năng dịch và hiển thị song ngữ.</summary>
    public bool EnableBilingualLyrics { get; set; } = true;

    /// <summary>Nhà cung cấp dịch thuật đang sử dụng.</summary>
    public TranslationProviderType TranslationProvider { get; set; } = TranslationProviderType.GoogleFree;

    /// <summary>API Key Google Gemini (lấy miễn phí từ Google AI Studio: aistudio.google.com).</summary>
    public string GeminiApiKey { get; set; } = string.Empty;

    /// <summary>Tên model Gemini sử dụng (ví dụ: gemini-3.5-flash, gemini-3.5-flash-lite, gemini-3.8-flash).</summary>
    public string GeminiModel { get; set; } = "gemini-3.5-flash";

    /// <summary>Base URL cho dịch vụ tương thích OpenAI (ví dụ: https://api.openai.com/v1 hoặc https://api.deepseek.com/v1).</summary>
    public string OpenAIBaseUrl { get; set; } = "https://api.openai.com/v1";

    /// <summary>API Key OpenAI / DeepSeek / Groq.</summary>
    public string OpenAIApiKey { get; set; } = string.Empty;

    /// <summary>Tên model OpenAI / DeepSeek (ví dụ: gpt-4o-mini, deepseek-chat).</summary>
    public string OpenAIModel { get; set; } = "gpt-4o-mini";

    /// <summary>Mã ngôn ngữ đích cần dịch sang (mặc định: "vi" - Tiếng Việt).</summary>
    public string TargetLanguage { get; set; } = "vi";

    /// <summary>Bật/tắt thanh Taskbar phụ hiển thị lời dịch / pinyin.</summary>
    public bool EnableDualTaskbarLyrics { get; set; } = true;

    /// <summary>Vị trí hiển thị của Lời dịch Tiếng Việt.</summary>
    public LyricElementPlacement TranslationPlacement { get; set; } = LyricElementPlacement.PrimaryTaskbar;

    /// <summary>Vị trí hiển thị của Pinyin / Phiên âm.</summary>
    public LyricElementPlacement RomanizationPlacement { get; set; } = LyricElementPlacement.SecondaryScreen;

    /// <summary>Căn lề vị trí hiển thị trên Taskbar chính.</summary>
    public PrimaryTaskbarAlignment PrimaryAlignment { get; set; } = PrimaryTaskbarAlignment.Right;

    /// <summary>Độ rộng tối đa cho phép hiển thị trên Taskbar chính (DIP, mặc định 480).</summary>
    public double PrimaryMaxWidth { get; set; } = 480.0;

    /// <summary>Căn lề vị trí hiển thị trên Màn hình thứ 2.</summary>
    public SecondaryTaskbarAlignment SecondaryAlignment { get; set; } = SecondaryTaskbarAlignment.Center;

    /// <summary>Thứ tự hiển thị 2 dòng: Lời dịch trên hoặc Pinyin trên.</summary>
    public BilingualLineOrder LineOrder { get; set; } = BilingualLineOrder.TranslationTop;

    /// <summary>Cỡ chữ Lời dịch Tiếng Việt (DIP, mặc định 11.5 pt).</summary>
    public double TranslationFontSize { get; set; } = 11.5;

    /// <summary>Cỡ chữ Pinyin / Phiên âm (DIP, mặc định 11.0 pt).</summary>
    public double RomanizationFontSize { get; set; } = 11.0;

    /// <summary>Độ đậm phông chữ khi hiển thị 2 dòng.</summary>
    public BilingualWeightEmphasis WeightEmphasis { get; set; } = BilingualWeightEmphasis.Balanced;

    /// <summary>Tự động căn giữa và điều chỉnh cỡ chữ to bằng lời gốc khi chỉ có 1 dòng hoạt động.</summary>
    public bool AutoBalanceSingleLine { get; set; } = true;

    /// <summary>Hiệu ứng chuyển câu xuất hiện lời bài hát.</summary>
    public LyricsAnimationMode AnimationMode { get; set; } = LyricsAnimationMode.SlideUp;

    /// <summary>Bật/tắt hiệu ứng chạy chữ Karaoke (Karaoke Wipe Highlight) theo tiến độ bài hát.</summary>
    public bool EnableKaraokeHighlight { get; set; } = true;

    /// <summary>Chỉ bật chạy chữ Karaoke khi bài hát có dữ liệu Karaoke từng từ thực sự (Words), nếu không tự động tắt.</summary>
    public bool OnlyKaraokeWhenAvailable { get; set; } = true;

    /// <summary>Phạm vi áp dụng hiệu ứng chạy chữ Karaoke.</summary>
    public KaraokeTargetMode KaraokeTarget { get; set; } = KaraokeTargetMode.Both;

    /// <summary>Tự động điều chỉnh màu sắc chữ dựa vào màu nền Taskbar (Light/Dark mode).</summary>
    public bool AutoAdaptTaskbarColor { get; set; } = true;

    /// <summary>Tự động ẩn bản dịch khi lời bài hát gốc đã là Tiếng Việt.</summary>
    public bool AutoSkipVietnamese { get; set; } = true;

    /// <summary>Màu sắc chữ của Lời dịch Tiếng Việt (mã HEX, ví dụ: #FFFFFF hoặc #18181B).</summary>
    public string TranslationColor { get; set; } = "#FFFFFF";

    /// <summary>Màu sắc chữ của Pinyin / Phiên âm (mã HEX, ví dụ: #FFD54F hoặc #18181B).</summary>
    public string RomanizationColor { get; set; } = "#FFD54F";

    /// <summary>Độ lệch thời gian hiển thị Lời dịch Tiếng Việt (ms). Dương = Sớm hơn, Âm = Trễ hơn.</summary>
    public int TranslationOffsetMs { get; set; } = 0;

    /// <summary>Độ lệch thời gian hiển thị Pinyin / Phiên âm (ms). Dương = Sớm hơn, Âm = Trễ hơn.</summary>
    public int RomanizationOffsetMs { get; set; } = 0;

    /// <summary>Cỡ chữ hiển thị cơ sở cũ (tương thích ngược).</summary>
    public double FontSize { get; set; } = 11.5;

    /// <summary>Độ mờ đục của chữ (0.2 -> 1.0).</summary>
    public double Opacity { get; set; } = 0.95;

    /// <summary>Kiểu hiệu ứng nền cho khu vực lời bài hát.</summary>
    public LyricsBackgroundEffectType BackgroundEffect { get; set; } = LyricsBackgroundEffectType.None;

    /// <summary>Phạm vi áp dụng hiệu ứng nền.</summary>
    public LyricsEffectScope BackgroundEffectScope { get; set; } = LyricsEffectScope.Both;

    /// <summary>Mật độ hạt của hiệu ứng nền.</summary>
    public LyricsEffectDensity BackgroundEffectDensity { get; set; } = LyricsEffectDensity.Medium;

    /// <summary>Tốc độ bay/rơi của hiệu ứng nền.</summary>
    public LyricsEffectSpeed BackgroundEffectSpeed { get; set; } = LyricsEffectSpeed.Normal;

    /// <summary>Độ trong suốt của hiệu ứng nền (0.1 -> 1.0, mặc định 0.65).</summary>
    public double BackgroundEffectOpacity { get; set; } = 0.65;
}

/// <summary>
/// Quản lý đọc/ghi cấu hình tùy biến.
/// </summary>
public sealed class CustomSettingsManager
{
    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AFMediaBar");

    private static readonly string SettingsFilePath = Path.Combine(SettingsDirectory, "custom_settings.json");

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static CustomSettingsManager? Instance { get; private set; }

    public CustomSettings Current { get; private set; } = new();

    public event Action<CustomSettings>? SettingsChanged;

    public CustomSettingsManager()
    {
        Instance = this;
        LoadSettings();
    }

    public void LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                var json = File.ReadAllText(SettingsFilePath);
                var loaded = JsonSerializer.Deserialize<CustomSettings>(json, _jsonOptions);
                if (loaded != null)
                {
                    // Tự động nâng cấp các model đã bị Google khai tử/ngừng hỗ trợ sang gemini-3.5-flash
                    if (string.IsNullOrWhiteSpace(loaded.GeminiModel) ||
                        loaded.GeminiModel.StartsWith("gemini-1.5", StringComparison.OrdinalIgnoreCase) ||
                        loaded.GeminiModel.StartsWith("gemini-2.0", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(loaded.GeminiModel, "gemini-2.5-flash", StringComparison.OrdinalIgnoreCase))
                    {
                        loaded.GeminiModel = "gemini-3.5-flash";
                    }

                    Current = loaded;
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CustomSettings] Loi doc settings: {ex.Message}");
        }

        Current = new CustomSettings();
        SaveSettings();
    }

    public void SaveSettings()
    {
        try
        {
            if (!Directory.Exists(SettingsDirectory))
            {
                Directory.CreateDirectory(SettingsDirectory);
            }

            var json = JsonSerializer.Serialize(Current, _jsonOptions);
            File.WriteAllText(SettingsFilePath, json);
            SettingsChanged?.Invoke(Current);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CustomSettings] Loi ghi settings: {ex.Message}");
        }
    }
}
