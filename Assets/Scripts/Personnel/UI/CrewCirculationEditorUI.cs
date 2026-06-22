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
    /// M8-8: Edytor pojedynczego turnusu pracowniczego.
    ///
    /// Layout:
    /// - Header: nazwa turnusu (editable), status, przyciski Zapisz/Zamknij
    /// - Top bar: role (readonly), przypisany pracownik dropdown, durationDays dropdown 1-3
    /// - Lista sluzb (scrollable): per duty row z info + [↑][↓][✕] + (dla Overnight) [Hotel]
    /// - Przyciski dodawania: [+ Service] [+ Deadhead] [+ Break] [+ Handover] [+ Overnight]
    /// - Add Duty modal (inline panel): pola form startTime/endTime/startStation/endStation/dayOffset/referencedTrainRunId
    /// - Footer: walidator live output (errors/warnings) + [Aktywuj] / [Draft] / [Archiwum]
    ///
    /// Drag&amp;drop pool TrainRun → duty: placeholder M8-11 (uproszczenie: manualne wpisywanie referencedTrainRunId).
    /// </summary>
    public class CrewCirculationEditorUI : MonoBehaviour
    {
        public static CrewCirculationEditorUI Instance { get; private set; }

        Canvas _canvas;
        GameObject _root;
        TMP_InputField _nameInput;
        TMP_Dropdown _employeeDropdown;
        TMP_Dropdown _durationDropdown;
        RectTransform _dutiesContent;
        TextMeshProUGUI _validationText;
        TextMeshProUGUI _statusLabel;
        Button _activateBtn;
        TextMeshProUGUI _activateBtnLabel;

        // Add duty inline form
        GameObject _addDutyForm;
        TMP_Dropdown _addKindDropdown;
        TMP_InputField _addDayOffsetInput;
        TMP_InputField _addStartTimeInput;
        TMP_InputField _addEndTimeInput;
        TMP_InputField _addStartStationInput;
        TMP_InputField _addEndStationInput;
        TMP_InputField _addTrainRunIdInput;

        int _currentCirculationId = -1;
        readonly List<GameObject> _dutyRows = new();
        readonly List<int> _employeeDropdownIds = new();
        bool _isVisible;

        public static CrewCirculationEditorUI EnsureExists()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("CrewCirculationEditorUI");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<CrewCirculationEditorUI>();
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
            CrewCirculationService.OnAnyChange += OnChanged;
            PersonnelService.OnEmployeesChanged += OnChanged;
        }
        void OnDisable()
        {
            CrewCirculationService.OnAnyChange -= OnChanged;
            PersonnelService.OnEmployeesChanged -= OnChanged;
        }
        void OnChanged() { if (_isVisible) Refresh(); }

        void Update()
        {
            if (!_isVisible) return;
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) Hide();
        }

        public void Show(int circulationId)
        {
            _currentCirculationId = circulationId;
            _root.SetActive(true);
            _isVisible = true;
            Refresh();
        }

        public void Hide()
        {
            _root.SetActive(false);
            _isVisible = false;
            _currentCirculationId = -1;
        }

        // ═══ Build UI ═══

        void BuildUI()
        {
            var canvasGo = new GameObject("CrewEditorCanvas");
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 115;
            // MUI-10: standard canvas scaler config (ref 1920×1080, match 0.5)
            UITheme.ApplyCanvasScaler(canvasGo.AddComponent<CanvasScaler>());
            canvasGo.AddComponent<GraphicRaycaster>();

            _root = new GameObject("Root");
            _root.transform.SetParent(canvasGo.transform, false);
            var rootRect = _root.AddComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero; rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero; rootRect.offsetMax = Vector2.zero;
            _root.AddComponent<Image>().color = UITheme.WithAlpha(Color.black, 0.78f);

            var panel = new GameObject("Panel");
            panel.transform.SetParent(_root.transform, false);
            var panelRect = panel.AddComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f); panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(1100, 760);
            panelRect.anchoredPosition = Vector2.zero;
            var panelImg = panel.AddComponent<Image>();
            UITheme.ApplySurface(panelImg, UITheme.WithAlpha(UITheme.OverlayPanelStrong, 0.98f), UIShapePreset.PanelLarge);

            var headerCard = UiHelper.CreatePanel(panel.transform, "HeaderCard",
                UITheme.WithAlpha(UITheme.SecondarySurface, 0.34f), UIShapePreset.Panel);
            Place(headerCard.GetComponent<RectTransform>(), 0, 1, 1, 1, 0.5f, 1, new Vector2(0, -12), new Vector2(-24, 54));

            var controlsCard = UiHelper.CreatePanel(panel.transform, "ControlsCard",
                UITheme.WithAlpha(UITheme.SecondarySurface, 0.3f), UIShapePreset.Panel);
            Place(controlsCard.GetComponent<RectTransform>(), 0, 1, 1, 1, 0.5f, 1, new Vector2(0, -72), new Vector2(-24, 60));

            var dutiesCard = UiHelper.CreatePanel(panel.transform, "DutiesCard",
                UITheme.WithAlpha(UITheme.SecondarySurface, 0.28f), UIShapePreset.Panel);
            dutiesCard.GetComponent<RectTransform>().anchorMin = new Vector2(0, 0);
            dutiesCard.GetComponent<RectTransform>().anchorMax = new Vector2(0.58f, 1);
            dutiesCard.GetComponent<RectTransform>().offsetMin = new Vector2(20, 100);
            dutiesCard.GetComponent<RectTransform>().offsetMax = new Vector2(0, -100);

            var formCard = UiHelper.CreatePanel(panel.transform, "FormCard",
                UITheme.WithAlpha(UITheme.SecondarySurface, 0.28f), UIShapePreset.Panel);
            formCard.GetComponent<RectTransform>().anchorMin = new Vector2(0.58f, 0);
            formCard.GetComponent<RectTransform>().anchorMax = new Vector2(1, 1);
            formCard.GetComponent<RectTransform>().offsetMin = new Vector2(10, 100);
            formCard.GetComponent<RectTransform>().offsetMax = new Vector2(-20, -100);

            var footerCard = UiHelper.CreatePanel(panel.transform, "FooterCard",
                UITheme.WithAlpha(UITheme.SecondarySurface, 0.3f), UIShapePreset.Panel);
            Place(footerCard.GetComponent<RectTransform>(), 0, 0, 1, 0, 0.5f, 0, new Vector2(0, 12), new Vector2(-24, 78));

            // Header
            _nameInput = UiHelper.CreateInputField(panel.transform, "NameInput", LocalizationService.Get("personnel.crew_editor.name_placeholder"));
            Place(_nameInput.GetComponent<RectTransform>(), 0, 1, 0, 1, 0, 1, new Vector2(28, -18), new Vector2(450, 35));
            _nameInput.onEndEdit.AddListener(OnNameChanged);

            _statusLabel = UiHelper.CreateText(panel.transform, "Status", "", 14, TextAlignmentOptions.MidlineLeft);
            Place(_statusLabel.GetComponent<RectTransform>(), 0, 1, 0, 1, 0, 1, new Vector2(490, -20), new Vector2(250, 30));

            var closeBtn = UiHelper.CreateButton(panel.transform, "Close", LocalizationService.Get("personnel.crew_editor.close_btn"), Hide);
            Place(closeBtn.GetComponent<RectTransform>(), 1, 1, 1, 1, 1, 1, new Vector2(-24, -22), new Vector2(132, 32));

            // Top bar: employee + duration
            var empLbl = UiHelper.CreateText(panel.transform, "EmpLbl", LocalizationService.Get("personnel.crew_editor.employee_label"), 13, TextAlignmentOptions.MidlineLeft);
            Place(empLbl.GetComponent<RectTransform>(), 0, 1, 0, 1, 0, 1, new Vector2(20, -65), new Vector2(100, 28));

            _employeeDropdown = UiHelper.CreateDropdown(panel.transform, "EmpDd", new List<string> { LocalizationService.Get("personnel.crew_editor.employee_none") });
            _employeeDropdown.onValueChanged.AddListener(OnEmployeeChanged);
            Place(_employeeDropdown.GetComponent<RectTransform>(), 0, 1, 0, 1, 0, 1, new Vector2(125, -60), new Vector2(340, 30));

            var durLbl = UiHelper.CreateText(panel.transform, "DurLbl", LocalizationService.Get("personnel.crew_editor.duration_label"), 13, TextAlignmentOptions.MidlineLeft);
            Place(durLbl.GetComponent<RectTransform>(), 0, 1, 0, 1, 0, 1, new Vector2(480, -65), new Vector2(80, 28));

            _durationDropdown = UiHelper.CreateDropdown(panel.transform, "DurDd",
                new List<string> {
                    LocalizationService.Get("personnel.crew_editor.duration_1"),
                    LocalizationService.Get("personnel.crew_editor.duration_2"),
                    LocalizationService.Get("personnel.crew_editor.duration_3")
                });
            _durationDropdown.onValueChanged.AddListener(OnDurationChanged);
            Place(_durationDropdown.GetComponent<RectTransform>(), 0, 1, 0, 1, 0, 1, new Vector2(560, -60), new Vector2(220, 30));

            // Duties list scroll
            var scroll = new GameObject("DutiesScroll");
            scroll.transform.SetParent(panel.transform, false);
            var scRt = scroll.AddComponent<RectTransform>();
            scRt.anchorMin = new Vector2(0, 0); scRt.anchorMax = new Vector2(0.58f, 1);
            scRt.offsetMin = new Vector2(20, 180); scRt.offsetMax = new Vector2(0, -100);
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
            _dutiesContent = content.AddComponent<RectTransform>();
            _dutiesContent.anchorMin = new Vector2(0, 1); _dutiesContent.anchorMax = new Vector2(1, 1);
            _dutiesContent.pivot = new Vector2(0.5f, 1);
            _dutiesContent.anchoredPosition = Vector2.zero;
            var vlg = content.AddComponent<VerticalLayoutGroup>();
            vlg.childControlHeight = false; vlg.childControlWidth = true;
            vlg.childForceExpandWidth = true;
            vlg.padding = UITheme.Padding(UITheme.Spacing.Sm);
            vlg.spacing = UITheme.Spacing.Sm;
            var csf = content.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            sr.content = _dutiesContent;

            // Add duty form (prawa kolumna)
            BuildAddDutyForm(panel.transform);

            // Footer: validation + activation
            _validationText = UiHelper.CreateText(panel.transform, "Validation", "", 12, TextAlignmentOptions.TopLeft);
            _validationText.richText = true;
            Place(_validationText.GetComponent<RectTransform>(), 0, 0, 0.58f, 0, 0, 0, new Vector2(20, 80), new Vector2(-20, 95));

            _activateBtn = UiHelper.CreateButton(panel.transform, "Activate", LocalizationService.Get("personnel.crew_editor.btn_activate"), OnActivate);
            _activateBtnLabel = _activateBtn.GetComponentInChildren<TextMeshProUGUI>();
            Place(_activateBtn.GetComponent<RectTransform>(), 0, 0, 0, 0, 0, 0, new Vector2(20, 20), new Vector2(180, 45));
            var actImg = _activateBtn.GetComponent<Image>();
            if (actImg != null) UITheme.ApplySurface(actImg, UITheme.WithAlpha(UITheme.Success, 0.92f), UIShapePreset.Pill);

            var draftBtn = UiHelper.CreateButton(panel.transform, "Draft", LocalizationService.Get("personnel.crew_editor.btn_draft"), OnBackToDraft);
            Place(draftBtn.GetComponent<RectTransform>(), 0, 0, 0, 0, 0, 0, new Vector2(210, 20), new Vector2(140, 45));

            var archiveBtn = UiHelper.CreateButton(panel.transform, "Archive", LocalizationService.Get("personnel.crew_editor.btn_archive"), OnArchive);
            Place(archiveBtn.GetComponent<RectTransform>(), 0, 0, 0, 0, 0, 0, new Vector2(360, 20), new Vector2(140, 45));

            var deleteBtn = UiHelper.CreateButton(panel.transform, "Delete", LocalizationService.Get("personnel.crew_editor.btn_delete"),
                () => { if (CrewCirculationService.Delete(_currentCirculationId)) Hide(); });
            Place(deleteBtn.GetComponent<RectTransform>(), 0, 0, 0, 0, 0, 0, new Vector2(510, 20), new Vector2(120, 45));
            var delImg = deleteBtn.GetComponent<Image>();
            if (delImg != null) UITheme.ApplySurface(delImg, UITheme.WithAlpha(UITheme.Danger, 0.92f), UIShapePreset.Pill);
        }

        void BuildAddDutyForm(Transform parent)
        {
            _addDutyForm = new GameObject("AddDutyForm");
            _addDutyForm.transform.SetParent(parent, false);
            var rt = _addDutyForm.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.58f, 0); rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(10, 100); rt.offsetMax = new Vector2(-20, -100);
            var formImg = _addDutyForm.AddComponent<Image>();
            UITheme.ApplySurface(formImg, UITheme.WithAlpha(UITheme.SecondarySurface, 0.3f), UIShapePreset.Panel);

            var title = UiHelper.CreateText(_addDutyForm.transform, "Title", LocalizationService.Get("personnel.crew_editor.form.title"), 15, TextAlignmentOptions.Center);
            title.fontStyle = FontStyles.Bold;
            Place(title.GetComponent<RectTransform>(), 0, 1, 1, 1, 0.5f, 1, new Vector2(0, -10), new Vector2(-10, 28));

            // Kind
            AddFormRow(_addDutyForm.transform, LocalizationService.Get("personnel.crew_editor.form.kind_label"), -50, out _, out var kindHolder);
            _addKindDropdown = UiHelper.CreateDropdown(kindHolder, "KindDd",
                new List<string> {
                    LocalizationService.Get("personnel.crew_editor.form.kind.service"),
                    LocalizationService.Get("personnel.crew_editor.form.kind.break"),
                    LocalizationService.Get("personnel.crew_editor.form.kind.deadhead"),
                    LocalizationService.Get("personnel.crew_editor.form.kind.handover"),
                    LocalizationService.Get("personnel.crew_editor.form.kind.overnight")
                });
            Place(_addKindDropdown.GetComponent<RectTransform>(), 0, 0, 1, 1, 0, 0, Vector2.zero, Vector2.zero);

            AddFormRow(_addDutyForm.transform, LocalizationService.Get("personnel.crew_editor.form.day_label"), -90, out _, out var dayHolder);
            _addDayOffsetInput = UiHelper.CreateInputField(dayHolder, "DayIn", "0");
            _addDayOffsetInput.contentType = TMP_InputField.ContentType.IntegerNumber;
            Place(_addDayOffsetInput.GetComponent<RectTransform>(), 0, 0, 1, 1, 0, 0, Vector2.zero, Vector2.zero);

            AddFormRow(_addDutyForm.transform, LocalizationService.Get("personnel.crew_editor.form.start_time_label"), -130, out _, out var startHolder);
            _addStartTimeInput = UiHelper.CreateInputField(startHolder, "StartIn", "06:00:00");
            Place(_addStartTimeInput.GetComponent<RectTransform>(), 0, 0, 1, 1, 0, 0, Vector2.zero, Vector2.zero);

            AddFormRow(_addDutyForm.transform, LocalizationService.Get("personnel.crew_editor.form.end_time_label"), -170, out _, out var endHolder);
            _addEndTimeInput = UiHelper.CreateInputField(endHolder, "EndIn", "10:00:00");
            Place(_addEndTimeInput.GetComponent<RectTransform>(), 0, 0, 1, 1, 0, 0, Vector2.zero, Vector2.zero);

            AddFormRow(_addDutyForm.transform, LocalizationService.Get("personnel.crew_editor.form.start_station_label"), -210, out _, out var ssHolder);
            _addStartStationInput = UiHelper.CreateInputField(ssHolder, "SsIn", LocalizationService.Get("personnel.crew_editor.form.start_station_placeholder"));
            Place(_addStartStationInput.GetComponent<RectTransform>(), 0, 0, 1, 1, 0, 0, Vector2.zero, Vector2.zero);

            AddFormRow(_addDutyForm.transform, LocalizationService.Get("personnel.crew_editor.form.end_station_label"), -250, out _, out var esHolder);
            _addEndStationInput = UiHelper.CreateInputField(esHolder, "EsIn", LocalizationService.Get("personnel.crew_editor.form.end_station_placeholder"));
            Place(_addEndStationInput.GetComponent<RectTransform>(), 0, 0, 1, 1, 0, 0, Vector2.zero, Vector2.zero);

            AddFormRow(_addDutyForm.transform, LocalizationService.Get("personnel.crew_editor.form.trainrun_label"), -290, out _, out var trHolder);
            _addTrainRunIdInput = UiHelper.CreateInputField(trHolder, "TrIn", LocalizationService.Get("personnel.crew_editor.form.trainrun_placeholder"));
            _addTrainRunIdInput.contentType = TMP_InputField.ContentType.IntegerNumber;
            Place(_addTrainRunIdInput.GetComponent<RectTransform>(), 0, 0, 1, 1, 0, 0, Vector2.zero, Vector2.zero);

            var info = UiHelper.CreateText(_addDutyForm.transform, "Info",
                LocalizationService.Get("personnel.crew_editor.form.info"),
                10, TextAlignmentOptions.Center);
            info.color = UITheme.SecondaryText;
            Place(info.GetComponent<RectTransform>(), 0, 1, 1, 1, 0.5f, 1, new Vector2(0, -325), new Vector2(-20, 40));

            var addBtn = UiHelper.CreateButton(_addDutyForm.transform, "AddBtn", LocalizationService.Get("personnel.crew_editor.form.add_btn"), OnAddDutyClicked);
            Place(addBtn.GetComponent<RectTransform>(), 0.5f, 0, 0.5f, 0, 0.5f, 0, new Vector2(0, 10), new Vector2(220, 40));
        }

        void AddFormRow(Transform parent, string labelText, float yOffset, out TextMeshProUGUI label, out Transform inputHolder)
        {
            var lbl = UiHelper.CreateText(parent, labelText, labelText, 12, TextAlignmentOptions.MidlineLeft);
            Place(lbl.GetComponent<RectTransform>(), 0, 1, 0, 1, 0, 1, new Vector2(15, yOffset), new Vector2(130, 30));
            label = lbl;

            var holder = new GameObject($"Holder_{labelText}");
            holder.transform.SetParent(parent, false);
            var hrt = holder.AddComponent<RectTransform>();
            Place(hrt, 0, 1, 1, 1, 0, 1, new Vector2(150, yOffset), new Vector2(-15, 30));
            inputHolder = holder.transform;
        }

        // ═══ Refresh ═══

        void Refresh()
        {
            var c = CrewCirculationService.GetById(_currentCirculationId);
            if (c == null) { Hide(); return; }

            _nameInput.SetTextWithoutNotify(c.name);
            _durationDropdown.SetValueWithoutNotify(Mathf.Clamp(c.durationDays - 1, 0, 2));

            // Status label
            string statusColor = c.status switch
            {
                CirculationStatus.Active => "#4ADE80",
                CirculationStatus.Draft => "#9CA3AF",
                CirculationStatus.Paused => "#FBBF24",
                CirculationStatus.Archived => "#6B7280",
                _ => "#FFFFFF"
            };
            _statusLabel.text = string.Format(LocalizationService.Get("personnel.crew_editor.status_format"),
                statusColor, c.status, RoleDefinitions.GetDisplayNamePl(c.role));

            // Employee dropdown — lista pracownikow tej roli (+ "Brak")
            RebuildEmployeeDropdown(c);

            // Duties list
            foreach (var r in _dutyRows) if (r != null) Destroy(r);
            _dutyRows.Clear();

            if (c.duties.Count == 0)
            {
                var empty = UiHelper.CreateText(_dutiesContent, "Empty",
                    LocalizationService.Get("personnel.crew_editor.no_duties"), 12, TextAlignmentOptions.Center);
                empty.color = UITheme.SecondaryText;
                var er = empty.GetComponent<RectTransform>();
                er.sizeDelta = new Vector2(0, 60);
                var emptyBg = empty.gameObject.AddComponent<Image>();
                UITheme.ApplySurface(emptyBg, UITheme.WithAlpha(UITheme.SecondarySurface, 0.72f), UIShapePreset.Inset);
                _dutyRows.Add(empty.gameObject);
            }
            else
            {
                for (int i = 0; i < c.duties.Count; i++)
                    AddDutyRow(c, c.duties[i], i);
            }

            // Validator
            var validation = CrewCirculationValidator.Validate(c);
            var sb = new StringBuilder();
            string summaryColor = validation.IsValid
                ? (validation.Warnings.Count == 0 ? "#4ADE80" : "#FBBF24")
                : "#F87171";
            sb.AppendLine(string.Format(LocalizationService.Get("personnel.crew_editor.validation_summary_format"),
                summaryColor, validation.GetSummary()));
            foreach (var err in validation.Errors)
                sb.AppendLine(string.Format(LocalizationService.Get("personnel.crew_editor.validation_error_format"), err));
            foreach (var warn in validation.Warnings)
                sb.AppendLine(string.Format(LocalizationService.Get("personnel.crew_editor.validation_warning_format"), warn));
            _validationText.text = sb.ToString();

            // Activate button
            _activateBtn.interactable = validation.IsValid && c.status != CirculationStatus.Active;
            _activateBtnLabel.text = LocalizationService.Get(c.status == CirculationStatus.Active
                ? "personnel.crew_editor.btn_active_label"
                : "personnel.crew_editor.btn_activate");
        }

        void RebuildEmployeeDropdown(CrewCirculation c)
        {
            _employeeDropdownIds.Clear();
            var options = new List<string> { LocalizationService.Get("personnel.crew_editor.employee_none") };
            _employeeDropdownIds.Add(-1);

            foreach (var e in PersonnelService.GetByRole(c.role))
            {
                options.Add(string.Format(LocalizationService.Get("personnel.crew_editor.employee_format"),
                    e.employeeId, e.DisplayFullName, e.skill, e.currentShift));
                _employeeDropdownIds.Add(e.employeeId);
            }

            _employeeDropdown.ClearOptions();
            _employeeDropdown.AddOptions(options);

            int selectedIdx = _employeeDropdownIds.IndexOf(c.assignedEmployeeId);
            if (selectedIdx < 0) selectedIdx = 0;
            _employeeDropdown.SetValueWithoutNotify(selectedIdx);
        }

        void AddDutyRow(CrewCirculation c, CrewDuty d, int idx)
        {
            var row = new GameObject($"Duty_{idx}");
            row.transform.SetParent(_dutiesContent, false);
            var rt = row.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0, 60);

            Color rowColor = d.kind switch
            {
                CrewDutyKind.Service => UITheme.WithAlpha(UITheme.Success, 0.18f),
                CrewDutyKind.Break => UITheme.WithAlpha(UITheme.Warning, 0.16f),
                CrewDutyKind.Deadhead => UITheme.WithAlpha(UITheme.PrimaryAccent, 0.16f),
                CrewDutyKind.Handover => UITheme.WithAlpha(new Color(0.6f, 0.45f, 0.8f, 1f), 0.18f),
                CrewDutyKind.Overnight => UITheme.WithAlpha(new Color(0.7f, 0.38f, 0.84f, 1f), 0.2f),
                _ => UITheme.WithAlpha(UITheme.SecondarySurface, 0.82f)
            };
            var rowImg = row.AddComponent<Image>();
            UITheme.ApplySurface(rowImg, rowColor, UIShapePreset.Inset);

            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.childControlHeight = true; hlg.childControlWidth = false;
            hlg.childForceExpandHeight = true;
            hlg.padding = UITheme.Padding(UITheme.Spacing.Sm, UITheme.Spacing.Xs, UITheme.Spacing.Xs, UITheme.Spacing.Xs);
            hlg.spacing = UITheme.Spacing.Xs;

            var sb = new StringBuilder();
            sb.AppendFormat(LocalizationService.Get("personnel.crew_editor.duty_row.main_format"),
                idx, d.kind, d.dayOffset + 1);
            if (!string.IsNullOrEmpty(d.startTimeIso)) sb.Append(d.startTimeIso);
            if (!string.IsNullOrEmpty(d.endTimeIso)) sb.Append($"→{d.endTimeIso}");
            sb.AppendLine();
            if (!string.IsNullOrEmpty(d.startStationName) || !string.IsNullOrEmpty(d.endStationName))
                sb.AppendFormat(LocalizationService.Get("personnel.crew_editor.duty_row.route_format"),
                    d.startStationName, d.endStationName);
            if (d.referencedTrainRunId > 0)
                sb.AppendFormat(LocalizationService.Get("personnel.crew_editor.duty_row.trainrun_format"),
                    d.referencedTrainRunId);
            if (d.kind == CrewDutyKind.Overnight && d.overnightHotel != null)
                sb.AppendFormat(LocalizationService.Get("personnel.crew_editor.duty_row.hotel_format"),
                    d.overnightHotel.tier, d.overnightHotel.cityStationName);
            else if (d.kind == CrewDutyKind.Overnight)
                sb.Append(LocalizationService.Get("personnel.crew_editor.duty_row.no_hotel"));

            var info = UiHelper.CreateText(row.transform, "Info", sb.ToString(), 11, TextAlignmentOptions.MidlineLeft);
            info.richText = true;
            var ile = info.gameObject.AddComponent<LayoutElement>();
            ile.flexibleWidth = 1;

            int localIdx = idx;
            var upBtn = UiHelper.CreateButton(row.transform, "Up", LocalizationService.Get("personnel.crew_editor.duty_row.btn_up"),
                () => CrewCirculationService.MoveDutyUp(_currentCirculationId, localIdx));
            var uble = upBtn.gameObject.AddComponent<LayoutElement>(); uble.preferredWidth = 32;

            var downBtn = UiHelper.CreateButton(row.transform, "Down", LocalizationService.Get("personnel.crew_editor.duty_row.btn_down"),
                () => CrewCirculationService.MoveDutyDown(_currentCirculationId, localIdx));
            var dble = downBtn.gameObject.AddComponent<LayoutElement>(); dble.preferredWidth = 32;

            if (d.kind == CrewDutyKind.Overnight)
            {
                var hotelBtn = UiHelper.CreateButton(row.transform, "Hotel", LocalizationService.Get("personnel.crew_editor.duty_row.btn_hotel"),
                    () => OpenHotelModal(c, d, localIdx));
                var hble = hotelBtn.gameObject.AddComponent<LayoutElement>(); hble.preferredWidth = 80;
            }

            var delBtn = UiHelper.CreateButton(row.transform, "Del", LocalizationService.Get("personnel.crew_editor.duty_row.btn_delete"),
                () => CrewCirculationService.RemoveDuty(_currentCirculationId, localIdx));
            var delLe = delBtn.gameObject.AddComponent<LayoutElement>(); delLe.preferredWidth = 32;
            var delImg = delBtn.GetComponent<Image>();
            if (delImg != null) delImg.color = new Color(0.5f, 0.2f, 0.2f, 1f);

            _dutyRows.Add(row);
        }

        // ═══ Actions ═══

        void OnNameChanged(string newName) => CrewCirculationService.Rename(_currentCirculationId, newName);

        void OnEmployeeChanged(int idx)
        {
            int empId = _employeeDropdownIds[Mathf.Clamp(idx, 0, _employeeDropdownIds.Count - 1)];
            if (empId <= 0) CrewCirculationService.UnassignEmployee(_currentCirculationId);
            else CrewCirculationService.AssignEmployee(_currentCirculationId, empId);
        }

        void OnDurationChanged(int idx)
        {
            CrewCirculationService.SetDurationDays(_currentCirculationId, idx + 1);
        }

        void OnAddDutyClicked()
        {
            var duty = new CrewDuty
            {
                kind = (CrewDutyKind)Mathf.Clamp(_addKindDropdown.value, 0, 4),
                dayOffset = ParseInt(_addDayOffsetInput.text, 0),
                startTimeIso = string.IsNullOrWhiteSpace(_addStartTimeInput.text) ? "06:00:00" : _addStartTimeInput.text,
                endTimeIso = string.IsNullOrWhiteSpace(_addEndTimeInput.text) ? "10:00:00" : _addEndTimeInput.text,
                startStationName = _addStartStationInput.text ?? "",
                endStationName = _addEndStationInput.text ?? "",
                referencedTrainRunId = ParseInt(_addTrainRunIdInput.text, -1),
                referencedCirculationId = -1
            };
            CrewCirculationService.AddDuty(_currentCirculationId, duty);

            // Clear input time/station dla szybkiego kolejnego dodania
            _addStartTimeInput.text = duty.endTimeIso;
            _addStartStationInput.text = duty.endStationName;
            _addEndTimeInput.text = "";
            _addEndStationInput.text = "";
        }

        void OpenHotelModal(CrewCirculation c, CrewDuty d, int dutyIdx)
        {
            var emp = c.assignedEmployeeId > 0 ? PersonnelService.GetById(c.assignedEmployeeId) : null;
            int empId = emp?.employeeId ?? -1;

            string defaultCity = d.overnightHotel?.cityStationName ?? d.startStationName ?? "";
            int defaultNights = d.overnightHotel?.nights ?? 1;

            HotelBookingModal.EnsureExists().Show(
                empId,
                GameState.CurrentDateIso,
                defaultCity,
                defaultNights,
                booking =>
                {
                    if (booking == null) return;
                    d.overnightHotel = booking;
                    CrewCirculationService.NotifyChanged(_currentCirculationId);
                    Refresh();
                });
        }

        void OnActivate() => CrewCirculationService.Activate(_currentCirculationId);
        void OnBackToDraft() => CrewCirculationService.BackToDraft(_currentCirculationId);
        void OnArchive() => CrewCirculationService.Archive(_currentCirculationId);

        // ═══ Helpers ═══

        static int ParseInt(string s, int fallback)
        {
            if (string.IsNullOrWhiteSpace(s)) return fallback;
            return int.TryParse(s.Trim(), out var v) ? v : fallback;
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
