using System;
using TMPro;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using RailwayManager;
using RailwayManager.Core;
using RailwayManager.SharedUI;
using RailwayManager.SharedUI.Localization;

namespace RailwayManager.Personnel
{
    /// <summary>
    /// M8-9: Modal auto-generatora turnusow.
    ///
    /// Flow:
    /// 1. Settings form (min gap, max hours, role, deadhead return, multi-day, prefix)
    /// 2. [Generuj podglad] — wywoluje <see cref="CrewCirculationAutoGenerator.Generate"/>
    /// 3. Preview text — lista generowanych turnusow + ostrzezenia
    /// 4. [Zatwierdz] — commit przez <see cref="CrewCirculationAutoGenerator.Commit"/>
    /// 5. [Anuluj] — zamknij bez zmian
    ///
    /// Pool TrainRun pobierany z aktywnych <c>Circulation.TrainRuns</c> przez
    /// <c>CrewAutoGenInputAdapter.BuildInputsFromTrainRuns</c> (debug-toggle „fake timetable” usunięty 2026-06-19).
    /// </summary>
    public class CrewAutoGeneratorModal : MonoBehaviour
    {
        public static CrewAutoGeneratorModal Instance { get; private set; }

        Canvas _canvas;
        GameObject _root;

        // Settings
        TMP_InputField _minGapInput;
        TMP_InputField _maxHoursInput;
        TMP_Dropdown _roleDropdown;
        Toggle _deadheadToggle;
        Toggle _multiDayToggle;
        TMP_InputField _prefixInput;

        // Preview
        TextMeshProUGUI _previewText;
        Button _commitBtn;
        TextMeshProUGUI _commitBtnLabel;

        CrewAutoGenPreview _currentPreview;
        bool _isVisible;

        private static readonly Color OverlayBg = UITheme.WithAlpha(Color.black, 0.82f);
        private static readonly Color BoxBg = UITheme.WithAlpha(UITheme.OverlayPanelStrong, 0.98f);
        private static readonly Color SectionBg = UITheme.WithAlpha(UITheme.PrimarySurface, 0.88f);
        private static readonly Color InsetBg = UITheme.WithAlpha(UITheme.SecondarySurface, 0.16f);

        public static CrewAutoGeneratorModal EnsureExists()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("CrewAutoGeneratorModal");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<CrewAutoGeneratorModal>();
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

        public void Show()
        {
            _currentPreview = null;
            _root.SetActive(true);
            _isVisible = true;
            UpdatePreviewText();
            UpdateCommitButton();
        }

        public void Hide()
        {
            _root.SetActive(false);
            _isVisible = false;
        }

        // ═══ Build UI ═══

