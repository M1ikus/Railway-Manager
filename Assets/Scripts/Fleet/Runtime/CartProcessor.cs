using System.Collections.Generic;
using System.Linq;
using RailwayManager.Core;
using RailwayManager.Economy;
using UnityEngine;

namespace RailwayManager.Fleet
{
    /// <summary>
    /// Przetwarzanie koszyka — konwersja CartItem → FleetVehicleData
    /// i dodawanie do FleetService.OwnedVehicles.
    /// </summary>
    public static class CartProcessor
    {
        // BUG-052 fix: dynamic z GameState.CurrentDateIso zamiast hardcoded 2024.
        // Wcześniej wszystkie nowe pojazdy po latach gameplay zostawały z productionYear=2024
        // → MaintenanceCostCalculator liczył age=0 (free utrzymanie). Cache per-call żeby
        // nie parsować daty wielokrotnie.
        private static int CurrentGameYear()
        {
            try
            {
                return IsoTime.ParseDate(GameState.CurrentDateIso).Year;
            }
            catch
            {
                return 2024; // Defensive fallback przy edge case empty dateIso
            }
        }

        // Szacowany czas produkcji nowego pojazdu (sekundy gry)
        private const long PRODUCTION_TIME_SECONDS = 30 * 24 * 3600;   // 30 dni
        // Szacowany czas dostawy uzywanego (sekundy gry)
        private const long DELIVERY_TIME_SECONDS   = 5 * 24 * 3600;    // 5 dni

        /// <summary>
        /// M-Economy Faza 3: checkout koszyka — sprawdza czy STAĆ, jeśli tak: tworzy pojazdy
        /// (<see cref="ProcessOrder"/>) + POBIERA kasę przez MoneyLedger (kategoria "vehicle_purchase",
        /// trafia do bilansu dziennego FinancePanelUI). Polityka „nie stać → nie kupuj" (decyzja user'a):
        /// blokuje CAŁY koszyk (all-or-nothing), nie kupuje częściowo, nie idzie w minus.
        ///
        /// Naprawia bug: wcześniej zakup taboru NIE odejmował pieniędzy (checkout tylko logował sumę).
        /// </summary>
        public static CheckoutResult TryCheckout(IReadOnlyList<CartItem> cart)
        {
            long totalZl = CartTotalZl(cart);
            long totalGroszy = totalZl * 100L;

            if (!MoneyLedger.CanAfford(totalGroszy))
            {
                Log.Info($"[CartProcessor] Zakup zablokowany — brak srodkow: potrzeba {totalZl:N0} zl, mamy {GameState.Money:N0} zl");
                return new CheckoutResult { InsufficientFunds = true, TotalZl = totalZl };
            }

            int added = ProcessOrder(cart);
            long charged = 0;
            if (added > 0)
            {
                MoneyLedger.Spend(totalGroszy, "vehicle_purchase", $"Zakup {added} pojazdow");
                charged = totalGroszy;
            }
            return new CheckoutResult { Success = true, Added = added, TotalZl = totalZl, ChargedGroszy = charged };
        }

        /// <summary>Suma cen koszyka [zł] (Σ TotalPrice — zawiera standardową dostawę z koszyka).</summary>
        public static long CartTotalZl(IReadOnlyList<CartItem> cart)
        {
            long t = 0;
            if (cart != null)
                foreach (var i in cart) if (i != null) t += i.TotalPrice;
            return t;
        }

