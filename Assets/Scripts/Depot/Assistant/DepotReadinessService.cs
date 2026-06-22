using UnityEngine;
using RailwayManager.Fleet;
using DepotSystem.OutdoorEquipment;

namespace DepotSystem.Assistant
{
    /// <summary>Migawka stanu zajezdni dla monitora asystenta (spec: „Introspekcja stanu zajezdni").</summary>
    public struct DepotReadiness
    {
        /// <summary>Czy kiedykolwiek policzono na żywej scenie Depot. False = stan nieznany
        /// (przed pierwszym wejściem do zajezdni) — reguły NIE strzelają na nieznanym stanie.</summary>
        public bool evaluated;

        public bool hasAnyTrack;
        public bool hasElectrifiedTrack;
        public bool hasHall;
        public bool hasFuelStation;

        /// <summary>Flota ma EMU / elektrowóz — potrzebuje sieci nad torami zajezdni.</summary>
        public bool fleetNeedsCatenary;

        /// <summary>Flota ma DMU / spalinowóz — przyda się stacja paliw.</summary>
        public bool fleetNeedsFuel;

        /// <summary>Pułapka nowego gracza: pojazd elektryczny + zajezdnia bez sieci = nie wyjedzie.</summary>
        public bool EmuTrapActive => evaluated && fleetNeedsCatenary && !hasElectrifiedTrack;
    }

    /// <summary>
    /// M11 AS-3: introspekcja stanu zajezdni — monitor NIE liczy binarnie, tylko czyta
    /// realny stan przez istniejące systemy (TrackGraph.HasCatenary per tor /
    /// RoomDetectionSystem.Rooms / OutdoorEquipmentPlacer.Placed / FleetService).
    ///
    /// Scene-safe: systemy Depot żyją tylko w scenie Depot — poza nią (MapScene) serwis
    /// trzyma LAST-KNOWN wartości (gracz przełącza sceny, diagnoza nie znika). TTL cache 1s
    /// (monitor tick'uje co 1s — bez podwójnej roboty).
    ///
    /// Uwaga preset: `DepotManager.generateDefaultLayout` tworzy tory na starcie — wtedy
    /// hasAnyTrack=true od pierwszej klatki i krok onboardingu „zbuduj tor" jest naturalnie
    /// pominięty (dokładnie zachowanie preset-aware ze specu).
    /// </summary>
    public static class DepotReadinessService
    {
        private const float CacheTtlRealSec = 1f;

        private static DepotReadiness _cached;
        private static double _lastEvalRealSec = double.MinValue;

        public static DepotReadiness Current
        {
            get
            {
                Refresh();
                return _cached;
            }
        }

        /// <summary>Wymusza przeliczenie przy następnym odczycie (np. po load save).</summary>
        public static void Invalidate() => _lastEvalRealSec = double.MinValue;

        private static void Refresh()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            if (now - _lastEvalRealSec < CacheTtlRealSec) return;

            var graph = DepotServices.Get<TrackGraph>();
            if (graph == null)
            {
                // Poza sceną Depot — nie nadpisuj last-known, spróbuj znów za TTL.
                _lastEvalRealSec = now;
                return;
            }
            _lastEvalRealSec = now;

            var r = new DepotReadiness { evaluated = true };

            foreach (var track in graph.Tracks.Values)
            {
                if (track == null) continue;
                r.hasAnyTrack = true;
                if (track.HasCatenary) r.hasElectrifiedTrack = true;
                if (r.hasElectrifiedTrack) break; // oba fakty ustalone
            }

            var rooms = DepotServices.Get<RoomDetectionSystem>();
            if (rooms != null)
            {
                foreach (var room in rooms.Rooms)
                {
                    if (room != null && room.roomType == RoomType.Hall) { r.hasHall = true; break; }
                }
            }

            var outdoor = DepotServices.Get<OutdoorEquipmentPlacer>();
            if (outdoor != null)
            {
                foreach (var placed in outdoor.Placed)
                {
                    if (placed != null && placed.type == OutdoorEquipmentType.FuelStation)
                    {
                        r.hasFuelStation = true;
                        break;
                    }
                }
            }

            foreach (var v in FleetService.OwnedVehicles)
            {
                if (v == null) continue;
                if (v.type == FleetVehicleType.EMU || v.type == FleetVehicleType.ElectricLocomotive)
                    r.fleetNeedsCatenary = true;
                if (v.type == FleetVehicleType.DMU || v.type == FleetVehicleType.DieselLocomotive)
                    r.fleetNeedsFuel = true;
                if (r.fleetNeedsCatenary && r.fleetNeedsFuel) break;
            }

            _cached = r;
        }
    }
}
