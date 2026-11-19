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