        /// <summary>
        /// Przetwarza pozycje z koszyka, dodaje nowe pojazdy do OwnedVehicles
        /// z odpowiednimi statusami (W produkcji / W dostawie / Oczekuje na odbior).
        /// Zwraca ilość dodanych pojazdów. UWAGA: NIE pobiera pieniędzy — użyj <see cref="TryCheckout"/>
        /// dla pełnego zakupu (affordability + płatność). Bezpośrednie wywołanie tylko gdy płatność
        /// obsłużona osobno (np. testy, grant startowy).
        /// </summary>
        public static int ProcessOrder(IReadOnlyList<CartItem> cart)
        {
            int added = 0;
            int nextOwnedId = FleetService.OwnedVehicles.Count > 0
                ? FleetService.OwnedVehicles.Max(v => v.id) + 1 : 1;
            var rng = new System.Random();
            // Crash-hunt (krytyczny): absolutny czas, NIE goły GameTimeSeconds (wewnątrz-dobowy max 86400).
            // estimatedCompletionGameTime = nowGameTime + 30 dni — z gołym GameTimeSeconds target ~2.6M
            // nigdy nieosiągalny (resetuje się o północy) → produkcja NIGDY się nie kończy. To samo
            // przeglądy (InspectionSchedule liczy deltę now-lastP). Absolutny czas naprawia oba.
            long nowGameTime = GameState.TotalGameSeconds;

            foreach (var item in cart)
            {
                // M-FC-2: Wagon configurator items (vehicleConfiguration != null)
                if (item.isNewVehicle && item.vehicleConfiguration != null)
                {
                    for (int i = 0; i < item.quantity; i++)
                    {
                        int newId = nextOwnedId++;
                        var v = BuildVehicleFromConfiguration(item, newId, nowGameTime, rng);
                        if (v != null)
                        {
                            FleetService.AddOwnedVehicle(v);
                            added++;
                        }
                    }
                    continue;
                }

                // Legacy NewVehicleModel flow usuniety (M-UIPolish 2026-06-18) — nowy tabor idzie
                // wylacznie przez vehicleConfiguration (obsluzone wyzej). Pozycja isNewVehicle bez
                // konfiguracji = malformed (np. edytowany save) -> pomijamy zamiast NRE na marketVehicle.
                if (item.isNewVehicle)
                {
                    Log.Warn("[CartProcessor] Pominieto nowy pojazd bez konfiguracji (malformed cart item).");
                    continue;
                }

                // Uzywany tabor (rynek wtorny)
                {
                    var mv = item.marketVehicle;
                    int newId = nextOwnedId++;

                    var v = new FleetVehicleData
                    {
                        id = newId,
                        // M-Fleet-1: nowe pola identyfikacji
                        seriesId = mv.seriesId,
                        family = mv.family,
                        country = string.IsNullOrEmpty(mv.country) ? "PL" : mv.country,
                        series = mv.series,
                        number = mv.number,
                        evn = !string.IsNullOrEmpty(mv.evn) ? mv.evn : EvnGenerator.Generate(mv.type, newId),
                        type = mv.type,
                        // M9c-D: u\u017cywany od razu gotowy do odbioru; dostaw\u0119 gracz wybiera w punkcie zakupu na mapie
                        status = FleetVehicleStatus.AwaitingPickup,
                        currentTask = $"Oczekuje na odbi\u00f3r: {mv.location}",
                        assignedConsist = null,
                        // M-Fleet-1: trakcja
                        supportedTractions = mv.supportedTractions != null
                            ? new List<TractionType>(mv.supportedTractions) : new List<TractionType>(),
                        maxSpeedKmh = mv.maxSpeedKmh,
                        powerKw = mv.powerKw,
                        wheelbase = mv.wheelbase,
                        passengerSeats = mv.passengerSeats,
                        seatBreakdown = CopyBreakdown(mv.seatBreakdown),
                        // M-Fleet-1: nowe pola techniczne
                        coachCount = mv.coachCount,
                        maxCoachesInTrain = mv.maxCoachesInTrain,
                        accelerationMps2 = mv.accelerationMps2 > 0 ? mv.accelerationMps2 : FleetBalanceConstants.DefaultAccelerationMps2,
                        decelerationMps2 = mv.decelerationMps2 > 0 ? mv.decelerationMps2 : FleetBalanceConstants.DefaultDecelerationMps2,
                        lengthM = mv.lengthM,
                        emptyMassTons = mv.emptyMassTons,
                        maxLoadedMassTons = mv.maxLoadedMassTons,
                        brakingMassTons = mv.brakingMassTons,
                        brakeRegime = mv.brakeRegime,
                        safetySystemsInstalled = mv.safetySystemsInstalled != null
                            ? new List<string>(mv.safetySystemsInstalled) : new List<string>(),
                        voltages = mv.voltages != null
                            ? new List<string>(mv.voltages) : new List<string>(),
                        comfortFeatures = mv.comfortFeatures != null
                            ? new List<string>(mv.comfortFeatures) : new List<string>(),
                        comfortClass = mv.comfortClass > 0 ? mv.comfortClass : FleetBalanceConstants.DefaultComfortClassBasic,
                        paintScheme = mv.paintScheme,
                        // M-FC-8: kopiuj resolved livery z paintSeed → FleetVehicleData.paintDefinition
                        paintDefinition = mv.GetOrResolvePaint(),
                        mileageKm = mv.mileageKm,
                        conditionPercent = mv.conditionPercent,
                        cleanlinessPercent = mv.cleanlinessPercent,
                        components = VehicleComponents.New(mv.conditionPercent),
                        productionYear = mv.productionYear > 0 ? mv.productionYear : (CurrentGameYear() - 20),
                        purchaseGameTime = nowGameTime,
                        inspections = CopyInspections(mv.inspections, nowGameTime),
                        estimatedCompletionGameTime = nowGameTime,
                        position = new VehiclePosition
                        {
                            kind = VehicleLocationKind.None,
                            externalLocation = mv.location
                        },
                        // M-Fleet-1: ekonomia
                        operationalCostPerKmGroszy = mv.operationalCostPerKmGroszy,
                        fuelTankCapacityLitres = mv.fuelTankCapacityLitres,
                        fuelConsumptionLper100km = mv.fuelConsumptionLper100km,
                        // M-Fleet-1: reliability
                        reliabilityScore = mv.reliabilityScore > 0 ? mv.reliabilityScore : FleetBalanceConstants.DefaultReliabilityScoreUsed,
                        breakdownRiskFactor = mv.breakdownRiskFactor > 0 ? mv.breakdownRiskFactor : FleetBalanceConstants.DefaultBreakdownRiskFactorUsed,
                        maintenanceCostFactor = mv.maintenanceCostFactor > 0 ? mv.maintenanceCostFactor : FleetBalanceConstants.DefaultMaintenanceCostFactorUsed,
                        // M7-2: component risk
                        componentRisk = CloneRisks(mv.componentRisk),
                        // M-Fleet-1: infrastruktura + gameplay
                        minPlatformLengthM = mv.minPlatformLengthM,
                        requiresMaintenanceCapabilities = mv.requiresMaintenanceCapabilities != null
                            ? new List<string>(mv.requiresMaintenanceCapabilities) : new List<string>(),
                        productionStatus = mv.status,
                        suggestedCategoryGroups = mv.suggestedCategoryGroups != null
                            ? new List<string>(mv.suggestedCategoryGroups) : new List<string>(),
                        canBePulledByDiesel = mv.canBePulledByDiesel,
                        isShuntingLocomotive = mv.isShuntingLocomotive,
                        historicalFactoid = mv.historicalFactoid
                    };

                    v.history.Add(new MaintenanceRecord
                    {
                        gameTimeSeconds = nowGameTime,
                        recordType = MaintenanceRecordTypes.PurchaseUsed,
                        description = $"Zakupiono z rynku wtornego w {mv.location}",
                        cost = item.unitPrice,
                        mileageAtRecord = mv.mileageKm
                    });

                    v.monthlyMaintenanceCost = MaintenanceCostCalculator.Calculate(v, CurrentGameYear());

                    FleetService.AddOwnedVehicle(v);
                    added++;
                }
            }

            if (added > 0)
                FleetService.NotifyOwnedChanged();
            return added;
        }

