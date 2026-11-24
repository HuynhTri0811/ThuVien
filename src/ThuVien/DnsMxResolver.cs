using System.Buffers.Binary;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace ThuVien;

/// <summary>Một bản ghi MX: máy chủ nhận mail và độ ưu tiên (số nhỏ được ưu tiên trước).</summary>
/// <param name="Preference">Độ ưu tiên.</param>
/// <param name="Host">Tên máy chủ nhận mail, viết thường, không có dấu chấm cuối.</param>
public readonly record struct MxRecord(ushort Preference, string Host);

/// <summary>Kết quả tra máy chủ nhận mail của một tên miền.</summary>
public enum MailServerStatus
{
    /// <summary>Có bản ghi MX.</summary>
    Found,

    /// <summary>Không có MX nhưng có A/AAAA: chính tên miền nhận mail (RFC 5321 §5.1).</summary>
    ImplicitMx,

    /// <summary>Tên miền khai báo không nhận mail (Null MX, RFC 7505).</summary>
    NullMx,

    /// <summary>Tên miền tồn tại nhưng không có MX, A hay AAAA.</summary>
    NoMailServer,

    /// <summary>Tên miền không tồn tại (NXDOMAIN).</summary>
    DomainNotFound,

    /// <summary>Không tra được (hết thời gian chờ, máy chủ DNS lỗi, không có mạng).</summary>
    Failed,
}

/// <summary>Kết quả tra máy chủ nhận mail.</summary>
/// <param name="Status">Trạng thái.</param>
/// <param name="Records">Các máy chủ nhận mail, sắp theo độ ưu tiên; rỗng nếu không có.</param>
public sealed record MailServerLookup(MailServerStatus Status, IReadOnlyList<MxRecord> Records);

/// <summary>Tra máy chủ nhận mail của một tên miền. Cài đặt riêng để dùng DNS khác hoặc để test.</summary>
public interface IMailServerResolver
{
    /// <summary>Tra máy chủ nhận mail của <paramref name="asciiDomain"/> (tên miền dạng ASCII/Punycode).</summary>
    Task<MailServerLookup> LookupAsync(string asciiDomain, CancellationToken cancellationToken = default);
}

/// <summary>
/// Tra bản ghi MX bằng cách gửi truy vấn DNS trực tiếp (UDP, tự chuyển sang TCP khi phản hồi bị cắt),
/// không cần thư viện ngoài. Mặc định dùng các máy chủ DNS mà hệ điều hành đang cấu hình.
/// </summary>
/// <remarks>
/// Mỗi máy chủ DNS được thử một lần, lần lượt theo thứ tự; máy chủ không trả lời, trả SERVFAIL/REFUSED
/// hay trả gói tin sai thì chuyển sang máy chủ kế tiếp. <see cref="Timeout"/> là thời gian chờ tổng cho
/// một máy chủ (gồm cả truy vấn MX và A/AAAA), nên một lần tra mất tối đa khoảng số máy chủ × <see cref="Timeout"/>.
/// Lớp này an toàn khi dùng từ nhiều luồng.
/// </remarks>
public sealed class DnsMxResolver : IMailServerResolver
{
    private const ushort TypeA = 1;
    private const ushort TypeMx = 15;
    private const ushort TypeAaaa = 28;

    private static readonly ushort[] AddressTypes = [TypeA, TypeAaaa];

    // Danh sách máy chủ DNS của hệ thống được lưu tạm để không liệt kê card mạng ở mỗi lần tra.
    private static readonly long SystemServersCacheMs = 30_000;
    private static (IPEndPoint[] Servers, long ExpiresAt)? s_systemServers;

    private readonly IPEndPoint[]? _servers;

    /// <summary>Thể hiện dùng chung, tra qua máy chủ DNS của hệ thống với thời gian chờ mặc định.</summary>
    public static DnsMxResolver Default { get; } = new();

    /// <summary>Dùng máy chủ DNS của hệ thống, thời gian chờ mỗi máy chủ là 3 giây.</summary>
    public DnsMxResolver()
        : this(null)
    {
    }

