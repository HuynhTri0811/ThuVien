namespace ThuVien.Tests;

public class WordsToNumberTests
{
    private const string Prefix =
        "chín tỷ tỷ hai trăm hai mươi ba triệu tỷ ba trăm bảy mươi hai nghìn tỷ không trăm ba mươi sáu tỷ "
        + "tám trăm năm mươi tư triệu bảy trăm bảy mươi lăm nghìn tám trăm linh ";

    // long.MaxValue + 1 = 9 223 372 036 854 775 808
    private const string MaxPlusOne = Prefix + "tám";

    // |long.MinValue| + 1 = 9 223 372 036 854 775 809
    private const string MinMinusOneMagnitude = Prefix + "chín";

    [Fact]
    public void FromVietnamese_Int64Boundaries()
    {
        Assert.Equal(long.MaxValue, WordsToNumber.FromVietnamese(Prefix + "bảy"));
        Assert.Equal(long.MinValue, WordsToNumber.FromVietnamese("âm " + MaxPlusOne));
    }

    [Theory]
    [InlineData("không", 0)]
    [InlineData("năm", 5)]
    [InlineData("mười", 10)]
    [InlineData("mười lăm", 15)]
    [InlineData("hai mươi mốt", 21)]
    [InlineData("hai mươi tư", 24)]
    [InlineData("hai mươi lăm", 25)]
    [InlineData("một trăm", 100)]
    [InlineData("một trăm linh năm", 105)]
    [InlineData("một trăm lẻ năm", 105)]
    [InlineData("một trăm mười", 110)]
    [InlineData("một nghìn không trăm linh năm", 1005)]
    [InlineData("hai triệu năm trăm nghìn", 2_500_000)]
    [InlineData("âm ba mươi mốt", -31)]
    [InlineData("trừ ba mươi mốt", -31)]
    [InlineData("một tỷ", 1_000_000_000)]
    [InlineData("năm nghìn tỷ hai trăm triệu", 5_000_200_000_000)]
    [InlineData("một tỷ tỷ", 1_000_000_000_000_000_000)]
    public void FromVietnamese_Works(string input, long expected) =>
        Assert.Equal(expected, WordsToNumber.FromVietnamese(input));

    [Theory]
    [InlineData("Một Trăm Hai Mươi Lăm Nghìn Đồng", 125_000)]
    [InlineData("một trăm nghìn đồng chẵn", 100_000)]
    [InlineData("  hai   ngàn,  ba trăm. ", 2_300)]
    [InlineData("ba tỉ", 3_000_000_000)]
    [InlineData("hai mươi bẩy", 27)]
    [InlineData("ba mươi nhăm", 35)]
    [InlineData("ba mươi năm", 35)]
    [InlineData("hai mươi một", 21)]
    [InlineData("hai nghìn hai mươi", 2_020)]
    [InlineData("một triệu năm nghìn", 1_005_000)]
    [InlineData("âm không", 0)]
    public void FromVietnamese_AcceptsCommonVariants(string input, long expected) =>
        Assert.Equal(expected, WordsToNumber.FromVietnamese(input));

    [Fact]
    public void FromVietnamese_AcceptsDecomposedUnicode() =>
        Assert.Equal(25, WordsToNumber.FromVietnamese("hai mươi lăm".Normalize(System.Text.NormalizationForm.FormD)));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("âm")]
    [InlineData("đồng")]
    [InlineData("chẵn")]
    [InlineData("abc")]
    [InlineData("123")]
    [InlineData("một trăm năm")]        // mơ hồ: 105 hay 150?
    [InlineData("một trăm năm nghìn")]
    [InlineData("trăm")]
    [InlineData("linh năm")]
    [InlineData("một trăm linh không")]
    [InlineData("mười mốt")]
    [InlineData("mười tư")]
    [InlineData("một mươi")]
    [InlineData("mười không")]
    [InlineData("một nghìn không")]
    [InlineData("không không")]
    [InlineData("hai ba")]
    [InlineData("một nghìn một triệu")]  // hàng không giảm dần
    [InlineData("một nghìn hai nghìn")]
    [InlineData("nghìn")]
    [InlineData("một nghìn đồng đồng")]
    [InlineData("hai mươi lăm!")]
    public void FromVietnamese_Invalid_ThrowsFormatException(string input) =>
        Assert.Throws<FormatException>(() => WordsToNumber.FromVietnamese(input));

    [Theory]
    [InlineData("mười tỷ tỷ")]
    [InlineData("một tỷ tỷ tỷ")]
    [InlineData(MaxPlusOne)]
    [InlineData("âm " + MinMinusOneMagnitude)]
    public void FromVietnamese_OutOfRange_ThrowsOverflowException(string input) =>
        Assert.Throws<OverflowException>(() => WordsToNumber.FromVietnamese(input));

    [Fact]
    public void FromVietnamese_Null_Throws() =>
        Assert.Throws<ArgumentNullException>(() => WordsToNumber.FromVietnamese(null!));

    [Fact]
    public void TryFromVietnamese_Works()
    {
        Assert.True(WordsToNumber.TryFromVietnamese("bốn mươi hai", out var value));
        Assert.Equal(42, value);
        Assert.False(WordsToNumber.TryFromVietnamese("không hợp lệ", out value));
        Assert.Equal(0, value);
        Assert.False(WordsToNumber.TryFromVietnamese(null, out _));
        Assert.False(WordsToNumber.TryFromVietnamese("mười tỷ tỷ", out _));
    }

    [Theory]
    [InlineData(long.MaxValue)]
    [InlineData(long.MinValue)]
    [InlineData(long.MinValue + 1)]
    [InlineData(1_000_000_000_000_000_000)]
    [InlineData(1_200_300_000_000_000_000)]
    [InlineData(1_000_001)]
    [InlineData(10_010_010)]
    public void RoundTrip_Extremes(long n)
    {
        Assert.Equal(n, WordsToNumber.FromVietnamese(NumberToWords.ToVietnamese(n)));
        if (n >= 0)
        {
            Assert.Equal(n, WordsToNumber.FromVietnamese(NumberToWords.ToVietnameseCurrency(n)));
        }
    }

    [Fact]
    public void RoundTrip_AllSmallNumbers()
    {
        for (long n = -2_000; n <= 1_000_000; n++)
        {
            Assert.Equal(n, WordsToNumber.FromVietnamese(NumberToWords.ToVietnamese(n)));
        }
    }

    [Fact]
    public void RoundTrip_RandomLargeNumbers()
    {
        var random = new Random(20261123);
        for (var i = 0; i < 100_000; i++)
        {
            var n = random.NextInt64(long.MinValue, long.MaxValue);
            Assert.Equal(n, WordsToNumber.FromVietnamese(NumberToWords.ToVietnamese(n)));
        }
    }
}