        /// <summary>
        /// Kopiuje harmonogram przegladow z market vehicle, przesuwajac czasy na aktualny moment gry.
        /// JSON ma last* wzgledem nowGameTime=0, wiec do kazdego czasu dodajemy obecny nowGameTime.
        /// </summary>
        private static InspectionSchedule CopyInspections(InspectionSchedule src, long nowGameTime)
        {
            if (src == null) return InspectionSchedule.CreateFresh(nowGameTime, 0f);
            return new InspectionSchedule
            {
                lastP1GameTime = src.lastP1GameTime + nowGameTime,
                lastP2GameTime = src.lastP2GameTime + nowGameTime,
                lastP3Mileage  = src.lastP3Mileage,
                lastP4GameTime = src.lastP4GameTime + nowGameTime,
                lastP4Mileage  = src.lastP4Mileage,
                lastP5GameTime = src.lastP5GameTime + nowGameTime,
                lastP5Mileage  = src.lastP5Mileage
            };
        }

        /// <summary>Głęboka kopia listy SeatCount, żeby każdy pojazd miał własną.</summary>
        private static List<SeatCount> CopyBreakdown(List<SeatCount> source)
        {
            var result = new List<SeatCount>();
            if (source == null) return result;
            foreach (var sc in source)
                result.Add(new SeatCount { type = sc.type, count = sc.count });
            return result;
        }

