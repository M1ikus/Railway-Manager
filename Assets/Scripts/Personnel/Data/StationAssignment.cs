using System;

namespace RailwayManager.Personnel
{
    /// <summary>
    /// M8-1: Przypisanie kasjera (<see cref="EmployeeRole.TicketClerk"/>) do stacji.
    /// Jedna stacja = max 1 obsadzony kasjer per shift (EA uproszczenie).
    /// Revenue bonus: +8% z tej stacji gdy obsadzona (D19 — widocznosc tylko w popupie stacji).
    /// </summary>
    [Serializable]
    public class StationAssignment
    {
        /// <summary>ID stacji OSM (z <see cref="Timetable.StationMarker"/> / Places).</summary>
        public int stationId;

        /// <summary>ID pracownika (rola TicketClerk).</summary>
        public int employeeId;

        /// <summary>Data przypisania (ISO).</summary>
        public string assignedSinceDateIso;

        /// <summary>Zmiana kasjera na tej stacji (Morning/Afternoon/Night).</summary>
        public ShiftType shift = ShiftType.Morning;
    }
}
