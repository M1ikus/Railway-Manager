using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using RailwayManager.SaveLoad.Modules;
using RailwayManager.Timetable;
using RailwayManager.Timetable.Economy;
using RailwayManager.Personnel;
using RailwayManager.Maintenance;
using TimetableObj = RailwayManager.Timetable.Timetable;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// Per-modul save round-trip dla realnych Savable'i opartych o STATIC services
    /// (CirculationsSavable, TimetableSavable). SaveLoadRoundtripTests/SaveSerializationTests
    /// testuja infra (bundle/HMAC) na dummy modulach; DeliverySaveRoundtripTests pokrywa Fleet.
    /// Tu: prawdziwy Serialize->reset->Deserialize->Serialize i JToken.DeepEquals (idempotence).
    ///
    /// Lapie field-drop: dodasz pole do Serialize, zapomnisz odczytu w Deserialize ->
    /// jobj1 ma pole, jobj2 nie -> DeepEquals FAIL. Plus asercja ze reset faktycznie czysci
    /// (dowod ze Deserialize ODTWORZYL stan, nie ze nigdy go nie wyczyszczono).
    /// </summary>
    public class SaveModuleRoundtripTests
    {
        [TearDown]
        public void TearDown()
        {
            CirculationService.Reset();
            TimetableService.ResetForNewGame();
            new PersonnelSavable().InitializeDefault();
            EconomyManager.Instance?.ResetRuntime();
            ReputationManager.Instance?.ResetRuntime();
            PartInventoryService.Instance?.ResetRuntime();
            WorkshopManager.Instance?.ResetRuntimeState();
        }

        [Test]
        public void CirculationsSavable_RoundTrip_IsIdempotent()
        {
            var savable = new CirculationsSavable();

            var circ = new Circulation
            {
                id = 7001,
                name = "RoundTrip Test",
                isOneTime = true,
                oneTimeDateIso = "2026-06-15",
                assignedVehicleIds = new List<int> { 11, 22 },
                vehicleAssignmentsPerDay = new Dictionary<string, List<int>>
                {
                    ["2026-06-15"] = new List<int> { 11, 22 }
                },
                notes = "round-trip notes"
            };
            CirculationService.RestoreFromSave(new List<Circulation> { circ }, 7002);

            var jobj1 = savable.Serialize();

            // reset -> stan faktycznie wyczyszczony
            savable.InitializeDefault();
            Assert.That(CirculationService.Circulations.Count, Is.EqualTo(0),
                "Pre-warunek: InitializeDefault czysci obiegi.");

            // restore z jobj1 -> ponowny Serialize == jobj1
            savable.Deserialize(jobj1, savable.SchemaVersion);
            var jobj2 = savable.Serialize();

            Assert.That(JToken.DeepEquals(jobj1, jobj2), Is.True,
                $"Round-trip CirculationsSavable nie jest idempotentny (field-drop?).\nPRZED:\n{jobj1}\nPO:\n{jobj2}");
            Assert.That(CirculationService.Circulations.Count, Is.EqualTo(1), "Obieg odtworzony.");
            Assert.That(CirculationService.GetNextId(), Is.EqualTo(7002), "nextId odtworzony (BUG-078).");
        }

        [Test]
        public void TimetableSavable_RoundTrip_IsIdempotent()
        {
            var savable = new TimetableSavable();

            TimetableService.ResetForNewGame();
            TimetableService.Routes.Add(new Route
            {
                id = 5001,
                name = "Test Route",
                nodeIds = new List<int> { 1, 2, 3 },
                totalLengthM = 12345f,
                startVoivodeship = "mazowieckie",
                isDeliveryRoute = false
            });
            TimetableService.Timetables.Add(new TimetableObj
            {
                id = 6001,
                name = "Test TT",
                commercialCategoryId = "test_cat",
                trainNumber = "12345",
                startDateIso = "2026-06-01",
                isDeliveryTimetable = false
            });
            TimetableService.CommercialCategories.Add(new CommercialCategory
            {
                id = "test_cat",
                displayName = "Test Cat",
                shortCode = "TC",
                basePriceZl = 5f,
                pricePerKmZl = 0.5f
            });
            TimetableService.RestoreCountersFromSave(5002, 6002, 7000);

            var jobj1 = savable.Serialize();

            savable.InitializeDefault();

            savable.Deserialize(jobj1, savable.SchemaVersion);
            var jobj2 = savable.Serialize();

            Assert.That(JToken.DeepEquals(jobj1, jobj2), Is.True,
                $"Round-trip TimetableSavable nie jest idempotentny (field-drop?).\nPRZED:\n{jobj1}\nPO:\n{jobj2}");
            Assert.That(TimetableService.Routes.Count, Is.GreaterThanOrEqualTo(1), "Trasa odtworzona.");
            Assert.That(TimetableService.NextRouteId, Is.EqualTo(5002), "nextRouteId odtworzony.");
            Assert.That(TimetableService.NextTimetableId, Is.EqualTo(6002), "nextTimetableId odtworzony.");
            Assert.That(TimetableService.NextTrainRunId, Is.EqualTo(7000), "nextTrainRunId odtworzony.");
        }

        [Test]
        public void PersonnelSavable_RoundTrip_IsIdempotent()
        {
            var savable = new PersonnelSavable();

            // Pre-tworzymy MonoBehaviour singletony dotykane w Serialize/Deserialize
            // (DontDestroyOnLoad rzuca w EditMode; Awake ustawia Instance -> EnsureExists short-circuit).
            EnsureSingleton<CandidateMarketService>();
            EnsureSingleton<ResearchService>();
            EnsureSingleton<TicketClerkService>();
            EnsureSingleton<WorkshopAssignmentService>();
            EnsureSingleton<TrafficControlService>();
            EnsureSingleton<CleaningService>();

            // Seed drift-prone skalary/toggle (BUG-088) na NIE-domyslne wartosci.
            // (Employee/schedule construction pominiete — fragile; te pola sa najczestszym
            //  zrodlem field-drop bo dochodza pojedynczo per-bug.)
            TrafficControlService.PriorityWorkshopOverdue = 991;
            TrafficControlService.PriorityScheduledDeparture = 992;
            TrafficControlService.PriorityWashBayPlanned = 993;
            TrafficControlService.PriorityParkingReshuffle = 994;
            CleaningService.AutoCleaningEnabled = false;
            CleaningService.AutoWashingEnabled = false;
            HotelBookingService.CompanyDefaultHotelTier = HotelTier.Basic;

            var jobj1 = savable.Serialize();

            savable.InitializeDefault();
            Assert.That(CleaningService.AutoCleaningEnabled, Is.True,
                "Pre-warunek: InitializeDefault przywraca AutoCleaning do default (true).");

            savable.Deserialize(jobj1, savable.SchemaVersion);
            var jobj2 = savable.Serialize();

            Assert.That(JToken.DeepEquals(jobj1, jobj2), Is.True,
                $"Round-trip PersonnelSavable nie jest idempotentny (field-drop?).\nPRZED:\n{jobj1}\nPO:\n{jobj2}");
            Assert.That(TrafficControlService.PriorityWorkshopOverdue, Is.EqualTo(991), "priorytet TrafficControl odtworzony (BUG-088).");
            Assert.That(CleaningService.AutoCleaningEnabled, Is.False, "toggle AutoCleaning odtworzony.");
            Assert.That(HotelBookingService.CompanyDefaultHotelTier, Is.EqualTo(HotelTier.Basic), "hotel tier odtworzony.");
        }

        [Test]
        public void EconomySavable_RoundTrip_IsIdempotent()
        {
            var savable = new EconomySavable();

            // Pre-tworzymy singletony bez DontDestroyOnLoad (rzuca w EditMode) — Awake ustawia
            // Instance, wiec EnsureExists short-circuituje przed DontDestroyOnLoad.
            EnsureSingleton<EconomyManager>();
            EnsureSingleton<ReputationManager>();
            var econ = EconomyManager.EnsureExists();
            var rep = ReputationManager.EnsureExists();

            econ.RestoreFromSave(123456L, 7890L, 4321L,
                new List<LineBalance>(), new List<DailyBalance>());
            rep.RestoreFromSave(73,
                new List<KeyValuePair<string, int>> { new("mazowieckie", 61) },
                new List<KeyValuePair<int, int>> { new(101, 72) }); // int-key -> string w JSON

            var jobj1 = savable.Serialize();

            savable.InitializeDefault(); // ResetRuntime econ + rep

            savable.Deserialize(jobj1, savable.SchemaVersion);
            var jobj2 = savable.Serialize();

            Assert.That(JToken.DeepEquals(jobj1, jobj2), Is.True,
                $"Round-trip EconomySavable nie jest idempotentny (field-drop?).\nPRZED:\n{jobj1}\nPO:\n{jobj2}");
            Assert.That(EconomyManager.Instance.RevenueTodayGroszy, Is.EqualTo(123456L), "revenue today odtworzony.");
            Assert.That(EconomyManager.Instance.CostsTodayGroszy, Is.EqualTo(7890L), "costs today odtworzony.");
            Assert.That(ReputationManager.Instance.Global, Is.EqualTo(73), "reputacja globalna odtworzona.");
        }

        [Test]
        public void MaintenanceSavable_RoundTrip_IsIdempotent()
        {
            var savable = new MaintenanceSavable();

            EnsureSingleton<PartInventoryService>();
            EnsureSingleton<WorkshopManager>();

            PartInventoryService.Instance.RestoreFromSave(
                new Dictionary<RailwayManager.Fleet.ComponentType, int>
                {
                    [RailwayManager.Fleet.ComponentType.Engine] = 5,
                    [RailwayManager.Fleet.ComponentType.Brake] = 3
                },
                new List<PendingPartOrder>(),
                new List<PartPurchaseRecord>());
            WorkshopManager.Instance.RestoreFromSave(
                new List<WorkshopSlot>(), new List<OngoingExternalJob>(), 7);

            var jobj1 = savable.Serialize();

            savable.InitializeDefault();

            savable.Deserialize(jobj1, savable.SchemaVersion);
            var jobj2 = savable.Serialize();

            Assert.That(JToken.DeepEquals(jobj1, jobj2), Is.True,
                $"Round-trip MaintenanceSavable nie jest idempotentny (field-drop?).\nPRZED:\n{jobj1}\nPO:\n{jobj2}");
            Assert.That(PartInventoryService.Instance.StockSnapshot[RailwayManager.Fleet.ComponentType.Engine],
                Is.EqualTo(5), "stock Engine odtworzony (enum-keyed dict -> string JSON).");
            Assert.That(WorkshopManager.Instance.GetNextSlotId(), Is.EqualTo(7), "workshopNextSlotId odtworzony.");
        }

        // ── Helpers ──────────────────────────────────────────────────
        // Tworzy MonoBehaviour singleton z wymuszonym Awake (ustawia Instance), bez
        // DontDestroyOnLoad ktory rzuca w EditMode. AddComponent NIE wola Awake samo.
        static void EnsureSingleton<T>() where T : MonoBehaviour
        {
            if (Resources.FindObjectsOfTypeAll<T>().Length > 0) return; // juz istnieje
            var go = new GameObject(typeof(T).Name + "_RTTest");
            var comp = go.AddComponent<T>();
            typeof(T).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(comp, null);
        }
    }
}
