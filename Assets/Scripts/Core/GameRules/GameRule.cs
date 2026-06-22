namespace RailwayManager.Core.GameRules
{
    /// <summary>
    /// M13-13 / D36: Enum z toggle'ami "reguły gry" wybieranymi w GameCreator.
    ///
    /// Każda reguła = bool włącz/wyłącz, niemodyfikowalny mid-game (D36).
    /// Sprawdzanie w runtime: <c>GameRulesService.IsEnabled(GameRule.VehicleBreakdowns)</c>.
    ///
    /// Scope w M13-13: tylko **infrastruktura** + 3 placeholder toggle (proof of concept).
    /// **Konkretna lista reguł + per-toggle behavior wire-up dorzucany w M-Balance**
    /// (wymaga playtesta czy "casual mode bez X" jest grywalny).
    ///
    /// Domyślne wartości (gdy reguła nie jest w configu): wszystko ON (gra "pełna").
    /// </summary>
    public enum GameRule
    {
        /// <summary>Awarie taboru (M7 BreakdownService). Off = 0% breakdown chance bez względu na komponenty.
        /// Casual mode dla osób które nie chcą maintenance gameplay.</summary>
        VehicleBreakdowns = 0,

        /// <summary>Losowe zdarzenia (M12d random events: pogoda, awarie sieci, strajki). Off = nigdy nie wystąpią.</summary>
        RandomEvents = 1,

        /// <summary>Dotacje wojewódzkie (M6 SubsidyCalculator). Off = brak dochodów z dotacji,
        /// gracz musi zarabiać tylko biletami. Hard-mode flavor.</summary>
        VoivodeshipSubsidies = 2,

        /// <summary>MB-1: Degradacja komponentów per-km (M7 DegradationService). Off = wszystkie komponenty
        /// zawsze 100%. Łączyć z <see cref="VehicleBreakdowns"/>=Off dla pełnego casual mode bez maintenance.</summary>
        MaintenanceComponentDecay = 3,

        /// <summary>MB-1: Morale pracowników wpływa na produktywność (M8). Off = produktywność 100% zawsze
        /// (pracownicy nigdy nie strajkują ad-hoc, nigdy nie zwalniają się sami z firmy).</summary>
        EmployeeMorale = 4,

        /// <summary>MB-1: Pogoda wpływa na breakdown chance i delay propagation (M12d). Off = pogoda neutralna
        /// — żadnych "marznący deszcz +30% breakdown" eventów.</summary>
        WeatherImpact = 5,

        /// <summary>M11 AS-D2/D7: proaktywność asystenta gracza — onboarding + szept advisora
        /// (to, co zaczepia SAMO). Off = asystent odzywa się tylko na życzenie; delegacja przez
        /// panel (PULL) działa ZAWSZE, niezależnie od tej reguły. Default per preset trudności:
        /// Easy/Normal ON, Hard/Realistic OFF (ustawia GameCreator).</summary>
        AssistantProactivity = 6

        // M-Balance dorzuci kolejne kandydatów (PassengerComplaints, RescueDeliveryRealtime,
        // WorkshopExternalQueue, PersonnelStrikes) — bez modyfikowania istniejących indeksów (zachowujemy
        // save back-compat: nowe wartości dodawane na końcu).
    }
}
