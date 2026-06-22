using UnityEngine;
using RailwayManager.Core;

namespace RailwayManager.Timetable
{
    /// <summary>
    /// TD-037 (rolling window): dogenerowywanie kursów dla przesuwającego się okna obiegów.
    ///
    /// Generacja runów była one-shot przy aktywacji obiegu (okno `GetActiveDates` = od dnia
    /// aktywacji, default 4 tyg) — po upłynięciu okna świat umierał (zero nowych kursów),
    /// a po load ze starego save'a martwe okno zostawało martwe.
    ///
    /// Hook: <see cref="GameState.OnDayEnded"/> (granica dnia — okno „od dziś" się przesunęło)
    /// + <see cref="TopUpAllActive"/> wołane po TD-037 restore (TrainRunSimulator) — save starszy
    /// niż okno odżywa od dziś. Top-up NIE rusza istniejących runów ani ich ID
    /// (CrewDuty.referencedTrainRunId linkuje po id).
    ///
    /// Załoga dla dogenerowanych dat: istniejący mechanizm (turnusy gracza per-data lub
    /// dispatcher auto-assign w CrewAssignmentService.CheckCrew) — duty linkuje po konkretnym
    /// run-id, więc relink nie ma zastosowania (ograniczenie architektury turnusów, nie TD-037).
    /// </summary>
    public static class TrainRunWindowTopUp
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            GameState.OnDayEnded -= HandleDayEnded; // domain-reload safe (bez podwójnej subskrypcji)
            GameState.OnDayEnded += HandleDayEnded;
        }

        static void HandleDayEnded(string dateIso) => TopUpAllActive();

        /// <summary>Top-up wszystkich Active obiegów. Zwraca liczbę dogenerowanych runów.</summary>
        public static int TopUpAllActive()
        {
            int total = 0;
            foreach (var c in CirculationService.Circulations)
            {
                if (c == null || c.status != CirculationStatus.Active) continue;
                total += TrainRunGenerator.TopUpForCirculation(c);
            }
            if (total > 0)
                Log.Info($"[TrainRunWindowTopUp] Dogenerowano {total} kursów (rolling window)");
            return total;
        }
    }
}
