using UnityEngine;
using TMPro;
using UnityEngine.UI;
using RailwayManager.Core;
using RailwayManager.SharedUI;
using RailwayManager.SharedUI.Localization;

namespace RailwayManager.Personnel
{
    /// <summary>
    /// BUG-010 (cz.1+2): popup "Uprawnienia pracownika" — placeholder UI dla post-EA mechanic.
    ///
    /// 🚧 EA mode: pokazuje **tylko disclaimer** + 2 listy uprawnień (puste w EA = "wszystkie").
    /// Gameplay impact = ZERO — pracownicy nie wymagają kwalifikacji w runtime.
    ///
    /// 🛣️ Post-EA: lista uprawnień edytowalna przez training events; runtime sprawdza
    /// w <see cref="Runtime.CrewAssignmentService"/> i <see cref="Runtime.CrewCirculationValidator"/>.
    ///
    /// Decyzja design'owa user'a 2026-05-07: "okno w UI ale na razie pracownicy nie potrzebują
    /// żadnych kwalifikacji". UI jest gotowy dla przyszłej rozbudowy, runtime omija check.
    /// </summary>
    public class EmployeeQualificationsUI : MonoBehaviour
    {
        public static EmployeeQualificationsUI Instance { get; private set; }

        Canvas _canvas;
        GameObject _root;
        TextMeshProUGUI _headerText;
        TextMeshProUGUI _disclaimerText;
        TextMeshProUGUI _tractionListText;
        TextMeshProUGUI _categoryListText;
        int _currentEmployeeId = -1;
        bool _isVisible;

        public static EmployeeQualificationsUI EnsureExists()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("EmployeeQualificationsUI");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<EmployeeQualificationsUI>();
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

        void Update()
        {
            if (!_isVisible) return;
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) Hide();
        }

        public void Show(int employeeId)
        {
            _currentEmployeeId = employeeId;
            _root.SetActive(true);
            _isVisible = true;
            Refresh();
        }

        public void Hide()
        {
            _root.SetActive(false);
            _isVisible = false;
            _currentEmployeeId = -1;
        }

        void BuildUI()
        {
            var canvasGo = new GameObject("QualificationsCanvas");
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 115; // above EmployeeDetailsUI (110)
            UITheme.ApplyCanvasScaler(canvasGo.AddComponent<CanvasScaler>());
            canvasGo.AddComponent<GraphicRaycaster>();

            _root = new GameObject("Root");
            _root.transform.SetParent(canvasGo.transform, false);
            var rootRect = _root.AddComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero; rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero; rootRect.offsetMax = Vector2.zero;
            _root.AddComponent<Image>().color = UITheme.WithAlpha(Color.black, 0.74f);

            // Card panel (centered)
            var panel = new GameObject("Panel");
            panel.transform.SetParent(_root.transform, false);
            var panelRect = panel.AddComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f); panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(640, 440);
            panelRect.anchoredPosition = Vector2.zero;
            var panelImage = panel.AddComponent<Image>();
            UITheme.ApplySurface(panelImage, UITheme.WithAlpha(UITheme.OverlayPanelStrong, 0.98f), UIShapePreset.PanelLarge);

            // Header
            _headerText = UiHelper.CreateText(panel.transform, "Header", "—", 18, TextAlignmentOptions.Top);
            var hr = _headerText.GetComponent<RectTransform>();
            hr.anchorMin = new Vector2(0, 1); hr.anchorMax = new Vector2(1, 1);
            hr.pivot = new Vector2(0.5f, 1f);
            hr.anchoredPosition = new Vector2(0, -22);
            hr.sizeDelta = new Vector2(-40, 36);

            // Disclaimer banner (Warning color, EA placeholder context)
            var disclaimerCard = UiHelper.CreatePanel(panel.transform, "DisclaimerCard",
                UITheme.WithAlpha(UITheme.Warning, 0.18f), UIShapePreset.Panel);
            var dcr = disclaimerCard.GetComponent<RectTransform>();
            dcr.anchorMin = new Vector2(0, 1); dcr.anchorMax = new Vector2(1, 1);
            dcr.pivot = new Vector2(0.5f, 1f);
            dcr.anchoredPosition = new Vector2(0, -64);
            dcr.sizeDelta = new Vector2(-40, 64);