        void BuildUI()
        {
            var canvasGo = new GameObject("AutoGenCanvas");
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 125; // above list UI (105)
            // MUI-10: standard canvas scaler config (ref 1920×1080, match 0.5)
            UITheme.ApplyCanvasScaler(canvasGo.AddComponent<CanvasScaler>());
            canvasGo.AddComponent<GraphicRaycaster>();

            _root = new GameObject("Root");
            _root.transform.SetParent(canvasGo.transform, false);
            var rootRect = _root.AddComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero; rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero; rootRect.offsetMax = Vector2.zero;
            _root.AddComponent<Image>().color = OverlayBg;

            var box = new GameObject("Box");
            box.transform.SetParent(_root.transform, false);
            var boxRt = box.AddComponent<RectTransform>();
            boxRt.anchorMin = new Vector2(0.5f, 0.5f); boxRt.anchorMax = new Vector2(0.5f, 0.5f);
            boxRt.sizeDelta = new Vector2(1000, 720);
            boxRt.anchoredPosition = Vector2.zero;
            UITheme.ApplySurface(box.AddComponent<Image>(), BoxBg, UIShapePreset.PanelLarge);

            // Title
            var title = UiHelper.CreateText(box.transform, "Title",
                LocalizationService.Get("personnel.crew_autogen.title"), 17, TextAlignmentOptions.Center);
            title.fontStyle = FontStyles.Bold;
            Place(title.GetComponent<RectTransform>(), 0, 1, 1, 1, 0.5f, 1, new Vector2(0, -15), new Vector2(-20, 30));

            // Settings panel (left 40%)
            var settingsPanel = new GameObject("SettingsPanel");
            settingsPanel.transform.SetParent(box.transform, false);
            var spRt = settingsPanel.AddComponent<RectTransform>();
            Place(spRt, 0, 0, 0.4f, 1, 0.5f, 0.5f, Vector2.zero, Vector2.zero);
            spRt.offsetMin = new Vector2(15, 90); spRt.offsetMax = new Vector2(-5, -55);
            UITheme.ApplySurface(settingsPanel.AddComponent<Image>(), SectionBg, UIShapePreset.Panel);

            BuildSettings(settingsPanel.transform);

            // Preview panel (right 60%)
            var previewPanel = new GameObject("PreviewPanel");
            previewPanel.transform.SetParent(box.transform, false);
            var ppRt = previewPanel.AddComponent<RectTransform>();
            Place(ppRt, 0.4f, 0, 1, 1, 0.5f, 0.5f, Vector2.zero, Vector2.zero);
            ppRt.offsetMin = new Vector2(10, 90); ppRt.offsetMax = new Vector2(-15, -55);
            UITheme.ApplySurface(previewPanel.AddComponent<Image>(), SectionBg, UIShapePreset.Panel);

            var prevHdr = UiHelper.CreateText(previewPanel.transform, "PrevHdr", LocalizationService.Get("personnel.crew_autogen.preview_header"), 15, TextAlignmentOptions.TopLeft);
            prevHdr.fontStyle = FontStyles.Bold;
            Place(prevHdr.GetComponent<RectTransform>(), 0, 1, 1, 1, 0.5f, 1, new Vector2(0, -5), new Vector2(-10, 25));

            // Preview scroll
            var prevScroll = new GameObject("PrevScroll");
            prevScroll.transform.SetParent(previewPanel.transform, false);
            var pscRt = prevScroll.AddComponent<RectTransform>();
            Place(pscRt, 0, 0, 1, 1, 0.5f, 0.5f, Vector2.zero, Vector2.zero);
            pscRt.offsetMin = new Vector2(5, 5); pscRt.offsetMax = new Vector2(-5, -30);
            UITheme.ApplySurface(prevScroll.AddComponent<Image>(), InsetBg, UIShapePreset.Inset);

            var psr = prevScroll.AddComponent<ScrollRect>();
            psr.horizontal = false; psr.vertical = true;

            var pvp = new GameObject("PVp");
            pvp.transform.SetParent(prevScroll.transform, false);
            var pvprt = pvp.AddComponent<RectTransform>();
            pvprt.anchorMin = Vector2.zero; pvprt.anchorMax = Vector2.one;
            pvprt.offsetMin = Vector2.zero; pvprt.offsetMax = Vector2.zero;
            UITheme.ApplySurface(pvp.AddComponent<Image>(), UITheme.WithAlpha(UITheme.SecondarySurface, 0.08f), UIShapePreset.Inset);
            pvp.AddComponent<Mask>().showMaskGraphic = false;
            psr.viewport = pvprt;

            var prevContent = new GameObject("PrevContent");
            prevContent.transform.SetParent(pvp.transform, false);
            var pcrt = prevContent.AddComponent<RectTransform>();
            pcrt.anchorMin = new Vector2(0, 1); pcrt.anchorMax = new Vector2(1, 1);
            pcrt.pivot = new Vector2(0.5f, 1);
            pcrt.anchoredPosition = Vector2.zero;

            _previewText = UiHelper.CreateText(prevContent.transform, "PrevTxt",
                LocalizationService.Get("personnel.crew_autogen.preview_prompt"),
                11, TextAlignmentOptions.TopLeft);
            _previewText.richText = true;
            _previewText.textWrappingMode = TextWrappingModes.Normal;
            _previewText.overflowMode = TextOverflowModes.Overflow;
            Place(_previewText.GetComponent<RectTransform>(), 0, 1, 1, 1, 0.5f, 1, new Vector2(5, -5), new Vector2(-10, 2000));

            var csf = prevContent.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            psr.content = pcrt;

            // Action buttons
            var genBtn = UiHelper.CreateButton(box.transform, "Gen", LocalizationService.Get("personnel.crew_autogen.btn_generate"), OnGenerateClicked);
            Place(genBtn.GetComponent<RectTransform>(), 0, 0, 0, 0, 0, 0, new Vector2(20, 20), new Vector2(200, 45));
            var genImg = genBtn.GetComponent<Image>();
            if (genImg != null)
                UITheme.ApplySurface(genImg, UITheme.WithAlpha(UITheme.PrimaryAccent, 0.85f), UIShapePreset.Button);

            _commitBtn = UiHelper.CreateButton(box.transform, "Commit", LocalizationService.Get("personnel.crew_autogen.btn_commit"), OnCommitClicked);
            _commitBtnLabel = _commitBtn.GetComponentInChildren<TextMeshProUGUI>();
            Place(_commitBtn.GetComponent<RectTransform>(), 0.5f, 0, 0.5f, 0, 0.5f, 0, new Vector2(0, 20), new Vector2(240, 45));
            var cImg = _commitBtn.GetComponent<Image>();
            if (cImg != null)
                UITheme.ApplySurface(cImg, UITheme.WithAlpha(UITheme.Success, 0.78f), UIShapePreset.Button);
            _commitBtn.interactable = false;

            var cancelBtn = UiHelper.CreateButton(box.transform, "Cancel", LocalizationService.Get("personnel.crew_autogen.btn_cancel"), Hide);
            Place(cancelBtn.GetComponent<RectTransform>(), 1, 0, 1, 0, 1, 0, new Vector2(-20, 20), new Vector2(180, 45));
        }

