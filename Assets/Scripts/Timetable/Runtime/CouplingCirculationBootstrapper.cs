using UnityEngine;

namespace RailwayManager.Timetable
{
    /// <summary>
    /// TD-032: instaluje hook ostrzeżenia obiegowego w Depot. DepotMovementSimulator NIE widzi
    /// Timetable (asmdef), więc eksponuje <c>static Func&lt;List&lt;int&gt;,bool&gt; CirculationWarnHook</c>,
    /// a Timetable (wyższa warstwa) podstawia implementację przy starcie gry. Wzór jak
    /// CrewCheckHook / PriorityProvider.
    /// </summary>
    public static class CouplingCirculationBootstrapper
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            DepotSystem.DepotMovementSimulator.CirculationWarnHook =
                CirculationService.IsConsistInActiveCirculationToday;
        }
    }
}
