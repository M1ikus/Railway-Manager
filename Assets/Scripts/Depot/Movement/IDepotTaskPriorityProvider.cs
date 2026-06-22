namespace DepotSystem
{
    /// <summary>
    /// M8-11 / D33: Hook dla Personnel TrafficController (dyzurny ruchu) aby priorytetyzowal
    /// manewry w <see cref="DepotMovementSimulator"/>.
    ///
    /// Implementacja w Personnel assembly (<c>RailwayManager.Personnel.TrafficControlService</c>).
    /// Bez providera: simulator dziala default FCFS + random delay (backward compat).
    ///
    /// Minimalny interface — tylko compute + admit check. DepotMovementSimulator wywoluje
    /// <see cref="ComputePriority"/> przy enqueue nowego taska (priorytet 0-100).
    /// </summary>
    public interface IDepotTaskPriorityProvider
    {
        /// <summary>Oblicza priorytet (0-100) dla taska. Wyzszy = wczesniejsze wykonanie.</summary>
        int ComputePriority(DepotMoveTask task);

        /// <summary>Czy simulator moze admitowac nowy task (false gdy over-capacity dyspozytora).</summary>
        bool CanAdmitNewTask();
    }
}
