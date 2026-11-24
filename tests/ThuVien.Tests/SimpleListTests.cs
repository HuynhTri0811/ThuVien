namespace ThuVien.Tests;

public class SimpleListTests
{
    [Fact]
    public void New_IsEmpty()
    {
        var list = new SimpleList<int>();
        Assert.Empty(list);
        Assert.Equal(0, list.Capacity);
    }

    [Fact]
    public void Add_GrowsByDoubling()
    {
        var list = new SimpleList<int>();
        list.Add(1);
        Assert.Equal(4, list.Capacity);

        for (var i = 2; i <= 5; i++)
        {
            list.Add(i);
        }

        Assert.Equal(8, list.Capacity);
        Assert.Equal([1, 2, 3, 4, 5], list.ToArray());
    }

    [Fact]
    public void Constructor_FromEnumerable_CopiesItems()
    {
        var source = new[] { 1, 2, 3 };
        var list = new SimpleList<int>(source);
        source[0] = 99;
        Assert.Equal([1, 2, 3], list.ToArray());

        Assert.Equal([0, 1, 2], new SimpleList<int>(Enumerable.Range(0, 3)).ToArray());
    }

    [Fact]
    public void Constructor_NegativeCapacity_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SimpleList<int>(-1));
    }

    [Fact]
    public void Indexer_ReadWrite()
    {
        var list = new SimpleList<string>(["a", "b"]);
        list[1] = "c";
        Assert.Equal("c", list[1]);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void Indexer_OutOfRange_Throws(int index)
    {
        // Capacity lớn hơn Count: ô thừa trong mảng vẫn không được đọc.
        var list = new SimpleList<int>(10) { 1, 2 };
        Assert.Throws<ArgumentOutOfRangeException>(() => list[index]);
        Assert.Throws<ArgumentOutOfRangeException>(() => list[index] = 0);
    }

    [Fact]
    public void AddRange_AppendsCollectionAndSequence()
    {
        var list = new SimpleList<int> { 1 };
        list.AddRange([2, 3]);
        list.AddRange(Enumerable.Range(4, 2));
        Assert.Equal([1, 2, 3, 4, 5], list.ToArray());
    }

    [Fact]
    public void AddRange_Self_DuplicatesItems()
    {
        var list = new SimpleList<int> { 1, 2, 3, 4 };
        list.AddRange(list);
        Assert.Equal([1, 2, 3, 4, 1, 2, 3, 4], list.ToArray());
    }

    [Fact]
    public void Insert_AtStartMiddleEnd()
    {
        var list = new SimpleList<int> { 2, 4 };
        list.Insert(0, 1);
        list.Insert(2, 3);
        list.Insert(4, 5);
        Assert.Equal([1, 2, 3, 4, 5], list.ToArray());
    }

    [Fact]
    public void Insert_OutOfRange_Throws()
    {
        var list = new SimpleList<int> { 1 };
        Assert.Throws<ArgumentOutOfRangeException>(() => list.Insert(2, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => list.Insert(-1, 0));
    }

    [Fact]
    public void Remove_RemovesFirstMatchOnly()
    {
        var list = new SimpleList<int> { 1, 2, 1 };
        Assert.True(list.Remove(1));
        Assert.Equal([2, 1], list.ToArray());
        Assert.False(list.Remove(9));
    }

    [Fact]
    public void RemoveAt_ShiftsItems()
    {
        var list = new SimpleList<int> { 1, 2, 3 };
        list.RemoveAt(0);
        Assert.Equal([2, 3], list.ToArray());
        Assert.Throws<ArgumentOutOfRangeException>(() => list.RemoveAt(2));
    }

    [Fact]
    public void RemoveAll_ReturnsCountAndKeepsOrder()
    {
        var list = new SimpleList<int>(Enumerable.Range(1, 10));
        Assert.Equal(5, list.RemoveAll(x => x % 2 == 0));
        Assert.Equal([1, 3, 5, 7, 9], list.ToArray());
        Assert.Equal(0, list.RemoveAll(x => x > 100));
    }

    [Fact]
    public void Clear_KeepsCapacity()
    {
        var list = new SimpleList<string>(["a", "b", "c"]);
        var capacity = list.Capacity;
        list.Clear();
        Assert.Empty(list);
        Assert.Equal(capacity, list.Capacity);
    }

    [Fact]
    public void Removing_ReleasesReferences()
    {
        var list = new SimpleList<object>();
        var weak = AddAndRemove(list);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        Assert.False(weak.IsAlive);

        // Không inline để đối tượng không còn sống trong biến cục bộ của test.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static WeakReference AddAndRemove(SimpleList<object> l)
        {
            var o = new object();
            l.Add(o);
            l.RemoveAt(0);
            return new WeakReference(o);
        }
    }

    [Fact]
    public void Search()
    {
        var list = new SimpleList<int> { 5, 8, 2, 8 };
        // Gọi thẳng SimpleList.Contains (Assert.Contains sẽ duyệt bằng enumerator thay vì gọi hàm này).
        bool has8 = list.Contains(8), has3 = list.Contains(3);
        Assert.True(has8);
        Assert.False(has3);
        Assert.Equal(1, list.IndexOf(8));
        Assert.Equal(3, list.LastIndexOf(8));
        Assert.Equal(-1, list.IndexOf(3));
        Assert.Equal(1, list.FindIndex(x => x > 5));
        Assert.True(list.Exists(x => x < 3));
        Assert.Equal(-1, new SimpleList<int>().LastIndexOf(1));
    }

    [Fact]
    public void IndexOf_IgnoresSlotsBeyondCount()
    {
        var list = new SimpleList<int>(10) { 1 };
        // Các ô thừa trong mảng có giá trị 0 nhưng không thuộc danh sách.
        Assert.Equal(-1, list.IndexOf(0));
    }

    [Fact]
    public void TryFind_DistinguishesMissingFromDefault()
    {
        var list = new SimpleList<int> { 0, 1 };
        Assert.True(list.TryFind(x => x == 0, out var zero));
        Assert.Equal(0, zero);
        Assert.False(list.TryFind(x => x == 5, out _));
    }

    [Fact]
    public void Sort_Variants()
    {
        var list = new SimpleList<int> { 3, 1, 2 };
        list.Sort();
        Assert.Equal([1, 2, 3], list.ToArray());

        list.Sort((a, b) => b.CompareTo(a));
        Assert.Equal([3, 2, 1], list.ToArray());

        list.Sort(Comparer<int>.Default);
        Assert.Equal([1, 2, 3], list.ToArray());
    }

    [Fact]
    public void Sort_OnlyTouchesItems()
    {
        // Ô thừa chứa 0; nếu Sort đụng tới thì 0 sẽ lọt vào đầu danh sách.
        var list = new SimpleList<int>(10) { 3, 1, 2 };
        list.Sort();
        Assert.Equal([1, 2, 3], list.ToArray());
    }

    [Fact]
    public void Reverse_ReversesItems()
    {
        var list = new SimpleList<int>(10) { 1, 2, 3 };
        list.Reverse();
        Assert.Equal([3, 2, 1], list.ToArray());
    }

    [Fact]
    public void Capacity_CannotBeLessThanCount()
    {
        var list = new SimpleList<int> { 1, 2, 3 };
        Assert.Throws<ArgumentOutOfRangeException>(() => list.Capacity = 2);
        list.Capacity = 3;
        Assert.Equal([1, 2, 3], list.ToArray());
    }

    [Fact]
    public void EnsureCapacity_AndTrimExcess()
    {
        var list = new SimpleList<int>();
        Assert.Equal(100, list.EnsureCapacity(100));
        list.Add(1);
        list.TrimExcess();
        Assert.Equal(1, list.Capacity);
        Assert.Equal([1], list.ToArray());
    }

    [Fact]
    public void Foreach_VisitsInOrder()
    {
        var list = new SimpleList<int> { 1, 2, 3 };
        var seen = new List<int>();
        foreach (var x in list)
        {
            seen.Add(x);
        }

        Assert.Equal([1, 2, 3], seen);
        Assert.Equal(6, list.Sum());
    }

    [Fact]
    public void Foreach_ModifyDuringEnumeration_Throws()
    {
        var list = new SimpleList<int> { 1, 2, 3 };
        Assert.Throws<InvalidOperationException>(() =>
        {
            foreach (var x in list)
            {
                list.Add(x);
            }
        });
    }

    [Fact]
    public void ForEach_ModifyDuringCall_Throws()
    {
        var list = new SimpleList<int> { 1, 2, 3 };
        Assert.Throws<InvalidOperationException>(() => list.ForEach(x => list.Remove(x)));
    }

    [Fact]
    public void AsReadOnly_ReflectsChangesAndCannotBeModified()
    {
        var list = new SimpleList<int> { 1 };
        var view = list.AsReadOnly();
        list.Add(2);
        Assert.Equal([1, 2], view);
        Assert.Throws<NotSupportedException>(() => ((IList<int>)view).Add(3));
    }

    [Fact]
    public void CopyTo_WritesAtOffset()
    {
        var list = new SimpleList<int> { 1, 2 };
        var target = new int[4];
        list.CopyTo(target, 1);
        Assert.Equal([0, 1, 2, 0], target);
        Assert.Throws<ArgumentException>(() => list.CopyTo(new int[1], 0));
    }

    [Fact]
    public void NullArguments_Throw()
    {
        var list = new SimpleList<int>();
        Assert.Throws<ArgumentNullException>(() => new SimpleList<int>(null!));
        Assert.Throws<ArgumentNullException>(() => list.AddRange(null!));
        Assert.Throws<ArgumentNullException>(() => list.RemoveAll(null!));
        Assert.Throws<ArgumentNullException>(() => list.FindIndex(null!));
        Assert.Throws<ArgumentNullException>(() => list.ForEach(null!));
        Assert.Throws<ArgumentNullException>(() => list.Sort((Comparison<int>)null!));
    }

    [Fact]
    public void WorksWithNullItems()
    {
        var list = new SimpleList<string?> { "a", null, "b" };
        Assert.Equal(1, list.IndexOf(null));
        Assert.True(list.Remove(null));
        Assert.Equal<string?>(["a", "b"], list);
    }
}
