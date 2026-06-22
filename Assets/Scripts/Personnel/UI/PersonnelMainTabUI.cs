using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using RailwayManager.Core;
using RailwayManager.SharedUI;
using RailwayManager.SharedUI.Localization;

namespace RailwayManager.Personnel
{
    /// <summary>
    /// M8-4: Glowny panel personelu — fullscreen overlay z tab bar.
    ///
    /// Sub-taby (§14.2 spec):
    /// - "Mój personel" — <see cref="EmployeeListUI"/> content (lista z filtrami, klik → Details)
    /// - "Rekrutacja" — przycisk "Otwórz pełny panel" → <see cref="RecruitmentUI"/>
    ///
    /// Kolejne taby (Turnusy/Warsztaty/Dyspozytura/Nastawnia/Biuro+R&D/Kasy/Raport plac)
    /// dodawane w odpowiednich podetapach M8-7+ w miare implementacji.
    ///
    /// Otwieranie: <see cref="Show"/> (z PersonnelServiceBootstrap ContextMenu lub shortcut keyboard).
    /// Zamykanie: ESC lub klik [Zamknij].
    ///
    /// Klasa rozbita na partial files per zakladka:
    /// - <c>PersonnelMainTabUI.cs</c>             — pola, lifecycle, BuildUI, tab bar, SwitchTab (ten plik)
    /// - <c>PersonnelMainTabUI.MyStaff.cs</c>     — lista pracownikow + filtry, GetStatusDisplayName (public)
    /// - <c>PersonnelMainTabUI.Recruitment.cs</c> — placeholder z przyciskiem do RecruitmentUI
    /// - <c>PersonnelMainTabUI.Dispatch.cs</c>    — dyspozytorzy + pending actions (M8-7)
    /// - <c>PersonnelMainTabUI.Turnuses.cs</c>    — podsumowanie obiegow pracowniczych (M8-8)
    /// - <c>PersonnelMainTabUI.Traffic.cs</c>     — nastawnia + sliders priorytetow (M8-11)
    /// - <c>PersonnelMainTabUI.Workshops.cs</c>   — przypisania mechanikow do slotow (M8-12)
    /// - <c>PersonnelMainTabUI.Office.cs</c>      — Office + R&D paths (M8-15)
    /// - <c>PersonnelMainTabUI.Kasy.cs</c>        — kasjerzy biletowi przy stacjach (M8-15)
    /// </summary>
    public partial class PersonnelMainTabUI : MonoBehaviour
    {
        public static PersonnelMainTabUI Instance { get; private set; }

        private static readonly Color OverlayBg = UITheme.WithAlpha(Color.black, 0.72f);
        private static readonly Color PanelBg = UITheme.WithAlpha(UITheme.OverlayPanelStrong, 0.98f);
        private static readonly Color HeaderBg = UITheme.WithAlpha(UITheme.TopBarInset, 0.98f);
        private static readonly Color TabBarBg = UITheme.WithAlpha(UITheme.SecondarySurface, 0.35f);
        private static readonly Color ContentBg = UITheme.WithAlpha(UITheme.TopBarInset, 0.92f);
        private static readonly Color ActiveTabBg = UITheme.PrimaryAccent;
        private static readonly Color InactiveTabBg = UITheme.WithAlpha(UITheme.SecondarySurface, 0.96f);

        Canvas _canvas;
        GameObject _root;
        GameObject _panel;

        // Tab bar
        readonly List<(Button btn, TextMeshProUGUI label, string id)> _tabs = new();
        string _activeTabId = "myStaff";

        // Content areas per tab
        GameObject _myStaffContent;
        GameObject _recruitmentContent;
        GameObject _dispatchContent;
        GameObject _turnusesContent;
        GameObject _trafficContent;
        GameObject _workshopsContent;
        GameObject _officeContent;
        GameObject _kasyContent;

        bool _isVisible;

        // Coalesced rebuild — eventy mark dirty zamiast wywoływać Refresh natychmiast.
        // LateUpdate flushuje tylko aktywny tab, raz per klatkę. Mass-mutation (np. PayrollService
        // daily tick wywołujący SetSalary per pracownik) generuje N×OnEmployeesChanged → 1 rebuild
        // zamiast N. Inne (nieaktywne) tab'y rebuild'ują się przy SwitchTab.
        bool _dirtyMyStaff;
        bool _dirtyDispatch;
        bool _dirtyTurnuses;
        bool _dirtyTraffic;
        bool _dirtyWorkshops;
        bool _dirtyOffice;
        bool _dirtyKasy;

