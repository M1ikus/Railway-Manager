using System.Collections;
using System.Collections.Generic;
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
    /// M-Dispatch Faza 2: test INTEGRACYJNY orchestracji w TrainRunSimulator —
    /// ShouldHoldForPredictiveDispatch buduje prognozy me + klastra (z _activeTrains) i pyta
    /// decidera. Dowodzi ze cala sciezka (forecast -> prefilter klastra -> decyzja) dziala
    /// end-to-end, bez sceny/mapy (SimulatedTrain budowane przez reflection).
    /// </summary>
    public class DispatchIntegrationTests
    {
        const int SharedBlock = 99;

        TrainRunSimulator _sim;
        MethodInfo _shouldHold;
        FieldInfo _fActive;
        string _dateBackup; int _dayBackup; float _timeBackup; bool _enabledBackup;
        DispatchPolicy _policyBackup;

        [SetUp]
        public void SetUp()
        {
            _dateBackup = GameState.GameStartDateIso;
            _dayBackup = GameState.GameDay;
            _timeBackup = GameState.GameTimeSeconds;
            _enabledBackup = TrainRunSimulator.PredictiveDispatchEnabled;
            _policyBackup = DispatchPolicyService.CurrentPolicy;

            var go = new GameObject("TRS_DispatchTest");
            _sim = go.AddComponent<TrainRunSimulator>(); // bez Awake w EditMode
            var t = typeof(TrainRunSimulator);
            _shouldHold = t.GetMethod("ShouldHoldForPredictiveDispatch", BindingFlags.Instance | BindingFlags.NonPublic);
            _fActive = t.GetField("_activeTrains", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(_shouldHold, Is.Not.Null, "ShouldHoldForPredictiveDispatch powinno istniec.");
            Assert.That(_fActive, Is.Not.Null, "_activeTrains powinno istniec.");

            TrainRunSimulator.PredictiveDispatchEnabled = true;
            DispatchPolicyService.CurrentPolicy = DispatchPolicy.Balanced;
            GameState.GameStartDateIso = "2026-06-01";
            GameState.GameDay = 0;
            GameState.GameTimeSeconds = 12f * 3600f; // poludnie -> brak rush hour (osobowy = prio 3)
        }

        [TearDown]
        public void TearDown()
        {
            if (_sim != null) Object.DestroyImmediate(_sim.gameObject);
            TrainRunSimulator.PredictiveDispatchEnabled = _enabledBackup;
            DispatchPolicyService.CurrentPolicy = _policyBackup;
            GameState.GameStartDateIso = _dateBackup;
            GameState.GameDay = _dayBackup;
            GameState.GameTimeSeconds = _timeBackup;
        }

        [Test]
        public void Holds_WhenHigherValueExpressWouldBeBlocked_AndWorthIt()
        {
            double now = GameState.GameTimeSeconds;

            // Osobowy (prio 3) gotowy do odjazdu: jego najblizszy blok to wspolny 99, dlugi
            // (4000m / 20 m/s = 200s okupacji) -> wjechawszy zablokowalby pospieszny na dlugo.
            var osobowy = Build(1, IrjGroup.RegionalLocal, TrainState.StoppedAtStation,
                blockKeys: new[] { SharedBlock }, entry: new[] { 0f }, exit: new[] { 4000f }, vmaxMps: 20f);

            // Pospieszny (prio 5) nadjezdza: blok 60 -> wspolny 99 (wjazd ~25s, wyjazd ~75s).
            var pospieszny = Build(2, IrjGroup.RegionalFast, TrainState.Running,
                blockKeys: new[] { 60, SharedBlock }, entry: new[] { 0f, 500f }, exit: new[] { 500f, 1500f }, vmaxMps: 20f);

            AddActive(osobowy); AddActive(pospieszny);

            Assert.That(Invoke(osobowy, now), Is.True,
                "Osobowy TRZYMA — przepuszcza pospieszny, ktorego by dlugo zablokowal (holdCost < releaseCost).");
            Assert.That(Invoke(pospieszny, now), Is.False,
                "Pospieszny nie ma kogo przepuszczac (wyzszy priorytet) -> jedzie.");
        }

        [Test]
        public void DoesNotHold_WhenRivalIsLowerPriority()
        {
            double now = GameState.GameTimeSeconds;

            // Ten sam uklad, ale role priorytetow odwrocone: "me" jest pospieszny (5), rywal osobowy (3).
            var pospieszny = Build(1, IrjGroup.RegionalFast, TrainState.StoppedAtStation,
                blockKeys: new[] { SharedBlock }, entry: new[] { 0f }, exit: new[] { 4000f }, vmaxMps: 20f);
            var osobowy = Build(2, IrjGroup.RegionalLocal, TrainState.Running,
                blockKeys: new[] { 60, SharedBlock }, entry: new[] { 0f, 500f }, exit: new[] { 500f, 1500f }, vmaxMps: 20f);

            AddActive(pospieszny); AddActive(osobowy);

            Assert.That(Invoke(pospieszny, now), Is.False,
                "Pospieszny NIE ustepuje nizej-wazonemu osobowemu -> jedzie.");
        }

        [Test]
        public void DoesNotHold_WhenPolicyOff()
        {
            double now = GameState.GameTimeSeconds;

            // Ten sam uklad co Holds_When..., ale polityka Off -> brak predykcyjnego trzymania.
            var osobowy = Build(1, IrjGroup.RegionalLocal, TrainState.StoppedAtStation,
                blockKeys: new[] { SharedBlock }, entry: new[] { 0f }, exit: new[] { 4000f }, vmaxMps: 20f);
            var pospieszny = Build(2, IrjGroup.RegionalFast, TrainState.Running,
                blockKeys: new[] { 60, SharedBlock }, entry: new[] { 0f, 500f }, exit: new[] { 500f, 1500f }, vmaxMps: 20f);
            AddActive(osobowy); AddActive(pospieszny);

            DispatchPolicyService.CurrentPolicy = DispatchPolicy.Off;

            Assert.That(Invoke(osobowy, now), Is.False,
                "Polityka Off -> dispatcher nie trzyma (czysto reaktywna warstwa blokowa).");
        }

        // ── Helpers ──────────────────────────────────────────────────

        bool Invoke(SimulatedTrain me, double nowSec) =>
            (bool)_shouldHold.Invoke(_sim, new object[] { me, nowSec });

        void AddActive(SimulatedTrain st)
        {
            var active = (IDictionary)_fActive.GetValue(_sim);
            active[st.trainRun.id] = st;
        }

        SimulatedTrain Build(int id, IrjGroup group, TrainState state,
            int[] blockKeys, float[] entry, float[] exit, float vmaxMps)
        {
            var st = (SimulatedTrain)FormatterServices.GetUninitializedObject(typeof(SimulatedTrain));
            SetField(st, "trainRun", new TrainRun { id = id, currentPositionOnRouteM = 0f, currentDelaySec = 0 });
            SetField(st, "timetable", new TimetableObj
            {
                id = id,
                irjCategory = new IrjCategory(group, default),
                stops = new List<TimetableStop>()
            });
            SetField(st, "state", state);
            SetField(st, "currentBlockIndex", 0);
            SetField(st, "routeBlockCount", blockKeys.Length);
            SetField(st, "routeBlockKeys", blockKeys);
            SetField(st, "blockEntryDistM", entry);
            SetField(st, "blockExitDistM", exit);
            SetField(st, "segmentEndDistM", new float[] { 1e9f });
            SetField(st, "segmentMaxSpeedMps", new float[] { vmaxMps });
            SetField(st, "stopDistancesM", new float[0]);
            SetField(st, "departureTimeOfDaySec", 0f);
            return st;
        }

        static void SetField(object o, string name, object val)
        {
            var fi = o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(fi, Is.Not.Null, $"Pole '{name}' powinno istniec na {o.GetType().Name}.");
            fi.SetValue(o, val);
        }
    }
}
