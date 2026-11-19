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
    /// <summary><c>datetimeoffset</c>: như datetime2, kèm độ lệch múi giờ (tối đa ±14:00, đơn vị phút).</summary>
    DateTimeOffset,
    /// <summary><c>date</c>.</summary>
    Date,
    /// <summary><c>time</c>: 00:00:00 → 23:59:59.9999999.</summary>
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
    /// <summary><c>time</c> (without time zone): 00:00:00 → 24:00:00.</summary>
    Time,
    /// <summary><c>timetz</c> (with time zone).</summary>
    TimeTz,
}

/// <summary>
/// Cách xử lý giá trị PostgreSQL mà .NET không biểu diễn được:
/// <c>infinity</c>, <c>-infinity</c>, ngày trước Công nguyên (<c>BC</c>) và năm sau 9999.
/// </summary>
public enum InfinityHandling
{
    /// <summary>
    /// Báo lỗi: <see cref="OverflowException"/> với infinity,
    /// <see cref="ArgumentOutOfRangeException"/> với ngày BC hoặc năm sau 9999.
    /// </summary>
    Throw,
    /// <summary>Đổi thành giá trị nhỏ nhất / lớn nhất của kiểu đích.</summary>
    ClampToRange,
}

/// <summary>Tùy chọn cho <see cref="DbDateTimeConverter"/>.</summary>
public sealed record DbDateTimeOptions
{
    private static readonly TimeSpan MaxDefaultOffset = TimeSpan.FromHours(14);
    private readonly TimeSpan _defaultOffset;

    /// <summary>Tùy chọn mặc định.</summary>
    public static DbDateTimeOptions Default { get; } = new();

    /// <summary>
    /// Độ lệch múi giờ dùng khi đích cần múi giờ (datetimeoffset, timestamptz, timetz)
    /// mà giá trị nguồn không có. Mặc định +00:00 (UTC).
    /// Phải là số phút nguyên trong khoảng -14:00 → +14:00 để hợp lệ với cả hai hệ.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Độ lệch ngoài khoảng hoặc có phần giây.</exception>
    public TimeSpan DefaultOffset
    {
        get => _defaultOffset;
        init
        {
            if (value.Ticks % TimeSpan.TicksPerMinute != 0 || value.Duration() > MaxDefaultOffset)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(DefaultOffset), value, "DefaultOffset phải là số phút nguyên trong khoảng -14:00 → +14:00.");
            }

            _defaultOffset = value;
        }
    }

    /// <summary>
    /// Cách xử lý infinity, ngày BC và năm sau 9999 khi chuyển sang SQL Server. Mặc định: báo lỗi.
    /// </summary>
    public InfinityHandling Infinity { get; init; } = InfinityHandling.Throw;
}

