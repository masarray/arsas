using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace ArIED61850Tester.Models;

/// <summary>
/// Observable collection with reset-based batch operations. This keeps WPF from
/// processing one collection notification per event during report bursts.
/// </summary>
public sealed class BulkObservableCollection<T> : ObservableCollection<T>
{
    public void AddRange(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var buffer = items as ICollection<T> ?? items.ToList();
        if (buffer.Count == 0)
            return;

        if (Items is List<T> list)
            list.AddRange(buffer);
        else
            foreach (var item in buffer)
                Items.Add(item);

        RaiseReset();
    }

    public void InsertRangeAtStart(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var buffer = items as IReadOnlyList<T> ?? items.ToList();
        if (buffer.Count == 0)
            return;

        // Input is expected oldest-to-newest; reverse it so the newest item is first.
        var newestFirst = buffer.Reverse().ToList();
        if (Items is List<T> list)
            list.InsertRange(0, newestFirst);
        else
            for (var index = newestFirst.Count - 1; index >= 0; index--)
                Items.Insert(0, newestFirst[index]);

        RaiseReset();
    }

    public void ReplaceAll(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var buffer = items as ICollection<T> ?? items.ToList();
        Items.Clear();
        if (Items is List<T> list)
            list.AddRange(buffer);
        else
            foreach (var item in buffer)
                Items.Add(item);
        RaiseReset();
    }

    public void TrimStart(int maximumCount)
    {
        maximumCount = Math.Max(0, maximumCount);
        if (Items.Count <= maximumCount)
            return;

        var removeCount = Items.Count - maximumCount;
        if (Items is List<T> list)
            list.RemoveRange(0, removeCount);
        else
            for (var index = 0; index < removeCount; index++)
                Items.RemoveAt(0);

        RaiseReset();
    }

    public void TrimEnd(int maximumCount)
    {
        maximumCount = Math.Max(0, maximumCount);
        if (Items.Count <= maximumCount)
            return;

        var removeCount = Items.Count - maximumCount;
        if (Items is List<T> list)
            list.RemoveRange(maximumCount, removeCount);
        else
            while (Items.Count > maximumCount)
                Items.RemoveAt(Items.Count - 1);

        RaiseReset();
    }

    private void RaiseReset()
    {
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
