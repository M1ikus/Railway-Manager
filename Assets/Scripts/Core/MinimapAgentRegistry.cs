using System.Collections.Generic;
using UnityEngine;

namespace RailwayManager.Core
{
    /// <summary>
    /// Cross-asmdef bridge dla minimapy zajezdni (Q2 depot-visual-direction.md, krok 6).
    ///
    /// Personnel asmdef NIE może być referowany przez Depot asmdef (cykl) — ale Depot
    /// minimap musi pokazywać pozycje pracowników. Rozwiązanie: ten static registry
    /// w Core asmdef (widoczny z obu stron).
    ///
    /// Wzorzec:
    /// - Personnel `EmployeeWalkSimulator.SpawnEmployee` → `Register(id, type, transform)`.
    /// - Personnel despawn → `Unregister(id, type)`.
    /// - Depot `DepotMinimapUI.UpdateMarkers` → `GetPositions(type)`.
    ///
    /// Generic AgentType pozwala na rozbudowę (np. cargo wagons, vehicles itd.).
    /// </summary>
    public static class MinimapAgentRegistry
    {
        public enum AgentType
        {
            Employee = 0,
            Train = 1,     // tymczasowo unused — ConsistMarker w Depot, używamy FindObjectsByType
            // Post-EA: dodaj kolejne (Outdoor equipment status itp.)
        }

        // Per-type dictionary: agent ID → Transform. Transform null jeśli zdespawnowany.
        private static readonly Dictionary<AgentType, Dictionary<int, Transform>> _agents
            = new Dictionary<AgentType, Dictionary<int, Transform>>();

        public static void Register(int id, AgentType type, Transform transform)
        {
            if (transform == null) return;
            if (!_agents.TryGetValue(type, out var dict))
            {
                dict = new Dictionary<int, Transform>();
                _agents[type] = dict;
            }
            dict[id] = transform;
        }

        public static void Unregister(int id, AgentType type)
        {
            if (_agents.TryGetValue(type, out var dict))
                dict.Remove(id);
        }

        /// <summary>Returns positions of all registered agents of given type. Skip null transforms (zdespawnowane).</summary>
        public static IEnumerable<Vector3> GetPositions(AgentType type)
        {
            if (!_agents.TryGetValue(type, out var dict)) yield break;
            foreach (var kv in dict)
            {
                if (kv.Value != null) yield return kv.Value.position;
            }
        }

        /// <summary>Count registered (non-null) agents of given type.</summary>
        public static int GetCount(AgentType type)
        {
            if (!_agents.TryGetValue(type, out var dict)) return 0;
            int count = 0;
            foreach (var kv in dict)
                if (kv.Value != null) count++;
            return count;
        }

        /// <summary>Clear all agents of given type. Używane przy scene unload.</summary>
        public static void ClearType(AgentType type)
        {
            if (_agents.TryGetValue(type, out var dict)) dict.Clear();
        }

        /// <summary>Clear wszystko. Używane przy load save (full reset).</summary>
        public static void ClearAll()
        {
            _agents.Clear();
        }
    }
}
