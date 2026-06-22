using System;

namespace RailwayManager.Personnel
{
    /// <summary>
    /// M8-7: Wynik proby auto-assignu przez <see cref="Runtime.DispatcherService.TryAutoAssignReplacement"/>.
    /// </summary>
    public enum DispatchResult
    {
        /// <summary>Zastepstwo znalezione i zakolejkowane — status Processing lub Delayed.</summary>
        Success,
        /// <summary>Brak aktywnych dyspozytorow w firmie — akcja niemozliwa.</summary>
        NoDispatcher,
        /// <summary>Brak odpowiedniego kandydata (nikt nie ma wymaganego skilla / wszyscy chorzy/na urlopie).</summary>
        NoCandidateFound,
        /// <summary>Dispatcher przeciazony (D27 critical) — random miss 20%.</summary>
        Missed,
        /// <summary>Vacancy data invalid / inner error.</summary>
        InvalidRequest
    }

    /// <summary>M8-7: Status akcji dyspozytora w kolejce pending.</summary>
    public enum PendingActionStatus
    {
        /// <summary>Capacity OK — akcja applied instant.</summary>
        Processing,
        /// <summary>Over capacity (D27) — akcja z delay 2-6h.</summary>
        Delayed,
        /// <summary>Zakonczona sukcesem — pracownik przypisany.</summary>
        Done,
        /// <summary>Zakonczona porazka — np. pracownik zastepczy odszedl przed aplikacja.</summary>
        Failed,
        /// <summary>Anulowana przez gracza.</summary>
        Cancelled
    }

    /// <summary>
    /// M8-7: Jedna akcja dyspozytora w kolejce (L4 replacement, ad-hoc crew assignment).
    /// Processowane w <see cref="Runtime.DispatcherService.ApplyDailyTick"/>:
    /// - Processing → Done natychmiast przy dodawaniu
    /// - Delayed → odczekuje delayHours game time, potem Done
    /// - Po Done: czyszczone w kolejnym tick'u
    /// </summary>
    [Serializable]
    public class PendingDispatchAction
    {
        public int actionId;
        public CrewVacancyData vacancy;
        public int replacementEmployeeId;
        public PendingActionStatus status;
        public string createdDateIso;
        /// <summary>Ile game hours zostalo do processu (dla Delayed).</summary>
        public float etaHoursRemaining;
        /// <summary>Skill dyspozytora ktory podejmie akcje — wplyw na quality decyzji.</summary>
        public int dispatcherSkillUsed;
    }
}