        public static PersonnelMainTabUI EnsureExists()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("PersonnelMainTabUI");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<PersonnelMainTabUI>();
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
            PersonnelService.OnEmployeesChanged += OnAnyDataChanged;
            DispatcherService.OnWorkloadChanged += OnDispatchChanged;
            DispatcherService.OnActionCreated += OnDispatchActionChanged;
            DispatcherService.OnActionCompleted += OnDispatchActionChanged;
            CrewCirculationService.OnAnyChange += OnAnyDataChanged;
            TrafficControlService.OnWorkloadChanged += OnTrafficChanged;
            WorkshopAssignmentService.OnAssignmentsChanged += OnWorkshopAssignChanged;
            ResearchService.OnPathsChanged += OnResearchChanged;
            TicketClerkService.OnAssignmentsChanged += OnTicketClerksChanged;
        }

        void OnDisable()
        {
            PersonnelService.OnEmployeesChanged -= OnAnyDataChanged;
            DispatcherService.OnWorkloadChanged -= OnDispatchChanged;
            DispatcherService.OnActionCreated -= OnDispatchActionChanged;
            DispatcherService.OnActionCompleted -= OnDispatchActionChanged;
            CrewCirculationService.OnAnyChange -= OnAnyDataChanged;
            TrafficControlService.OnWorkloadChanged -= OnTrafficChanged;
            WorkshopAssignmentService.OnAssignmentsChanged -= OnWorkshopAssignChanged;
            ResearchService.OnPathsChanged -= OnResearchChanged;
            TicketClerkService.OnAssignmentsChanged -= OnTicketClerksChanged;
        }

        // Hidden-panel guard: subskrypcje w OnEnable żyją cały czas (panel to
        // DontDestroyOnLoad singleton), ale Hide() tylko ukrywa Root (NIE
        // dezaktywuje gameObject'u, więc OnDisable nie strzela). Bez tego guarda
        // niewidoczny panel by rebuild'ował przy każdym evencie z 8 service'ów.

        void OnResearchChanged()
        {
            if (!_isVisible) return;
            _dirtyOffice = true;
        }

        void OnTicketClerksChanged()
        {
            if (!_isVisible) return;
            _dirtyKasy = true;
        }

        void OnWorkshopAssignChanged()
        {
            if (!_isVisible) return;
            _dirtyWorkshops = true;
        }

        void OnTrafficChanged(TrafficControllerWorkload w)
        {
            if (!_isVisible) return;
            _dirtyTraffic = true;
        }

        void OnAnyDataChanged()
        {
            if (!_isVisible) return;
            // Każda zmiana w PersonnelService/CrewCirculationService potencjalnie dotyka
            // wszystkich tabów — mark wszystkie, LateUpdate refreshuje tylko aktywny.
            _dirtyMyStaff = true;
            _dirtyDispatch = true;
            _dirtyTurnuses = true;
            _dirtyTraffic = true;
            _dirtyWorkshops = true;
            _dirtyOffice = true;
            _dirtyKasy = true;
        }

        void OnDispatchChanged(DispatcherWorkload w)
        {
            if (!_isVisible) return;
            _dirtyDispatch = true;
        }

        void OnDispatchActionChanged(PendingDispatchAction a)
        {
            if (!_isVisible) return;
            _dirtyDispatch = true;
        }

        void Update()
        {
            if (!_isVisible) return;
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
            {
                Hide();
            }
        }

        void LateUpdate()
        {
            if (!_isVisible) return;
            FlushDirtyActiveTab();
        }

        void FlushDirtyActiveTab()
        {
            switch (_activeTabId)
            {
                case "myStaff":
                    if (_dirtyMyStaff) { RefreshStaffList(); _dirtyMyStaff = false; }
                    break;
                case "dispatch":
                    if (_dirtyDispatch) { RefreshDispatchContent(); _dirtyDispatch = false; }
                    break;
                case "turnuses":
                    if (_dirtyTurnuses) { RefreshTurnusesSummary(); _dirtyTurnuses = false; }
                    break;
                case "traffic":
                    if (_dirtyTraffic) { RefreshTrafficContent(); _dirtyTraffic = false; }
                    break;
                case "workshops":
                    if (_dirtyWorkshops) { RefreshWorkshopsContent(); _dirtyWorkshops = false; }
                    break;
                case "office":
                    if (_dirtyOffice) { RefreshOfficeContent(); _dirtyOffice = false; }
                    break;
                case "kasy":
                    if (_dirtyKasy) { RefreshKasyContent(); _dirtyKasy = false; }
                    break;
            }
        }

