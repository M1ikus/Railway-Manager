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
    /// M8-3: Prosty panel rekrutacji — lista kandydatow z <see cref="CandidateMarketService.Candidates"/>
    /// + przycisk "Zatrudnij" + przycisk "Nowe ogloszenie".
    ///
    /// Prototyp standalone (M8-3). W M8-4 zostanie zintegrowany jako sub-tab w PersonnelMainTabUI.
    ///
    /// UI budowany programatycznie (bez prefabow) — zgodnie ze wzorcem
    /// <c>DepotLocationPickerUI</c> / <c>WorkshopsPanelUI</c>.
    ///
    /// Pokazanie: <see cref="Show"/> (wywolane z PersonnelServiceBootstrap ContextMenu).
    /// Ukrycie: ESC lub klik "Zamknij".
    /// </summary>
    public class RecruitmentUI : MonoBehaviour
    {
        public static RecruitmentUI Instance { get; private set; }

        Canvas _canvas;
        GameObject _root;
        RectTransform _candidatesContent;
        TextMeshProUGUI _footerInfoText;
        GameObject _postingModal;
        TMP_Dropdown _postingRoleDropdown;
        TMP_Dropdown _postingSkillDropdown;
        TextMeshProUGUI _postingCostText;

        bool _isVisible;
        readonly List<GameObject> _rowObjects = new();
        readonly List<Button> _hireButtons = new();

        public static RecruitmentUI EnsureExists()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("RecruitmentUI");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<RecruitmentUI>();
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
            CandidateMarketService.OnMarketChanged += RefreshList;
            JobPostingService.OnPostingsChanged += RefreshFooter;
        }

        void OnDisable()
        {
            CandidateMarketService.OnMarketChanged -= RefreshList;
            JobPostingService.OnPostingsChanged -= RefreshFooter;
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

        // ═══ Show / Hide ═══

        public void Show()
        {
            CandidateMarketService.EnsureExists(); // ensure pool loaded
            PersonnelServiceBootstrap.EnsureExists();

            _root.SetActive(true);
            _isVisible = true;
            RefreshList();
            RefreshFooter();
        }

        public void Hide()
        {
            _root.SetActive(false);
            _isVisible = false;
            if (_postingModal != null) _postingModal.SetActive(false);
        }

        // ═══ Build UI ═══

        void BuildUI()
        {
            // Canvas overlay
            var canvasGo = new GameObject("RecruitmentCanvas");
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 100;
            // MUI-10: standard canvas scaler config (ref 1920×1080, match 0.5)
            UITheme.ApplyCanvasScaler(canvasGo.AddComponent<CanvasScaler>());
            canvasGo.AddComponent<GraphicRaycaster>();

            // Root (full-screen with semi-transparent background)
            _root = new GameObject("RecruitmentRoot");
            _root.transform.SetParent(canvasGo.transform, false);
            var rootRect = _root.AddComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;
            var rootImage = _root.AddComponent<Image>();
            rootImage.color = UITheme.WithAlpha(Color.black, 0.72f);

            // Centered panel
            var panel = new GameObject("Panel");
            panel.transform.SetParent(_root.transform, false);
            var panelRect = panel.AddComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(1000, 700);
            panelRect.anchoredPosition = Vector2.zero;
            var panelImage = panel.AddComponent<Image>();
            UITheme.ApplySurface(panelImage, UITheme.WithAlpha(UITheme.OverlayPanelStrong, 0.98f), UIShapePreset.PanelLarge);

            var headerCard = UiHelper.CreatePanel(panel.transform, "HeaderCard",
                UITheme.WithAlpha(UITheme.SecondarySurface, 0.34f), UIShapePreset.Panel);
            var headerCardRt = headerCard.GetComponent<RectTransform>();
            headerCardRt.anchorMin = new Vector2(0, 1); headerCardRt.anchorMax = new Vector2(1, 1);
            headerCardRt.pivot = new Vector2(0.5f, 1f);
            headerCardRt.anchoredPosition = new Vector2(0, -12);
            headerCardRt.sizeDelta = new Vector2(-24, 54);

            // Header
            var header = CreateText(panel.transform, "Header", LocalizationService.Get("personnel.recruit.panel.header"), 22, TextAlignmentOptions.MidlineLeft);
            var hr = header.GetComponent<RectTransform>();
            hr.anchorMin = new Vector2(0, 1); hr.anchorMax = new Vector2(1, 1);
            hr.pivot = new Vector2(0, 1);
            hr.anchoredPosition = new Vector2(28, -20);
            hr.sizeDelta = new Vector2(-220, 36);

            // Close button
            var closeBtn = CreateButton(panel.transform, "CloseBtn", LocalizationService.Get("personnel.recruit.panel.close_btn"), Hide);
            var cbRt = closeBtn.GetComponent<RectTransform>();
            cbRt.anchorMin = new Vector2(1, 1); cbRt.anchorMax = new Vector2(1, 1);
            cbRt.pivot = new Vector2(1, 1);
            cbRt.anchoredPosition = new Vector2(-24, -22);
            cbRt.sizeDelta = new Vector2(132, 32);

            // Scroll view dla kandydatow
            var scrollGo = new GameObject("CandidatesScroll");
            scrollGo.transform.SetParent(panel.transform, false);
            var scrollRect = scrollGo.AddComponent<RectTransform>();
            scrollRect.anchorMin = new Vector2(0, 0); scrollRect.anchorMax = new Vector2(1, 1);
            scrollRect.pivot = new Vector2(0.5f, 0.5f);
            scrollRect.offsetMin = new Vector2(15, 100);  // space dla footer
            scrollRect.offsetMax = new Vector2(-15, -60); // space dla header
            var scrollBg = scrollGo.AddComponent<Image>();
            UITheme.ApplySurface(scrollBg, UITheme.WithAlpha(UITheme.PrimarySurface, 0.32f), UIShapePreset.Panel);

            var sr = scrollGo.AddComponent<ScrollRect>();
            sr.horizontal = false;
            sr.vertical = true;

            // Viewport
            var viewport = new GameObject("Viewport");
            viewport.transform.SetParent(scrollGo.transform, false);
            var viewportRect = viewport.AddComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero; viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = new Vector2(6, 6); viewportRect.offsetMax = new Vector2(-6, -6);
            var viewportImg = viewport.AddComponent<Image>();
            UITheme.ApplySurface(viewportImg, UITheme.WithAlpha(UITheme.TopBarInset, 0.9f), UIShapePreset.Inset);
            viewport.AddComponent<Mask>().showMaskGraphic = true;
            sr.viewport = viewportRect;

            // Content
            var content = new GameObject("Content");
            content.transform.SetParent(viewport.transform, false);
            _candidatesContent = content.AddComponent<RectTransform>();
            _candidatesContent.anchorMin = new Vector2(0, 1);
            _candidatesContent.anchorMax = new Vector2(1, 1);
            _candidatesContent.pivot = new Vector2(0.5f, 1);
            _candidatesContent.anchoredPosition = Vector2.zero;
            _candidatesContent.sizeDelta = new Vector2(0, 0);
            var vlg = content.AddComponent<VerticalLayoutGroup>();
            vlg.childControlHeight = false;
            vlg.childControlWidth = true;
            vlg.childForceExpandWidth = true;
            vlg.padding = UITheme.Padding(UITheme.Spacing.Md);
            vlg.spacing = UITheme.Spacing.Md;
            var csf = content.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            sr.content = _candidatesContent;

            // Footer
            var footer = new GameObject("Footer");
            footer.transform.SetParent(panel.transform, false);
            var footerRect = footer.AddComponent<RectTransform>();
            footerRect.anchorMin = new Vector2(0, 0); footerRect.anchorMax = new Vector2(1, 0);
            footerRect.pivot = new Vector2(0.5f, 0);
            footerRect.sizeDelta = new Vector2(0, 80);
            footerRect.anchoredPosition = Vector2.zero;
            var footerImg = footer.AddComponent<Image>();
            UITheme.ApplySurface(footerImg, UITheme.WithAlpha(UITheme.SecondarySurface, 0.34f), UIShapePreset.Panel);

            var footerInfo = CreateText(footer.transform, "FooterInfo", "", 13, TextAlignmentOptions.MidlineLeft);
            _footerInfoText = footerInfo;
            var fir = footerInfo.GetComponent<RectTransform>();
            fir.anchorMin = new Vector2(0, 0); fir.anchorMax = new Vector2(1, 1);
            fir.offsetMin = new Vector2(20, 10); fir.offsetMax = new Vector2(-220, -10);

            var postingBtn = CreateButton(footer.transform, "PostingBtn", LocalizationService.Get("personnel.recruit.panel.posting_btn"), ShowPostingModal);
            var pbr = postingBtn.GetComponent<RectTransform>();
            pbr.anchorMin = new Vector2(1, 0.5f); pbr.anchorMax = new Vector2(1, 0.5f);
            pbr.pivot = new Vector2(1, 0.5f);
            pbr.anchoredPosition = new Vector2(-15, 0);
            pbr.sizeDelta = new Vector2(190, 40);

            // Posting modal (hidden)
            BuildPostingModal(canvasGo.transform);
        }

        // ═══ Posting modal ═══

        void BuildPostingModal(Transform parent)
        {
            _postingModal = new GameObject("PostingModal");
            _postingModal.transform.SetParent(parent, false);
            var modalRect = _postingModal.AddComponent<RectTransform>();
            modalRect.anchorMin = Vector2.zero; modalRect.anchorMax = Vector2.one;
            modalRect.offsetMin = Vector2.zero; modalRect.offsetMax = Vector2.zero;
            _postingModal.AddComponent<Image>().color = UITheme.WithAlpha(Color.black, 0.78f);

            var box = new GameObject("ModalBox");
            box.transform.SetParent(_postingModal.transform, false);
            var boxRect = box.AddComponent<RectTransform>();
            boxRect.anchorMin = new Vector2(0.5f, 0.5f); boxRect.anchorMax = new Vector2(0.5f, 0.5f);
            boxRect.sizeDelta = new Vector2(500, 380);
            boxRect.anchoredPosition = Vector2.zero;
            var boxImg = box.AddComponent<Image>();
            UITheme.ApplySurface(boxImg, UITheme.WithAlpha(UITheme.OverlayPanelStrong, 0.98f), UIShapePreset.PanelLarge);

            var title = CreateText(box.transform, "Title", LocalizationService.Get("personnel.recruit.posting_modal.title"), 18, TextAlignmentOptions.Center);
            var tr = title.GetComponent<RectTransform>();
            tr.anchorMin = new Vector2(0, 1); tr.anchorMax = new Vector2(1, 1);
            tr.pivot = new Vector2(0.5f, 1);
            tr.anchoredPosition = new Vector2(0, -15);
            tr.sizeDelta = new Vector2(-20, 30);

            // Role dropdown
            var roleLabel = CreateText(box.transform, "RoleLabel", LocalizationService.Get("personnel.recruit.posting_modal.role_label"), 14, TextAlignmentOptions.MidlineLeft);
            var rlr = roleLabel.GetComponent<RectTransform>();
            rlr.anchorMin = new Vector2(0, 1); rlr.anchorMax = new Vector2(0, 1);
            rlr.pivot = new Vector2(0, 1);
            rlr.anchoredPosition = new Vector2(20, -70);
            rlr.sizeDelta = new Vector2(100, 25);

            _postingRoleDropdown = CreateDropdown(box.transform, "RoleDropdown",
                new List<string> {
                    RoleDefinitions.GetDisplayNamePl(EmployeeRole.Driver),
                    RoleDefinitions.GetDisplayNamePl(EmployeeRole.Conductor),
                    RoleDefinitions.GetDisplayNamePl(EmployeeRole.Mechanic),
                    RoleDefinitions.GetDisplayNamePl(EmployeeRole.Cleaner),
                    RoleDefinitions.GetDisplayNamePl(EmployeeRole.WashBay),
                    RoleDefinitions.GetDisplayNamePl(EmployeeRole.Office),
                    RoleDefinitions.GetDisplayNamePl(EmployeeRole.Research),
                    RoleDefinitions.GetDisplayNamePl(EmployeeRole.TicketClerk),
                    RoleDefinitions.GetDisplayNamePl(EmployeeRole.Dispatcher),
                    RoleDefinitions.GetDisplayNamePl(EmployeeRole.TrafficController)
                });
            var rdr = _postingRoleDropdown.GetComponent<RectTransform>();
            rdr.anchorMin = new Vector2(0, 1); rdr.anchorMax = new Vector2(1, 1);
            rdr.pivot = new Vector2(0, 1);
            rdr.anchoredPosition = new Vector2(130, -65);
            rdr.sizeDelta = new Vector2(-150, 30);

            // Skill dropdown
            var skillLabel = CreateText(box.transform, "SkillLabel", LocalizationService.Get("personnel.recruit.posting_modal.skill_label"), 14, TextAlignmentOptions.MidlineLeft);
            var slr = skillLabel.GetComponent<RectTransform>();
            slr.anchorMin = new Vector2(0, 1); slr.anchorMax = new Vector2(0, 1);
            slr.pivot = new Vector2(0, 1);
            slr.anchoredPosition = new Vector2(20, -115);
            slr.sizeDelta = new Vector2(100, 25);

            string skillFmt = LocalizationService.Get("personnel.recruit.posting_modal.skill_option_format");
            _postingSkillDropdown = CreateDropdown(box.transform, "SkillDropdown",
                new List<string> {
                    string.Format(skillFmt, 1),
                    string.Format(skillFmt, 2),
                    string.Format(skillFmt, 3),
                    string.Format(skillFmt, 4),
                    string.Format(skillFmt, 5)
                });
            var sdr = _postingSkillDropdown.GetComponent<RectTransform>();
            sdr.anchorMin = new Vector2(0, 1); sdr.anchorMax = new Vector2(1, 1);
            sdr.pivot = new Vector2(0, 1);
            sdr.anchoredPosition = new Vector2(130, -110);
            sdr.sizeDelta = new Vector2(-150, 30);
            _postingSkillDropdown.onValueChanged.AddListener(_ => UpdatePostingCost());

            // Cost text
            _postingCostText = CreateText(box.transform, "CostText", "", 14, TextAlignmentOptions.Center);
            var ctr = _postingCostText.GetComponent<RectTransform>();
            ctr.anchorMin = new Vector2(0, 1); ctr.anchorMax = new Vector2(1, 1);
            ctr.pivot = new Vector2(0.5f, 1);
            ctr.anchoredPosition = new Vector2(0, -170);
            ctr.sizeDelta = new Vector2(-20, 30);

            // Info text
            var info = CreateText(box.transform, "Info",
                LocalizationService.Get("personnel.recruit.posting_modal.info"),
                11, TextAlignmentOptions.Center);
            info.color = UITheme.SecondaryText;
            var ir = info.GetComponent<RectTransform>();
            ir.anchorMin = new Vector2(0, 1); ir.anchorMax = new Vector2(1, 1);
            ir.pivot = new Vector2(0.5f, 1);
            ir.anchoredPosition = new Vector2(0, -210);
            ir.sizeDelta = new Vector2(-40, 50);

            // Confirm button
            var confirmBtn = CreateButton(box.transform, "ConfirmBtn", LocalizationService.Get("personnel.recruit.posting_modal.confirm_btn"), ConfirmPosting);
            var cbr = confirmBtn.GetComponent<RectTransform>();
            cbr.anchorMin = new Vector2(0.5f, 0); cbr.anchorMax = new Vector2(0.5f, 0);
            cbr.pivot = new Vector2(1, 0);
            cbr.anchoredPosition = new Vector2(-10, 20);
            cbr.sizeDelta = new Vector2(180, 40);

            // Cancel button
            var cancelBtn = CreateButton(box.transform, "CancelBtn", LocalizationService.Get("personnel.recruit.posting_modal.cancel_btn"), () => _postingModal.SetActive(false));
            var canr = cancelBtn.GetComponent<RectTransform>();
            canr.anchorMin = new Vector2(0.5f, 0); canr.anchorMax = new Vector2(0.5f, 0);
            canr.pivot = new Vector2(0, 0);
            canr.anchoredPosition = new Vector2(10, 20);
            canr.sizeDelta = new Vector2(180, 40);

            _postingModal.SetActive(false);
        }

        void ShowPostingModal()
        {
            if (_postingModal == null) return;
            _postingModal.SetActive(true);
            UpdatePostingCost();
        }

        void UpdatePostingCost()
        {
            if (_postingCostText == null || _postingSkillDropdown == null) return;
            int skillTarget = _postingSkillDropdown.value + 1;
            int cost = JobPostingService.ComputeCost(skillTarget);
            _postingCostText.text = string.Format(LocalizationService.Get("personnel.recruit.posting_modal.cost_format"),
                cost / 100, PersonnelBalanceConstants.JobPostingMaxActive);
        }

        void ConfirmPosting()
        {
            var roles = new[] {
                EmployeeRole.Driver, EmployeeRole.Conductor, EmployeeRole.Mechanic,
                EmployeeRole.Cleaner, EmployeeRole.WashBay, EmployeeRole.Office,
                EmployeeRole.Research, EmployeeRole.TicketClerk,
                EmployeeRole.Dispatcher, EmployeeRole.TrafficController
            };
            var role = roles[Mathf.Clamp(_postingRoleDropdown.value, 0, roles.Length - 1)];
            int skillTarget = _postingSkillDropdown.value + 1;

            var posting = JobPostingService.CreatePosting(role, skillTarget);
            if (posting != null)
            {
                _postingModal.SetActive(false);
            }
        }

        // ═══ Refresh list & footer ═══

        void RefreshList()
        {
            if (_candidatesContent == null) return;

            // Clear old rows
            foreach (var r in _rowObjects) if (r != null) Destroy(r);
            _rowObjects.Clear();
            _hireButtons.Clear();

            if (CandidateMarketService.Candidates.Count == 0)
            {
                var empty = CreateText(_candidatesContent, "Empty",
                    LocalizationService.Get("personnel.recruit.panel.empty"),
                    14, TextAlignmentOptions.Center);
                empty.color = UITheme.SecondaryText;
                var er = empty.GetComponent<RectTransform>();
                er.sizeDelta = new Vector2(0, 50);
                var emptyBg = empty.gameObject.AddComponent<Image>();
                UITheme.ApplySurface(emptyBg, UITheme.WithAlpha(UITheme.SecondarySurface, 0.72f), UIShapePreset.Inset);
                _rowObjects.Add(empty.gameObject);
            }
            else
            {
                foreach (var c in CandidateMarketService.Candidates)
                    AddCandidateRow(c);
            }
        }

        void AddCandidateRow(EmployeeCandidate c)
        {
            var row = new GameObject($"Row_{c.candidateId}");
            row.transform.SetParent(_candidatesContent, false);
            var rowRect = row.AddComponent<RectTransform>();
            rowRect.sizeDelta = new Vector2(0, 80);
            var rowImg = row.AddComponent<Image>();
            UITheme.ApplySurface(rowImg, UITheme.WithAlpha(UITheme.SecondarySurface, 0.82f), UIShapePreset.Inset);
            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.childControlHeight = true; hlg.childControlWidth = false;
            hlg.childForceExpandHeight = true; hlg.childForceExpandWidth = false;
            hlg.padding = UITheme.Padding(UITheme.Spacing.Md, UITheme.Spacing.Sm);
            hlg.spacing = UITheme.Spacing.Sm;

            // Info text (large)
            var info = CreateText(row.transform, "Info", BuildCandidateInfo(c), 12, TextAlignmentOptions.MidlineLeft);
            info.richText = true;
            info.textWrappingMode = TextWrappingModes.Normal;
            info.overflowMode = TextOverflowModes.Truncate;
            var ir = info.GetComponent<RectTransform>();
            var le = info.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = 780;
            le.flexibleWidth = 1;

            // Hire button
            var btn = CreateButton(row.transform, "Hire", LocalizationService.Get("personnel.recruit.panel.hire_btn"), () => OnHireClicked(c));
            var btnLe = btn.gameObject.AddComponent<LayoutElement>();
            btnLe.preferredWidth = 130;
            btnLe.preferredHeight = 60;
            _hireButtons.Add(btn);

            _rowObjects.Add(row);
        }

        string BuildCandidateInfo(EmployeeCandidate c)
        {
            var sb = new StringBuilder();
            string stars = new string('★', c.skill) + new string('☆', 5 - c.skill);
            sb.AppendFormat(LocalizationService.Get("personnel.recruit.panel.candidate_main_format"),
                c.firstName, c.lastName, c.age, RoleDefinitions.GetDisplayNamePl(c.role), stars);
            sb.AppendLine();
            sb.AppendFormat(LocalizationService.Get("personnel.recruit.panel.candidate_salary_format"),
                c.expectedSalaryGroszy / 100);
            if (c.hireBonusGroszy > 0)
                sb.AppendFormat(LocalizationService.Get("personnel.recruit.panel.candidate_bonus_format"),
                    c.hireBonusGroszy / 100);
            sb.AppendLine();
            if (!string.IsNullOrEmpty(c.resumeNotes))
                sb.AppendFormat(LocalizationService.Get("personnel.recruit.panel.candidate_resume_format"),
                    c.resumeNotes);
            return sb.ToString();
        }

        void OnHireClicked(EmployeeCandidate c)
        {
            // Pay hire bonus upfront
            if (c.hireBonusGroszy > 0)
            {
                var econ = RailwayManager.Timetable.Economy.EconomyManager.Instance;
                if (econ != null)
                    econ.AddCost(-1, c.hireBonusGroszy, "Recruitment",
                        $"Hire bonus {c.firstName} {c.lastName}");
                else
                    GameState.Money -= c.hireBonusGroszy / 100;
            }

            // Compute birth date from age
            string birthDate = "1990-01-01";
            try
            {
                birthDate = IsoTime.ParseDate(GameState.CurrentDateIso)
                    .AddYears(-c.age).ToString("yyyy-MM-dd");
            }
            catch { /* fallback */ }

            var terms = new HireTerms
            {
                firstName = c.firstName,
                lastName = c.lastName,
                age = c.age,
                birthDateIso = birthDate,
                role = c.role,
                skill = c.skill,
                negotiatedSalaryGroszy = c.expectedSalaryGroszy,
                hireBonusGroszy = c.hireBonusGroszy,
                initialShift = ShiftType.Morning,
                initialCycle = WorkCyclePattern.Cycle5_2
            };
            var e = PersonnelService.Hire(terms);
            if (e == null)
            {
                // MM-5: cap reached — Hire zwróciło null. Nie usuwamy kandydata z rynku
                // (gracz może spróbować po awansie pokoju). UI feedback przez log warn
                // (PersonnelService.Hire już logował szczegóły).
                Log.Warn($"[RecruitmentUI] Hire failed for {c.firstName} {c.lastName} — " +
                         $"sprawdź cap pokoju ({RoleCaps.FormatHeadcount(c.role)})");
                return;
            }

            // Remove from market
            CandidateMarketService.RemoveCandidate(c.candidateId);

            Log.Info($"[RecruitmentUI] Hired #{e.employeeId} {e.DisplayFullName} " +
                     $"({RoleDefinitions.GetDisplayNamePl(e.role)} {e.skill}*)");
        }

        void RefreshFooter()
        {
            if (_footerInfoText == null) return;

            var sb = new StringBuilder();
            sb.AppendFormat(LocalizationService.Get("personnel.recruit.panel.footer_main_format"),
                CandidateMarketService.Candidates.Count, PersonnelBalanceConstants.CandidateMarketMaxSize,
                JobPostingService.ActivePostings.Count, PersonnelBalanceConstants.JobPostingMaxActive);
            if (JobPostingService.ActivePostings.Count > 0)
            {
                sb.AppendLine();
                foreach (var p in JobPostingService.ActivePostings)
                {
                    sb.AppendFormat(LocalizationService.Get("personnel.recruit.panel.footer_active_format"),
                        p.jobPostingId, RoleDefinitions.GetDisplayNamePl(p.role),
                        p.skillTarget, p.daysRemaining);
                }
            }

            _footerInfoText.text = sb.ToString();
        }

        // ═══ UI primitives ═══

        static TextMeshProUGUI CreateText(Transform parent, string name, string text, int fontSize, TextAlignmentOptions align)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = fontSize;
            t.alignment = align;
            UITheme.ApplyTmpText(t, UIThemeTextRole.Primary);
            t.richText = true;
            return t;
        }

        static Button CreateButton(Transform parent, string name, string label, Action onClick)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            var img = go.AddComponent<Image>();
            var btn = go.AddComponent<Button>();
            btn.onClick.AddListener(() => onClick?.Invoke());
            UITheme.ApplyButtonStyle(btn, img, UIButtonTone.Primary, UIShapePreset.Pill);

            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(go.transform, false);
            var lrt = labelGo.AddComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;
            var lt = labelGo.AddComponent<TextMeshProUGUI>();
            lt.text = label;
            lt.alignment = TextAlignmentOptions.Center;
            lt.fontSize = 13;
            UITheme.ApplyTmpText(lt, UIThemeTextRole.Inverse);
            return btn;
        }

        static TMP_Dropdown CreateDropdown(Transform parent, string name, List<string> options)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            var img = go.AddComponent<Image>();
            UITheme.ApplySurface(img, UITheme.TopBarInset, UIShapePreset.Inset);
            var dd = go.AddComponent<TMP_Dropdown>();
            dd.ClearOptions();
            dd.AddOptions(options);

            // Default template dropdown requires some setup; for runtime-created dropdown to look acceptable
            // we leave default Unity dropdown template behavior (may need prefab in production UI).
            // Post-EA: dorzucić proper styled template (caption + arrow + viewport + item).

            return dd;
        }
    }
}
