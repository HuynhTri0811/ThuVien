namespace ThuVien.Tests;

public class EmailValidatorSyntaxTests
{
    [Theory]
    [InlineData("a@b.vn")]
    [InlineData("ten.ho@congty.com.vn")]
    [InlineData("an+tin-tuc@gmail.com")]
    [InlineData("o'neil@example.org")]
    [InlineData("!#$%&'*+-/=?^_`{|}~@example.com")]
    [InlineData("user@sub-domain.example.co")]
    [InlineData("user@123.example.com")]
    [InlineData("user@xn--th-e0a.vn")]
    [InlineData("user@thư.vn")]
    [InlineData("  an@b.vn  ")]
    public void Valid(string email) => Assert.True(EmailValidator.IsValid(email), EmailValidator.Validate(email).Message);

    [Theory]
    [InlineData(null, EmailError.Empty)]
    [InlineData("", EmailError.Empty)]
    [InlineData("   ", EmailError.Empty)]
    [InlineData("khong-hop-le", EmailError.MissingAt)]
    [InlineData("a@b@c.vn", EmailError.MultipleAt)]
    [InlineData("@b.vn", EmailError.LocalPartEmpty)]
    [InlineData(".an@b.vn", EmailError.LocalPartInvalid)]
    [InlineData("an.@b.vn", EmailError.LocalPartInvalid)]
    [InlineData("a..n@b.vn", EmailError.LocalPartInvalid)]
    [InlineData("a n@b.vn", EmailError.LocalPartInvalid)]
    [InlineData("\"an\"@b.vn", EmailError.LocalPartInvalid)]
    [InlineData("nguyễn@b.vn", EmailError.LocalPartInvalid)]
    [InlineData("a(b)@b.vn", EmailError.LocalPartInvalid)]
    [InlineData("an@", EmailError.DomainEmpty)]
    [InlineData("an@b", EmailError.DomainInvalid)]
    [InlineData("an@.b.vn", EmailError.DomainInvalid)]
    [InlineData("an@b..vn", EmailError.DomainInvalid)]
    [InlineData("an@b.vn.", EmailError.DomainInvalid)]
    [InlineData("an@-b.vn", EmailError.DomainInvalid)]
    [InlineData("an@b-.vn", EmailError.DomainInvalid)]
    [InlineData("an@b_c.vn", EmailError.DomainInvalid)]
    [InlineData("an@b c.vn", EmailError.DomainInvalid)]
    [InlineData("an@1.2.3.4", EmailError.DomainInvalid)]
    [InlineData("an@[1.2.3.4]", EmailError.DomainInvalid)]
    public void Invalid(string? email, EmailError expected) =>
        Assert.Equal(expected, EmailValidator.Validate(email).Error);

    [Fact]
    public void LengthLimits()
    {
        var local64 = new string('a', 64);
        Assert.True(EmailValidator.IsValid(local64 + "@b.vn"));
        Assert.Equal(EmailError.LocalPartTooLong, EmailValidator.Validate(local64 + "a@b.vn").Error);

        var label63 = new string('b', 63);
        Assert.True(EmailValidator.IsValid("a@" + label63 + ".vn"));
        Assert.Equal(EmailError.DomainInvalid, EmailValidator.Validate("a@" + label63 + "b.vn").Error);

        // Tên miền 253 ký tự: 3 nhãn 63 + 1 nhãn 59 + 3 dấu chấm.
        var domain253 = string.Join('.', label63, label63, label63, new string('c', 61));
        Assert.Equal(253, domain253.Length);
        Assert.Equal(EmailError.DomainTooLong, EmailValidator.Validate("a@" + domain253 + "x").Error);

        // Tổng 254 ký tự là giới hạn.
        var domain = string.Join('.', label63, label63, new string('c', 63), "vn");
        var local = new string('a', 254 - 1 - domain.Length);
        Assert.True(EmailValidator.IsValid(local + "@" + domain));
        Assert.Equal(EmailError.TooLong, EmailValidator.Validate("a" + local + "@" + domain).Error);
    }

    [Fact]
    public void HugeInput_IsRejectedQuickly()
    {
        var input = "a@" + new string('.', 1_000_000) + "@";
        var watch = System.Diagnostics.Stopwatch.StartNew();
        Assert.Equal(EmailError.TooLong, EmailValidator.Validate(input).Error);
        Assert.True(watch.Elapsed < TimeSpan.FromMilliseconds(200), $"Mất {watch.Elapsed}");
    }

    [Fact]
    public void Normalizes_DomainCase_AndIdn()
    {
        var result = EmailValidator.Validate("  An.Nguyen@Thư.VN ");
        Assert.True(result.IsValid);
        Assert.Equal("An.Nguyen@thư.vn", result.NormalizedEmail);
        Assert.Equal("An.Nguyen@xn--th-e0a.vn", result.AsciiEmail);
    }

    [Fact]
    public void Punycode_Input_NormalizesToUnicode()
    {
        var result = EmailValidator.Validate("a@XN--TH-E0A.vn");
        Assert.Equal("a@thư.vn", result.NormalizedEmail);
        Assert.Equal("a@xn--th-e0a.vn", result.AsciiEmail);
    }

    [Theory]
    [InlineData("an@gmial.com", "an@gmail.com")]
    [InlineData("an@gmail.co", "an@gmail.com")]
    [InlineData("an@gmai.com", "an@gmail.com")]
    [InlineData("an@Gmail.con", "an@gmail.com")]
    [InlineData("an@hotmial.com", "an@hotmail.com")]
    [InlineData("an@yaho.com.vn", "an@yahoo.com.vn")]
    [InlineData("an@outlok.com", "an@outlook.com")]
    [InlineData("an@gmail", "an@gmail.com")]
    public void SuggestsCorrection(string email, string expected) =>
        Assert.Equal(expected, EmailValidator.SuggestCorrection(email));

    [Theory]
    [InlineData("an@gmail.com")]
    [InlineData("an@mail.com")]
    [InlineData("an@ymail.com")]
    [InlineData("an@congty.vn")]
    [InlineData("an@example.com")]
    [InlineData("an@mc.com")] // gần cả me.com và mac.com → không đoán
    [InlineData("khong-hop-le")]
    public void NoSuggestion(string email) => Assert.Null(EmailValidator.SuggestCorrection(email));

    [Fact]
    public void Suggestion_DoesNotAffectValidity()
    {
        var result = EmailValidator.Validate("an@gmial.com");
        Assert.True(result.IsValid);
        Assert.Equal("an@gmail.com", result.Suggestion);
    }

    [Fact]
    public void EveryError_HasVietnameseMessage()
    {
        foreach (var error in Enum.GetValues<EmailError>())
        {
            Assert.NotEqual(error.ToString(), EmailValidator.Describe(error));
        }
    }

    [Theory]
    [InlineData("gmail.com", "gmial.com", 1)]
    [InlineData("abc", "abc", 0)]
    [InlineData("abc", "", 3)]
    [InlineData("kitten", "sitting", 3)]
    public void Distance_Works(string a, string b, int expected) =>
        Assert.Equal(expected, EmailValidator.Distance(a, b));
}
