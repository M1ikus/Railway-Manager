using System;
using TMPro;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using RailwayManager;
using RailwayManager.Core;
using RailwayManager.Timetable;
using RailwayManager.SharedUI;
using RailwayManager.SharedUI.Localization;

namespace RailwayManager.Personnel
{
    /// <summary>
    /// M8-8: Master-detail lista turnusow pracowniczych.
    ///
    /// Lewa kolumna (40%): lista turnusow z filtrami (role, status) + przycisk "+ Nowy turnus"
    /// Prawa kolumna (60%): detail wybranego turnusu (nazwa, pracownik, sluzby summary, przycisk Edytuj)
    ///
    /// Nawigacja: klik wiersza → select → refresh right. Klik "Edytuj" → <see cref="CrewCirculationEditorUI.Show"/>.
    /// </summary>
    public class CrewCirculationListUI : MonoBehaviour
    {
        public static CrewCirculationListUI Instance { get; private set; }

        Canvas _canvas;
        GameObject _root;
        TMP_Dropdown _filterRoleDropdown;
        TMP_Dropdown _filterStatusDropdown;
        RectTransform _listContent;
        TextMeshProUGUI _detailHeaderText;
        TextMeshProUGUI _detailInfoText;
        Button _editBtn;
        Button _deleteBtn;
        Button _activateBtn;
        Button _newFromRoleBtn;
        TextMeshProUGUI _countText;

        int _selectedCirculationId = -1;
        readonly List<GameObject> _listRows = new();
        bool _isVisible;

        public static CrewCirculationListUI EnsureExists()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("CrewCirculationListUI");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<CrewCirculationListUI>();
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

        void OnEnable() { CrewCirculationService.OnAnyChange += OnChanged; }
        void OnDisable() { CrewCirculationService.OnAnyChange -= OnChanged; }
        void OnChanged() { if (_isVisible) Refresh(); }

        void Update()
        {
            if (!_isVisible) return;
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) Hide();
        }

        public void Show()
        {
            _root.SetActive(true);
            _isVisible = true;
            Refresh();
        }

        public void Hide()
        {
            _root.SetActive(false);
            _isVisible = false;
        }

        // ═══ Build UI ═══

        void BuildUI()
        {
            var canvasGo = new GameObject("CrewListCanvas");
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 105;
            // MUI-10: standard canvas scaler config (ref 1920×1080, match 0.5)
            UITheme.ApplyCanvasScaler(canvasGo.AddComponent<CanvasScaler>());
            canvasGo.AddComponent<GraphicRaycaster>();

            _root = new GameObject("Root");
            _root.transform.SetParent(canvasGo.transform, false);
            var rootRect = _root.AddComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero; rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero; rootRect.offsetMax = Vector2.zero;
            _root.AddComponent<Image>().color = UITheme.WithAlpha(Color.black, 0.72f);

            var panel = new GameObject("Panel");
            panel.transform.SetParent(_root.transform, false);
            var panelRect = panel.AddComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f); panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(1200, 780);
            panelRect.anchoredPosition = Vector2.zero;
            var panelImg = panel.AddComponent<Image>();
            UITheme.ApplySurface(panelImg, UITheme.WithAlpha(UITheme.OverlayPanelStrong, 0.98f), UIShapePreset.PanelLarge);

            var headerCard = UiHelper.CreatePanel(panel.transform, "HeaderCard",
                UITheme.WithAlpha(UITheme.SecondarySurface, 0.34f), UIShapePreset.Panel);
            Place(headerCard.GetComponent<RectTransform>(), 0, 1, 1, 1, 0.5f, 1, new Vector2(0, -12), new Vector2(-24, 54));

            // Header
            var header = UiHelper.CreateText(panel.transform, "Header", LocalizationService.Get("personnel.crew_list.title"), 22, TextAlignmentOptions.MidlineLeft);
            Place(header.GetComponent<RectTransform>(), 0, 1, 1, 1, 0, 1, new Vector2(28, -20), new Vector2(-200, 40));

            var closeBtn = UiHelper.CreateButton(panel.transform, "Close", LocalizationService.Get("personnel.crew_list.close_btn"), Hide);
            Place(closeBtn.GetComponent<RectTransform>(), 1, 1, 1, 1, 1, 1, new Vector2(-24, -22), new Vector2(132, 32));

