namespace RailwayManager.Personnel
{
    /// <summary>
    /// M8/D1: 10 rol personelu w EA.
    /// 4 operacyjne (Driver/Conductor/Mechanic/TrafficController),
    /// 2 administracyjne (Office/Dispatcher),
    /// 4 wspierajace (Cleaner/WashBay/Research/TicketClerk).
    /// </summary>
    public enum EmployeeRole
    {
        /// <summary>Maszynista — prowadzi pociag, wymagany dla kazdego TrainRun.</summary>
        Driver,
        /// <summary>Konduktor — kontrola biletow, wymagany wg D16/D31 (ilosc wagonow/EMU/DMU).</summary>
        Conductor,
        /// <summary>Mechanik — naprawy i przeglady w warsztatach (M7 hook).</summary>
        Mechanic,
        /// <summary>Sprzatacz — czyszczenie wnetrza pojazdu w depocie.</summary>
        Cleaner,
        /// <summary>Pracownik myjni — obsluga myjni zewnetrznej.</summary>
        WashBay,
        /// <summary>Biurowy — administracja, obniza fixed costs, odblokowuje akcje.</summary>
        Office,
        /// <summary>R&amp;D — badania i rozwoj, sciezki upgrade (framework w M8, tresc post-M12d).</summary>
        Research,
        /// <summary>Kasjer — sprzedaz biletow na stacji. D19: wizualnie tylko w popupie stacji.</summary>
        TicketClerk,
        /// <summary>Dyspozytor (D27) — auto-assign personel/pociag, L4 replacement. Capacity 50+5×(skill-1).</summary>
        Dispatcher,
        /// <summary>Dyzurny ruchu (D28) — priority provider dla M9b DepotMovementSim. Capacity 10+5×(skill-1).</summary>
        TrafficController
    }

    /// <summary>Biezacy stan pracownika — warunkuje czy mozna przypisac do pracy.</summary>
    public enum EmployeeStatus
    {
        /// <summary>Dostepny — moze byc przypisany do turnusu/pracy.</summary>
        Available,
        /// <summary>Na aktywnej zmianie.</summary>
        OnShift,
        /// <summary>Na dniu wolnym (regen fatigue).</summary>
        Resting,
        /// <summary>L4 — krotka choroba 1-3 dni (D13).</summary>
        Sick,
        /// <summary>Dluga choroba (post-EA, M12d).</summary>
        LongSick,
        /// <summary>Na szkoleniu (stub — post-EA, D11).</summary>
        Training,
        /// <summary>Emerytura (D13, po 30-dniowym wypowiedzeniu).</summary>
        Retired,
        /// <summary>Zwolniony.</summary>
        Fired,
        /// <summary>
        /// MM-D11/D20: Onboarding — pracownik zaczął zmianę ale czeka na odprawę
        /// dyspozytora. Po <see cref="Employee.onboardingFinishGameTime"/> przechodzi
        /// do <see cref="OnShift"/>. NIE jest produktywny (Office/Workshop hooki
        /// sprawdzają tylko OnShift). Czas zależny od Dispatcher lvl
        /// (lvl1=2.0× baseline, lvl5=0.5× baseline).
        /// </summary>
        Onboarding,
    }

    /// <summary>3 typy zmian bazowych (D4). Dodatkowo indywidualne harmonogramy nadpisuja przez <see cref="ScheduleOverride"/>.</summary>
    public enum ShiftType
    {
        /// <summary>06:00–14:00.</summary>
        Morning,
        /// <summary>14:00–22:00.</summary>
        Afternoon,
        /// <summary>22:00–06:00 (rollover doby).</summary>
        Night
    }

    /// <summary>Cykl dni pracujacych/wolnych (D4). Domyslny 5+2, custom w post-EA.</summary>
    public enum WorkCyclePattern
    {
        /// <summary>5 dni pracy, 2 wolne (klasyczny).</summary>
        Cycle5_2,
        /// <summary>4+2 (krotszy tydzien).</summary>
        Cycle4_2,
        /// <summary>6+2 (dluzszy tydzien).</summary>
        Cycle6_2,
        /// <summary>7+7 (tygodniowy rytm — dalekobiezne).</summary>
        Cycle7_7,
        /// <summary>Ustalany recznie przez ScheduleOverride (post-EA pelne UI).</summary>
        Custom
    }

