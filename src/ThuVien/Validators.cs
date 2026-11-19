using System.Text.RegularExpressions;

namespace ThuVien;

/// <summary>
/// Các hàm kiểm tra dữ liệu phổ biến ở Việt Nam.
/// </summary>
public static partial class Validators
{
    /// <summary>
    /// Kiểm tra số điện thoại di động Việt Nam (10 số, đầu 03/05/07/08/09,
    /// chấp nhận tiền tố +84 hoặc 84, cho phép dấu cách, chấm, gạch ngang).
    /// </summary>
    public static bool IsVietnamesePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return false;
        }

        return PhoneRegex().IsMatch(NormalizePhone(phone));
    }

    /// <summary>
    /// Chuẩn hóa số điện thoại về dạng 0xxxxxxxxx. Trả về null nếu không hợp lệ.
    /// </summary>
    public static string? NormalizeVietnamesePhone(string? phone)
    {
        if (!IsVietnamesePhone(phone))
        {
            return null;
        }

        var digits = NormalizePhone(phone!);
        if (digits.StartsWith("+84", StringComparison.Ordinal))
        {
            return "0" + digits[3..];
        }

        if (digits.StartsWith("84", StringComparison.Ordinal) && digits.Length == 11)
        {
            return "0" + digits[2..];
        }

        return digits;
    }

    /// <summary>
    /// Kiểm tra định dạng email cơ bản.
    /// </summary>
    public static bool IsEmail(string? email) =>
        !string.IsNullOrWhiteSpace(email) && EmailRegex().IsMatch(email);

    private static string NormalizePhone(string phone) =>
        phone.Replace(" ", "").Replace(".", "").Replace("-", "").Trim();

    [GeneratedRegex(@"^(0|\+84|84)(3|5|7|8|9)\d{8}$")]
    private static partial Regex PhoneRegex();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailRegex();
}
