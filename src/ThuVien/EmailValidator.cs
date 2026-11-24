using System.Globalization;

namespace ThuVien;

/// <summary>Lý do một địa chỉ email không hợp lệ.</summary>
public enum EmailError
{
    /// <summary>Hợp lệ.</summary>
    None,

    /// <summary>Chuỗi rỗng hoặc chỉ có khoảng trắng.</summary>
    Empty,

    /// <summary>Tổng độ dài vượt quá <see cref="EmailValidator.MaxLength"/> ký tự.</summary>
    TooLong,

    /// <summary>Không có ký tự <c>@</c>.</summary>
    MissingAt,

    /// <summary>Có nhiều hơn một ký tự <c>@</c>.</summary>
    MultipleAt,

    /// <summary>Phần tên (trước <c>@</c>) rỗng.</summary>
    LocalPartEmpty,

    /// <summary>Phần tên dài quá <see cref="EmailValidator.MaxLocalPartLength"/> ký tự.</summary>
    LocalPartTooLong,

    /// <summary>Phần tên chứa ký tự không cho phép hoặc dấu chấm sai vị trí.</summary>
    LocalPartInvalid,

    /// <summary>Phần tên miền (sau <c>@</c>) rỗng.</summary>
    DomainEmpty,

    /// <summary>Tên miền dài quá <see cref="EmailValidator.MaxDomainLength"/> ký tự (dạng ASCII).</summary>
    DomainTooLong,

    /// <summary>Tên miền sai cú pháp (nhãn rỗng, ký tự lạ, thiếu đuôi tên miền, ...).</summary>
    DomainInvalid,

    /// <summary>Tên miền không tồn tại (DNS trả NXDOMAIN).</summary>
    DomainNotFound,

    /// <summary>Tên miền tồn tại nhưng không có bản ghi MX, A hay AAAA để nhận mail.</summary>
    NoMailServer,

    /// <summary>Tên miền khai báo không nhận mail (Null MX, RFC 7505).</summary>
    NullMx,

    /// <summary>
    /// Không tra được DNS (hết thời gian chờ, máy chủ DNS lỗi, không có mạng).
    /// Đây là lỗi tạm thời: địa chỉ có thể vẫn đúng, nên cân nhắc thử lại thay vì từ chối.
    /// </summary>
    DnsLookupFailed,
}

/// <summary>Kết quả kiểm tra một địa chỉ email.</summary>
public sealed record EmailValidationResult
{
    /// <summary><c>true</c> khi <see cref="Error"/> là <see cref="EmailError.None"/>.</summary>
    public bool IsValid => Error == EmailError.None;

    /// <summary>Lý do không hợp lệ, hoặc <see cref="EmailError.None"/>.</summary>
    public EmailError Error { get; init; }

    /// <summary>Mô tả lỗi bằng tiếng Việt, dùng được để hiển thị cho người dùng.</summary>
    public string Message => EmailValidator.Describe(Error);

    /// <summary>
    /// Dạng chuẩn hóa: bỏ khoảng trắng hai đầu, tên miền viết thường dạng Unicode
    /// (vd. <c>An@Thư.VN</c> → <c>An@thư.vn</c>). Phần tên giữ nguyên hoa thường.
    /// Có giá trị khi cú pháp hợp lệ.
    /// </summary>
    public string? NormalizedEmail { get; init; }

    /// <summary>
    /// Dạng ASCII để gửi mail: tên miền đổi sang Punycode (vd. <c>An@xn--th-e0a.vn</c>).
    /// Có giá trị khi cú pháp hợp lệ.
    /// </summary>
    public string? AsciiEmail { get; init; }

    /// <summary>
    /// Gợi ý khi tên miền giống một nhà cung cấp phổ biến bị gõ sai
    /// (vd. <c>an@gmial.com</c> → <c>an@gmail.com</c>). Chỉ là gợi ý, không ảnh hưởng <see cref="IsValid"/>.
    /// </summary>
    public string? Suggestion { get; init; }

    /// <summary><c>true</c> nếu đã tra máy chủ mail qua DNS (dùng <see cref="EmailValidator.ValidateWithMailServerAsync"/>).</summary>
    public bool MailServerChecked { get; init; }

    /// <summary>Các máy chủ nhận mail tìm được, sắp theo độ ưu tiên. Rỗng nếu chưa tra hoặc không có.</summary>
    public IReadOnlyList<MxRecord> MailServers { get; init; } = [];
}

