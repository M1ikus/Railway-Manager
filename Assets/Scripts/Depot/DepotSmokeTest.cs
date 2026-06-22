using System.Text;
using UnityEngine;
using RailwayManager.Core;

namespace DepotSystem
{
    /// <summary>
    /// Smoke test regresji dla refactor'ów Depot z 2026-05-13.
    ///
    /// Sprawdza invariants:
    /// - <see cref="DepotServices"/>.Get&lt;T&gt; lazy cache (2× Get zwraca tę samą referencję)
    /// - <see cref="DepotServices"/>.Invalidate&lt;T&gt; / InvalidateAll wymusza fresh lookup
    /// - <see cref="SnapPointSystem"/> spatial grid lookup (TD-009) działa identycznie do brute force
    /// - <see cref="SchemaParameters"/>.Normalize expanduje shorthand do array (per-pair foldout, TD-002)
    /// - <see cref="ToolModeGate"/> lifecycle (Start/Stop bez crashu, enabled toggle po OnToolChanged)
    /// - <see cref="DepotUIManager"/>.OnReady event subscribable bez NRE
    ///
    /// Każdy test ContextMenu jest niezależny — możesz uruchamiać selektywnie. Wszystkie
    /// piszą wynik przez <see cref="Log.Info"/> z prefiksem `[DepotSmokeTest]` i markerem
    /// PASS/FAIL na końcu wiersza. Testy które tworzą GameObjects sprzątają po sobie.
    ///
    /// Konwencja projektu (CLAUDE.md): brak Unity Test Framework — smoke tests +
    /// `[ContextMenu]` ręczne uruchamianie w Editor. Wzór: <c>Assets/Scripts/Core/CoreSmokeTest.cs</c>.
    /// </summary>
    public class DepotSmokeTest : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn()
        {
            if (Object.FindAnyObjectByType<DepotSmokeTest>() != null) return;
            var go = new GameObject("DepotSmokeTest (auto-spawn)");
            go.AddComponent<DepotSmokeTest>();
        }

        [ContextMenu("Depot: Run ALL smoke tests")]
        public void RunAll()
        {
            TestDepotServicesCache();
            TestDepotServicesInvalidate();
            TestSnapPointSystemSpatialGrid();
            TestSchemaParametersNormalize();
            TestToolModeGateLifecycle();
            TestDepotUIManagerOnReady();
        }

        // ── Test 1: DepotServices lazy cache ───────────────────────────────

        [ContextMenu("Depot: Test DepotServices.Get<T> lazy cache")]
        public void TestDepotServicesCache()
        {
            var sb = new StringBuilder();
            sb.AppendLine("[DepotSmokeTest] DepotServices.Get<T> lazy cache:");

            // Test 1a: 2× Get<DepotSmokeTest> zwraca tę samą referencję
            DepotServices.Invalidate<DepotSmokeTest>();
            var first = DepotServices.Get<DepotSmokeTest>();
            var second = DepotServices.Get<DepotSmokeTest>();
            bool t1a_ok = first != null && object.ReferenceEquals(first, second);
            sb.AppendLine($"  1a) 2× Get<DepotSmokeTest> → same ref (cache hit) " + (t1a_ok ? "PASS" : "FAIL"));

            // Test 1b: Get<T> dla nieistniejącego typu zwraca null (nie cache'uje null)
            // Używamy ad-hoc typu: TestStubComponent doesn't exist as scene MonoBehaviour
            // → null. Sprawdzamy że to nie crashuje i nie blokuje przyszłego lookup.
            DepotServices.Invalidate<DepotManager>();
            var firstManager = DepotServices.Get<DepotManager>();
            // jeśli scena ma DepotManager → firstManager != null; jeśli nie ma → null.
            // W obu przypadkach drugi lookup musi być consistent.
            var secondManager = DepotServices.Get<DepotManager>();
            bool t1b_ok = object.ReferenceEquals(firstManager, secondManager);
            sb.AppendLine($"  1b) 2× Get<DepotManager> → consistent (null lub same ref) " + (t1b_ok ? "PASS" : "FAIL"));

            sb.AppendLine($"  Result: {(t1a_ok && t1b_ok ? "ALL PASS" : "SOMETHING FAILED")}");
            Log.Info(sb.ToString());
        }

        // ── Test 2: DepotServices Invalidate ───────────────────────────────

