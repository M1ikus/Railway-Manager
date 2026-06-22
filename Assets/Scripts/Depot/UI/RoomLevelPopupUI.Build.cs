using TMPro;
using UnityEngine;
using UnityEngine.UI;
using RailwayManager.SharedUI;

namespace DepotSystem
{
    /// <summary>
    /// Partial: procedural BuildUI (Canvas overlay + backdrop + DialogCard z 5 sekcjami:
    /// Hero / Bonus / Requirements / Summary / BottomRow z buttonami) + helpers
    /// (CreateTextObj / CreateSectionCard / CreateButton).
    /// </summary>
    public partial class RoomLevelPopupUI
    {
        private void BuildUI()
        {
            var canvas = DepotUIManager.Instance != null ? DepotUIManager.Instance.canvas : null;
            if (canvas == null)
                return;

            popupPanel = new GameObject("RoomLevelPopup");
            popupPanel.transform.SetParent(canvas.transform, false);
            var overlayRT = popupPanel.AddComponent<RectTransform>();
            overlayRT.anchorMin = Vector2.zero;
            overlayRT.anchorMax = Vector2.one;
            overlayRT.offsetMin = Vector2.zero;
            overlayRT.offsetMax = Vector2.zero;

            var backdrop = new GameObject("Backdrop");
            backdrop.transform.SetParent(popupPanel.transform, false);
            var backdropRT = backdrop.AddComponent<RectTransform>();
            backdropRT.anchorMin = Vector2.zero;
            backdropRT.anchorMax = Vector2.one;
            backdropRT.offsetMin = Vector2.zero;
            backdropRT.offsetMax = Vector2.zero;
            backdrop.AddComponent<Image>().color = UITheme.WithAlpha(Color.black, 0.44f);
            var backdropButton = backdrop.AddComponent<Button>();
            backdropButton.transition = Selectable.Transition.None;
            backdropButton.onClick.AddListener(Close);

            var dialogCard = new GameObject("DialogCard");
            dialogCard.transform.SetParent(popupPanel.transform, false);
            var dialogRT = dialogCard.AddComponent<RectTransform>();
            dialogRT.anchorMin = new Vector2(0.5f, 0.5f);
            dialogRT.anchorMax = new Vector2(0.5f, 0.5f);
            dialogRT.pivot = new Vector2(0.5f, 0.5f);
            dialogRT.sizeDelta = new Vector2(520f, 612f);

            var dialogImg = dialogCard.AddComponent<Image>();
            UITheme.ApplySurface(dialogImg, panelColor, UIShapePreset.PanelLarge);

            var rootLayout = dialogCard.AddComponent<VerticalLayoutGroup>();
            rootLayout.padding = UITheme.Padding(UITheme.Spacing.Lg);
            rootLayout.spacing = UITheme.Spacing.Md;
            rootLayout.childForceExpandWidth = true;
            rootLayout.childForceExpandHeight = false;

            var heroCard = CreateSectionCard(
                dialogCard.transform,
                "HeroCard",
                headerColor,
                UIShapePreset.Panel,
                UITheme.Padding(UITheme.Spacing.Lg),
                UITheme.Spacing.Sm);
            heroCard.AddComponent<LayoutElement>().preferredHeight = 102f;

            var eyebrowObj = CreateTextObj(heroCard.transform, "Eyebrow", "POZIOM POMIESZCZENIA", 10, TextAlignmentOptions.MidlineLeft);
            var eyebrowText = eyebrowObj.GetComponent<TextMeshProUGUI>();
            eyebrowText.fontStyle = FontStyles.Bold;
            eyebrowText.color = confirmColor;
            eyebrowObj.AddComponent<LayoutElement>().preferredHeight = 16f;

            var heroRow = new GameObject("HeroRow");
            heroRow.transform.SetParent(heroCard.transform, false);
            heroRow.AddComponent<RectTransform>();
            var heroRowLayout = heroRow.AddComponent<HorizontalLayoutGroup>();
            heroRowLayout.spacing = UITheme.Spacing.Md;
            heroRowLayout.childForceExpandWidth = true;
            heroRowLayout.childForceExpandHeight = true;
            heroRowLayout.childAlignment = TextAnchor.MiddleCenter;

            var titleObj = CreateTextObj(heroRow.transform, "Title", "POMIESZCZENIE", 22, TextAlignmentOptions.MidlineLeft);
            titleText = titleObj.GetComponent<TextMeshProUGUI>();
            titleText.fontStyle = FontStyles.Bold;
            titleObj.AddComponent<LayoutElement>().flexibleWidth = 1f;

            var levelBadge = new GameObject("LevelBadge");
            levelBadge.transform.SetParent(heroRow.transform, false);
            levelBadge.AddComponent<RectTransform>();
            var levelBadgeImg = levelBadge.AddComponent<Image>();
            UITheme.ApplySurface(levelBadgeImg, UITheme.WithAlpha(UITheme.PrimaryAccent, 0.22f), UIShapePreset.Pill);
            var levelBadgeLE = levelBadge.AddComponent<LayoutElement>();
            levelBadgeLE.preferredWidth = 116f;
            levelBadgeLE.preferredHeight = 32f;

            var levelObj = CreateTextObj(levelBadge.transform, "Level", "Lvl 1/5", 16, TextAlignmentOptions.Center);
            levelText = levelObj.GetComponent<TextMeshProUGUI>();
            levelText.fontStyle = FontStyles.Bold;
            levelText.color = confirmColor;
            var levelRT = levelObj.GetComponent<RectTransform>();
            levelRT.anchorMin = Vector2.zero;
            levelRT.anchorMax = Vector2.one;
            levelRT.offsetMin = Vector2.zero;
            levelRT.offsetMax = Vector2.zero;

            var subtitleObj = CreateTextObj(
                heroCard.transform,
                "Subtitle",
                "Sprawdz bonusy, wymagania i gotowosc pomieszczenia do kolejnego poziomu.",
                11,
                TextAlignmentOptions.TopLeft);
            var subtitleText = subtitleObj.GetComponent<TextMeshProUGUI>();
            subtitleText.color = UITheme.SecondaryText;
            subtitleText.textWrappingMode = TextWrappingModes.Normal;
            subtitleObj.AddComponent<LayoutElement>().preferredHeight = 30f;

            var bonusCard = CreateSectionCard(
                dialogCard.transform,
                "BonusCard",
                UITheme.WithAlpha(UITheme.SecondarySurface, 0.35f),
                UIShapePreset.Panel,
                UITheme.Padding(UITheme.Spacing.Lg, UITheme.Spacing.Md),
                UITheme.Spacing.Sm);
            bonusCard.AddComponent<LayoutElement>().preferredHeight = 88f;

            var bonusHeader = CreateTextObj(bonusCard.transform, "BonusHeader", "AKTUALNY BONUS", 10, TextAlignmentOptions.MidlineLeft);
            var bonusHeaderText = bonusHeader.GetComponent<TextMeshProUGUI>();
            bonusHeaderText.fontStyle = FontStyles.Bold;
            bonusHeaderText.color = confirmColor;
            bonusHeader.AddComponent<LayoutElement>().preferredHeight = 16f;

            var bonusObj = CreateTextObj(bonusCard.transform, "BonusText", "Bonus", 13, TextAlignmentOptions.TopLeft);
            currentBonusText = bonusObj.GetComponent<TextMeshProUGUI>();
            currentBonusText.color = UITheme.PrimaryText;
            currentBonusText.textWrappingMode = TextWrappingModes.Normal;

            var reqCard = CreateSectionCard(
                dialogCard.transform,
                "RequirementsCard",
                UITheme.WithAlpha(UITheme.SecondarySurface, 0.35f),
                UIShapePreset.Panel,
                UITheme.Padding(UITheme.Spacing.Lg),
                UITheme.Spacing.Sm);
            var reqLE = reqCard.AddComponent<LayoutElement>();
            reqLE.flexibleHeight = 1f;
            reqLE.preferredHeight = 286f;

            var reqHeaderObj = CreateTextObj(reqCard.transform, "ReqHeader", "Checklista nastepnego poziomu", 13, TextAlignmentOptions.MidlineLeft);
            nextLevelHeaderText = reqHeaderObj.GetComponent<TextMeshProUGUI>();
            nextLevelHeaderText.fontStyle = FontStyles.Bold;
            reqHeaderObj.AddComponent<LayoutElement>().preferredHeight = 22f;

            var reqHintObj = CreateTextObj(
                reqCard.transform,
                "ReqHint",
                "Kazdy warunek ponizej pokazuje, co jest juz gotowe i czego nadal brakuje.",
                11,
                TextAlignmentOptions.TopLeft);
            var reqHintText = reqHintObj.GetComponent<TextMeshProUGUI>();
            reqHintText.color = UITheme.SecondaryText;
            reqHintText.textWrappingMode = TextWrappingModes.Normal;
            reqHintObj.AddComponent<LayoutElement>().preferredHeight = 30f;

            var scrollObj = new GameObject("ChecklistScroll");
            scrollObj.transform.SetParent(reqCard.transform, false);
            scrollObj.AddComponent<RectTransform>();
            var scrollLE = scrollObj.AddComponent<LayoutElement>();
            scrollLE.flexibleHeight = 1f;
            scrollLE.preferredHeight = 206f;

            var scrollRect = scrollObj.AddComponent<ScrollRect>();
            scrollRect.vertical = true;
            scrollRect.horizontal = false;
            var scrollImg = scrollObj.AddComponent<Image>();
            UITheme.ApplySurface(scrollImg, UITheme.TopBarInset, UIShapePreset.Inset);
            var mask = scrollObj.AddComponent<Mask>();
            mask.showMaskGraphic = true;

            var contentObj = new GameObject("Content");
            contentObj.transform.SetParent(scrollObj.transform, false);
            var contentRT = contentObj.AddComponent<RectTransform>();
            contentRT.anchorMin = new Vector2(0f, 1f);
            contentRT.anchorMax = new Vector2(1f, 1f);
            contentRT.pivot = new Vector2(0.5f, 1f);
            contentRT.sizeDelta = Vector2.zero;
            var contentLayout = contentObj.AddComponent<VerticalLayoutGroup>();
            contentLayout.padding = UITheme.Padding(UITheme.Spacing.Sm);
            contentLayout.spacing = UITheme.Spacing.Sm;
            contentLayout.childForceExpandWidth = true;
            contentLayout.childForceExpandHeight = false;
            var contentFitter = contentObj.AddComponent<ContentSizeFitter>();
            contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scrollRect.viewport = scrollObj.GetComponent<RectTransform>();
            scrollRect.content = contentRT;
            checklistContainer = contentObj.transform;

            var summaryCard = CreateSectionCard(
                dialogCard.transform,
                "SummaryCard",
                UITheme.WithAlpha(UITheme.TopBarInset, 0.88f),
                UIShapePreset.Panel,
                UITheme.Padding(UITheme.Spacing.Lg, UITheme.Spacing.Md),
                UITheme.Spacing.Sm);
            summaryCard.AddComponent<LayoutElement>().preferredHeight = 80f;

            var readinessObj = CreateTextObj(
                summaryCard.transform,
                "Readiness",
                "Brakuje jeszcze kilku warunkow do kolejnego poziomu.",
                12,
                TextAlignmentOptions.TopLeft);
            readinessText = readinessObj.GetComponent<TextMeshProUGUI>();
            readinessText.fontStyle = FontStyles.Bold;
            readinessText.textWrappingMode = TextWrappingModes.Normal;

            var costObj = CreateTextObj(summaryCard.transform, "Cost", "Koszt awansu: --", 13, TextAlignmentOptions.MidlineLeft);
            costText = costObj.GetComponent<TextMeshProUGUI>();
            costText.color = UITheme.SecondaryText;
            costText.textWrappingMode = TextWrappingModes.Normal;

            var bottomRow = new GameObject("BottomRow");
            bottomRow.transform.SetParent(dialogCard.transform, false);
            bottomRow.AddComponent<RectTransform>();
            var bottomImg = bottomRow.AddComponent<Image>();
            UITheme.ApplySurface(bottomImg, UITheme.WithAlpha(UITheme.SecondarySurface, 0.35f), UIShapePreset.Panel);
            bottomRow.AddComponent<LayoutElement>().preferredHeight = 58f;

            var bottomLayout = bottomRow.AddComponent<HorizontalLayoutGroup>();
            bottomLayout.padding = UITheme.Padding(UITheme.Spacing.Md);
            bottomLayout.spacing = UITheme.Spacing.Sm;
            bottomLayout.childForceExpandWidth = true;
            bottomLayout.childForceExpandHeight = true;

            CreateButton(bottomRow.transform, "Wroc", buttonColor, Close, out _);
            upgradeButton = CreateButton(bottomRow.transform, "AWANSUJ", confirmColor, OnUpgradeClicked, out upgradeButtonLabel);
            upgradeButton.interactable = false;

            popupPanel.SetActive(false);
        }

