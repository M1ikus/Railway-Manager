using TMPro;
using UnityEngine;
using UnityEngine.UI;
using RailwayManager.Core;
using RailwayManager.SharedUI.Localization;

namespace RailwayManager.Timetable
{
    /// <summary>
    /// Kreator/edytor kategorii handlowych. Lista wszystkich kategorii + form do
    /// dodania/edycji. Wywoływany z TimetableListUI przyciskiem [Kategorie...].
    ///
    /// Layout: header → tabela kategorii → separator → form pól edycji → akcje.
    /// Form pokazuje aktualnie wybraną do edycji LUB nową szkicowaną kategorię.
    ///
    /// Klasa rozbita na partial files:
    /// - <c>CategoryEditorUI.cs</c>          — pola, lifecycle (Awake/OnDestroy/Update),
    ///                                          i18n hot-reload, Open/Show/Hide/Close (ten plik)
    /// - <c>CategoryEditorUI.Editor.cs</c>   — RefreshTable + form actions
    ///                                          (OnNewClicked/LoadCategoryToForm/ClearForm/
    ///                                          SaveForm/DeleteCategory/SetFormStatus) + parsery
    /// - <c>CategoryEditorUI.BuildUI.cs</c>  — public BuildUI + UI helpers
    ///                                          (Lbl/MakeRow/Sep/Btn/Inp/MakeToggle/MakePolicyDropdown)
    /// </summary>
    public partial class CategoryEditorUI : MonoBehaviour
    {
        public static CategoryEditorUI Instance { get; private set; }

        private GameObject _panel;
        private GraphicRaycaster _raycaster;
        private Transform _tableContent;

        // Form fields
        private TMP_InputField _idInput;          // tylko przy nowej, disabled przy edycji
        private TMP_InputField _shortCodeInput;
        private TMP_InputField _displayNameInput;
        private TMP_InputField _basePriceInput;
        private TMP_InputField _pricePerKmInput;
        private TMP_InputField _firstClassMultInput;
        private TMP_InputField _minStopSecInput;
        private TMP_InputField _trafficPriorityInput;
        private TMP_InputField _maxSpeedInput;
        private Toggle _emuToggle;
        private TMP_Dropdown _stopPolicyDropdown;
        private Toggle _airconToggle;
        private Toggle _wifiToggle;
        private Toggle _socketsToggle;
        private Toggle _cateringToggle;
        private Toggle _sleepingToggle;
        private TMP_InputField _notesInput;
        private TextMeshProUGUI _formHeader;
        private TextMeshProUGUI _formStatus;

        // Stan edycji: jeśli null → tworzymy nową, inaczej edycja istniejącej
        private CommercialCategory _editingCategory;
        private bool _isNewCategory;

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

        // === i18n hot-reload (M13-4i-2) ===
        // Refresh tabeli przy zmianie języka (table rows mają lokalizowane buttons).
        // Form fields są built raz w BuildPanel — pozostają w starym języku do następnego open.
        private void OnLocaleChanged()
        {
            if (_panel != null && _panel.activeSelf)
                RefreshTable();
        }

        void Update()
        {
            if (_panel == null || !_panel.activeSelf) return;
            if (!SceneController.TimetablePopupOpen) { Hide(); return; }
            if (UnityEngine.InputSystem.Keyboard.current != null
                && UnityEngine.InputSystem.Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                SceneController.LastEscConsumedFrame = Time.frameCount;
                Close();
            }
        }

        // ─────────────────────────────────────────────
        //  Show / Hide
        // ─────────────────────────────────────────────

        public void Open()
        {
            Show();
            _editingCategory = null;
            _isNewCategory = false;
            ClearForm();
            RefreshTable();
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
        }

        public void Close()
        {
            Hide();
            SceneController.TimetablePopupOpen = false;
            // Wróć do listy rozkładów jeśli istnieje
            if (TimetableListUI.Instance != null)
            {
                TimetableListUI.Instance.Show();
                TimetableListUI.Instance.RefreshTable();
            }
        }
    }
}
