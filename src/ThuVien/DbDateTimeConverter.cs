using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;

namespace ThuVien;

/// <summary>Các kiểu ngày giờ của SQL Server.</summary>
public enum SqlServerDateType
{
    /// <summary><c>datetime</c>: 1753-01-01 → 9999-12-31, làm tròn tới 1/300 giây.</summary>
    DateTime,
    /// <summary><c>datetime2</c>: 0001-01-01 → 9999-12-31, chính xác 100 ns.</summary>
    DateTime2,
    /// <summary><c>smalldatetime</c>: 1900-01-01 → 2079-06-06, làm tròn tới phút.</summary>
    SmallDateTime,
    /// <summary><c>datetimeoffset</c>: như datetime2, kèm độ lệch múi giờ.</summary>
    DateTimeOffset,
    /// <summary><c>date</c>.</summary>
    Date,
    /// <summary><c>time</c>.</summary>
    Time,
}

/// <summary>Các kiểu ngày giờ của PostgreSQL.</summary>
public enum PostgresDateType
{
    /// <summary><c>timestamp</c> (without time zone), chính xác 1 µs.</summary>
    Timestamp,
    /// <summary><c>timestamptz</c> (with time zone).</summary>
    TimestampTz,
    /// <summary><c>date</c>.</summary>
    Date,
    /// <summary><c>time</c> (without time zone).</summary>
    Time,
    /// <summary><c>timetz</c> (with time zone).</summary>
    TimeTz,
}

/// <summary>Cách xử lý giá trị <c>infinity</c> / <c>-infinity</c> của PostgreSQL.</summary>
public enum InfinityHandling
{
    /// <summary>Ném <see cref="OverflowException"/>.</summary>
    Throw,
    /// <summary>Đổi thành giá trị nhỏ nhất / lớn nhất của kiểu đích.</summary>
    ClampToRange,
}

/// <summary>Tùy chọn cho <see cref="DbDateTimeConverter"/>.</summary>
public sealed record DbDateTimeOptions
{
    /// <summary>Tùy chọn mặc định.</summary>
    public static DbDateTimeOptions Default { get; } = new();

    /// <summary>
    /// Độ lệch múi giờ dùng khi đích cần múi giờ (datetimeoffset, timestamptz, timetz)
    /// mà giá trị nguồn không có. Mặc định +00:00 (UTC).
    /// </summary>
    public TimeSpan DefaultOffset { get; init; } = TimeSpan.Zero;

    /// <summary>Cách xử lý <c>infinity</c> khi chuyển sang SQL Server. Mặc định: ném lỗi.</summary>
    public InfinityHandling Infinity { get; init; } = InfinityHandling.Throw;
}