            _disclaimerText = UiHelper.CreateText(panel.transform, "Disclaimer", "", 13, TextAlignmentOptions.Center);
            _disclaimerText.color = UITheme.Warning;
            var disr = _disclaimerText.GetComponent<RectTransform>();
            disr.anchorMin = new Vector2(0, 1); disr.anchorMax = new Vector2(1, 1);
            disr.pivot = new Vector2(0.5f, 1f);
            disr.anchoredPosition = new Vector2(0, -72);
            disr.sizeDelta = new Vector2(-60, 50);

            // Traction permits section
            var tractionLabel = UiHelper.CreateText(panel.transform, "TractionLabel", "", 14, TextAlignmentOptions.TopLeft);
            tractionLabel.fontStyle = FontStyles.Bold;
            var tlr = tractionLabel.GetComponent<RectTransform>();
            tlr.anchorMin = new Vector2(0, 1); tlr.anchorMax = new Vector2(0.5f, 1);
            tlr.pivot = new Vector2(0, 1);
            tlr.anchoredPosition = new Vector2(28, -148);
            tlr.sizeDelta = new Vector2(-20, 22);

            _tractionListText = UiHelper.CreateText(panel.transform, "TractionList", "", 12, TextAlignmentOptions.TopLeft);
            _tractionListText.color = UITheme.SecondaryText;
            var trlr = _tractionListText.GetComponent<RectTransform>();
            trlr.anchorMin = new Vector2(0, 1); trlr.anchorMax = new Vector2(0.5f, 1);
            trlr.pivot = new Vector2(0, 1);
            trlr.anchoredPosition = new Vector2(28, -174);
            trlr.sizeDelta = new Vector2(-20, 180);

            // Category permits section
            var categoryLabel = UiHelper.CreateText(panel.transform, "CategoryLabel", "", 14, TextAlignmentOptions.TopLeft);
            categoryLabel.fontStyle = FontStyles.Bold;
            var clr = categoryLabel.GetComponent<RectTransform>();
            clr.anchorMin = new Vector2(0.5f, 1); clr.anchorMax = new Vector2(1, 1);
            clr.pivot = new Vector2(0, 1);
            clr.anchoredPosition = new Vector2(8, -148);
            clr.sizeDelta = new Vector2(-28, 22);

            _categoryListText = UiHelper.CreateText(panel.transform, "CategoryList", "", 12, TextAlignmentOptions.TopLeft);
            _categoryListText.color = UITheme.SecondaryText;
            var calr = _categoryListText.GetComponent<RectTransform>();
            calr.anchorMin = new Vector2(0.5f, 1); calr.anchorMax = new Vector2(1, 1);
            calr.pivot = new Vector2(0, 1);
            calr.anchoredPosition = new Vector2(8, -174);
            calr.sizeDelta = new Vector2(-28, 180);

            // Close button
            var closeBtn = UiHelper.CreateButton(panel.transform, "Close",
                LocalizationService.Get("personnel.qualifications.close"), Hide);
            var cbr = closeBtn.GetComponent<RectTransform>();
            cbr.anchorMin = new Vector2(0.5f, 0); cbr.anchorMax = new Vector2(0.5f, 0);
            cbr.pivot = new Vector2(0.5f, 0);
            cbr.anchoredPosition = new Vector2(0, 22);
            cbr.sizeDelta = new Vector2(180, 36);

            // Headers — refresh dla labelek dla i18n
            tractionLabel.text = LocalizationService.Get("personnel.qualifications.traction.title");
            categoryLabel.text = LocalizationService.Get("personnel.qualifications.category.title");
        }

        void Refresh()
        {
            var emp = PersonnelService.GetById(_currentEmployeeId);
            if (emp == null) { Hide(); return; }

            _headerText.text = LocalizationService.Get("personnel.qualifications.title_format", emp.DisplayFullName);
            _disclaimerText.text = LocalizationService.Get("personnel.qualifications.disclaimer");

            var quals = PersonnelService.GetQualifications(emp.employeeId);
            string none = LocalizationService.Get("personnel.qualifications.none");

            _tractionListText.text = (quals.tractionPermits == null || quals.tractionPermits.Count == 0)
                ? none
                : string.Join("\n", quals.tractionPermits);
            _categoryListText.text = (quals.categoryPermits == null || quals.categoryPermits.Count == 0)
                ? none
                : string.Join("\n", quals.categoryPermits);
        }
    }
}
