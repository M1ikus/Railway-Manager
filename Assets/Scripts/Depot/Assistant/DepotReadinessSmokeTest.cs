using UnityEngine;
using RailwayManager.Core;

namespace DepotSystem.Assistant
{
    /// <summary>
    /// M11 AS-3: diagnostyka introspekcji zajezdni — systemy scene-based (TrackGraph/
    /// RoomDetection/OutdoorEquipment), więc weryfikacja w scenie Depot przez ContextMenu
    /// (konwencja: smoke dla warstwy sceny, EditMode dla czystej logiki).
    /// </summary>
    public class DepotReadinessSmokeTest : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn()
        {
            if (FindAnyObjectByType<DepotReadinessSmokeTest>() != null) return;
            var go = new GameObject("DepotReadinessSmokeTest");
            DontDestroyOnLoad(go);
            go.AddComponent<DepotReadinessSmokeTest>();
        }

        [ContextMenu("Assistant: raport readiness zajezdni")]
        private void Report()
        {
            DepotReadinessService.Invalidate();
            var r = DepotReadinessService.Current;
            Log.Info("[DepotReadinessSmokeTest] ── Introspekcja zajezdni ──\n"
                     + $"  evaluated:           {r.evaluated}\n"
                     + $"  hasAnyTrack:         {r.hasAnyTrack}\n"
                     + $"  hasElectrifiedTrack: {r.hasElectrifiedTrack}\n"
                     + $"  hasHall:             {r.hasHall}\n"
                     + $"  hasFuelStation:      {r.hasFuelStation}\n"
                     + $"  fleetNeedsCatenary:  {r.fleetNeedsCatenary}\n"
                     + $"  fleetNeedsFuel:      {r.fleetNeedsFuel}\n"
                     + $"  EmuTrapActive:       {r.EmuTrapActive}");
        }

        [ContextMenu("Assistant: raport reguł monitora")]
        private void ReportRules()
        {
            var sb = new System.Text.StringBuilder("[DepotReadinessSmokeTest] ── Reguły monitora ──\n");
            foreach (var rule in RailwayManager.Core.Assistant.AssistantRuleRegistry.All)
            {
                bool active;
                try { active = rule.isActive(); }
                catch (System.Exception e) { sb.AppendLine($"  {rule.id}: EXCEPTION {e.Message}"); continue; }
                sb.AppendLine($"  {rule.id} (prio {rule.priority}, {rule.kind}): active={active}");
            }
            var candidate = RailwayManager.Core.Assistant.NextStepMonitor.CurrentCandidate;
            sb.AppendLine($"  CurrentCandidate: {candidate?.id ?? "(brak)"}");
            Log.Info(sb.ToString());
        }
    }
}
