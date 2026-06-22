using System.Collections.Generic;

namespace RailwayManager.Timetable.Simulation
{
    /// <summary>
    /// M-Dispatch Faza 1: ze zbioru prognoz (<see cref="TrainForecast"/>) wykrywa konflikty —
    /// pary pociągów, których rezerwacje TEGO SAMEGO bloku nakładają się w czasie. Network-wide:
    /// bierze wszystkie podane prognozy (caller decyduje o zakresie/horyzoncie/klastrze).
    ///
    /// Deterministyczne: rezerwacje per blok sortowane (enter asc, potem trainRunId), a finalna
    /// lista konfliktów też (blockKey, firstId, secondId) — niezależne od kolejności iteracji słownika.
    /// </summary>
    public static class BlockConflictDetector
    {
        public static List<BlockConflict> Detect(IReadOnlyList<TrainForecast> forecasts)
        {
            var result = new List<BlockConflict>();
            if (forecasts == null || forecasts.Count < 2) return result;

            var byBlock = new Dictionary<int, List<Entry>>();
            foreach (var f in forecasts)
            {
                if (f == null || f.reservations == null) continue;
                foreach (var r in f.reservations)
                {
                    if (!byBlock.TryGetValue(r.blockKey, out var list))
                    {
                        list = new List<Entry>();
                        byBlock[r.blockKey] = list;
                    }
                    list.Add(new Entry(f.trainRunId, r.enterSec, r.exitSec));
                }
            }

            foreach (var kv in byBlock)
            {
                var list = kv.Value;
                if (list.Count < 2) continue;

                list.Sort((a, b) =>
                {
                    int c = a.enter.CompareTo(b.enter);
                    return c != 0 ? c : a.id.CompareTo(b.id);
                });

                for (int i = 0; i < list.Count - 1; i++)
                {
                    for (int j = i + 1; j < list.Count; j++)
                    {
                        // Posortowane po enter: gdy j wjeżdża po wyjeździe i, dalsze też nie kolidują z i.
                        if (list[j].enter >= list[i].exit) break;

                        double os = System.Math.Max(list[i].enter, list[j].enter);
                        double oe = System.Math.Min(list[i].exit, list[j].exit);
                        result.Add(new BlockConflict(kv.Key, list[i].id, list[j].id, os, oe));
                    }
                }
            }

            // Deterministyczny output niezależnie od kolejności iteracji słownika.
            result.Sort((a, b) =>
            {
                int c = a.blockKey.CompareTo(b.blockKey);
                if (c != 0) return c;
                c = a.firstTrainRunId.CompareTo(b.firstTrainRunId);
                return c != 0 ? c : a.secondTrainRunId.CompareTo(b.secondTrainRunId);
            });
            return result;
        }

        readonly struct Entry
        {
            public readonly int id;
            public readonly double enter;
            public readonly double exit;
            public Entry(int id, double enter, double exit) { this.id = id; this.enter = enter; this.exit = exit; }
        }
    }
}
