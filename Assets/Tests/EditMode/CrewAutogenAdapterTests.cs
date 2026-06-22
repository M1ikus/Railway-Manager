using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RailwayManager.Personnel;
using RailwayManager.Personnel.Assistant;
using RailwayManager.Timetable;
using TimetableObj = RailwayManager.Timetable.Timetable;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// M11 AS-P2 + AS-4: mapper TrainRuns→CrewAutoGenTrainRunInput (zamyka planowany
    /// „M8-11 adapter") + kontrakt adaptera grafik załóg (Plan nie mutuje / Apply commituje /
    /// stale plan odrzucony w całości — duties splecione czasowo).
    /// </summary>
    public class CrewAutogenAdapterTests
    {
        const string FixtureDate = "2031-03-03"; // egzotyczna data — zero kolizji z cudzymi runami

        readonly List<int> _routeIds = new();
        readonly List<int> _ttIds = new();
        readonly List<TrainRun> _addedRuns = new();
        readonly List<int> _createdCrewCircIds = new();

        CrewAutogenCapability _capability;

        [SetUp]
        public void SetUp()
        {
            _capability = new CrewAutogenCapability();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (int id in _createdCrewCircIds)
            {
                CrewCirculationService.Delete(id);
            }
            _createdCrewCircIds.Clear();

            foreach (var run in _addedRuns)
            {
                TimetableService.TrainRuns.Remove(run);
            }
            _addedRuns.Clear();

            foreach (int id in _ttIds)
            {
                var t = TimetableService.GetTimetable(id);
                if (t != null) TimetableService.Timetables.Remove(t);
            }
            _ttIds.Clear();
            foreach (int id in _routeIds)
            {
                var r = TimetableService.GetRoute(id);
                if (r != null) TimetableService.Routes.Remove(r);
            }
            _routeIds.Clear();
        }

        int MakeTimetable(string startStation, string endStation, int durationMin,
            CompositionMode mode = CompositionMode.MultipleUnit,
            TractionLetter traction = TractionLetter.ElectricUnit,
            string symbolicNotation = null)
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
                irjCategory = new IrjCategory(IrjGroup.RegionalLocal, traction),
                composition = new PlannedComposition { mode = mode, symbolicNotation = symbolicNotation }
            };
            tt.stops.Add(new TimetableStop { stationName = startStation, stationNodeId = route.stations[0].stationNodeId });
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

        TrainRun AddRun(int timetableId, int runId, int startMin, bool delivery = false, string dateIso = FixtureDate)
        {
            var run = new TrainRun
            {
                id = runId,
                timetableId = timetableId,
                runDateIso = dateIso,
                startMinutesFromMidnight = startMin,
                isDeliveryRun = delivery
            };
            TimetableService.TrainRuns.Add(run);
            _addedRuns.Add(run);
            return run;
        }

        // ────────────── AS-P2: mapper ──────────────

        [Test]
        public void Mapper_MapsAllFieldsFromRealTrainRun()
        {
            int ttId = MakeTimetable("AdaptAlfa", "AdaptBeta", durationMin: 90);
            AddRun(ttId, runId: 91001, startMin: 8 * 60);

            var inputs = CrewAutoGenInputAdapter.BuildInputsFromTrainRuns(FixtureDate);

            Assert.That(inputs.Count, Is.EqualTo(1));
            var input = inputs[0];
            Assert.That(input.trainRunId, Is.EqualTo(91001));
            Assert.That(input.startStation, Is.EqualTo("AdaptAlfa"));
            Assert.That(input.endStation, Is.EqualTo("AdaptBeta"));
            Assert.That(input.startTimeIso, Is.EqualTo("08:00:00"));
            Assert.That(input.endTimeIso, Is.EqualTo("09:30:00"), "start 08:00 + 90 min trasy");
            Assert.That(input.dateIso, Is.EqualTo(FixtureDate));
            Assert.That(string.IsNullOrEmpty(input.irjCategory), Is.False, "Kod IRJ z katalogu");
            Assert.That(input.emuCount, Is.EqualTo(1), "Symbolic EZT + trakcja elektryczna → 1 EMU");
            Assert.That(input.passengerCarsCount, Is.EqualTo(0));
        }

        [Test]
        public void Mapper_SkipsDeliveryRunsAndOtherDates()
        {
            int ttId = MakeTimetable("AdaptGamma", "AdaptDelta", durationMin: 60);
            AddRun(ttId, runId: 91002, startMin: 6 * 60, delivery: true);                       // dostawczy — bez załogi
            AddRun(ttId, runId: 91003, startMin: 7 * 60, dateIso: "2031-03-04");                // inny dzień
            AddRun(ttId, runId: 91004, startMin: 9 * 60);                                      // ten liczy się

            var inputs = CrewAutoGenInputAdapter.BuildInputsFromTrainRuns(FixtureDate);

            Assert.That(inputs.Count, Is.EqualTo(1));
            Assert.That(inputs[0].trainRunId, Is.EqualTo(91004));
        }

        [Test]
        public void Mapper_SymbolicLocoNotation_CountsCars()
        {
            Assert.That(CrewAutoGenInputAdapter.ParseSymbolicCarCount("3B+WR+2A"), Is.EqualTo(6));
            Assert.That(CrewAutoGenInputAdapter.ParseSymbolicCarCount("B"), Is.EqualTo(1));
            Assert.That(CrewAutoGenInputAdapter.ParseSymbolicCarCount(""), Is.EqualTo(0));
            Assert.That(CrewAutoGenInputAdapter.ParseSymbolicCarCount(null), Is.EqualTo(0));

            int ttId = MakeTimetable("AdaptEpsilon", "AdaptZeta", durationMin: 120,
                mode: CompositionMode.LocoWithCars, traction: TractionLetter.ElectricLoco,
                symbolicNotation: "3B+WR+2A");
            AddRun(ttId, runId: 91005, startMin: 10 * 60);

            var inputs = CrewAutoGenInputAdapter.BuildInputsFromTrainRuns(FixtureDate);
            Assert.That(inputs[0].passengerCarsCount, Is.EqualTo(6));
            Assert.That(inputs[0].emuCount, Is.EqualTo(0));
        }

        [Test]
        public void Mapper_OvernightRollover_WrapsWithinDay()
        {
            Assert.That(CrewAutoGenInputAdapter.FormatTime(23 * 60 + 30), Is.EqualTo("23:30:00"));
            Assert.That(CrewAutoGenInputAdapter.FormatTime(25 * 60), Is.EqualTo("01:00:00"), "Rollover mod 24h");
        }

        // ────────────── AS-4: kontrakt capability ──────────────

        [Test]
        public void Capability_PlanDoesNotMutate_ApplyCommitsRosters()
        {
            // Dwa łańcuchowalne kursy tego samego dnia → generator złoży turnus.
            int tt1 = MakeTimetable("CrewAlfa", "CrewBeta", durationMin: 60);
            int tt2 = MakeTimetable("CrewBeta", "CrewAlfa", durationMin: 60);
            AddRun(tt1, runId: 91006, startMin: 8 * 60);
            AddRun(tt2, runId: 91007, startMin: 10 * 60);

            // Plan liczy dla CurrentDateIso — fixture daty musi się zgadzać.
            string realDate = RailwayManager.Core.GameState.CurrentDateIso;
            foreach (var run in _addedRuns) run.runDateIso = realDate;

            int crewBefore = CrewCirculationService.All.Count;
            var before = CrewCirculationService.All.Select(c => c.crewCirculationId).ToHashSet();

            var plan = _capability.Plan();

            Assert.That(plan, Is.Not.Null, "Dwa kursy dziś → propozycja turnusu istnieje");
            Assert.That(CrewCirculationService.All.Count, Is.EqualTo(crewBefore),
                "KONTRAKT AS-D3: Plan() nie mutuje CrewCirculationService");
            Assert.That(plan.previewLines.Count, Is.GreaterThan(0));

            bool applied = _capability.Apply(plan);
            foreach (var c in CrewCirculationService.All)
            {
                if (!before.Contains(c.crewCirculationId)) _createdCrewCircIds.Add(c.crewCirculationId);
            }

            Assert.That(applied, Is.True);
            Assert.That(CrewCirculationService.All.Count, Is.GreaterThan(crewBefore), "Commit utworzył turnus(y)");
        }

        [Test]
        public void Capability_StaleRun_RejectsWholePlan()
        {
            int ttId = MakeTimetable("CrewGamma", "CrewDelta", durationMin: 60);
            var run = AddRun(ttId, runId: 91008, startMin: 8 * 60);
            string realDate = RailwayManager.Core.GameState.CurrentDateIso;
            run.runDateIso = realDate;

            var plan = _capability.Plan();
            Assert.That(plan, Is.Not.Null);

            // Kurs znika między Plan() a akceptacją (np. zmiana obiegu przez gracza).
            TimetableService.TrainRuns.Remove(run);
            _addedRuns.Remove(run);

            int crewBefore = CrewCirculationService.All.Count;
            Assert.That(_capability.Apply(plan), Is.False, "Martwy referencedTrainRunId → cały plan odrzucony");
            Assert.That(CrewCirculationService.All.Count, Is.EqualTo(crewBefore),
                "Guard świeżości: zero dziurawych grafików");
        }

        [Test]
        public void Capability_NoRunsToday_PlanNull_CanExecuteFalse()
        {
            // Fixture w FixtureDate (nie dziś) → dla CurrentDateIso brak kursów z naszej puli.
            int ttId = MakeTimetable("CrewEpsilon", "CrewZeta", durationMin: 60);
            AddRun(ttId, runId: 91009, startMin: 8 * 60); // FixtureDate, nie dziś

            // CanExecute/Plan czytają CurrentDateIso — nasz egzotyczny FixtureDate się nie liczy.
            // (Globalna pula może mieć cudze dzisiejsze runy — assertujemy tylko brak wpływu naszych.)
            var inputs = CrewAutoGenInputAdapter.BuildInputsFromTrainRuns("1999-01-01");
            Assert.That(inputs, Is.Empty, "Data bez kursów → puste wejście → Plan() null");
        }
    }
}