        private GameObject CreateTextObj(Transform parent, string name, string text, int fontSize, TextAlignmentOptions alignment)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            obj.AddComponent<RectTransform>();

            var txt = obj.AddComponent<TextMeshProUGUI>();
            UITheme.ApplyTmpText(txt, UIThemeTextRole.Primary);
            txt.text = text;
            txt.fontSize = fontSize;
            txt.alignment = alignment;
            txt.textWrappingMode = TextWrappingModes.Normal;
            txt.overflowMode = TextOverflowModes.Overflow;
            txt.raycastTarget = false;
            return obj;
        }

        private GameObject CreateSectionCard(
            Transform parent,
            string name,
            Color color,
            UIShapePreset shape,
            RectOffset padding,
            float spacing)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            obj.AddComponent<RectTransform>();
            var img = obj.AddComponent<Image>();
            UITheme.ApplySurface(img, color, shape);

            var layout = obj.AddComponent<VerticalLayoutGroup>();
            layout.padding = padding;
            layout.spacing = spacing;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            return obj;
        }

        private Button CreateButton(Transform parent, string label, Color bgColor, System.Action onClick, out TextMeshProUGUI labelTextOut)
        {
            var btnObj = new GameObject($"Btn_{label}");
            btnObj.transform.SetParent(parent, false);
            btnObj.AddComponent<RectTransform>();
            var img = btnObj.AddComponent<Image>();
            UITheme.ApplySurface(img, bgColor, bgColor == confirmColor ? UIShapePreset.Pill : UIShapePreset.Button);

            var btn = btnObj.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.colors = UITheme.CreateColorBlock(
                bgColor,
                bgColor == confirmColor ? UITheme.PrimaryAccentHover : buttonHoverColor,
                bgColor == confirmColor ? UITheme.Darken(UITheme.PrimaryAccentHover, 0.18f) : UITheme.Border,
                bgColor,
                lockedButtonColor);
            btn.onClick.AddListener(() => onClick?.Invoke());

            var le = btnObj.AddComponent<LayoutElement>();
            le.preferredHeight = 38f;

            var txt = CreateTextObj(btnObj.transform, "Label", label, 14, TextAlignmentOptions.Center);
            labelTextOut = txt.GetComponent<TextMeshProUGUI>();
            labelTextOut.color = bgColor == confirmColor ? UITheme.InverseText : UITheme.PrimaryText;
            labelTextOut.fontStyle = FontStyles.Bold;

            var txtRT = txt.GetComponent<RectTransform>();
            txtRT.anchorMin = Vector2.zero;
            txtRT.anchorMax = Vector2.one;
            txtRT.offsetMin = Vector2.zero;
            txtRT.offsetMax = Vector2.zero;

            return btn;
        }
    }
}