        public void Show(string tabId = "myStaff")
        {
            PersonnelServiceBootstrap.EnsureExists();
            CandidateMarketService.EnsureExists();

            _root.SetActive(true);
            _isVisible = true;
            SwitchTab(tabId);
        }

        public void Hide()
        {
            _root.SetActive(false);
            _isVisible = false;
        }

        // ═══ Build UI ═══

        void BuildUI()
        {
            var canvasGo = new GameObject("PersonnelMainCanvas");
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 90;
            // MUI-10: standard canvas scaler config (ref 1920×1080, match 0.5)
            UITheme.ApplyCanvasScaler(canvasGo.AddComponent<CanvasScaler>());
            canvasGo.AddComponent<GraphicRaycaster>();

            _root = new GameObject("Root");
            _root.transform.SetParent(canvasGo.transform, false);
            var rootRect = _root.AddComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero; rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero; rootRect.offsetMax = Vector2.zero;
            _root.AddComponent<Image>().color = OverlayBg;

            _panel = new GameObject("Panel");
            _panel.transform.SetParent(_root.transform, false);
            var panelRect = _panel.AddComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f); panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(1200, 800);
            panelRect.anchoredPosition = Vector2.zero;
            var panelImg = _panel.AddComponent<Image>();
            UITheme.ApplySurface(panelImg, PanelBg, UIShapePreset.PanelLarge);

            var headerCard = UiHelper.CreatePanel(_panel.transform, "HeaderCard", HeaderBg, UIShapePreset.Panel);
            var headerCardRt = headerCard.GetComponent<RectTransform>();
            headerCardRt.anchorMin = new Vector2(0, 1); headerCardRt.anchorMax = new Vector2(1, 1);
            headerCardRt.pivot = new Vector2(0.5f, 1f);
            headerCardRt.anchoredPosition = new Vector2(0, -12);
            headerCardRt.sizeDelta = new Vector2(-24, 54);

            // Header
            var header = UiHelper.CreateText(headerCard.transform, "Header", LocalizationService.Get("personnel.title"), 22, TextAlignmentOptions.MidlineLeft);
            var hr = header.GetComponent<RectTransform>();
            hr.anchorMin = new Vector2(0, 0);
            hr.anchorMax = new Vector2(1, 1);
            hr.offsetMin = new Vector2(18, 8);
            hr.offsetMax = new Vector2(-180, -8);
            header.color = UITheme.PrimaryText;

            var closeBtn = UiHelper.CreateButton(headerCard.transform, "CloseBtn", LocalizationService.Get("personnel.close_button"), Hide);
            var cbRt = closeBtn.GetComponent<RectTransform>();
            cbRt.anchorMin = new Vector2(1, 0.5f); cbRt.anchorMax = new Vector2(1, 0.5f);
            cbRt.pivot = new Vector2(1, 0.5f);
            cbRt.anchoredPosition = new Vector2(-14, 0);
            cbRt.sizeDelta = new Vector2(150, 34);

            // Tab bar
            BuildTabBar();

            // Content containers
            _myStaffContent = new GameObject("MyStaffContent");
            _myStaffContent.transform.SetParent(_panel.transform, false);
            var msRt = _myStaffContent.AddComponent<RectTransform>();
            msRt.anchorMin = new Vector2(0, 0); msRt.anchorMax = new Vector2(1, 1);
            msRt.offsetMin = new Vector2(18, 18); msRt.offsetMax = new Vector2(-18, -118);
            UiHelper.CreatePanel(_myStaffContent.transform, "Background", ContentBg, UIShapePreset.Panel);
            _myStaffContent.transform.GetChild(0).SetAsFirstSibling();
            StretchChild(_myStaffContent.transform.GetChild(0).GetComponent<RectTransform>());

            _recruitmentContent = new GameObject("RecruitmentContent");
            _recruitmentContent.transform.SetParent(_panel.transform, false);
            var rcRt = _recruitmentContent.AddComponent<RectTransform>();
            rcRt.anchorMin = new Vector2(0, 0); rcRt.anchorMax = new Vector2(1, 1);
            rcRt.offsetMin = new Vector2(18, 18); rcRt.offsetMax = new Vector2(-18, -118);
            UiHelper.CreatePanel(_recruitmentContent.transform, "Background", ContentBg, UIShapePreset.Panel);
            _recruitmentContent.transform.GetChild(0).SetAsFirstSibling();
            StretchChild(_recruitmentContent.transform.GetChild(0).GetComponent<RectTransform>());