        /// <summary>M7-2: głęboka kopia ComponentRiskFactors (żeby pojazd miał własną).</summary>
        private static ComponentRiskFactors CloneRisks(ComponentRiskFactors src)
        {
            if (src == null) return new ComponentRiskFactors();
            return new ComponentRiskFactors
            {
                engine = src.engine, brake = src.brake, doors = src.doors, ac = src.ac,
                body = src.body, wheels = src.wheels, electrical = src.electrical,
                interior = src.interior, lights = src.lights, toilets = src.toilets,
                pantograph = src.pantograph, coupling = src.coupling
            };
        }

        /// <summary>
        /// M-FC-2/3: Dispatch dla nowego flow konfiguratora — rozróżnia wagon (z bodyTypeId)
        /// od rodziny FleetFamily (z familyId + variantKey). Każdy typ ma własną builder method.
        /// </summary>
        private static FleetVehicleData BuildVehicleFromConfiguration(
            CartItem item, int newId, long nowGameTime, System.Random rng)
        {
            var cfg = item.vehicleConfiguration;
            if (!string.IsNullOrEmpty(cfg.bodyTypeId))
                return BuildWagonFromConfig(item, newId, nowGameTime, rng);
            if (!string.IsNullOrEmpty(cfg.familyId))
                return BuildFamilyVariantFromConfig(item, newId, nowGameTime, rng);

            Log.Error("[CartProcessor] VehicleConfiguration has neither bodyTypeId nor familyId");
            return null;
        }

