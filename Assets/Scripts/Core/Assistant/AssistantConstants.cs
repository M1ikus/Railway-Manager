namespace RailwayManager.Core.Assistant
{
    /// <summary>
    /// M11: stałe asystenta gracza. Progi stuck-detectora to wartości balansowe —
    /// konserwatywny tuning anty-Clippy (lepiej za późno niż za nachalnie).
    /// </summary>
    public static class AssistantConstants
    {
        /// <summary>Cadence wołania NextStepMonitor.Tick przez hosta (AssistantOrchestrator w SharedUI).</summary>
        public const float EvaluateIntervalRealSec = 1f;

        // TODO (M-Balance, AS-OQ3): progi stuck-detectora — do tuningu w playtestach (anty-Clippy).

        /// <summary>Czas realny bezczynności na bieżącym kroku, po którym monitor uznaje „gracz utknął".</summary>
        public const float StuckTimeOnStepRealSec = 120f;

        /// <summary>Ile razy gracz otwiera i zamyka panel bez akcji, zanim uznamy „utknął".</summary>
        public const int StuckPanelAbandonedThreshold = 2;

        /// <summary>Ile anulowań akcji w obrębie bieżącego kroku, zanim uznamy „utknął".</summary>
        public const int StuckActionCanceledThreshold = 3;

        /// <summary>Minimalny odstęp realny między kolejnymi szeptami (driver w SharedUI).</summary>
        public const float WhisperCooldownRealSec = 45f;

        /// <summary>Auto-expire szeptu — czas realny (hybryda jak PersonnelNotificationToastUI).</summary>
        public const float WhisperAutoExpireRealSec = 60f;

        /// <summary>Auto-expire szeptu — czas gry (hybryda: cokolwiek minie pierwsze).</summary>
        public const float WhisperAutoExpireGameSec = 3600f;

        /// <summary>Maks. wpisów historii działań asystenta w panelu (AS-D6 log „co zrobiłem").</summary>
        public const int HistoryMaxEntries = 20;

        // ── AS-6 advisor (progi reguł reaktywnych — tuning M-Balance) ──

        /// <summary>
        /// Próg „pojazd niesprawny" w regułach advisora — parytet z
        /// CirculationService.GetBrokenVehiclesInCirculation (M7: awaria poniżej 10%).
        /// </summary>
        public const float BrokenConditionPercent = 10f;

        /// <summary>Ile KOLEJNYCH dni linia na minusie, zanim advisor zasugeruje przegląd finansów.</summary>
        public const int UnprofitableLineDays = 3;

        /// <summary>
        /// Czas realny „wszystko skonfigurowane i gra się toczy", po którym advisor podsuwa
        /// rozwój („rozważ nową linię"). Proxy nudy MVP — nie mierzy bezczynności inputu.
        /// </summary>
        public const float BoredomHoldRealSec = 480f;
    }
}
