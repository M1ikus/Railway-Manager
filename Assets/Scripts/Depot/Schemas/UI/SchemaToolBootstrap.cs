using UnityEngine;
using DepotSystem;
using RailwayManager.Core;

namespace DepotSystem.Schemas.UI
{
    /// <summary>
    /// MD-10 — bootstrap component łączący <c>TrackBuildSubMode.Schemas</c> z <see cref="SchemaPanelUI"/>.
    ///
    /// Subscribuje się na <c>DepotUIManager.OnTrackSubModeChanged</c> + <c>OnToolChanged</c>:
    /// - Mode = BuildTrack + SubMode = Schemas → ensureSchemaPanel + Show
    /// - Inny ToolMode lub SubMode → Hide (jeśli istnieje)
    ///
    /// Auto-spawn'uje SchemaPanelUI przy pierwszym kliknięciu (lazy create).
    ///
    /// Wrzucić jako GameObject na scenę Depot — komponent znajdzie DepotUIManager.Instance
    /// w Start. Singleton, ale nie wymaga manual instancji w scenie (auto-create przez
    /// <c>DepotUIBootstrap</c> jeśli istnieje, lub manual w Inspector).
    /// </summary>
    public class SchemaToolBootstrap : MonoBehaviour
    {
        public static SchemaToolBootstrap Instance { get; private set; }

        private SchemaPanelUI _panel;
        private bool _eventsSubscribed;

        /// <summary>
        /// Auto-spawn po załadowaniu sceny — gracz nie musi ręcznie wrzucać GameObject'u
        /// w Inspector. Tworzy persistent GameObject z SchemaToolBootstrap.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn()
        {
            if (Instance != null) return;
            // Sprawdź czy istnieje w scenie (może developer dodał manualnie)
            var existing = FindAnyObjectByType<SchemaToolBootstrap>();
            if (existing != null) return;

            var go = new GameObject("SchemaToolBootstrap (auto-spawn)");
            go.AddComponent<SchemaToolBootstrap>();
            // NIE DontDestroyOnLoad — komponent działa w obrębie sceny Depot, OnDestroy unsubscribe
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        void Start()
        {
            // Event-driven init: jeśli DepotUIManager już istnieje → subscribe immediate;
            // w przeciwnym razie czekamy na OnReady event z jego Awake.
            DepotUIManager.OnReady += TrySubscribe;
            TrySubscribe();
        }

        private void TrySubscribe()
        {
            if (_eventsSubscribed) return;
            var uiManager = DepotUIManager.Instance;
            if (uiManager == null) return;

            uiManager.OnToolChanged += OnToolChanged;
            uiManager.OnTrackSubModeChanged += OnTrackSubModeChanged;
            _eventsSubscribed = true;
        }

        void OnDestroy()
        {
            DepotUIManager.OnReady -= TrySubscribe;
            UnsubscribeEvents();
            if (Instance == this) Instance = null;
        }

        private void UnsubscribeEvents()
        {
            if (!_eventsSubscribed) return;
            var uiManager = DepotUIManager.Instance;
            if (uiManager == null) return;

            uiManager.OnToolChanged -= OnToolChanged;
            uiManager.OnTrackSubModeChanged -= OnTrackSubModeChanged;
            _eventsSubscribed = false;
        }

        private void OnToolChanged(ToolMode mode)
        {
            // Inny tool niż BuildTrack → ukryj panel + anuluj aktywny placement
            if (mode != ToolMode.BuildTrack)
            {
                if (_panel != null && _panel.IsVisible) _panel.Hide();
                CancelActivePlacement();
            }
        }

        private void OnTrackSubModeChanged(TrackBuildSubMode mode)
        {
            if (mode == TrackBuildSubMode.Schemas)
            {
                ForceShow();
            }
            else
            {
                if (_panel != null && _panel.IsVisible) _panel.Hide();
                // Sub-mode zmieniony na coś innego niż Schemas — anuluj aktywny placement
                CancelActivePlacement();
            }
        }

        /// <summary>
        /// Anuluje aktywny placement schematu (jeśli istnieje). Wywoływane gdy gracz przełącza
        /// tool/sub-mode poza Schemas — preview schematu nie powinien zostać aktywny.
        /// </summary>
        private void CancelActivePlacement()
        {
            var placer = DepotSystem.Schemas.Placement.TurnoutSchemaPlacer.Instance;
            if (placer != null && placer.IsActive)
            {
                placer.CancelPlacement();
            }
        }

        /// <summary>
        /// Public API — wymusza pokazanie panelu, niezależnie od stanu OnTrackSubModeChanged
        /// event'u. Wywoływane przez TrackSubToolbarUI.OnButtonClicked dla klika na "SCH"
        /// gdy SubMode już jest Schemas (event nie firuje przy braku zmiany SubMode).
        /// </summary>
        public void ForceShow()
        {
            try
            {
                EnsurePanel();
                if (_panel != null)
                {
                    _panel.Show();
                    Log.Info("[SchemaToolBootstrap] ForceShow — panel pokazany");
                }
                else
                {
                    Log.Error("[SchemaToolBootstrap] ForceShow: EnsurePanel zwróciło null!");
                }
            }
            catch (System.Exception e)
            {
                Log.Error($"[SchemaToolBootstrap] ForceShow exception: {e.Message}\n{e.StackTrace}");
            }
        }

        private void EnsurePanel()
        {
            if (_panel != null) return;

            _panel = SchemaPanelUI.Instance;
            if (_panel != null) return;

            var existing = FindAnyObjectByType<SchemaPanelUI>();
            if (existing != null)
            {
                _panel = existing;
                return;
            }

            // Lazy auto-create
            var go = new GameObject("SchemaPanelUI (auto-created by SchemaToolBootstrap)");
            _panel = go.AddComponent<SchemaPanelUI>();
        }
    }
}