    /// <summary>Dùng các máy chủ DNS chỉ định (vd. <c>new IPEndPoint(IPAddress.Parse("1.1.1.1"), 53)</c>).</summary>
    /// <param name="servers">Máy chủ DNS; <c>null</c> để dùng của hệ thống.</param>
    /// <param name="timeout">Thời gian chờ tổng cho mỗi máy chủ; mặc định 3 giây.</param>
    /// <exception cref="ArgumentException">Danh sách máy chủ rỗng hoặc có phần tử <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="timeout"/> không lớn hơn 0.</exception>
    public DnsMxResolver(IEnumerable<IPEndPoint>? servers, TimeSpan? timeout = null)
    {
        _servers = servers?.ToArray();
        if (_servers is { Length: 0 })
        {
            throw new ArgumentException("Cần ít nhất một máy chủ DNS.", nameof(servers));
        }

        if (_servers is not null && Array.IndexOf(_servers, null) >= 0)
        {
            throw new ArgumentException("Danh sách máy chủ DNS có phần tử null.", nameof(servers));
        }

        Timeout = timeout ?? TimeSpan.FromSeconds(3);
        if (Timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Thời gian chờ phải lớn hơn 0.");
        }
    }

    /// <summary>Thời gian chờ tổng cho mỗi máy chủ DNS (gồm mọi truy vấn gửi tới máy chủ đó).</summary>
    public TimeSpan Timeout { get; }