        /// <summary>
        /// M-FC-2: Buduje FleetVehicleData z wagon configuration (pudło + wózki + drzwi).
        /// </summary>
        private static FleetVehicleData BuildWagonFromConfig(
            CartItem item, int newId, long nowGameTime, System.Random rng)
        {
            var cfg = item.vehicleConfiguration;
            var bodyDef = FleetCatalog.FindWagonBody(cfg.bodyTypeId);
            var bogieDef = FleetCatalog.FindWagonBogie(cfg.bogieTypeId);

            if (bodyDef == null || bogieDef == null)
            {
                Log.Error(
                    $"[CartProcessor] Cannot build wagon: bodyDef={bodyDef?.id ?? "null"} ({cfg.bodyTypeId}), " +
                    $"bogieDef={bogieDef?.id ?? "null"} ({cfg.bogieTypeId})");
                return null;
            }

            // Vmax = min(body cap, bogie cap) — bo pełna swoboda mieszania (A1)
            int vMax = System.Math.Min(bodyDef.maxSpeedKmhCap, bogieDef.maxSpeedKmh);

            // M-FC-5: seats + breakdown z interiorMix (calculated by InteriorMixCalculator)
            int seats = cfg.calculatedSeats > 0
                ? cfg.calculatedSeats
                : InteriorMixCalculator.CalculateSeats(cfg.interiorMix, bodyDef.lengthM);
            var breakdown = BuildSeatBreakdownFromMix(cfg.interiorMix, bodyDef.lengthM, seats);

            float emptyMass = bodyDef.emptyMassTons + bogieDef.emptyMassTonsPair; // 1 pudło + 1 para wózków (2 wózki)
            float loadedMass = emptyMass + seats * 0.08f;                          // ~80kg per pasażer
            float brakingMass = loadedMass * 1.05f;                                // typical UIC factor

            string seriesLabel = $"Wagon {bodyDef.displayName}";
            string number = $"WAG-{rng.Next(10000, 99999)}";

            var v = new FleetVehicleData
            {
                id = newId,
                seriesId = "Coach_Configurable",
                family = "Coach",
                country = "PL",
                series = seriesLabel,
                number = number,
                evn = EvnGenerator.Generate(FleetVehicleType.PassengerCar, newId),
                type = FleetVehicleType.PassengerCar,
                status = FleetVehicleStatus.InProduction,
                currentTask = "Produkcja w fabryce wagonów",
                assignedConsist = null,

                supportedTractions = new List<TractionType> { TractionType.None },
                maxSpeedKmh = vMax,
                powerKw = 0,
                wheelbase = "2'2'",
                passengerSeats = seats,
                seatBreakdown = breakdown,
                coachCount = 1,
                maxCoachesInTrain = 0,
                accelerationMps2 = FleetBalanceConstants.DefaultAccelerationMps2,
                decelerationMps2 = FleetBalanceConstants.DefaultDecelerationMps2,

                lengthM = bodyDef.lengthM,
                emptyMassTons = emptyMass,
                maxLoadedMassTons = loadedMass,
                brakingMassTons = brakingMass,
                brakeRegime = bogieDef.brakeRegime,

                safetySystemsInstalled = new List<string> { "CA" },
                voltages = new List<string>(),
                comfortFeatures = cfg.comfortFeaturesSelected != null
                    ? new List<string>(cfg.comfortFeaturesSelected) : new List<string>(),
                comfortClass = cfg.calculatedComfortClass > 0
                    ? cfg.calculatedComfortClass
                    : InteriorMixCalculator.CalculateComfortClass(cfg.interiorMix, cfg.comfortFeaturesSelected),
                paintScheme = "Barwy fabryczne",

                mileageKm = 0,
                conditionPercent = 100,
                cleanlinessPercent = 100,
                components = VehicleComponents.New(100f),
                productionYear = CurrentGameYear(),
                purchaseGameTime = nowGameTime,
                inspections = InspectionSchedule.CreateFresh(nowGameTime, 0f),
                // M9c-D: ETA = sama produkcja; wagon pasywny — externalLocation puste → dostawa z lokomotywą (F5)
                estimatedCompletionGameTime = nowGameTime + PRODUCTION_TIME_SECONDS,
                position = new VehiclePosition { kind = VehicleLocationKind.None },

                operationalCostPerKmGroszy = 60, // M-Fleet wagon baseline (taki jak Coach_264_Universal)
                reliabilityScore = FleetBalanceConstants.DefaultReliabilityScoreNew,
                breakdownRiskFactor = FleetBalanceConstants.DefaultBreakdownRiskFactorNew,
                maintenanceCostFactor = FleetBalanceConstants.DefaultMaintenanceCostFactorNew,
                componentRisk = new ComponentRiskFactors(), // baseline 1.0

                minPlatformLengthM = Mathf.RoundToInt(bodyDef.lengthM + 4f), // peron + zapas
                requiresMaintenanceCapabilities = new List<string> { "UndergroundInspection" },

                productionStatus = VehicleProductionStatus.InProduction,
                suggestedCategoryGroups = new List<string> { "RegionalFast", "InterregionalFast", "ExpressDomestic" },
                canBePulledByDiesel = true,
                isShuntingLocomotive = false,
                historicalFactoid = "Wagon skonfigurowany przez gracza w konfiguratorze.",

                // M-FC-1: zachowaj konfigurację źródłową (do M-Modernization, paint editor, smart re-buy)
                sourceConfiguration = cfg,
                paintDefinition = cfg.paint ?? new PaintDefinition()
            };

            v.history.Add(new MaintenanceRecord
            {
                gameTimeSeconds = nowGameTime,
                recordType = MaintenanceRecordTypes.PurchaseNewConfigurable,
                description = $"Zamowiono wagon konfigurowalny: {bodyDef.displayName} + {bogieDef.displayName}",
                cost = item.unitPrice,
                mileageAtRecord = 0
            });

            v.monthlyMaintenanceCost = MaintenanceCostCalculator.Calculate(v, CurrentGameYear());

            return v;
        }