            _dispatchContent = new GameObject("DispatchContent");
            _dispatchContent.transform.SetParent(_panel.transform, false);
            var dcRt = _dispatchContent.AddComponent<RectTransform>();
            dcRt.anchorMin = new Vector2(0, 0); dcRt.anchorMax = new Vector2(1, 1);
            dcRt.offsetMin = new Vector2(18, 18); dcRt.offsetMax = new Vector2(-18, -118);
            UiHelper.CreatePanel(_dispatchContent.transform, "Background", ContentBg, UIShapePreset.Panel);
            _dispatchContent.transform.GetChild(0).SetAsFirstSibling();
            StretchChild(_dispatchContent.transform.GetChild(0).GetComponent<RectTransform>());

            _turnusesContent = new GameObject("TurnusesContent");
            _turnusesContent.transform.SetParent(_panel.transform, false);
            var tcRt = _turnusesContent.AddComponent<RectTransform>();
            tcRt.anchorMin = new Vector2(0, 0); tcRt.anchorMax = new Vector2(1, 1);
            tcRt.offsetMin = new Vector2(18, 18); tcRt.offsetMax = new Vector2(-18, -118);
            UiHelper.CreatePanel(_turnusesContent.transform, "Background", ContentBg, UIShapePreset.Panel);
            _turnusesContent.transform.GetChild(0).SetAsFirstSibling();
            StretchChild(_turnusesContent.transform.GetChild(0).GetComponent<RectTransform>());

            _trafficContent = new GameObject("TrafficContent");
            _trafficContent.transform.SetParent(_panel.transform, false);
            var trRt = _trafficContent.AddComponent<RectTransform>();
            trRt.anchorMin = new Vector2(0, 0); trRt.anchorMax = new Vector2(1, 1);
            trRt.offsetMin = new Vector2(18, 18); trRt.offsetMax = new Vector2(-18, -118);
            UiHelper.CreatePanel(_trafficContent.transform, "Background", ContentBg, UIShapePreset.Panel);
            _trafficContent.transform.GetChild(0).SetAsFirstSibling();
            StretchChild(_trafficContent.transform.GetChild(0).GetComponent<RectTransform>());

            _workshopsContent = new GameObject("WorkshopsContent");
            _workshopsContent.transform.SetParent(_panel.transform, false);
            var wsRt = _workshopsContent.AddComponent<RectTransform>();
            wsRt.anchorMin = new Vector2(0, 0); wsRt.anchorMax = new Vector2(1, 1);
            wsRt.offsetMin = new Vector2(18, 18); wsRt.offsetMax = new Vector2(-18, -118);
            UiHelper.CreatePanel(_workshopsContent.transform, "Background", ContentBg, UIShapePreset.Panel);
            _workshopsContent.transform.GetChild(0).SetAsFirstSibling();
            StretchChild(_workshopsContent.transform.GetChild(0).GetComponent<RectTransform>());

            _officeContent = new GameObject("OfficeContent");
            _officeContent.transform.SetParent(_panel.transform, false);
            var ocRt = _officeContent.AddComponent<RectTransform>();
            ocRt.anchorMin = new Vector2(0, 0); ocRt.anchorMax = new Vector2(1, 1);
            ocRt.offsetMin = new Vector2(18, 18); ocRt.offsetMax = new Vector2(-18, -118);
            UiHelper.CreatePanel(_officeContent.transform, "Background", ContentBg, UIShapePreset.Panel);
            _officeContent.transform.GetChild(0).SetAsFirstSibling();
            StretchChild(_officeContent.transform.GetChild(0).GetComponent<RectTransform>());

            _kasyContent = new GameObject("KasyContent");
            _kasyContent.transform.SetParent(_panel.transform, false);
            var kcRt = _kasyContent.AddComponent<RectTransform>();
            kcRt.anchorMin = new Vector2(0, 0); kcRt.anchorMax = new Vector2(1, 1);
            kcRt.offsetMin = new Vector2(18, 18); kcRt.offsetMax = new Vector2(-18, -118);
            UiHelper.CreatePanel(_kasyContent.transform, "Background", ContentBg, UIShapePreset.Panel);
            _kasyContent.transform.GetChild(0).SetAsFirstSibling();
            StretchChild(_kasyContent.transform.GetChild(0).GetComponent<RectTransform>());

