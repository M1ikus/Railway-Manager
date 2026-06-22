using System.Text;
using UnityEngine;

namespace RailwayManager.Core
{
    /// <summary>
    /// Smoke test regresji dla refactor'ów Core z 2026-05-13.
    ///
    /// Sprawdza invariants:
    /// - <see cref="GameState"/>.Money/GlobalReputation properties z events
    /// - GlobalReputation clamp [0, 100]
    /// - IsHomeDepotSet helper
    /// - <see cref="VehicleLocationService"/> per-type index correctness przy transitions
    /// - <see cref="RandomRegistry"/> determinism (sekwencja po ResetAll identyczna)
    /// - <see cref="DeterministicRng"/>.Range(int,int) rejection sampling — bias smoke check
    ///
    /// Każdy test ContextMenu jest niezależny — możesz uruchamiać selektywnie. Wszystkie
    /// piszą wynik przez <see cref="Log.Info"/> z prefiksem `[CoreSmokeTest]` i markerem
    /// PASS/FAIL na końcu wiersza. Nie modyfikują state'u trwale (przywracają backup
    /// na końcu).
    ///
    /// Konwencja projektu (CLAUDE.md): brak Unity Test Framework — smoke tests +
    /// `[ContextMenu]` ręczne uruchamianie w Editor.
    /// </summary>
    public class CoreSmokeTest : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn()
        {
            if (Object.FindAnyObjectByType<CoreSmokeTest>() != null) return;
            var go = new GameObject("CoreSmokeTest (auto-spawn)");
            go.AddComponent<CoreSmokeTest>();
        }

        [ContextMenu("Core: Run ALL smoke tests")]
        public void RunAll()
        {
            TestMoneyEvent();
            TestReputationClamp();
            TestIsHomeDepotSet();
            TestVehicleLocationServiceIndex();
            TestRandomRegistryDeterminism();
            TestRandomRegistryRangeBias();
        }

        // ── Test 1: GameState.Money event ───────────────────────────────

        [ContextMenu("Core: Test Money property + OnMoneyChanged event")]
        public void TestMoneyEvent()
        {
            var sb = new StringBuilder();
            sb.AppendLine("[CoreSmokeTest] Money property + OnMoneyChanged event:");

            long backupMoney = GameState.Money;
            int eventCount = 0;
            long lastOld = -1;
            long lastNew = -1;
            System.Action<long, long> handler = (oldV, newV) =>
            {
                eventCount++;
                lastOld = oldV;
                lastNew = newV;
            };
            GameState.OnMoneyChanged += handler;

            // Test 1a: zmiana wartości → 1 event z poprawnymi argumentami
            GameState.Money = 12345L;
            bool t1a_ok = eventCount == 1 && lastOld == backupMoney && lastNew == 12345L;
            sb.AppendLine($"  1a) set 12345 → event(old={lastOld},new={lastNew}) count={eventCount} " + (t1a_ok ? "PASS" : "FAIL"));

            // Test 1b: identyczna wartość → brak event (property guard)
            eventCount = 0;
            GameState.Money = 12345L;
            bool t1b_ok = eventCount == 0;
            sb.AppendLine($"  1b) set 12345 again → count={eventCount} (expected 0, guard early-return) " + (t1b_ok ? "PASS" : "FAIL"));

            // Test 1c: += syntax działa identycznie jak field
            eventCount = 0;
            GameState.Money += 100L;
            bool t1c_ok = eventCount == 1 && GameState.Money == 12445L;
            sb.AppendLine($"  1c) Money += 100 → Money={GameState.Money} count={eventCount} " + (t1c_ok ? "PASS" : "FAIL"));

            GameState.OnMoneyChanged -= handler;
            GameState.Money = backupMoney; // restore
            sb.AppendLine($"  Result: {(t1a_ok && t1b_ok && t1c_ok ? "ALL PASS" : "SOMETHING FAILED")}");
            Log.Info(sb.ToString());
        }

