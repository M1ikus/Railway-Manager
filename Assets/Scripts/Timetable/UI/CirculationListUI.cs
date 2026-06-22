using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using RailwayManager.Core;
using RailwayManager.SharedUI.Localization;

namespace RailwayManager.Timetable
{
    public partial class CirculationListUI : MonoBehaviour
    {
        public static CirculationListUI Instance { get; private set; }

        private GameObject _panel;
        private GraphicRaycaster _raycaster;
        private Canvas _rootCanvas;

        private Transform _circulationsContent;
        private Transform _schedulesPoolContent;
        private Transform _warningsContent;

        private readonly HashSet<int> _expandedIds = new();

        private GameObject _assignModal;
        private TMP_Dropdown _assignVehicleDropdown;
        private TextMeshProUGUI _assignStatusText;
        private Circulation _assignTarget;

        private GameObject _autoGenModal;
        private Transform _autoGenListContent;
        private TextMeshProUGUI _autoGenSummary;
        private CirculationAutoGenerator.GenerationResult _pendingAutoGen;

        private GameObject _autoGenSettingsModal;
        private TMP_InputField _autoGenMinGapInput;
        private Toggle _autoGenRespectCompToggle;
        private Toggle _autoGenRespectClassToggle;
        private Toggle _autoGenAutoAssignToggle;
        private Transform _autoGenTimetablesContent;
        private readonly Dictionary<int, Toggle> _autoGenTimetableToggles = new();

        private GameObject _deadheadModal;
        private TextMeshProUGUI _deadheadModalText;
        private int _deadheadCircId = -1;
        private int _deadheadInsertIdx = -1;
        private string _deadheadFrom;
        private string _deadheadTo;
        private int _deadheadOriginalNext = -1;

        private GameObject _returnTripModal;
        private TextMeshProUGUI _returnTripModalText;
        private int _returnTripCircId = -1;
        private int _returnTripBaseTimetableId = -1;

        private string _flashMessage;
        private float _flashExpireTime;

        private GameObject _optionsModal;
        private int _optionsCircId = -1;
        private TMP_InputField _optNameInput;
        private Toggle _optIsOneTimeToggle;
        private readonly Toggle[] _optDayToggles = new Toggle[7];
        private TMP_InputField _optWeeksInput;
        private TMP_InputField _optOneTimeDateInput;
        private TMP_InputField _optNotesInput;
        private TextMeshProUGUI _optRecurringLabel;
        private TextMeshProUGUI _optOneTimeLabel;

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

        private void OnLocaleChanged()
        {
            if (_panel == null || !_panel.activeSelf) return;
            Refresh();
        }

        void Update()
        {
            if (_panel == null || !_panel.activeSelf) return;

            if (_flashMessage != null && Time.unscaledTime > _flashExpireTime)
            {
                _flashMessage = null;
                RefreshWarnings();
            }

            if (!SceneController.TimetablePopupOpen) { Hide(); return; }
            if (UnityEngine.InputSystem.Keyboard.current != null
                && UnityEngine.InputSystem.Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                SceneController.LastEscConsumedFrame = Time.frameCount;
                if (_assignModal != null && _assignModal.activeSelf) CloseAssignModal();
                else if (_autoGenSettingsModal != null && _autoGenSettingsModal.activeSelf) CloseAutoGenSettingsModal();
                else if (_autoGenModal != null && _autoGenModal.activeSelf) CloseAutoGenModal();
                else if (_deadheadModal != null && _deadheadModal.activeSelf) CloseDeadheadModal();
                else if (_returnTripModal != null && _returnTripModal.activeSelf) CloseReturnTripModal();
                else if (_optionsModal != null && _optionsModal.activeSelf) CloseOptionsModal();
                else Close();
            }
        }

        public void Open()
        {
            HideAuxiliaryPanels();
            Show();
            Refresh();
        }

        public void Show()
        {
            if (_panel != null) _panel.SetActive(true);
            if (_raycaster != null) _raycaster.enabled = true;
            SceneController.TimetablePopupOpen = true;
        }

        public void Hide()
        {
            if (_panel != null) _panel.SetActive(false);
            if (_raycaster != null) _raycaster.enabled = false;
            HideAuxiliaryPanels();
        }

        public void Close()
        {
            Hide();
            SceneController.TimetablePopupOpen = false;
        }

        public void Refresh()
        {
            RefreshCirculations();
            RefreshSchedulesPool();
            RefreshWarnings();
        }

        public void RefreshTable()
        {
            RefreshCirculations();
        }

        private void HideAuxiliaryPanels()
        {
            if (_assignModal != null) _assignModal.SetActive(false);
            if (_autoGenModal != null) _autoGenModal.SetActive(false);
            if (_autoGenSettingsModal != null) _autoGenSettingsModal.SetActive(false);
            if (_deadheadModal != null) _deadheadModal.SetActive(false);
            if (_returnTripModal != null) _returnTripModal.SetActive(false);
            if (_optionsModal != null) _optionsModal.SetActive(false);
        }
    }
}
