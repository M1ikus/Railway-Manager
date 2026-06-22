using System.Collections.Generic;

namespace RailwayManager.Timetable.Simulation
{
    /// <summary>
    /// M-Dispatch Faza 3a: gwarancja „nie zakorkuje się" — wykrywa, czy decyzja HOLD
    /// (me czeka na rywala) domknęłaby cykl wait-for = zakleszczenie sieci.
    ///
    /// Graf wait-for z prognoz: krawędź <c>X→Y</c> gdy Y rezerwuje wspólny blok ŚCIŚLE
    /// wcześniej i blokuje X (X musi czekać na Y). Naturalnie, skoro `me` wjeżdża na sporny
    /// blok pierwszy, istnieje krawędź <c>rywal→me</c>. HOLD ją INWERTUJE (me→rywal). Jeśli po
    /// inwersji rywal nadal może dotrzeć do `me` INNYMI krawędziami (rywal→…→me), to me→rywal
    /// domyka cykl → trzymanie zakleszcza → decyzja musi brzmieć PUŚĆ (rozbij cykl).
    ///
    /// Deterministyczne: wynik (bool osiągalności) jest niezależny od kolejności krawędzi.
    /// </summary>
    public static class DispatchSafeState
    {
        /// <summary>
        /// Czy HOLD (me czeka na rywala) stworzyłby deadlock. True = trzymać NIE WOLNO.
        /// </summary>
        public static bool HoldWouldDeadlock(int meId, int rivalId, IReadOnlyList<TrainForecast> all)
        {
            if (all == null || all.Count < 2 || meId == rivalId) return false;

            var edges = BuildWaitForEdges(all);
            // Cykl me→rywal→…→me istnieje wtw rywal dociera do me NATURALNYMI krawędziami,
            // pomijając bezpośrednią rywal→me (tę inwertujemy HOLD-em).
            return Reachable(rivalId, meId, edges, ignoreFromNode: rivalId, ignoreToNode: meId);
        }

        /// <summary>
        /// M-Dispatch Faza 4a (koszt kaskady): suma priorytetów pociągów, które czekają na
        /// <paramref name="blocker"/> (czyli utkną/opóźnią się, gdy on stoi). <paramref name="excludeId"/>
        /// pomija kontrpartnera decyzji (rywala/me), by nie liczyć go jako „kolejki za".
        /// </summary>
        public static int SumWaiterPriorities(TrainForecast blocker, IReadOnlyList<TrainForecast> all, int excludeId)
        {
            if (blocker == null || all == null) return 0;
            int sum = 0;
            foreach (var y in all)
            {
                if (y == null || y.trainRunId == blocker.trainRunId || y.trainRunId == excludeId) continue;
                if (Waits(y, blocker)) sum += y.priority; // y czeka na blocker
            }
            return sum;
        }

        static Dictionary<int, List<int>> BuildWaitForEdges(IReadOnlyList<TrainForecast> all)
        {
            var edges = new Dictionary<int, List<int>>();
            for (int a = 0; a < all.Count; a++)
            {
                var x = all[a];
                if (x == null) continue;
                for (int b = 0; b < all.Count; b++)
                {
                    if (a == b) continue;
                    var y = all[b];
                    if (y == null) continue;
                    if (Waits(x, y))
                    {
                        if (!edges.TryGetValue(x.trainRunId, out var list))
                        {
                            list = new List<int>();
                            edges[x.trainRunId] = list;
                        }
                        if (!list.Contains(y.trainRunId)) list.Add(y.trainRunId);
                    }
                }
            }
            return edges;
        }

        /// <summary>X czeka na Y, jeśli istnieje wspólny blok, na którym Y wjeżdża ściśle wcześniej
        /// i jeszcze go zajmuje, gdy X chce wjechać.</summary>
        static bool Waits(TrainForecast x, TrainForecast y)
        {
            var xr = x.reservations;
            var yr = y.reservations;
            for (int i = 0; i < xr.Count; i++)
            {
                for (int j = 0; j < yr.Count; j++)
                {
                    if (xr[i].blockKey != yr[j].blockKey) continue;
                    if (yr[j].enterSec < xr[i].enterSec && yr[j].exitSec > xr[i].enterSec)
                        return true;
                }
            }
            return false;
        }

        static bool Reachable(int from, int to, Dictionary<int, List<int>> edges,
                              int ignoreFromNode, int ignoreToNode)
        {
            if (from == to) return true;
            var visited = new HashSet<int> { from };
            var stack = new Stack<int>();
            stack.Push(from);
            while (stack.Count > 0)
            {
                int u = stack.Pop();
                if (!edges.TryGetValue(u, out var neighbors)) continue;
                foreach (int v in neighbors)
                {
                    if (u == ignoreFromNode && v == ignoreToNode) continue; // pomiń inwertowaną krawędź
                    if (v == to) return true;
                    if (visited.Add(v)) stack.Push(v);
                }
            }
            return false;
        }
    }
}