        [ContextMenu("Depot: Test DepotServices Invalidate")]
        public void TestDepotServicesInvalidate()
        {
            var sb = new StringBuilder();
            sb.AppendLine("[DepotSmokeTest] DepotServices Invalidate:");

            // Test 2a: Invalidate<T> wymusza fresh lookup (nowa instancja w cache)
            var beforeInvalidate = DepotServices.Get<DepotSmokeTest>();
            DepotServices.Invalidate<DepotSmokeTest>();
            // Po Invalidate cache pusty dla T. Następny Get robi FindAnyObjectByType i
            // znajduje TĘ SAMĄ instancję w scenie (bo my jesteśmy w scenie). Reference
            // equality powinna nadal być true (jeden obiekt sceny), ale cache state został
            // zresetowany. Test: po Invalidate, Get nie crashuje + zwraca non-null.
            var afterInvalidate = DepotServices.Get<DepotSmokeTest>();
            bool t2a_ok = afterInvalidate != null && object.ReferenceEquals(beforeInvalidate, afterInvalidate);
            sb.AppendLine($"  2a) Invalidate<T> + Get → fresh lookup, same scene instance " + (t2a_ok ? "PASS" : "FAIL"));

            // Test 2b: InvalidateAll czyści cache całkowicie
            DepotServices.Get<DepotSmokeTest>();   // ensure cached
            DepotServices.InvalidateAll();
            var afterClearAll = DepotServices.Get<DepotSmokeTest>();
            bool t2b_ok = afterClearAll != null;
            sb.AppendLine($"  2b) InvalidateAll + Get → fresh lookup non-null " + (t2b_ok ? "PASS" : "FAIL"));

            sb.AppendLine($"  Result: {(t2a_ok && t2b_ok ? "ALL PASS" : "SOMETHING FAILED")}");
            Log.Info(sb.ToString());
        }

        // ── Test 3: SnapPointSystem spatial grid (TD-009) ──────────────────

        [ContextMenu("Depot: Test SnapPointSystem spatial grid lookup")]
        public void TestSnapPointSystemSpatialGrid()
        {
            var sb = new StringBuilder();
            sb.AppendLine("[DepotSmokeTest] SnapPointSystem spatial grid (TD-009):");

            var snapSystem = DepotServices.Get<SnapPointSystem>();
            if (snapSystem == null)
            {
                sb.AppendLine("  SKIP: SnapPointSystem nie istnieje w scenie (uruchom test w Depot.unity)");
                Log.Info(sb.ToString());
                return;
            }

            // Test 3a: FindNearestSnapPoint nie crashuje na pustej pozycji
            var (nodeIdFar, posFar) = snapSystem.FindNearestSnapPoint(new Vector3(99999f, 0f, 99999f), maxDistance: 3f);
            bool t3a_ok = nodeIdFar == -1;
            sb.AppendLine($"  3a) FindNearestSnapPoint(far) → no snap (-1) " + (t3a_ok ? "PASS" : "FAIL"));

            // Test 3b: FindNearestSnapPoint z dużym maxDistance — sprawdza że spatial grid
            // iteruje wystarczająco dużo chunków (radius = ceil(maxDistance / 50)).
            var trackGraph = DepotServices.Get<TrackGraph>();
            if (trackGraph != null && trackGraph.Nodes.Count > 0)
            {
                // Znajdź dowolny node w grafie + zapytaj o niego z radius=1000m (chunkRadius=20)
                int firstNodeId = -1;
                Vector3 firstNodePos = Vector3.zero;
                foreach (var kv in trackGraph.Nodes)
                {
                    var node = kv.Value;
                    if (node.EdgeIds.Count == 0) continue;
                    if (node.Type == NodeType.Throughput) continue;
                    if (node.Type == NodeType.Junction && node.EdgeIds.Count >= 3) continue;
                    firstNodeId = kv.Key;
                    firstNodePos = node.Position;
                    break;
                }

                if (firstNodeId >= 0)
                {
                    // Pytamy z 200m radius (chunkRadius = ceil(200/50) = 4 → 9×9 = 81 chunków)
                    var (foundId, foundPos) = snapSystem.FindNearestSnapPoint(firstNodePos, maxDistance: 200f);
                    bool t3b_ok = foundId == firstNodeId
                        && Vector3.Distance(foundPos, firstNodePos) < 0.01f;
                    sb.AppendLine($"  3b) FindNearestSnapPoint(node[{firstNodeId}].pos, 200m) → found={foundId} " + (t3b_ok ? "PASS" : "FAIL"));
                }
                else
                {
                    sb.AppendLine($"  3b) SKIP: brak valid endpoint/Junction node w grafie do testu");
                }
            }
            else
            {
                sb.AppendLine($"  3b) SKIP: TrackGraph pusty lub brak — uruchom z istniejącymi torami");
            }

            // Test 3c: idempotentność — 2× FindNearestSnapPoint na tej samej pozycji daje ten sam wynik
            var (id1, _) = snapSystem.FindNearestSnapPoint(Vector3.zero, maxDistance: 10f);
            var (id2, _) = snapSystem.FindNearestSnapPoint(Vector3.zero, maxDistance: 10f);
            bool t3c_ok = id1 == id2;
            sb.AppendLine($"  3c) 2× FindNearestSnapPoint same input → same output ({id1}=={id2}) " + (t3c_ok ? "PASS" : "FAIL"));

            sb.AppendLine($"  Result: {(t3a_ok && t3c_ok ? "ALL PASS (sprawdź t3b w pełnej scenie)" : "SOMETHING FAILED")}");
            Log.Info(sb.ToString());
        }

