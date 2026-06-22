using UnityEngine;
using UnityEngine.UI;
using MapSystem;
using RailwayManager.Core;
using RailwayManager.SharedUI;

namespace RailwayManager.Timetable
{
    // Audit 2026-05-15 (split god class): część bootstrap/scene-hook wyniesiona z root
    // TimetableInitializer.cs. Zawiera RegisterSceneHook + BootstrapDelayer + EnsureBootstrapped
    // (factory dla TimetableInitializer + popup canvas + UI singletony).
    public partial class TimetableInitializer
    {
        /// <summary>
        /// Auto-bootstrap przy starcie gry. Tworzy GO z TimetableInitializer
        /// który będzie żył w MapScene i nasłuchiwał <see cref="UIIntents.OnIntent"/>.
        /// Wołane automatycznie przez Unity [RuntimeInitializeOnLoadMethod].
        /// </summary>
        /// <summary>
        /// Hook wołany przez SceneManager.sceneLoaded — gdy MapScene ładuje się
        /// (additively albo standalone), automatycznie bootstrapuje Timetable.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterSceneHook()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene,
                                          UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            if (Instance != null) return;
            // BUG-028: idempotent — nie spawnuj duplikatu helper'a jeśli już istnieje
            // (production build: Menu→Depot→Map→Depot mnoży GameObject'y bez tego).
            if (Object.FindAnyObjectByType<BootstrapDelayer>() != null) return;
            var helper = new GameObject("TimetableBootstrapHelper").AddComponent<BootstrapDelayer>();
            helper.StartCoroutine(helper.DelayedBootstrap());
        }

        private class BootstrapDelayer : MonoBehaviour
        {
            public System.Collections.IEnumerator DelayedBootstrap()
            {
                yield return null;
                if (TimetableInitializer.Instance != null) { Destroy(gameObject); yield break; }
                var mapUI = Object.FindAnyObjectByType<MapUIManager>();
                if (mapUI != null)
                {
                    EnsureBootstrapped();
                    // Auto-initialize w tle po 2 sekundach żeby pierwszy klik "Rozkłady"
                    // nie musiał czekać na budowę grafu
                    Instance.StartCoroutine(Instance.DelayedAutoInit());
                    Log.Info("[TimetableInitializer] Auto-bootstrapped after MapScene load");
                }
                Destroy(gameObject);
            }
        }

        private System.Collections.IEnumerator DelayedAutoInit()
        {
            // M-PL: WaitForSeconds(2) może hang w Editor (focus loss). Frame counter zamiast.
            for (int i = 0; i < 120; i++) yield return null; // ~2s at 60 FPS
            if (!isReady)
            {
                Log.Info("[TimetableInitializer] Background initialization starting...");
                Initialize();
            }
        }