/// <summary>
/// Chuyển đổi kiểu dữ liệu và giá trị ngày giờ giữa PostgreSQL và SQL Server.
/// </summary>
/// <remarks>
/// Quy tắc múi giờ: khi giá trị nguồn có độ lệch (vd. timestamptz <c>+07</c>) mà kiểu đích
/// là kiểu dấu thời gian không có múi giờ (datetime, datetime2, smalldatetime, timestamp),
/// giá trị được đổi sang UTC. Với đích <c>date</c>/<c>time</c>, ngày/giờ được giữ nguyên
/// như trong chuỗi nguồn và bỏ độ lệch.
/// </remarks>
public static partial class DbDateTimeConverter
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static readonly DateTime SqlDateTimeMin = new(1753, 1, 1);
    private static readonly DateTime SqlDateTimeMax = new DateTime(9999, 12, 31, 23, 59, 59).AddMilliseconds(997);
    private static readonly DateTime SmallDateTimeMin = new(1900, 1, 1);
    private static readonly DateTime SmallDateTimeMax = new(2079, 6, 6, 23, 59, 0);

    // ───────────────────────────── Kiểu dữ liệu ─────────────────────────────

    /// <summary>
    /// Đổi tên kiểu PostgreSQL sang SQL Server.
    /// Ví dụ: <c>timestamp(3) with time zone</c> → <c>datetimeoffset(3)</c>.
    /// </summary>
    /// <exception cref="NotSupportedException">Kiểu không phải kiểu ngày giờ hoặc không có tương đương (interval).</exception>
    public static string PostgresTypeToSqlServer(string postgresType)
    {
        var (name, precision) = ParseTypeName(postgresType, maxPrecision: 6);
        return name switch
        {
            "timestamp" or "timestamp without time zone" => $"datetime2({precision ?? 6})",
            "timestamptz" or "timestamp with time zone" => $"datetimeoffset({precision ?? 6})",
            "date" => "date",
            // SQL Server không có time kèm múi giờ: độ lệch sẽ bị bỏ.
            "time" or "time without time zone" or "timetz" or "time with time zone" => $"time({precision ?? 6})",
            "interval" => throw new NotSupportedException(
                "SQL Server không có kiểu interval. Nên lưu dạng bigint (số micro giây) hoặc time nếu dưới 24 giờ."),
            _ => throw new NotSupportedException($"'{postgresType}' không phải kiểu ngày giờ PostgreSQL được hỗ trợ."),
        };
    }

    /// <summary>
    /// Đổi tên kiểu SQL Server sang PostgreSQL.
    /// Ví dụ: <c>datetime</c> → <c>timestamp(3)</c>, <c>datetime2(7)</c> → <c>timestamp(6)</c>.
    /// </summary>
    /// <exception cref="NotSupportedException">Kiểu không phải kiểu ngày giờ.</exception>
    public static string SqlServerTypeToPostgres(string sqlServerType)
    {
        var (name, precision) = ParseTypeName(sqlServerType.Replace("[", "").Replace("]", ""), maxPrecision: 7);
        // PostgreSQL chỉ chính xác tới micro giây (6 chữ số).
        var p = Math.Min(precision ?? 7, 6);
        return name switch
        {
            "datetime" => "timestamp(3)",
            "smalldatetime" => "timestamp(0)",
            "datetime2" => $"timestamp({p})",
            "datetimeoffset" => $"timestamptz({p})",
            "date" => "date",
            "time" => $"time({p})",
            _ => throw new NotSupportedException($"'{sqlServerType}' không phải kiểu ngày giờ SQL Server được hỗ trợ."),
        };
    }

    // ───────────────────────────── Giá trị ─────────────────────────────

    /// <summary>
    /// Chuyển một giá trị ngày giờ dạng chuỗi của PostgreSQL sang chuỗi dùng được cho SQL Server.
    /// Ví dụ: <c>2024-01-02 03:04:05.123456+07</c> → (datetime2) <c>2024-01-01T20:04:05.123456</c>.
    /// </summary>
    /// <returns>Chuỗi định dạng ISO 8601 an toàn với mọi cài đặt DATEFORMAT/LANGUAGE; <c>null</c> nếu đầu vào <c>null</c>.</returns>
    /// <exception cref="FormatException">Chuỗi không đúng định dạng.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Giá trị nằm ngoài khoảng của kiểu đích.</exception>
    /// <exception cref="OverflowException">Gặp <c>infinity</c> khi <see cref="DbDateTimeOptions.Infinity"/> là Throw.</exception>
    [return: NotNullIfNotNull(nameof(postgresValue))]
    public static string? PostgresToSqlServer(
        string? postgresValue,
        SqlServerDateType target = SqlServerDateType.DateTime2,
        DbDateTimeOptions? options = null)
    {
        if (postgresValue is null)
        {
            return null;
        }

        options ??= DbDateTimeOptions.Default;
        var p = Parse(postgresValue);

        if (p.Infinity != 0)
        {
            if (options.Infinity == InfinityHandling.Throw || target == SqlServerDateType.Time)
            {
                throw new OverflowException($"SQL Server không biểu diễn được '{postgresValue.Trim()}' cho kiểu {target}.");
            }

            var (min, max) = SqlServerRange(target);
            p = new Parsed(p.Infinity > 0 ? max : min, null, HasDate: true, Infinity: 0);
        }

        switch (target)
        {
            case SqlServerDateType.Date:
                RequireDate(p, postgresValue);
                return p.Value.ToString("yyyy-MM-dd", Inv);

            case SqlServerDateType.Time:
                return p.Value.ToString("HH:mm:ss.FFFFFFF", Inv);

            case SqlServerDateType.DateTimeOffset:
                RequireDate(p, postgresValue);
                return p.Value.ToString("yyyy-MM-ddTHH:mm:ss.FFFFFFF", Inv)
                    + FormatOffset(p.Offset ?? options.DefaultOffset);

            case SqlServerDateType.DateTime:
                RequireDate(p, postgresValue);
                return RoundToSqlServerDateTime(ToUtcIfZoned(p)).ToString("yyyy-MM-ddTHH:mm:ss.fff", Inv);

            case SqlServerDateType.SmallDateTime:
                RequireDate(p, postgresValue);
                return RoundToSmallDateTime(ToUtcIfZoned(p)).ToString("yyyy-MM-ddTHH:mm:ss", Inv);

            case SqlServerDateType.DateTime2:
                RequireDate(p, postgresValue);
                return ToUtcIfZoned(p).ToString("yyyy-MM-ddTHH:mm:ss.FFFFFFF", Inv);

            default:
                throw new ArgumentOutOfRangeException(nameof(target), target, null);
        }
    }

    /// <summary>
    /// Chuyển một giá trị ngày giờ dạng chuỗi của SQL Server sang chuỗi dùng được cho PostgreSQL.
    /// Phần lẻ dưới micro giây được làm tròn (PostgreSQL chỉ lưu tới 1 µs).
    /// Ví dụ: <c>2024-01-02 03:04:05.1234567 +07:00</c> → (timestamptz) <c>2024-01-02 03:04:05.123457+07:00</c>.
    /// </summary>
    /// <returns>Chuỗi định dạng ISO; <c>null</c> nếu đầu vào <c>null</c>.</returns>
    /// <exception cref="FormatException">Chuỗi không đúng định dạng.</exception>
    [return: NotNullIfNotNull(nameof(sqlServerValue))]
    public static string? SqlServerToPostgres(
        string? sqlServerValue,
        PostgresDateType target = PostgresDateType.Timestamp,
        DbDateTimeOptions? options = null)
    {
        if (sqlServerValue is null)
        {
            return null;
        }

        options ??= DbDateTimeOptions.Default;
        var p = Parse(sqlServerValue);

        if (p.Infinity != 0)
        {
            return target is PostgresDateType.Time or PostgresDateType.TimeTz
                ? throw new OverflowException($"Kiểu {target} của PostgreSQL không nhận '{sqlServerValue.Trim()}'.")
                : p.Infinity > 0 ? "infinity" : "-infinity";
        }

        switch (target)
        {
            case PostgresDateType.Timestamp:
                RequireDate(p, sqlServerValue);
                return RoundToPostgres(ToUtcIfZoned(p)).ToString("yyyy-MM-dd HH:mm:ss.FFFFFF", Inv);

            case PostgresDateType.TimestampTz:
                RequireDate(p, sqlServerValue);
                return RoundToPostgres(p.Value).ToString("yyyy-MM-dd HH:mm:ss.FFFFFF", Inv)
                    + FormatOffset(p.Offset ?? options.DefaultOffset);

            case PostgresDateType.Date:
                RequireDate(p, sqlServerValue);
                return p.Value.ToString("yyyy-MM-dd", Inv);

            case PostgresDateType.Time:
                return FormatPostgresTime(p.Value.TimeOfDay);

            case PostgresDateType.TimeTz:
                return FormatPostgresTime(p.Value.TimeOfDay) + FormatOffset(p.Offset ?? options.DefaultOffset);

            default:
                throw new ArgumentOutOfRangeException(nameof(target), target, null);
        }
    }

    // ───────────────────────────── Làm tròn .NET ─────────────────────────────

    /// <summary>
    /// Làm tròn như SQL Server <c>datetime</c> (bước .000 / .003 / .007 giây).
    /// Ví dụ: 23:59:59.998 → 23:59:59.997; 23:59:59.999 → 00:00:00.000 ngày hôm sau.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Ngoài khoảng 1753-01-01 → 9999-12-31 23:59:59.997.</exception>
    public static DateTime RoundToSqlServerDateTime(DateTime value)
    {
        EnsureRange(value, SqlDateTimeMin, DateTime.MaxValue, "datetime");

        var secondStart = value.Ticks - value.Ticks % TimeSpan.TicksPerSecond;
        var ms = (int)Math.Round(
            (double)(value.Ticks % TimeSpan.TicksPerSecond) / TimeSpan.TicksPerMillisecond,
            MidpointRounding.AwayFromZero);
        var last = ms % 10;
        ms += last switch
        {
            0 or 1 => -last,
            <= 4 => 3 - last,
            <= 8 => 7 - last,
            _ => 1,
        };

        var ticks = secondStart + ms * TimeSpan.TicksPerMillisecond;
        if (ticks > SqlDateTimeMax.Ticks)
        {
            throw OutOfRange(value, "datetime");
        }

        return new DateTime(ticks, value.Kind);
    }

    /// <summary>
    /// Làm tròn như SQL Server <c>smalldatetime</c> (tới phút; từ 29.999 giây trở lên làm tròn lên).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Ngoài khoảng 1900-01-01 → 2079-06-06 23:59.</exception>
    public static DateTime RoundToSmallDateTime(DateTime value)
    {
        var remainder = value.Ticks % TimeSpan.TicksPerMinute;
        var ticks = value.Ticks - remainder;
        if (remainder >= 29_999 * TimeSpan.TicksPerMillisecond)
        {
            ticks += TimeSpan.TicksPerMinute;
        }

        var result = new DateTime(Math.Min(ticks, DateTime.MaxValue.Ticks), value.Kind);
        if (result < SmallDateTimeMin || result > SmallDateTimeMax)
        {
            throw OutOfRange(value, "smalldatetime");
        }

        return result;
    }

    /// <summary>Làm tròn tới micro giây như PostgreSQL.</summary>
    public static DateTime RoundToPostgres(DateTime value)
    {
        var remainder = value.Ticks % 10;
        var ticks = value.Ticks - remainder;
        if (remainder >= 5 && ticks + 10 <= DateTime.MaxValue.Ticks)
        {
            ticks += 10;
        }

        return new DateTime(ticks, value.Kind);
    }

    // ───────────────────────────── Nội bộ ─────────────────────────────

    private readonly record struct Parsed(DateTime Value, TimeSpan? Offset, bool HasDate, int Infinity);

    private static Parsed Parse(string text)
    {
        var s = text.Trim();
        if (s.Equals("infinity", StringComparison.OrdinalIgnoreCase)
            || s.Equals("+infinity", StringComparison.OrdinalIgnoreCase))
        {
            return new Parsed(DateTime.MaxValue, null, true, 1);
        }

        if (s.Equals("-infinity", StringComparison.OrdinalIgnoreCase))
        {
            return new Parsed(DateTime.MinValue, null, true, -1);
        }

        var m = ValueRegex().Match(s);
        var hasDate = m.Groups["date"].Success;
        var hasTime = m.Groups["h"].Success;
        if (!m.Success || (!hasDate && !hasTime))
        {
            throw new FormatException($"Không nhận dạng được giá trị ngày giờ '{text}'.");
        }

        var date = DateTime.MinValue;
        if (hasDate && !DateTime.TryParseExact(m.Groups["date"].Value, "yyyy-MM-dd", Inv, DateTimeStyles.None, out date))
        {
            throw new FormatException($"Ngày không hợp lệ trong '{text}'.");
        }

        long ticks = 0;
        if (hasTime)
        {
            var h = int.Parse(m.Groups["h"].Value, Inv);
            var mi = int.Parse(m.Groups["mi"].Value, Inv);
            var sec = m.Groups["s"].Success ? int.Parse(m.Groups["s"].Value, Inv) : 0;
            if (h > 23 || mi > 59 || sec > 59)
            {
                throw new FormatException($"Giờ không hợp lệ trong '{text}'.");
            }

            ticks = new TimeSpan(h, mi, sec).Ticks + FractionToTicks(m.Groups["f"].Value);
        }

        TimeSpan? offset = null;
        if (m.Groups["off"].Success)
        {
            offset = ParseOffset(m.Groups["off"].Value, text);
        }

        if (date.Ticks + ticks > DateTime.MaxValue.Ticks)
        {
            ticks = DateTime.MaxValue.Ticks - date.Ticks;
        }

        return new Parsed(date.AddTicks(ticks), offset, hasDate, 0);
    }

    /// <summary>Phần lẻ giây (1–9 chữ số) → tick 100 ns, làm tròn chữ số thứ 8.</summary>
    private static long FractionToTicks(string fraction)
    {
        if (fraction.Length == 0)
        {
            return 0;
        }

        var seven = fraction.Length >= 7 ? fraction[..7] : fraction.PadRight(7, '0');
        var ticks = long.Parse(seven, Inv);
        if (fraction.Length > 7 && fraction[7] >= '5')
        {
            ticks++;
        }

        return ticks;
    }

    private static TimeSpan ParseOffset(string value, string original)
    {
        if (value == "Z")
        {
            return TimeSpan.Zero;
        }

        var sign = value[0] == '-' ? -1 : 1;
        var digits = value[1..].Replace(":", "");
        var hours = int.Parse(digits[..2], Inv);
        var minutes = digits.Length > 2 ? int.Parse(digits[2..], Inv) : 0;
        if (hours > 14 || minutes > 59)
        {
            throw new FormatException($"Độ lệch múi giờ không hợp lệ trong '{original}'.");
        }

        return sign * new TimeSpan(hours, minutes, 0);
    }

    private static string FormatOffset(TimeSpan offset) =>
        (offset < TimeSpan.Zero ? "-" : "+") + offset.Duration().ToString(@"hh\:mm", Inv);

    private static string FormatPostgresTime(TimeSpan time)
    {
        var rounded = RoundToPostgres(new DateTime(time.Ticks));
        // 23:59:59.9999995 làm tròn thành 24:00:00 — PostgreSQL chấp nhận giá trị này cho time.
        return rounded.Day > 1 ? "24:00:00" : rounded.ToString("HH:mm:ss.FFFFFF", Inv);
    }

    private static DateTime ToUtcIfZoned(Parsed p) =>
        p.Offset is { } offset ? new DateTimeOffset(p.Value, offset).UtcDateTime : p.Value;

    private static (DateTime Min, DateTime Max) SqlServerRange(SqlServerDateType type) => type switch
    {
        SqlServerDateType.DateTime => (SqlDateTimeMin, SqlDateTimeMax),
        SqlServerDateType.SmallDateTime => (SmallDateTimeMin, SmallDateTimeMax),
        SqlServerDateType.Date => (DateTime.MinValue, DateTime.MaxValue.Date),
        _ => (DateTime.MinValue, DateTime.MaxValue),
    };

    private static void RequireDate(Parsed p, string original)
    {
        if (!p.HasDate)
        {
            throw new FormatException($"Giá trị '{original}' thiếu phần ngày.");
        }
    }

    private static void EnsureRange(DateTime value, DateTime min, DateTime max, string typeName)
    {
        if (value < min || value > max)
        {
            throw OutOfRange(value, typeName);
        }
    }

    private static ArgumentOutOfRangeException OutOfRange(DateTime value, string typeName) =>
        new(nameof(value), value, $"Giá trị {value:O} nằm ngoài khoảng của kiểu {typeName}.");

    private static (string Name, int? Precision) ParseTypeName(string typeName, int maxPrecision)
    {
        ArgumentNullException.ThrowIfNull(typeName);
        var lower = typeName.Trim().ToLowerInvariant();
        int? precision = null;

        var pm = PrecisionRegex().Match(lower);
        if (pm.Success)
        {
            precision = int.Parse(pm.Groups[1].Value, Inv);
            if (precision > maxPrecision)
            {
                throw new NotSupportedException($"Độ chính xác {precision} trong '{typeName}' vượt quá {maxPrecision}.");
            }

            lower = PrecisionRegex().Replace(lower, " ");
        }

        return (WhitespaceRegex().Replace(lower, " ").Trim(), precision);
    }

    [GeneratedRegex(@"^(?:(?<date>\d{4}-\d{2}-\d{2})(?:[ T]|$))?(?:(?<h>\d{2}):(?<mi>\d{2})(?::(?<s>\d{2})(?:\.(?<f>\d{1,9}))?)?)?\s*(?<off>Z|[+-]\d{2}(?::?\d{2})?)?$")]
    private static partial Regex ValueRegex();

    [GeneratedRegex(@"\s*\(\s*(\d+)\s*\)\s*")]
    private static partial Regex PrecisionRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
