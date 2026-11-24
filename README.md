# ThuVien

Bộ tiện ích tiếng Việt cho .NET 10.

## Tính năng

| Lớp | Hàm | Ví dụ |
|---|---|---|
| `VietnameseText` | `RemoveDiacritics` | `"Tiếng Việt"` → `"Tieng Viet"` |
| | `ToSlug` | `"Xin chào, Việt Nam!"` → `"xin-chao-viet-nam"` |
| `NumberToWords` | `ToVietnamese` | `1005` → `"một nghìn không trăm linh năm"` |
| | `ToVietnameseCurrency` | `125000` → `"Một trăm hai mươi lăm nghìn đồng"` |
| `WordsToNumber` | `FromVietnamese` | `"một nghìn không trăm linh năm"` → `1005` |
| | `TryFromVietnamese` | `"hai mươi tư"` → `true`, `24` |
| `Validators` | `IsVietnamesePhone` | `"+84 912 345 678"` → `true` |
| | `NormalizeVietnamesePhone` | `"+84 912 345 678"` → `"0912345678"` |
| | `IsEmail` | `"a@b.vn"` → `true` |
| `EmailValidator` | `Validate` | `"An@Thư.VN"` → hợp lệ, `NormalizedEmail = "An@thư.vn"` |
| | `ValidateWithMailServerAsync` | `"an@example.com"` → `EmailError.NullMx` (tên miền không nhận mail) |
| | `SuggestCorrection` | `"an@gmial.com"` → `"an@gmail.com"` |
| `CurrencyFormatter` | `ToVnd` | `1234567m` → `"1.234.567 ₫"` |
| `DbDateTimeConverter` | `PostgresTypeToSqlServer` | `"timestamptz(3)"` → `"datetimeoffset(3)"` |
| | `SqlServerTypeToPostgres` | `"datetime"` → `"timestamp(3)"` |
| | `PostgresToSqlServer` | `"2024-01-02 03:04:05+07"` → `"2024-01-01T20:04:05"` |
| | `SqlServerToPostgres` | `"2024-01-02 03:04:05.1234567"` → `"2024-01-02 03:04:05.123457"` |

## Bỏ dấu và tạo slug

```csharp
VietnameseText.RemoveDiacritics("Đường Nguyễn Huệ");           // "Duong Nguyen Hue"
VietnameseText.ToSlug("  Đà Lạt -- mộng mơ!!  ", '_');          // "da_lat_mong_mo"
VietnameseText.ToSlug("Cà phê ☕ sữa đá");                      // "ca-phe-sua-da"
```

Quy tắc chính:
- `RemoveDiacritics` bỏ mọi dấu thanh và dấu mũ, đổi `đ`/`Đ` thành `d`/`D`, giữ nguyên chữ hoa thường và các ký tự khác. `null` hoặc chuỗi rỗng trả về `""`.
- `ToSlug` bỏ dấu, viết thường, chỉ giữ chữ cái và chữ số ASCII. Mỗi đoạn ký tự khác (khoảng trắng, dấu câu, emoji, ...) gộp thành một dấu phân cách. Không có dấu phân cách ở đầu hay cuối.
- Dấu phân cách mặc định là `-`, đổi bằng tham số thứ hai.

## Đọc số thành chữ

```csharp
NumberToWords.ToVietnamese(21);         // "hai mươi mốt"
NumberToWords.ToVietnamese(105);        // "một trăm linh năm"
NumberToWords.ToVietnamese(1_000_005);  // "một triệu không trăm linh năm"
NumberToWords.ToVietnamese(-5);         // "âm năm"
NumberToWords.ToVietnameseCurrency(0);  // "Không đồng"
```

