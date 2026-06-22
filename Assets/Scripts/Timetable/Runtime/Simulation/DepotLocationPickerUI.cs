using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using MapSystem;
using RailwayManager;
using RailwayManager.Core;
using RailwayManager.Timetable;

namespace RailwayManager.Timetable.Simulation
{
    /// <summary>
    /// M9c DepotLocationPicker — nakładka wyboru home station.
    ///
    /// Flow:
    /// 1. Overlay pojawia się NA DEPOT (full-screen, centered box). GameState.HomeDepotStationId=-1.
    /// 2. Centrum: input "Wpisz nazwę stacji…" + przycisk "Wybierz z mapy".
    /// 3. Opcja A: user wpisuje → lista pasujących stacji → klik → select.
    ///    Opcja B: user klika "Wybierz z mapy" → overlay hide, switch do Map (bez UI),
    ///    user klika ikonę stacji → switch back do Depot, overlay show z wybraną stacją.
    /// 4. User widzi podgląd wyboru, klika "Potwierdź" → GameState set, overlay hide, gra rusza.
    ///
    /// Klasa rozbita na partial files:
    /// - <c>DepotLocationPickerUI.cs</c>            — pola, lifecycle (Awake/Update/OnEnable/OnDisable/Start),
    ///                                                  Show/Hide, EnsureExists, TryPickStationAtMouse,
    ///                                                  Debug ContextMenu (ten plik)
    /// - <c>DepotLocationPickerUI.MapPicking.cs</c> — flow "Wybierz z mapy": switch do Map, suppress UI,
    ///                                                  OnStationClicked, switch back do Depot
    /// - <c>DepotLocationPickerUI.Search.cs</c>     — text search + result rows + SelectStation +
    ///                                                  UpdateSelectedInfo + OnConfirmClicked
    /// - <c>DepotLocationPickerUI.BuildUI.cs</c>    — BuildUI (overlay layout) + BuildMapPickingBanner
    ///                                                  + AddText helper
    ///
    /// Patrz <c>docs/design/game-creator.md</c>.
    /// </summary>
    public partial class DepotLocationPickerUI : MonoBehaviour
    {
        public static DepotLocationPickerUI Instance { get; private set; }

        Canvas _canvas;
        GameObject _root;
        GameObject _centerBox;
        TMP_InputField _searchInput;
        GameObject _resultsPanel;
        RectTransform _resultsContent;
        TextMeshProUGUI _selectedInfoText;
        Button _confirmButton;
        TextMeshProUGUI _confirmButtonLabel;
        Button _pickFromMapButton;
        TextMeshProUGUI _pickFromMapLabel;

        GameObject _mapPickingBanner; // banner pokazywany na Map scene podczas pickingu

        RailwayStation _selected;
        readonly List<GameObject> _resultRows = new();

        /// <summary>True gdy user wybiera z mapy — overlay ukryty, czekamy na OnAnyStationClicked.</summary>
        bool _isMapPicking;

        /// <summary>Canvases które tymczasowo wyłączyliśmy w MapScene podczas pickingu — do re-enable.</summary>
        readonly List<Canvas> _suppressedMapCanvases = new();

        public static DepotLocationPickerUI EnsureExists()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("DepotLocationPickerUI");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<DepotLocationPickerUI>();
            return Instance;
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            BuildUI();
            BuildMapPickingBanner();
            _root.SetActive(false);
            _mapPickingBanner.SetActive(false);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            if (!_isMapPicking) return;

            var kb = UnityEngine.InputSystem.Keyboard.current;
            var mouse = UnityEngine.InputSystem.Mouse.current;

            // ESC anuluje map picking
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
            {
                CancelMapPicking();
                return;
            }

            // LMB: explicit raycast do StationMarker (StationMarker.OnMouseDown polega na Camera.main
            // i może nie działać gdy Map camera nie ma tagu MainCamera po scene switch)
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
                TryPickStationAtMouse();
        }

        void TryPickStationAtMouse()
        {
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse == null) return;

            // Znajdź aktywną kamerę Map scene
            Camera mapCam = null;
            var mapScene = SceneManager.GetSceneByName("MapScene");
            if (mapScene.IsValid() && mapScene.isLoaded)
            {
                foreach (var root in mapScene.GetRootGameObjects())
                {
                    var cams = root.GetComponentsInChildren<Camera>(includeInactive: false);
                    foreach (var c in cams)
                    {
                        if (c != null && c.enabled) { mapCam = c; break; }
                    }
                    if (mapCam != null) break;
                }
            }
            if (mapCam == null)
            {
                Log.Warn("[DepotLocationPicker] Nie znaleziono aktywnej kamery MapScene");
                return;
            }

            var mousePos = mouse.position.ReadValue();
            var ray = mapCam.ScreenPointToRay(mousePos);
            if (!Physics.Raycast(ray, out var hit, 100000f))
            {
                Log.Info("[DepotLocationPicker] Kliknięcie nie trafiło w żaden obiekt na mapie");
                return;
            }

            // Szukaj StationMarker w hierarchii trafionego obiektu
            var marker = hit.collider.GetComponentInParent<StationMarker>();
            if (marker == null)
            {
                Log.Info($"[DepotLocationPicker] Kliknięcie trafiło '{hit.collider.name}', ale to nie stacja");
                return;
            }

            OnStationClicked(marker);
        }

        void OnEnable()
        {
            StationMarker.OnAnyStationClicked += OnStationClicked;
            Log.Info("[DepotLocationPicker] OnEnable — subscribed to StationMarker.OnAnyStationClicked");
        }

        void OnDisable()
        {
            StationMarker.OnAnyStationClicked -= OnStationClicked;
            Log.Info("[DepotLocationPicker] OnDisable — unsubscribed from StationMarker.OnAnyStationClicked");
        }

        void Start()
        {
            if (GameState.HomeDepotStationId < 0)
                Show();
        }

        // ── Show/Hide ───────────────────────────────────────────────

        public void Show()
        {
            _root.SetActive(true);
            SceneController.FullscreenOverlayOpen = true;
            _isMapPicking = false;
            if (_mapPickingBanner != null) _mapPickingBanner.SetActive(false);
            ClearResultRows();
            if (_resultsPanel != null) _resultsPanel.SetActive(false);
            if (_searchInput != null)
            {
                _searchInput.SetTextWithoutNotify(_selected != null ? _selected.name : string.Empty);
                if (_selected == null)
                {
                    _searchInput.ActivateInputField();
                    _searchInput.Select();
                }
            }
            UpdateSelectedInfo();
            Log.Info("[DepotLocationPicker] Overlay shown (depot scene)");
        }

        public void Hide()
        {
            _root.SetActive(false);
            SceneController.FullscreenOverlayOpen = false;
            _isMapPicking = false;
            if (_mapPickingBanner != null) _mapPickingBanner.SetActive(false);
            ClearResultRows();
            if (_resultsPanel != null) _resultsPanel.SetActive(false);
        }

        // ── Debug ───────────────────────────────────────────────────

        [ContextMenu("Debug: Show picker overlay")]
        public void DebugShow() => Show();

        [ContextMenu("Debug: Reset HomeDepotStationId = -1 + show")]
        public void DebugReset()
        {
            GameState.HomeDepotStationId = -1;
            Show();
        }
    }
}
