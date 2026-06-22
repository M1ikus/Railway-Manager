namespace RailwayManager.Personnel.Workflows
{
    /// <summary>
    /// TD-034: rodzaj czynności osobistej pracownika w cyklu dnia 3D.
    /// </summary>
    public enum PersonalActivityKind
    {
        /// <summary>Przebranie w ubranie robocze przy szafce na początku zmiany (role operacyjne).</summary>
        LockerIn,
        /// <summary>Przebranie z powrotem w ubranie prywatne przy szafce przed wyjściem (role operacyjne).</summary>
        LockerOut,
        /// <summary>Wizyta w łazience (mebel Sanitary).</summary>
        Bathroom,
        /// <summary>Przerwa/posiłek (mebel SeatingRest/SeatingMeal/Kitchen w Social).</summary>
        Break,
    }

    /// <summary>TD-034: pojedyncza zaplanowana czynność osobista (rodzaj + absolutny game-time wymagalności).</summary>
    public struct PersonalActivity
    {
        public PersonalActivityKind kind;
        public long dueAt; // absolutny game-time (s) kiedy czynność powinna się odpalić
    }

    /// <summary>
    /// TD-034 seam: dostawca czynności osobistych. Workflowy pytają „czy pracownik powinien teraz
    /// pójść do łazienki / na przerwę / przebrać się?". Na teraz: <see cref="ScheduledNeedProvider"/>
    /// (deterministyczny harmonogram). W przyszłości wymienialny na SimulatedNeedProvider
    /// (liczniki bladder/hunger/intra-shift fatigue) BEZ ruszania workflowów — patrz decyzja TD-034 #1.
    /// </summary>
    public interface IPersonalNeedProvider
    {
        /// <summary>
        /// Zwraca czynność „due teraz" dla pracownika (dueAt &lt;= currentGameTime, jeszcze nie skonsumowana),
        /// lub null gdy żadna nie jest wymagana w tej chwili. LockerIn/Bathroom/Break w trakcie zmiany;
        /// LockerOut wstrzykiwany osobno przy końcu zmiany (nie z tej metody).
        /// </summary>
        PersonalActivity? GetDueActivity(Employee e, long currentGameTime);
    }
}