Quy tắc chính:
- Nhận mọi giá trị `long`, kể cả `long.MinValue`. Số âm có tiền tố `âm`.
- Đọc theo cách phổ biến ở miền Bắc: `linh` (không dùng `lẻ`), `nghìn` (không dùng `ngàn`), `tỷ`, `bốn` sau `mươi` (không dùng `tư`).
- `mốt` cho hàng đơn vị 1 khi hàng chục từ 2 trở lên (`21` → `hai mươi mốt`, `11` → `mười một`). `lăm` cho hàng đơn vị 5 khi có hàng chục (`15` → `mười lăm`).
- Nhóm ba chữ số không đứng đầu được đọc đủ `không trăm`, `linh`. Nhóm toàn số 0 thì bỏ qua.
- Sau `nghìn tỷ` là `triệu tỷ`, `tỷ tỷ`.
- `ToVietnameseCurrency` viết hoa chữ cái đầu và thêm `đồng` vào cuối.

## Số điện thoại

```csharp
Validators.IsVietnamesePhone("0912.345.678");          // true
Validators.NormalizeVietnamesePhone("84912345678");     // "0912345678"
Validators.NormalizeVietnamesePhone("+84-912-345-678"); // "0912345678"
Validators.NormalizeVietnamesePhone("02838123456");     // null (số cố định)
```

Quy tắc chính:
- Chỉ nhận số di động 10 chữ số, đầu số `03`, `05`, `07`, `08`, `09`. Số cố định và số 11 chữ số kiểu cũ (`01xx`) không hợp lệ.
- Chấp nhận tiền tố `0`, `+84` hoặc `84`. Dấu cách, dấu chấm và gạch ngang được bỏ qua. Ngoặc đơn như `(+84)` thì không.
- `NormalizeVietnamesePhone` trả về dạng `0xxxxxxxxx`, hoặc `null` nếu số không hợp lệ.

## Định dạng tiền VND

```csharp
CurrencyFormatter.ToVnd(1234567.5m);    // "1.234.568 ₫"
CurrencyFormatter.ToVnd(-50000);        // "-50.000 ₫"
CurrencyFormatter.ToVnd(1000, "VND");   // "1.000 VND"
CurrencyFormatter.ToVnd(1000, "");      // "1.000"
```

Quy tắc chính:
- Làm tròn đến đồng; nửa đồng làm tròn ra xa số 0 (`0,5` → `1`, `-0,5` → `-1`).
- Dấu chấm ngăn cách hàng nghìn, không phụ thuộc vào `CultureInfo` của máy.
- Ký hiệu đứng sau số, cách một dấu cách. Truyền `""` để bỏ ký hiệu.

## Chuyển ngày giờ PostgreSQL ↔ SQL Server

```csharp
// Kiểu dữ liệu (dùng khi chuyển schema)
DbDateTimeConverter.PostgresTypeToSqlServer("timestamp(3) with time zone"); // datetimeoffset(3)
DbDateTimeConverter.SqlServerTypeToPostgres("datetime2(7)");                // timestamp(6)

// Giá trị (dùng khi chuyển dữ liệu)
DbDateTimeConverter.PostgresToSqlServer("2024-01-02 03:04:05.998", SqlServerDateType.DateTime);
// → "2024-01-02T03:04:05.997"  (làm tròn 1/300 giây như SQL Server)

DbDateTimeConverter.SqlServerToPostgres("2024-01-02 03:04:05.1234567 +07:00", PostgresDateType.TimestampTz);
// → "2024-01-02 03:04:05.123457+07:00"  (PostgreSQL chỉ lưu tới micro giây)

// Đổi infinity, ngày BC, năm > 9999 thành giá trị lớn nhất/nhỏ nhất thay vì báo lỗi
var options = new DbDateTimeOptions { Infinity = InfinityHandling.ClampToRange };
DbDateTimeConverter.PostgresToSqlServer("infinity", SqlServerDateType.DateTime, options);
// → "9999-12-31T23:59:59.997"

// Nhận thẳng giá trị đọc từ Npgsql / SqlClient, không cần tự ToString()
DateTime fromNpgsql = reader.GetDateTime(0);
DbDateTimeConverter.ToSqlServer(fromNpgsql, SqlServerDateType.DateTime2);
DbDateTimeConverter.ToPostgres(new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.FromHours(7)),
                               PostgresDateType.TimestampTz);
// → "2024-01-02 03:04:05+07:00"
```

