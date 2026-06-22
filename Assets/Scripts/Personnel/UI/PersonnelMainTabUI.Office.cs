using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using RailwayManager.Core;
using RailwayManager.SharedUI;
using RailwayManager.SharedUI.Localization;

namespace RailwayManager.Personnel
{
    public partial class PersonnelMainTabUI
    {
        // ═══ Office + R&D content (M8-15) ═══

        TextMeshProUGUI _officeSummaryText;
        RectTransform _researchListContent;
        readonly List<GameObject> _researchRows = new();

        void BuildOfficeContent()
        {
            var parent = _officeContent.transform;

            var summaryCard = UiHelper.CreatePanel(parent, "OfficeSummaryCard",
                UITheme.WithAlpha(UITheme.SecondarySurface, 0.34f), UIShapePreset.Panel);
            var summaryRt = summaryCard.GetComponent<RectTransform>();
            summaryRt.anchorMin = new Vector2(0, 1); summaryRt.anchorMax = new Vector2(1, 1);
            summaryRt.pivot = new Vector2(0.5f, 1f);
            summaryRt.anchoredPosition = new Vector2(0, -8);
            summaryRt.sizeDelta = new Vector2(-20, 170);

            _officeSummaryText = UiHelper.CreateText(summaryCard.transform, "OfficeSum", "", 13, TextAlignmentOptions.TopLeft);
            _officeSummaryText.richText = true;
            var sr = _officeSummaryText.GetComponent<RectTransform>();
            sr.anchorMin = new Vector2(0, 0); sr.anchorMax = new Vector2(1, 1);
            sr.offsetMin = new Vector2(18, 16); sr.offsetMax = new Vector2(-18, -16);

            var researchCard = UiHelper.CreatePanel(parent, "ResearchListCard",
                UITheme.WithAlpha(UITheme.SecondarySurface, 0.3f), UIShapePreset.Panel);
            var researchRt = researchCard.GetComponent<RectTransform>();
            researchRt.anchorMin = new Vector2(0, 0); researchRt.anchorMax = new Vector2(1, 1);
            researchRt.offsetMin = new Vector2(10, 10); researchRt.offsetMax = new Vector2(-10, -190);

            var rdLabel = UiHelper.CreateText(researchCard.transform, "RdLbl",
                LocalizationService.Get("personnel.office.research_label"), 13, TextAlignmentOptions.TopLeft);
            rdLabel.fontStyle = FontStyles.Bold;
            var rdl = rdLabel.GetComponent<RectTransform>();
            rdl.anchorMin = new Vector2(0, 1); rdl.anchorMax = new Vector2(1, 1);
            rdl.pivot = new Vector2(0, 1);
            rdl.anchoredPosition = new Vector2(16, -14);
            rdl.sizeDelta = new Vector2(-32, 24);

            var scroll = new GameObject("RdScroll");
            scroll.transform.SetParent(researchCard.transform, false);
            var scRt = scroll.AddComponent<RectTransform>();
            scRt.anchorMin = new Vector2(0, 0); scRt.anchorMax = new Vector2(1, 1);
            scRt.offsetMin = new Vector2(14, 14); scRt.offsetMax = new Vector2(-14, -44);
            var scrollImg = scroll.AddComponent<Image>();
            UITheme.ApplySurface(scrollImg, UITheme.WithAlpha(UITheme.PrimarySurface, 0.32f), UIShapePreset.Inset);

            var srComp = scroll.AddComponent<ScrollRect>();
            srComp.horizontal = false; srComp.vertical = true;

            var vp = new GameObject("Viewport");
            vp.transform.SetParent(scroll.transform, false);
            var vprt = vp.AddComponent<RectTransform>();
            vprt.anchorMin = Vector2.zero; vprt.anchorMax = Vector2.one;
            vprt.offsetMin = new Vector2(6, 6); vprt.offsetMax = new Vector2(-6, -6);
            var viewportImg = vp.AddComponent<Image>();
            UITheme.ApplySurface(viewportImg, UITheme.WithAlpha(UITheme.TopBarInset, 0.9f), UIShapePreset.Inset);
            vp.AddComponent<Mask>().showMaskGraphic = true;
            srComp.viewport = vprt;

            var content = new GameObject("Content");
            content.transform.SetParent(vp.transform, false);
            _researchListContent = content.AddComponent<RectTransform>();
            _researchListContent.anchorMin = new Vector2(0, 1); _researchListContent.anchorMax = new Vector2(1, 1);
            _researchListContent.pivot = new Vector2(0.5f, 1);
            _researchListContent.anchoredPosition = Vector2.zero;
            var vlg = content.AddComponent<VerticalLayoutGroup>();
            vlg.childControlHeight = false; vlg.childControlWidth = true;
            vlg.childForceExpandWidth = true;
            vlg.padding = UITheme.Padding(UITheme.Spacing.Sm);
            vlg.spacing = UITheme.Spacing.Sm;
            var csf = content.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            srComp.content = _researchListContent;
        }