/// <summary>
/// Chuyển đổi kiểu dữ liệu và giá trị ngày giờ giữa PostgreSQL và SQL Server.
/// </summary>
/// <remarks>
/// <para>
/// Quy tắc múi giờ: khi giá trị nguồn có độ lệch (vd. timestamptz <c>+07</c>) mà kiểu đích
/// là kiểu dấu thời gian không có múi giờ (datetime, datetime2, smalldatetime, timestamp),
/// giá trị được đổi sang UTC. Với đích <c>date</c>/<c>time</c>, ngày/giờ được giữ nguyên
/// như trong giá trị nguồn và bỏ độ lệch.
/// </para>
/// <para>
/// Với đích <c>datetimeoffset</c>, độ lệch mà SQL Server không nhận (có phần giây như
/// <c>+07:06:40</c>, hoặc vượt ±14:00) được chuẩn hóa: giá trị đổi sang UTC, độ lệch thành <c>+00:00</c>.
/// </para>
/// </remarks>
public static partial class DbDateTimeConverter
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static readonly DateTime SqlDateTimeMin = new(1753, 1, 1);
    private static readonly DateTime SqlDateTimeMax = new DateTime(9999, 12, 31, 23, 59, 59).AddMilliseconds(997);
    private static readonly DateTime SmallDateTimeMin = new(1900, 1, 1);
    private static readonly DateTime SmallDateTimeMax = new(2079, 6, 6, 23, 59, 0);
    private static readonly TimeSpan SqlServerMaxOffset = TimeSpan.FromHours(14);
    private static readonly TimeSpan PostgresMaxOffset = new(15, 59, 59);

    // ───────────────────────────── Kiểu dữ liệu ─────────────────────────────

    /// <summary>
    /// Đổi tên kiểu PostgreSQL sang SQL Server.
    /// Ví dụ: <c>timestamp(3) with time zone</c> → <c>datetimeoffset(3)</c>.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="postgresType"/> là null.</exception>
    /// <exception cref="NotSupportedException">Kiểu không phải kiểu ngày giờ hoặc không có tương đương (interval).</exception>
    public static string PostgresTypeToSqlServer(string postgresType)
    {
        ArgumentNullException.ThrowIfNull(postgresType);
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
    /// <exception cref="ArgumentNullException"><paramref name="sqlServerType"/> là null.</exception>
    /// <exception cref="NotSupportedException">Kiểu không phải kiểu ngày giờ.</exception>
    public static string SqlServerTypeToPostgres(string sqlServerType)
    {
        ArgumentNullException.ThrowIfNull(sqlServerType);
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

    // ───────────────────────────── Giá trị: PostgreSQL → SQL Server ─────────────────────────────

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
        DbDateTimeOptions? options = null) =>
        postgresValue is null
            ? null
            : ToSqlServer(Parse(postgresValue), target, options ?? DbDateTimeOptions.Default, postgresValue.Trim());

    /// <summary>
    /// Chuyển một <see cref="DateTime"/> (vd. đọc từ Npgsql) sang chuỗi dùng được cho SQL Server.
    /// <see cref="DateTimeKind.Utc"/> được coi là có độ lệch +00:00, <see cref="DateTimeKind.Local"/>
    /// dùng độ lệch của máy, <see cref="DateTimeKind.Unspecified"/> là không có múi giờ.
    /// </summary>
    /// <inheritdoc cref="PostgresToSqlServer(string, SqlServerDateType, DbDateTimeOptions)" path="/exception"/>
    public static string PostgresToSqlServer(
        DateTime value,
        SqlServerDateType target = SqlServerDateType.DateTime2,
        DbDateTimeOptions? options = null) =>
        ToSqlServer(FromDateTime(value), target, options ?? DbDateTimeOptions.Default, value.ToString("O", Inv));

    /// <summary>Chuyển một <see cref="DateTimeOffset"/> sang chuỗi dùng được cho SQL Server.</summary>
    /// <inheritdoc cref="PostgresToSqlServer(string, SqlServerDateType, DbDateTimeOptions)" path="/exception"/>
    public static string PostgresToSqlServer(
        DateTimeOffset value,
        SqlServerDateType target = SqlServerDateType.DateTime2,
        DbDateTimeOptions? options = null) =>
        ToSqlServer(FromDateTimeOffset(value), target, options ?? DbDateTimeOptions.Default, value.ToString("O", Inv));

    // ───────────────────────────── Giá trị: SQL Server → PostgreSQL ─────────────────────────────

    /// <summary>
    /// Chuyển một giá trị ngày giờ dạng chuỗi của SQL Server sang chuỗi dùng được cho PostgreSQL.
    /// Phần lẻ dưới micro giây được làm tròn (PostgreSQL chỉ lưu tới 1 µs).
    /// Ví dụ: <c>2024-01-02 03:04:05.1234567 +07:00</c> → (timestamptz) <c>2024-01-02 03:04:05.123457+07:00</c>.
    /// </summary>
    /// <returns>Chuỗi định dạng ISO; <c>null</c> nếu đầu vào <c>null</c>.</returns>
    /// <exception cref="FormatException">Chuỗi không đúng định dạng.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Giá trị nằm ngoài khoảng ngày giờ của .NET.</exception>
    [return: NotNullIfNotNull(nameof(sqlServerValue))]
    public static string? SqlServerToPostgres(
        string? sqlServerValue,
        PostgresDateType target = PostgresDateType.Timestamp,
        DbDateTimeOptions? options = null) =>
        sqlServerValue is null
            ? null
            : ToPostgres(Parse(sqlServerValue), target, options ?? DbDateTimeOptions.Default, sqlServerValue.Trim());

    /// <summary>
    /// Chuyển một <see cref="DateTime"/> (vd. đọc từ SqlClient) sang chuỗi dùng được cho PostgreSQL.
    /// Quy ước về <see cref="DateTime.Kind"/> giống
    /// <see cref="PostgresToSqlServer(DateTime, SqlServerDateType, DbDateTimeOptions)"/>.
    /// </summary>
    public static string SqlServerToPostgres(
        DateTime value,
        PostgresDateType target = PostgresDateType.Timestamp,
        DbDateTimeOptions? options = null) =>
        ToPostgres(FromDateTime(value), target, options ?? DbDateTimeOptions.Default, value.ToString("O", Inv));

    /// <summary>Chuyển một <see cref="DateTimeOffset"/> sang chuỗi dùng được cho PostgreSQL.</summary>
    public static string SqlServerToPostgres(
        DateTimeOffset value,
        PostgresDateType target = PostgresDateType.Timestamp,
        DbDateTimeOptions? options = null) =>
        ToPostgres(FromDateTimeOffset(value), target, options ?? DbDateTimeOptions.Default, value.ToString("O", Inv));

    // ───────────────────────────── Làm tròn .NET ─────────────────────────────

    /// <summary>
    /// Làm tròn như SQL Server <c>datetime</c> (bước .000 / .003 / .007 giây).
    /// Ví dụ: 23:59:59.998 → 23:59:59.997; 23:59:59.999 → 00:00:00.000 ngày hôm sau.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Ngoài khoảng 1753-01-01 → 9999-12-31 23:59:59.997.</exception>
    public static DateTime RoundToSqlServerDateTime(DateTime value)
    {
        if (value < SqlDateTimeMin)
        {
            throw OutOfRange(value, "datetime");
        }

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

    // ───────────────────────────── Lõi chuyển đổi ─────────────────────────────

    /// <param name="Value">Ngày giờ (giờ địa phương theo <paramref name="Offset"/>, nếu có).</param>
    /// <param name="Offset">Độ lệch múi giờ, null nếu không có.</param>
    /// <param name="HasDate">Giá trị có phần ngày.</param>
    /// <param name="TimeTicks">Phần giờ tính bằng tick, 0 → TicksPerDay (TicksPerDay nghĩa là 24:00:00).</param>
    /// <param name="Overflow">-1/+1 khi giá trị nằm ngoài khoảng .NET (infinity, BC, năm sau 9999).</param>
    /// <param name="IsInfinity">Giá trị là <c>infinity</c>/<c>-infinity</c>.</param>
    private readonly record struct Parsed(
        DateTime Value, TimeSpan? Offset, bool HasDate, long TimeTicks, int Overflow, bool IsInfinity);

    private static string ToSqlServer(Parsed p, SqlServerDateType target, DbDateTimeOptions options, string original)
    {
        if (p.Overflow != 0)
        {
            if (options.Infinity == InfinityHandling.ClampToRange && target != SqlServerDateType.Time)
            {
                var (min, max) = SqlServerRange(target);
                p = FromDateTime(p.Overflow > 0 ? max : min);
            }
            else if (p.IsInfinity)
            {
                throw new OverflowException($"SQL Server không biểu diễn được '{original}' cho kiểu {target}.");
            }
            else
            {
                throw new ArgumentOutOfRangeException(
                    nameof(original), original, $"'{original}' nằm ngoài khoảng ngày giờ mà SQL Server hỗ trợ.");
            }
        }

        switch (target)
        {
            case SqlServerDateType.Date:
                RequireDate(p, original);
                return p.Value.ToString("yyyy-MM-dd", Inv);

            case SqlServerDateType.Time:
                // SQL Server time tối đa 23:59:59.9999999 (không có 24:00:00).
                var timeTicks = Math.Min(p.TimeTicks, TimeSpan.TicksPerDay - 1);
                return new DateTime(timeTicks).ToString("HH:mm:ss.FFFFFFF", Inv);

            case SqlServerDateType.DateTimeOffset:
            {
                RequireDate(p, original);
                var offset = p.Offset ?? options.DefaultOffset;
                var utc = ToUtc(p.Value, offset, original);
                var local = p.Value;
                if (offset.Ticks % TimeSpan.TicksPerMinute != 0 || offset.Duration() > SqlServerMaxOffset)
                {
                    local = utc;
                    offset = TimeSpan.Zero;
                }

                return local.ToString("yyyy-MM-ddTHH:mm:ss.FFFFFFF", Inv) + FormatOffset(offset);
            }

            case SqlServerDateType.DateTime:
                RequireDate(p, original);
                return RoundToSqlServerDateTime(ToUtcIfZoned(p, original)).ToString("yyyy-MM-ddTHH:mm:ss.fff", Inv);

            case SqlServerDateType.SmallDateTime:
                RequireDate(p, original);
                return RoundToSmallDateTime(ToUtcIfZoned(p, original)).ToString("yyyy-MM-ddTHH:mm:ss", Inv);

            case SqlServerDateType.DateTime2:
                RequireDate(p, original);
                return ToUtcIfZoned(p, original).ToString("yyyy-MM-ddTHH:mm:ss.FFFFFFF", Inv);

            default:
                throw new ArgumentOutOfRangeException(nameof(target), target, null);
        }
    }

    private static string ToPostgres(Parsed p, PostgresDateType target, DbDateTimeOptions options, string original)
    {
        if (p.Overflow != 0)
        {
            if (!p.IsInfinity)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(original), original, $"'{original}' nằm ngoài khoảng ngày giờ của .NET.");
            }

            return target is PostgresDateType.Time or PostgresDateType.TimeTz
                ? throw new OverflowException($"Kiểu {target} của PostgreSQL không nhận '{original}'.")
                : p.Overflow > 0 ? "infinity" : "-infinity";
        }

        switch (target)
        {
            case PostgresDateType.Timestamp:
                RequireDate(p, original);
                return RoundToPostgres(ToUtcIfZoned(p, original)).ToString("yyyy-MM-dd HH:mm:ss.FFFFFF", Inv);

            case PostgresDateType.TimestampTz:
                RequireDate(p, original);
                return RoundToPostgres(p.Value).ToString("yyyy-MM-dd HH:mm:ss.FFFFFF", Inv)
                    + FormatOffset(p.Offset ?? options.DefaultOffset);

            case PostgresDateType.Date:
                RequireDate(p, original);
                return p.Value.ToString("yyyy-MM-dd", Inv);

            case PostgresDateType.Time:
                return FormatPostgresTime(p.TimeTicks);

            case PostgresDateType.TimeTz:
                return FormatPostgresTime(p.TimeTicks) + FormatOffset(p.Offset ?? options.DefaultOffset);

            default:
                throw new ArgumentOutOfRangeException(nameof(target), target, null);
        }
    }

    // ───────────────────────────── Đọc giá trị ─────────────────────────────

    private static Parsed FromDateTime(DateTime value)
    {
        TimeSpan? offset = value.Kind switch
        {
            DateTimeKind.Utc => TimeSpan.Zero,
            DateTimeKind.Local => TimeZoneInfo.Local.GetUtcOffset(value),
            _ => null,
        };

        return new Parsed(
            DateTime.SpecifyKind(value, DateTimeKind.Unspecified), offset, true, value.TimeOfDay.Ticks, 0, false);
    }

    private static Parsed FromDateTimeOffset(DateTimeOffset value) =>
        new(value.DateTime, value.Offset, true, value.TimeOfDay.Ticks, 0, false);

    private static Parsed Parse(string text)
    {
        var s = text.Trim();
        if (s.Equals("infinity", StringComparison.OrdinalIgnoreCase)
            || s.Equals("+infinity", StringComparison.OrdinalIgnoreCase))
        {
            return new Parsed(DateTime.MaxValue, null, true, 0, 1, true);
        }

        if (s.Equals("-infinity", StringComparison.OrdinalIgnoreCase))
        {
            return new Parsed(DateTime.MinValue, null, true, 0, -1, true);
        }

        var m = ValueRegex().Match(s);
        var hasDate = m.Groups["y"].Success;
        var hasTime = m.Groups["h"].Success;
        var isBc = m.Groups["bc"].Success;
        if (!m.Success || (!hasDate && !hasTime) || (isBc && !hasDate))
        {
            throw new FormatException($"Không nhận dạng được giá trị ngày giờ '{text}'.");
        }

        long timeTicks = 0;
        if (hasTime)
        {
            var h = int.Parse(m.Groups["h"].Value, Inv);
            var mi = int.Parse(m.Groups["mi"].Value, Inv);
            var sec = m.Groups["s"].Success ? int.Parse(m.Groups["s"].Value, Inv) : 0;
            var fraction = m.Groups["f"].Value;
            // PostgreSQL cho phép đúng 24:00:00 (cuối ngày).
            var isEndOfDay = h == 24 && mi == 0 && sec == 0 && fraction.TrimEnd('0').Length == 0;
            if ((h > 23 && !isEndOfDay) || mi > 59 || sec > 59)
            {
                throw new FormatException($"Giờ không hợp lệ trong '{text}'.");
            }

            timeTicks = h * TimeSpan.TicksPerHour + mi * TimeSpan.TicksPerMinute
                + sec * TimeSpan.TicksPerSecond + FractionToTicks(fraction);
        }

        TimeSpan? offset = m.Groups["off"].Success ? ParseOffset(m.Groups["off"].Value, text) : null;

        if (!hasDate)
        {
            return new Parsed(new DateTime(timeTicks), offset, false, timeTicks, 0, false);
        }

        var yearText = m.Groups["y"].Value;
        var monthDay = m.Groups["md"].Value;
        if (isBc || yearText.Length > 4)
        {
            // Ngày BC hoặc năm > 9999: hợp lệ trong PostgreSQL nhưng .NET không biểu diễn được.
            if (yearText.TrimStart('0').Length == 0
                || !DateTime.TryParseExact("2000" + monthDay, "yyyy-MM-dd", Inv, DateTimeStyles.None, out _))
            {
                throw new FormatException($"Ngày không hợp lệ trong '{text}'.");
            }

            var sign = isBc ? -1 : 1;
            return new Parsed(sign > 0 ? DateTime.MaxValue : DateTime.MinValue, offset, true, timeTicks, sign, false);
        }

        if (!DateTime.TryParseExact(yearText + monthDay, "yyyy-MM-dd", Inv, DateTimeStyles.None, out var date))
        {
            throw new FormatException($"Ngày không hợp lệ trong '{text}'.");
        }

        if (date.Ticks + timeTicks > DateTime.MaxValue.Ticks)
        {
            // vd. 9999-12-31 24:00:00 → sang năm 10000.
            return new Parsed(DateTime.MaxValue, offset, true, timeTicks, 1, false);
        }

        return new Parsed(date.AddTicks(timeTicks), offset, true, timeTicks, 0, false);
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

    /// <summary>Đọc độ lệch: <c>Z</c>, <c>+07</c>, <c>+0700</c>, <c>+07:00</c>, <c>+07:06:40</c>.</summary>
    private static TimeSpan ParseOffset(string value, string original)
    {
        if (value is "Z" or "z")
        {
            return TimeSpan.Zero;
        }

        var sign = value[0] == '-' ? -1 : 1;
        var digits = value[1..].Replace(":", "");
        var hours = int.Parse(digits[..2], Inv);
        var minutes = digits.Length >= 4 ? int.Parse(digits[2..4], Inv) : 0;
        var seconds = digits.Length >= 6 ? int.Parse(digits[4..6], Inv) : 0;
        var offset = new TimeSpan(hours, minutes, seconds);
        if (minutes > 59 || seconds > 59 || offset > PostgresMaxOffset)
        {
            throw new FormatException($"Độ lệch múi giờ không hợp lệ trong '{original}'.");
        }

        return sign * offset;
    }

    // ───────────────────────────── Tiện ích nội bộ ─────────────────────────────

    /// <summary>Định dạng độ lệch: <c>+07:00</c>, hoặc <c>+07:06:40</c> khi có phần giây.</summary>
    private static string FormatOffset(TimeSpan offset)
    {
        var abs = offset.Duration();
        var text = (offset < TimeSpan.Zero ? "-" : "+") + abs.ToString(@"hh\:mm", Inv);
        return abs.Seconds != 0 ? text + abs.ToString(@"\:ss", Inv) : text;
    }

    /// <summary>Làm tròn phần giờ tới micro giây; tràn qua cuối ngày thành <c>24:00:00</c> (PostgreSQL chấp nhận).</summary>
    private static string FormatPostgresTime(long timeTicks)
    {
        var remainder = timeTicks % 10;
        var ticks = timeTicks - remainder + (remainder >= 5 ? 10 : 0);
        return ticks >= TimeSpan.TicksPerDay ? "24:00:00" : new DateTime(ticks).ToString("HH:mm:ss.FFFFFF", Inv);
    }

    private static DateTime ToUtcIfZoned(Parsed p, string original) =>
        p.Offset is { } offset ? ToUtc(p.Value, offset, original) : p.Value;

    /// <summary>
    /// Đổi sang UTC bằng phép trừ tick (không dùng <see cref="DateTimeOffset"/>, vì nó không nhận
    /// độ lệch có phần giây hoặc vượt ±14:00).
    /// </summary>
    private static DateTime ToUtc(DateTime value, TimeSpan offset, string original)
    {
        var ticks = value.Ticks - offset.Ticks;
        if (ticks < 0 || ticks > DateTime.MaxValue.Ticks)
        {
            throw new ArgumentOutOfRangeException(
                nameof(original), original, $"'{original}' đổi sang UTC nằm ngoài khoảng ngày giờ của .NET.");
        }

        return new DateTime(ticks);
    }

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

    private static ArgumentOutOfRangeException OutOfRange(DateTime value, string typeName) =>
        new(nameof(value), value, $"Giá trị {value:O} nằm ngoài khoảng của kiểu {typeName}.");

    private static (string Name, int? Precision) ParseTypeName(string typeName, int maxPrecision)
    {
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

            lower = lower.Remove(pm.Index, pm.Length).Insert(pm.Index, " ");
        }

        return (WhitespaceRegex().Replace(lower, " ").Trim(), precision);
    }

    [GeneratedRegex(@"^(?:(?<y>\d{4,})(?<md>-\d{2}-\d{2})(?:[ T]|$))?(?:(?<h>\d{2}):(?<mi>\d{2})(?::(?<s>\d{2})(?:\.(?<f>\d{1,9}))?)?)?\s*(?<off>Z|[+-]\d{2}(?::?\d{2}(?::?\d{2})?)?)?(?:\s*(?<bc>BC))?$", RegexOptions.IgnoreCase)]
    private static partial Regex ValueRegex();

    [GeneratedRegex(@"\s*\(\s*(\d+)\s*\)\s*")]
    private static partial Regex PrecisionRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