/// <summary>
/// Kiểm tra địa chỉ email: cú pháp theo RFC 5321/5322, tên miền quốc tế (IDN, vd. <c>thư.vn</c>),
/// gợi ý sửa lỗi gõ, và (tùy chọn) tra bản ghi MX để biết tên miền có nhận mail không.
/// </summary>
/// <remarks>
/// Kiểm tra cú pháp chặt hơn <see cref="Validators.IsEmail"/>. Hỗ trợ phần tên dạng dot-atom
/// (chữ, số và <c>!#$%&amp;'*+-/=?^_`{|}~</c>, dấu chấm không ở đầu/cuối và không liền nhau).
/// Không hỗ trợ phần tên trong ngoặc kép, phần tên có ký tự Unicode (SMTPUTF8), chú thích
/// hay địa chỉ IP thay cho tên miền: những dạng này hợp lệ về lý thuyết nhưng hiếm khi dùng
/// và nhiều hệ thống không nhận.
/// </remarks>
public static class EmailValidator
{
    /// <summary>Độ dài tối đa của cả địa chỉ (RFC 5321, dạng ASCII).</summary>
    public const int MaxLength = 254;

    /// <summary>Độ dài tối đa của phần tên trước <c>@</c> (RFC 5321).</summary>
    public const int MaxLocalPartLength = 64;

    /// <summary>Độ dài tối đa của tên miền dạng ASCII (RFC 1035).</summary>
    public const int MaxDomainLength = 253;

    // Chặn sớm chuỗi quá dài trước khi đổi IDN (dạng Unicode có thể ngắn hơn dạng ASCII, nên để dư).
    private const int MaxInputLength = 1024;

    private const string AtextSymbols = "!#$%&'*+-/=?^_`{|}~";

    /// <summary>Tên miền mail phổ biến dùng để gợi ý sửa lỗi gõ.</summary>
    private static readonly string[] CommonDomains =
    [
        "gmail.com", "googlemail.com", "yahoo.com", "yahoo.com.vn", "ymail.com",
        "hotmail.com", "outlook.com", "outlook.com.vn", "live.com", "msn.com",
        "icloud.com", "me.com", "mac.com", "aol.com", "proton.me", "protonmail.com",
        "mail.com", "gmx.com", "zoho.com", "yandex.com", "vnn.vn", "fpt.vn",
    ];

    /// <summary>
    /// Kiểm tra cú pháp email (không dùng mạng).
    /// </summary>
    public static EmailValidationResult Validate(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return Fail(EmailError.Empty);
        }

        var s = email.Trim();
        if (s.Length > MaxInputLength)
        {
            return Fail(EmailError.TooLong);
        }

        var at = s.IndexOf('@');
        if (at < 0)
        {
            return Fail(EmailError.MissingAt);
        }

        if (s.IndexOf('@', at + 1) >= 0)
        {
            return Fail(EmailError.MultipleAt);
        }

        var local = s[..at];
        var domain = s[(at + 1)..];

        var localError = CheckLocalPart(local);
        if (localError != EmailError.None)
        {
            return Fail(localError);
        }

        if (domain.Length == 0)
        {
            return Fail(EmailError.DomainEmpty);
        }

        // IdnMapping tự từ chối tên miền quá dài; kiểm tra trước để báo đúng lỗi.
        if (domain.Length > MaxDomainLength && System.Text.Ascii.IsValid(domain))
        {
            return Fail(EmailError.DomainTooLong);
        }

        var asciiDomain = ToAsciiDomain(domain, out var unicodeDomain);
        if (asciiDomain is null)
        {
            // Vẫn gợi ý được, vd. "an@gmail" → "an@gmail.com".
            return Fail(EmailError.DomainInvalid) with { Suggestion = Suggest(local, domain.ToLowerInvariant()) };
        }

        if (asciiDomain.Length > MaxDomainLength)
        {
            return Fail(EmailError.DomainTooLong);
        }

        if (local.Length + 1 + asciiDomain.Length > MaxLength)
        {
            return Fail(EmailError.TooLong);
        }

