using ThuVien;

namespace ThuVien.Tests;

public class DbDateTimeTypeMappingTests
{
    [Theory]
    [InlineData("timestamp", "datetime2(6)")]
    [InlineData("timestamp(3)", "datetime2(3)")]
    [InlineData("TIMESTAMP WITHOUT TIME ZONE", "datetime2(6)")]
    [InlineData("timestamptz", "datetimeoffset(6)")]
    [InlineData("timestamp(3) with time zone", "datetimeoffset(3)")]
    [InlineData("date", "date")]
    [InlineData("time(2)", "time(2)")]
    [InlineData("timetz", "time(6)")]
    public void PostgresTypeToSqlServer_Works(string input, string expected) =>
        Assert.Equal(expected, DbDateTimeConverter.PostgresTypeToSqlServer(input));

    [Theory]
    [InlineData("datetime", "timestamp(3)")]
    [InlineData("smalldatetime", "timestamp(0)")]
    [InlineData("datetime2", "timestamp(6)")]
    [InlineData("DATETIME2(7)", "timestamp(6)")]
    [InlineData("[datetime2](3)", "timestamp(3)")]
    [InlineData("datetimeoffset(7)", "timestamptz(6)")]
    [InlineData("date", "date")]
    [InlineData("time(7)", "time(6)")]
    public void SqlServerTypeToPostgres_Works(string input, string expected) =>
        Assert.Equal(expected, DbDateTimeConverter.SqlServerTypeToPostgres(input));

    [Theory]
    [InlineData("interval")]
    [InlineData("text")]
    [InlineData("timestamp(7)")]
    public void PostgresTypeToSqlServer_Unsupported_Throws(string input) =>
        Assert.Throws<NotSupportedException>(() => DbDateTimeConverter.PostgresTypeToSqlServer(input));

    [Fact]
    public void SqlServerTypeToPostgres_Unsupported_Throws() =>
        Assert.Throws<NotSupportedException>(() => DbDateTimeConverter.SqlServerTypeToPostgres("nvarchar(50)"));
}

public class PostgresToSqlServerTests
{
    [Theory]
    [InlineData("2024-01-02 03:04:05.123456", SqlServerDateType.DateTime2, "2024-01-02T03:04:05.123456")]
    [InlineData("2024-01-02 03:04:05", SqlServerDateType.DateTime2, "2024-01-02T03:04:05")]
    [InlineData("2024-01-02T03:04:05.5", SqlServerDateType.DateTime2, "2024-01-02T03:04:05.5")]
    [InlineData("2024-01-02 03:04:05+07", SqlServerDateType.DateTime2, "2024-01-01T20:04:05")]
    [InlineData("2024-01-02 03:04:05-05:30", SqlServerDateType.DateTime2, "2024-01-02T08:34:05")]
    [InlineData("2024-01-02 03:04:05.123456+07", SqlServerDateType.DateTimeOffset, "2024-01-02T03:04:05.123456+07:00")]
    [InlineData("2024-01-02 03:04:05", SqlServerDateType.DateTimeOffset, "2024-01-02T03:04:05+00:00")]
    [InlineData("2024-01-02 03:04:05.998", SqlServerDateType.DateTime, "2024-01-02T03:04:05.997")]
    [InlineData("2024-01-02 03:04:05.992", SqlServerDateType.DateTime, "2024-01-02T03:04:05.993")]
    [InlineData("2024-01-02 03:04:05.991", SqlServerDateType.DateTime, "2024-01-02T03:04:05.990")]
    [InlineData("2024-01-02 23:59:59.999", SqlServerDateType.DateTime, "2024-01-03T00:00:00.000")]
    [InlineData("2024-01-02 03:04:29.998", SqlServerDateType.SmallDateTime, "2024-01-02T03:04:00")]
    [InlineData("2024-01-02 03:04:29.999", SqlServerDateType.SmallDateTime, "2024-01-02T03:05:00")]
    [InlineData("2024-01-02 03:04:05", SqlServerDateType.Date, "2024-01-02")]
    [InlineData("2024-01-02", SqlServerDateType.DateTime2, "2024-01-02T00:00:00")]
    [InlineData("03:04:05.123456", SqlServerDateType.Time, "03:04:05.123456")]
    [InlineData("03:04:05+07", SqlServerDateType.Time, "03:04:05")]
    public void Converts(string input, SqlServerDateType target, string expected) =>
        Assert.Equal(expected, DbDateTimeConverter.PostgresToSqlServer(input, target));