        // ── Test 2: GlobalReputation clamp [0, 100] ─────────────────────

        [ContextMenu("Core: Test GlobalReputation clamp + event")]
        public void TestReputationClamp()
        {
            var sb = new StringBuilder();
            sb.AppendLine("[CoreSmokeTest] GlobalReputation clamp [0,100] + event:");

            int backupRep = GameState.GlobalReputation;
            int eventCount = 0;
            int lastNew = -999;
            System.Action<int, int> handler = (oldV, newV) =>
            {
                eventCount++;
                lastNew = newV;
            };
            GameState.OnGlobalReputationChanged += handler;

            // Test 2a: 9999 → clamp do 100
            GameState.GlobalReputation = 9999;
            bool t2a_ok = GameState.GlobalReputation == 100 && lastNew == 100;
            sb.AppendLine($"  2a) set 9999 → rep={GameState.GlobalReputation} event.newV={lastNew} " + (t2a_ok ? "PASS" : "FAIL"));

            // Test 2b: -50 → clamp do 0
            eventCount = 0;
            GameState.GlobalReputation = -50;
            bool t2b_ok = GameState.GlobalReputation == 0 && lastNew == 0;
            sb.AppendLine($"  2b) set -50 → rep={GameState.GlobalReputation} event.newV={lastNew} " + (t2b_ok ? "PASS" : "FAIL"));

            // Test 2c: 50 → bez clamp (valid range)
            eventCount = 0;
            GameState.GlobalReputation = 50;
            bool t2c_ok = GameState.GlobalReputation == 50 && eventCount == 1;
            sb.AppendLine($"  2c) set 50 → rep={GameState.GlobalReputation} count={eventCount} " + (t2c_ok ? "PASS" : "FAIL"));

            GameState.OnGlobalReputationChanged -= handler;
            GameState.GlobalReputation = backupRep; // restore
            sb.AppendLine($"  Result: {(t2a_ok && t2b_ok && t2c_ok ? "ALL PASS" : "SOMETHING FAILED")}");
            Log.Info(sb.ToString());
        }

        // ── Test 3: IsHomeDepotSet helper ───────────────────────────────

        [ContextMenu("Core: Test IsHomeDepotSet helper")]
        public void TestIsHomeDepotSet()
        {
            var sb = new StringBuilder();
            sb.AppendLine("[CoreSmokeTest] IsHomeDepotSet helper:");

            int backup = GameState.HomeDepotStationId;

            GameState.HomeDepotStationId = -1;
            bool t3a_ok = !GameState.IsHomeDepotSet;
            sb.AppendLine($"  3a) HomeDepotStationId=-1 → IsHomeDepotSet={GameState.IsHomeDepotSet} (expected false) " + (t3a_ok ? "PASS" : "FAIL"));

            GameState.HomeDepotStationId = 0;
            bool t3b_ok = GameState.IsHomeDepotSet; // 0 jest valid station ID
            sb.AppendLine($"  3b) HomeDepotStationId=0 → IsHomeDepotSet={GameState.IsHomeDepotSet} (expected true) " + (t3b_ok ? "PASS" : "FAIL"));

            GameState.HomeDepotStationId = 42;
            bool t3c_ok = GameState.IsHomeDepotSet;
            sb.AppendLine($"  3c) HomeDepotStationId=42 → IsHomeDepotSet={GameState.IsHomeDepotSet} (expected true) " + (t3c_ok ? "PASS" : "FAIL"));

            GameState.HomeDepotStationId = backup; // restore
            sb.AppendLine($"  Result: {(t3a_ok && t3b_ok && t3c_ok ? "ALL PASS" : "SOMETHING FAILED")}");
            Log.Info(sb.ToString());
        }

        // ── Test 4: VehicleLocationService per-type index ───────────────