        return new EmailValidationResult
        {
            Error = EmailError.None,
            NormalizedEmail = local + "@" + unicodeDomain,
            AsciiEmail = local + "@" + asciiDomain,
            Suggestion = Suggest(local, asciiDomain),
        };
    }

    /// <summary>Như <see cref="Validate"/> nhưng chỉ trả về đúng/sai.</summary>
    public static bool IsValid(string? email) => Validate(email).IsValid;

    /// <summary>
    /// Kiểm tra cú pháp, rồi tra DNS xem tên miền có nhận mail không: có bản ghi MX, hoặc
    /// không có MX nhưng có A/AAAA (RFC 5321 §5.1). Tên miền khai báo Null MX (RFC 7505) bị coi là
    /// không nhận mail.
    /// </summary>
    /// <param name="email">Địa chỉ cần kiểm tra.</param>
    /// <param name="resolver">Bộ tra máy chủ mail; mặc định <see cref="DnsMxResolver.Default"/> (dùng máy chủ DNS của hệ thống).</param>
    /// <param name="cancellationToken">Hủy thao tác.</param>
    /// <remarks>
    /// Có máy chủ mail không có nghĩa là hộp thư tồn tại; muốn chắc chắn vẫn cần gửi mail xác nhận.
    /// Khi DNS lỗi tạm thời, kết quả là <see cref="EmailError.DnsLookupFailed"/>.
    /// </remarks>
    public static async Task<EmailValidationResult> ValidateWithMailServerAsync(
        string? email,
        IMailServerResolver? resolver = null,
        CancellationToken cancellationToken = default)
    {
        var result = Validate(email);
        if (!result.IsValid)
        {
            return result;
        }

        var ascii = result.AsciiEmail!;
        var domain = ascii[(ascii.LastIndexOf('@') + 1)..];
        var lookup = await (resolver ?? DnsMxResolver.Default)
            .LookupAsync(domain, cancellationToken)
            .ConfigureAwait(false);

        var error = lookup.Status switch
        {
            MailServerStatus.Found or MailServerStatus.ImplicitMx => EmailError.None,
            MailServerStatus.NullMx => EmailError.NullMx,
            MailServerStatus.NoMailServer => EmailError.NoMailServer,
            MailServerStatus.DomainNotFound => EmailError.DomainNotFound,
            _ => EmailError.DnsLookupFailed,
        };

        return result with
        {
            Error = error,
            MailServerChecked = true,
            MailServers = lookup.Records,
        };
    }

    /// <summary>
    /// Gợi ý sửa khi tên miền giống một nhà cung cấp mail phổ biến bị gõ sai
    /// (vd. <c>an@gmial.com</c> → <c>an@gmail.com</c>). Trả về <c>null</c> nếu không có gợi ý.
    /// </summary>
    public static string? SuggestCorrection(string? email) => Validate(email).Suggestion;

    /// <summary>Mô tả lỗi bằng tiếng Việt.</summary>
    public static string Describe(EmailError error) => error switch
    {
        EmailError.None => "Email hợp lệ.",
        EmailError.Empty => "Chưa nhập email.",
        EmailError.TooLong => $"Email dài quá {MaxLength} ký tự.",
        EmailError.MissingAt => "Email thiếu ký tự @.",
        EmailError.MultipleAt => "Email chỉ được có một ký tự @.",
        EmailError.LocalPartEmpty => "Thiếu phần tên trước @.",
        EmailError.LocalPartTooLong => $"Phần tên trước @ dài quá {MaxLocalPartLength} ký tự.",
        EmailError.LocalPartInvalid => "Phần tên trước @ có ký tự không hợp lệ hoặc dấu chấm sai vị trí.",
        EmailError.DomainEmpty => "Thiếu tên miền sau @.",
        EmailError.DomainTooLong => $"Tên miền dài quá {MaxDomainLength} ký tự.",
        EmailError.DomainInvalid => "Tên miền không hợp lệ.",
        EmailError.DomainNotFound => "Tên miền không tồn tại.",
        EmailError.NoMailServer => "Tên miền không có máy chủ nhận mail.",
        EmailError.NullMx => "Tên miền khai báo không nhận mail.",
        EmailError.DnsLookupFailed => "Không kiểm tra được máy chủ mail, vui lòng thử lại sau.",
        _ => error.ToString(),
    };

    private static EmailValidationResult Fail(EmailError error) => new() { Error = error };

    private static EmailError CheckLocalPart(string local)
    {
        if (local.Length == 0)
        {
            return EmailError.LocalPartEmpty;
        }

        if (local.Length > MaxLocalPartLength)
        {
            return EmailError.LocalPartTooLong;
        }

        if (local[0] == '.' || local[^1] == '.' || local.Contains("..", StringComparison.Ordinal))
        {
            return EmailError.LocalPartInvalid;
        }

        foreach (var c in local)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '.' && !AtextSymbols.Contains(c))
            {
                return EmailError.LocalPartInvalid;
            }
        }

        return EmailError.None;
    }

    /// <summary>Đổi tên miền sang ASCII (Punycode, viết thường) và kiểm tra từng nhãn; <c>null</c> nếu không hợp lệ.</summary>
    internal static string? ToAsciiDomain(string domain) => ToAsciiDomain(domain, out _);

    /// <summary>
    /// Như <see cref="ToAsciiDomain(string)"/>, kèm dạng Unicode của tên miền (vd. <c>thư.vn</c>).
    /// Chỉ nhận tên miền đổi qua lại ASCII ↔ Unicode được mà không đổi giá trị, để kết quả giống nhau
    /// dù chạy với ICU hay ở chế độ invariant globalization (Docker/Alpine, AOT).
    /// </summary>
    internal static string? ToAsciiDomain(string domain, out string unicode)
    {
        unicode = "";

        // Ký tự điều khiển, định dạng vô hình (vd. U+200D), khoảng trắng, ... không bao giờ hợp lệ trong tên miền.
        // ICU tự từ chối chúng nhưng chế độ invariant thì không, nên chặn trước.
        foreach (var c in domain)
        {
            switch (char.GetUnicodeCategory(c))
            {
                case UnicodeCategory.Control:
                case UnicodeCategory.Format:
                case UnicodeCategory.SpaceSeparator:
                case UnicodeCategory.LineSeparator:
                case UnicodeCategory.ParagraphSeparator:
                case UnicodeCategory.Surrogate:
                case UnicodeCategory.PrivateUse:
                case UnicodeCategory.OtherNotAssigned:
                    return null;
            }
        }

        string ascii;
        try
        {
            var idn = new IdnMapping { UseStd3AsciiRules = true };
            ascii = idn.GetAscii(domain.ToLowerInvariant()).ToLowerInvariant();
            unicode = idn.GetUnicode(ascii);
            if (!string.Equals(idn.GetAscii(unicode), ascii, StringComparison.OrdinalIgnoreCase))
            {
                return null; // Punycode không chuẩn (giải mã rồi mã hóa lại ra chuỗi khác)
            }
        }
        catch (ArgumentException)
        {
            return null;
        }

        var labels = ascii.Split('.');
        if (labels.Length < 2)
        {
            return null; // cần có đuôi tên miền, vd. ".vn"
        }

        foreach (var label in labels)
        {
            if (!IsLdhLabel(label))
            {
                return null;
            }
        }

        // Đuôi tên miền toàn số (vd. "a@1.2.3.4") không phải tên miền.
        return labels[^1].All(char.IsAsciiDigit) ? null : ascii;
    }

    /// <summary>Nhãn 1–63 ký tự gồm chữ thường ASCII, số, gạch ngang; không bắt đầu/kết thúc bằng gạch ngang.</summary>
    internal static bool IsLdhLabel(string label)
    {
        if (label.Length is 0 or > 63 || label[0] == '-' || label[^1] == '-')
        {
            return false;
        }

        foreach (var c in label)
        {
            if (!char.IsAsciiLetterLower(c) && !char.IsAsciiDigit(c) && c != '-')
            {
                return false;
            }
        }

        return true;
    }

    private static string? Suggest(string local, string domain)
    {
        if (domain.Length > 64 || Array.IndexOf(CommonDomains, domain) >= 0)
        {
            return null;
        }

        // Thiếu đuôi tên miền (vd. "gmail") thì so với phần đầu của tên miền phổ biến.
        var missingSuffix = !domain.Contains('.');
        var maxDistance = domain.Length >= 6 ? 2 : 1;
        string? best = null;
        var bestDistance = int.MaxValue;
        var tie = false;
        foreach (var candidate in CommonDomains)
        {
            var d = Distance(domain, missingSuffix ? candidate[..candidate.IndexOf('.')] : candidate);
            if (d < bestDistance)
            {
                (best, bestDistance, tie) = (candidate, d, false);
            }
            else if (d == bestDistance)
            {
                tie = true;
            }
        }

        // Không gợi ý khi có hai ứng viên gần như nhau (không biết người dùng định gõ cái nào).
        return bestDistance <= maxDistance && !tie ? local + "@" + best : null;
    }

    /// <summary>Khoảng cách sửa chuỗi (Damerau–Levenshtein dạng hạn chế: thêm, xóa, thay, đổi chỗ hai ký tự kề nhau).</summary>
    internal static int Distance(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++)
        {
            d[i, 0] = i;
        }

        for (var j = 0; j <= b.Length; j++)
        {
            d[0, j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                {
                    d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
                }
            }
        }

        return d[a.Length, b.Length];
    }
}