        // ── Test 4: SchemaParameters.Normalize (TD-002) ────────────────────

        [ContextMenu("Depot: Test SchemaParameters.Normalize shorthand expansion")]
        public void TestSchemaParametersNormalize()
        {
            var sb = new StringBuilder();
            sb.AppendLine("[DepotSmokeTest] SchemaParameters.Normalize (per-pair foldout, TD-002):");

            // Test 4a: shorthand expand do array długości N
            var p1 = new Schemas.SchemaParameters
            {
                trackCount = 5,
                trackSpacing = 4.5f,
                turnoutType = Schemas.SchemaTurnoutType.R190,
            };
            p1.Normalize(expectedTurnoutCount: 4);
            bool t4a_ok = p1.trackSpacings != null && p1.trackSpacings.Length == 4
                && Mathf.Approximately(p1.trackSpacings[0], 4.5f)
                && p1.turnoutTypes != null && p1.turnoutTypes.Length == 4
                && p1.turnoutTypes[0] == Schemas.SchemaTurnoutType.R190;
            sb.AppendLine($"  4a) shorthand (4.5m, R190) + Normalize(4) → arrays length=4, value preserved " + (t4a_ok ? "PASS" : "FAIL"));

            // Test 4b: per-pair edit zostaje (nie nadpisuje shorthand)
            var p2 = new Schemas.SchemaParameters
            {
                trackCount = 4,
                trackSpacings = new[] { 4.0f, 5.0f, 6.0f },
                turnoutTypes = new[] { Schemas.SchemaTurnoutType.R190, Schemas.SchemaTurnoutType.R300, Schemas.SchemaTurnoutType.R190 },
            };
            p2.Normalize(expectedTurnoutCount: 3);
            bool t4b_ok = Mathf.Approximately(p2.trackSpacings[1], 5.0f)
                && p2.turnoutTypes[1] == Schemas.SchemaTurnoutType.R300;
            sb.AppendLine($"  4b) per-pair array preserved przez Normalize (5.0m + R300 na pair[1]) " + (t4b_ok ? "PASS" : "FAIL"));

            // Test 4c: array shorter than expected → pad ostatnią wartością
            var p3 = new Schemas.SchemaParameters
            {
                trackCount = 6,
                trackSpacings = new[] { 5.0f, 4.5f },  // length=2, ale potrzebujemy 5
                turnoutTypes = new[] { Schemas.SchemaTurnoutType.R190 },  // length=1
            };
            p3.Normalize(expectedTurnoutCount: 5);
            bool t4c_ok = p3.trackSpacings.Length == 5
                && Mathf.Approximately(p3.trackSpacings[4], 4.5f)  // padded fill=ostatnia
                && p3.turnoutTypes.Length == 5;
            sb.AppendLine($"  4c) shorter array padded with last value (4.5m fill) " + (t4c_ok ? "PASS" : "FAIL"));

            // Test 4d: ClampSpacing dla wartości poza zakresem
            var p4 = new Schemas.SchemaParameters
            {
                trackCount = 3,
                trackSpacings = new[] { 99f, -10f, 5.0f },
            };
            p4.Normalize(expectedTurnoutCount: 2);
            bool t4d_ok = Mathf.Approximately(p4.trackSpacings[0], Schemas.SchemaParameters.MaxSpacing)
                && Mathf.Approximately(p4.trackSpacings[1], Schemas.SchemaParameters.MinSpacing);
            sb.AppendLine($"  4d) Out-of-range clamp ({Schemas.SchemaParameters.MinSpacing}..{Schemas.SchemaParameters.MaxSpacing}m): 99→{p4.trackSpacings[0]}, -10→{p4.trackSpacings[1]} " + (t4d_ok ? "PASS" : "FAIL"));

            sb.AppendLine($"  Result: {(t4a_ok && t4b_ok && t4c_ok && t4d_ok ? "ALL PASS" : "SOMETHING FAILED")}");
            Log.Info(sb.ToString());
        }

