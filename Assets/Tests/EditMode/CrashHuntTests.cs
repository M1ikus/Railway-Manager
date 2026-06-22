using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine.TestTools;
using RailwayManager.Core;
using RailwayManager.Fleet;
using RailwayManager.SaveLoad.Modules;
using RailwayManager.Timetable;
using RailwayManager.Timetable.Economy;
using RailwayManager.Personnel;
using UnityEngine;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// DIAGNOSTYKA (nie regresja): adversarial hunt na unhandled exceptions. Karmi PUBLICZNE wejścia
    /// (osiągalne przez gracza / uszkodzony lub edytowany save) patologicznymi danymi. Asercja
    /// DoesNotThrow = kontrakt "publiczne API nie wywala gry na złym wejściu". FAIL = potwierdzony
    /// wektor crashu do zaguardowania.
    /// </summary>
    public class CrashHuntTests
    {
        string _startDateBackup;

        [SetUp]
        public void SetUp() => _startDateBackup = GameState.GameStartDateIso;

        [TearDown]
        public void TearDown() => GameState.GameStartDateIso = _startDateBackup;

        // ── V1: zły node id w grafie bloków (stale save / regeneracja mapy) ──
        [Test]
        public void V1_BlockSectionGraph_BadNodeId_DoesNotThrow()
        {
            var bsg = new BlockSectionGraph(new BlockSectionBuilder.BuildResult
            {
                sections = new List<BlockSection>(), edgeToSection = new int[0]
            });
            var emptyGraph = new PathfindingGraph(); // 0 węzłów
            var route = new List<int> { 0, 1, 2 };   // id spoza zakresu (graf pusty)

            Assert.DoesNotThrow(() => bsg.GetSectionsForRoute(route, emptyGraph),
                "GetSectionsForRoute z node id spoza grafu nie powinno wywalać gry.");
        }

        // ── V2: zepsuta data jednorazowego obiegu (save corruption / manual edit) ──
        [Test]
        public void V2_Circulation_MalformedOneTimeDate_DoesNotThrow()
        {
            var c = new Circulation { isOneTime = true, oneTimeDateIso = "to-nie-jest-data" };
            Assert.DoesNotThrow(() => c.GetActiveDates(),
                "Circulation z uszkodzoną oneTimeDateIso nie powinno wywalać.");
        }

        // ── V3: zepsuta data startu gry (kotwica cykli) ──
        [Test]
        public void V3_Circulation_MalformedGameStartDate_DoesNotThrow()
        {
            GameState.GameStartDateIso = "XXXX-99-99";
            var c = new Circulation { calendar = DayMask.Daily(), weeksValid = 1 };
            Assert.DoesNotThrow(() => c.GetActiveDates(),
                "Uszkodzona GameStartDateIso (kotwica) nie powinna wywalać GetActiveDates.");
        }

        // ── V4: zepsuta data w grafiku pracownika ──
        [Test]
        public void V4_ShiftManager_MalformedDate_DoesNotThrow()
        {
            var e = new Employee { employeeId = 800900, role = EmployeeRole.Driver };
            Assert.DoesNotThrow(() => ShiftManager.ComputeStatusForDate(e, "garbage"),
                "ComputeStatusForDate z błędną datą nie powinno wywalać.");
        }

        // ── V5: NaN / ujemny dystans w cenie biletu ──
        [Test]
        public void V5_TicketSystem_NaNAndNegativeKm_DoesNotThrow()
        {
            var cat = new CommercialCategory { basePriceZl = 10f, pricePerKmZl = 1f };
            Assert.DoesNotThrow(() =>
            {
                TicketSystem.CalculatePriceGroszy(cat, float.NaN);
                TicketSystem.CalculatePriceGroszy(cat, -100f);
                TicketSystem.CalculatePriceGroszy(cat, float.PositiveInfinity);
            }, "Cena biletu z NaN/ujemnym/inf dystansem nie powinna wywalać.");
        }

        // ── V6: NaN deltaKm w degradacji ──
        [Test]
        public void V6_Degradation_NaNDistance_DoesNotThrow()
        {
            int id = 800901;
            FleetService.RemoveOwnedVehicle(id);
            FleetService.AddOwnedVehicle(new FleetVehicleData
            {
                id = id, type = FleetVehicleType.EMU, components = VehicleComponents.New(100f),
                componentRisk = new ComponentRiskFactors()
            });
            try
            {
                Assert.DoesNotThrow(() =>
                {
                    DegradationService.ApplyDegradation(new List<int> { id }, float.NaN);
                    DegradationService.ApplyDegradation(new List<int> { id }, float.PositiveInfinity);
                }, "Degradacja z NaN/inf km nie powinna wywalać (ani produkować NaN conditionPercent).");
            }
            finally { FleetService.RemoveOwnedVehicle(id); }
        }

        // ── V7: ekstremalne wartości w koszcie utrzymania (overflow) ──
        [Test]
        public void V7_MaintenanceCost_ExtremeValues_DoesNotThrow()
        {
            var v = new FleetVehicleData
            {
                type = FleetVehicleType.EMU, productionYear = int.MinValue,
                conditionPercent = -9999f, operationalCostPerKmGroszy = int.MaxValue
            };
            Assert.DoesNotThrow(() => MaintenanceCostCalculator.Calculate(v, int.MaxValue),
                "Koszt utrzymania z ekstremalnymi wartościami nie powinien rzucać OverflowException.");
        }

        // ── V8: NaN pozycje stacji w OD matrix ──
        [Test]
        public void V8_ODMatrix_NaNPositions_DoesNotThrow()
        {
            var stations = new List<RailwayStation>
            {
                new() { stationId = 1, pathNodeId = 1, position = new Vector2(float.NaN, 0f) },
                new() { stationId = 2, pathNodeId = 2, position = new Vector2(0f, float.NaN) }
            };
            var importance = new Dictionary<int, float> { { 1, 10f }, { 2, 10f } };
            var m = new OriginDestinationMatrix();
            Assert.DoesNotThrow(() => m.Build(stations, importance),
                "OD matrix z NaN pozycjami stacji nie powinno wywalać.");
        }

        // ── V9: ujemny / przepełniony koszyk (modded quantity) ──
        [Test]
        public void V9_DemandModifiers_OutOfRangeInputs_DoesNotThrow()
        {
            Assert.DoesNotThrow(() =>
            {
                DemandModifiers.GetHourOfDayModifier(-5);
                DemandModifiers.GetHourOfDayModifier(999);
                DemandModifiers.GetOfferFrequencyModifier(int.MinValue);
            }, "DemandModifiers z godziną/częstotliwością spoza zakresu nie powinno wywalać.");
        }

        // ── V10: ConsistValidator z mieszaną listą zawierającą nieistniejące id ──
        [Test]
        public void V10_ConsistValidator_NonexistentIds_DoesNotThrow()
        {
            Assert.DoesNotThrow(() =>
                ConsistValidator.Validate(new List<int> { -1, int.MaxValue, 999999 }),
                "ConsistValidator z nieistniejącymi id nie powinien wywalać.");
        }

        // ── V11+V12: SYSTEMOWY — load sanityzuje uszkodzoną GameStartDateIso ──
        // Fix jest w WorldSavable.Deserialize (jedyny choke point load→runtime), nie w accessorach.
        // Po sanityzacji GameStartDateIso jest zawsze parsowalny → WSZYSTKIE callery ParseDate
        // (CurrentDateIso używane w setkach miejsc, DemandModifiers spawn pasażerów, CrewDuty) bezpieczne.
        [Test]
        public void V11_WorldSavable_SanitizesCorruptStartDate()
        {
            var snapshot = new WorldSavable().Serialize(); // backup całego world state
            try
            {
                new WorldSavable().Deserialize(new JObject { ["gameStartDateIso"] = "@@@bad@@@" }, 1);

                Assert.That(IsoTime.TryParseDate(GameState.GameStartDateIso, out _), Is.True,
                    "Load uszkodzonej daty → fallback do poprawnej (nie 'garbage' w runtime).");
                // Skoro source jest czysty, downstream accessory nie rzucają:
                Assert.DoesNotThrow(() => { var _ = GameState.CurrentDateIso; },
                    "CurrentDateIso bezpieczne po sanityzacji źródła (używane wszędzie).");
                Assert.DoesNotThrow(() => DemandModifiers.GetCurrentGameDateTime(),
                    "GetCurrentGameDateTime bezpieczne (spawn pasażerów).");
            }
            finally
            {
                new WorldSavable().Deserialize(snapshot, 1); // restore world state
            }
        }

        // ── V13: koszyk z isNewVehicle=true ale null model (malformed/edytowany save) ──
        [Test]
        public void V13_CartProcessor_NewVehicleNullModel_DoesNotThrow()
        {
            var cart = new List<CartItem>
            {
                new() { isNewVehicle = true, vehicleConfiguration = null, quantity = 1 }
            };
            Assert.DoesNotThrow(() => CartProcessor.ProcessOrder(cart),
                "ProcessOrder z isNewVehicle=true + null model nie powinno wywalać NRE.");
        }

        // ── V14: lookup pojazdu po nieistniejącym id ──
        [Test]
        public void V14_FleetService_GetOwnedById_BadId_DoesNotThrow()
        {
            Assert.DoesNotThrow(() =>
            {
                FleetService.GetOwnedById(-1);
                FleetService.GetOwnedById(int.MaxValue);
            }, "GetOwnedById ze złym id nie powinno wywalać (oczekiwane null).");
        }

        // ── V15: lookup rozkładu/trasy po nieistniejącym id ──
        [Test]
        public void V15_TimetableService_BadLookups_DoNotThrow()
        {
            Assert.DoesNotThrow(() =>
            {
                TimetableService.GetTimetable(-1);
                TimetableService.GetRoute(int.MaxValue);
            }, "Lookupy rozkładu/trasy po złym id nie powinny wywalać.");
        }

        // ── V17: malformed paint JSON (uszkodzony shareable paint / save) ──
        [Test]
        public void V17_PaintSerializer_MalformedInput_DoesNotThrow()
        {
            // PaintSerializer loguje [Error] przy złym wejściu (graceful degradation z diagnostyką)
            // — to OCZEKIWANE, nie crash. Mówimy harnessowi, że te logi błędu są zamierzone.
            LogAssert.ignoreFailingMessages = true;
            Assert.DoesNotThrow(() =>
            {
                PaintSerializer.Deserialize("to-nie-jest-json");
                PaintSerializer.Deserialize(null);
                PaintSerializer.Deserialize("{ niepełny");
                PaintSerializer.Validate(null, out _);
            }, "PaintSerializer z uszkodzonym/null wejściem nie powinien wywalać (paint z save/share).");
        }

        // ── V18: EVN z ekstremalnym serialem ──
        [Test]
        public void V18_EvnGenerator_ExtremeSerial_DoesNotThrow()
        {
            Assert.DoesNotThrow(() =>
            {
                EvnGenerator.Generate(FleetVehicleType.EMU, int.MaxValue);
                EvnGenerator.Generate(FleetVehicleType.PassengerCar, int.MinValue);
                EvnGenerator.Generate(FleetVehicleType.ElectricLocomotive, -1);
            }, "EvnGenerator z ekstremalnym serialem nie powinien wywalać.");
        }

        // ── V19: katalog dotacji — nieznany / null kod województwa ──
        [Test]
        public void V19_SubsidyCatalog_UnknownCode_DoesNotThrow()
        {
            Assert.DoesNotThrow(() =>
            {
                SubsidyRulesCatalog.GetSubsidyPerRunGroszy("ZZ-nieistniejące");
                SubsidyRulesCatalog.GetSubsidyPerRunGroszy(null);
                SubsidyRulesCatalog.GetByVoivodeshipCode(null);
            }, "SubsidyRulesCatalog z nieznanym/null kodem nie powinien wywalać.");
        }

        // ── V20: katalog IRJ — dowolna (potencjalnie niedozwolona) kombinacja kategorii ──
        [Test]
        public void V20_IrjCatalog_ArbitraryCategory_DoesNotThrow()
        {
            var weird = new IrjCategory(IrjGroup.FreightEmptyTest, TractionLetter.DieselLoco);
            Assert.DoesNotThrow(() =>
            {
                IrjCategoryCatalog.Get(weird);
                IrjCategoryCatalog.GetCode(weird);
                IrjCategoryCatalog.GetDigits(weird);
                IrjCategoryCatalog.IsAllowed(weird);
            }, "IrjCategoryCatalog z dowolną kombinacją nie powinien wywalać (zwraca Forbidden/'???').");
        }

        // ── V21/V22: #1A — repair dangling cross-module refs po load (PartialLoad) ──
        [Test]
        public void V21_RepairDangling_NullsVehicleRefToMissingCirculation()
        {
            const int vid = 870500;
            FleetService.RemoveOwnedVehicle(vid);
            FleetService.AddOwnedVehicle(new FleetVehicleData { id = vid, assignedCirculationId = 999999 });
            try
            {
                CirculationService.RepairDanglingReferences();
                Assert.That(FleetService.GetOwnedById(vid).assignedCirculationId, Is.EqualTo(-1),
                    "Pojazd wskazujący nieistniejący obieg → wyczyszczony do -1 (zamiast dangling ref).");
            }
            finally { FleetService.RemoveOwnedVehicle(vid); }
        }

        [Test]
        public void V22_RepairDangling_RemovesOrphanVehicleFromCirculation()
        {
            var c = new Circulation { id = 870600 };
            c.vehicleAssignmentsPerDay["2026-06-01"] = new List<int> { 870601 }; // vid spoza floty
            CirculationService.Circulations.Add(c);
            try
            {
                CirculationService.RepairDanglingReferences();
                Assert.That(c.vehicleAssignmentsPerDay["2026-06-01"], Has.No.Member(870601),
                    "Obieg z vehicleId spoza floty → orphan usunięty z assignmentu.");
            }
            finally { CirculationService.Circulations.Remove(c); }
        }

        // ── V23: #1B — InitializeDefault izoluje resety singletonow ──
        // Realny SPOF: subskrybent OnOwnedChanged rzuca -> FleetService.ResetForNewGame()
        // rzuca w polowie InitializeDefault. Bez izolacji pominieto by reset
        // VehicleLocationService (stale "pojazd-zombie") i FleetMarketRefreshService.
        [Test]
        public void V23_FleetSavable_InitializeDefault_IsolatesResets_WhenFleetResetThrows()
        {
            DestroyExisting<VehicleLocationService>();
            var vls = CreateWithAwake<VehicleLocationService>();
            VehicleLocationService.ResetAll();
            vls.SetInDepot(777); // stary cross-session record
            FleetMarketRefreshService.RestoreDaysSinceLastRefresh(99); // brudny countdown

            System.Action boom = () => throw new System.InvalidOperationException("test: subscriber throws");
            FleetService.OnOwnedChanged += boom;
            try
            {
                Assert.That(vls.Get(777), Is.Not.Null, "Pre-warunek: record zaseedowany.");

                // SafeReset łapie i loguje Error — to oczekiwane (dowod ze guard zadzialal).
                LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(
                    @"\[FleetSavable\] Reset 'FleetService\.ResetForNewGame' threw"));

                Assert.DoesNotThrow(() => new FleetSavable().InitializeDefault(),
                    "#1B: InitializeDefault nie propaguje wyjatku z resetu pojedynczego singletona.");

                Assert.That(vls.Get(777), Is.Null,
                    "#1B: VehicleLocationService zresetowany mimo wyjatku w FleetService.ResetForNewGame (izolacja).");
                Assert.That(FleetMarketRefreshService.GetDaysSinceLastRefresh(), Is.EqualTo(0),
                    "#1B: FleetMarketRefreshService zresetowany mimo wczesniejszego wyjatku (izolacja).");
            }
            finally
            {
                FleetService.OnOwnedChanged -= boom;
                VehicleLocationService.ResetAll();
                DestroyExisting<VehicleLocationService>();
            }
        }

        // ── Helpers ──────────────────────────────────────────────────
        static T CreateWithAwake<T>() where T : MonoBehaviour
        {
            var go = new GameObject(typeof(T).Name + "_Test");
            var comp = go.AddComponent<T>();
            typeof(T).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(comp, null);
            return comp;
        }

        static void DestroyExisting<T>() where T : MonoBehaviour
        {
            foreach (var c in Resources.FindObjectsOfTypeAll<T>())
                Object.DestroyImmediate(c.gameObject);
        }
    }
}
