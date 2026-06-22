using System.Text;
using UnityEngine;
using DepotSystem.Assistant;
using RailwayManager.Core;
using RailwayManager.Core.Assistant;
using RailwayManager.SharedUI.Assistant;
using RailwayManager.SharedUI.Suggestions;

namespace RailwayManager.Personnel.Assistant
{
    /// <summary>
    /// M11 AS-8 — diagnostic component pokrywający smoke scenariusze milestone'u Asystenta
    /// (wzór FurnitureMilestoneSmokeTest). Ląduje w Personnel asmdef, bo widzi wszystkie
    /// moduły rejestrujące capability (Core/Depot/Fleet/Timetable/Maintenance/SharedUI).
    ///
    /// Raporty są READ-ONLY (zero mutacji stanu gry) — wyjątek: #5 emituje TESTOWY sygnał
    /// sugestii (przejdzie przez normalny flow szeptu z cooldownem) i #6 czyści go snooze'em.
    ///
    /// Użycie (anty-Clippy playtest AS-8, fresh i preset):
    /// 1. Wejdź do gry (Depot/MapScene) — komponent AutoSpawnuje się sam.
    /// 2. Inspector na GameObject "AssistantMilestoneSmokeTest" → ContextMenu raporty #1-#6.
    /// 3. Obserwuj Console + zachowanie awatara/szeptów na realnym save.
    /// </summary>
    public class AssistantMilestoneSmokeTest : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoSpawn()
        {
            if (FindAnyObjectByType<AssistantMilestoneSmokeTest>() != null) return;
            var go = new GameObject("AssistantMilestoneSmokeTest");
            DontDestroyOnLoad(go);
            go.AddComponent<AssistantMilestoneSmokeTest>();
        }

        [ContextMenu("AS-8 #1: Rejestr — capabilities + reguły (stan TERAZ)")]
        public void Report_Registry()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[AS-8 #1] Capabilities: {AssistantCapabilityRegistry.Count}");
            foreach (var cap in AssistantCapabilityRegistry.All)
            {
                bool canExec;
                try { canExec = cap.CanExecute(); }
                catch (System.Exception e) { sb.AppendLine($"  !! {cap.Id}: CanExecute THREW: {e.Message}"); continue; }
                sb.AppendLine($"  {cap.Id,-24} cat={cap.Category,-9} brain={(cap.CanAutoExecute ? "TAK" : "nie")} " +
                              $"autoAllowed={(cap.AutoModeAllowed ? "TAK" : "nie")} " +
                              $"autoON={(AssistantState.IsAutoModeEnabled(cap.Id) ? "TAK" : "nie")} " +
                              $"canExecute={(canExec ? "TAK" : "nie")}");
            }

            sb.AppendLine($"[AS-8 #1] Reguły: {AssistantRuleRegistry.Count} (malejąco wg priorytetu; * = aktywna TERAZ)");
            foreach (var rule in AssistantRuleRegistry.All)
            {
                bool active;
                try { active = rule.isActive(); }
                catch (System.Exception e) { sb.AppendLine($"  !! {rule.id}: isActive THREW: {e.Message}"); continue; }
                bool filtered = !AssistantState.OfferFilterAllows(rule);
                sb.AppendLine($"  {(active ? "*" : " ")} {rule.priority,4} {rule.id,-26} kind={rule.kind,-10} " +
                              $"cap={rule.capabilityId}{(filtered ? "  [ODFILTROWANA: skip/memoria]" : "")}");
            }
            Log.Info(sb.ToString());
        }

        [ContextMenu("AS-8 #2: Stan drivera (persona/monitor)")]
        public void Report_DriverState()
        {
            var candidate = NextStepMonitor.CurrentCandidate;
            var sb = new StringBuilder();
            sb.AppendLine("[AS-8 #2] Stan drivera:");
            sb.AppendLine($"  Persona: '{AssistantState.DisplayName}', proaktywność={(AssistantState.ProactivityEnabled ? "ON" : "OFF")}, " +
                          $"introShown={AssistantState.IntroShown}, onboardingSkipped={AssistantState.OnboardingSkipped}");
            sb.AppendLine($"  Monitor: kandydat={(candidate != null ? $"{candidate.id} (prio {candidate.priority})" : "BRAK")}, " +
                          $"toolActive={NextStepMonitor.IsToolActive}, stuckFired={NextStepMonitor.StuckFiredForCurrent}");
            sb.Append($"  Historia działań: {AssistantState.History.Count} wpisów");
            for (int i = 0; i < AssistantState.History.Count && i < 5; i++)
            {
                sb.AppendLine();
                sb.Append($"    [{i}] {AssistantState.History[i].text}");
            }
            Log.Info(sb.ToString());
        }