        void RefreshOfficeContent()
        {
            if (_officeSummaryText == null) return;

            int clerks = PersonnelService.CountActiveByRole(EmployeeRole.Office);
            int researchers = PersonnelService.CountActiveByRole(EmployeeRole.Research);
            float reduction = OfficeService.GetFixedCostReduction();
            int marketDays = OfficeService.GetAdjustedMarketRefreshDays();

            var sb = new System.Text.StringBuilder();
            sb.AppendLine(LocalizationService.Get("personnel.office.summary_title"));
            sb.AppendLine();
            sb.AppendLine(string.Format(LocalizationService.Get("personnel.office.clerks_format"), clerks));
            sb.AppendLine(string.Format(LocalizationService.Get("personnel.office.reduction_format"),
                (reduction * 100).ToString("F1"),
                (OfficeService.MaxReduction * 100).ToString("F0")));
            sb.AppendLine(string.Format(LocalizationService.Get("personnel.office.market_format"),
                marketDays, PersonnelBalanceConstants.CandidateMarketRefreshDays));
            sb.AppendLine();
            sb.AppendLine(string.Format(LocalizationService.Get("personnel.office.researchers_format"), researchers));

            if (ResearchService.Active != null)
            {
                var a = ResearchService.Active;
                string statusColor = a.status switch
                {
                    ResearchPathStatus.InProgress => "#4ADE80",
                    ResearchPathStatus.Interrupted => "#F87171",
                    _ => "#FFFFFF"
                };
                float progress = 1f - a.daysRemaining / (float)a.durationDays;
                sb.AppendLine(string.Format(LocalizationService.Get("personnel.office.active_research_format"),
                    a.displayName, statusColor, a.status));
                sb.AppendLine(string.Format(LocalizationService.Get("personnel.office.progress_format"),
                    a.durationDays - a.daysRemaining, a.durationDays,
                    (progress * 100).ToString("F0"),
                    a.requiredResearchers, a.minSkill));
            }
            else
            {
                sb.AppendLine(LocalizationService.Get("personnel.office.no_research"));
            }

            _officeSummaryText.text = sb.ToString();

            // Lista ścieżek
            foreach (var r in _researchRows) if (r != null) Destroy(r);
            _researchRows.Clear();

            foreach (var path in ResearchService.AllPaths)
                AddResearchRow(path);

            // Unlocks info
            if (ResearchUnlocks.Global.AllEffects.Count > 0)
            {
                var unlocksRow = new GameObject("UnlocksInfo");
                unlocksRow.transform.SetParent(_researchListContent, false);
                var urt = unlocksRow.AddComponent<RectTransform>();
                urt.sizeDelta = new Vector2(0, 40);
                var unlockImg = unlocksRow.AddComponent<Image>();
                UITheme.ApplySurface(unlockImg, UITheme.WithAlpha(UITheme.Success, 0.16f), UIShapePreset.Inset);

                var usb = new System.Text.StringBuilder();
                usb.Append(LocalizationService.Get("personnel.office.unlocks_label"));
                foreach (var kv in ResearchUnlocks.Global.AllEffects)
                    usb.AppendFormat(LocalizationService.Get("personnel.office.unlock_item_format"), kv.Key, kv.Value);
                var t = UiHelper.CreateText(unlocksRow.transform, "Txt", usb.ToString(), 11, TextAlignmentOptions.MidlineLeft);
                var trt = t.GetComponent<RectTransform>();
                trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
                trt.offsetMin = new Vector2(10, 2); trt.offsetMax = new Vector2(-10, -2);
                _researchRows.Add(unlocksRow);
            }
        }

