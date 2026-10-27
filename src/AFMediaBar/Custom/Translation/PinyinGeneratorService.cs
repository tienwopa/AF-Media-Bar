using System.Text.RegularExpressions;
using TinyPinyin;

namespace AFMediaBar.Custom.Translation;

/// <summary>
/// Dịch vụ tự động sinh Pinyin cho chữ Hán (tiếng Trung) offline 100%.
/// </summary>
public static class PinyinGeneratorService
{
    private static readonly Regex ChineseRegex = new(@"[\u4e00-\u9fa5]", RegexOptions.Compiled);

    /// <summary>
    /// Kiểm tra xem văn bản có chứa ký tự Hán tự tiếng Trung hay không.
    /// </summary>
    public static bool HasChinese(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        return ChineseRegex.IsMatch(text);
    }

    /// <summary>
    /// Chuyển đổi một câu tiếng Trung sang Pinyin dạng chữ thường có khoảng trắng đẹp mắt.
    /// </summary>
    public static string ToPinyin(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        try
        {
            // TinyPinyin tra ve chu hoa phan tach bang separator
            var pinyin = PinyinHelper.GetPinyin(text, " ");
            if (!string.IsNullOrWhiteSpace(pinyin))
            {
                return pinyin.ToLowerInvariant();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PinyinGenerator] Loi convert pinyin: {ex.Message}");
        }

        return text;
    }
}
