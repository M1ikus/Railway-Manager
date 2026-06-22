using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using RailwayManager.Core;
using RailwayManager.SharedUI.Localization;

namespace RailwayManager.Timetable
{
    /// <summary>
    /// Fullscreen modal przypisywania pojazdów do obiegu — per dzień, z drag&drop.
    ///
    /// Layout:
    /// - Lewa kolumna: lista dni obowiązywania obiegu (ScrollRect). Każdy wiersz =
    ///   data + bar przypisanych pojazdów [Lok][Wag][Wag] + [Kopiuj do wszystkich]
    /// - Prawa kolumna: pula wszystkich pojazdów z FleetService.OwnedVehicles.
    ///   Każdy kafelek draggable, pokazuje availability (np. 'zajęty obieg #12').
    ///
    /// Drop vehicle tile → dodaje do listy dnia. Wiele pojazdów per dzień (skład).
    /// Walidacja: pojazd nie może być w dwóch obiegach w tym samym dniu — konflikt
    /// jest wizualizowany ale nie blokuje drop; zapis blokuje jeśli są niezasłonięte
    /// konflikty (user musi usunąć konfliktowe).
    ///
    /// Klasa rozbita na partial files:
    /// - <c>VehicleAssignmentModal.cs</c>          — pola, lifecycle, Open/Close (public API),
    ///                                                ESC handling, i18n hot-reload (ten plik)
    /// - <c>VehicleAssignmentModal.Drop.cs</c>     — drop handling: OnVehicleDroppedOnDay,
    ///                                                SortConsist (auto-sort lok→wag),
    ///                                                OnRemoveVehicleFromDay, OnCopyToAllDays
    /// - <c>VehicleAssignmentModal.Save.cs</c>     — OnSaveClicked + walidacja (CountConflicts,
    ///                                                CountConsistErrors) + UpdateStatus
    /// - <c>VehicleAssignmentModal.Refresh.cs</c>  — RefreshDays/RefreshPool + tile builders
    ///                                                (BuildDayRow, BuildVehicleBarTile,
    ///                                                BuildPoolTile) + ComputeAvailabilitySummary
    /// - <c>VehicleAssignmentModal.BuildUI.cs</c>  — public BuildUI + BuildScrollColumn +
    ///                                                MakeText/MakeBtn helpers
    /// </summary>
    public partial class VehicleAssignmentModal : MonoBehaviour
    {
        public static VehicleAssignmentModal Instance { get; private set; }

        private GameObject _panel;
        private Canvas _rootCanvas;
        private Transform _daysContent;   // lewa kolumna: dni
        private Transform _poolContent;   // prawa kolumna: pojazdy
        private TextMeshProUGUI _titleText;
        private TextMeshProUGUI _statusText;

        private Circulation _target;
        private List<System.DateTime> _activeDates = new();

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            LocalizationService.OnLanguageChanged += OnLocaleChanged;
        }

        void OnDestroy()
        {
            LocalizationService.OnLanguageChanged -= OnLocaleChanged;
            if (Instance == this) Instance = null;
        }

        // === i18n hot-reload (M13-4j-5) ===
        // Refresh dni + pula + status przy zmianie języka. Static labels (top bar
        // title default + 2 buttons + 2 column titles) są built raz w BuildUI —
        // pozostają w starym języku do następnego open (akceptowalny edge case pre-EA).
        private void OnLocaleChanged()
        {
            if (_panel == null || !_panel.activeSelf) return;
            if (_target != null && _titleText != null)
                _titleText.text = string.Format(LocalizationService.Get("timetable.vehicle_assign.title_format"), _target.name, _target.id);
            RefreshDays();
            RefreshPool();
            UpdateStatus();
        }

        void Update()
        {
            if (_panel == null || !_panel.activeSelf) return;
            if (UnityEngine.InputSystem.Keyboard.current != null
                && UnityEngine.InputSystem.Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                SceneController.LastEscConsumedFrame = Time.frameCount;
                Close();
            }
        }

        // ─────────────────────────────────────────────
        //  Show / Hide / Open / Close
        // ─────────────────────────────────────────────

        public void Open(Circulation circulation)
        {
            if (circulation == null) return;
            if (_panel == null)
            {
                Log.Warn("[VehicleAssignmentModal] Panel not built — bootstrap go zbuduje");
                return;
            }
            _target = circulation;
            _activeDates = circulation.GetActiveDates();
            if (_titleText != null)
                _titleText.text = string.Format(LocalizationService.Get("timetable.vehicle_assign.title_format"), circulation.name, circulation.id);
            _panel.SetActive(true);
            RefreshDays();
            RefreshPool();
            UpdateStatus();
        }

        public void Close()
        {
            if (_panel != null) _panel.SetActive(false);
            _target = null;
            _activeDates.Clear();
            if (_titleText != null) _titleText.text = LocalizationService.Get("timetable.vehicle_assign.title");
            if (_statusText != null) _statusText.text = string.Empty;
            if (CirculationListUI.Instance != null)
                CirculationListUI.Instance.Refresh();
        }
    }
}
