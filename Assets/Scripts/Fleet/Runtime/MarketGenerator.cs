using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using RailwayManager.Core;

namespace RailwayManager.Fleet
{
    /// <summary>
    /// M-Fleet-3: Generator rynku wtórnego.
    ///
    /// Strategia: "clone + perturb" — losuje pozycje z puli templatów
    /// (`FleetCatalog.InitialMarket` traktowane jako templates) i tworzy z nich
    /// nowe pojazdy z losowym rocznikiem/stanem/miejscem/ceną.
    ///
    /// Wywoływane przez <see cref="FleetMarketRefreshService"/> co
    /// <see cref="FleetBalanceConstants.MarketRefreshIntervalDays"/> dni gry.
    /// Usuwa N najstarszych niekupionych + dodaje M losowych nowych.
    ///
    /// M-Fleet-4: docelowo osobny `used_templates.json` z parametrami puli.
    /// Teraz MVP używa initial_market jako seed + templates jednocześnie.
    /// </summary>
    public static class MarketGenerator
    {
        [Serializable] private class LocationsWrapper
        {
            public int schemaFormatVersion = 0;
            public List<string> locations = new();
        }

        static string[] _usedLocationsCache;

        /// <summary>Miejsca odbioru używanych pojazdów — ładowane lazy z market_locations.json.</summary>
        static string[] UsedLocations
        {
            get
            {
                if (_usedLocationsCache != null) return _usedLocationsCache;
                _usedLocationsCache = LoadUsedLocations();
                return _usedLocationsCache;
            }
        }

        static string[] LoadUsedLocations()
        {
            string path = Path.Combine(AppPaths.FleetCatalogDir, "market_locations.json");
            try
            {
                if (File.Exists(path))
                {
                    var parsed = JsonUtility.FromJson<LocationsWrapper>(File.ReadAllText(path));
                    if (parsed?.locations != null && parsed.locations.Count > 0)
                        return parsed.locations.ToArray();
                }
                Log.Warn($"[MarketGenerator] {path} brak/pusty — fallback default 1 lokacja");
            }
            catch (Exception e)
            {
                Log.Error($"[MarketGenerator] LoadUsedLocations failed: {e.Message}");
            }
            return new[] { "Nieznana lokalizacja" };
        }

        /// <summary>Test/diagnostyka: wymuś re-load market_locations.json.</summary>
        public static void InvalidateCache() => _usedLocationsCache = null;

        /// <summary>
        /// Wykonuje refresh rynku: usuwa <paramref name="removeOldest"/> najstarszych niekupionych
        /// + dodaje <paramref name="addCount"/> nowych losowych.
        /// Cap: <see cref="FleetBalanceConstants.MarketMaxSize"/>.
        ///
        /// <paramref name="allMarketOrig"/> zmodyfikowana in-place (target: FleetCatalog.InitialMarket lub runtime market list).
        /// </summary>
        public static void Refresh(
            List<FleetMarketVehicle> market,
            IReadOnlyList<FleetMarketVehicle> templatePool,
            long nowGameTime,
            int removeOldest = 3,
            int addCount = 5,
            int cap = 20)
        {
            if (market == null) return;
            // C5: deterministic RNG per system (MP-9) — Refresh ten sam seed + game time
            // daje powtarzalny rynek, save/load reprodukuje. UnityEngine.Random oraz
            // System.Random były niedeterministyczne.
            var rng = RandomRegistry.GetRng("MarketGenerator");

            // 1. Remove N najstarszych (po addedGameTime; 0 = seed traktowany jak najstarsze)
            int removed = 0;
            // Sortuj w kopii żeby znaleźć najstarsze
            var sortedByAge = new List<FleetMarketVehicle>(market);
            sortedByAge.Sort((a, b) => a.addedGameTime.CompareTo(b.addedGameTime));
            for (int i = 0; i < removeOldest && i < sortedByAge.Count; i++)
            {
                market.Remove(sortedByAge[i]);
                removed++;
            }

            // 2. Add M nowych (respektując cap)
            int room = cap - market.Count;
            int toAdd = Mathf.Min(addCount, room);
            int added = 0;
            int nextId = GetNextId(market);

            if (templatePool == null || templatePool.Count == 0)
            {
                Log.Warn("[MarketGenerator] templatePool empty — brak rozszerzenia rynku");
                return;
            }

            for (int i = 0; i < toAdd; i++)
            {
                var template = templatePool[rng.Range(0, templatePool.Count)];
                var perturbed = PerturbClone(template, nextId++, nowGameTime, rng);
                market.Add(perturbed);
                added++;
            }

            Log.Info($"[MarketGenerator] Refresh: removed {removed}, added {added}, market size={market.Count}");
        }