        /// <summary>
        /// M-FC-3: Buduje FleetVehicleData z FleetFamily + FleetVariantSpec (FLIRT, SA, EU160 itp.).
        /// Parametry techniczne dziedziczone z wariantu, opcje (safety/comfort/drzwi/pantografy)
        /// z VehicleConfiguration. EVN i numer per egzemplarz.
        /// </summary>
        private static FleetVehicleData BuildFamilyVariantFromConfig(
            CartItem item, int newId, long nowGameTime, System.Random rng)
        {
            var cfg = item.vehicleConfiguration;
            var family = FleetCatalog.FindFamily(cfg.familyId);
            var variant = FleetCatalog.FindVariant(cfg.familyId, GetMemberCountFromKey(cfg.variantKey), GetVoltageFromKey(cfg.variantKey));

            if (family == null || variant == null)
            {
                Log.Error($"[CartProcessor] Cannot build family vehicle: family={family?.familyId ?? "null"} ({cfg.familyId}), variantKey={cfg.variantKey}");
                return null;
            }

            string number = $"{family.familyId}-{rng.Next(1000, 9999)}";

            var v = new FleetVehicleData
            {
                id = newId,
                seriesId = $"{family.familyId}_{variant.memberCount}cz_{variant.voltageConfigId}",
                family = family.familyId,
                country = string.IsNullOrEmpty(family.country) ? "PL" : family.country,
                series = $"{family.displayName} ({variant.variantLabel})",
                number = number,
                evn = EvnGenerator.Generate(family.type, newId),
                type = family.type,
                status = FleetVehicleStatus.InProduction,
                currentTask = $"Produkcja w {family.factoryLocation}",
                assignedConsist = null,

                supportedTractions = variant.supportedTractions != null
                    ? new List<TractionType>(variant.supportedTractions)
                    : new List<TractionType>(),
                maxSpeedKmh = variant.maxSpeedKmh,
                powerKw = variant.powerKw,
                wheelbase = variant.wheelbase,
                // M-FC-5: dla EZT/SZT seats z interiorMix × członów (cfg.calculatedSeats); fallback na variant base
                passengerSeats = cfg.calculatedSeats > 0 ? cfg.calculatedSeats : variant.passengerSeatsBase,
                seatBreakdown = (family.type == FleetVehicleType.EMU || family.type == FleetVehicleType.DMU) && cfg.interiorMix != null && cfg.interiorMix.Count > 0
                    ? BuildSeatBreakdownFromMix(cfg.interiorMix,
                        variant.memberCount > 0 ? variant.lengthM / variant.memberCount : variant.lengthM,
                        cfg.calculatedSeats > 0 ? cfg.calculatedSeats / Mathf.Max(1, variant.memberCount) : variant.passengerSeatsBase / Mathf.Max(1, variant.memberCount))
                    : CopyBreakdown(variant.seatBreakdownBase),
                coachCount = variant.memberCount,
                maxCoachesInTrain = 0,
                accelerationMps2 = variant.accelerationMps2 > 0 ? variant.accelerationMps2 : FleetBalanceConstants.DefaultAccelerationMps2,
                decelerationMps2 = variant.decelerationMps2 > 0 ? variant.decelerationMps2 : FleetBalanceConstants.DefaultDecelerationMps2,

                lengthM = variant.lengthM,
                emptyMassTons = variant.emptyMassTons,
                maxLoadedMassTons = variant.maxLoadedMassTons,
                brakingMassTons = variant.brakingMassTons,
                brakeRegime = variant.brakeRegime,

                safetySystemsInstalled = cfg.safetySystemsSelected != null
                    ? new List<string>(cfg.safetySystemsSelected)
                    : new List<string>(),
                voltages = variant.voltages != null
                    ? new List<string>(variant.voltages)
                    : new List<string>(),
                comfortFeatures = cfg.comfortFeaturesSelected != null
                    ? new List<string>(cfg.comfortFeaturesSelected)
                    : new List<string>(),
                comfortClass = cfg.calculatedComfortClass > 0
                    ? cfg.calculatedComfortClass
                    : (variant.comfortClassBase > 0 ? variant.comfortClassBase : FleetBalanceConstants.DefaultComfortClassStandard),
                paintScheme = "Barwy fabryczne",

                mileageKm = 0,
                conditionPercent = 100,
                cleanlinessPercent = 100,
                components = VehicleComponents.New(100f),
                productionYear = CurrentGameYear(),
                purchaseGameTime = nowGameTime,
                inspections = InspectionSchedule.CreateFresh(nowGameTime, 0f),
                // M9c-D: ETA = sama produkcja; externalLocation → fabryka rodziny (punkt zakupu)
                estimatedCompletionGameTime = nowGameTime + PRODUCTION_TIME_SECONDS,
                position = new VehiclePosition { kind = VehicleLocationKind.None, externalLocation = family.factoryLocation },

                operationalCostPerKmGroszy = variant.operationalCostPerKmGroszy,
                fuelTankCapacityLitres = variant.fuelTankCapacityLitres,
                fuelConsumptionLper100km = variant.fuelConsumptionLper100km,
                reliabilityScore = variant.reliabilityScore > 0 ? variant.reliabilityScore : FleetBalanceConstants.DefaultReliabilityScoreNew,
                breakdownRiskFactor = variant.breakdownRiskFactor > 0 ? variant.breakdownRiskFactor : FleetBalanceConstants.DefaultBreakdownRiskFactorNew,
                maintenanceCostFactor = variant.maintenanceCostFactor > 0 ? variant.maintenanceCostFactor : FleetBalanceConstants.DefaultMaintenanceCostFactorNew,
                componentRisk = CloneRisks(variant.componentRisk),

                minPlatformLengthM = variant.minPlatformLengthM,
                requiresMaintenanceCapabilities = variant.requiresMaintenanceCapabilities != null
                    ? new List<string>(variant.requiresMaintenanceCapabilities)
                    : new List<string>(),

                productionStatus = family.status,
                suggestedCategoryGroups = variant.suggestedCategoryGroups != null
                    ? new List<string>(variant.suggestedCategoryGroups)
                    : new List<string>(),
                canBePulledByDiesel = variant.canBePulledByDiesel,
                isShuntingLocomotive = variant.isShuntingLocomotive,
                historicalFactoid = !string.IsNullOrEmpty(variant.variantFactoid)
                    ? variant.variantFactoid
                    : family.historicalFactoid,

                sourceConfiguration = cfg,
                paintDefinition = cfg.paint ?? new PaintDefinition()
            };

            v.history.Add(new MaintenanceRecord
            {
                gameTimeSeconds = nowGameTime,
                recordType = MaintenanceRecordTypes.PurchaseNew,
                description = $"Zamowiono nowy pojazd: {family.displayName} ({variant.variantLabel})",
                cost = item.unitPrice,
                mileageAtRecord = 0
            });

            v.monthlyMaintenanceCost = MaintenanceCostCalculator.Calculate(v, CurrentGameYear());
            return v;
        }