            BuildMyStaffContent();
            BuildRecruitmentContent();
            BuildDispatchContent();
            BuildTurnusesContent();
            BuildTrafficContent();
            BuildWorkshopsContent();
            BuildOfficeContent();
            BuildKasyContent();

            SwitchTab("myStaff");
        }

        void BuildTabBar()
        {
            var bar = new GameObject("TabBar");
            bar.transform.SetParent(_panel.transform, false);
            var barRt = bar.AddComponent<RectTransform>();
            barRt.anchorMin = new Vector2(0, 1); barRt.anchorMax = new Vector2(1, 1);
            barRt.pivot = new Vector2(0.5f, 1);
            barRt.anchoredPosition = new Vector2(0, -74);
            barRt.sizeDelta = new Vector2(-24, 44);

            var barImg = bar.AddComponent<Image>();
            UITheme.ApplySurface(barImg, TabBarBg, UIShapePreset.Panel);

            var hlg = bar.AddComponent<HorizontalLayoutGroup>();
            hlg.childControlHeight = true; hlg.childControlWidth = false;
            hlg.childForceExpandHeight = true;
            hlg.padding = UITheme.Padding(UITheme.Spacing.Md, UITheme.Spacing.Sm);
            hlg.spacing = UITheme.Spacing.Sm;

            AddTab(bar.transform, "myStaff", LocalizationService.Get("personnel.tabs.my_staff"), 180);
            AddTab(bar.transform, "recruit", LocalizationService.Get("personnel.tabs.recruit"), 140);
            AddTab(bar.transform, "dispatch", LocalizationService.Get("personnel.tabs.dispatch"), 140);
            AddTab(bar.transform, "turnuses", LocalizationService.Get("personnel.tabs.turnuses"), 130);
            AddTab(bar.transform, "traffic", LocalizationService.Get("personnel.tabs.traffic"), 130);
            AddTab(bar.transform, "workshops", LocalizationService.Get("personnel.tabs.workshops"), 130);
            AddTab(bar.transform, "office", LocalizationService.Get("personnel.tabs.office"), 140);
            AddTab(bar.transform, "kasy", LocalizationService.Get("personnel.tabs.kasy"), 100);
            // Future M8-16+: Raport plac
        }

        void AddTab(Transform parent, string id, string label, float width)
        {
            var btn = UiHelper.CreateButton(parent, $"Tab_{id}", label, () => SwitchTab(id));
            var le = btn.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.preferredHeight = 32;
            var txt = btn.GetComponentInChildren<TextMeshProUGUI>();
            _tabs.Add((btn, txt, id));
        }

        void SwitchTab(string id)
        {
            _activeTabId = id;
            foreach (var (btn, label, tabId) in _tabs)
            {
                bool active = tabId == id;
                var img = btn.GetComponent<Image>();
                if (img != null)
                    UITheme.ApplySurface(img, active ? ActiveTabBg : InactiveTabBg, active ? UIShapePreset.Pill : UIShapePreset.Button);
                if (label != null)
                {
                    label.fontStyle = active ? FontStyles.Bold : FontStyles.Normal;
                    label.color = active ? UITheme.InverseText : UITheme.PrimaryText;
                }
            }

            _myStaffContent.SetActive(id == "myStaff");
            _recruitmentContent.SetActive(id == "recruit");
            _dispatchContent.SetActive(id == "dispatch");
            _turnusesContent.SetActive(id == "turnuses");
            _trafficContent.SetActive(id == "traffic");
            _workshopsContent.SetActive(id == "workshops");
            _officeContent.SetActive(id == "office");
            _kasyContent.SetActive(id == "kasy");

            // Refresh + clear dirty flag dla nowego aktywnego tab (FlushDirtyActiveTab już nie musi).
            if (id == "myStaff") { RefreshStaffList(); _dirtyMyStaff = false; }
            else if (id == "dispatch") { RefreshDispatchContent(); _dirtyDispatch = false; }
            else if (id == "turnuses") { RefreshTurnusesSummary(); _dirtyTurnuses = false; }
            else if (id == "traffic") { RefreshTrafficContent(); _dirtyTraffic = false; }
            else if (id == "workshops") { RefreshWorkshopsContent(); _dirtyWorkshops = false; }
            else if (id == "office") { RefreshOfficeContent(); _dirtyOffice = false; }
            else if (id == "kasy") { RefreshKasyContent(); _dirtyKasy = false; }
        }

        static void StretchChild(RectTransform rt)
        {
            if (rt == null) return;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