Quy tắc chính:
- Chuỗi kết quả cho SQL Server dùng định dạng ISO 8601 (`yyyy-MM-ddTHH:mm:ss`). Định dạng này không phụ thuộc vào cài đặt `DATEFORMAT` hay `LANGUAGE`.
- Nếu giá trị có múi giờ mà kiểu đích không lưu múi giờ (`datetime`, `datetime2`, `smalldatetime`, `timestamp`), giá trị được đổi sang UTC.
- Nếu kiểu đích cần múi giờ mà giá trị không có, thư viện dùng `DbDateTimeOptions.DefaultOffset` (mặc định `+00:00`).
- Với giá trị có kiểu, dùng `ToSqlServer(...)` / `ToPostgres(...)` (kết quả chỉ phụ thuộc kiểu đích). `Kind = Utc` được coi là `+00:00`, `Kind = Local` dùng múi giờ của máy, `Unspecified` là không có múi giờ.
- Với đích `datetimeoffset`, độ lệch mà SQL Server không nhận (có phần giây như `+07:06:40` của ngày trước 1906, hoặc vượt ±14:00) được đổi sang UTC và ghi `+00:00`. Sang PostgreSQL thì độ lệch có giây được giữ nguyên.
- PostgreSQL cho phép `24:00:00` cho giá trị chỉ có giờ; sang SQL Server `time`, nó thành `23:59:59.9999999`. Với giá trị có ngày (vd. `2024-01-02 24:00:00`), ngày chuyển sang hôm sau và phần giờ là `00:00:00`, giống `timestamp::time` của PostgreSQL.
- Giá trị ngoài khoảng của kiểu đích sẽ gây `ArgumentOutOfRangeException`. Ví dụ: năm 1700 với `datetime`, hoặc năm 2080 với `smalldatetime`.
- Ngày trước Công nguyên (`BC`) và năm sau 9999 cũng gây `ArgumentOutOfRangeException`, trừ khi dùng `InfinityHandling.ClampToRange` (giá trị kẹp được coi là UTC, ghi `+00:00`). Sang kiểu `time`, phần giờ của chúng vẫn được giữ.
- `interval` không có kiểu tương đương trong SQL Server nên sẽ gây `NotSupportedException`.

## Đọc chữ thành số

`WordsToNumber.FromVietnamese` là chiều ngược của `NumberToWords`: đọc lại được mọi chuỗi mà `ToVietnamese` / `ToVietnameseCurrency` sinh ra, cùng các cách viết phổ biến.

- Không phân biệt hoa thường; chấp nhận `ngàn`/`nghìn`, `tỉ`/`tỷ`, `lẻ`/`linh`, `bẩy`/`bảy`; sau `mươi` nhận `mốt`/`một`, `tư`/`bốn`, `lăm`/`nhăm`/`năm`.
- Tiền tố `âm` hoặc `trừ`; hậu tố `đồng` hoặc `đồng chẵn`; dấu phẩy, chấm, gạch ngang được coi như khoảng trắng.
- Nhóm không đọc đủ vẫn hiểu theo giá trị: `"hai nghìn hai mươi"` → `2020`.
- Cách nói tắt mơ hồ bị từ chối: `"một trăm năm"` phải viết `"một trăm linh năm"` (105) hoặc `"một trăm năm mươi"` (150).
- Chuỗi không hợp lệ gây `FormatException`, vượt khoảng `long` gây `OverflowException`. Dùng `TryFromVietnamese` nếu không muốn bắt lỗi.

## Kiểm tra email

`Validators.IsEmail` chỉ kiểm tra rất cơ bản. `EmailValidator` kiểm tra kỹ hơn và cho biết lý do:

