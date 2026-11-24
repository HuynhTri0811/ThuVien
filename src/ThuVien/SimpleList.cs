using System.Collections;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace ThuVien;

/// <summary>
/// Danh sách có kiểu, tự tăng kích thước khi thêm phần tử, tương tự <see cref="List{T}"/>.
/// Bên trong là một mảng; khi mảng đầy, danh sách cấp mảng mới lớn gấp đôi và chép dữ liệu sang.
/// </summary>
/// <remarks>
/// Không an toàn khi nhiều luồng cùng ghi. Thêm hoặc xóa phần tử trong lúc duyệt bằng
/// <c>foreach</c> sẽ gây <see cref="InvalidOperationException"/>.
/// </remarks>
/// <typeparam name="T">Kiểu phần tử.</typeparam>
[DebuggerDisplay("Count = {Count}")]
public sealed class SimpleList<T> : IList<T>, IReadOnlyList<T>
{
    private const int DefaultCapacity = 4;

    private T[] _items;
    private int _size;
    private int _version;

    /// <summary>Tạo danh sách rỗng.</summary>
    public SimpleList()
    {
        _items = [];
    }

    /// <summary>Tạo danh sách rỗng với sức chứa ban đầu cho trước.</summary>
    /// <param name="capacity">Số phần tử chứa được trước khi phải cấp lại mảng.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> âm.</exception>
    public SimpleList(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);
        _items = capacity == 0 ? [] : new T[capacity];
    }

    /// <summary>Tạo danh sách chứa bản sao các phần tử của <paramref name="items"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="items"/> là <c>null</c>.</exception>
    public SimpleList(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        _items = [];
        AddRange(items);
    }

    /// <summary>Số phần tử đang có.</summary>
    public int Count => _size;

    /// <summary>
    /// Kích thước mảng bên trong, luôn lớn hơn hoặc bằng <see cref="Count"/>.
    /// Đặt giá trị mới sẽ cấp lại mảng với đúng kích thước đó.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Giá trị nhỏ hơn <see cref="Count"/>.</exception>
    public int Capacity
    {
        get => _items.Length;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, _size);
            if (value == _items.Length)
            {
                return;
            }

            var next = value == 0 ? [] : new T[value];
            Array.Copy(_items, next, _size);
            _items = next;
        }
    }

    /// <summary>Đọc hoặc ghi phần tử tại vị trí <paramref name="index"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="index"/> âm hoặc không nhỏ hơn <see cref="Count"/>.
    /// </exception>
    public T this[int index]
    {
        get
        {
            CheckIndex(index);
            return _items[index];
        }
        set
        {
            CheckIndex(index);
            _items[index] = value;
            _version++;
        }
    }

    bool ICollection<T>.IsReadOnly => false;

    /// <summary>Thêm phần tử vào cuối danh sách.</summary>
    public void Add(T item)
    {
        if (_size == _items.Length)
        {
            Grow(_size + 1);
        }

        _items[_size++] = item;
        _version++;
    }

    /// <summary>Thêm mọi phần tử của <paramref name="items"/> vào cuối danh sách.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="items"/> là <c>null</c>.</exception>
    public void AddRange(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (items is ICollection<T> collection)
        {
            var count = collection.Count;
            if (count == 0)
            {
                return;
            }

            EnsureCapacity(_size + count);
            // Nếu items chính là danh sách này, CopyTo chép phần đầu mảng sang phần sau — vẫn đúng.
            collection.CopyTo(_items, _size);
            _size += count;
            _version++;
            return;
        }

        foreach (var item in items)
        {
            Add(item);
        }
    }

    /// <summary>Chèn phần tử vào vị trí <paramref name="index"/>, dồn các phần tử sau ra sau một chỗ.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="index"/> âm hoặc lớn hơn <see cref="Count"/>.
    /// </exception>
    public void Insert(int index, T item)
    {
        if ((uint)index > (uint)_size)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, "Vị trí phải nằm trong khoảng 0 đến Count.");
        }

        if (_size == _items.Length)
        {
            Grow(_size + 1);
        }

        if (index < _size)
        {
            Array.Copy(_items, index, _items, index + 1, _size - index);
        }

        _items[index] = item;
        _size++;
        _version++;
    }

    /// <summary>Xóa phần tử đầu tiên bằng <paramref name="item"/>.</summary>
    /// <returns><c>true</c> nếu tìm thấy và đã xóa.</returns>
    public bool Remove(T item)
    {
        var index = IndexOf(item);
        if (index < 0)
        {
            return false;
        }

        RemoveAt(index);
        return true;
    }

    /// <summary>Xóa phần tử tại vị trí <paramref name="index"/>, dồn các phần tử sau lên trước một chỗ.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="index"/> âm hoặc không nhỏ hơn <see cref="Count"/>.
    /// </exception>
    public void RemoveAt(int index)
    {
        CheckIndex(index);

        _size--;
        if (index < _size)
        {
            Array.Copy(_items, index + 1, _items, index, _size - index);
        }

        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            // Bỏ tham chiếu ở ô cuối để bộ thu gom rác giải phóng được đối tượng.
            _items[_size] = default!;
        }

        _version++;
    }

    /// <summary>Xóa mọi phần tử thỏa <paramref name="match"/>.</summary>
    /// <returns>Số phần tử đã xóa.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="match"/> là <c>null</c>.</exception>
    public int RemoveAll(Predicate<T> match)
    {
        ArgumentNullException.ThrowIfNull(match);

        var write = 0;
        for (var read = 0; read < _size; read++)
        {
            if (!match(_items[read]))
            {
                _items[write++] = _items[read];
            }
        }

        var removed = _size - write;
        if (removed == 0)
        {
            return 0;
        }

        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            Array.Clear(_items, write, removed);
        }

        _size = write;
        _version++;
        return removed;
    }

    /// <summary>Xóa hết phần tử. <see cref="Capacity"/> giữ nguyên.</summary>
    public void Clear()
    {
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            Array.Clear(_items, 0, _size);
        }

        _size = 0;
        _version++;
    }

    /// <summary>Kiểm tra danh sách có chứa <paramref name="item"/> không.</summary>
    public bool Contains(T item) => IndexOf(item) >= 0;

    /// <summary>Vị trí đầu tiên của <paramref name="item"/>, hoặc -1 nếu không có.</summary>
    /// <remarks>So sánh bằng <see cref="EqualityComparer{T}.Default"/>.</remarks>
    public int IndexOf(T item) => Array.IndexOf(_items, item, 0, _size);

    /// <summary>Vị trí cuối cùng của <paramref name="item"/>, hoặc -1 nếu không có.</summary>
    public int LastIndexOf(T item) => _size == 0 ? -1 : Array.LastIndexOf(_items, item, _size - 1, _size);

    /// <summary>Vị trí đầu tiên thỏa <paramref name="match"/>, hoặc -1 nếu không có.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="match"/> là <c>null</c>.</exception>
    public int FindIndex(Predicate<T> match)
    {
        ArgumentNullException.ThrowIfNull(match);

        for (var i = 0; i < _size; i++)
        {
            if (match(_items[i]))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Tìm phần tử đầu tiên thỏa <paramref name="match"/>.
    /// </summary>
    /// <param name="match">Điều kiện cần thỏa.</param>
    /// <param name="value">Phần tử tìm được, hoặc <c>default</c> nếu không có.</param>
    /// <returns><c>true</c> nếu tìm thấy.</returns>
    /// <remarks>
    /// Khác <see cref="List{T}.Find"/>, hàm này cho biết có tìm thấy hay không, nên phân biệt được
    /// "không có" với "tìm thấy giá trị mặc định" (ví dụ số 0).
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="match"/> là <c>null</c>.</exception>
    public bool TryFind(Predicate<T> match, out T value)
    {
        var index = FindIndex(match);
        value = index >= 0 ? _items[index] : default!;
        return index >= 0;
    }

    /// <summary>Kiểm tra có phần tử nào thỏa <paramref name="match"/> không.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="match"/> là <c>null</c>.</exception>
    public bool Exists(Predicate<T> match) => FindIndex(match) >= 0;

    /// <summary>Sắp xếp tăng dần theo <see cref="Comparer{T}.Default"/>. Không ổn định.</summary>
    public void Sort() => Sort(comparer: null);

    /// <summary>Sắp xếp theo <paramref name="comparer"/> (<c>null</c> dùng so sánh mặc định). Không ổn định.</summary>
    public void Sort(IComparer<T>? comparer)
    {
        Array.Sort(_items, 0, _size, comparer);
        _version++;
    }

    /// <summary>Sắp xếp theo hàm so sánh <paramref name="comparison"/>. Không ổn định.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="comparison"/> là <c>null</c>.</exception>
    public void Sort(Comparison<T> comparison)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        _items.AsSpan(0, _size).Sort(comparison);
        _version++;
    }

    /// <summary>Đảo ngược thứ tự phần tử.</summary>
    public void Reverse()
    {
        Array.Reverse(_items, 0, _size);
        _version++;
    }

    /// <summary>Chép các phần tử ra mảng mới.</summary>
    public T[] ToArray() => _items.AsSpan(0, _size).ToArray();

    /// <summary>Chép các phần tử vào <paramref name="array"/>, bắt đầu từ vị trí <paramref name="arrayIndex"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="array"/> là <c>null</c>.</exception>
    /// <exception cref="ArgumentException">Mảng đích không đủ chỗ.</exception>
    public void CopyTo(T[] array, int arrayIndex)
    {
        ArgumentNullException.ThrowIfNull(array);
        Array.Copy(_items, 0, array, arrayIndex, _size);
    }

    /// <summary>
    /// Bảo đảm <see cref="Capacity"/> ít nhất bằng <paramref name="capacity"/>.
    /// Gọi trước khi thêm nhiều phần tử để tránh cấp lại mảng nhiều lần.
    /// </summary>
    /// <returns><see cref="Capacity"/> sau khi gọi.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> âm.</exception>
    public int EnsureCapacity(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);
        if (_items.Length < capacity)
        {
            Grow(capacity);
        }

        return _items.Length;
    }

    /// <summary>
    /// Thu nhỏ mảng bên trong về đúng <see cref="Count"/> nếu phần thừa lớn hơn 10%,
    /// để trả lại bộ nhớ sau khi đã xóa nhiều phần tử.
    /// </summary>
    public void TrimExcess()
    {
        if (_size < (int)(_items.Length * 0.9))
        {
            Capacity = _size;
        }
    }

    /// <summary>
    /// Trả về lớp bọc chỉ đọc. Bên gọi không sửa được danh sách qua lớp bọc này,
    /// kể cả khi ép kiểu; thay đổi trên danh sách gốc vẫn thấy được qua nó.
    /// </summary>
    public ReadOnlyCollection<T> AsReadOnly() => new(this);

    /// <summary>Thực hiện <paramref name="action"/> trên từng phần tử theo thứ tự.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> là <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException">Danh sách bị sửa trong lúc chạy.</exception>
    public void ForEach(Action<T> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        var version = _version;
        for (var i = 0; i < _size; i++)
        {
            action(_items[i]);
            if (version != _version)
            {
                throw ModifiedDuringEnumeration();
            }
        }
    }

    /// <summary>Trả về bộ duyệt dùng cho <c>foreach</c>. Là struct nên không cấp phát bộ nhớ.</summary>
    public Enumerator GetEnumerator() => new(this);

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private void CheckIndex(int index)
    {
        if ((uint)index >= (uint)_size)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, "Vị trí phải nằm trong khoảng 0 đến Count - 1.");
        }
    }

    private void Grow(int minCapacity)
    {
        var next = _items.Length == 0 ? DefaultCapacity : _items.Length * 2;

        // Nhân đôi có thể vượt giới hạn mảng (hoặc tràn số âm); kẹp lại.
        if ((uint)next > (uint)Array.MaxLength)
        {
            next = Array.MaxLength;
        }

        if (next < minCapacity)
        {
            next = minCapacity;
        }

        Capacity = next;
    }

    private static InvalidOperationException ModifiedDuringEnumeration() =>
        new("Danh sách đã bị sửa trong lúc đang duyệt.");

    /// <summary>Bộ duyệt các phần tử của <see cref="SimpleList{T}"/>.</summary>
    public struct Enumerator : IEnumerator<T>
    {
        private readonly SimpleList<T> _list;
        private readonly int _version;
        private int _index;
        private T _current;

        internal Enumerator(SimpleList<T> list)
        {
            _list = list;
            _version = list._version;
            _index = 0;
            _current = default!;
        }

        /// <summary>Phần tử hiện tại.</summary>
        public readonly T Current => _current;

        readonly object? IEnumerator.Current => _current;

        /// <summary>Chuyển sang phần tử tiếp theo.</summary>
        /// <returns><c>false</c> khi đã duyệt hết.</returns>
        /// <exception cref="InvalidOperationException">Danh sách bị sửa sau khi bắt đầu duyệt.</exception>
        public bool MoveNext()
        {
            if (_version != _list._version)
            {
                throw ModifiedDuringEnumeration();
            }

            if (_index < _list._size)
            {
                _current = _list._items[_index++];
                return true;
            }

            _current = default!;
            return false;
        }

        /// <summary>Không cần giải phóng gì.</summary>
        public readonly void Dispose()
        {
        }

        void IEnumerator.Reset()
        {
            if (_version != _list._version)
            {
                throw ModifiedDuringEnumeration();
            }

            _index = 0;
            _current = default!;
        }
    }
}
