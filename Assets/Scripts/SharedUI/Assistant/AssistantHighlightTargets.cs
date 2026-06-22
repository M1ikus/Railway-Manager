using System.Collections.Generic;
using UnityEngine;

namespace RailwayManager.SharedUI.Assistant
{
    /// <summary>
    /// M11 AS-3: rejestr celów highlight — moduły rejestrują RectTransformy swoich
    /// elementów UI pod stabilnym id (np. "depot.tab.Build", "depot.tool.BuildTrack"),
    /// a guidance (AssistantGuidanceStep.highlightTargetId) wskazuje je po id.
    /// SharedUI nie zna paneli modułów — to odwraca zależność (moduł rejestruje, asystent czyta).
    ///
    /// Wpisy ze zniszczonym RectTransform (scene unload) są usuwane lazy przy TryGet —
    /// moduły NIE muszą wyrejestrowywać przy destroy.
    /// </summary>
    public static class AssistantHighlightTargets
    {
        private static readonly Dictionary<string, RectTransform> _targets =
            new Dictionary<string, RectTransform>();

        public static int Count => _targets.Count;

        public static void Register(string id, RectTransform target)
        {
            if (string.IsNullOrEmpty(id) || target == null) return;
            _targets[id] = target; // re-rejestracja po rebuild UI nadpisuje stary wpis
        }

        public static void Unregister(string id)
        {
            if (!string.IsNullOrEmpty(id)) _targets.Remove(id);
        }

        /// <summary>False gdy brak wpisu LUB target zniszczony (wtedy wpis czyszczony lazy).</summary>
        public static bool TryGet(string id, out RectTransform target)
        {
            target = null;
            if (string.IsNullOrEmpty(id)) return false;
            if (!_targets.TryGetValue(id, out var rt)) return false;
            if (rt == null) // Unity-null po scene unload
            {
                _targets.Remove(id);
                return false;
            }
            target = rt;
            return true;
        }

        /// <summary>Testy / pełny reset.</summary>
        public static void Clear() => _targets.Clear();
    }
}
