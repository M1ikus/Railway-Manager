using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using DepotSystem;
using RailwayManager.Core;
using RailwayManager.Economy;

namespace RailwayManager.Tests.PlayMode
{
    /// <summary>
    /// TD-035 PlayMode: księgowość budowy rozjazdów — pełne lustro place↔remove.
    /// Kluczowe asercje: cykl place→remove = net-zero (obie drukarki pieniędzy martwe),
    /// krzyżownica pobiera mechanizm 900M gr + restore toru działa, para atomowa,
    /// branch-return pobiera 2 mechanizmy. Kalkulatory pokryte EditMode (ConstructionCostsBillingTests).
    /// </summary>
    public class DepotConstructionBillingPlayTests
    {
        GameObject _go;
        TrackGraph _graph;
        PrefabTrackBuilder _builder;
        TurnoutPlacer _placer;
        long _moneyBackup;

        const long StartMoneyZl = 100_000_000L; // 100 mln zł — pokrywa wszystkie scenariusze

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            PauseStack.Clear();
            GameState.IsPaused = false;

            // Izolacja (TD-038 lekcja): zabij wycieki z poprzednich klas zanim postawimy świeże.
            foreach (var b in UnityEngine.Object.FindObjectsByType<PrefabTrackBuilder>(FindObjectsSortMode.None))
                UnityEngine.Object.DestroyImmediate(b.gameObject);
            foreach (var g in UnityEngine.Object.FindObjectsByType<TrackGraph>(FindObjectsSortMode.None))
                UnityEngine.Object.DestroyImmediate(g.gameObject);

            _go = new GameObject("TestBilling");
            _graph = _go.AddComponent<TrackGraph>();
            _builder = _go.AddComponent<PrefabTrackBuilder>();
            _placer = _go.AddComponent<TurnoutPlacer>();
            DepotServices.Invalidate<TrackGraph>();
            DepotServices.Invalidate<PrefabTrackBuilder>();
            DepotServices.Invalidate<TurnoutPlacer>();

