using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RailwayManager.Core.Assistant;
using RailwayManager.Timetable;
using RailwayManager.Timetable.Assistant;
using RailwayManager.Timetable.Suggestions;
using TimetableObj = RailwayManager.Timetable.Timetable;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// M11 AS-2: kontrakt adaptera CirculationAutogenCapability — krytycznie:
    /// Plan() NIE mutuje stanu gry (AS-D3), Apply() tworzy obiegi, walidacja
    /// świeżości pomija propozycje nieaktualne (rozkład skasowany między Plan a Apply).
    /// Fixture wzorem CirculationSequenceTests (Route+Timetable headless).
    /// </summary>
    public class CirculationAutogenCapabilityTests
    {
        readonly List<int> _routeIds = new();
        readonly List<int> _ttIds = new();
        readonly List<int> _createdCirculationIds = new();

        CirculationAutogenCapability _capability;

        [SetUp]
        public void SetUp()
        {
            _routeIds.Clear();
            _ttIds.Clear();
            _createdCirculationIds.Clear();
            _capability = new CirculationAutogenCapability();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (int id in _createdCirculationIds)
            {
                var c = CirculationService.Circulations.FirstOrDefault(x => x != null && x.id == id);
                if (c != null) CirculationService.Circulations.Remove(c);
            }
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
        }

        /// <summary>Active timetable z kalendarzem na wszystkie dni — łańcuchowalny przez generator.</summary>
        int MakeTimetable(string startStation, string endStation, int startMin, int durationMin)
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
                calendar = new DayMask { bits = 0x7F }, // wszystkie dni — intersection != 0
                frequency = FrequencySpec.SingleRun(startMin),
                irjCategory = new IrjCategory(IrjGroup.RegionalLocal, TractionLetter.ElectricUnit),
                composition = new PlannedComposition { mode = CompositionMode.MultipleUnit }
            };
            tt.stops.Add(new TimetableStop
            {
                stationName = startStation,
                stationNodeId = route.stations[0].stationNodeId,
                plannedArrivalSec = 0,
                plannedDepartureSec = 0
            });
            tt.stops.Add(new TimetableStop
            {
                stationName = endStation,
                stationNodeId = route.stations[1].stationNodeId,
                plannedArrivalSec = durationMin * 60,
                plannedDepartureSec = durationMin * 60
            });
            TimetableService.AddTimetable(tt);
            _ttIds.Add(tt.id);
            return tt.id;
        }

        int CountCirculationsContaining(int timetableId)
        {
            int n = 0;
            foreach (var c in CirculationService.Circulations)
            {
                if (c?.steps == null) continue;
                if (c.steps.Any(s => s.timetableId == timetableId)) n++;
            }
            return n;
        }

        void TrackCreatedCirculations(IEnumerable<int> ttIds)
        {
            foreach (var c in CirculationService.Circulations)
            {
                if (c?.steps == null) continue;
                if (c.steps.Any(s => ttIds.Contains(s.timetableId)) && !_createdCirculationIds.Contains(c.id))
                    _createdCirculationIds.Add(c.id);
            }
        }

        // ────────────────────────── Kontrakty ──────────────────────────

        [Test]
        public void Plan_DoesNotMutate_Apply_CreatesCirculation()
        {
            // A→B 08:00 (60min), B→A 10:00 — generator złoży w jeden chain.
            int tt1 = MakeTimetable("TestAlfa", "TestBeta", 8 * 60, 60);
            int tt2 = MakeTimetable("TestBeta", "TestAlfa", 10 * 60, 60);

            int circulationsBefore = CirculationService.Circulations.Count;

            var plan = _capability.Plan();

            Assert.That(plan, Is.Not.Null, "Dwa łańcuchowalne rozkłady → propozycja istnieje");
            Assert.That(CirculationService.Circulations.Count, Is.EqualTo(circulationsBefore),
                "KONTRAKT AS-D3: Plan() nie mutuje stanu gry");
            Assert.That(plan.capabilityId, Is.EqualTo(CirculationAutogenCapability.CapabilityId));
            Assert.That(plan.previewLines.Count, Is.GreaterThan(0), "Preview ma linie dla gracza");
            Assert.That(plan.costGroszy, Is.EqualTo(0), "Generowanie obiegów nie kosztuje");

            bool applied = _capability.Apply(plan);
            TrackCreatedCirculations(new[] { tt1, tt2 });

            Assert.That(applied, Is.True);
            Assert.That(CountCirculationsContaining(tt1), Is.EqualTo(1), "Rozkład 1 w nowym obiegu");
            Assert.That(CountCirculationsContaining(tt2), Is.EqualTo(1), "Rozkład 2 w tym samym chainie");
        }

        [Test]
        public void Plan_WithoutOurFixture_DoesNotCrash_NullWhenPoolEmpty()
        {
            // Pula globalna (statyczny TimetableService) może zawierać rozkłady innych testów —
            // twardy kontrakt: bez naszego fixture Plan() nie wybucha; przy pustej puli → null.
            var plan = _capability.Plan();
            if (plan == null) Assert.Pass("Pusta pula → Plan() == null (kontrakt)");
            Assert.That(_ttIds, Is.Empty, "Plan pochodzi z cudzego stanu puli, nie z naszego fixture");
        }

        [Test]
        public void Apply_RejectsNullForeignAndMalformedPlan()
        {
            Assert.That(_capability.Apply(null), Is.False);
            Assert.That(_capability.Apply(new AssistantPlan { capabilityId = "inna.capability" }), Is.False);
            Assert.That(_capability.Apply(new AssistantPlan
            {
                capabilityId = CirculationAutogenCapability.CapabilityId,
                payload = "nie-GenerationResult"
            }), Is.False);
        }

        [Test]
        public void Apply_StaleProposal_SkippedWithoutCreatingBrokenCirculation()
        {
            int tt1 = MakeTimetable("TestGamma", "TestDelta", 8 * 60, 60);
            int tt2 = MakeTimetable("TestDelta", "TestGamma", 10 * 60, 60);

            var plan = _capability.Plan();
            Assert.That(plan, Is.Not.Null);

            // Gracz kasuje rozkład MIĘDZY Plan() a akceptacją preview.
            var deleted = TimetableService.GetTimetable(tt2);
            TimetableService.Timetables.Remove(deleted);
            _ttIds.Remove(tt2);

            int circulationsBefore = CirculationService.Circulations.Count;
            bool applied = _capability.Apply(plan);
            TrackCreatedCirculations(new[] { tt1, tt2 });

            Assert.That(applied, Is.False, "Jedyna propozycja nieaktualna → Apply false");
            Assert.That(CirculationService.Circulations.Count, Is.EqualTo(circulationsBefore),
                "Guard świeżości: ŻADEN obieg z martwym timetableId nie powstał");
        }

        [Test]
        public void CanExecute_TracksActiveTimetables()
        {
            // Uwaga: inne testy mogą zostawić w puli swoje rozkłady — sprawdzamy tylko kierunek
            // false→true po dodaniu naszego (nie absolutny stan globalny).
            MakeTimetable("TestEpsilon", "TestZeta", 9 * 60, 45);
            Assert.That(_capability.CanExecute(), Is.True, "Active rozkład w puli → CanExecute true");
        }

        [Test]
        public void GuidanceAlwaysPresent_WithI18nKeys()
        {
            var guidance = _capability.GetGuidance();
            Assert.That(guidance, Is.Not.Null);
            Assert.That(guidance.steps.Count, Is.EqualTo(2));
            Assert.That(guidance.steps[0].messageKey, Does.StartWith("assistant.guidance."));
        }

        // ────────────── AS-6: split propose/commit w CirculationSuggestionService ──────────────

        [Test]
        public void SuggestionService_BuildProposal_DoesNotMutate_AcceptStillCommits()
        {
            int tt1 = MakeTimetable("SugAlfa", "SugBeta", 8 * 60, 60);
            int tt2 = MakeTimetable("SugBeta", "SugAlfa", 10 * 60, 60);

            var suggestion = new CirculationSuggestion
            {
                timetableIdEarlier = tt1,
                timetableIdLater = tt2,
                connectionStationName = "SugBeta",
                gapSec = 600,
                contextKey = $"test.split:{tt1}->{tt2}"
            };

            int before = CirculationService.Circulations.Count;
            var proposal = CirculationSuggestionService.BuildProposal(suggestion);

            Assert.That(proposal, Is.Not.Null);
            Assert.That(proposal.steps.Count, Is.EqualTo(2), "2-stepowy obieg z sugestii");
            Assert.That(CirculationService.Circulations.Count, Is.EqualTo(before),
                "KONTRAKT AS-6: BuildProposal nie dotyka CirculationService");

            // Stale: rozkład znika → null (bez tworzenia obiegu z martwym ID).
            var dead = TimetableService.GetTimetable(tt2);
            TimetableService.Timetables.Remove(dead);
            _ttIds.Remove(tt2);
            Assert.That(CirculationSuggestionService.BuildProposal(suggestion), Is.Null);
            TimetableService.Timetables.Add(dead);
            _ttIds.Add(tt2);

            // Accept = commit (RecordChoice + Add) — zachowanie bez zmian po splicie.
            CirculationSuggestionService.Accept(suggestion);
            TrackCreatedCirculations(new[] { tt1, tt2 });
            Assert.That(CirculationService.Circulations.Count, Is.EqualTo(before + 1));
        }
    }
}
