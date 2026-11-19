using System.Globalization;
using System.Text;

namespace ThuVien;

/// <summary>
/// Các hàm xử lý chuỗi tiếng Việt.
/// </summary>
public static class VietnameseText
{
    /// <summary>
    /// Bỏ dấu tiếng Việt. Ví dụ: "Tiếng Việt Đẹp" → "Tieng Viet Dep".
    /// </summary>
    public static string RemoveDiacritics(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var decomposed = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);

        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            sb.Append(c switch
            {
                'đ' => 'd',
                'Đ' => 'D',
                _ => c,
            });
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>
    /// Tạo slug dùng cho URL. Ví dụ: "Xin chào, Việt Nam!" → "xin-chao-viet-nam".
    /// </summary>
    public static string ToSlug(string? text, char separator = '-')
    {
        var plain = RemoveDiacritics(text).ToLowerInvariant();
        var sb = new StringBuilder(plain.Length);
        var pendingSeparator = false;

        foreach (var c in plain)
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                if (pendingSeparator && sb.Length > 0)
                {
                    sb.Append(separator);
                }

                sb.Append(c);
                pendingSeparator = false;
            }
            else
            {
                pendingSeparator = true;
            }
        }

        return sb.ToString();
    }
}