    /// <inheritdoc />
    /// <exception cref="ArgumentException">Tên miền rỗng hoặc không hợp lệ.</exception>
    public async Task<MailServerLookup> LookupAsync(string asciiDomain, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(asciiDomain);
        var domain = EmailValidator.ToAsciiDomain(asciiDomain.TrimEnd('.'))
            ?? throw new ArgumentException($"Tên miền '{asciiDomain}' không hợp lệ.", nameof(asciiDomain));

        var servers = _servers ?? GetSystemDnsServers();
        foreach (var server in servers)
        {
            try
            {
                return await LookupWithServerAsync(server, domain, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is DnsServerException or TimeoutException or SocketException or IOException or FormatException)
            {
                // Thử máy chủ kế tiếp.
            }
        }

        return new MailServerLookup(MailServerStatus.Failed, []);
    }

    private async Task<MailServerLookup> LookupWithServerAsync(IPEndPoint server, string domain, CancellationToken ct)
    {
        // Một hạn chờ chung cho mọi truy vấn tới máy chủ này.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(Timeout);
        try
        {
            return await LookupWithServerCoreAsync(server, domain, deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"Máy chủ DNS {server} không trả lời trong {Timeout}.");
        }
    }

    private static async Task<MailServerLookup> LookupWithServerCoreAsync(IPEndPoint server, string domain, CancellationToken ct)
    {
        var mx = await QueryAsync(server, domain, TypeMx, ct).ConfigureAwait(false);
        if (mx.ResponseCode == DnsMessage.NameError)
        {
            return new MailServerLookup(MailServerStatus.DomainNotFound, []);
        }

        if (mx.MxRecords.Count > 0)
        {
            // RFC 7505: MX trỏ tới "." nghĩa là tên miền không nhận mail.
            if (mx.MxRecords.Any(r => r.Host.Length == 0))
            {
                return new MailServerLookup(MailServerStatus.NullMx, []);
            }

            var sorted = mx.MxRecords
                .OrderBy(r => r.Preference)
                .ThenBy(r => r.Host, StringComparer.Ordinal)
                .ToArray();
            return new MailServerLookup(MailServerStatus.Found, sorted);
        }

        // Không có MX: tên miền có địa chỉ A/AAAA thì chính nó nhận mail.
        // Câu trả lời "không có MX" đã chắc chắn; một truy vấn địa chỉ lỗi không được làm mất nó,
        // chỉ khi mọi truy vấn địa chỉ đều lỗi mới coi là không tra được.
        Exception? addressError = null;
        var answered = false;
        foreach (var type in AddressTypes)
        {
            DnsResponse address;
            try
            {
                address = await QueryAsync(server, domain, type, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is DnsServerException or SocketException or IOException or FormatException)
            {
                addressError = ex;
                continue;
            }

            answered = true;
            if (address.AddressCount > 0)
            {
                return new MailServerLookup(MailServerStatus.ImplicitMx, [new MxRecord(0, domain)]);
            }
        }

        if (!answered)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(addressError!);
        }

        return new MailServerLookup(MailServerStatus.NoMailServer, []);
    }

    /// <summary>Gửi một truy vấn; ném <see cref="DnsServerException"/> nếu máy chủ trả mã lỗi khác NXDOMAIN.</summary>
    private static async Task<DnsResponse> QueryAsync(IPEndPoint server, string domain, ushort type, CancellationToken ct)
    {
        var id = (ushort)RandomNumberGenerator.GetInt32(0, 0x10000);
        var query = DnsMessage.BuildQuery(id, domain, type);

        var response = await QueryUdpAsync(server, query, id, domain, type, ct).ConfigureAwait(false);
        if (response.Truncated)
        {
            response = await QueryTcpAsync(server, query, id, domain, type, ct).ConfigureAwait(false);
        }

        return response.ResponseCode is DnsMessage.NoError or DnsMessage.NameError
            ? response
            : throw new DnsServerException(response.ResponseCode);
    }

    private static async Task<DnsResponse> QueryUdpAsync(
        IPEndPoint server, byte[] query, ushort id, string domain, ushort type, CancellationToken ct)
    {
        using var udp = new UdpClient(server.AddressFamily);
        udp.Connect(server);
        await udp.SendAsync(query, ct).ConfigureAwait(false);
        while (true)
        {
            var packet = await udp.ReceiveAsync(ct).ConfigureAwait(false);

            // Bỏ qua gói không khớp mã truy vấn hoặc hỏng (gói trễ hoặc giả mạo), tiếp tục chờ gói đúng
            // tới khi hết hạn chờ.
            if (DnsMessage.TryReadId(packet.Buffer, out var responseId) && responseId == id)
            {
                try
                {
                    return DnsMessage.Parse(packet.Buffer, domain, type);
                }
                catch (FormatException)
                {
                    // chờ gói kế tiếp
                }
            }
        }
    }

    private static async Task<DnsResponse> QueryTcpAsync(
        IPEndPoint server, byte[] query, ushort id, string domain, ushort type, CancellationToken ct)
    {
        using var tcp = new TcpClient(server.AddressFamily);
        await tcp.ConnectAsync(server, ct).ConfigureAwait(false);
        var stream = tcp.GetStream();

        var framed = new byte[query.Length + 2];
        BinaryPrimitives.WriteUInt16BigEndian(framed, (ushort)query.Length);
        query.CopyTo(framed, 2);
        await stream.WriteAsync(framed, ct).ConfigureAwait(false);

        var lengthBytes = new byte[2];
        await stream.ReadExactlyAsync(lengthBytes, ct).ConfigureAwait(false);
        var response = new byte[BinaryPrimitives.ReadUInt16BigEndian(lengthBytes)];
        await stream.ReadExactlyAsync(response, ct).ConfigureAwait(false);

        if (!DnsMessage.TryReadId(response, out var responseId) || responseId != id)
        {
            throw new FormatException("Phản hồi DNS qua TCP không khớp mã truy vấn.");
        }

        var parsed = DnsMessage.Parse(response, domain, type);
        return parsed.Truncated ? throw new FormatException("Phản hồi DNS qua TCP vẫn bị cắt.") : parsed;
    }

    private static IPEndPoint[] GetSystemDnsServers()
    {
        var now = Environment.TickCount64;
        if (s_systemServers is { } cached && now < cached.ExpiresAt)
        {
            return cached.Servers;
        }

        var servers = ReadSystemDnsServers();
        s_systemServers = (servers, now + SystemServersCacheMs);
        return servers;
    }

    private static IPEndPoint[] ReadSystemDnsServers()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up)
                .SelectMany(n => n.GetIPProperties().DnsAddresses)
                // fec0:0:0:ffff::1/2/3 là địa chỉ DNS mặc định cũ của Windows, thường không dùng được.
                .Where(a => !(a.AddressFamily == AddressFamily.InterNetworkV6 && a.IsIPv6SiteLocal))
                .Distinct()
                .Select(a => new IPEndPoint(a, 53))
                .ToArray();
        }
        catch (Exception ex) when (ex is NetworkInformationException or PlatformNotSupportedException)
        {
            return [];
        }
    }

    private sealed class DnsServerException(int responseCode)
        : Exception($"Máy chủ DNS trả mã lỗi {responseCode}.");
}