        void BuildSettings(Transform parent)
        {
            var hdr = UiHelper.CreateText(parent, "Hdr", LocalizationService.Get("personnel.crew_autogen.settings.title"), 14, TextAlignmentOptions.TopLeft);
            hdr.fontStyle = FontStyles.Bold;
            Place(hdr.GetComponent<RectTransform>(), 0, 1, 1, 1, 0, 1, new Vector2(10, -5), new Vector2(-10, 25));

            // Min gap
            AddSettingLabel(parent, LocalizationService.Get("personnel.crew_autogen.settings.min_gap_label"), -40);
            _minGapInput = UiHelper.CreateInputField(parent, "GapIn", "30");
            _minGapInput.contentType = TMP_InputField.ContentType.IntegerNumber;
            _minGapInput.text = "30";
            Place(_minGapInput.GetComponent<RectTransform>(), 0, 1, 1, 1, 0, 1, new Vector2(180, -35), new Vector2(-20, 28));

            // Max hours
            AddSettingLabel(parent, LocalizationService.Get("personnel.crew_autogen.settings.max_hours_label"), -80);
            _maxHoursInput = UiHelper.CreateInputField(parent, "HoursIn", "12");
            _maxHoursInput.contentType = TMP_InputField.ContentType.IntegerNumber;
            _maxHoursInput.text = "12";
            Place(_maxHoursInput.GetComponent<RectTransform>(), 0, 1, 1, 1, 0, 1, new Vector2(180, -75), new Vector2(-20, 28));

            // Role
            AddSettingLabel(parent, LocalizationService.Get("personnel.crew_autogen.settings.role_label"), -120);
            _roleDropdown = UiHelper.CreateDropdown(parent, "RoleDd",
                new List<string> {
                    LocalizationService.Get("personnel.crew_autogen.settings.role_drivers"),
                    LocalizationService.Get("personnel.crew_autogen.settings.role_conductors"),
                    LocalizationService.Get("personnel.crew_autogen.settings.role_both")
                });
            Place(_roleDropdown.GetComponent<RectTransform>(), 0, 1, 1, 1, 0, 1, new Vector2(180, -115), new Vector2(-20, 28));

            // Deadhead toggle
            _deadheadToggle = AddToggle(parent, "DhToggle",
                LocalizationService.Get("personnel.crew_autogen.settings.deadhead_toggle"), -160, true);

            // Multi-day
            _multiDayToggle = AddToggle(parent, "MdToggle",
                LocalizationService.Get("personnel.crew_autogen.settings.multiday_toggle"), -195, false);
            _multiDayToggle.interactable = false; // M8-9 placeholder — wlaczone po M8-11

            // Prefix
            AddSettingLabel(parent, LocalizationService.Get("personnel.crew_autogen.settings.prefix_label"), -275);
            _prefixInput = UiHelper.CreateInputField(parent, "PrefIn", "T");
            _prefixInput.text = "T";
            Place(_prefixInput.GetComponent<RectTransform>(), 0, 1, 1, 1, 0, 1, new Vector2(180, -270), new Vector2(-20, 28));

            // Info
            var info = UiHelper.CreateText(parent, "Info",
                LocalizationService.Get("personnel.crew_autogen.settings.info"),
                10, TextAlignmentOptions.Top);
            info.color = UITheme.SecondaryText;
            Place(info.GetComponent<RectTransform>(), 0, 0, 1, 0, 0.5f, 0, new Vector2(0, 10), new Vector2(-20, 80));
        }