        [ContextMenu("AS-8 #3: Introspekcja zajezdni (DepotReadiness)")]
        public void Report_DepotReadiness()
        {
            var r = DepotReadinessService.Current;
            Log.Info("[AS-8 #3] DepotReadiness: " +
                     $"evaluated={r.evaluated}, hasAnyTrack={r.hasAnyTrack}, hasElectrifiedTrack={r.hasElectrifiedTrack}, " +
                     $"hasHall={r.hasHall}, hasFuelStation={r.hasFuelStation}, " +
                     $"fleetNeedsCatenary={r.fleetNeedsCatenary}, fleetNeedsFuel={r.fleetNeedsFuel}, " +
                     $"EmuTrapActive={r.EmuTrapActive}" +
                     (r.evaluated ? "" : "  (NIEZNANY stan — reguły Depot milczą, wejdź do sceny Depot)"));
        }

        [ContextMenu("AS-8 #4: Plan dry-run (wszystkie mózgi, BEZ Apply)")]
        public void Report_PlanDryRun()
        {
            // Kontrakt AS-D3: Plan() nie mutuje stanu gry — dry-run jest bezpieczny.
            var sb = new StringBuilder();
            sb.AppendLine("[AS-8 #4] Plan() dry-run (capabilities z mózgiem):");
            foreach (var cap in AssistantCapabilityRegistry.All)
            {
                if (!cap.CanAutoExecute) continue;
                AssistantPlan plan;
                try { plan = cap.Plan(); }
                catch (System.Exception e) { sb.AppendLine($"  !! {cap.Id}: Plan THREW: {e.Message}"); continue; }

                if (plan == null)
                {
                    sb.AppendLine($"  {cap.Id,-24} → null (nic do zaproponowania w tym stanie)");
                    continue;
                }
                sb.AppendLine($"  {cap.Id,-24} → '{plan.title}', koszt={plan.costGroszy / 100.0:F2} zł, " +
                              $"linii preview: {plan.previewLines.Count}");
                for (int i = 0; i < plan.previewLines.Count && i < 4; i++)
                {
                    sb.AppendLine($"      · {plan.previewLines[i]}");
                }
            }
            Log.Info(sb.ToString());
        }

        [ContextMenu("AS-8 #5: Emituj TESTOWY sygnał sugestii (obserwuj szept)")]
        public void Emit_TestSuggestionSignal()
        {
            AssistantSignals.Emit(AssistantSignalKind.Suggestion, "AssistantMilestoneSmokeTest",
                contextKey: "smoke:test-suggestion",
                payload: "[SMOKE] Testowa sugestia advisora — jeśli to widzisz jako szept, bridge działa.");
            Log.Info("[AS-8 #5] Sygnał wyemitowany. Oczekiwane: szept przy awatarze " +
                     "(o ile proaktywność ON, tool nieaktywny, panel zamknięty, cooldown minął).");
        }

        [ContextMenu("AS-8 #6: Pamięć sugestii (typ Assistant) + sprzątnij sygnał testowy")]
        public void Report_SuggestionMemory()
        {
            int total = 0, assistant = 0;
            foreach (var rec in SuggestionMemoryService.GetAllRecords())
            {
                total++;
                if (rec.type == SuggestionType.Assistant) assistant++;
            }
            Log.Info($"[AS-8 #6] SuggestionMemory: {total} rekordów (typ Assistant: {assistant}). " +
                     "Sygnał testowy z #5 snooze'uję na 1h (nie zaśmieca realnego playtestu).");
            SuggestionMemoryService.RecordChoice(SuggestionType.Assistant, "smoke:test-suggestion",
                SuggestionChoice.Snooze);
        }
    }
}