        /// <summary>
        /// Klonuje template + perturb'uje: losowy rocznik (±5 lat z zakresu inProductionFrom-To),
        /// losowy mileage (proporcjonalny do wieku), losowy condition (±20%), losowy price (±30% z base).
        /// </summary>
        static FleetMarketVehicle PerturbClone(FleetMarketVehicle tmpl, int newId, long nowGameTime, DeterministicRng rng)
        {
            int year = tmpl.productionYear;
            // Jeśli template ma sensowny rocznik, zostaw ±5 lat. W przeciwnym razie domyślnie.
            if (year > 1900) year = year + rng.Range(-5, 6);

            int ageYears = Mathf.Max(0, (int)(nowGameTime / 31536000L) - (year - 1900));
            float baseMileage = tmpl.mileageKm > 0 ? tmpl.mileageKm : 100000f * ageYears;
            float mileagePerturb = (float)(rng.NextDouble() * 0.6 + 0.7); // 0.7-1.3
            float newMileage = baseMileage * mileagePerturb;

            float baseCondition = tmpl.conditionPercent > 0 ? tmpl.conditionPercent : 60f;
            float conditionPerturb = (float)(rng.NextDouble() * 40 - 20); // ±20
            float newCondition = Mathf.Clamp(baseCondition + conditionPerturb, 15f, 95f);

            long basePrice = tmpl.price > 0 ? tmpl.price : 1_000_000L;
            // Cena skalowana stanem: kondycja 15% = 0.3x, 95% = 1.3x
            float priceFactor = 0.3f + (newCondition - 15f) / 80f * 1.0f;
            long newPrice = (long)(basePrice * priceFactor);

            string newLocation = UsedLocations[rng.Range(0, UsedLocations.Length)];
            string newNumber = string.IsNullOrEmpty(tmpl.series)
                ? $"ID-{newId:D4}"
                : $"{tmpl.series}-{rng.Range(1000, 9999):D4}";

            return new FleetMarketVehicle
            {
                id = newId,
                seriesId = tmpl.seriesId,
                series = tmpl.series,
                family = tmpl.family,
                country = tmpl.country,
                number = newNumber,
                evn = EvnGenerator.Generate(tmpl.type, newId),
                type = tmpl.type,
                supportedTractions = CloneList(tmpl.supportedTractions),
                productionYear = year,
                cleanlinessPercent = Mathf.Clamp(newCondition + rng.Range(-10, 11), 20f, 100f),
                mileageKm = newMileage,
                conditionPercent = newCondition,
                passengerSeats = tmpl.passengerSeats,
                seatBreakdown = CloneSeats(tmpl.seatBreakdown),
                maxSpeedKmh = tmpl.maxSpeedKmh,
                powerKw = tmpl.powerKw,
                wheelbase = tmpl.wheelbase,
                coachCount = tmpl.coachCount,
                maxCoachesInTrain = tmpl.maxCoachesInTrain,
                accelerationMps2 = tmpl.accelerationMps2,
                decelerationMps2 = tmpl.decelerationMps2,
                lengthM = tmpl.lengthM,
                emptyMassTons = tmpl.emptyMassTons,
                maxLoadedMassTons = tmpl.maxLoadedMassTons,
                brakingMassTons = tmpl.brakingMassTons,
                brakeRegime = tmpl.brakeRegime,
                price = newPrice,
                operationalCostPerKmGroszy = tmpl.operationalCostPerKmGroszy,
                fuelTankCapacityLitres = tmpl.fuelTankCapacityLitres,
                fuelConsumptionLper100km = tmpl.fuelConsumptionLper100km,
                location = newLocation,
                safetySystemsInstalled = CloneList(tmpl.safetySystemsInstalled),
                voltages = CloneList(tmpl.voltages),
                comfortFeatures = CloneList(tmpl.comfortFeatures),
                comfortClass = tmpl.comfortClass,
                paintScheme = tmpl.paintScheme,
                reliabilityScore = Mathf.Clamp(tmpl.reliabilityScore + rng.Range(-10, 11), 20, 80),
                breakdownRiskFactor = tmpl.breakdownRiskFactor,
                maintenanceCostFactor = tmpl.maintenanceCostFactor,
                minPlatformLengthM = tmpl.minPlatformLengthM,
                requiresMaintenanceCapabilities = CloneList(tmpl.requiresMaintenanceCapabilities),
                status = tmpl.status,
                suggestedCategoryGroups = CloneList(tmpl.suggestedCategoryGroups),
                canBePulledByDiesel = tmpl.canBePulledByDiesel,
                isShuntingLocomotive = tmpl.isShuntingLocomotive,
                historicalFactoid = tmpl.historicalFactoid,
                addedGameTime = nowGameTime,
                // Inspekcje — regenerowane przez Reconstruct w loader'ze
                inspections = new InspectionSchedule(),
                ins_kmSinceP3 = Mathf.Max(0, newMileage * 0.3f),
                ins_kmSinceP4 = Mathf.Max(0, newMileage * 0.6f),
                ins_yearsSinceP4 = Mathf.Max(0, ageYears * 0.5f),
                ins_kmSinceP5 = newMileage,
                ins_yearsSinceP5 = ageYears,
                // M-FC-8: deterministic paintSeed (każdy refresh dostaje nowy seed)
                paintSeed = rng.Range(1, int.MaxValue),
            };
        }

        static List<T> CloneList<T>(List<T> src) => src != null ? new List<T>(src) : new List<T>();

        static List<SeatCount> CloneSeats(List<SeatCount> src)
        {
            if (src == null) return new List<SeatCount>();
            var r = new List<SeatCount>(src.Count);
            foreach (var s in src) r.Add(new SeatCount { type = s.type, count = s.count });
            return r;
        }

        /// <summary>
        /// Start auto-generowanych id pojazdów rynku. initial_market.json używa zakresu
        /// 100-999 (15 itemów EA, headroom dla DLC), generated odpowiada od 1000.
        /// </summary>
        const int GeneratedMarketIdStart = 1000;

        static int GetNextId(List<FleetMarketVehicle> market)
        {
            int max = GeneratedMarketIdStart;
            foreach (var m in market) if (m.id > max) max = m.id;
            return max + 1;
        }
    }
}