        Toggle AddToggle(Transform parent, string name, string label, float yOffset, bool defaultValue)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            Place(rt, 0, 1, 1, 1, 0, 1, new Vector2(10, yOffset), new Vector2(-20, 28));

            var toggle = go.AddComponent<Toggle>();
            toggle.isOn = defaultValue;

            // Checkbox visual (minimal)
            var bgGo = new GameObject("Bg");
            bgGo.transform.SetParent(go.transform, false);
            var brt = bgGo.AddComponent<RectTransform>();
            brt.anchorMin = new Vector2(0, 0.5f); brt.anchorMax = new Vector2(0, 0.5f);
            brt.pivot = new Vector2(0, 0.5f);
            brt.anchoredPosition = new Vector2(0, 0);
            brt.sizeDelta = new Vector2(20, 20);
            var bgImg = bgGo.AddComponent<Image>();
            UITheme.ApplySurface(bgImg, UITheme.SecondarySurface, UIShapePreset.Inset);
            toggle.targetGraphic = bgImg;

            var chkGo = new GameObject("Check");
            chkGo.transform.SetParent(bgGo.transform, false);
            var crt = chkGo.AddComponent<RectTransform>();
            crt.anchorMin = Vector2.zero; crt.anchorMax = Vector2.one;
            crt.offsetMin = new Vector2(3, 3); crt.offsetMax = new Vector2(-3, -3);
            var chkImg = chkGo.AddComponent<Image>();
            chkImg.color = UITheme.Success;
            toggle.graphic = chkImg;

            // Label
            var lbl = UiHelper.CreateText(go.transform, "Lbl", label, 12, TextAlignmentOptions.MidlineLeft);
            Place(lbl.GetComponent<RectTransform>(), 0, 0, 1, 1, 0, 0.5f, new Vector2(28, 0), new Vector2(-30, 24));

            return toggle;
        }

        static void AddSettingLabel(Transform parent, string text, float yOffset)
        {
            var lbl = UiHelper.CreateText(parent, $"Lbl_{text}", text, 12, TextAlignmentOptions.MidlineLeft);
            Place(lbl.GetComponent<RectTransform>(), 0, 1, 0, 1, 0, 1, new Vector2(10, yOffset), new Vector2(170, 28));
        }

        // ═══ Actions ═══

        void OnGenerateClicked()
        {
            var settings = ReadSettingsFromUI();
            var inputs = CrewAutoGenInputAdapter.BuildInputsFromTrainRuns(GameState.CurrentDateIso); // realne TrainRuns z aktywnych obiegów

            if (inputs.Count == 0)
            {
                _previewText.text = LocalizationService.Get("personnel.crew_autogen.preview_no_input");
                _currentPreview = null;
                UpdateCommitButton();
                return;
            }

            _currentPreview = CrewCirculationAutoGenerator.Generate(inputs, settings);
            UpdatePreviewText();
            UpdateCommitButton();
        }

        void OnCommitClicked()
        {
            if (_currentPreview == null) return;
            int committed = CrewCirculationAutoGenerator.Commit(_currentPreview);
            Log.Info($"[CrewAutoGeneratorModal] Committed {committed} turnuses");
            Hide();
        }