        /// <summary>
        /// M-FC-5: Buduje List&lt;SeatCount&gt; z miksu stref siedzeń. Każda strefa generuje
        /// osobny SeatCount proporcjonalnie do swojego udziału w długości pudła/członu.
        /// </summary>
        private static List<SeatCount> BuildSeatBreakdownFromMix(List<SeatZoneSlot> mix, float bodyLengthM, int totalSeatsCap)
        {
            var result = new List<SeatCount>();
            if (mix == null || mix.Count == 0)
            {
                if (totalSeatsCap > 0)
                    result.Add(new SeatCount { type = SeatZoneType.SecondClassOpen, count = totalSeatsCap });
                return result;
            }

            // Per zone: wylicz seats z calculator, agregując po typie (np. dwa SecondClassOpen segmenty → 1 SeatCount)
            var perType = new Dictionary<SeatZoneType, int>();
            foreach (var slot in mix)
            {
                var single = new List<SeatZoneSlot>
                {
                    new SeatZoneSlot { startPercent = slot.startPercent, endPercent = slot.endPercent, type = slot.type }
                };
                int seats = InteriorMixCalculator.CalculateSeats(single, bodyLengthM);
                if (seats <= 0) continue;
                if (perType.ContainsKey(slot.type)) perType[slot.type] += seats;
                else perType[slot.type] = seats;
            }

            foreach (var kv in perType)
                result.Add(new SeatCount { type = kv.Key, count = kv.Value });
            return result;
        }

        /// <summary>M-FC-3: parsuje memberCount z variantKey (np. "FLIRT|3|3kV" → 3).</summary>
        private static int GetMemberCountFromKey(string variantKey)
        {
            if (string.IsNullOrEmpty(variantKey)) return 1;
            var parts = variantKey.Split('|');
            if (parts.Length < 2) return 1;
            return int.TryParse(parts[1], out var n) ? n : 1;
        }

        /// <summary>M-FC-3: parsuje voltageConfigId z variantKey (np. "FLIRT|3|3kV" → "3kV").</summary>
        private static string GetVoltageFromKey(string variantKey)
        {
            if (string.IsNullOrEmpty(variantKey)) return "";
            var parts = variantKey.Split('|');
            return parts.Length >= 3 ? parts[2] : "";
        }
    }

    /// <summary>M-Economy Faza 3: wynik checkout koszyka (<see cref="CartProcessor.TryCheckout"/>).</summary>
    public struct CheckoutResult
    {
        public bool Success;            // ProcessOrder wykonany (kasa pobrana gdy Added>0)
        public bool InsufficientFunds;  // zablokowano — brak środków (nic nie kupiono, nic nie pobrano)
        public int Added;               // liczba dodanych pojazdów
        public long TotalZl;            // suma koszyka [zł]
        public long ChargedGroszy;      // faktycznie pobrane [gr]
    }
}