        [ContextMenu("Core: Test VehicleLocationService per-type index")]
        public void TestVehicleLocationServiceIndex()
        {
            var sb = new StringBuilder();
            sb.AppendLine("[CoreSmokeTest] VehicleLocationService per-type index:");

            // Setup fresh service
            VehicleLocationService.EnsureExists();
            VehicleLocationService.ResetAll();
            var svc = VehicleLocationService.Instance;

            // Test 4a: pojazd zaczyna w InDepot (po SetInDepot)
            svc.SetInDepot(9001, depotTrackId: 5);
            var inDepot = svc.GetInDepot();
            bool t4a_ok = inDepot.Count == 1 && inDepot[0].vehicleId == 9001;
            sb.AppendLine($"  4a) SetInDepot(9001) → InDepot.Count={inDepot.Count} " + (t4a_ok ? "PASS" : "FAIL"));

            // Test 4b: tranzycja InDepot → AtStation, indexes consistent
            svc.SetAtStation(9001, stationId: 100, worldPos: Vector2.zero);
            inDepot = svc.GetInDepot();
            var atStation = svc.GetByType(VehicleLocationType.AtStation);
            bool t4b_ok = inDepot.Count == 0 && atStation.Count == 1 && atStation[0].vehicleId == 9001;
            sb.AppendLine($"  4b) SetAtStation → InDepot={inDepot.Count}, AtStation={atStation.Count} " + (t4b_ok ? "PASS" : "FAIL"));

            // Test 4c: dwa pojazdy, różne typy
            svc.SetInDepot(9002, depotTrackId: 3);
            svc.SetOnRoute(9003, trainRunId: 50, worldPos: Vector2.zero);
            int total = svc.GetInDepot().Count + svc.GetByType(VehicleLocationType.AtStation).Count + svc.GetOnRoute().Count;
            bool t4c_ok = total == 3 && svc.GetInDepot().Count == 1 && svc.GetOnRoute().Count == 1;
            sb.AppendLine($"  4c) 3 vehicles 3 types → total={total} (1+1+1) " + (t4c_ok ? "PASS" : "FAIL"));

            // Test 4d: zero alokacji per call (GetByType zwraca tę samą referencję)
            var firstCall = svc.GetByType(VehicleLocationType.InDepot);
            var secondCall = svc.GetByType(VehicleLocationType.InDepot);
            bool t4d_ok = object.ReferenceEquals(firstCall, secondCall);
            sb.AppendLine($"  4d) GetByType returns same ref (zero-alloc) " + (t4d_ok ? "PASS" : "FAIL"));

            // Test 4e: ResetAll czyści wszystkie indexes
            VehicleLocationService.ResetAll();
            bool t4e_ok = svc.GetInDepot().Count == 0 && svc.GetOnRoute().Count == 0;
            sb.AppendLine($"  4e) ResetAll → all indexes empty " + (t4e_ok ? "PASS" : "FAIL"));

            sb.AppendLine($"  Result: {(t4a_ok && t4b_ok && t4c_ok && t4d_ok && t4e_ok ? "ALL PASS" : "SOMETHING FAILED")}");
            Log.Info(sb.ToString());
        }

        // ── Test 5: RandomRegistry determinism ──────────────────────────

