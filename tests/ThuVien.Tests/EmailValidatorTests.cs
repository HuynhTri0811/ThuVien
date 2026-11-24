using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;

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
        Assert.False(result.MailServerChecked);
        Assert.Empty(result.MailServers);
    }

    [Fact]
    public void Punycode_Input_NormalizesToUnicode()
    {
        var result = EmailValidator.Validate("a@XN--TH-E0A.vn");
        Assert.Equal("a@thư.vn", result.NormalizedEmail);
        Assert.Equal("a@xn--th-e0a.vn", result.AsciiEmail);
    }

    [Theory]
    [InlineData("a@b\u200d.vn")] // ký tự định dạng vô hình (ZWJ)
    [InlineData("a@b\u00ad.vn")] // gạch nối mềm
    [InlineData("a@b\u0000.vn")]
    [InlineData("a@b\u00a0c.vn")] // khoảng trắng không ngắt
    [InlineData("a@b\ue000.vn")] // vùng dùng riêng
    public void Domain_WithInvisibleOrControlChars_IsInvalid(string email) =>
        Assert.Equal(EmailError.DomainInvalid, EmailValidator.Validate(email).Error);

    [Theory]
    [InlineData("a@xn--zz.vn")]
    [InlineData("a@xn--.vn")]
    [InlineData("a@xn---.vn")]
    [InlineData("a@xn--a.vn")]
    [InlineData("a@xn--ab-.vn")]
    [InlineData("a@xn--80ak6aa92e.xn--zz")]
    public void BadPunycode_ReturnsResult_NeverThrows(string email)
    {
        var result = EmailValidator.Validate(email);
        if (result.IsValid)
        {
            // Nếu nhận thì dạng Unicode phải đổi ngược lại đúng dạng ASCII.
            var normalized = result.NormalizedEmail!;
            var unicodeDomain = normalized[(normalized.IndexOf('@') + 1)..];
            Assert.Equal(result.AsciiEmail, "a@" + new System.Globalization.IdnMapping().GetAscii(unicodeDomain));
        }
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

public class EmailValidatorMailServerTests
{
    private sealed class FakeResolver(MailServerLookup result) : IMailServerResolver
    {
        public string? Domain { get; private set; }

        public Task<MailServerLookup> LookupAsync(string asciiDomain, CancellationToken cancellationToken = default)
        {
            Domain = asciiDomain;
            return Task.FromResult(result);
        }
    }

    [Theory]
    [InlineData(MailServerStatus.Found, EmailError.None)]
    [InlineData(MailServerStatus.ImplicitMx, EmailError.None)]
    [InlineData(MailServerStatus.NullMx, EmailError.NullMx)]
    [InlineData(MailServerStatus.NoMailServer, EmailError.NoMailServer)]
    [InlineData(MailServerStatus.DomainNotFound, EmailError.DomainNotFound)]
    [InlineData(MailServerStatus.Failed, EmailError.DnsLookupFailed)]
    public async Task MapsLookupStatus(MailServerStatus status, EmailError expected)
    {
        var records = status == MailServerStatus.Found ? new[] { new MxRecord(10, "mx.thư.vn") } : [];
        var resolver = new FakeResolver(new MailServerLookup(status, records));

        var result = await EmailValidator.ValidateWithMailServerAsync("An@Thư.vn", resolver);

        Assert.Equal(expected, result.Error);
        Assert.True(result.MailServerChecked);
        Assert.Equal("xn--th-e0a.vn", resolver.Domain); // tra bằng dạng ASCII
        Assert.Equal("An@thư.vn", result.NormalizedEmail);
        Assert.Equal(records, result.MailServers);
    }

    [Fact]
    public async Task InvalidSyntax_DoesNotQueryDns()
    {
        var resolver = new FakeResolver(new MailServerLookup(MailServerStatus.Found, []));
        var result = await EmailValidator.ValidateWithMailServerAsync("an@b", resolver);
        Assert.Equal(EmailError.DomainInvalid, result.Error);
        Assert.False(result.MailServerChecked);
        Assert.Null(resolver.Domain);
    }
}

public class DnsMessageTests
{
    [Fact]
    public void BuildQuery_EncodesQuestion()
    {
        var q = DnsMessage.BuildQuery(0xABCD, "mx.b.vn", 15);
        byte[] expected =
        [
            0xAB, 0xCD, 0x01, 0x00, 0, 1, 0, 0, 0, 0, 0, 0,
            2, (byte)'m', (byte)'x', 1, (byte)'b', 2, (byte)'v', (byte)'n', 0,
            0, 15, 0, 1,
        ];
        Assert.Equal(expected, q);
    }

    [Theory]
    [InlineData("a..b")]
    [InlineData("thư.vn")]
    public void BuildQuery_RejectsBadLabels(string name) =>
        Assert.Throws<ArgumentException>(() => DnsMessage.BuildQuery(1, name, 15));

    [Fact]
    public void BuildQuery_RejectsLongLabel() =>
        Assert.Throws<ArgumentException>(() => DnsMessage.BuildQuery(1, new string('a', 64) + ".vn", 15));

    [Fact]
    public void Parse_ReadsCompressedMx()
    {
        var query = DnsMessage.BuildQuery(7, "b.vn", 15);
        var response = FakeDns.Response(query, 0, FakeDns.Mx(20, "mx2.b.vn"), FakeDns.MxPointer(10, "mx1"));
        var parsed = DnsMessage.Parse(response, "b.vn", 15);
        Assert.Equal(0, parsed.ResponseCode);
        Assert.Equal([new MxRecord(20, "mx2.b.vn"), new MxRecord(10, "mx1.b.vn")], parsed.MxRecords);
    }

    [Fact]
    public void Parse_IgnoresRecordsOfOtherNames()
    {
        var query = DnsMessage.BuildQuery(7, "b.vn", 15);
        var response = FakeDns.ResponseWithOwners(query,
            ("khac.vn", 15, FakeDns.Mx(10, "mx.khac.vn").Item2),
            ("b.vn", 15, FakeDns.Mx(20, "mx.b.vn").Item2),
            ("khac.vn", 1, new byte[4]));
        var parsed = DnsMessage.Parse(response, "b.vn", 15);
        Assert.Equal([new MxRecord(20, "mx.b.vn")], parsed.MxRecords);
        Assert.Equal(0, parsed.AddressCount);
    }

    [Fact]
    public void Parse_FollowsCname()
    {
        var query = DnsMessage.BuildQuery(7, "b.vn", 1);
        var cname = new MemoryStream();
        foreach (var label in "that.b.vn".Split('.'))
        {
            cname.WriteByte((byte)label.Length);
            cname.Write(Encoding.ASCII.GetBytes(label));
        }

        cname.WriteByte(0);
        var response = FakeDns.ResponseWithOwners(query,
            ("b.vn", 5, cname.ToArray()),
            ("that.b.vn", 1, new byte[4]));
        Assert.Equal(1, DnsMessage.Parse(response, "b.vn", 1).AddressCount);
    }

    [Theory]
    [InlineData(new byte[] { 3, (byte)'m', 0xC3, (byte)'x', 2, (byte)'v', (byte)'n', 0 })] // byte ngoài ASCII
    [InlineData(new byte[] { 3, (byte)'a', (byte)'.', (byte)'b', 2, (byte)'v', (byte)'n', 0 })] // dấu chấm trong nhãn
    [InlineData(new byte[] { 3, (byte)'a', (byte)' ', (byte)'b', 2, (byte)'v', (byte)'n', 0 })]
    [InlineData(new byte[] { 3, (byte)'-', (byte)'a', (byte)'b', 2, (byte)'v', (byte)'n', 0 })]
    public void Parse_DropsMxWithInvalidHost(byte[] host)
    {
        var query = DnsMessage.BuildQuery(7, "b.vn", 15);
        var response = FakeDns.ResponseWithOwners(query,
            ("b.vn", 15, [0, 10, .. host]),
            ("b.vn", 15, FakeDns.Mx(20, "mx.b.vn").Item2));
        Assert.Equal([new MxRecord(20, "mx.b.vn")], DnsMessage.Parse(response, "b.vn", 15).MxRecords);
    }

    [Fact]
    public void Parse_RejectsPointerLoop()
    {
        byte[] message = [0, 1, 0x81, 0x80, 0, 1, 0, 0, 0, 0, 0, 0, 0xC0, 12];
        Assert.Throws<FormatException>(() => DnsMessage.Parse(message, "b.vn", 15));
    }

    [Fact]
    public void Parse_RejectsTruncatedPackets()
    {
        var query = DnsMessage.BuildQuery(7, "b.vn", 15);
        var response = FakeDns.Response(query, 0, FakeDns.Mx(10, "mx.b.vn"));
        for (var length = 0; length < response.Length; length++)
        {
            var cut = response[..length];
            Assert.Throws<FormatException>(() => DnsMessage.Parse(cut, "b.vn", 15));
        }
    }

    [Fact]
    public void Parse_RejectsMismatchedQuestion()
    {
        var query = DnsMessage.BuildQuery(7, "khac.vn", 15);
        var response = FakeDns.Response(query, 0, FakeDns.Mx(10, "mx.b.vn"));
        Assert.Throws<FormatException>(() => DnsMessage.Parse(response, "b.vn", 15));
        Assert.Throws<FormatException>(() => DnsMessage.Parse(response, "khac.vn", 1));
    }

    [Fact]
    public void Parse_RejectsQuery() =>
        Assert.Throws<FormatException>(() => DnsMessage.Parse(DnsMessage.BuildQuery(7, "b.vn", 15), "b.vn", 15));
}

public class DnsMxResolverTests
{
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromMilliseconds(300);

    [Fact]
    public async Task Found_SortedByPreference()
    {
        await using var dns = new FakeDns(q => [FakeDns.Response(q, 0, FakeDns.Mx(20, "mx2.b.vn"), FakeDns.Mx(10, "MX1.b.vn"))]);
        var lookup = await new DnsMxResolver([dns.EndPoint], ShortTimeout).LookupAsync("b.vn");
        Assert.Equal(MailServerStatus.Found, lookup.Status);
        Assert.Equal([new MxRecord(10, "mx1.b.vn"), new MxRecord(20, "mx2.b.vn")], lookup.Records);
    }

    [Fact]
    public async Task NullMx()
    {
        await using var dns = new FakeDns(q => [FakeDns.Response(q, 0, FakeDns.Mx(0, ""))]);
        var lookup = await new DnsMxResolver([dns.EndPoint], ShortTimeout).LookupAsync("b.vn");
        Assert.Equal(MailServerStatus.NullMx, lookup.Status);
        Assert.Empty(lookup.Records);
    }

    [Fact]
    public async Task DomainNotFound()
    {
        await using var dns = new FakeDns(q => [FakeDns.Response(q, 3)]);
        var lookup = await new DnsMxResolver([dns.EndPoint], ShortTimeout).LookupAsync("khongco.vn");
        Assert.Equal(MailServerStatus.DomainNotFound, lookup.Status);
    }

    [Theory]
    [InlineData((ushort)1)]
    [InlineData((ushort)28)]
    public async Task NoMx_FallsBackToAddress(ushort addressType)
    {
        await using var dns = new FakeDns(q => FakeDns.QueryType(q) == addressType
            ? [FakeDns.Response(q, 0, FakeDns.Address(addressType))]
            : [FakeDns.Response(q, 0)]);
        var lookup = await new DnsMxResolver([dns.EndPoint], ShortTimeout).LookupAsync("b.vn");
        Assert.Equal(MailServerStatus.ImplicitMx, lookup.Status);
        Assert.Equal([new MxRecord(0, "b.vn")], lookup.Records);
    }

    [Fact]
    public async Task NoRecordsAtAll()
    {
        await using var dns = new FakeDns(q => [FakeDns.Response(q, 0)]);
        var lookup = await new DnsMxResolver([dns.EndPoint], ShortTimeout).LookupAsync("b.vn");
        Assert.Equal(MailServerStatus.NoMailServer, lookup.Status);
        Assert.Equal(3, dns.QueryCount); // MX, A, AAAA
    }

    [Fact]
    public async Task AddressQueryError_KeepsDefiniteNoMxAnswer()
    {
        // MX: không có; A: không có; AAAA: SERVFAIL → vẫn là NoMailServer, không phải Failed.
        await using var dns = new FakeDns(q => FakeDns.QueryType(q) == 28
            ? [FakeDns.Response(q, 2)]
            : [FakeDns.Response(q, 0)]);
        var lookup = await new DnsMxResolver([dns.EndPoint], ShortTimeout).LookupAsync("b.vn");
        Assert.Equal(MailServerStatus.NoMailServer, lookup.Status);
    }

    [Fact]
    public async Task AddressQueryError_StillFindsOtherAddressType()
    {
        await using var dns = new FakeDns(q => FakeDns.QueryType(q) switch
        {
            1 => [FakeDns.Response(q, 2)],
            28 => [FakeDns.Response(q, 0, FakeDns.Address(28))],
            _ => [FakeDns.Response(q, 0)],
        });
        var lookup = await new DnsMxResolver([dns.EndPoint], ShortTimeout).LookupAsync("b.vn");
        Assert.Equal(MailServerStatus.ImplicitMx, lookup.Status);
    }

    [Fact]
    public async Task AllAddressQueriesFail_ReturnsFailed()
    {
        await using var dns = new FakeDns(q => FakeDns.QueryType(q) == 15
            ? [FakeDns.Response(q, 0)]
            : [FakeDns.Response(q, 2)]);
        var lookup = await new DnsMxResolver([dns.EndPoint], ShortTimeout).LookupAsync("b.vn");
        Assert.Equal(MailServerStatus.Failed, lookup.Status);
    }

    [Fact]
    public async Task Timeout_CoversAllQueriesToOneServer()
    {
        // Mỗi phản hồi chậm 200 ms (< Timeout 500 ms) nhưng ba truy vấn MX, A, AAAA cộng lại vượt Timeout.
        await using var dns = new FakeDns(q =>
        {
            Thread.Sleep(200);
            return [FakeDns.Response(q, 0)];
        });
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var lookup = await new DnsMxResolver([dns.EndPoint], TimeSpan.FromMilliseconds(500)).LookupAsync("b.vn");
        Assert.Equal(MailServerStatus.Failed, lookup.Status);
        Assert.True(watch.Elapsed < TimeSpan.FromMilliseconds(580), $"Mất {watch.Elapsed}");
    }

    [Fact]
    public async Task IgnoresMalformedResponseWithMatchingId()
    {
        await using var dns = new FakeDns(q =>
        {
            var malformed = FakeDns.Response(q, 0, FakeDns.Mx(10, "ke-gian.vn"));
            return [malformed[..^3], FakeDns.Response(q, 0, FakeDns.Mx(10, "mx.b.vn"))];
        });
        var lookup = await new DnsMxResolver([dns.EndPoint], ShortTimeout).LookupAsync("b.vn");
        Assert.Equal([new MxRecord(10, "mx.b.vn")], lookup.Records);
    }

    [Fact]
    public async Task ServFail_TriesNextServer()
    {
        await using var failing = new FakeDns(q => [FakeDns.Response(q, 2)]);
        await using var good = new FakeDns(q => [FakeDns.Response(q, 0, FakeDns.Mx(10, "mx.b.vn"))]);
        var lookup = await new DnsMxResolver([failing.EndPoint, good.EndPoint], ShortTimeout).LookupAsync("b.vn");
        Assert.Equal(MailServerStatus.Found, lookup.Status);
        Assert.Equal(1, failing.QueryCount);
    }

    [Fact]
    public async Task Timeout_TriesNextServer()
    {
        await using var silent = new FakeDns(_ => []);
        await using var good = new FakeDns(q => [FakeDns.Response(q, 0, FakeDns.Mx(10, "mx.b.vn"))]);
        var lookup = await new DnsMxResolver([silent.EndPoint, good.EndPoint], ShortTimeout).LookupAsync("b.vn");
        Assert.Equal(MailServerStatus.Found, lookup.Status);
    }

    [Fact]
    public async Task AllServersFail_ReturnsFailed()
    {
        await using var silent = new FakeDns(_ => []);
        await using var garbage = new FakeDns(q => [[.. q[..2], 0xFF, 0xFF]]);
        var lookup = await new DnsMxResolver([silent.EndPoint, garbage.EndPoint], ShortTimeout).LookupAsync("b.vn");
        Assert.Equal(MailServerStatus.Failed, lookup.Status);
    }

    [Fact]
    public async Task IgnoresResponseWithWrongId()
    {
        await using var dns = new FakeDns(q =>
        {
            var spoofed = FakeDns.Response(q, 0, FakeDns.Mx(10, "ke-gian.vn"));
            spoofed[0] ^= 0xFF;
            return [spoofed, FakeDns.Response(q, 0, FakeDns.Mx(10, "mx.b.vn"))];
        });
        var lookup = await new DnsMxResolver([dns.EndPoint], ShortTimeout).LookupAsync("b.vn");
        Assert.Equal([new MxRecord(10, "mx.b.vn")], lookup.Records);
    }

    [Fact]
    public async Task Truncated_RetriesOverTcp()
    {
        await using var dns = new FakeDns(
            q => [FakeDns.Response(q, 0, truncated: true)],
            tcp: q => FakeDns.Response(q, 0, FakeDns.Mx(10, "mx.b.vn")));
        var lookup = await new DnsMxResolver([dns.EndPoint], ShortTimeout).LookupAsync("b.vn");
        Assert.Equal(MailServerStatus.Found, lookup.Status);
        Assert.Equal(1, dns.TcpQueryCount);
    }

    [Fact]
    public async Task Cancellation_Propagates()
    {
        await using var silent = new FakeDns(_ => []);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        var resolver = new DnsMxResolver([silent.EndPoint], TimeSpan.FromSeconds(10));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => resolver.LookupAsync("b.vn", cts.Token));
    }

    [Theory]
    [InlineData("")]
    [InlineData("khong hop le")]
    [InlineData("b")]
    public async Task InvalidDomain_Throws(string domain) =>
        await Assert.ThrowsAnyAsync<ArgumentException>(() => DnsMxResolver.Default.LookupAsync(domain));

    [Fact]
    public void Constructor_Validates()
    {
        Assert.Throws<ArgumentException>(() => new DnsMxResolver([]));
        Assert.Throws<ArgumentException>(() => new DnsMxResolver([null!]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DnsMxResolver(null, TimeSpan.Zero));
    }

    [Fact]
    public async Task ValidateWithMailServer_EndToEnd()
    {
        await using var dns = new FakeDns(q => [FakeDns.Response(q, 0, FakeDns.Mx(5, "mx.xn--th-e0a.vn"))]);
        var result = await EmailValidator.ValidateWithMailServerAsync(
            "an@thư.vn", new DnsMxResolver([dns.EndPoint], ShortTimeout));
        Assert.True(result.IsValid);
        Assert.Equal([new MxRecord(5, "mx.xn--th-e0a.vn")], result.MailServers);
    }

    /// <summary>Tra DNS thật; chỉ chạy khi đặt biến môi trường THUVIEN_NETWORK_TESTS=1, nếu không thì báo bỏ qua.</summary>
    [NetworkFact]
    public async Task RealDns_WhenEnabled()
    {
        Assert.Equal(MailServerStatus.Found, (await DnsMxResolver.Default.LookupAsync("gmail.com")).Status);
        Assert.Equal(MailServerStatus.DomainNotFound,
            (await DnsMxResolver.Default.LookupAsync("khong-ton-tai-" + Guid.NewGuid().ToString("N") + ".com")).Status);
        Assert.Equal(EmailError.None, (await EmailValidator.ValidateWithMailServerAsync("test@gmail.com")).Error);
    }
}

/// <summary>Máy chủ DNS giả trên 127.0.0.1 (UDP, kèm TCP trên cùng cổng nếu cần).</summary>
internal sealed class FakeDns : IAsyncDisposable
{
    private readonly UdpClient _udp = null!;
    private readonly TcpListener? _tcp;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _udpLoop;
    private readonly Task _tcpLoop = Task.CompletedTask;
    private int _queryCount;
    private int _tcpQueryCount;

    public FakeDns(Func<byte[], byte[][]> udp, Func<byte[], byte[]>? tcp = null)
    {
        // Cổng UDP ngẫu nhiên có thể đang bị chương trình khác dùng cho TCP: thử lại với cổng khác.
        for (var attempt = 0; ; attempt++)
        {
            _udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            EndPoint = (IPEndPoint)_udp.Client.LocalEndPoint!;
            if (tcp is null)
            {
                break;
            }

            try
            {
                _tcp = new TcpListener(IPAddress.Loopback, EndPoint.Port);
                _tcp.Start();
                break;
            }
            catch (SocketException) when (attempt < 20)
            {
                _tcp = null;
                _udp.Dispose();
            }
        }

        _udpLoop = Task.Run(async () =>
        {
            while (!_stop.IsCancellationRequested)
            {
                UdpReceiveResult packet;
                try
                {
                    packet = await _udp.ReceiveAsync(_stop.Token);
                }
                catch (Exception)
                {
                    return;
                }

                Interlocked.Increment(ref _queryCount);
                try
                {
                    foreach (var response in udp(packet.Buffer))
                    {
                        await _udp.SendAsync(response, packet.RemoteEndPoint);
                    }
                }
                catch (Exception)
                {
                    return; // đã dừng trong lúc đang trả lời
                }
            }
        });

        if (_tcp is not null && tcp is not null)
        {
            _tcpLoop = Task.Run(async () =>
            {
                try
                {
                    using var client = await _tcp.AcceptTcpClientAsync(_stop.Token);
                    var stream = client.GetStream();
                    var len = new byte[2];
                    await stream.ReadExactlyAsync(len, _stop.Token);
                    var query = new byte[BinaryPrimitives.ReadUInt16BigEndian(len)];
                    await stream.ReadExactlyAsync(query, _stop.Token);
                    Interlocked.Increment(ref _tcpQueryCount);
                    var response = tcp(query);
                    var framed = new byte[response.Length + 2];
                    BinaryPrimitives.WriteUInt16BigEndian(framed, (ushort)response.Length);
                    response.CopyTo(framed, 2);
                    await stream.WriteAsync(framed, _stop.Token);
                }
                catch (Exception)
                {
                    // dừng
                }
            });
        }
    }

    public IPEndPoint EndPoint { get; }

    public int QueryCount => Volatile.Read(ref _queryCount);

    public int TcpQueryCount => Volatile.Read(ref _tcpQueryCount);

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _udp.Dispose();
        _tcp?.Stop();
        await Task.WhenAll(_udpLoop, _tcpLoop);
        _stop.Dispose();
    }

    public static ushort QueryType(byte[] query) =>
        BinaryPrimitives.ReadUInt16BigEndian(query.AsSpan(query.Length - 4));

    /// <summary>Phản hồi: chép header + câu hỏi của truy vấn, thêm các bản ghi trả lời (tên chủ trỏ về câu hỏi).</summary>
    public static byte[] Response(byte[] query, int rcode, params (ushort Type, byte[] Data)[] answers) =>
        Response(query, rcode, truncated: false, answers);

    public static byte[] Response(byte[] query, int rcode, bool truncated, params (ushort Type, byte[] Data)[] answers)
    {
        var ms = new MemoryStream();
        ms.Write(query, 0, 2);
        WriteU16(ms, (ushort)(0x8180 | rcode | (truncated ? 0x0200 : 0)));
        WriteU16(ms, 1);
        WriteU16(ms, (ushort)answers.Length);
        WriteU16(ms, 0);
        WriteU16(ms, 0);
        ms.Write(query, 12, query.Length - 12);
        foreach (var (type, data) in answers)
        {
            ms.Write([0xC0, 12]);
            WriteU16(ms, type);
            WriteU16(ms, 1);
            ms.Write([0, 0, 1, 44]);
            WriteU16(ms, (ushort)data.Length);
            ms.Write(data);
        }

        return ms.ToArray();
    }

    public static (ushort, byte[]) Mx(ushort preference, string host)
    {
        var ms = new MemoryStream();
        WriteU16(ms, preference);
        WriteName(ms, host);
        ms.WriteByte(0);
        return (15, ms.ToArray());
    }

    /// <summary>Phản hồi với tên chủ ghi đầy đủ cho từng bản ghi.</summary>
    public static byte[] ResponseWithOwners(byte[] query, params (string Owner, ushort Type, byte[] Data)[] answers)
    {
        var ms = new MemoryStream();
        ms.Write(Response(query, 0)[..6]);
        WriteU16(ms, (ushort)answers.Length);
        WriteU16(ms, 0);
        WriteU16(ms, 0);
        ms.Write(query, 12, query.Length - 12);
        foreach (var (owner, type, data) in answers)
        {
            WriteName(ms, owner);
            ms.WriteByte(0);
            WriteU16(ms, type);
            WriteU16(ms, 1);
            ms.Write([0, 0, 1, 44]);
            WriteU16(ms, (ushort)data.Length);
            ms.Write(data);
        }

        return ms.ToArray();
    }

    /// <summary>MX có tên dạng "<paramref name="label"/>" + con trỏ về tên trong câu hỏi.</summary>
    public static (ushort, byte[]) MxPointer(ushort preference, string label)
    {
        var ms = new MemoryStream();
        WriteU16(ms, preference);
        WriteName(ms, label);
        ms.Write([0xC0, 12]);
        return (15, ms.ToArray());
    }

    public static (ushort, byte[]) Address(ushort type) => (type, new byte[type == 1 ? 4 : 16]);

    private static void WriteName(MemoryStream ms, string name)
    {
        if (name.Length == 0)
        {
            return;
        }

        foreach (var label in name.Split('.'))
        {
            ms.WriteByte((byte)label.Length);
            ms.Write(Encoding.ASCII.GetBytes(label));
        }
    }

    private static void WriteU16(MemoryStream ms, ushort value)
    {
        ms.WriteByte((byte)(value >> 8));
        ms.WriteByte((byte)value);
    }
}

/// <summary>Test cần mạng: bị bỏ qua (và báo là bỏ qua) trừ khi đặt THUVIEN_NETWORK_TESTS=1.</summary>
public sealed class NetworkFactAttribute : FactAttribute
{
    public NetworkFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("THUVIEN_NETWORK_TESTS") != "1")
        {
            Skip = "Cần mạng: đặt THUVIEN_NETWORK_TESTS=1 để chạy.";
        }
    }
}
