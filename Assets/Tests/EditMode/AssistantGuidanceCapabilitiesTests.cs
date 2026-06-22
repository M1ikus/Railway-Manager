using System.Collections.Generic;
using NUnit.Framework;
using RailwayManager.Core;
using RailwayManager.Core.Assistant;
using RailwayManager.Fleet;
using RailwayManager.Maintenance.Assistant;
using RailwayManager.Personnel.Assistant;
using RailwayManager.Timetable;
using RailwayManager.Timetable.Assistant;
using DepotSystem.Assistant;
using TimetableObj = RailwayManager.Timetable.Timetable;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// M11 AS-3: kontrakty guidance-only capabilities (sufit AS-D5 na [1]: Plan null /
    /// Apply false / guidance zawsze obecne z kluczami i18n) + predykaty reguł Timetable
    /// + scene-safety reguł Depot (nieznany stan zajezdni w EditMode → reguły NIE strzelają)
    /// + kształt rejestracji bootstrapów wszystkich modułów.
    /// </summary>
    public class AssistantGuidanceCapabilitiesTests
    {
        readonly List<int> _routeIds = new();
        readonly List<int> _ttIds = new();

        [SetUp]
        public void SetUp()
        {
            AssistantCapabilityRegistry.Clear();
            AssistantRuleRegistry.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (int id in _ttIds)
            {
                var t = TimetableService.GetTimetable(id);
                if (t != null) TimetableService.Timetables.Remove(t);
            }
            foreach (int id in _routeIds)
            {
                var r = TimetableService.GetRoute(id);
                if (r != null) TimetableService.Routes.Remove(r);
            }
            _ttIds.Clear();
            _routeIds.Clear();
            AssistantCapabilityRegistry.Clear();
            AssistantRuleRegistry.Clear();
        }

        int MakeActiveTimetable(string startStation, string endStation)
        {
            var route = new Route { name = $"{startStation}->{endStation}" };
            route.stations.Add(new RouteStation { stationName = startStation, stationNodeId = startStation.GetHashCode() });
            route.stations.Add(new RouteStation { stationName = endStation, stationNodeId = endStation.GetHashCode() });
            TimetableService.AddRoute(route);
            _routeIds.Add(route.id);

            var tt = new TimetableObj
            {
                name = route.name,
                routeId = route.id,
                status = TimetableStatus.Active,
                calendar = new DayMask { bits = 0x7F },
                frequency = FrequencySpec.SingleRun(8 * 60),
                irjCategory = new IrjCategory(IrjGroup.RegionalLocal, TractionLetter.ElectricUnit),
                composition = new PlannedComposition { mode = CompositionMode.MultipleUnit }
            };
            tt.stops.Add(new TimetableStop { stationName = startStation, stationNodeId = route.stations[0].stationNodeId });
            tt.stops.Add(new TimetableStop { stationName = endStation, stationNodeId = route.stations[1].stationNodeId, plannedArrivalSec = 3600, plannedDepartureSec = 3600 });
            TimetableService.AddTimetable(tt);
            _ttIds.Add(tt.id);
            return tt.id;
        }

        // ────────────── Kontrakt guidance-only (sufit AS-D5) ──────────────

        [Test]
        public void GuidanceOnlyCapabilities_CeilingContractHolds()
        {
            var capabilities = new IAssistantCapability[]
            {
                new DepotBuildTrackCapability(),
                new FleetBuyCapability(),
                new FleetDeliverCapability(),
                new TimetableCreateCapability(),
                new PersonnelHireCapability(),
                new EconomyReviewCapability(),
                new DepotPlaceDeskCapability()
            };

            foreach (var cap in capabilities)
            {
                Assert.That(cap.CanAutoExecute, Is.False, $"{cap.Id}: guidance-only — sufit na [1]");
                Assert.That(cap.Plan(), Is.Null, $"{cap.Id}: bez mózgu Plan() == null");
                Assert.That(cap.Apply(new AssistantPlan { capabilityId = cap.Id }), Is.False,
                    $"{cap.Id}: Apply zawsze false (nie udaje że umie [3])");

                var guidance = cap.GetGuidance();
                Assert.That(guidance, Is.Not.Null, $"{cap.Id}: guidance nigdy null (AS-D5)");
                Assert.That(guidance.steps.Count, Is.GreaterThan(0), $"{cap.Id}: ma kroki");
                foreach (var step in guidance.steps)
                {
                    Assert.That(step.messageKey, Does.StartWith("assistant."),
                        $"{cap.Id}: klucze i18n z prefiksem assistant.");
                }
            }
        }

        [Test]
        public void DepotBuildTrack_FirstStepTargetsBuildTab()
        {
            var guidance = new DepotBuildTrackCapability().GetGuidance();
            Assert.That(guidance.steps[0].highlightTargetId, Is.EqualTo("depot.tab.Build"));
            Assert.That(guidance.steps[1].highlightTargetId, Is.EqualTo("depot.tool.BuildTrack"));
        }

        // ────────────── Predykaty reguł Timetable ──────────────

        [Test]
        public void TimetableRules_NoActiveTimetables_FlipsWithFixture()
        {
            Assert.That(TimetableAssistantRules.NoActiveTimetables(), Is.True,
                "Pusta pula → reguła ob.timetable aktywna");

            int ttId = MakeActiveTimetable("RuleAlfa", "RuleBeta");
            Assert.That(TimetableAssistantRules.NoActiveTimetables(), Is.False,
                "Active rozkład w puli → krok spełniony (reguła gaśnie)");

            var tt = TimetableService.GetTimetable(ttId);
            TimetableService.Timetables.Remove(tt);
            _ttIds.Remove(ttId);
            Assert.That(TimetableAssistantRules.NoActiveTimetables(), Is.True);
        }

        [Test]
        public void TimetableRules_CirculationRule_RequiresActiveTimetable()
        {
            Assert.That(TimetableAssistantRules.ActiveTimetablesButNoCirculations(), Is.False,
                "Bez rozkładów reguła ob.circulation nie strzela (sekwencja stanowa)");

            MakeActiveTimetable("RuleGamma", "RuleDelta");
            Assert.That(TimetableAssistantRules.ActiveTimetablesButNoCirculations(),
                Is.EqualTo(CirculationService.Circulations.Count == 0),
                "Z rozkładem: aktywna dokładnie gdy zero obiegów");
        }

        // ────────────── Scene-safety reguł Depot (preset-aware) ──────────────

        [Test]
        public void DepotRules_UnknownDepotState_DoNotFire()
        {
            // EditMode = brak sceny Depot → DepotReadiness.evaluated == false.
            // Kontrakt: na NIEZNANYM stanie reguły milczą (zero fałszywych szeptów
            // przed pierwszym wejściem do zajezdni / na presecie).
            Assert.That(DepotAssistantRules.NoTrackInKnownDepot(), Is.False,
                "Nieznany stan zajezdni → ob.track nie strzela");
            Assert.That(DepotAssistantRules.EmuTrapActive(), Is.False,
                "Nieznany stan → pułapka EMU nie strzela");
        }

        // ────────────── AS-6: predykaty reaktywne advisora ──────────────

        [Test]
        public void OrphanTimetableRule_FlipsWithCirculationMembership()
        {
            int tt1 = MakeActiveTimetable("OrphAlfa", "OrphBeta");
            int tt2 = MakeActiveTimetable("OrphBeta", "OrphAlfa");

            // Zero obiegów → milczy (pokrywa onboarding ob.circulation, nie advisor).
            if (CirculationService.Circulations.Count == 0)
            {
                Assert.That(TimetableAssistantRules.AnyOrphanTimetable(), Is.False,
                    "Bez obiegów reguła orphan nie strzela");
            }

            var c = CirculationService.AddCirculation(new Circulation { name = "OrphanFixture" });
            try
            {
                c.steps.Add(new CirculationStep(tt2, StepKind.Commercial));
                Assert.That(TimetableAssistantRules.AnyOrphanTimetable(), Is.True,
                    "tt1 poza obiegiem przy istniejących obiegach → orphan aktywny");

                c.steps.Add(new CirculationStep(tt1, StepKind.Commercial));
                // Oba nasze rozkłady w obiegu — przewidywalny tylko nasz wkład (pula globalna
                // może mieć cudze sieroty), więc sprawdzamy przez GetOrphanedTimetables.
                foreach (var orphan in CirculationService.GetOrphanedTimetables())
                {
                    Assert.That(orphan.id, Is.Not.EqualTo(tt1).And.Not.EqualTo(tt2),
                        "Nasze rozkłady przestały być sierotami po dopięciu do obiegu");
                }
            }
            finally
            {
                CirculationService.Circulations.Remove(c);
            }
        }

        [Test]
        public void VehicleIdleRule_FlipsWithAssignment()
        {
            const int vehicleId = 770001;
            var c = CirculationService.AddCirculation(new Circulation { name = "IdleFixture" });
            bool foreignIdle = TimetableAssistantRules.AnyTractionVehicleIdle(); // stan obcej puli

            var v = new FleetVehicleData
            {
                id = vehicleId,
                type = FleetVehicleType.EMU,
                status = FleetVehicleStatus.StoppedInDepot,
                conditionPercent = 100f
            };
            FleetService.AddOwnedVehicle(v);
            try
            {
                Assert.That(TimetableAssistantRules.AnyTractionVehicleIdle(), Is.True,
                    "Sprawny EMU w zajezdni bez przydziału przy istniejących obiegach → idle");

                c.assignedVehicleIds.Add(vehicleId);
                Assert.That(TimetableAssistantRules.AnyTractionVehicleIdle(), Is.EqualTo(foreignIdle),
                    "Po przypisaniu nasz pojazd nie podbija predykatu (wynik = stan obcy)");
            }
            finally
            {
                FleetService.RemoveOwnedVehicle(vehicleId);
                CirculationService.Circulations.Remove(c);
            }
        }

        [Test]
        public void BoredomCore_ArmsHoldsAndResets()
        {
            double held = -1;

            Assert.That(TimetableAssistantRules.BoredomCore(false, 1000, ref held), Is.False);
            Assert.That(held, Is.LessThan(0), "Niespełnione warunki → zegar nieuzbrojony");

            Assert.That(TimetableAssistantRules.BoredomCore(true, 1000, ref held), Is.False,
                "Pierwszy tick uzbraja zegar, jeszcze nie strzela");
            Assert.That(held, Is.EqualTo(1000));

            Assert.That(TimetableAssistantRules.BoredomCore(
                true, 1000 + AssistantConstants.BoredomHoldRealSec - 1, ref held), Is.False);
            Assert.That(TimetableAssistantRules.BoredomCore(
                true, 1000 + AssistantConstants.BoredomHoldRealSec, ref held), Is.True,
                "Po przetrzymaniu progu → nuda aktywna");

            Assert.That(TimetableAssistantRules.BoredomCore(false, 5000, ref held), Is.False);
            Assert.That(held, Is.LessThan(0), "Zerwanie warunków resetuje zegar");
        }

        [Test]
        public void UnprofitableRule_NullSafeWithoutEconomyManager()
        {
            // EditMode bez sceny: EconomyManager.Instance == null → predykat milczy.
            if (RailwayManager.Timetable.Economy.EconomyManager.Instance == null)
            {
                Assert.That(TimetableAssistantRules.AnyLineUnprofitable(), Is.False);
            }
        }

        [Test]
        public void WorkshopCapability_HeadlessContract()
        {
            // Interfejs, nie typ konkretny — AutoModeAllowed to DIM (dostępny tylko przez interfejs).
            IAssistantCapability cap = new WorkshopSendCapability();

            // EditMode bez sceny: WorkshopManager.Instance == null → Plan() null (kontrakt).
            if (RailwayManager.Maintenance.WorkshopManager.Instance == null)
            {
                Assert.That(cap.Plan(), Is.Null, "Bez WorkshopManagera nie ma czego planować");
            }

            Assert.That(cap.CanAutoExecute, Is.True, "Capability z mózgiem (Plan/Apply)");
            Assert.That(cap.AutoModeAllowed, Is.False,
                "Odstawienie do warsztatu wyłącza pojazd z ruchu — NIE addytywne, bez auto-mode (AS-D6)");
            Assert.That(cap.Apply(null), Is.False);
            Assert.That(cap.Apply(new AssistantPlan { capabilityId = "inna.capability" }), Is.False);

            var guidance = cap.GetGuidance();
            Assert.That(guidance.steps.Count, Is.GreaterThan(0));
            Assert.That(guidance.steps[0].uiIntent, Is.EqualTo(UIIntent.OpenWorkshopsPanel),
                "Pierwszy krok otwiera panel Warsztatów");
        }

        // ────────────── Kształt rejestracji bootstrapów ──────────────

        [Test]
        public void AllModuleBootstraps_RegisterCapabilitiesAndRules()
        {
            DepotAssistantBootstrap.EnsureRegistered();
            TimetableAssistantBootstrap.EnsureRegistered();
            PersonnelAssistantBootstrap.EnsureRegistered();
            MaintenanceAssistantBootstrap.EnsureRegistered();

            // Capabilities (10 = 7 guidance + 3 execution: obiegi AS-2 + grafiki AS-4 + warsztat AS-6).
            string[] expectedCaps =
            {
                "depot.buildTrack", "fleet.buy", "fleet.deliver", "depot.placeDesk",
                "timetable.create", "personnel.hire", "circulation.autogen", "crew.autogen",
                "economy.review", "maintenance.workshop"
            };
            foreach (var id in expectedCaps)
            {
                Assert.That(AssistantCapabilityRegistry.Get(id), Is.Not.Null, $"Brak capability '{id}'");
            }

            // Reguły: 6 onboardingu (sekwencja 1000..950) + 8 reaktywnych advisora
            // (AS-6: pełne „Reguły MVP" — brokenDown/emuTrap/crewless/orphan/idle/nierentowna/noDesk/nuda).
            (string id, int priority)[] expectedRules =
            {
                ("ob.track", 1000), ("ob.vehicle", 990), ("ob.deliver", 980),
                ("ob.timetable", 970), ("ob.circulation", 960), ("ob.crew", 950),
                ("reactive.brokenDown", 500), ("reactive.emuTrap", 450), ("reactive.crewless", 400),
                ("reactive.orphanTimetable", 350), ("reactive.vehicleIdle", 300),
                ("reactive.unprofitable", 250), ("reactive.noDesk", 200), ("reactive.bored", 50)
            };
            foreach (var (id, priority) in expectedRules)
            {
                var rule = AssistantRuleRegistry.Get(id);
                Assert.That(rule, Is.Not.Null, $"Brak reguły '{id}'");
                Assert.That(rule.priority, Is.EqualTo(priority), $"Priorytet '{id}'");
                Assert.That(AssistantCapabilityRegistry.Get(rule.capabilityId), Is.Not.Null,
                    $"Reguła '{id}' wskazuje na niezarejestrowaną capability '{rule.capabilityId}'");
            }

            // Idempotencja (drugie wywołanie nie dubluje).
            int caps = AssistantCapabilityRegistry.Count;
            int rules = AssistantRuleRegistry.Count;
            DepotAssistantBootstrap.EnsureRegistered();
            TimetableAssistantBootstrap.EnsureRegistered();
            PersonnelAssistantBootstrap.EnsureRegistered();
            MaintenanceAssistantBootstrap.EnsureRegistered();
            Assert.That(AssistantCapabilityRegistry.Count, Is.EqualTo(caps));
            Assert.That(AssistantRuleRegistry.Count, Is.EqualTo(rules));
        }
    }
}
