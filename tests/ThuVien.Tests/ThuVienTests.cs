using ThuVien;

namespace ThuVien.Tests;

public class VietnameseTextTests
{
    [Theory]
    [InlineData("Tiếng Việt Đẹp", "Tieng Viet Dep")]
    [InlineData("đường phố Hà Nội", "duong pho Ha Noi")]
    [InlineData("Ồ, ỨNG DỤNG!", "O, UNG DUNG!")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void RemoveDiacritics_Works(string? input, string expected) =>
        Assert.Equal(expected, VietnameseText.RemoveDiacritics(input));

    [Theory]
    [InlineData("Xin chào, Việt Nam!", "xin-chao-viet-nam")]
    [InlineData("  Đà Lạt   mộng mơ  ", "da-lat-mong-mo")]
    [InlineData("C# & .NET 10", "c-net-10")]
    [InlineData("!!!", "")]
    public void ToSlug_Works(string input, string expected) =>
        Assert.Equal(expected, VietnameseText.ToSlug(input));

    [Fact]
    public void ToSlug_CustomSeparator() =>
        Assert.Equal("ho_chi_minh", VietnameseText.ToSlug("Hồ Chí Minh", '_'));
}

public class NumberToWordsTests
{
    [Theory]
    [InlineData(0, "không")]
    [InlineData(5, "năm")]
    [InlineData(10, "mười")]
    [InlineData(15, "mười lăm")]
    [InlineData(21, "hai mươi mốt")]
    [InlineData(25, "hai mươi lăm")]
    [InlineData(105, "một trăm linh năm")]
    [InlineData(110, "một trăm mười")]
    [InlineData(1005, "một nghìn không trăm linh năm")]
    [InlineData(1_000_000, "một triệu")]
    [InlineData(1_000_000_005, "một tỷ không trăm linh năm")]
    [InlineData(2_500_000, "hai triệu năm trăm nghìn")]
    [InlineData(-31, "âm ba mươi mốt")]
    public void ToVietnamese_Works(long input, string expected) =>
        Assert.Equal(expected, NumberToWords.ToVietnamese(input));

    [Fact]
    public void ToVietnamese_HandlesExtremes()
    {
        Assert.StartsWith("chín tỷ tỷ", NumberToWords.ToVietnamese(long.MaxValue));
        Assert.StartsWith("âm chín tỷ tỷ", NumberToWords.ToVietnamese(long.MinValue));
    }

    [Fact]
    public void ToVietnameseCurrency_Works() =>
        Assert.Equal("Một trăm hai mươi lăm nghìn đồng", NumberToWords.ToVietnameseCurrency(125_000));
}

public class ValidatorsTests
{
    [Theory]
    [InlineData("0912345678", true)]
    [InlineData("091 234 5678", true)]
    [InlineData("+84912345678", true)]
    [InlineData("84 912.345.678", true)]
    [InlineData("0212345678", false)]
    [InlineData("091234567", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsVietnamesePhone_Works(string? input, bool expected) =>
        Assert.Equal(expected, Validators.IsVietnamesePhone(input));

    [Theory]
    [InlineData("+84 912 345 678", "0912345678")]
    [InlineData("84912345678", "0912345678")]
    [InlineData("0912-345-678", "0912345678")]
    [InlineData("abc", null)]
    public void NormalizeVietnamesePhone_Works(string input, string? expected) =>
        Assert.Equal(expected, Validators.NormalizeVietnamesePhone(input));

    [Theory]
    [InlineData("a@b.vn", true)]
    [InlineData("ten.ho@congty.com.vn", true)]
    [InlineData("khong-hop-le", false)]
    [InlineData("a@b", false)]
    public void IsEmail_Works(string input, bool expected) =>
        Assert.Equal(expected, Validators.IsEmail(input));
}

public class CurrencyFormatterTests
{
    [Theory]
    [InlineData(0, "0 ₫")]
    [InlineData(1234567, "1.234.567 ₫")]
    [InlineData(999.5, "1.000 ₫")]
    [InlineData(-50000, "-50.000 ₫")]
    public void ToVnd_Works(decimal input, string expected) =>
        Assert.Equal(expected, CurrencyFormatter.ToVnd(input));

    [Fact]
    public void ToVnd_CustomSymbol() =>
        Assert.Equal("10.000 VND", CurrencyFormatter.ToVnd(10000, "VND"));
}
