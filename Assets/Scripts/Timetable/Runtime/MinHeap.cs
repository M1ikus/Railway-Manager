using System.Collections.Generic;

namespace RailwayManager.Timetable
{
    /// <summary>
    /// Prosty binary min-heap (priority queue) dla A*. Trzyma pary (priority, value).
    /// Implementacja bez dependencies, żeby działało pod każdym runtime Unity.
    /// </summary>
    public class MinHeap<T>
    {
        private readonly List<(float priority, T value)> _items = new();

        public int Count => _items.Count;

        public void Push(T value, float priority)
        {
            _items.Add((priority, value));
            BubbleUp(_items.Count - 1);
        }

        public bool TryPop(out T value)
        {
            if (_items.Count == 0) { value = default; return false; }
            value = _items[0].value;

            int last = _items.Count - 1;
            _items[0] = _items[last];
            _items.RemoveAt(last);
            if (_items.Count > 0) BubbleDown(0);
            return true;
        }

        public void Clear() => _items.Clear();

        private void BubbleUp(int i)
        {
            while (i > 0)
            {
                int parent = (i - 1) / 2;
                if (_items[i].priority >= _items[parent].priority) break;
                (_items[i], _items[parent]) = (_items[parent], _items[i]);
                i = parent;
            }
        }

        private void BubbleDown(int i)
        {
            int n = _items.Count;
            while (true)
            {
                int left = 2 * i + 1;
                int right = 2 * i + 2;
                int smallest = i;

                if (left < n && _items[left].priority < _items[smallest].priority) smallest = left;
                if (right < n && _items[right].priority < _items[smallest].priority) smallest = right;
                if (smallest == i) break;

                (_items[i], _items[smallest]) = (_items[smallest], _items[i]);
                i = smallest;
            }
        }
    }
}
