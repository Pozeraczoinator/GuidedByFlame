using System;

namespace Pathfinding.Core
{
    /// <summary>
    /// Prosta wielospecyficzna (generic) implementacja kopca minimalnego (Min-Heap).
    /// Zapewnia wysoką wydajność dla algorytmów operujących na kolejce priorytetowej (A*, Dijkstra).
    /// </summary>
    public class MinHeap<T> where T : IHeapItem<T>
    {
        private T[] _items;
        private int _currentItemCount;

        public MinHeap(int maxHeapSize)
        {
            _items = new T[Math.Max(1, maxHeapSize)];
        }

        public void Add(T item)
        {
            // Powiększ tablicę w razie potrzeby (np. mapy większe niż przewidywano)
            if (_currentItemCount >= _items.Length)
            {
                Array.Resize(ref _items, _items.Length * 2);
            }
            
            item.HeapIndex = _currentItemCount;
            _items[_currentItemCount] = item;
            SortUp(item);
            _currentItemCount++;
        }

        public T RemoveFirst()
        {
            if (_currentItemCount == 0)
                throw new InvalidOperationException("Nie można pobrać elementu z pustego MinHeap.");

            T firstItem = _items[0];
            _currentItemCount--;

            if (_currentItemCount > 0)
            {
                _items[0] = _items[_currentItemCount];
                _items[0].HeapIndex = 0;
                SortDown(_items[0]);
            }

            _items[_currentItemCount] = default;
            return firstItem;
        }

        public void UpdateItem(T item)
        {
            SortUp(item);
        }

        public int Count
        {
            get { return _currentItemCount; }
        }

        public void Clear()
        {
            Array.Clear(_items, 0, _currentItemCount);
            _currentItemCount = 0;
        }

        public bool Contains(T item)
        {
            if (item == null || item.HeapIndex < 0 || item.HeapIndex >= _currentItemCount)
                return false;

            return Equals(_items[item.HeapIndex], item);
        }

        private void SortDown(T item)
        {
            while (true)
            {
                int childIndexLeft = item.HeapIndex * 2 + 1;
                int childIndexRight = item.HeapIndex * 2 + 2;
                int swapIndex = 0;

                if (childIndexLeft < _currentItemCount)
                {
                    swapIndex = childIndexLeft;

                    if (childIndexRight < _currentItemCount)
                    {
                        if (_items[childIndexLeft].CompareTo(_items[childIndexRight]) < 0)
                        {
                            swapIndex = childIndexRight;
                        }
                    }

                    if (item.CompareTo(_items[swapIndex]) < 0)
                    {
                        Swap(item, _items[swapIndex]);
                    }
                    else
                    {
                        return;
                    }
                }
                else
                {
                    return;
                }
            }
        }

        private void SortUp(T item)
        {
            int parentIndex = (item.HeapIndex - 1) / 2;

            while (true)
            {
                T parentItem = _items[parentIndex];
                if (item.CompareTo(parentItem) > 0)
                {
                    Swap(item, parentItem);
                }
                else
                {
                    break;
                }
                parentIndex = (item.HeapIndex - 1) / 2;
            }
        }

        private void Swap(T itemA, T itemB)
        {
            _items[itemA.HeapIndex] = itemB;
            _items[itemB.HeapIndex] = itemA;
            int itemAIndex = itemA.HeapIndex;
            itemA.HeapIndex = itemB.HeapIndex;
            itemB.HeapIndex = itemAIndex;
        }
    }

    /// <summary>
    /// Interfejs wymagany dla elementów trzymanych w MinHeap do szybkiego dostępu O(1).
    /// Klasy i struktury węzłów w algorytmach Pathfinding implementują ten interfejs.
    /// </summary>
    public interface IHeapItem<T> : IComparable<T>
    {
        int HeapIndex { get; set; }
    }
}