/// <summary>Phản hồi DNS đã đọc (chỉ giữ phần cần cho việc tra máy chủ mail).</summary>
internal sealed record DnsResponse(int ResponseCode, bool Truncated, List<MxRecord> MxRecords, int AddressCount);

/// <summary>Tạo và đọc gói tin DNS (RFC 1035).</summary>
internal static class DnsMessage
{
    internal const int NoError = 0;
    internal const int NameError = 3;

    private const int HeaderLength = 12;
    private const ushort ClassIn = 1;
    private const ushort TypeA = 1;
    private const ushort TypeCname = 5;
    private const ushort TypeMx = 15;
    private const ushort TypeAaaa = 28;

    /// <summary>Tạo truy vấn chuẩn, yêu cầu đệ quy (RD).</summary>
    internal static byte[] BuildQuery(ushort id, string name, ushort type)
    {
        var labels = name.Length == 0 ? [] : name.Split('.');
        var buffer = new byte[HeaderLength + name.Length + 2 + 4];
        BinaryPrimitives.WriteUInt16BigEndian(buffer, id);
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(2), 0x0100); // RD
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(4), 1); // QDCOUNT

        var pos = HeaderLength;
        foreach (var label in labels)
        {
            if (label.Length is 0 or > 63 || !Ascii.IsValid(label))
            {
                throw new ArgumentException($"Nhãn tên miền '{label}' không hợp lệ.", nameof(name));
            }

            buffer[pos++] = (byte)label.Length;
            pos += Encoding.ASCII.GetBytes(label, buffer.AsSpan(pos));
        }

        buffer[pos++] = 0;
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(pos), type);
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(pos + 2), ClassIn);
        return buffer[..(pos + 4)];
    }

    internal static bool TryReadId(ReadOnlySpan<byte> message, out ushort id)
    {
        id = message.Length >= 2 ? BinaryPrimitives.ReadUInt16BigEndian(message) : (ushort)0;
        return message.Length >= HeaderLength;
    }

    /// <summary>
    /// Đọc phản hồi cho câu hỏi (<paramref name="name"/>, <paramref name="type"/>).
    /// </summary>
    /// <exception cref="FormatException">Gói tin hỏng, không phải phản hồi, hoặc không khớp câu hỏi.</exception>
    internal static DnsResponse Parse(ReadOnlySpan<byte> message, string name, ushort type)
    {
        if (message.Length < HeaderLength)
        {
            throw new FormatException("Gói DNS quá ngắn.");
        }

        var flags = BinaryPrimitives.ReadUInt16BigEndian(message[2..]);
        if ((flags & 0x8000) == 0 || ((flags >> 11) & 0xF) != 0)
        {
            throw new FormatException("Gói DNS không phải phản hồi cho truy vấn chuẩn.");
        }

        var rcode = flags & 0xF;
        if ((flags & 0x0200) != 0)
        {
            return new DnsResponse(rcode, true, [], 0);
        }

        var questions = BinaryPrimitives.ReadUInt16BigEndian(message[4..]);
        var answers = BinaryPrimitives.ReadUInt16BigEndian(message[6..]);
        var pos = HeaderLength;

        if (questions != 1)
        {
            throw new FormatException("Phản hồi DNS phải có đúng một câu hỏi.");
        }

        var questionName = ReadName(message, ref pos);
        if (pos + 4 > message.Length
            || !string.Equals(questionName, name, StringComparison.OrdinalIgnoreCase)
            || BinaryPrimitives.ReadUInt16BigEndian(message[pos..]) != type
            || BinaryPrimitives.ReadUInt16BigEndian(message[(pos + 2)..]) != ClassIn)
        {
            throw new FormatException("Câu hỏi trong phản hồi DNS không khớp truy vấn.");
        }

        pos += 4;

        // Đọc hết các bản ghi trước, rồi chỉ lấy bản ghi của tên đã hỏi hoặc của tên bí danh (CNAME) dẫn từ nó;
        // bản ghi của tên khác trong phần trả lời bị bỏ qua.
        var records = new List<(string Owner, ushort Type, int Start, int Length)>(answers);
        for (var i = 0; i < answers; i++)
        {
            var owner = ReadName(message, ref pos);
            if (pos + 10 > message.Length)
            {
                throw new FormatException("Bản ghi DNS bị cắt.");
            }

            var recordType = BinaryPrimitives.ReadUInt16BigEndian(message[pos..]);
            var recordClass = BinaryPrimitives.ReadUInt16BigEndian(message[(pos + 2)..]);
            var length = BinaryPrimitives.ReadUInt16BigEndian(message[(pos + 8)..]);
            pos += 10;
            if (pos + length > message.Length)
            {
                throw new FormatException("Dữ liệu bản ghi DNS bị cắt.");
            }

            if (recordClass == ClassIn)
            {
                records.Add((owner, recordType, pos, length));
            }

            pos += length;
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { name };
        for (var changed = true; changed && names.Count <= 16;)
        {
            changed = false;
            foreach (var r in records)
            {
                if (r.Type == TypeCname && names.Contains(r.Owner))
                {
                    var targetPos = r.Start;
                    changed |= names.Add(ReadName(message[..(r.Start + r.Length)], ref targetPos));
                }
            }
        }

        var mx = new List<MxRecord>();
        var addressCount = 0;
        foreach (var r in records)
        {
            if (!names.Contains(r.Owner))
            {
                continue;
            }

            switch (r.Type)
            {
                case TypeMx when r.Length >= 3:
                    var preference = BinaryPrimitives.ReadUInt16BigEndian(message[r.Start..]);
                    var hostPos = r.Start + 2;
                    var host = ReadName(message[..(r.Start + r.Length)], ref hostPos);

                    // Bỏ tên máy chủ không phải tên miền hợp lệ (ký tự lạ, byte ngoài ASCII, ...).
                    if (host.Length == 0 || IsHostName(host))
                    {
                        mx.Add(new MxRecord(preference, host));
                    }

                    break;
                case TypeA when r.Length == 4:
                case TypeAaaa when r.Length == 16:
                    addressCount++;
                    break;
            }
        }

        return new DnsResponse(rcode, false, mx, addressCount);
    }

    private static bool IsHostName(string host)
    {
        foreach (var label in host.Split('.'))
        {
            if (!EmailValidator.IsLdhLabel(label))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Đọc tên miền, hỗ trợ con trỏ nén (RFC 1035 §4.1.4); trả về dạng viết thường không có dấu chấm cuối.</summary>
    internal static string ReadName(ReadOnlySpan<byte> message, ref int offset)
    {
        var sb = new StringBuilder();
        var pos = offset;
        var jumped = false;
        var jumps = 0;
        while (true)
        {
            if (pos >= message.Length)
            {
                throw new FormatException("Tên miền trong gói DNS bị cắt.");
            }

            var length = message[pos];
            if ((length & 0xC0) == 0xC0)
            {
                if (pos + 1 >= message.Length || ++jumps > 32)
                {
                    throw new FormatException("Con trỏ tên miền trong gói DNS không hợp lệ.");
                }

                if (!jumped)
                {
                    offset = pos + 2;
                    jumped = true;
                }

                pos = ((length & 0x3F) << 8) | message[pos + 1];
                continue;
            }

            if ((length & 0xC0) != 0)
            {
                throw new FormatException("Nhãn tên miền trong gói DNS không hợp lệ.");
            }

            pos++;
            if (length == 0)
            {
                if (!jumped)
                {
                    offset = pos;
                }

                return sb.ToString();
            }

            if (pos + length > message.Length || sb.Length + length + 1 > 255)
            {
                throw new FormatException("Tên miền trong gói DNS không hợp lệ.");
            }

            if (sb.Length > 0)
            {
                sb.Append('.');
            }

            foreach (var b in message.Slice(pos, length))
            {
                // Byte ngoài ASCII in được, hoặc dấu chấm nằm trong nhãn, được thay bằng U+FFFD để tên
                // không bị hiểu nhầm (vd. nhãn "a.b" thành hai nhãn) và không qua được kiểm tra tên miền.
                sb.Append(b is > 0x20 and < 0x7F and not (byte)'.' ? char.ToLowerInvariant((char)b) : '\uFFFD');
            }

            pos += length;
        }
    }
}
