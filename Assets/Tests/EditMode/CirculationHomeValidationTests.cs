using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RailwayManager.Core;
using RailwayManager.Timetable;
// 'Timetable' to zarówno namespace (RailwayManager.Timetable) jak i typ — alias rozstrzyga CS0118.
using TimetableObj = RailwayManager.Timetable.Timetable;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// M9c-D F6: testy CirculationValidator.ValidateHomeStation — gate który decyduje czy obieg
    /// może być aktywowany. KRYTYCZNE: w F6 zmieniono z log-warn na twardą blokadę w SetStatus,
    /// bo niespójny home → pociąg spawnuje się w złym miejscu. Czysta logika (TimetableService
    /// + GameState.HomeDepotStationId), EditMode.
    ///
    /// Kontrakt: pierwszy krok MUSI startować z home (Error gdy nie); ostatni krok poza home =
    /// tylko Warning (pojazd zostaje na peronie, nie wraca do depot — dozwolone). Home<0 = no-op.
    /// </summary>
    public class CirculationHomeValidationTests
    {
        const int HomeNode = 1000;
        const int AwayNode = 2000;

        int _homeBackup;
        readonly List<int> _routeIds = new();
        readonly List<int> _ttIds = new();

        [SetUp]
        public void SetUp()
        {
            _homeBackup = GameState.HomeDepotStationId;
            GameState.HomeDepotStationId = HomeNode;
            _routeIds.Clear();
            _ttIds.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            // Sprzątanie syntetycznych Route/Timetable z globalnego TimetableService.
            foreach (int id in _ttIds)
            {
                var tt = TimetableService.GetTimetable(id);
                if (tt != null) TimetableService.Timetables.Remove(tt);
            }
            foreach (int id in _routeIds)
            {
                var r = TimetableService.GetRoute(id);
                if (r != null) TimetableService.Routes.Remove(r);
            }
            GameState.HomeDepotStationId = _homeBackup;
        }

        /// <summary>Tworzy Timetable z trasą startNode → endNode (2 stacje). Zwraca timetableId.</summary>
        int MakeStep(int startNode, int endNode)
        {
            var route = new Route { name = $"R{startNode}->{endNode}" };
            route.stations.Add(new RouteStation { stationNodeId = startNode, stationName = $"S{startNode}" });
            route.stations.Add(new RouteStation { stationNodeId = endNode, stationName = $"S{endNode}" });
            TimetableService.AddRoute(route);
            _routeIds.Add(route.id);

            var tt = new TimetableObj { name = route.name, routeId = route.id };
            TimetableService.AddTimetable(tt);
            _ttIds.Add(tt.id);
            return tt.id;
        }

        static List<CirculationStep> Steps(params int[] timetableIds)
            => timetableIds.Select(id => new CirculationStep(id)).ToList();

        [Test]
        public void HomeNotSet_NoIssues()
        {
            GameState.HomeDepotStationId = -1;
            var steps = Steps(MakeStep(AwayNode, HomeNode)); // celowo zły start

            var issues = CirculationValidator.ValidateHomeStation(steps);

            Assert.That(issues, Is.Empty, "Bez ustawionego home (grace period) walidacja nie zgłasza nic.");
        }

        [Test]
        public void FirstStepNotFromHome_ReturnsError()
        {
            var steps = Steps(MakeStep(AwayNode, HomeNode)); // start poza home

            var issues = CirculationValidator.ValidateHomeStation(steps);

            Assert.That(issues.Any(i => i.severity == CirculationValidator.IssueSeverity.Error), Is.True,
                "Pierwszy krok nie z home → Error (blokuje aktywację w F6).");
            Assert.That(issues.First(i => i.severity == CirculationValidator.IssueSeverity.Error).stepIndex,
                Is.EqualTo(0), "Error dotyczy pierwszego kroku.");
        }

        [Test]
        public void FirstFromHome_LastNotHome_OnlyWarning()
        {
            var steps = Steps(MakeStep(HomeNode, AwayNode)); // start home, koniec poza home

            var issues = CirculationValidator.ValidateHomeStation(steps);

            Assert.That(issues.Any(i => i.severity == CirculationValidator.IssueSeverity.Error), Is.False,
                "Start z home → brak Error.");
            Assert.That(issues.Any(i => i.severity == CirculationValidator.IssueSeverity.Warning), Is.True,
                "Koniec poza home → Warning (pojazd zostaje na peronie, dozwolone — nie blokuje).");
        }

        [Test]
        public void FirstFromHome_LastToHome_NoIssues()
        {
            // Round-trip: home → away (krok 1), away → home (krok 2). Idealny obieg.
            var steps = Steps(MakeStep(HomeNode, AwayNode), MakeStep(AwayNode, HomeNode));

            var issues = CirculationValidator.ValidateHomeStation(steps);

            Assert.That(issues, Is.Empty, "Start i koniec w home → brak uwag.");
        }

        [Test]
        public void EmptySteps_NoIssues()
        {
            Assert.That(CirculationValidator.ValidateHomeStation(new List<CirculationStep>()), Is.Empty);
            Assert.That(CirculationValidator.ValidateHomeStation(null), Is.Empty);
        }
    }
}