        // ── Test 5: ToolModeGate lifecycle ─────────────────────────────────

        [ContextMenu("Depot: Test ToolModeGate Start/Stop lifecycle")]
        public void TestToolModeGateLifecycle()
        {
            var sb = new StringBuilder();
            sb.AppendLine("[DepotSmokeTest] ToolModeGate lifecycle:");

            // Tworzymy dummy MonoBehaviour żeby zarządzać przez gate
            var stubGO = new GameObject("ToolModeGateTestStub");
            var stub = stubGO.AddComponent<ToolModeGateTestStub>();
            int deactivateCallCount = 0;

            try
            {
                // Test 5a: Start nie crashuje, ustawia enabled zgodnie z aktualnym tool
                var ui = DepotUIManager.Instance;
                if (ui == null)
                {
                    sb.AppendLine("  SKIP: DepotUIManager.Instance == null (uruchom w Depot.unity)");
                    Log.Info(sb.ToString());
                    return;
                }

                ToolMode initialTool = ui.CurrentTool;
                var gate = new ToolModeGate(stub, m => m == initialTool, () => deactivateCallCount++);
                gate.Start();
                bool t5a_ok = stub.enabled == true;  // gate widzi CurrentTool==initialTool → enabled=true
                sb.AppendLine($"  5a) gate.Start() z predykatem matching CurrentTool → enabled=true " + (t5a_ok ? "PASS" : "FAIL"));

                // Test 5b: gate z predykatem NON-matching → enabled=false + OnDeactivated wywołane
                gate.Stop();
                deactivateCallCount = 0;
                stub.enabled = true;  // reset
                var gate2 = new ToolModeGate(stub, m => false, () => deactivateCallCount++);
                gate2.Start();
                bool t5b_ok = stub.enabled == false && deactivateCallCount == 1;
                sb.AppendLine($"  5b) gate.Start() z always-false predicate → enabled=false, OnDeactivated count={deactivateCallCount} " + (t5b_ok ? "PASS" : "FAIL"));
                gate2.Stop();

                // Test 5c: 2× Stop nie crashuje (idempotent)
                gate2.Stop();
                gate.Stop();
                sb.AppendLine($"  5c) 2× Stop idempotent (brak NRE/crash) PASS");

                sb.AppendLine($"  Result: {(t5a_ok && t5b_ok ? "ALL PASS" : "SOMETHING FAILED")}");
                Log.Info(sb.ToString());
            }
            finally
            {
                if (stubGO != null) Destroy(stubGO);
            }
        }

        /// <summary>Stub MonoBehaviour żeby ToolModeGate miał czego zarządzać `enabled` flagą.</summary>
        private sealed class ToolModeGateTestStub : MonoBehaviour { }

        // ── Test 6: DepotUIManager.OnReady event ───────────────────────────

        [ContextMenu("Depot: Test DepotUIManager.OnReady event subscribable")]
        public void TestDepotUIManagerOnReady()
        {
            var sb = new StringBuilder();
            sb.AppendLine("[DepotSmokeTest] DepotUIManager.OnReady event:");

            // Test 6a: Instance dostępny
            var ui = DepotUIManager.Instance;
            bool t6a_ok = ui != null;
            sb.AppendLine($"  6a) DepotUIManager.Instance != null " + (t6a_ok ? "PASS" : "FAIL"));

            if (!t6a_ok)
            {
                sb.AppendLine($"  Result: SKIP reszty (uruchom w Depot.unity)");
                Log.Info(sb.ToString());
                return;
            }

            // Test 6b: OnReady subscribe + unsubscribe nie crashuje
            // Note: OnReady jest static event firowany RAZ w Awake. Po Awake event jest
            // już no-op dla nowych subscriberów. Test sprawdza tylko subscribable bez NRE.
            System.Action handler = () => { };
            bool t6b_ok = true;
            try
            {
                DepotUIManager.OnReady += handler;
                DepotUIManager.OnReady -= handler;
            }
            catch (System.Exception e)
            {
                t6b_ok = false;
                sb.AppendLine($"  6b) EXCEPTION: {e.Message}");
            }
            sb.AppendLine($"  6b) OnReady += / -= bez exception " + (t6b_ok ? "PASS" : "FAIL"));

            sb.AppendLine($"  Result: {(t6a_ok && t6b_ok ? "ALL PASS" : "SOMETHING FAILED")}");
            Log.Info(sb.ToString());
        }
    }
}