            // Filter bar (left column)
            var filterBar = new GameObject("FilterBar");
            filterBar.transform.SetParent(panel.transform, false);
            var fbRt = filterBar.AddComponent<RectTransform>();
            Place(fbRt, 0, 1, 0.42f, 1, 0, 1, new Vector2(20, -60), new Vector2(-10, 45));
            var filterImg = filterBar.AddComponent<Image>();
            UITheme.ApplySurface(filterImg, UITheme.WithAlpha(UITheme.SecondarySurface, 0.34f), UIShapePreset.Panel);

            var hlg = filterBar.AddComponent<HorizontalLayoutGroup>();
            hlg.childControlHeight = true; hlg.childControlWidth = false;
            hlg.padding = UITheme.Padding(UITheme.Spacing.Sm);
            hlg.spacing = UITheme.Spacing.Sm;

            _filterRoleDropdown = UiHelper.CreateDropdown(filterBar.transform, "RoleFilter",
                new List<string> {
                    LocalizationService.Get("personnel.crew_list.filter_role_all"),
                    LocalizationService.Get("personnel.role.driver"),
                    LocalizationService.Get("personnel.role.conductor")
                });
            _filterRoleDropdown.onValueChanged.AddListener(_ => Refresh());
            var rdLe = _filterRoleDropdown.gameObject.AddComponent<LayoutElement>();
            rdLe.preferredWidth = 160; rdLe.preferredHeight = 30;

            _filterStatusDropdown = UiHelper.CreateDropdown(filterBar.transform, "StatusFilter",
                new List<string> {
                    LocalizationService.Get("personnel.crew_list.filter_status_all"),
                    LocalizationService.Get("personnel.crew_list.status.draft"),
                    LocalizationService.Get("personnel.crew_list.status.active"),
                    LocalizationService.Get("personnel.crew_list.status.paused"),
                    LocalizationService.Get("personnel.crew_list.status.archived")
                });
            _filterStatusDropdown.onValueChanged.AddListener(_ => Refresh());
            var sdLe = _filterStatusDropdown.gameObject.AddComponent<LayoutElement>();
            sdLe.preferredWidth = 170; sdLe.preferredHeight = 30;

            _countText = UiHelper.CreateText(filterBar.transform, "CountLbl", "", 12, TextAlignmentOptions.MidlineLeft);
            _countText.color = UITheme.SecondaryText;
            var ctLe = _countText.gameObject.AddComponent<LayoutElement>();
            ctLe.flexibleWidth = 1;

            // New button + Auto-generator
            _newFromRoleBtn = UiHelper.CreateButton(panel.transform, "New", LocalizationService.Get("personnel.crew_list.btn.new"), OnNewTurnusClicked);
            Place(_newFromRoleBtn.GetComponent<RectTransform>(), 0, 0, 0, 0, 0, 0, new Vector2(20, 15), new Vector2(140, 40));
            var newImg = _newFromRoleBtn.GetComponent<Image>();
            if (newImg != null) UITheme.ApplySurface(newImg, UITheme.WithAlpha(UITheme.Success, 0.92f), UIShapePreset.Pill);

            var autoBtn = UiHelper.CreateButton(panel.transform, "AutoGen", LocalizationService.Get("personnel.crew_list.btn.auto_gen"),
                () => CrewAutoGeneratorModal.EnsureExists().Show());
            Place(autoBtn.GetComponent<RectTransform>(), 0, 0, 0.42f, 0, 0, 0, new Vector2(165, 15), new Vector2(-10, 40));
            var autoImg = autoBtn.GetComponent<Image>();
            if (autoImg != null) UITheme.ApplySurface(autoImg, UITheme.WithAlpha(UITheme.PrimaryAccent, 0.92f), UIShapePreset.Pill);