    [Fact]
    public void Null_ReturnsNull() =>
        Assert.Null(DbDateTimeConverter.PostgresToSqlServer(null));

    [Fact]
    public void DefaultOffset_IsUsed()
    {
        var options = new DbDateTimeOptions { DefaultOffset = TimeSpan.FromHours(7) };
        Assert.Equal(
            "2024-01-02T03:04:05+07:00",
            DbDateTimeConverter.PostgresToSqlServer("2024-01-02 03:04:05", SqlServerDateType.DateTimeOffset, options));
    }

    [Fact]
    public void Infinity_ThrowsByDefault() =>
        Assert.Throws<OverflowException>(() => DbDateTimeConverter.PostgresToSqlServer("infinity"));

    [Theory]
    [InlineData("infinity", SqlServerDateType.DateTime, "9999-12-31T23:59:59.997")]
    [InlineData("-infinity", SqlServerDateType.DateTime, "1753-01-01T00:00:00.000")]
    [InlineData("infinity", SqlServerDateType.SmallDateTime, "2079-06-06T23:59:00")]
    [InlineData("-infinity", SqlServerDateType.DateTime2, "0001-01-01T00:00:00")]
    [InlineData("infinity", SqlServerDateType.DateTime2, "9999-12-31T23:59:59.9999999")]
    [InlineData("infinity", SqlServerDateType.Date, "9999-12-31")]
    public void Infinity_ClampToRange(string input, SqlServerDateType target, string expected)
    {
        var options = new DbDateTimeOptions { Infinity = InfinityHandling.ClampToRange };
        Assert.Equal(expected, DbDateTimeConverter.PostgresToSqlServer(input, target, options));
    }

    [Theory]
    [InlineData("1700-01-01 00:00:00", SqlServerDateType.DateTime)]
    [InlineData("2080-01-01 00:00:00", SqlServerDateType.SmallDateTime)]
    [InlineData("1899-12-31 23:59:00", SqlServerDateType.SmallDateTime)]
    public void OutOfRange_Throws(string input, SqlServerDateType target) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => DbDateTimeConverter.PostgresToSqlServer(input, target));

    [Theory]
    [InlineData("hôm qua")]
    [InlineData("2024-13-01")]
    [InlineData("2024-01-02 25:00:00")]
    [InlineData("")]
    public void InvalidFormat_Throws(string input) =>
        Assert.Throws<FormatException>(() => DbDateTimeConverter.PostgresToSqlServer(input));

    [Fact]
    public void TimeOnly_ToDateTarget_Throws() =>
        Assert.Throws<FormatException>(() => DbDateTimeConverter.PostgresToSqlServer("03:04:05", SqlServerDateType.DateTime2));
}

public class SqlServerToPostgresTests
{
    [Theory]
    [InlineData("2024-01-02 03:04:05.1234567", PostgresDateType.Timestamp, "2024-01-02 03:04:05.123457")]
    [InlineData("2024-01-02 03:04:05.1234564", PostgresDateType.Timestamp, "2024-01-02 03:04:05.123456")]
    [InlineData("2024-01-02T03:04:05.997", PostgresDateType.Timestamp, "2024-01-02 03:04:05.997")]
    [InlineData("2024-01-02 03:04:00", PostgresDateType.Timestamp, "2024-01-02 03:04:00")]
    [InlineData("2024-01-02 03:04:05 +07:00", PostgresDateType.Timestamp, "2024-01-01 20:04:05")]
    [InlineData("2024-01-02 03:04:05.1234567 +07:00", PostgresDateType.TimestampTz, "2024-01-02 03:04:05.123457+07:00")]
    [InlineData("2024-01-02 03:04:05", PostgresDateType.TimestampTz, "2024-01-02 03:04:05+00:00")]
    [InlineData("2024-01-02 03:04:05", PostgresDateType.Date, "2024-01-02")]
    [InlineData("03:04:05.1234567", PostgresDateType.Time, "03:04:05.123457")]
    [InlineData("23:59:59.9999999", PostgresDateType.Time, "24:00:00")]
    [InlineData("03:04:05 -03:00", PostgresDateType.TimeTz, "03:04:05-03:00")]
    [InlineData("9999-12-31 23:59:59.9999999", PostgresDateType.Timestamp, "9999-12-31 23:59:59.999999")]
    public void Converts(string input, PostgresDateType target, string expected) =>
        Assert.Equal(expected, DbDateTimeConverter.SqlServerToPostgres(input, target));

