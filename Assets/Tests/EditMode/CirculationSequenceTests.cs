using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RailwayManager.Timetable;
using TimetableObj = RailwayManager.Timetable.Timetable;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// M5: testy CirculationValidator.ValidateSequence / GetNextStepErrors — reguły spójności
    /// obiegu (łańcuch rozkładów wykonywanych przez ten sam tabor). Czysta logika, EditMode.
    ///
    /// Reguły: (1) stacja końcowa kroku N == startowa N+1, (2) N+1 startuje po końcu N
    /// (z roll-over nocnym), (3) reverse margin EMU 2min / Loco 10min (Warning, nie blok),
    /// (4) ServiceClass — nie mieszać Local+Express (Deadhead neutralny), (5) CompositionMode
    /// EMU vs Loco+Wagony muszą być spójne.
    /// </summary>
    public class CirculationSequenceTests
    {
        readonly List<int> _routeIds = new();
        readonly List<int> _ttIds = new();

        [SetUp]
        public void SetUp() { _routeIds.Clear(); _ttIds.Clear(); }

        [TearDown]
        public void TearDown()
        {
            foreach (int id in _ttIds) { var t = TimetableService.GetTimetable(id); if (t != null) TimetableService.Timetables.Remove(t); }
            foreach (int id in _routeIds) { var r = TimetableService.GetRoute(id); if (r != null) TimetableService.Routes.Remove(r); }
        }

        /// <summary>
        /// Tworzy Timetable: trasa startStation→endStation, odjazd startMin, jazda durationMin.
        /// </summary>
        int MakeTimetable(string startStation, string endStation, int startMin, int durationMin,
                          IrjGroup group = IrjGroup.RegionalLocal,
                          CompositionMode mode = CompositionMode.MultipleUnit)
        {
            var route = new Route { name = $"{startStation}->{endStation}" };
            route.stations.Add(new RouteStation { stationName = startStation, stationNodeId = startStation.GetHashCode() });
            route.stations.Add(new RouteStation { stationName = endStation, stationNodeId = endStation.GetHashCode() });
            TimetableService.AddRoute(route);
            _routeIds.Add(route.id);

            var tt = new TimetableObj
            {
                name = route.name, routeId = route.id,
                frequency = FrequencySpec.SingleRun(startMin),
                irjCategory = new IrjCategory(group, TractionLetter.ElectricUnit),
                composition = new PlannedComposition { mode = mode }
            };
            // stops: [0] start (dep=0), [1] end (arr=dep=durationMin*60). EndMinutes = start + duration.
            tt.stops.Add(new TimetableStop { stationName = startStation, stationNodeId = route.stations[0].stationNodeId,
                                             plannedArrivalSec = 0, plannedDepartureSec = 0 });
            tt.stops.Add(new TimetableStop { stationName = endStation, stationNodeId = route.stations[1].stationNodeId,
                                             plannedArrivalSec = durationMin * 60, plannedDepartureSec = durationMin * 60 });
            TimetableService.AddTimetable(tt);
            _ttIds.Add(tt.id);
            return tt.id;
        }

        static List<CirculationStep> Steps(params int[] ids) => ids.Select(id => new CirculationStep(id)).ToList();

        [Test]
        public void ValidSequence_StationsLinkAndTimeOk_NoIssues()
        {
            // A→B (08:00, 60min → koniec 09:00), B→A (10:00) — spina się, czas OK, margines duży.
            var s = Steps(MakeTimetable("A", "B", 8 * 60, 60), MakeTimetable("B", "A", 10 * 60, 60));
            var issues = CirculationValidator.ValidateSequence(s);
            Assert.That(issues, Is.Empty, "Poprawna sekwencja — brak uwag.");
        }

        [Test]
        public void StationsDoNotLink_ReturnsError()
        {
            // A→B potem C→A: koniec B != start C → Error spójności.
            var s = Steps(MakeTimetable("A", "B", 8 * 60, 60), MakeTimetable("C", "A", 10 * 60, 60));
            var issues = CirculationValidator.ValidateSequence(s);
            Assert.That(issues.Any(i => i.severity == CirculationValidator.IssueSeverity.Error
                                     && i.message.Contains("spina")), Is.True,
                "Niespinające się stacje → Error.");
        }

        [Test]
        public void NextStartsEarlierClock_TreatedAsNextDay_NoTimeError()
        {
            // A→B (10:00, 120min → koniec 12:00), B→A (10:30 = wcześniej na zegarze) — walidator
            // rolluje na następną dobę (obieg przez północ jest legalny), więc NIE zgłasza Error
            // czasowego. Stacje się spinają (B→B), margines duży. Dokumentuje intencjonalny roll-over.
            var s = Steps(MakeTimetable("A", "B", 10 * 60, 120), MakeTimetable("B", "A", 10 * 60 + 30, 60));
            var issues = CirculationValidator.ValidateSequence(s);
            Assert.That(issues.Any(i => i.severity == CirculationValidator.IssueSeverity.Error), Is.False,
                "Wcześniejsza godzina = następna doba (obieg nocny) → brak Error czasowego.");
        }

        [Test]
        public void ShortTurnaround_EmuMargin_ReturnsWarning()
        {
            // EMU: koniec 09:00, następny 09:01 → gap 1min < 2min margines EMU → Warning (nie Error).
            var s = Steps(
                MakeTimetable("A", "B", 8 * 60, 60, mode: CompositionMode.MultipleUnit),
                MakeTimetable("B", "A", 9 * 60 + 1, 60, mode: CompositionMode.MultipleUnit));
            var issues = CirculationValidator.ValidateSequence(s);
            Assert.That(issues.Any(i => i.severity == CirculationValidator.IssueSeverity.Warning), Is.True,
                "Krótki obrót < margines → Warning.");
            Assert.That(issues.Any(i => i.severity == CirculationValidator.IssueSeverity.Error), Is.False,
                "Krótki obrót to Warning, NIE Error (gracz może zatwierdzić).");
        }

        [Test]
        public void GetNextStepErrors_IncompatibleServiceClass_ReturnsError()
        {
            // Obieg ma krok Local (RegionalLocal), próba dodania Express → niezgodna klasa usługi.
            var current = Steps(MakeTimetable("A", "B", 8 * 60, 60, group: IrjGroup.RegionalLocal));
            int expressId = MakeTimetable("B", "C", 10 * 60, 60, group: IrjGroup.ExpressDomestic);

            var issues = CirculationValidator.GetNextStepErrors(current, expressId);

            Assert.That(issues.Any(i => i.severity == CirculationValidator.IssueSeverity.Error), Is.True,
                "Mieszanie Local + Express → Error klasy usługi.");
        }

        [Test]
        public void GetNextStepErrors_IncompatibleCompositionMode_ReturnsError()
        {
            // Obieg EMU, próba dodania Loco+Wagony → niezgodny typ taboru.
            var current = Steps(MakeTimetable("A", "B", 8 * 60, 60, mode: CompositionMode.MultipleUnit));
            int locoId = MakeTimetable("B", "C", 10 * 60, 60, mode: CompositionMode.LocoWithCars);

            var issues = CirculationValidator.GetNextStepErrors(current, locoId);

            Assert.That(issues.Any(i => i.severity == CirculationValidator.IssueSeverity.Error), Is.True,
                "Mieszanie EMU + Lok+Wagony → Error typu taboru.");
        }

        [Test]
        public void GetNextStepErrors_FirstStep_AlwaysOk()
        {
            // Pierwszy krok (pusta dotychczasowa sekwencja) — nie ma z czym walidować.
            var issues = CirculationValidator.GetNextStepErrors(new List<CirculationStep>(),
                MakeTimetable("A", "B", 8 * 60, 60));
            Assert.That(issues, Is.Empty);
        }
    }
}
