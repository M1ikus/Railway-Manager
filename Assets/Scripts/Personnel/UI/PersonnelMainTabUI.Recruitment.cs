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
        // ═══ Recruitment content ═══

        void BuildRecruitmentContent()
        {
            var card = UiHelper.CreatePanel(_recruitmentContent.transform, "RecruitmentCard",
                UITheme.WithAlpha(UITheme.SecondarySurface, 0.34f), UIShapePreset.Panel);
            var cardRt = card.GetComponent<RectTransform>();
            cardRt.anchorMin = new Vector2(0.5f, 0.5f); cardRt.anchorMax = new Vector2(0.5f, 0.5f);
            cardRt.pivot = new Vector2(0.5f, 0.5f);
            cardRt.sizeDelta = new Vector2(520, 220);
            cardRt.anchoredPosition = new Vector2(0, -12);

            var cardLayout = card.AddComponent<VerticalLayoutGroup>();
            cardLayout.padding = UITheme.Padding(UITheme.Spacing.Xl);
            cardLayout.spacing = UITheme.Spacing.Md;
            cardLayout.childAlignment = TextAnchor.MiddleCenter;
            cardLayout.childControlWidth = true;
            cardLayout.childControlHeight = false;
            cardLayout.childForceExpandWidth = true;
            cardLayout.childForceExpandHeight = false;

            var title = UiHelper.CreateText(card.transform, "RecruitmentTitle",
                LocalizationService.Get("personnel.tabs.recruit"), 20, TextAlignmentOptions.Center);
            title.fontStyle = FontStyles.Bold;
            title.color = UITheme.PrimaryText;
            title.gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;

            var info = UiHelper.CreateText(card.transform, "Info",
                LocalizationService.Get("personnel.recruit.info"),
                16, TextAlignmentOptions.Center);
            info.color = UITheme.SecondaryText;
            var ir = info.GetComponent<RectTransform>();
            ir.sizeDelta = new Vector2(0, 78);
            info.gameObject.AddComponent<LayoutElement>().preferredHeight = 78f;

            var btn = UiHelper.CreateButton(card.transform, "OpenRecruit",
                LocalizationService.Get("personnel.recruit.open_btn"), () => RecruitmentUI.EnsureExists().Show());
            var br = btn.GetComponent<RectTransform>();
            br.sizeDelta = new Vector2(300, 46);
        }
    }
}
