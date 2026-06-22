namespace RailwayManager.Core
{
    /// <summary>
    /// M-Dispatch Faza 4b: polityka autonomicznego predykcyjnego dispatchera ruchu na mapie OSM.
    /// Globalny knob gracza — ustawiany w kreatorze gry, ZMIENIALNY w trakcie (inaczej niż
    /// difficulty/rules). Wartość żyje w Core (jak Seed/difficulty), bo ustawiają ją GameCreator
    /// i ekran ustawień (asmdef bez Timetable), a czyta dispatcher w Timetable.
    ///
    /// Steruje TYLKO ruchem na mapie OSM. Ruch w zajezdni ma osobnego dyżurnego
    /// (Personnel.TrafficControlService) — nie mylić.
    /// </summary>
    public enum DispatchPolicy
    {
        /// <summary>Bez predykcyjnego trzymania — tylko reaktywna warstwa blokowa (klasyczne semafory).</summary>
        Off = 0,
        /// <summary>Domyślna: minimalizuj ważone opóźnienie (priorytet IRJ + obłożenie + kaskada).</summary>
        Balanced = 1,
        /// <summary>Chroń punktualność wyżej-ważonych — trzymaj chętniej (większy hold-bias).</summary>
        Punctuality = 2,
    }
}