            // List scroll
            var scroll = new GameObject("ListScroll");
            scroll.transform.SetParent(panel.transform, false);
            var scRt = scroll.AddComponent<RectTransform>();
            Place(scRt, 0, 0, 0.42f, 1, 0.5f, 0.5f, Vector2.zero, Vector2.zero);
            scRt.offsetMin = new Vector2(20, 65); scRt.offsetMax = new Vector2(-10, -110);
            var scrollImg = scroll.AddComponent<Image>();
            UITheme.ApplySurface(scrollImg, UITheme.WithAlpha(UITheme.PrimarySurface, 0.32f), UIShapePreset.Panel);
            var sr = scroll.AddComponent<ScrollRect>();
            sr.horizontal = false; sr.vertical = true;

            var vp = new GameObject("Viewport");
            vp.transform.SetParent(scroll.transform, false);
            var vprt = vp.AddComponent<RectTransform>();
            vprt.anchorMin = Vector2.zero; vprt.anchorMax = Vector2.one;
            vprt.offsetMin = new Vector2(6, 6); vprt.offsetMax = new Vector2(-6, -6);
            var viewportImg = vp.AddComponent<Image>();
            UITheme.ApplySurface(viewportImg, UITheme.WithAlpha(UITheme.TopBarInset, 0.9f), UIShapePreset.Inset);
            vp.AddComponent<Mask>().showMaskGraphic = true;
            sr.viewport = vprt;

            var content = new GameObject("Content");
            content.transform.SetParent(vp.transform, false);
            _listContent = content.AddComponent<RectTransform>();
            _listContent.anchorMin = new Vector2(0, 1); _listContent.anchorMax = new Vector2(1, 1);
            _listContent.pivot = new Vector2(0.5f, 1);
            _listContent.anchoredPosition = Vector2.zero;
            var vlg = content.AddComponent<VerticalLayoutGroup>();
            vlg.childControlHeight = false; vlg.childControlWidth = true;
            vlg.childForceExpandWidth = true;
            vlg.padding = UITheme.Padding(UITheme.Spacing.Sm);
            vlg.spacing = UITheme.Spacing.Sm;
            var csf = content.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            sr.content = _listContent;

            // Detail panel (right)
            var detailPanel = new GameObject("DetailPanel");
            detailPanel.transform.SetParent(panel.transform, false);
            var dpRt = detailPanel.AddComponent<RectTransform>();
            Place(dpRt, 0.42f, 0, 1, 1, 0.5f, 0.5f, Vector2.zero, Vector2.zero);
            dpRt.offsetMin = new Vector2(10, 15); dpRt.offsetMax = new Vector2(-20, -65);
            var detailImg = detailPanel.AddComponent<Image>();
            UITheme.ApplySurface(detailImg, UITheme.WithAlpha(UITheme.SecondarySurface, 0.3f), UIShapePreset.Panel);

            _detailHeaderText = UiHelper.CreateText(detailPanel.transform, "DH", LocalizationService.Get("personnel.crew_list.detail.select_prompt"), 16, TextAlignmentOptions.TopLeft);
            Place(_detailHeaderText.GetComponent<RectTransform>(), 0, 1, 1, 1, 0.5f, 1, new Vector2(0, -14), new Vector2(-24, 84));

            _detailInfoText = UiHelper.CreateText(detailPanel.transform, "DI", "", 12, TextAlignmentOptions.TopLeft);
            _detailInfoText.richText = true;
            Place(_detailInfoText.GetComponent<RectTransform>(), 0, 0, 1, 1, 0.5f, 1, new Vector2(0, -102), new Vector2(-28, -172));

            _editBtn = UiHelper.CreateButton(detailPanel.transform, "Edit", LocalizationService.Get("personnel.crew_list.btn.edit"), OnEditClicked);
            Place(_editBtn.GetComponent<RectTransform>(), 0, 0, 0, 0, 0, 0, new Vector2(15, 15), new Vector2(160, 40));
            _editBtn.interactable = false;

            _activateBtn = UiHelper.CreateButton(detailPanel.transform, "Act", LocalizationService.Get("personnel.crew_list.btn.activate"), OnActivateClicked);
            Place(_activateBtn.GetComponent<RectTransform>(), 0, 0, 0, 0, 0, 0, new Vector2(185, 15), new Vector2(130, 40));
            _activateBtn.interactable = false;

