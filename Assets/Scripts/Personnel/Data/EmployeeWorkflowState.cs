namespace RailwayManager.Personnel
{
    /// <summary>
    /// TD-025: Stan workflow pracownika w 3D depot — ortogonalny do
    /// <see cref="EmployeeStatus"/>.
    ///
    /// <para><b>Relacja:</b> <see cref="EmployeeStatus"/> odpowiada na pytanie "czy
    /// pracownik mógłby dziś pracować" (Available/OnShift/Resting/Sick…).
    /// <see cref="EmployeeWorkflowState"/> odpowiada na pytanie "co fizycznie teraz
    /// robi w depot" (idzie do biurka, melduje się, pracuje, wraca do domu…).</para>
    ///
    /// <para><b>Universal flow</b> dla wszystkich ról oprócz Cleaner i TicketClerk
    /// (user decision 2026-05-11):</para>
    /// <code>
    /// OffShift → ComingToDepot → ReportingToDispatcher → GoingToWorkstation
    ///                                  → WorkingAtStation ↔ WorkingMobile
    ///                                  → GoingHome → OffShift
    /// </code>
    ///
    /// <para><b>Driver/Conductor flow</b> (pre-duty workflow):</para>
    /// <code>
    /// OffShift → ComingToDepot → ReportingToDispatcher → AwaitingDeparture
    ///                                  → GoingToVehicle → DrivingTrain (visual hidden)
    ///                                  → [pociag wraca] → GoingHome → OffShift
    /// </code>
    ///
    /// <para><b>Cleaner flow</b> (skip dispatcher meldunek):</para>
    /// <code>
    /// OffShift → ComingToDepot → WorkingMobile (lookup dirty vehicle → walk → 60s clean)
    ///                                                  ↔ idle Social room
    ///                                  → GoingHome → OffShift
    /// </code>
    ///
    /// <para><b>NOT saved</b> — rebuild on load przez <c>PersonnelDispatcher3D</c>
    /// w pierwszym tick'u (default <see cref="OffShift"/> dla missing field).</para>
    /// </summary>
    public enum EmployeeWorkflowState
    {
        /// <summary>Pracownik nie jest w depot (Resting, Sick, OffShift przed startem zmiany).
        /// Visual not spawned. Default state for new Employee i po despawn.</summary>
        OffShift,

        /// <summary>Idzie od <see cref="DepotGateMarker"/> do pierwszego celu (zwykle dispatcher's desk).
        /// Visual spawned, walking via PathGraph.</summary>
        ComingToDepot,

        /// <summary>Melduje się u dyspozytora przed rozpoczęciem pracy. Stoi przy
        /// dispatcher's desk (WorkstationOffice w Dispatcher room) przez <see cref="PersonnelBalanceConstants.MeldunekDurationSec"/>.
        /// Wszyscy oprócz Cleaner/TicketClerk/Dispatcher (sam siebie nie melduje).</summary>
        ReportingToDispatcher,

        /// <summary>Idzie do swojego stanowiska/slotu/pojazdu po zakończeniu meldunku.
        /// Visual walking po PathGraph.</summary>
        GoingToWorkstation,

        /// <summary>Pracuje stacjonarnie (biurkowy, dyspozytor, TC, research przy biurku;
        /// mechanik na slocie warsztatowym; washbay przy WashZone). Visual idle z subtle bob.</summary>
        WorkingAtStation,

        /// <summary>Pracuje mobile (mechanik idzie do drugiego slotu, sprzątacz chodzi po
        /// brudnych pojazdach). Walking z onArrive → timer pracy → next target.</summary>
        WorkingMobile,

        /// <summary>Czeka na wyjazd pojazdu (Driver/Conductor między meldunkiem
        /// a <see cref="GoingToVehicle"/>). Stoi przy biurku dyspozytora lub idle Social room.</summary>
        AwaitingDeparture,

        /// <summary>Driver/Conductor idzie do parking pojazdu po triggerze z
        /// <see cref="Timetable.Simulation.TrainRunSimulator.OnRunSpawned"/>.</summary>
        GoingToVehicle,

        /// <summary>Driver/Conductor wsiadł do pojazdu — visual hidden. Pojazd jedzie
        /// po mapie 2D. Po <c>OnRunDespawned</c> visual reappear przy pojezdzie.</summary>
        DrivingTrain,

        /// <summary>Idzie do <see cref="DepotGateMarker"/>, po dotarciu → despawn +
        /// <c>workflowState = OffShift</c>.</summary>
        GoingHome,

        // ── TD-034: czynności osobiste (transient jak reszta) ──

        /// <summary>TD-034: idzie do mebla czynności osobistej (szafka/łazienka/miejsce przerwy).
        /// Po dotarciu → ChangingClothes/UsingBathroom/OnBreak (zależnie od <c>activeActivityKind</c>).</summary>
        GoingToPersonal,

        /// <summary>TD-034: przebiera się przy szafce (LockerIn na początku / LockerOut na końcu zmiany).
        /// Timer <see cref="PersonnelBalanceConstants.LockerChangeDurationSec"/>. Role operacyjne.</summary>
        ChangingClothes,

        /// <summary>TD-034: w łazience. Timer <see cref="PersonnelBalanceConstants.BathroomDurationSec"/>.</summary>
        UsingBathroom,

        /// <summary>TD-034: na przerwie/posiłku (Social). Timer <see cref="PersonnelBalanceConstants.BreakDurationSec"/>.</summary>
        OnBreak,

        /// <summary>TD-034 G: czeka w kolejce do dyspozytora (biurko zajęte przez innego meldującego).
        /// Re-próba zajęcia co tick; po zwolnieniu → podejście do biurka + meldunek.</summary>
        QueuingForDispatcher,
    }
}
