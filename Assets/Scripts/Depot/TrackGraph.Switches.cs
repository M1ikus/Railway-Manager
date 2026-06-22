using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using RailwayManager.Core;

namespace DepotSystem
{
    public partial class TrackGraph
    {
        // ═══════════════════════════════════════════
        //  ROZJAZDY — IGLICE (switch blades)
        // ═══════════════════════════════════════════

        /// <summary>
        /// Ustawia iglicę na node Junction.
        /// straightEdgeId/divergingEdgeId — krawędzie, między którymi iglica przełącza.
        /// </summary>
        public void SetSwitchBlade(int nodeId, int straightEdgeId, int divergingEdgeId, int turnoutEntityId = -1)
        {
            if (!nodes.ContainsKey(nodeId)) return;
            var node = nodes[nodeId];

            if (node.Type != NodeType.Junction)
            {
                Log.Warn($"[TrackGraph] SetSwitchBlade: node {nodeId} is {node.Type}, not Junction");
                return;
            }

            node.Blade = new SwitchBladeData
            {
                TurnoutEntityId = turnoutEntityId,
                StraightEdgeId = straightEdgeId,
                DivergingEdgeId = divergingEdgeId,
                IsDiverging = false
            };

            node.SwitchActiveEdgeId = straightEdgeId;
        }

        /// <summary>Przełącza iglicę na node (prosta ↔ odnoga)</summary>
        public void ToggleSwitchBlade(int nodeId)
        {
            if (!nodes.ContainsKey(nodeId)) return;
            var blade = nodes[nodeId].Blade;
            if (blade == null) return;

            blade.IsDiverging = !blade.IsDiverging;
            nodes[nodeId].SwitchActiveEdgeId = blade.ActiveEdgeId;
        }

        /// <summary>Ustawia iglicę na konkretną pozycję</summary>
        public void SetSwitchBladePosition(int nodeId, bool diverging)
        {
            if (!nodes.ContainsKey(nodeId)) return;
            var blade = nodes[nodeId].Blade;
            if (blade == null) return;

            blade.IsDiverging = diverging;
            nodes[nodeId].SwitchActiveEdgeId = blade.ActiveEdgeId;
        }

        /// <summary>Zwraca wszystkie node'y z iglicami</summary>
        public List<TrackNode> GetSwitchBladeNodes()
        {
            return nodes.Values.Where(n => n.Blade != null).ToList();
        }

        /// <summary>Sprawdza, czy przejazd z nodeA przez nodeB na nodeC jest dozwolony przez iglicę</summary>
        public bool IsRouteAllowedByBlade(int nodeId, int incomingEdgeId)
        {
            if (!nodes.ContainsKey(nodeId)) return true;
            var blade = nodes[nodeId].Blade;
            if (blade == null) return true; // Brak iglicy = wolny przejazd

            // Krawędź wchodząca nie jest ani prosta ani odnoga — to trzecia noga (np. body), zawsze OK
            if (incomingEdgeId != blade.StraightEdgeId && incomingEdgeId != blade.DivergingEdgeId)
                return true;

            // Krawędź wchodząca musi odpowiadać aktywnej pozycji iglicy
            return incomingEdgeId == blade.ActiveEdgeId;
        }
    }
}
