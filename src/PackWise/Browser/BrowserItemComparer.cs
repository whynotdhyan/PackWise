using System.Collections;

namespace PackWise.Browser;

public enum SortColumn
{
    Name,
    Size,
    Packed,
    Type,
    Modified,
    Crc,
    Location,
}

/// <summary>Explorer-like ordering: parent link first, folders before files, natural name order.</summary>
public sealed class BrowserItemComparer : IComparer, IComparer<BrowserItem>
{
    public BrowserItemComparer(SortColumn column, bool descending)
    {
        Column = column;
        Descending = descending;
    }

    public SortColumn Column { get; }

    public bool Descending { get; }

    public int Compare(object? x, object? y) => Compare(x as BrowserItem, y as BrowserItem);

    public int Compare(BrowserItem? x, BrowserItem? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null || y is null)
        {
            return x is null ? -1 : 1;
        }

        var rank = Rank(x).CompareTo(Rank(y));
        if (rank != 0)
        {
            return rank;
        }

        var result = Column switch
        {
            SortColumn.Size => Nullable.Compare(x.Size, y.Size),
            SortColumn.Packed => Nullable.Compare(x.PackedSize, y.PackedSize),
            SortColumn.Type => string.Compare(x.TypeName, y.TypeName, StringComparison.CurrentCultureIgnoreCase),
            SortColumn.Modified => Nullable.Compare(x.Modified, y.Modified),
            SortColumn.Crc => Nullable.Compare(x.Crc, y.Crc),
            SortColumn.Location => NaturalCompare(x.Location ?? string.Empty, y.Location ?? string.Empty),
            _ => 0,
        };

        if (result == 0)
        {
            result = NaturalCompare(x.Name, y.Name);
        }

        return Descending ? -result : result;
    }

    private static int Rank(BrowserItem item) => item.Type switch
    {
        BrowserItemType.ParentLink => 0,
        BrowserItemType.Drive => 1,
        BrowserItemType.Folder or BrowserItemType.ArchiveFolder => 2,
        _ => 3,
    };

    /// <summary>Compares names so that "file2" sorts before "file10".</summary>
    public static int NaturalCompare(string a, string b)
    {
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (char.IsAsciiDigit(a[i]) && char.IsAsciiDigit(b[j]))
            {
                var startA = i;
                var startB = j;
                while (i < a.Length && char.IsAsciiDigit(a[i]))
                {
                    i++;
                }

                while (j < b.Length && char.IsAsciiDigit(b[j]))
                {
                    j++;
                }

                var numberA = a.AsSpan(startA, i - startA).TrimStart('0');
                var numberB = b.AsSpan(startB, j - startB).TrimStart('0');
                if (numberA.Length != numberB.Length)
                {
                    return numberA.Length.CompareTo(numberB.Length);
                }

                var digits = numberA.CompareTo(numberB, StringComparison.Ordinal);
                if (digits != 0)
                {
                    return digits;
                }
            }
            else
            {
                var compare = string.Compare(a, i, b, j, 1, StringComparison.CurrentCultureIgnoreCase);
                if (compare != 0)
                {
                    return compare;
                }

                i++;
                j++;
            }
        }

        return (a.Length - i).CompareTo(b.Length - j);
    }
}