    [Fact]
    public void Null_ReturnsNull() =>
        Assert.Null(DbDateTimeConverter.SqlServerToPostgres(null));

    [Fact]
    public void RoundTrip_DateTimeOffset()
    {
        const string pg = "2024-06-15 10:20:30.654321+07:00";
        var sql = DbDateTimeConverter.PostgresToSqlServer(pg, SqlServerDateType.DateTimeOffset);
        Assert.Equal(pg, DbDateTimeConverter.SqlServerToPostgres(sql, PostgresDateType.TimestampTz));
    }
}

public class DbDateTimeRoundingTests
{
    [Fact]
    public void RoundToSqlServerDateTime_BelowRange_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DbDateTimeConverter.RoundToSqlServerDateTime(new DateTime(1752, 12, 31)));

    [Fact]
    public void RoundToSqlServerDateTime_AboveRange_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DbDateTimeConverter.RoundToSqlServerDateTime(DateTime.MaxValue));

    [Fact]
    public void RoundToSqlServerDateTime_KeepsKind()
    {
        var utc = new DateTime(2024, 1, 2, 3, 4, 5, 998, DateTimeKind.Utc);
        var result = DbDateTimeConverter.RoundToSqlServerDateTime(utc);
        Assert.Equal(DateTimeKind.Utc, result.Kind);
        Assert.Equal(997, result.Millisecond);
    }

    [Fact]
    public void RoundToPostgres_RoundsToMicroseconds()
    {
        var value = new DateTime(2024, 1, 2).AddTicks(15);
        Assert.Equal(20, DbDateTimeConverter.RoundToPostgres(value).Ticks - new DateTime(2024, 1, 2).Ticks);
    }

    [Fact]
    public void RoundToPostgres_MaxValue_DoesNotOverflow() =>
        Assert.Equal(DateTime.MaxValue.Ticks - 9, DbDateTimeConverter.RoundToPostgres(DateTime.MaxValue).Ticks);
}

/// <summary>Các trường hợp từ issue #4.</summary>
public class DbDateTimeIssue4Tests
{
    private static readonly DbDateTimeOptions Clamp = new() { Infinity = InfinityHandling.ClampToRange };

    // ── 24:00:00 ──
    [Theory]
    [InlineData("24:00:00", SqlServerDateType.Time, "23:59:59.9999999")]
    [InlineData("24:00:00.000", SqlServerDateType.Time, "23:59:59.9999999")]
    [InlineData("2024-01-02 24:00:00", SqlServerDateType.DateTime2, "2024-01-03T00:00:00")]
    [InlineData("2024-01-02 24:00:00", SqlServerDateType.Date, "2024-01-03")]
    public void EndOfDay_PostgresToSqlServer(string input, SqlServerDateType target, string expected) =>
        Assert.Equal(expected, DbDateTimeConverter.PostgresToSqlServer(input, target));

    [Fact]
    public void EndOfDay_RoundTrip()
    {
        var pg = DbDateTimeConverter.SqlServerToPostgres("23:59:59.9999999", PostgresDateType.Time);
        Assert.Equal("24:00:00", pg);
        Assert.Equal("24:00:00", DbDateTimeConverter.SqlServerToPostgres(pg, PostgresDateType.Time));
        Assert.Equal("23:59:59.9999999", DbDateTimeConverter.PostgresToSqlServer(pg, SqlServerDateType.Time));
    }

    [Theory]
    [InlineData("24:00:01")]
    [InlineData("24:00:00.5")]
    [InlineData("24:01:00")]
    public void EndOfDay_Invalid_Throws(string input) =>
        Assert.Throws<FormatException>(() => DbDateTimeConverter.PostgresToSqlServer(input, SqlServerDateType.Time));

