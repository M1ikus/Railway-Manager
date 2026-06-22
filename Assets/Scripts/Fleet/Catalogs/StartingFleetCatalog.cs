using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using RailwayManager.Core;

namespace RailwayManager.Fleet
{
    /// <summary>
    /// A2: Katalog startowego taboru gracza — wczytuje
    /// <c>StreamingAssets/Fleet/starting_fleet.json</c>.
    ///
    /// Wcześniej był to hardcoded 180-liniowy mock w <see cref="FleetService.LoadStartingFleet"/>
    /// z komentarzem TODO „docelowo z save game". Refactor zamyka 2-letni dług i pozwala
    /// dorzucić preset'y per kraj DLC (każdy DLC może mieć własny starting_fleet).
    ///
    /// JSON używa string fields dla enum (typeName/statusName/brakeRegimeName/...) — czytelność
    /// dla potencjalnego editora plus walidacja przy load (literówka łapie się parserem zamiast
    /// cichego 0).
    /// </summary>
    public static class StartingFleetCatalog
    {
        [Serializable] public class Dto
        {
            public int schemaFormatVersion = 0;
            public List<StartingVehicleDto> vehicles = new();
            public List<StartingConsistDto> consists = new();
        }

        [Serializable] public class StartingVehicleDto
        {
            public int id;
            public string series;
            public string number;
            public string typeName;        // FleetVehicleType
            public string statusName;      // FleetVehicleStatus
            public string currentTask;
            public string assignedConsist;
            public int productionYear;
            public string paintScheme;
            public float mileageKm;
            public float conditionPercent;
            public int maxSpeedKmh;
            public int powerKw;
            public string wheelbase;
            public int passengerSeats;
            public float lengthM;
            public float emptyMassTons;
            public float maxLoadedMassTons;
            public float brakingMassTons;
            public string brakeRegimeName;
            public List<SeatCountDto> seatBreakdown = new();
            public List<string> safetySystemsInstalled = new();
            public List<string> voltages = new();
            public List<string> supportedTractionsNames = new();
        }

        [Serializable] public class SeatCountDto
        {
            public string typeName;        // SeatZoneType
            public int count;
        }

        [Serializable] public class StartingConsistDto
        {
            public string name;
            public List<int> vehicleIds = new();
            public string route;
            public string statusName;      // FleetVehicleStatus
        }

        /// <summary>Wczytuje JSON ze StreamingAssets. Zwraca null gdy plik brak / błąd parse.</summary>
        public static Dto Load()
        {
            string path = Path.Combine(AppPaths.FleetCatalogDir, "starting_fleet.json");
            try
            {
                if (!File.Exists(path))
                {
                    Log.Error($"[StartingFleetCatalog] File not found: {path}");
                    return null;
                }
                var parsed = JsonUtility.FromJson<Dto>(File.ReadAllText(path));
                if (parsed == null)
                {
                    Log.Error($"[StartingFleetCatalog] Parse failed: {path}");
                    return null;
                }
                Log.Info($"[StartingFleetCatalog] Loaded: {parsed.vehicles?.Count ?? 0} vehicles + {parsed.consists?.Count ?? 0} consists");
                return parsed;
            }
            catch (Exception e)
            {
                Log.Error($"[StartingFleetCatalog] Load failed: {e.Message}");
                return null;
            }
        }

        /// <summary>Map DTO → FleetVehicleData (bez pól derived: evn/components/inspections/cost/position).</summary>
        public static FleetVehicleData ToVehicle(StartingVehicleDto dto)
        {
            var v = new FleetVehicleData
            {
                id = dto.id,
                series = dto.series,
                number = dto.number,
                type = ParseEnum<FleetVehicleType>(dto.typeName),
                status = ParseEnum<FleetVehicleStatus>(dto.statusName),
                currentTask = string.IsNullOrEmpty(dto.currentTask) ? null : dto.currentTask,
                assignedConsist = string.IsNullOrEmpty(dto.assignedConsist) ? null : dto.assignedConsist,
                productionYear = dto.productionYear,
                paintScheme = dto.paintScheme,
                mileageKm = dto.mileageKm,
                conditionPercent = dto.conditionPercent,
                maxSpeedKmh = dto.maxSpeedKmh,
                powerKw = dto.powerKw,
                wheelbase = dto.wheelbase,
                passengerSeats = dto.passengerSeats,
                lengthM = dto.lengthM,
                emptyMassTons = dto.emptyMassTons,
                maxLoadedMassTons = dto.maxLoadedMassTons,
                brakingMassTons = dto.brakingMassTons,
                brakeRegime = ParseEnum<BrakeRegime>(dto.brakeRegimeName),
                seatBreakdown = ToSeatBreakdown(dto.seatBreakdown),
                safetySystemsInstalled = dto.safetySystemsInstalled != null ? new List<string>(dto.safetySystemsInstalled) : new List<string>(),
                voltages = dto.voltages != null ? new List<string>(dto.voltages) : new List<string>(),
                supportedTractions = ToTractions(dto.supportedTractionsNames),
            };
            return v;
        }

        public static FleetConsistData ToConsist(StartingConsistDto dto)
        {
            return new FleetConsistData
            {
                name = dto.name,
                vehicleIds = dto.vehicleIds != null ? new List<int>(dto.vehicleIds) : new List<int>(),
                route = dto.route,
                status = ParseEnum<FleetVehicleStatus>(dto.statusName),
            };
        }

        static List<SeatCount> ToSeatBreakdown(List<SeatCountDto> src)
        {
            var result = new List<SeatCount>();
            if (src == null) return result;
            foreach (var s in src)
                result.Add(new SeatCount { type = ParseEnum<SeatZoneType>(s.typeName), count = s.count });
            return result;
        }

        static List<TractionType> ToTractions(List<string> src)
        {
            var result = new List<TractionType>();
            if (src == null) return result;
            foreach (var s in src)
                if (!string.IsNullOrEmpty(s)) result.Add(ParseEnum<TractionType>(s));
            return result;
        }

        static T ParseEnum<T>(string value) where T : struct, Enum
        {
            if (string.IsNullOrEmpty(value)) return default;
            if (Enum.TryParse<T>(value, false, out var result)) return result;
            Log.Warn($"[StartingFleetCatalog] Unknown {typeof(T).Name}: '{value}' — fallback {default(T)}");
            return default;
        }
    }
}
