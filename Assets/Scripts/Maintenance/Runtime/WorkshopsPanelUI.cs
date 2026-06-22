using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using RailwayManager;
using RailwayManager.Core;
using RailwayManager.SharedUI.Localization;

namespace RailwayManager.Maintenance
{
    /// <summary>
    /// M7-4: Panel "Warsztaty" — przegląd slotów + lista pojazdów do przeglądu + przydzielanie.
    ///
    /// Otwiera się przez <see cref="UIIntent.OpenWorkshopsPanel"/> (zakładka Warsztaty
    /// w MainTabBarUI), zamyka przez <see cref="UIIntent.CloseWorkshopsPanel"/> lub ESC.
    /// Pełnoekranowy overlay w Depot scene.
    ///
    /// Żyje w Maintenance assembly (DontDestroyOnLoad, split 2026-05-15) — skanuje
    /// Depot.RoomDetectionSystem + używa EconomyManager/ReputationManager z Timetable.Economy.
    ///
    /// Layout (2 kolumny):
    /// - Lewa: Sloty warsztatowe (hala A/B/C..., każdy slot: wolny/zajęty + ETA)
    /// - Prawa: Pojazdy z urgency >= 0.8 — klik "Przydziel" → auto-pick slot
    ///
    /// Klasa rozbita na partial files:
    /// - <c>WorkshopsPanelUI.cs</c>          — pola, EnsureExists, lifecycle
    ///                                          (Awake/Update), Show/Hide,
    ///                                          RefreshContent dispatcher (ten plik)
    /// - <c>WorkshopsPanelUI.Refresh.cs</c>  — RefreshSlots (lewa kolumna), RefreshOverdue
    ///                                          + CreateOverdueRow (prawa kolumna)
    /// - <c>WorkshopsPanelUI.External.cs</c> — M7-5 ZNTK picker (Show/Create/Hide)
    /// - <c>WorkshopsPanelUI.BuildUI.cs</c>  — BuildUI + BuildExternalPicker + UI helpers
    ///                                          (CreateScrollContent/CreateColumn/AddText/CreateButton)
    /// </summary>
    public partial class WorkshopsPanelUI : MonoBehaviour
    {
        public static WorkshopsPanelUI Instance { get; private set; }

        Canvas _canvas;
        GameObject _root;
        TextMeshProUGUI _slotsText;
        TextMeshProUGUI _overdueText;
        ScrollRect _slotsScroll;
        ScrollRect _overdueScroll;
        RectTransform _overdueContent;
        bool _isVisible;
        float _refreshTimer;
        const float RefreshInterval = 0.5f;

        // Click-bindings (regenerated on Refresh)
        readonly List<Button> _assignButtons = new();

        // M7-5: External ZNTK picker sub-panel
        GameObject _externalPickerPanel;
        TextMeshProUGUI _externalPickerTitle;
        RectTransform _externalPickerContent;
        readonly List<GameObject> _externalPickerRows = new();

        public static WorkshopsPanelUI EnsureExists()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("WorkshopsPanelUI");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<WorkshopsPanelUI>();
            return Instance;
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            BuildUI();
            _root.SetActive(false);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void OnEnable()
        {
            UIIntents.OnIntent += HandleUIIntent;
        }

        void OnDisable()
        {
            UIIntents.OnIntent -= HandleUIIntent;
        }

        void HandleUIIntent(UIIntent intent)
        {
            if (intent == UIIntent.OpenWorkshopsPanel && !_isVisible) Show();
            else if (intent == UIIntent.CloseWorkshopsPanel && _isVisible) Hide();
        }

        void Update()
        {
            if (_isVisible)
            {
                // BUG-048: unscaledDeltaTime — UI refresh ma działać przy pauzie.
                _refreshTimer += Time.unscaledDeltaTime;
                if (_refreshTimer >= RefreshInterval)
                {
                    _refreshTimer = 0f;
                    RefreshContent();
                }
            }

            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (_isVisible && keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                SceneController.LastEscConsumedFrame = Time.frameCount;
                Hide();
            }
        }

        public void Show()
        {
            _root.SetActive(true);
            _isVisible = true;
            SceneController.FullscreenOverlayOpen = true;
            RefreshContent();
        }

        public void Hide()
        {
            _root.SetActive(false);
            _isVisible = false;
            SceneController.FullscreenOverlayOpen = false;
        }

        void RefreshContent()
        {
            var wm = WorkshopManager.Instance;
            if (wm == null)
            {
                _slotsText.text = LocalizationService.Get("maintenance.workshops.service_inactive");
                _overdueText.text = "";
                return;
            }

            // Force rescan to reflect current Depot state
            wm.RescanDepotRooms();

            RefreshSlots(wm);
            RefreshOverdue(wm);
        }
    }
}
