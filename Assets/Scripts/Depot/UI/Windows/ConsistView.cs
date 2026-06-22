using System.Collections.Generic;
using RailwayManager.Fleet;

namespace DepotSystem
{
    /// <summary>
    /// M-Windows P3: lekka abstrakcja „składu" — odpina okno od TRZECH rozjechanych modeli składu
    /// (logiczny <see cref="FleetConsistData"/> / runtime <see cref="ConsistMarker"/> /
    /// on-route <c>TrainRun.runningVehicleIds</c>). Każda płaszczyzna produkuje ConsistView adapterem;
    /// okno (<see cref="ConsistWindowUI"/>) zna TYLKO ConsistView → niezależne od przyszłej przebudowy
    /// modelu składu.
    ///
    /// <para>Adapter z <c>TrainRun</c> NIE jest tu (Depot nie widzi asmdef Timetable) — Timetable
    /// buduje ConsistView publicznym konstruktorem z prymitywów przy wpięciu Mapy.</para>
    /// </summary>
    public sealed class ConsistView
    {
        public readonly string Key;                 // klucz dedupe okna
        public readonly string Title;               // tytuł okna
        public readonly string Context;             // trasa/relacja/lokalizacja
        public readonly string Status;              // status słownie
        public readonly IReadOnlyList<int> VehicleIds;

        public ConsistView(string key, string title, string context, string status, IReadOnlyList<int> vehicleIds)
        {
            Key = key;
            Title = title;
            Context = context;
            Status = status;
            VehicleIds = vehicleIds ?? new List<int>();
        }

        /// <summary>Adapter: logiczny skład Fleet (zakładka „Składy").</summary>
        public static ConsistView FromFleetConsist(FleetConsistData c)
        {
            if (c == null) return new ConsistView("consist:fleet:null", "Skład", "—", "—", null);
            return new ConsistView(
                "consist:fleet:" + c.name,
                string.IsNullOrEmpty(c.name) ? "Skład" : c.name,
                string.IsNullOrEmpty(c.route) ? "—" : c.route,
                StatusText(c.status),
                c.vehicleIds);
        }

        /// <summary>Adapter: runtime skład w zajezdni 3D (ConsistMarker).</summary>
        public static ConsistView FromConsistMarker(ConsistMarker m)
        {
            if (m == null) return new ConsistView("consist:depot:null", "Skład", "—", "—", null);
            bool moving = DepotMovementSimulator.Instance != null
                && DepotMovementSimulator.Instance.HasTaskForConsist(m.consistId);
            return new ConsistView(
                "consist:depot:" + m.consistId,
                "Skład #" + m.consistId,
                m.currentTrackId >= 0 ? ("Zajezdnia · tor " + m.currentTrackId) : "Zajezdnia",
                moving ? "Manewruje" : "Stoi",
                m.vehicleIds);
        }

        static string StatusText(FleetVehicleStatus s) => s switch
        {
            FleetVehicleStatus.MovingOnMap => "W trasie",
            FleetVehicleStatus.StoppedOnMap => "Postój (mapa)",
            FleetVehicleStatus.StoppedInDepot => "W zajezdni",
            FleetVehicleStatus.MovingInDepot => "Manewruje",
            FleetVehicleStatus.InRepair => "W naprawie",
            FleetVehicleStatus.OutOfService => "Wycofany",
            FleetVehicleStatus.InProduction => "W produkcji",
            FleetVehicleStatus.InTransit => "W dostawie",
            FleetVehicleStatus.AwaitingPickup => "Oczekuje odbioru",
            _ => s.ToString()
        };
    }
}
