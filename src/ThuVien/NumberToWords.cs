namespace ThuVien;

/// <summary>
/// Đọc số thành chữ tiếng Việt.
/// </summary>
public static class NumberToWords
{
    private static readonly string[] Digits =
        ["không", "một", "hai", "ba", "bốn", "năm", "sáu", "bảy", "tám", "chín"];

    private static readonly string[] Scales =
        ["", "nghìn", "triệu", "tỷ", "nghìn tỷ", "triệu tỷ", "tỷ tỷ"];

    /// <summary>
    /// Đọc một số nguyên thành chữ. Ví dụ: 1005 → "một nghìn không trăm linh năm".
    /// </summary>
    public static string ToVietnamese(long number)
    {
        if (number == 0)
        {
            return Digits[0];
        }

        var negative = number < 0;
        // Xử lý cả long.MinValue mà không bị tràn số.
        var value = negative ? (ulong)(-(number + 1)) + 1 : (ulong)number;

        var groups = new List<int>();
        while (value > 0)
        {
            groups.Add((int)(value % 1000));
            value /= 1000;
        }

        var top = groups.Count - 1;
        var words = new List<string>();

        for (var i = top; i >= 0; i--)
        {
            if (groups[i] == 0)
            {
                continue;
            }

            words.Add(ReadGroup(groups[i], full: i < top));
            if (Scales[i].Length > 0)
            {
                words.Add(Scales[i]);
            }
        }

        var result = string.Join(' ', words);
        return negative ? "âm " + result : result;
    }

    /// <summary>
    /// Đọc số tiền thành chữ, viết hoa chữ cái đầu và thêm "đồng".
    /// Ví dụ: 125000 → "Một trăm hai mươi lăm nghìn đồng".
    /// </summary>
    public static string ToVietnameseCurrency(long amount)
    {
        var text = ToVietnamese(amount) + " đồng";
        return char.ToUpper(text[0]) + text[1..];
    }

    /// <summary>
    /// Đọc một nhóm 3 chữ số. <paramref name="full"/> = true khi nhóm không đứng đầu,
    /// lúc đó phải đọc đủ "không trăm", "linh".
    /// </summary>
    private static string ReadGroup(int n, bool full)
    {
        var hundreds = n / 100;
        var tens = n / 10 % 10;
        var units = n % 10;
        var parts = new List<string>(4);

        if (full || hundreds > 0)
        {
            parts.Add(Digits[hundreds]);
            parts.Add("trăm");
        }

        switch (tens)
        {
            case 0 when units != 0 && (full || hundreds > 0):
                parts.Add("linh");
                break;
            case 0:
                break;
            case 1:
                parts.Add("mười");
                break;
            default:
                parts.Add(Digits[tens]);
                parts.Add("mươi");
                break;
        }

        if (units != 0)
        {
            parts.Add(units switch
            {
                1 when tens >= 2 => "mốt",
                5 when tens >= 1 => "lăm",
                _ => Digits[units],
            });
        }

        return string.Join(' ', parts);
    }
}