        /// <summary>
        /// Lazy bootstrap: tworzy TimetableInitializer + RouteBuildStateMachine +
        /// RouteBuildPanelUI w MapScene jeśli nie istnieją. Wołane przy pierwszym
        /// użyciu kreatora (z RouteBuildStateMachine.EnsureInitialized).
        /// Map asmdef nie referuje Timetable, więc MapUIManager nie może nas stworzyć —
        /// sami się doczepiamy do sceny.
        /// </summary>
        public static TimetableInitializer EnsureBootstrapped()
        {
            if (Instance != null) return Instance;

            var mapUI = MapUIManager.Instance;
            if (mapUI == null)
            {
                Log.Warn("[TimetableInitializer] MapUIManager not found — can't bootstrap");
                return null;
            }

            // Twórz na MapUIManager GO (żyje razem ze sceną)
            var init = mapUI.gameObject.AddComponent<TimetableInitializer>();

            // RouteBuild state machine (na tym samym GO)
            if (RouteBuildStateMachine.Instance == null)
                mapUI.gameObject.AddComponent<RouteBuildStateMachine>();

            // RouteBuild popup — osobny Canvas overlay (DontDestroyOnLoad), widoczny
            // niezależnie od aktywnej sceny (Depot lub Map). SceneController nie ukrywa
            // tego canvas bo nie jest częścią MapScene ani Depot canvas hierarchy.
            var popupCanvasObj = new GameObject("TimetablePopupCanvas");
            Object.DontDestroyOnLoad(popupCanvasObj);
            var canvas = popupCanvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200;
            var scaler = popupCanvasObj.AddComponent<CanvasScaler>();
            UITheme.ApplyCanvasScaler(scaler);
            popupCanvasObj.AddComponent<GraphicRaycaster>();

            // TimetableService musi być zainicjalizowany PRZED BuildUI (bo dropdown
            // CommercialCategories pobiera dane z niego)
            TimetableService.Initialize();

            // M9a TrainRunSimulator bootstrap — wcześniej w MapSetup.BootstrapTrainSimulator
            // przez reflection po stringu (Map asmdef NIE referuje Timetable, cykl). Tutaj
            // żyjemy w Timetable namespace, więc bezpośredni AddComponent. Idempotent przez
            // singleton check (Awake sprawdza Instance != null && != this → Destroy).
            //
            // KLUCZOWE: MoveGameObjectToScene do MapScene. Bez tego simGo trafia w aktywną
            // scenę w momencie spawnu — gdy EnsureBootstrapped wywołane przy ActiveScene=Depot
            // (np. lazy init z innej drogi), TrainSimulation + jego child Canvases (StationPopupUI,
            // TrainPopupUI, TrackPopupUI) trafiały w Depot scene → ukryte przez SceneController
            // (Map UI gated per scene). Pre-pkt-18 MapSetup.BootstrapTrainSimulator robił
            // SetParent na sibling MapRenderer, więc gwarantowanie w MapScene.
            if (RailwayManager.Timetable.Simulation.TrainRunSimulator.Instance == null)
            {
                var simGo = new GameObject("TrainSimulation");
                var mapScene = mapUI.gameObject.scene;
                if (mapScene.IsValid())
                    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(simGo, mapScene);
                simGo.AddComponent<RailwayManager.Timetable.Simulation.TrainRunSimulator>();
            }

            // Lista rozkładów (główny widok po kliknięciu taba)
            if (TimetableListUI.Instance == null)
            {
                var list = popupCanvasObj.AddComponent<TimetableListUI>();
                list.BuildUI(popupCanvasObj.transform);
            }

            // Kreator rozkładów (otwierany z listy przez [+ Nowy])
            if (TimetableCreatorUI.Instance == null)
            {
                var creator = popupCanvasObj.AddComponent<TimetableCreatorUI>();
                creator.BuildUI(popupCanvasObj.transform);
            }

            // Edytor kategorii handlowych (otwierany z listy przez [Kategorie...])
            if (CategoryEditorUI.Instance == null)
            {
                var catEd = popupCanvasObj.AddComponent<CategoryEditorUI>();
                catEd.BuildUI(popupCanvasObj.transform);
            }

            // Hybrydowy widok obiegów: lista + drag&drop pool + modale (M5 Etap 6 refactor)
            // Zastępuje osobny CirculationCreatorUI który był w Etap 6 — cała edycja
            // dzieje się teraz bezpośrednio w liście przez expand row + drop.
            if (CirculationListUI.Instance == null)
            {
                var circList = popupCanvasObj.AddComponent<CirculationListUI>();
                circList.BuildUI(popupCanvasObj.transform);
            }

            // Modal per-day przypisywania pojazdów (M5 Etap 9 refactor) — drag&drop pojazdów
            // na konkretne dni obiegu z walidacją konfliktów per-date.
            if (VehicleAssignmentModal.Instance == null)
            {
                var vam = popupCanvasObj.AddComponent<VehicleAssignmentModal>();
                vam.BuildUI(popupCanvasObj.transform);
            }

            return init;
        }
    }
}
