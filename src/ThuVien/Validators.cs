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

    /// <summary>Độ dài tối đa của địa chỉ email (RFC 5321).</summary>
    public const int MaxEmailLength = 254;

    /// <summary>
    /// Kiểm tra định dạng email cơ bản: đúng một ký tự <c>@</c>, không có khoảng trắng,
    /// phần tên miền có dấu chấm không nằm ở đầu/cuối, tổng độ dài không quá
    /// <see cref="MaxEmailLength"/> ký tự. Thời gian chạy tuyến tính theo độ dài chuỗi.
    /// </summary>
    public static bool IsEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > MaxEmailLength)
        {
            return false;
        }

        var at = email.IndexOf('@');
        if (at <= 0 || at == email.Length - 1 || email.IndexOf('@', at + 1) >= 0)
        {
            return false;
        }

        foreach (var c in email)
        {
            if (char.IsWhiteSpace(c))
            {
                return false;
            }
        }

        // Tên miền phải có ít nhất một dấu chấm với ký tự ở cả hai phía.
        var domain = email.AsSpan(at + 1);
        return domain.Length >= 3 && domain[1..^1].Contains('.');
    }

    private static string NormalizePhone(string phone) =>
        phone.Replace(" ", "").Replace(".", "").Replace("-", "").Trim();

    [GeneratedRegex(@"^(0|\+84|84)(3|5|7|8|9)[0-9]{8}$")]
    private static partial Regex PhoneRegex();

}
