using UnityEngine;
using TMPro;
using UnityEngine.UI;
using RailwayManager.Core;
using RailwayManager.SharedUI;
using RailwayManager.SharedUI.Localization;

namespace RailwayManager.Personnel
{
    public partial class PersonnelMainTabUI
    {
        // ═══ Turnusy content (M8-8) ═══

        TextMeshProUGUI _turnusesSummaryText;

        void BuildTurnusesContent()
        {
            var parent = _turnusesContent.transform;

            var summaryCard = UiHelper.CreatePanel(parent, "TurnusesSummaryCard",
                UITheme.WithAlpha(UITheme.SecondarySurface, 0.34f), UIShapePreset.Panel);
            var summaryRt = summaryCard.GetComponent<RectTransform>();
            summaryRt.anchorMin = new Vector2(0.5f, 0.5f); summaryRt.anchorMax = new Vector2(0.5f, 0.5f);
            summaryRt.pivot = new Vector2(0.5f, 0.5f);
            summaryRt.sizeDelta = new Vector2(640, 420);
            summaryRt.anchoredPosition = new Vector2(0, 24);

            _turnusesSummaryText = UiHelper.CreateText(summaryCard.transform, "TurnSummary",
                "", 14, TextAlignmentOptions.TopLeft);
            _turnusesSummaryText.richText = true;
            var sr = _turnusesSummaryText.GetComponent<RectTransform>();
            sr.anchorMin = new Vector2(0, 0); sr.anchorMax = new Vector2(1, 1);
            sr.offsetMin = new Vector2(24, 96); sr.offsetMax = new Vector2(-24, -24);

            var footerCard = UiHelper.CreatePanel(parent, "TurnusesFooterCard",
                UITheme.WithAlpha(UITheme.SecondarySurface, 0.3f), UIShapePreset.Panel);
            var footerRt = footerCard.GetComponent<RectTransform>();
            footerRt.anchorMin = new Vector2(0.5f, 0.5f); footerRt.anchorMax = new Vector2(0.5f, 0.5f);
            footerRt.pivot = new Vector2(0.5f, 0.5f);
            footerRt.sizeDelta = new Vector2(640, 86);
            footerRt.anchoredPosition = new Vector2(0, -238);

            var hint = UiHelper.CreateText(footerCard.transform, "TurnusesHint",
                LocalizationService.Get("personnel.turnuses.footer"), 12, TextAlignmentOptions.MidlineLeft);
            hint.color = UITheme.SecondaryText;
            var hintRt = hint.GetComponent<RectTransform>();
            hintRt.anchorMin = new Vector2(0, 0); hintRt.anchorMax = new Vector2(1, 1);
            hintRt.offsetMin = new Vector2(18, 14); hintRt.offsetMax = new Vector2(-220, -14);

            var btn = UiHelper.CreateButton(footerCard.transform, "OpenList",
                LocalizationService.Get("personnel.turnuses.open_btn"), () => CrewCirculationListUI.EnsureExists().Show());
            var br = btn.GetComponent<RectTransform>();
            br.anchorMin = new Vector2(1, 0.5f); br.anchorMax = new Vector2(1, 0.5f);
            br.pivot = new Vector2(1, 0.5f);
            br.sizeDelta = new Vector2(220, 42);
            br.anchoredPosition = new Vector2(-18, 0);
        }

        void RefreshTurnusesSummary()
        {
            if (_turnusesSummaryText == null) return;

            int total = CrewCirculationService.All.Count;
            int active = CrewCirculationService.GetByStatus(RailwayManager.Timetable.CirculationStatus.Active).Count;
            int draft = CrewCirculationService.GetByStatus(RailwayManager.Timetable.CirculationStatus.Draft).Count;
            int drivers = CrewCirculationService.GetByRole(EmployeeRole.Driver).Count;
            int conductors = CrewCirculationService.GetByRole(EmployeeRole.Conductor).Count;

            int unassigned = 0;
            int invalid = 0;
            foreach (var c in CrewCirculationService.All)
            {
                if (c.assignedEmployeeId <= 0) unassigned++;
                var validation = CrewCirculationValidator.Validate(c);
                if (!validation.IsValid) invalid++;
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine(LocalizationService.Get("personnel.turnuses.title"));
            sb.AppendLine();
            sb.AppendLine(string.Format(LocalizationService.Get("personnel.turnuses.total_format"), total, draft, active));
            sb.AppendLine(string.Format(LocalizationService.Get("personnel.turnuses.roles_format"), drivers, conductors));
            sb.AppendLine();
            if (unassigned > 0)
                sb.AppendLine(string.Format(LocalizationService.Get("personnel.turnuses.unassigned_format"), unassigned));
            if (invalid > 0)
                sb.AppendLine(string.Format(LocalizationService.Get("personnel.turnuses.invalid_format"), invalid));
            if (unassigned == 0 && invalid == 0 && total > 0)
                sb.AppendLine(LocalizationService.Get("personnel.turnuses.all_ok"));

            _turnusesSummaryText.text = sb.ToString();
        }
    }
}
