using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using DepotSystem;
using DepotSystem.Undo;
using RailwayManager.Core;
using RailwayManager.Economy;

namespace RailwayManager.Tests.PlayMode
{
    /// <summary>
    /// TD-010 PlayMode: atomowe cofnięcie schematu (SchemaPlacementCommand). Kluczowe asercje:
    /// (1) schemat z rozjazdem → undo usuwa WSZYSTKO bez osieroconego odtworzonego chainu (wcześniej:
    /// RemoveTurnout odtwarzał chain jako nowy tor którego per-element undo nie sprzątał = zombie),
    /// (2) cykl place→atomic-undo = net-zero billing, (3) schemat samych torów też cofa się jednym krokiem.
    /// Harness wzorowany na DepotConstructionBillingPlayTests (izolacja TD-038).
    /// </summary>
    public class DepotSchemaUndoPlayTests
    {
        GameObject _go;
        TrackGraph _graph;
        PrefabTrackBuilder _builder;
        TurnoutPlacer _placer;
        long _moneyBackup;

        const long StartMoneyZl = 100_000_000L;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            PauseStack.Clear();
            GameState.IsPaused = false;

            foreach (var b in UnityEngine.Object.FindObjectsByType<PrefabTrackBuilder>(FindObjectsSortMode.None))
                UnityEngine.Object.DestroyImmediate(b.gameObject);
            foreach (var g in UnityEngine.Object.FindObjectsByType<TrackGraph>(FindObjectsSortMode.None))
                UnityEngine.Object.DestroyImmediate(g.gameObject);

            _go = new GameObject("TestSchemaUndo");
            _graph = _go.AddComponent<TrackGraph>();
            _builder = _go.AddComponent<PrefabTrackBuilder>();
            _placer = _go.AddComponent<TurnoutPlacer>();
            DepotServices.Invalidate<TrackGraph>();
            DepotServices.Invalidate<PrefabTrackBuilder>();
            DepotServices.Invalidate<TurnoutPlacer>();

            UndoManager.ClearAll();
            UndoManager.Silenced = false;
            ConstructionBilling.SuppressCharging = false;
            MoneyLedger.ResetAll();
            _moneyBackup = GameState.Money;
            GameState.Money = StartMoneyZl;
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) UnityEngine.Object.DestroyImmediate(_go);
            UndoManager.ClearAll();
            UndoManager.Silenced = false;
            ConstructionBilling.SuppressCharging = false;
            MoneyLedger.ResetAll();
            GameState.Money = _moneyBackup;
            DepotServices.Invalidate<TrackGraph>();
            DepotServices.Invalidate<PrefabTrackBuilder>();
            DepotServices.Invalidate<TurnoutPlacer>();
            LogAssert.ignoreFailingMessages = false;
        }

        int LiveTrackCount() => _builder.PlacedTracks.Count(t => t != null && t.GraphTrackId >= 0);
        List<int> LiveTrackIds() => _builder.PlacedTracks.Where(t => t != null && t.GraphTrackId >= 0)
                                            .Select(t => t.GraphTrackId).ToList();

        static void AssertBalance(long expectedZl, string msg)
            => Assert.LessOrEqual(Math.Abs(GameState.Money - expectedZl), 1L,
                $"{msg} (saldo={GameState.Money}, oczekiwane={expectedZl} ±1 zł akumulator groszy)");

        /// <summary>Undo komendy jak przez UndoManager.UndoTop (suppress nagrywania w trakcie).</summary>
        void RunUndo(IUndoCommand cmd)
        {
            bool prev = UndoManager.Silenced;
            UndoManager.Silenced = true;
            try { cmd.Undo(); }
            finally { UndoManager.Silenced = prev; }
        }

        [UnityTest]
        public IEnumerator SchemaWithTurnout_AtomicUndo_NoOrphanTrack_NetZero()
        {
            long start = GameState.Money;

            // "Schemat": tor chain + rozjazd na nim (charged jak w realnym ConfirmPlacement).
            var baseSeg = _builder.PlaceTrackWithPolyline(
                TrackGeometry.GenerateStraightLine(new Vector3(0, 0, 0), new Vector3(100, 0, 0)),
                "Schema chain", DepotTrackType.Parking);
            Assert.IsNotNull(baseSeg);

            var chain = _placer.FindStraightChain(baseSeg);
            Assert.IsNotNull(chain);
            // PlaceTurnoutOnChain ZWRACA id toru odgałęziającego, NIE id encji rozjazdu —
            // encję bierzemy z TurnoutEntities (jak DepotConstructionBillingPlayTests).
            int divergingTrackId = _placer.PlaceTurnoutOnChain(chain, 50f, TurnoutData.R190_1_9, divergeLeft: true);
            Assert.GreaterOrEqual(divergingTrackId, 0, "Rozjazd schematu postawiony.");
            Assert.AreEqual(1, _builder.TurnoutEntities.Count);
            int realTurnoutId = _builder.TurnoutEntities.Keys.First();
            Assert.Greater(LiveTrackCount(), 1, "Po rozjeździe jest pre/body/post/diverging.");

            // Finalny zbiór id schematu (jak mySchemaSegments) + id ENCJI rozjazdu.
            var cmd = new SchemaPlacementCommand(LiveTrackIds(), new List<int> { realTurnoutId });

            RunUndo(cmd);

            Assert.AreEqual(0, _builder.TurnoutEntities.Count, "Rozjazd usunięty.");
            var leftover = _builder.PlacedTracks.Where(t => t != null && t.GraphTrackId >= 0)
                .Select(t => $"id={t.GraphTrackId} perm={_graph.GetTrack(t.GraphTrackId)?.IsPermanent} member={_builder.TryGetTurnoutForTrack(t.GraphTrackId, out _)}")
                .ToList();
            Assert.AreEqual(0, leftover.Count,
                "ZERO torów po atomic-undo — w tym odtworzony chain (regresja: zostawał zombie-tor). Zostało: " + string.Join(" | ", leftover));
            AssertBalance(start, "Schemat z rozjazdem: place→atomic-undo = net-zero");
            yield return null;
        }

        [UnityTest]
        public IEnumerator SchemaTracksOnly_AtomicUndo_RemovesAll_NetZero()
        {
            long start = GameState.Money;

            var t1 = _builder.PlaceTrackWithPolyline(
                TrackGeometry.GenerateStraightLine(new Vector3(0, 0, 0), new Vector3(40, 0, 0)),
                "Schema track 1", DepotTrackType.Parking);
            var t2 = _builder.PlaceTrackWithPolyline(
                TrackGeometry.GenerateStraightLine(new Vector3(0, 0, 6), new Vector3(40, 0, 6)),
                "Schema track 2", DepotTrackType.Parking);
            Assert.IsNotNull(t1); Assert.IsNotNull(t2);
            Assert.AreEqual(2, LiveTrackCount());

            var cmd = new SchemaPlacementCommand(LiveTrackIds(), new List<int>());
            RunUndo(cmd);

            Assert.AreEqual(0, LiveTrackCount(), "Oba tory schematu cofnięte jednym krokiem.");
            AssertBalance(start, "Schemat samych torów: place→atomic-undo = net-zero");
            yield return null;
        }
    }
}
