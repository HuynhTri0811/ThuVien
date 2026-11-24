# `List<T>` trong C#

`List<T>` (namespace `System.Collections.Generic`) là danh sách có kiểu, tự tăng kích thước khi thêm phần tử. Bên trong nó là một mảng `T[]`: khi mảng đầy, nó cấp mảng mới lớn gấp đôi và chép dữ liệu sang.

## Tạo danh sách

```csharp
var rong = new List<int>();                 // danh sách rỗng
var coSan = new List<int> { 3, 1, 2 };      // khởi tạo kèm phần tử
var datTruoc = new List<string>(1000);      // đặt trước sức chứa 1000
var tuMang = new List<int>(new[] { 1, 2 }); // chép từ một IEnumerable<T>

// C# 12 trở lên: collection expression
List<string> ten = ["An", "Bình"];
```

## Thao tác cơ bản

```csharp
var so = new List<int> { 3, 1, 2 };

so.Add(4);                  // [3, 1, 2, 4]
so.AddRange([5, 6]);        // [3, 1, 2, 4, 5, 6]
so.Insert(0, 9);            // [9, 3, 1, 2, 4, 5, 6]

so.Remove(1);               // xóa phần tử đầu tiên có giá trị 1 → [9, 3, 2, 4, 5, 6]
so.RemoveAt(0);             // xóa theo vị trí → [3, 2, 4, 5, 6]
so.RemoveAll(x => x > 4);   // xóa mọi phần tử thỏa điều kiện → [3, 2, 4]

int x = so[1];              // 2
so[1] = 7;                  // [3, 7, 4]
int n = so.Count;           // 3

so.Clear();                 // []
```

## Tìm kiếm

```csharp
var so = new List<int> { 5, 8, 2, 8 };

so.Contains(8);                     // true
so.IndexOf(8);                      // 1  (vị trí đầu tiên, -1 nếu không có)
so.LastIndexOf(8);                  // 3
so.Find(x => x > 5);                // 8  (default(T) nếu không có)
so.FindIndex(x => x > 5);           // 1
so.FindAll(x => x > 5);             // [8, 8]  (trả về List<T> mới)
so.Exists(x => x < 0);              // false
so.TrueForAll(x => x > 0);          // true
```

`Find` trả về `default(T)` khi không tìm thấy. Với `List<int>`, giá trị đó là `0`, nên không phân biệt được "không có" với "tìm thấy số 0". Trường hợp này nên dùng `FindIndex`.

## Sắp xếp và đảo

```csharp
var ten = new List<string> { "Bình", "an", "Chi" };

ten.Sort();                                         // theo culture hiện tại
ten.Sort(StringComparer.Ordinal);                   // theo mã ký tự
ten.Sort((a, b) => a.Length.CompareTo(b.Length));   // theo độ dài
ten.Reverse();

var so = new List<int> { 1, 3, 5, 7 };
so.BinarySearch(5);                                 // 2 — chỉ đúng khi danh sách đã sắp xếp
```

`Sort` sắp xếp ngay trên danh sách và **không ổn định**: các phần tử bằng nhau có thể bị đổi chỗ. Cần giữ thứ tự ban đầu thì dùng LINQ `OrderBy`, vì nó sắp xếp ổn định và trả về chuỗi mới.

## Duyệt danh sách

```csharp
foreach (var item in so) { /* ... */ }

for (var i = 0; i < so.Count; i++) { /* so[i] */ }

so.ForEach(item => Console.WriteLine(item));
```

Thêm hoặc xóa phần tử trong lúc `foreach` sẽ gây `InvalidOperationException`:

```csharp
// SAI
foreach (var item in so)
    if (item < 0) so.Remove(item);

// ĐÚNG: dùng RemoveAll
so.RemoveAll(item => item < 0);

// ĐÚNG: chạy ngược bằng for
for (var i = so.Count - 1; i >= 0; i--)
    if (so[i] < 0) so.RemoveAt(i);
```

## `Count` và `Capacity`

- `Count` là số phần tử đang có.
- `Capacity` là kích thước mảng bên trong, luôn lớn hơn hoặc bằng `Count`.

Mỗi lần mảng đầy, `List<T>` phải cấp mảng mới và chép toàn bộ dữ liệu sang. Nếu biết trước số lượng, đặt sẵn sức chứa để tránh việc này:

```csharp
var ketQua = new List<string>(nguon.Count);
foreach (var s in nguon)
    ketQua.Add(s.Trim());
```

Muốn trả bớt bộ nhớ thừa sau khi đã xóa nhiều phần tử thì gọi `TrimExcess()`.

## Độ phức tạp

| Thao tác | Độ phức tạp |
|---|---|
| `list[i]` (đọc/ghi) | O(1) |
| `Add` | O(1) trung bình (O(n) khi phải cấp lại mảng) |
| `Insert`, `RemoveAt` ở đầu hoặc giữa | O(n) |
| `RemoveAt` ở cuối | O(1) |
| `Contains`, `IndexOf`, `Remove(value)` | O(n) |
| `Sort` | O(n log n) |
| `BinarySearch` | O(log n) |

## Khi nào dùng kiểu khác

| Nhu cầu | Nên dùng |
|---|---|
| Kiểm tra "có chứa không" nhanh, không trùng lặp | `HashSet<T>` |
| Tra theo khóa | `Dictionary<TKey, TValue>` |
| Lấy ra theo thứ tự vào trước ra trước | `Queue<T>` |
| Lấy ra theo thứ tự vào sau ra trước | `Stack<T>` |
| Thêm/xóa nhiều ở hai đầu hoặc giữa | `LinkedList<T>` |
| Số phần tử cố định | mảng `T[]` |
| Nhiều luồng cùng ghi | `lock`, `ConcurrentBag<T>`, `ConcurrentQueue<T>` |
| Dữ liệu không được sửa sau khi tạo | `ImmutableList<T>` (`System.Collections.Immutable`) |

## Dùng `List<T>` trong API

Thường nên trả `IReadOnlyList<T>` hoặc `IEnumerable<T>` thay vì `List<T>`, để bên gọi không sửa được dữ liệu bên trong:

```csharp
public class GioHang
{
    private readonly List<string> _sanPham = [];

    public IReadOnlyList<string> SanPham => _sanPham;

    public void Them(string sp) => _sanPham.Add(sp);
}
```

Lưu ý: `IReadOnlyList<T>` chỉ ngăn sửa qua interface đó. Bên gọi vẫn có thể ép kiểu ngược về `List<T>`. Cần chặn hẳn thì trả `_sanPham.AsReadOnly()`.

## Đa luồng

`List<T>` **không an toàn** khi nhiều luồng cùng ghi, hoặc một luồng ghi trong khi luồng khác đọc. Hậu quả có thể là mất phần tử, ngoại lệ, hoặc dữ liệu hỏng mà không báo lỗi. Nếu chỉ đọc đồng thời, không có luồng nào ghi, thì không sao.

```csharp
private readonly List<int> _ds = [];
private readonly Lock _khoa = new();   // .NET 9 trở lên; bản cũ dùng object

public void Them(int x)
{
    lock (_khoa)
    {
        _ds.Add(x);
    }
}
```