        CrewAutoGenSettings ReadSettingsFromUI()
        {
            int gap = ParseInt(_minGapInput.text, 30);
            int hours = ParseInt(_maxHoursInput.text, 12);
            var role = (AutoGenRoleMode)Mathf.Clamp(_roleDropdown.value, 0, 2);
            string prefix = string.IsNullOrWhiteSpace(_prefixInput.text) ? "T" : _prefixInput.text.Trim();

            return new CrewAutoGenSettings
            {
                minGapMinutes = Math.Max(0, gap),
                maxWorkHoursPerDay = Math.Clamp(hours, 1, 24),
                roleMode = role,
                autoReturnDeadhead = _deadheadToggle.isOn,
                allowMultiDay = _multiDayToggle.isOn,
                namePrefix = prefix
            };
        }

        void UpdatePreviewText()
        {
            if (_currentPreview == null)
            {
                _previewText.text = LocalizationService.Get("personnel.crew_autogen.preview_prompt");
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine(string.Format(LocalizationService.Get("personnel.crew_autogen.preview_summary_format"),
                _currentPreview.GeneratedCirculations.Count,
                _currentPreview.TotalDuties,
                _currentPreview.DeadheadsGenerated));
            sb.AppendLine();

            if (_currentPreview.Warnings.Count > 0)
            {
                sb.AppendLine(LocalizationService.Get("personnel.crew_autogen.preview_warnings_label"));
                foreach (var w in _currentPreview.Warnings)
                    sb.AppendLine(string.Format(LocalizationService.Get("personnel.crew_autogen.preview_warning_format"), w));
                sb.AppendLine();
            }

            for (int i = 0; i < _currentPreview.GeneratedCirculations.Count; i++)
            {
                var c = _currentPreview.GeneratedCirculations[i];
                sb.AppendLine(string.Format(LocalizationService.Get("personnel.crew_autogen.preview_circ_format"),
                    i + 1, c.name, RoleDefinitions.GetDisplayNamePl(c.role)));
                for (int j = 0; j < c.duties.Count; j++)
                {
                    var d = c.duties[j];
                    string kindColor = d.kind switch
                    {
                        CrewDutyKind.Service => "#4ADE80",
                        CrewDutyKind.Break => "#9CA3AF",
                        CrewDutyKind.Deadhead => "#60A5FA",
                        CrewDutyKind.Handover => "#A855F7",
                        CrewDutyKind.Overnight => "#EC4899",
                        _ => "#FFFFFF"
                    };
                    sb.AppendFormat(LocalizationService.Get("personnel.crew_autogen.preview_duty_format"),
                        kindColor, d.kind);
                    if (!string.IsNullOrEmpty(d.startTimeIso)) sb.Append(d.startTimeIso);
                    if (!string.IsNullOrEmpty(d.endTimeIso)) sb.Append($"→{d.endTimeIso}");
                    if (!string.IsNullOrEmpty(d.startStationName) || !string.IsNullOrEmpty(d.endStationName))
                        sb.AppendFormat(LocalizationService.Get("personnel.crew_autogen.preview_route_format"),
                            d.startStationName, d.endStationName);
                    if (d.referencedTrainRunId > 0)
                        sb.AppendFormat(LocalizationService.Get("personnel.crew_autogen.preview_trainrun_format"),
                            d.referencedTrainRunId);
                    sb.AppendLine();
                }
                sb.AppendLine();
            }

            _previewText.text = sb.ToString();
        }

        void UpdateCommitButton()
        {
            bool canCommit = _currentPreview != null && _currentPreview.GeneratedCirculations.Count > 0;
            _commitBtn.interactable = canCommit;
            if (_commitBtnLabel != null)
                _commitBtnLabel.text = canCommit
                    ? string.Format(LocalizationService.Get("personnel.crew_autogen.btn_commit_format"),
                        _currentPreview.GeneratedCirculations.Count)
                    : LocalizationService.Get("personnel.crew_autogen.btn_commit_disabled");
        }

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
