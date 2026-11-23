using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace ThuVien;

/// <summary>
/// Đọc chữ số tiếng Việt thành số nguyên (chiều ngược của <see cref="NumberToWords"/>).
/// </summary>
/// <remarks>
/// <para>
/// Đọc được mọi chuỗi do <see cref="NumberToWords.ToVietnamese"/> và
/// <see cref="NumberToWords.ToVietnameseCurrency"/> sinh ra, cùng các cách viết phổ biến:
/// </para>
/// <list type="bullet">
/// <item><description>Không phân biệt hoa thường; chấp nhận chuỗi dựng sẵn hoặc tổ hợp (NFC/NFD).</description></item>
/// <item><description><c>ngàn</c> = <c>nghìn</c>, <c>tỉ</c> = <c>tỷ</c>, <c>lẻ</c> = <c>linh</c>, <c>bẩy</c> = <c>bảy</c>.</description></item>
/// <item><description>Sau <c>mươi</c>: <c>mốt</c>/<c>một</c>, <c>tư</c>/<c>bốn</c>, <c>lăm</c>/<c>nhăm</c>/<c>năm</c>.</description></item>
/// <item><description>Tiền tố âm: <c>âm</c> hoặc <c>trừ</c>; hậu tố tiền tệ: <c>đồng</c>, <c>đồng chẵn</c>.</description></item>
/// <item><description>Nhóm không đọc đủ "không trăm"/"linh" vẫn hiểu theo giá trị của nhóm,
/// vd. "hai nghìn hai mươi" → 2020, "một triệu năm nghìn" → 1 005 000.</description></item>
/// </list>
/// <para>
/// Cách nói tắt dễ gây hiểu nhầm bị từ chối, vd. "một trăm năm" (100 hay 150?) phải viết
/// "một trăm linh năm" hoặc "một trăm năm mươi".
/// </para>
/// </remarks>
public static class WordsToNumber
{
    private const ulong MaxMagnitude = (ulong)long.MaxValue + 1;

    private static readonly Dictionary<string, int> Units = new(StringComparer.Ordinal)
    {
        ["không"] = 0,
        ["một"] = 1,
        ["hai"] = 2,
        ["ba"] = 3,
        ["bốn"] = 4,
        ["năm"] = 5,
        ["sáu"] = 6,
        ["bảy"] = 7,
        ["bẩy"] = 7,
        ["tám"] = 8,
        ["chín"] = 9,
    };

    private static readonly Dictionary<string, ulong> Scales = new(StringComparer.Ordinal)
    {
        ["nghìn"] = 1_000,
        ["ngàn"] = 1_000,
        ["triệu"] = 1_000_000,
        ["tỷ"] = 1_000_000_000,
        ["tỉ"] = 1_000_000_000,
    };

    /// <summary>
    /// Đọc chữ thành số. Ví dụ: "một nghìn không trăm linh năm" → 1005.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> là null.</exception>
    /// <exception cref="FormatException">Chuỗi không phải cách đọc số hợp lệ.</exception>
    /// <exception cref="OverflowException">Giá trị nằm ngoài khoảng <see cref="long"/>.</exception>
    public static long FromVietnamese(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Parse(text, out var value) switch
        {
            Result.Ok => value,
            Result.Overflow => throw new OverflowException($"'{text}' vượt quá khoảng của Int64."),
            _ => throw new FormatException($"Không đọc được số từ '{text}'."),
        };
    }

    /// <summary>
    /// Như <see cref="FromVietnamese"/> nhưng trả về <c>false</c> thay vì ném lỗi.
    /// </summary>
    public static bool TryFromVietnamese([NotNullWhen(true)] string? text, out long value)
    {
        value = 0;
        return text is not null && Parse(text, out value) == Result.Ok;
    }

    private enum Result
    {
        Ok,
        Invalid,
        Overflow,
    }

