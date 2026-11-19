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
