using UnityEditor;
using UnityEngine;
using RailwayManager.Timetable;

namespace RailwayManager.EditorTools
{
    /// <summary>
    /// Menu wrapper żeby uruchomić TimetableInitializer ContextMenu actions
    /// bez konieczności znalezienia GameObject w Hierarchy (jest lazy-bootstrapped
    /// w MapScene dopiero po otwarciu kreatora).
    ///
    /// Wymaga aktywnego Play mode + zainicjowanego TimetableInitializer.
    /// </summary>
    public static class TimetableDiagnoseMenu
    {
        [MenuItem("Railway Manager/Diagnose/Królewo Malborskie platforms")]
        public static void DiagnoseKrolewo()
        {
            if (!Application.isPlaying)
            {
                EditorUtility.DisplayDialog("Diagnose",
                    "Najpierw uruchom grę (Play mode) i otwórz kreator rozkładu.",
                    "OK");
                return;
            }
            var init = TimetableInitializer.Instance;
            if (init == null)
            {
                EditorUtility.DisplayDialog("Diagnose",
                    "TimetableInitializer nie istnieje. Otwórz kreator rozkładu w MapScene.",
                    "OK");
                return;
            }
            if (!init.IsReady)
            {
                EditorUtility.DisplayDialog("Diagnose",
                    "TimetableInitializer nie zakończył inicjalizacji. Poczekaj na log [TimetableInitializer] Initialize complete.",
                    "OK");
                return;
            }
            init.DiagnoseKrolewoMalborskie();
        }

        [MenuItem("Railway Manager/Diagnose/Regenerate StationTrackData")]
        public static void RegenerateStationTrackData()
        {
            if (!Application.isPlaying)
            {
                EditorUtility.DisplayDialog("Regenerate",
                    "Najpierw uruchom grę (Play mode) i otwórz kreator rozkładu.",
                    "OK");
                return;
            }
            var init = TimetableInitializer.Instance;
            if (init == null || !init.IsReady)
            {
                EditorUtility.DisplayDialog("Regenerate",
                    "TimetableInitializer nie gotowy.", "OK");
                return;
            }
            init.RegenerateStationTrackData();
        }
    }
}
