namespace RailwayManager.Fleet
{
    /// <summary>
    /// C1: typowany wynik <c>Schedule*</c> w job services (Painting/SelfPainting/Outdoor/
    /// Modernization/VehicleModification). Wcześniej zwracały <c>bool</c> z <c>Log.Warn</c>
    /// opisującym powód — UI nie miało jak zaprezentować gracza powodu odmowy.
    ///
    /// <para>UI może użyć <see cref="ScheduleResultExtensions.ToUserMessage"/> dla
    /// human-readable komunikatu.</para>
    /// </summary>
    public enum ScheduleResult
    {
        /// <summary>Job zaplanowany pomyślnie.</summary>
        Success = 0,

        /// <summary>Pojazd o danym <c>vehicleId</c> nie istnieje w OwnedVehicles.</summary>
        VehicleNotFound,
        /// <summary>Pojazd ma już aktywny job tego samego typu — anuluj poprzedni najpierw.</summary>
        VehicleHasActiveJob,
        /// <summary>Pojazd nie pasuje do typu jobu (np. Refuel dla elektryka).</summary>
        IncompatibleVehicle,

        /// <summary>ZNTK / external workshop nie znaleziony po id.</summary>
        WorkshopNotFound,
        /// <summary>Workshop nie oferuje danej usługi (paint/modernization).</summary>
        WorkshopUnavailable,

        /// <summary>Brak gotówki na pokrycie kosztu jobu.</summary>
        InsufficientFunds,

        /// <summary>Hall lvl za niski (modernization internal, self-paint).</summary>
        HallLevelTooLow,
        /// <summary>ServicePit length za krótki dla pojazdu / modernization minimum.</summary>
        ServicePitTooShort,

        /// <summary>Wybrana ścieżka modernizacji nie pasuje do source seriesId pojazdu.</summary>
        PathNotApplicable,
        /// <summary>Wybrana modyfikacja nie pasuje do pojazdu (vehicleType/bogie/length filter).</summary>
        ModNotApplicable,

        /// <summary>Funkcjonalność jeszcze nieddostępna (stub w EA, M-Models / post-EA).</summary>
        StubNotImplemented,
    }

    public static class ScheduleResultExtensions
    {
        public static bool IsSuccess(this ScheduleResult r) => r == ScheduleResult.Success;

        /// <summary>Polski user-friendly komunikat dla wyświetlenia w UI.</summary>
        public static string ToUserMessage(this ScheduleResult r) => r switch
        {
            ScheduleResult.Success              => "OK",
            ScheduleResult.VehicleNotFound      => "Pojazd nie istnieje.",
            ScheduleResult.VehicleHasActiveJob  => "Pojazd ma już aktywne zadanie — anuluj najpierw.",
            ScheduleResult.IncompatibleVehicle  => "Pojazd nie pasuje do tej usługi.",
            ScheduleResult.WorkshopNotFound     => "Wybrany zakład nieznany.",
            ScheduleResult.WorkshopUnavailable  => "Zakład nie oferuje tej usługi.",
            ScheduleResult.InsufficientFunds    => "Brak gotówki.",
            ScheduleResult.HallLevelTooLow      => "Poziom Hali za niski — awansuj Halę.",
            ScheduleResult.ServicePitTooShort   => "Kanał inspekcyjny za krótki dla pojazdu.",
            ScheduleResult.PathNotApplicable    => "Wybrana modernizacja nie pasuje do pojazdu.",
            ScheduleResult.ModNotApplicable     => "Wybrana modyfikacja nie pasuje do pojazdu.",
            ScheduleResult.StubNotImplemented   => "Funkcja niedostępna w tej wersji gry.",
            _                                    => "Nieznany błąd planowania.",
        };
    }
}