            DepotSystem.Undo.UndoManager.ClearAll();
            DepotSystem.Undo.UndoManager.Silenced = false;
            ConstructionBilling.SuppressCharging = false;
            MoneyLedger.ResetAll();
            _moneyBackup = GameState.Money;
            GameState.Money = StartMoneyZl;
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) UnityEngine.Object.DestroyImmediate(_go);
            DepotSystem.Undo.UndoManager.ClearAll();
            DepotSystem.Undo.UndoManager.Silenced = false;
            ConstructionBilling.SuppressCharging = false;
            MoneyLedger.ResetAll();
            GameState.Money = _moneyBackup;
            DepotServices.Invalidate<TrackGraph>();
            DepotServices.Invalidate<PrefabTrackBuilder>();
            DepotServices.Invalidate<TurnoutPlacer>();
            LogAssert.ignoreFailingMessages = false;
        }

        // ── Helpers ───────────────────────────────────────────────────

        /// <summary>Prosty tor A→B postawiony POZA billingiem (suppress) — czysty punkt startu salda.</summary>
        PlacedTrackSegment PlaceBaseTrack(Vector3 a, Vector3 b, string name = "Tor bazowy")
        {
            bool prev = ConstructionBilling.SuppressCharging;
            ConstructionBilling.SuppressCharging = true;
            try { return _builder.PlaceTrackWithPolyline(TrackGeometry.GenerateStraightLine(a, b), name, DepotTrackType.Parking); }
            finally { ConstructionBilling.SuppressCharging = prev; }
        }

        StraightChain ChainOf(PlacedTrackSegment seg)
        {
            var chain = _placer.FindStraightChain(seg);
            Assert.IsNotNull(chain, "FindStraightChain na prostym torze musi zwrócić chain.");
            return chain;
        }

        static void AssertBalance(long expectedZl, string msg)
            => Assert.LessOrEqual(Math.Abs(GameState.Money - expectedZl), 1L,
                $"{msg} (saldo={GameState.Money}, oczekiwane={expectedZl} ±1 zł akumulator groszy)");

        // ── Testy ─────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator SingleTurnout_PlaceRemoveLoop_NetZero()
        {
            PlaceBaseTrack(new Vector3(0, 0, 0), new Vector3(100, 0, 0));
            long start = GameState.Money;

            for (int i = 0; i < 3; i++)
            {
                var seg = _builder.PlacedTracks.First(t => t.GraphTrackId >= 0);
                int res = _placer.PlaceTurnoutOnChain(ChainOf(seg), 50f, TurnoutData.R190_1_9, divergeLeft: true);
                Assert.GreaterOrEqual(res, 0, $"Cykl {i}: place się udał.");
                Assert.AreEqual(1, _builder.TurnoutEntities.Count, $"Cykl {i}: 1 rozjazd.");

                long afterPlace = GameState.Money;
                long dropZl = start - afterPlace;
                // Mechanizm R190 = 3.5 mln zł + nowa geometria odnogi (~33 m ≈ 165 tys zł)
                Assert.GreaterOrEqual(dropZl, 3_500_000L, $"Cykl {i}: pobrany co najmniej mechanizm.");
                Assert.LessOrEqual(dropZl, 4_100_000L, $"Cykl {i}: bez nadmiarowego charge.");

                int turnoutId = _builder.TurnoutEntities.Keys.First();
                _builder.RemoveTurnout(turnoutId);
                Assert.AreEqual(0, _builder.TurnoutEntities.Count, $"Cykl {i}: rozjazd usunięty.");
                AssertBalance(start, $"Cykl {i}: place→remove = net-zero (drukarka #1 martwa)");
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator Crossover_ChargesMechanism_RemoveRestoresNetZero()
        {
            PlaceBaseTrack(new Vector3(0, 0, 0), new Vector3(120, 0, 0));
            long start = GameState.Money;
            var seg = _builder.PlacedTracks.First(t => t.GraphTrackId >= 0);

            bool ok = _placer.PlaceCrossoverOnChain(ChainOf(seg), 40f, TurnoutData.Crossover_R190, flip: false, divergeLeft: true);
            Assert.IsTrue(ok, "Place krzyżownicy udany.");
            Assert.AreEqual(1, _builder.TurnoutEntities.Count, "1 encja krzyżownicy.");
            var entity = _builder.TurnoutEntities.Values.First();
            Assert.AreEqual(TurnoutEntityType.Crossover, entity.Type);
            Assert.IsNotNull(entity.OriginalPolyline, "TD-035: Original* metadata ustawione (restore + undo).");

            long dropZl = start - GameState.Money;
            // Mechanizm krzyżownicy = 9 mln zł (KrzyzownicaPodwojna, nie fallback R190) + nogi/przekątna
            Assert.GreaterOrEqual(dropZl, 9_000_000L, "Pobrany mechanizm 900M gr (drukarka #2: brak charge naprawiony).");
            Assert.LessOrEqual(dropZl, 10_000_000L, "Bez nadmiarowego charge (geometria nóg ~setki tys zł).");

            _builder.RemoveTurnout(entity.TurnoutId);
            Assert.AreEqual(0, _builder.TurnoutEntities.Count, "Krzyżownica usunięta.");
            Assert.AreEqual(1, _builder.PlacedTracks.Count(t => t.GraphTrackId >= 0),
                "Oryginalny tor odtworzony (wcześniej: dziura po krzyżownicy).");
            AssertBalance(start, "Cykl krzyżownicy = net-zero (refund == to co pobrano)");
            yield return null;
        }

        [UnityTest]
        public IEnumerator Pair_InsufficientFunds_NothingPlaced()
        {
            var segA = PlaceBaseTrack(new Vector3(0, 0, 0), new Vector3(100, 0, 0), "Tor A");
            var segB = PlaceBaseTrack(new Vector3(0, 0, 6), new Vector3(100, 0, 6), "Tor B");
            GameState.Money = 5_000_000L; // 5 mln < 7 mln (2× R190)
            int tracksBefore = _builder.PlacedTracks.Count;

            _placer.PlaceTurnoutPairOnChains(ChainOf(segA), 50f, ChainOf(segB), 60f,
                TurnoutData.R190_1_9, TurnoutData.R190_1_9, divergeLeft: true, flipDirection: false);

            Assert.AreEqual(0, _builder.TurnoutEntities.Count, "Atomowość: NIC nie postawione (nie pół pary).");
            Assert.AreEqual(tracksBefore, _builder.PlacedTracks.Count, "Tory nietknięte.");
            Assert.AreEqual(5_000_000L, GameState.Money, "Saldo nietknięte.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator Pair_Affordable_ChargesTwoMechanisms()
        {
            var segA = PlaceBaseTrack(new Vector3(0, 0, 0), new Vector3(100, 0, 0), "Tor A");
            var segB = PlaceBaseTrack(new Vector3(0, 0, 6), new Vector3(100, 0, 6), "Tor B");
            long start = GameState.Money;

            _placer.PlaceTurnoutPairOnChains(ChainOf(segA), 50f, ChainOf(segB), 60f,
                TurnoutData.R190_1_9, TurnoutData.R190_1_9, divergeLeft: true, flipDirection: false);

            Assert.AreEqual(2, _builder.TurnoutEntities.Count, "Para = 2 rozjazdy.");
            long dropZl = start - GameState.Money;
            Assert.GreaterOrEqual(dropZl, 7_000_000L, "2× mechanizm R190 pobrany.");
            Assert.LessOrEqual(dropZl, 8_500_000L, "Bez nadmiarowego charge (geometria odnóg+wstawka).");
            yield return null;
        }

        [UnityTest]
        public IEnumerator BranchReturn_ChargesBothMechanisms()
        {
            PlaceBaseTrack(new Vector3(0, 0, 0), new Vector3(100, 0, 0));
            long start = GameState.Money;
            var seg = _builder.PlacedTracks.First(t => t.GraphTrackId >= 0);

            _placer.PlaceBranchWithReturn(ChainOf(seg), 30f, TurnoutData.R190_1_9,
                divergeLeft: true, flipDirection: false,
                trackSpacing: 6f, returnType: 1, returnRadius: 300f);

            Assert.AreEqual(2, _builder.TurnoutEntities.Count, "Główny + powrotny zarejestrowane.");
            long dropZl = start - GameState.Money;
            Assert.GreaterOrEqual(dropZl, 7_000_000L,
                "2× mechanizm R190 (wcześniej powrotny = 0 zł → refund niezapłaconego przy remove).");
            yield return null;
        }
    }
}
