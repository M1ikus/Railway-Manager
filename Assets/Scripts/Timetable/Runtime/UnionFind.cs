namespace RailwayManager.Timetable
{
    /// <summary>
    /// Union-Find (Disjoint Set) z path compression i union-by-rank.
    /// Używane do merging vertex positions przy budowie PathfindingGraph.
    /// </summary>
    public class UnionFind
    {
        private readonly int[] _parent;
        private readonly int[] _rank;

        public UnionFind(int size)
        {
            _parent = new int[size];
            _rank = new int[size];
            for (int i = 0; i < size; i++)
            {
                _parent[i] = i;
                _rank[i] = 0;
            }
        }

        /// <summary>Znajduje root tego elementu (z path compression).</summary>
        public int Find(int x)
        {
            while (_parent[x] != x)
            {
                _parent[x] = _parent[_parent[x]]; // path compression
                x = _parent[x];
            }
            return x;
        }

        /// <summary>Łączy dwa zbiory. Zwraca true jeśli nastąpił merge, false jeśli już były w tym samym zbiorze.</summary>
        public bool Union(int a, int b)
        {
            int rootA = Find(a);
            int rootB = Find(b);
            if (rootA == rootB) return false;

            // Union by rank: dołącz mniejsze drzewo do większego
            if (_rank[rootA] < _rank[rootB])
            {
                _parent[rootA] = rootB;
            }
            else if (_rank[rootA] > _rank[rootB])
            {
                _parent[rootB] = rootA;
            }
            else
            {
                _parent[rootB] = rootA;
                _rank[rootA]++;
            }
            return true;
        }

        public int Count => _parent.Length;
    }
}
