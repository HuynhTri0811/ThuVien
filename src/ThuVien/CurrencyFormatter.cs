using System.Globalization;

namespace ThuVien;

/// <summary>
/// Định dạng tiền Việt Nam Đồng.
/// </summary>
public static class CurrencyFormatter
{
    private static readonly NumberFormatInfo VndFormat = new()
    {
        NumberGroupSeparator = ".",
        NumberDecimalSeparator = ",",
        NegativeSign = "-",
    };

    /// <summary>
    /// Định dạng số tiền VND (làm tròn đến đồng). Ví dụ: 1234567 → "1.234.567 ₫".
    /// </summary>
    /// <param name="amount">Số tiền.</param>
    /// <param name="symbol">Ký hiệu đứng sau số, mặc định "₫".</param>
    public static string ToVnd(decimal amount, string symbol = "₫")
    {
        var rounded = Math.Round(amount, 0, MidpointRounding.AwayFromZero);
        var text = rounded.ToString("#,##0", VndFormat);
        return string.IsNullOrEmpty(symbol) ? text : $"{text} {symbol}";
    }
}
