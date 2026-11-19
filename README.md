# ThuVien

Bộ tiện ích tiếng Việt cho .NET 10.

## Tính năng

| Lớp | Hàm | Ví dụ |
|---|---|---|
| `VietnameseText` | `RemoveDiacritics` | `"Tiếng Việt"` → `"Tieng Viet"` |
| | `ToSlug` | `"Xin chào, Việt Nam!"` → `"xin-chao-viet-nam"` |
| `NumberToWords` | `ToVietnamese` | `1005` → `"một nghìn không trăm linh năm"` |
| | `ToVietnameseCurrency` | `125000` → `"Một trăm hai mươi lăm nghìn đồng"` |
| `Validators` | `IsVietnamesePhone` | `"+84 912 345 678"` → `true` |
| | `NormalizeVietnamesePhone` | `"+84 912 345 678"` → `"0912345678"` |
| | `IsEmail` | `"a@b.vn"` → `true` |
| `CurrencyFormatter` | `ToVnd` | `1234567m` → `"1.234.567 ₫"` |
| `DbDateTimeConverter` | `PostgresTypeToSqlServer` | `"timestamptz(3)"` → `"datetimeoffset(3)"` |
| | `SqlServerTypeToPostgres` | `"datetime"` → `"timestamp(3)"` |
| | `PostgresToSqlServer` | `"2024-01-02 03:04:05+07"` → `"2024-01-01T20:04:05"` |
| | `SqlServerToPostgres` | `"2024-01-02 03:04:05.1234567"` → `"2024-01-02 03:04:05.123457"` |

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
DbDateTimeConverter.PostgresToSqlServer(fromNpgsql, SqlServerDateType.DateTime2);
DbDateTimeConverter.SqlServerToPostgres(new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.FromHours(7)),
                                        PostgresDateType.TimestampTz);
// → "2024-01-02 03:04:05+07:00"
```

Quy tắc chính:
- Chuỗi kết quả cho SQL Server dùng định dạng ISO 8601 (`yyyy-MM-ddTHH:mm:ss`). Định dạng này không phụ thuộc vào cài đặt `DATEFORMAT` hay `LANGUAGE`.
- Nếu giá trị có múi giờ mà kiểu đích không lưu múi giờ (`datetime`, `datetime2`, `smalldatetime`, `timestamp`), giá trị được đổi sang UTC.
- Nếu kiểu đích cần múi giờ mà giá trị không có, thư viện dùng `DbDateTimeOptions.DefaultOffset` (mặc định `+00:00`).
- Với overload nhận `DateTime`: `Kind = Utc` được coi là `+00:00`, `Kind = Local` dùng múi giờ của máy, `Unspecified` là không có múi giờ.
- Với đích `datetimeoffset`, độ lệch mà SQL Server không nhận (có phần giây như `+07:06:40` của ngày trước 1906, hoặc vượt ±14:00) được đổi sang UTC và ghi `+00:00`. Sang PostgreSQL thì độ lệch có giây được giữ nguyên.
- PostgreSQL cho phép `24:00:00`. Sang SQL Server `time`, giá trị này thành `23:59:59.9999999`. Sang kiểu có ngày, nó thành 00:00 của ngày hôm sau.
- Giá trị ngoài khoảng của kiểu đích sẽ gây `ArgumentOutOfRangeException`. Ví dụ: năm 1700 với `datetime`, hoặc năm 2080 với `smalldatetime`.
- Ngày trước Công nguyên (`BC`) và năm sau 9999 cũng gây `ArgumentOutOfRangeException`, trừ khi dùng `InfinityHandling.ClampToRange`.
- `interval` không có kiểu tương đương trong SQL Server nên sẽ gây `NotSupportedException`.

## Sử dụng

```csharp
using ThuVien;

Console.WriteLine(VietnameseText.ToSlug("Đà Lạt mộng mơ"));      // da-lat-mong-mo
Console.WriteLine(NumberToWords.ToVietnamese(2_500_000));        // hai triệu năm trăm nghìn
Console.WriteLine(CurrencyFormatter.ToVnd(1234567));             // 1.234.567 ₫
```

## Lệnh

```bash
dotnet build          # build
dotnet test           # chạy unit test
dotnet pack src/ThuVien -c Release -o artifacts   # tạo gói NuGet
```