    private static Result Parse(string text, out long value)
    {
        value = 0;
        var tokens = Tokenize(text);
        var end = tokens.Count;

        // Hậu tố tiền tệ: "... đồng" hoặc "... đồng chẵn".
        if (end > 0 && tokens[end - 1] == "chẵn")
        {
            end--;
            if (end == 0 || tokens[end - 1] != "đồng")
            {
                return Result.Invalid;
            }
        }

        if (end > 0 && tokens[end - 1] == "đồng")
        {
            end--;
        }

        var pos = 0;
        var negative = pos < end && tokens[pos] is "âm" or "trừ";
        if (negative)
        {
            pos++;
        }

        if (pos >= end)
        {
            return Result.Invalid;
        }

        if (end - pos == 1 && tokens[pos] == "không")
        {
            return Result.Ok;
        }

        ulong total = 0;
        var previousScale = ulong.MaxValue;
        try
        {
            while (pos < end)
            {
                if (!TryReadGroup(tokens, ref pos, end, out var group))
                {
                    return Result.Invalid;
                }

                // Một hay nhiều từ chỉ hàng liền nhau nhân với nhau: "nghìn tỷ" = 10^12, "tỷ tỷ" = 10^18.
                ulong scale = 1;
                var hasScale = false;
                while (pos < end && Scales.TryGetValue(tokens[pos], out var s))
                {
                    scale = checked(scale * s);
                    hasScale = true;
                    pos++;
                }

                // Hàng phải giảm dần, và nhóm không có từ chỉ hàng chỉ được đứng cuối.
                if (scale >= previousScale || (!hasScale && pos < end))
                {
                    return Result.Invalid;
                }

                previousScale = scale;
                total = checked(total + checked((ulong)group * scale));
            }
        }
        catch (OverflowException)
        {
            return Result.Overflow;
        }

        if (total > (negative ? MaxMagnitude : long.MaxValue))
        {
            return Result.Overflow;
        }

        value = negative ? (long)(0 - total) : (long)total;
        return Result.Ok;
    }

    /// <summary>
    /// Đọc một nhóm 0–999: [chữ số "trăm"] [("linh" chữ số) | ("mười" [đơn vị]) | (chữ số "mươi" [đơn vị]) | chữ số].
    /// </summary>
    private static bool TryReadGroup(List<string> tokens, ref int pos, int end, out int value)
    {
        value = 0;
        var start = pos;
        var hasHundreds = false;

        if (pos + 1 < end && tokens[pos + 1] == "trăm" && Units.TryGetValue(tokens[pos], out var h))
        {
            value = h * 100;
            hasHundreds = true;
            pos += 2;
        }

        if (pos < end)
        {
            var word = tokens[pos];
            if (word is "linh" or "lẻ")
            {
                // "linh" chỉ đứng sau hàng trăm và trước một chữ số 1–9.
                if (!hasHundreds || pos + 1 >= end || !Units.TryGetValue(tokens[pos + 1], out var u) || u == 0)
                {
                    return false;
                }

                value += u;
                pos += 2;
            }
            else if (word == "mười")
            {
                value += 10;
                pos++;
                if (pos < end && TryReadUnitAfterTen(tokens[pos], afterMuoi: false, out var u))
                {
                    value += u;
                    pos++;
                }
            }
            else if (pos + 1 < end && tokens[pos + 1] == "mươi"
                && Units.TryGetValue(word, out var t) && t >= 2)
            {
                value += t * 10;
                pos += 2;
                if (pos < end && TryReadUnitAfterTen(tokens[pos], afterMuoi: true, out var u))
                {
                    value += u;
                    pos++;
                }
            }
            else if (!hasHundreds && Units.TryGetValue(word, out var u) && u != 0)
            {
                // Chữ số đứng một mình. Sau "trăm" phải có "linh" (tránh nhầm "một trăm năm" = 150).
                value += u;
                pos++;
            }
        }

        return pos > start;
    }

    /// <summary>Chữ số hàng đơn vị sau "mười" / "mươi" (1–9, không nhận "không").</summary>
    private static bool TryReadUnitAfterTen(string word, bool afterMuoi, out int unit)
    {
        unit = word switch
        {
            "lăm" or "nhăm" => 5,
            "mốt" when afterMuoi => 1,
            "tư" when afterMuoi => 4,
            _ => Units.GetValueOrDefault(word, 0),
        };

        return unit != 0;
    }

    private static List<string> Tokenize(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormC).ToLower(CultureInfo.InvariantCulture);
        var tokens = new List<string>();
        var sb = new StringBuilder();
        foreach (var c in normalized)
        {
            if (char.IsLetter(c) || CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
                continue;
            }

            // Cho phép khoảng trắng, dấu phẩy, gạch ngang, dấu chấm làm dấu phân cách.
            if (!char.IsWhiteSpace(c) && c is not (',' or '-' or '.'))
            {
                tokens.Clear();
                tokens.Add(string.Empty); // ký tự lạ → chuỗi không hợp lệ
                return tokens;
            }

            Flush();
        }

        Flush();
        return tokens;

        void Flush()
        {
            if (sb.Length > 0)
            {
                tokens.Add(sb.ToString());
                sb.Clear();
            }
        }
    }
}
