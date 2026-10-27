namespace AFMediaBar.Custom.Translation;

/// <summary>
/// Hợp đồng dịch thuật bất đồng bộ hỗ trợ dịch đơn lẻ và dịch theo lô (batch).
/// </summary>
public interface ITranslationService
{
    /// <summary>
    /// Dịch một đoạn văn bản từ ngôn ngữ tự động nhận diện sang ngôn ngữ đích.
    /// </summary>
    /// <param name="text">Đoạn văn bản cần dịch.</param>
    /// <param name="targetLanguage">Mã ngôn ngữ đích (ví dụ: "vi", "en").</param>
    /// <param name="cancellationToken">Token hủy bỏ tác vụ.</param>
    /// <returns>Bản dịch hoặc chuỗi rỗng nếu thất bại.</returns>
    Task<string> TranslateAsync(string text, string targetLanguage, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dịch một danh sách các câu (lyrics) theo lô (batch) để tối ưu số lần gọi mạng.
    /// </summary>
    /// <param name="texts">Danh sách các câu lyric cần dịch.</param>
    /// <param name="targetLanguage">Mã ngôn ngữ đích.</param>
    /// <param name="cancellationToken">Token hủy bỏ tác vụ.</param>
    /// <returns>Danh sách các câu đã dịch tương ứng theo đúng chỉ số thứ tự.</returns>
    Task<IReadOnlyList<string>> TranslateBatchAsync(IReadOnlyList<string> texts, string targetLanguage, CancellationToken cancellationToken = default);
}