    [Fact]
    public void EndOfDay_AtMaxDate_IsOverflow() =>
        Assert.Equal(
            "9999-12-31T23:59:59.9999999",
            DbDateTimeConverter.PostgresToSqlServer("9999-12-31 24:00:00", SqlServerDateType.DateTime2, Clamp));

    // ── Độ lệch có giây (LMT) ──
    [Theory]
    [InlineData("1900-01-01 00:00:00+07:06:40", SqlServerDateType.DateTime2, "1899-12-31T16:53:20")]
    [InlineData("1900-01-01 00:00:00+070640", SqlServerDateType.DateTime2, "1899-12-31T16:53:20")]
    [InlineData("1900-01-01 00:00:00+07:06:40", SqlServerDateType.DateTimeOffset, "1899-12-31T16:53:20+00:00")]
    [InlineData("1900-01-01 00:00:00-00:25:21", SqlServerDateType.DateTimeOffset, "1900-01-01T00:25:21+00:00")]
    public void SecondsOffset_PostgresToSqlServer(string input, SqlServerDateType target, string expected) =>
        Assert.Equal(expected, DbDateTimeConverter.PostgresToSqlServer(input, target));

    [Theory]
    [InlineData("1900-01-01 00:00:00+07:06:40", PostgresDateType.TimestampTz, "1900-01-01 00:00:00+07:06:40")]
    [InlineData("03:04:05+07:06:40", PostgresDateType.TimeTz, "03:04:05+07:06:40")]
    public void SecondsOffset_KeptForPostgres(string input, PostgresDateType target, string expected) =>
        Assert.Equal(expected, DbDateTimeConverter.SqlServerToPostgres(input, target));

    // ── Phần lẻ giây làm tròn tràn ngày ──
    [Theory]
    [InlineData("23:59:59.99999995", SqlServerDateType.Time, "23:59:59.9999999")]
    [InlineData("2024-01-02 23:59:59.99999995", SqlServerDateType.Time, "00:00:00")]
    [InlineData("2024-01-02 23:59:59.99999995", SqlServerDateType.DateTime2, "2024-01-03T00:00:00")]
    public void FractionRoundUp_DoesNotWrap(string input, SqlServerDateType target, string expected) =>
        Assert.Equal(expected, DbDateTimeConverter.PostgresToSqlServer(input, target));

    // ── Kiểm tra độ lệch ──
    [Fact]
    public void OffsetBeyondSqlServerLimit_IsNormalizedToUtc() =>
        Assert.Equal(
            "2024-01-01T12:34:05+00:00",
            DbDateTimeConverter.PostgresToSqlServer("2024-01-02 03:04:05+14:30", SqlServerDateType.DateTimeOffset));

    [Theory]
    [InlineData("2024-01-02 03:04:05+16:00")]
    [InlineData("2024-01-02 03:04:05+07:60")]
    [InlineData("2024-01-02 03:04:05+07:00:60")]
    public void InvalidOffset_ThrowsFormatException(string input) =>
        Assert.Throws<FormatException>(() => DbDateTimeConverter.PostgresToSqlServer(input));

    [Fact]
    public void LowercaseZ_IsUtc() =>
        Assert.Equal("2024-01-02T03:04:05", DbDateTimeConverter.PostgresToSqlServer("2024-01-02t03:04:05z"));