        [ContextMenu("Core: Test RandomRegistry determinism")]
        public void TestRandomRegistryDeterminism()
        {
            var sb = new StringBuilder();
            sb.AppendLine("[CoreSmokeTest] RandomRegistry determinism:");

            int backupSeed = GameState.Seed;
            GameState.Seed = 42;

            // Test 5a: dwa identyczne systemId po reset → identyczna sekwencja
            RandomRegistry.ResetAll();
            var rng1 = RandomRegistry.GetRng("SmokeTest.A");
            var seq1 = new int[5];
            for (int i = 0; i < 5; i++) seq1[i] = rng1.NextInt();

            RandomRegistry.ResetAll();
            var rng2 = RandomRegistry.GetRng("SmokeTest.A");
            var seq2 = new int[5];
            for (int i = 0; i < 5; i++) seq2[i] = rng2.NextInt();

            bool t5a_ok = true;
            for (int i = 0; i < 5; i++) if (seq1[i] != seq2[i]) { t5a_ok = false; break; }
            sb.AppendLine($"  5a) ResetAll → identyczna sekwencja (seed=42): [{string.Join(",", seq1)}] vs [{string.Join(",", seq2)}] " + (t5a_ok ? "PASS" : "FAIL"));

            // Test 5b: różne systemId → różne sekwencje (substream independence)
            RandomRegistry.ResetAll();
            var rngA = RandomRegistry.GetRng("SmokeTest.X");
            var rngB = RandomRegistry.GetRng("SmokeTest.Y");
            int seqA0 = rngA.NextInt();
            int seqB0 = rngB.NextInt();
            bool t5b_ok = seqA0 != seqB0;
            sb.AppendLine($"  5b) Different systemId → different first draw: X={seqA0}, Y={seqB0} " + (t5b_ok ? "PASS" : "FAIL"));

            // Test 5c: zmiana Seed → różna sekwencja
            RandomRegistry.ResetAll();
            var rngC = RandomRegistry.GetRng("SmokeTest.A");
            int draw_seed42 = rngC.NextInt();

            GameState.Seed = 1337;
            RandomRegistry.ResetAll();
            var rngD = RandomRegistry.GetRng("SmokeTest.A");
            int draw_seed1337 = rngD.NextInt();
            bool t5c_ok = draw_seed42 != draw_seed1337;
            sb.AppendLine($"  5c) Different Seed → different sequence: seed42={draw_seed42}, seed1337={draw_seed1337} " + (t5c_ok ? "PASS" : "FAIL"));

            GameState.Seed = backupSeed;
            RandomRegistry.ResetAll(); // restore clean state
            sb.AppendLine($"  Result: {(t5a_ok && t5b_ok && t5c_ok ? "ALL PASS" : "SOMETHING FAILED")}");
            Log.Info(sb.ToString());
        }

        // ── Test 6: DeterministicRng.Range(int,int) bias ────────────────

        [ContextMenu("Core: Test Range(int,int) uniform distribution")]
        public void TestRandomRegistryRangeBias()
        {
            var sb = new StringBuilder();
            sb.AppendLine("[CoreSmokeTest] DeterministicRng.Range(int,int) uniform distribution:");

            int backupSeed = GameState.Seed;
            GameState.Seed = 12345;
            RandomRegistry.ResetAll();
            var rng = RandomRegistry.GetRng("SmokeTest.RangeBias");

            // Test 6: 100k drawów z Range(0,10), każdy bucket powinien mieć ~10000 (±10%)
            const int samples = 100_000;
            const int buckets = 10;
            const float tolerance = 0.10f;
            var counts = new int[buckets];
            for (int i = 0; i < samples; i++)
            {
                int v = rng.Range(0, buckets);
                if (v < 0 || v >= buckets) { sb.AppendLine($"  FAIL: Range(0,10) zwrócił poza zakres: {v}"); Log.Info(sb.ToString()); return; }
                counts[v]++;
            }

            int expected = samples / buckets;
            int minAllowed = (int)(expected * (1f - tolerance));
            int maxAllowed = (int)(expected * (1f + tolerance));
            bool allOk = true;
            for (int i = 0; i < buckets; i++)
            {
                bool ok = counts[i] >= minAllowed && counts[i] <= maxAllowed;
                if (!ok) allOk = false;
                sb.AppendLine($"  bucket[{i}]={counts[i]} (expected ~{expected}, allowed {minAllowed}..{maxAllowed}) " + (ok ? "PASS" : "FAIL"));
            }

            GameState.Seed = backupSeed;
            RandomRegistry.ResetAll();
            sb.AppendLine($"  Result: {(allOk ? "ALL PASS (uniform within ±10%)" : "SOMETHING FAILED — modulo bias?")}");
            Log.Info(sb.ToString());
        }
    }
}