            _deleteBtn = UiHelper.CreateButton(detailPanel.transform, "Del", LocalizationService.Get("personnel.crew_list.btn.delete"), OnDeleteClicked);
            Place(_deleteBtn.GetComponent<RectTransform>(), 1, 0, 1, 0, 1, 0, new Vector2(-15, 15), new Vector2(130, 40));
            var delImg = _deleteBtn.GetComponent<Image>();
            if (delImg != null) UITheme.ApplySurface(delImg, UITheme.WithAlpha(UITheme.Danger, 0.92f), UIShapePreset.Pill);
            _deleteBtn.interactable = false;
        }

        // ═══ Refresh ═══

        void Refresh()
        {
            var filtered = FilterList();
            _countText.text = string.Format(LocalizationService.Get("personnel.crew_list.count_format"),
                filtered.Count, CrewCirculationService.All.Count);

            foreach (var r in _listRows) if (r != null) Destroy(r);
            _listRows.Clear();

            if (filtered.Count == 0)
            {
                var empty = UiHelper.CreateText(_listContent, "Empty",
                    LocalizationService.Get("personnel.crew_list.empty"), 12, TextAlignmentOptions.Center);
                empty.color = UITheme.SecondaryText;
                var er = empty.GetComponent<RectTransform>();
                er.sizeDelta = new Vector2(0, 50);
                var emptyBg = empty.gameObject.AddComponent<Image>();
                UITheme.ApplySurface(emptyBg, UITheme.WithAlpha(UITheme.SecondarySurface, 0.72f), UIShapePreset.Inset);
                _listRows.Add(empty.gameObject);
            }
            else
            {
                foreach (var c in filtered) AddListRow(c);
            }

            RefreshDetail();
        }

        List<CrewCirculation> FilterList()
        {
            var result = new List<CrewCirculation>();
            int roleIdx = _filterRoleDropdown.value;
            int statusIdx = _filterStatusDropdown.value;

            foreach (var c in CrewCirculationService.All)
            {
                if (roleIdx == 1 && c.role != EmployeeRole.Driver) continue;
                if (roleIdx == 2 && c.role != EmployeeRole.Conductor) continue;

                if (statusIdx == 1 && c.status != CirculationStatus.Draft) continue;
                if (statusIdx == 2 && c.status != CirculationStatus.Active) continue;
                if (statusIdx == 3 && c.status != CirculationStatus.Paused) continue;
                if (statusIdx == 4 && c.status != CirculationStatus.Archived) continue;

                result.Add(c);
            }
            return result;
        }

        void AddListRow(CrewCirculation c)
        {
            var row = new GameObject($"Row_{c.crewCirculationId}");
            row.transform.SetParent(_listContent, false);
            var rt = row.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0, 50);

            bool selected = c.crewCirculationId == _selectedCirculationId;
            Color rowColor = selected
                ? UITheme.WithAlpha(UITheme.PrimaryAccent, 0.78f)
                : UITheme.WithAlpha(UITheme.SecondarySurface, 0.82f);
            var rowImg = row.AddComponent<Image>();
            UITheme.ApplySurface(rowImg, rowColor, UIShapePreset.Inset);

            var btn = row.AddComponent<Button>();
            int localId = c.crewCirculationId;
            btn.onClick.AddListener(() => { _selectedCirculationId = localId; Refresh(); });

            string stars = c.assignedEmployeeId > 0
                ? (PersonnelService.GetById(c.assignedEmployeeId)?.DisplayFullName ?? LocalizationService.Get("personnel.crew_list.unknown_employee"))
                : LocalizationService.Get("personnel.crew_list.row.no_employee");
            string statusColor = c.status switch
            {
                CirculationStatus.Active => "#4ADE80",
                CirculationStatus.Draft => "#9CA3AF",
                CirculationStatus.Paused => "#FBBF24",
                _ => "#6B7280"
            };

            var info = UiHelper.CreateText(row.transform, "Info",
                string.Format(LocalizationService.Get("personnel.crew_list.row.format"),
                    c.crewCirculationId, c.name, statusColor, c.status,
                    RoleDefinitions.GetDisplayNamePl(c.role), stars,
                    c.duties.Count, c.durationDays),
                11, TextAlignmentOptions.MidlineLeft);
            info.richText = true;
            Place(info.GetComponent<RectTransform>(), 0, 0, 1, 1, 0.5f, 0.5f, Vector2.zero, Vector2.zero);
            info.rectTransform.offsetMin = new Vector2(10, 2); info.rectTransform.offsetMax = new Vector2(-10, -2);

            _listRows.Add(row);
        }

        void RefreshDetail()
        {
            var c = CrewCirculationService.GetById(_selectedCirculationId);
            if (c == null)
            {
                _detailHeaderText.text = LocalizationService.Get("personnel.crew_list.detail.select_prompt");
                _detailInfoText.text = "";
                _editBtn.interactable = false;
                _activateBtn.interactable = false;
                _deleteBtn.interactable = false;
                return;
            }

            string empName = c.assignedEmployeeId > 0
                ? (PersonnelService.GetById(c.assignedEmployeeId)?.DisplayFullName ?? "#" + c.assignedEmployeeId)
                : LocalizationService.Get("personnel.crew_list.detail.no_employee");

            _detailHeaderText.text = string.Format(LocalizationService.Get("personnel.crew_list.detail.header_format"),
                c.crewCirculationId, c.name,
                RoleDefinitions.GetDisplayNamePl(c.role),
                empName, c.durationDays, c.status);

            var sb = new StringBuilder();
            sb.AppendLine(string.Format(LocalizationService.Get("personnel.crew_list.detail.duties_label_format"), c.duties.Count));
            if (c.duties.Count == 0)
            {
                sb.AppendLine(LocalizationService.Get("personnel.crew_list.detail.no_duties"));
            }
            else
            {
                for (int i = 0; i < c.duties.Count; i++)
                {
                    var d = c.duties[i];
                    sb.AppendFormat(LocalizationService.Get("personnel.crew_list.detail.duty_main_format"),
                        i + 1, d.kind, d.dayOffset + 1);
                    if (!string.IsNullOrEmpty(d.startTimeIso)) sb.Append(d.startTimeIso);
                    if (!string.IsNullOrEmpty(d.endTimeIso)) sb.Append($"→{d.endTimeIso}");
                    if (!string.IsNullOrEmpty(d.endStationName))
                        sb.AppendFormat(LocalizationService.Get("personnel.crew_list.detail.duty_route_format"),
                            d.startStationName, d.endStationName);
                    sb.AppendLine();
                }
            }
            sb.AppendLine();

            var validation = CrewCirculationValidator.Validate(c);
            string vColor = validation.IsValid
                ? (validation.Warnings.Count == 0 ? "#4ADE80" : "#FBBF24")
                : "#F87171";
            sb.AppendLine(string.Format(LocalizationService.Get("personnel.crew_list.detail.validation_format"),
                vColor, validation.GetSummary()));

            _detailInfoText.text = sb.ToString();

            _editBtn.interactable = true;
            _activateBtn.interactable = validation.IsValid && c.status != CirculationStatus.Active;
            _deleteBtn.interactable = c.status != CirculationStatus.Active;
        }

        // ═══ Actions ═══

        void OnNewTurnusClicked()
        {
            int roleIdx = _filterRoleDropdown.value;
            var role = roleIdx == 2 ? EmployeeRole.Conductor : EmployeeRole.Driver;
            var c = CrewCirculationService.Create(string.Format(LocalizationService.Get("personnel.crew_list.new_turnus_format"), DateTime.Now.ToString("HH:mm")), role);
            if (c != null)
            {
                _selectedCirculationId = c.crewCirculationId;
                Refresh();
                CrewCirculationEditorUI.EnsureExists().Show(c.crewCirculationId);
            }
        }

        void OnEditClicked()
        {
            if (_selectedCirculationId <= 0) return;
            CrewCirculationEditorUI.EnsureExists().Show(_selectedCirculationId);
        }

        void OnActivateClicked()
        {
            CrewCirculationService.Activate(_selectedCirculationId);
        }

        void OnDeleteClicked()
        {
            if (CrewCirculationService.Delete(_selectedCirculationId))
            {
                _selectedCirculationId = -1;
                Refresh();
            }
        }

        static void Place(RectTransform rt, float amin_x, float amin_y, float amax_x, float amax_y, float piv_x, float piv_y, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = new Vector2(amin_x, amin_y);
            rt.anchorMax = new Vector2(amax_x, amax_y);
            rt.pivot = new Vector2(piv_x, piv_y);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }
    }
}