    [Theory]
    [InlineData(20 * 60)]
    [InlineData(-15 * 60)]
    public void DefaultOffset_OutOfRange_Throws(int minutes) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new DbDateTimeOptions { DefaultOffset = TimeSpan.FromMinutes(minutes) });

    [Fact]
    public void DefaultOffset_WithSeconds_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new DbDateTimeOptions { DefaultOffset = new TimeSpan(7, 0, 30) });

    [Fact]
    public void Offset_UtcUnderflow_ThrowsOutOfRange() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DbDateTimeConverter.PostgresToSqlServer("0001-01-01 00:00:00+07", SqlServerDateType.DateTime2));

    // ── null tên kiểu ──
    [Fact]
    public void NullTypeName_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() => DbDateTimeConverter.SqlServerTypeToPostgres(null!));
        Assert.Throws<ArgumentNullException>(() => DbDateTimeConverter.PostgresTypeToSqlServer(null!));
    }

    // ── BC và năm > 9999 ──
    [Theory]
    [InlineData("0044-03-15 00:00:00 BC")]
    [InlineData("0044-03-15 BC")]
    [InlineData("0044-03-15 00:00:00+00 BC")]
    [InlineData("10000-01-01 00:00:00")]
    public void OutsideDotNetRange_ThrowsByDefault(string input) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => DbDateTimeConverter.PostgresToSqlServer(input));

    [Theory]
    [InlineData("0044-03-15 00:00:00 BC", SqlServerDateType.DateTime2, "0001-01-01T00:00:00")]
    [InlineData("0044-03-15 bc", SqlServerDateType.DateTime, "1753-01-01T00:00:00.000")]
    [InlineData("10000-01-01 00:00:00", SqlServerDateType.DateTime, "9999-12-31T23:59:59.997")]
    [InlineData("294276-12-31 23:59:59", SqlServerDateType.SmallDateTime, "2079-06-06T23:59:00")]
    public void OutsideDotNetRange_ClampToRange(string input, SqlServerDateType target, string expected) =>
        Assert.Equal(expected, DbDateTimeConverter.PostgresToSqlServer(input, target, Clamp));

    [Theory]
    [InlineData("0000-01-01 00:00:00 BC")]
    [InlineData("0044-13-15 BC")]
    [InlineData("12:00:00 BC")]
    public void InvalidBc_ThrowsFormatException(string input) =>
        Assert.Throws<FormatException>(() => DbDateTimeConverter.PostgresToSqlServer(input, SqlServerDateType.DateTime2, Clamp));

    // ── Overload DateTime / DateTimeOffset ──
    [Fact]
    public void Typed_DateTime_ToSqlServer() =>
        Assert.Equal(
            "2024-01-02T03:04:05.997",
            DbDateTimeConverter.ToSqlServer(new DateTime(2024, 1, 2, 3, 4, 5, 998), SqlServerDateType.DateTime));

    [Fact]
    public void Typed_UtcDateTime_GetsZeroOffset() =>
        Assert.Equal(
            "2024-01-02T03:04:05+00:00",
            DbDateTimeConverter.ToSqlServer(
                new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc), SqlServerDateType.DateTimeOffset));

    [Fact]
    public void Typed_DateTimeOffset_ToSqlServer() =>
        Assert.Equal(
            "2024-01-01T20:04:05",
            DbDateTimeConverter.ToSqlServer(
                new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.FromHours(7)), SqlServerDateType.DateTime2));

    [Fact]
    public void Typed_DateTime_ToPostgres_RoundsToMicroseconds() =>
        Assert.Equal(
            "2024-01-02 03:04:05.123457",
            DbDateTimeConverter.ToPostgres(
                new DateTime(2024, 1, 2, 3, 4, 5).AddTicks(1_234_567), PostgresDateType.Timestamp));

    [Fact]
    public void Typed_DateTimeOffset_ToPostgres() =>
        Assert.Equal(
            "2024-01-02 03:04:05-03:30",
            DbDateTimeConverter.ToPostgres(
                new DateTimeOffset(2024, 1, 2, 3, 4, 5, new TimeSpan(-3, -30, 0)), PostgresDateType.TimestampTz));

    [Fact]
    public void Typed_DateTime_ToPostgresTime() =>
        Assert.Equal(
            "00:00:00",
            DbDateTimeConverter.ToPostgres(
                new DateTime(2024, 1, 2).AddTicks(TimeSpan.TicksPerDay - 1), PostgresDateType.Time));
}

/// <summary>Các trường hợp từ lần review commit 0872389.</summary>
public class DbDateTimeReview2Tests
{
    // ── ClampToRange + DefaultOffset ──
    [Theory]
    [InlineData("infinity", -5, "9999-12-31T23:59:59.9999999+00:00")]
    [InlineData("-infinity", 7, "0001-01-01T00:00:00+00:00")]
    [InlineData("0044-03-15 00:00:00 BC", 7, "0001-01-01T00:00:00+00:00")]
    public void Clamp_IgnoresDefaultOffset(string input, int offsetHours, string expected)
    {
        var options = new DbDateTimeOptions
        {
            Infinity = InfinityHandling.ClampToRange,
            DefaultOffset = TimeSpan.FromHours(offsetHours),
        };
        Assert.Equal(expected, DbDateTimeConverter.PostgresToSqlServer(input, SqlServerDateType.DateTimeOffset, options));
    }

