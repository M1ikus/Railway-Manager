using System.Collections;
using System.Reflection;
using System.Runtime.Serialization;
using NUnit.Framework;
using RailwayManager.Core;
using RailwayManager.Timetable;
using RailwayManager.Timetable.Simulation;
using UnityEngine;
using TimetableObj = RailwayManager.Timetable.Timetable;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// Charakteryzacja rozstrzygania konfliktu na wspolnym bloku (osobowy vs pospieszny) —
    /// odtwarza decyzje "kto jedzie pierwszy" przez REALNE prywatne metody
    /// TrainRunSimulator.GetTrainPriority + HasHigherPriorityWaiting + SyncBlockWaitIndex.
    ///
    /// Scenariusz pytania: osobowy ma planowy postoj w X, spozniony pospieszny ma go wyprzedzic.
    /// Test pokazuje DWIE wlasciwosci, ktore daja odpowiedz:
    ///  (T1) priorytet jest REAKTYWNY — ustapienie liczy sie tylko gdy OBA pociagi fizycznie
    ///       stoja (BlockedBySignal) przed tym samym blokiem; wtedy wygrywa wyzszy IRJ.
    ///  (T2) BRAK predykcyjnego trzymania — osobowy NIE ustepuje pospiesznemu ktory dopiero
    ///       NADJEZDZA (state=Running, nie BlockedBySignal). To dokladnie odtwarza "wypuszczenie
    ///       osobowego": spozniony pospieszny nie jest jeszcze w kolejce -> osobowy rusza.
    ///  (T3) godzina szczytu: osobowy (boost do 9) BIJE pospieszny (5) — inwersja.
    ///  (T4) tie-break: przy rownym priorytecie jedzie bardziej opozniony.
    ///
    /// Dodatkowo (zweryfikowane code-readingiem, nie unit-testowalne bez realnej trasy mapy):
    /// odjazd z planowego postoju w Advance (branch StoppedAtStation) jest CZYSTO CZASOWY —
    /// w ogole NIE wola HasHigherPriorityWaiting. Stad osobowy zawsze rusza o swojej (ew.
    /// skompresowanej) godzinie, niezaleznie od nadjezdzajacego pospiesznego.
    ///
    /// SimulatedTrain budowany przez GetUninitializedObject + reflection (pola blokow sa
    /// readonly, normalnie liczone w ctorze z grafu OSM — tu ustawiamy je wprost).
    /// </summary>
    public class OvertakePriorityTests
    {
        const int SharedBlock = 99;

        TrainRunSimulator _sim;
        MethodInfo _getPrio, _hasHigherPrio, _syncIdx;
        FieldInfo _fActive;

        string _dateBackup;
        int _dayBackup;
        float _timeBackup;

        [SetUp]
        public void SetUp()
        {
            _dateBackup = GameState.GameStartDateIso;
            _dayBackup = GameState.GameDay;
            _timeBackup = GameState.GameTimeSeconds;

            var go = new GameObject("TRS_OvertakeTest");
            // AddComponent w EditMode NIE wola Awake (brak DontDestroyOnLoad / spawnu serwisow).
            // Dicty (_activeTrains/_trainsWaitingForBlock/_currentlyWaitingForBlock) sa readonly=new()
            // field-init -> gotowe po konstrukcji.
            _sim = go.AddComponent<TrainRunSimulator>();

            var t = typeof(TrainRunSimulator);
            _getPrio = t.GetMethod("GetTrainPriority", BindingFlags.Instance | BindingFlags.NonPublic);
            _hasHigherPrio = t.GetMethod("HasHigherPriorityWaiting", BindingFlags.Instance | BindingFlags.NonPublic);
            _syncIdx = t.GetMethod("SyncBlockWaitIndex", BindingFlags.Instance | BindingFlags.NonPublic);
            _fActive = t.GetField("_activeTrains", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(_getPrio, Is.Not.Null, "GetTrainPriority powinno istniec.");
            Assert.That(_hasHigherPrio, Is.Not.Null, "HasHigherPriorityWaiting powinno istniec.");
            Assert.That(_syncIdx, Is.Not.Null, "SyncBlockWaitIndex powinno istniec.");
            Assert.That(_fActive, Is.Not.Null, "_activeTrains powinno istniec.");

            // Domyslnie POZA szczytem (12:00 — poza oknami 6-9 i 13-16) -> osobowy ma bazowe 3.
            GameState.GameStartDateIso = "2026-06-01";
            GameState.GameDay = 0;
            GameState.GameTimeSeconds = 12f * 3600f;
        }

        [TearDown]
        public void TearDown()
        {
            if (_sim != null) Object.DestroyImmediate(_sim.gameObject);
            GameState.GameStartDateIso = _dateBackup;
            GameState.GameDay = _dayBackup;
            GameState.GameTimeSeconds = _timeBackup;
        }

        // ── T1: oba stoja na wspolnym bloku -> wygrywa pospieszny ──
        [Test]
        public void BothBlocked_PassengerYieldsToExpress()
        {
            var osobowy = MakeTrain(1, IrjGroup.RegionalLocal, TrainState.BlockedBySignal, new[] { 10, SharedBlock }, delaySec: 0);
            var pospieszny = MakeTrain(2, IrjGroup.RegionalFast, TrainState.BlockedBySignal, new[] { 20, SharedBlock }, delaySec: 300);
            Register(osobowy); Register(pospieszny);

            Assert.That(Prio(osobowy), Is.EqualTo(3), "Osobowy poza szczytem = priorytet 3.");
            Assert.That(Prio(pospieszny), Is.EqualTo(5), "Pospieszny = priorytet 5.");

            Assert.That(Yields(osobowy), Is.True,
                "Osobowy USTEPUJE — pospieszny (5>3) tez czeka na ten sam blok (kolizja fizyczna).");
            Assert.That(Yields(pospieszny), Is.False,
                "Pospieszny NIE ustepuje nikomu — ma wyzszy priorytet.");
        }

        // ── T2: SEDNO scenariusza — osobowy NIE czeka na nadjezdzajacy pospieszny ──
        [Test]
        public void ApproachingExpress_DoesNotHoldPassenger()
        {
            // Osobowy fizycznie czeka na blok 99 (BlockedBySignal).
            var osobowy = MakeTrain(1, IrjGroup.RegionalLocal, TrainState.BlockedBySignal, new[] { 10, SharedBlock }, delaySec: 0);
            // Pospieszny JEDZIE (Running) — spozniony, jeszcze nie dojechal do bloku, nie stoi w kolejce.
            var pospieszny = MakeTrain(2, IrjGroup.RegionalFast, TrainState.Running, new[] { 20, SharedBlock }, delaySec: 600);
            Register(osobowy); Register(pospieszny);

            Assert.That(Yields(osobowy), Is.False,
                "Osobowy NIE ustepuje pospiesznemu ktory dopiero NADJEZDZA (Running). Priorytet jest reaktywny, " +
                "nie predykcyjny -> osobowy zostaje wypuszczony, a spozniony pospieszny dostanie jeszcze wieksze opoznienie.");
        }

        // ── T3: godzina szczytu -> osobowy (9) bije pospieszny (5) ──
        [Test]
        public void RushHour_PassengerOutranksExpress()
        {
            SetRushHourWeekday();

            var osobowy = MakeTrain(1, IrjGroup.RegionalLocal, TrainState.BlockedBySignal, new[] { 10, SharedBlock }, delaySec: 0);
            var pospieszny = MakeTrain(2, IrjGroup.RegionalFast, TrainState.BlockedBySignal, new[] { 20, SharedBlock }, delaySec: 0);
            Register(osobowy); Register(pospieszny);

            Assert.That(Prio(osobowy), Is.EqualTo(9), "Osobowy w szczycie = boost do 9.");
            Assert.That(Prio(pospieszny), Is.EqualTo(5), "Pospieszny pozostaje 5.");

            Assert.That(Yields(osobowy), Is.False, "W szczycie osobowy NIE ustepuje.");
            Assert.That(Yields(pospieszny), Is.True, "W szczycie pospieszny USTEPUJE osobowemu (9>5) — inwersja priorytetu.");
        }

        // ── T4: rowny priorytet -> jedzie bardziej opozniony ──
        [Test]
        public void SamePriority_MoreDelayedGoesFirst()
        {
            var early = MakeTrain(1, IrjGroup.RegionalLocal, TrainState.BlockedBySignal, new[] { 10, SharedBlock }, delaySec: 0);
            var late = MakeTrain(2, IrjGroup.RegionalLocal, TrainState.BlockedBySignal, new[] { 20, SharedBlock }, delaySec: 600);
            Register(early); Register(late);

            Assert.That(Yields(early), Is.True, "Przy rownym priorytecie USTEPUJE mniej opozniony.");
            Assert.That(Yields(late), Is.False, "Bardziej opozniony jedzie pierwszy (tie-break wg currentDelaySec).");
        }

        // ── Helpers ──────────────────────────────────────────────────

        SimulatedTrain MakeTrain(int id, IrjGroup group, TrainState state, int[] blockKeys, int delaySec)
        {
            var st = (SimulatedTrain)FormatterServices.GetUninitializedObject(typeof(SimulatedTrain));
            SetField(st, "trainRun", new TrainRun { id = id, currentDelaySec = delaySec });
            SetField(st, "timetable", new TimetableObj { id = id, irjCategory = new IrjCategory(group, default) });
            SetField(st, "state", state);
            SetField(st, "currentBlockIndex", 0);          // jedzie do bloku index 1 = SharedBlock
            SetField(st, "routeBlockKeys", blockKeys);
            SetField(st, "routeBlockCount", blockKeys.Length);
            return st;
        }

        void Register(SimulatedTrain st)
        {
            var active = (IDictionary)_fActive.GetValue(_sim);
            active[st.trainRun.id] = st;
            _syncIdx.Invoke(_sim, new object[] { st }); // realny sync wpisu w block-wait index
        }

        int Prio(SimulatedTrain st) => (int)_getPrio.Invoke(_sim, new object[] { st });

        bool Yields(SimulatedTrain st) =>
            (bool)_hasHigherPrio.Invoke(_sim, new object[] { SharedBlock, st.trainRun.id, Prio(st) });

        void SetRushHourWeekday()
        {
            GameState.GameStartDateIso = "2026-06-01";
            GameState.GameTimeSeconds = 7f * 3600f; // 07:00 — okno szczytu (6-9)
            for (int d = 0; d < 7; d++)
            {
                GameState.GameDay = d;
                if (IsoTime.TryParseDate(GameState.CurrentDateIso, out var date) &&
                    date.DayOfWeek != System.DayOfWeek.Saturday &&
                    date.DayOfWeek != System.DayOfWeek.Sunday)
                    return;
            }
        }

        static void SetField(object o, string name, object val)
        {
            var fi = o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(fi, Is.Not.Null, $"Pole '{name}' powinno istniec na {o.GetType().Name}.");
            fi.SetValue(o, val);
        }
    }
}