        void AddResearchRow(ResearchPath p)
        {
            var row = new GameObject($"Path_{p.pathId}");
            row.transform.SetParent(_researchListContent, false);
            var rt = row.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0, 82);

            Color rowColor = p.status switch
            {
                ResearchPathStatus.InProgress => UITheme.WithAlpha(UITheme.Success, 0.18f),
                ResearchPathStatus.Interrupted => UITheme.WithAlpha(UITheme.Warning, 0.2f),
                ResearchPathStatus.Completed => UITheme.WithAlpha(UITheme.PrimaryAccent, 0.18f),
                _ => UITheme.WithAlpha(UITheme.SecondarySurface, 0.82f)
            };
            var rowImg = row.AddComponent<Image>();
            UITheme.ApplySurface(rowImg, rowColor, UIShapePreset.Inset);

            var sb = new System.Text.StringBuilder();
            sb.AppendFormat(LocalizationService.Get("personnel.office.row_main_format"),
                p.displayName, p.status, p.requiredResearchers, p.minSkill, p.durationDays);
            sb.AppendLine();
            sb.Append(p.description);
            sb.AppendLine();
            sb.AppendFormat(LocalizationService.Get("personnel.office.row_effect_format"),
                p.effectKey, p.effectValue);

            var info = UiHelper.CreateText(row.transform, "Info", sb.ToString(), 11, TextAlignmentOptions.TopLeft);
            info.richText = true;
            var irt = info.GetComponent<RectTransform>();
            irt.anchorMin = new Vector2(0, 0); irt.anchorMax = new Vector2(0.8f, 1);
            irt.offsetMin = new Vector2(14, 8); irt.offsetMax = new Vector2(-6, -8);

            if (p.status == ResearchPathStatus.Available)
            {
                bool canStart = ResearchService.Active == null;
                var btn = UiHelper.CreateButton(row.transform, $"Start_{p.pathId}", LocalizationService.Get("personnel.office.btn_start"),
                    () => ResearchService.StartResearch(p.pathId));
                var brt = btn.GetComponent<RectTransform>();
                brt.anchorMin = new Vector2(0.8f, 0.5f); brt.anchorMax = new Vector2(1, 0.5f);
                brt.pivot = new Vector2(0.5f, 0.5f);
                brt.offsetMin = new Vector2(6, -16); brt.offsetMax = new Vector2(-12, 16);
                btn.interactable = canStart;
            }
            else if (p.status == ResearchPathStatus.InProgress || p.status == ResearchPathStatus.Interrupted)
            {
                var btn = UiHelper.CreateButton(row.transform, $"Cancel_{p.pathId}", LocalizationService.Get("personnel.office.btn_cancel"),
                    () => ResearchService.CancelActive());
                var brt = btn.GetComponent<RectTransform>();
                brt.anchorMin = new Vector2(0.8f, 0.5f); brt.anchorMax = new Vector2(1, 0.5f);
                brt.pivot = new Vector2(0.5f, 0.5f);
                brt.offsetMin = new Vector2(6, -16); brt.offsetMax = new Vector2(-12, 16);
                var bImg = btn.GetComponent<Image>();
                if (bImg != null) UITheme.ApplySurface(bImg, UITheme.WithAlpha(UITheme.Danger, 0.92f), UIShapePreset.Button);
            }
            else if (p.status == ResearchPathStatus.Completed)
            {
                var txt = UiHelper.CreateText(row.transform, $"Done_{p.pathId}", LocalizationService.Get("personnel.office.completed_text"),
                    13, TextAlignmentOptions.Center);
                txt.color = UITheme.Success;
                txt.fontStyle = FontStyles.Bold;
                var trt = txt.GetComponent<RectTransform>();
                trt.anchorMin = new Vector2(0.8f, 0.5f); trt.anchorMax = new Vector2(1, 0.5f);
                trt.pivot = new Vector2(0.5f, 0.5f);
                trt.offsetMin = new Vector2(6, -16); trt.offsetMax = new Vector2(-12, 16);
            }

            _researchRows.Add(row);
        }
    }
}