```csharp
var r = EmailValidator.Validate("  An.Nguyen@Thư.VN ");
r.IsValid;          // true
r.NormalizedEmail;  // "An.Nguyen@thư.vn"       (tên miền viết thường)
r.AsciiEmail;       // "An.Nguyen@xn--th-e0a.vn" (dạng gửi mail được)

EmailValidator.Validate("an..b@gmial.com").Message;  // "Phần tên trước @ có ký tự không hợp lệ hoặc dấu chấm sai vị trí."
EmailValidator.SuggestCorrection("an@gmial.com");    // "an@gmail.com"

// Tra DNS xem tên miền có máy chủ nhận mail không (cần mạng)
var m = await EmailValidator.ValidateWithMailServerAsync("an@gmail.com");
m.IsValid;      // true
m.MailServers;  // [5 gmail-smtp-in.l.google.com, 10 alt1.gmail-smtp-in.l.google.com, ...]
```

Quy tắc chính:
- Cú pháp theo RFC 5321/5322: tối đa 254 ký tự; phần tên tối đa 64 ký tự gồm chữ, số, `` !#$%&'*+-/=?^_`{|}~ `` và dấu chấm (không ở đầu/cuối, không liền nhau); tên miền gồm các nhãn 1–63 ký tự chữ/số/gạch ngang và phải có đuôi (vd. `.vn`).
- Tên miền tiếng Việt (IDN) như `thư.vn` được chấp nhận và đổi sang Punycode trong `AsciiEmail`.
- Không hỗ trợ phần tên trong ngoặc kép, phần tên có dấu tiếng Việt (SMTPUTF8), hay địa chỉ IP thay tên miền.
- `Suggestion` gợi ý khi tên miền gần giống nhà cung cấp phổ biến (Gmail, Yahoo, Outlook, iCloud, ...). Đây chỉ là gợi ý, không ảnh hưởng `IsValid`.
- `ValidateWithMailServerAsync` gửi truy vấn DNS trực tiếp tới máy chủ DNS của hệ thống (UDP, tự chuyển TCP khi cần), không cần thư viện ngoài:
  - Có bản ghi MX → hợp lệ. Không có MX nhưng có A/AAAA → hợp lệ (RFC 5321 §5.1).
  - Null MX (RFC 7505) → `NullMx`; tên miền không tồn tại → `DomainNotFound`; không có bản ghi nào → `NoMailServer`.
  - DNS không trả lời hoặc lỗi → `DnsLookupFailed`. Đây là lỗi tạm thời, nên cho thử lại thay vì từ chối người dùng.
- Có máy chủ mail không có nghĩa là hộp thư tồn tại; muốn chắc chắn vẫn cần gửi mail xác nhận.
- Dùng máy chủ DNS riêng hoặc đổi thời gian chờ: `new DnsMxResolver([new IPEndPoint(IPAddress.Parse("1.1.1.1"), 53)], TimeSpan.FromSeconds(2))`. Cài `IMailServerResolver` để tự tra theo cách khác (vd. trong test).

## Sử dụng

```csharp
using ThuVien;

Console.WriteLine(VietnameseText.ToSlug("Đà Lạt mộng mơ"));      // da-lat-mong-mo
Console.WriteLine(NumberToWords.ToVietnamese(2_500_000));        // hai triệu năm trăm nghìn
Console.WriteLine(WordsToNumber.FromVietnamese("Hai triệu năm trăm ngàn đồng")); // 2500000
Console.WriteLine(CurrencyFormatter.ToVnd(1234567));             // 1.234.567 ₫
```

## Lệnh

```bash
dotnet build          # build
dotnet test           # chạy unit test
THUVIEN_NETWORK_TESTS=1 dotnet test   # chạy thêm test tra DNS thật (cần mạng)
dotnet pack src/ThuVien -c Release -o artifacts   # tạo gói NuGet
```