    // ── Phần giờ của giá trị có ngày khớp với phần ngày ──
    [Theory]
    [InlineData("2024-01-02 24:00:00", "00:00:00")]
    [InlineData("2024-01-02 23:59:59.99999995", "00:00:00")]
    [InlineData("24:00:00", "23:59:59.9999999")]
    [InlineData("23:59:59.99999995", "23:59:59.9999999")]
    public void PostgresToSqlServer_TimePart_MatchesDate(string input, string expected) =>
        Assert.Equal(expected, DbDateTimeConverter.PostgresToSqlServer(input, SqlServerDateType.Time));

    [Theory]
    [InlineData("2024-01-02 23:59:59.9999999", PostgresDateType.Time, "00:00:00")]
    [InlineData("2024-01-02 23:59:59.9999999", PostgresDateType.Timestamp, "2024-01-03 00:00:00")]
    [InlineData("23:59:59.9999999", PostgresDateType.Time, "24:00:00")]
    [InlineData("2024-01-02 23:59:59.9999999 +07:00", PostgresDateType.TimeTz, "00:00:00+07:00")]
    public void SqlServerToPostgres_TimePart_MatchesDate(string input, PostgresDateType target, string expected) =>
        Assert.Equal(expected, DbDateTimeConverter.SqlServerToPostgres(input, target));

    // ── Năm có số 0 ở đầu ──
    [Theory]
    [InlineData("02024-01-02 00:00:00", "2024-01-02T00:00:00")]
    [InlineData("000999-01-02", "0999-01-02T00:00:00")]
    public void ZeroPaddedYear_IsNotOverflow(string input, string expected) =>
        Assert.Equal(expected, DbDateTimeConverter.PostgresToSqlServer(input, SqlServerDateType.DateTime2));

    [Theory]
    [InlineData("0000-01-01")]
    [InlineData("00000-01-01")]
    public void YearZero_ThrowsFormatException(string input) =>
        Assert.Throws<FormatException>(() => DbDateTimeConverter.PostgresToSqlServer(input));

    // ── Ngày BC / năm > 9999 sang đích time ──
    [Theory]
    [InlineData("0044-03-15 10:30:00 BC", "10:30:00")]
    [InlineData("10000-01-01 08:15:30.5", "08:15:30.5")]
    [InlineData("9999-12-31 24:00:00", "00:00:00")]
    public void OutOfRangeDate_ToTime_KeepsTime(string input, string expected) =>
        Assert.Equal(expected, DbDateTimeConverter.PostgresToSqlServer(input, SqlServerDateType.Time));

    [Fact]
    public void OutOfRangeDate_ToPostgresTime_KeepsTime() =>
        Assert.Equal(
            "10:30:00.123457",
            DbDateTimeConverter.SqlServerToPostgres("0044-03-15 10:30:00.1234567 BC", PostgresDateType.Time));

    [Fact]
    public void Infinity_ToTime_StillThrows()
    {
        var clamp = new DbDateTimeOptions { Infinity = InfinityHandling.ClampToRange };
        Assert.Throws<OverflowException>(() =>
            DbDateTimeConverter.PostgresToSqlServer("infinity", SqlServerDateType.Time, clamp));
    }

    // ── API có kiểu: thông báo lỗi vẫn mô tả đúng giá trị ──
    [Fact]
    public void Typed_OutOfRange_MessageDescribesValue()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            DbDateTimeConverter.ToSqlServer(new DateTime(1700, 1, 1), SqlServerDateType.DateTime));
        Assert.Contains("1700-01-01", ex.Message);
    }

    [Fact]
    public void Typed_ToPostgres_Date() =>
        Assert.Equal("2024-01-02", DbDateTimeConverter.ToPostgres(new DateTime(2024, 1, 2, 23, 0, 0), PostgresDateType.Date));
}