    /// <summary>Typ sluzby w obiegu pracowniczym (D5, D20).</summary>
    public enum CrewDutyKind
    {
        /// <summary>Wlasciwe prowadzenie pociagu (TrainRun z obiegu taboru).</summary>
        Service,
        /// <summary>Przerwa na stacji (czeka w kantynie/peronie).</summary>
        Break,
        /// <summary>Powrot sluzbowy — jedzie pasazerem (nie prowadzi).</summary>
        Deadhead,
        /// <summary>Przekazanie zmiany innemu maszyniscie (5 min buforu).</summary>
        Handover,
        /// <summary>Nocleg w hotelu (D20, multi-day).</summary>
        Overnight
    }

    /// <summary>Typ szkolenia (stub — POST-EA, D11).</summary>
    public enum TrainingType
    {
        /// <summary>Szkolenie zewnetrzne (100% success, drozsze).</summary>
        External,
        /// <summary>Mentoring z seniorem firmy (80% success, tanszy, wymaga mentora).</summary>
        Internal
    }

    /// <summary>Poziom hotelu w multi-day turnusie (D20, D29). Wplywa na morale + fatigue regen + error chance.</summary>
    public enum HotelTier
    {
        /// <summary>80 zl/noc. Morale -1/noc, fatigue regen -20%, error chance +10%.</summary>
        Basic,
        /// <summary>150 zl/noc. Baseline — neutralne.</summary>
        Standard,
        /// <summary>250 zl/noc. Morale +2/noc, fatigue regen +20%, error chance -10%.</summary>
        Premium
    }

    /// <summary>Typ wpisu w indywidualnym harmonogramie (D6). Nadpisuje cykl bazowy.</summary>
    public enum ScheduleOverrideType
    {
        /// <summary>Urlop platny (pensja pelna, 26 dni/rok, D13).</summary>
        Vacation,
        /// <summary>L4 (auto-generowane przez SickLeaveService, pensja pelna w EA).</summary>
        SickLeave,
        /// <summary>Szkolenie (stub post-EA, D11).</summary>
        Training,
        /// <summary>Zmiana zmiany na konkretny dzien (z Morning na Night itp.).</summary>
        ShiftSwap,
        /// <summary>Dodatkowy dzien pracy poza cyklem (nadgodziny, +morale penalty).</summary>
        ExtraDutyDay,
        /// <summary>Dodatkowy dzien wolny poza cyklem.</summary>
        FreeDay
    }

    /// <summary>Stan obciazenia dyspozytora (D27).</summary>
    public enum DispatcherStatus
    {
        /// <summary>Headcount ≤ capacity. Auto-akcje instant.</summary>
        Normal,
        /// <summary>Headcount 1.0–1.5× capacity. Auto-akcje z delay 2-6h.</summary>
        Delayed,
        /// <summary>Headcount &gt;1.5× capacity. Random 20% akcji missed.</summary>
        Critical
    }

    /// <summary>Stan obciazenia dyzurnego ruchu per depot (D28).</summary>
    public enum TrafficControllerStatus
    {
        /// <summary>Active tasks ≤ capacity. Priority scheduling aktywne.</summary>
        Normal,
        /// <summary>Active tasks 1.0–1.5× capacity. Kolejka PendingTasks rosnie.</summary>
        Queued,
        /// <summary>Active tasks &gt;1.5× capacity. Random +50% czas wykonania.</summary>
        Critical
    }

    /// <summary>Status wyplaty dla konkretnego pracownika w danym miesiacu (D10).</summary>
    public enum PaymentStatus
    {
        /// <summary>Wyplacono na czas.</summary>
        Paid,
        /// <summary>Opoznione (czeka na srodki, morale -20 juz zastosowane).</summary>
        Delayed,
        /// <summary>Niewyplacone przez 3+ miesiace (pracownik odchodzi).</summary>
        Missed
    }
}
