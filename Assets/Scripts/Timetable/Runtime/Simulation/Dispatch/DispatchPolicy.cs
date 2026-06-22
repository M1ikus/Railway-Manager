using RailwayManager.Core;

namespace RailwayManager.Timetable.Simulation
{
    /// <summary>
    /// M-Dispatch Faza 4b: warstwa Timetable nad polityką dispatchera. Sama WARTOŚĆ polityki
    /// żyje w <see cref="GameState.MapDispatchPolicy"/> (Core — ustawiana przez GameCreator/ustawienia,
    /// persistowana przez WorldSavable). Tu tylko mapowanie polityki → parametry decyzji
    /// (hold-bias, enabled), korzystające ze stałych tuningu w Timetable.
    /// </summary>
    public static class DispatchPolicyService
    {
        /// <summary>Read-through do GameState (Core). Setter dla testów/debug; gracz ustawia przez UI.</summary>
        public static DispatchPolicy CurrentPolicy
        {
            get => GameState.MapDispatchPolicy;
            set => GameState.MapDispatchPolicy = value;
        }

        /// <summary>Czy predykcyjne trzymanie jest w ogóle aktywne (Off = nie).</summary>
        public static bool HoldingEnabled => CurrentPolicy != DispatchPolicy.Off;

        /// <summary>Mnożnik kosztu wypuszczenia: &gt;1 = trzymaj chętniej (Punktualność), 1 = neutralnie.</summary>
        public static float HoldBias =>
            CurrentPolicy == DispatchPolicy.Punctuality
                ? TimetableTuningConstants.DispatchPunctualityHoldBias
                : 1f;

        public static void ResetToDefault() => GameState.MapDispatchPolicy = DispatchPolicy.Balanced;
    }
}
