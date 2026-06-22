using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using RailwayManager.SharedUI;
using RailwayManager.SharedUI.Localization;

namespace MainMenu
{
    /// <summary>
    /// SettingsScreenUI — partial: dolny pasek akcji (Resetuj sekcję / Resetuj wszystko / Anuluj / Zastosuj).
    /// </summary>
    public partial class SettingsScreenUI
    {
        private void BuildBottomBar()
        {
            var bar = NewGO("BottomBar", _root.transform);
            UITheme.ApplySurface(bar.AddComponent<Image>(), BottomBarBg, UIShapePreset.PanelLarge);
            var barRT = bar.GetComponent<RectTransform>();
            barRT.anchorMin = new Vector2(0f, 0f);
            barRT.anchorMax = new Vector2(1f, 0f);
            barRT.pivot     = new Vector2(0.5f, 0f);
            barRT.anchoredPosition = Vector2.zero;
            barRT.sizeDelta = new Vector2(0f, BottomBarHeight);

            var hl = bar.AddComponent<HorizontalLayoutGroup>();
            hl.padding  = UITheme.Padding(UITheme.Spacing.Xl, UITheme.Spacing.Md);
            hl.spacing  = UITheme.Spacing.Md;
            hl.childAlignment      = TextAnchor.MiddleRight;
            // 2026-05-17: childControlWidth false→true (childControlHeight też). Bez tego
            // HLG nie szanował LayoutElement.preferredWidth=180px na buttonach → wszystkie
            // miały default sizeDelta.x=100, tekst "Resetuj sekcję"/"Resetuj wszystko" nie
            // mieścił się, buttons nakładały na siebie.
            hl.childControlWidth   = true;
            hl.childControlHeight  = true;
            hl.childForceExpandWidth  = false;
            hl.childForceExpandHeight = false;

            // Layout: reset/reset-all po lewej (przed spacer'em), cancel/apply po prawej
            _resetSectionLbl = AddBottomBarButton(bar.transform, "ResetSection", LocalizationService.Get("settings.bottom.reset_section"), BtnDanger, true, OnResetSectionClicked);
            _resetAllLbl     = AddBottomBarButton(bar.transform, "ResetAll",     LocalizationService.Get("settings.bottom.reset_all"),     BtnDanger, true, OnResetAllClicked);

            // Spacer
            var spacer = NewGO("Spacer", bar.transform);
            var spacerLE = spacer.AddComponent<LayoutElement>();
            spacerLE.flexibleWidth = 1f;
            spacerLE.preferredHeight = 1f;

            _cancelLbl = AddBottomBarButton(bar.transform, "Cancel", LocalizationService.Get("settings.bottom.cancel"), BtnAction, false, OnCancelClicked);
            _applyLbl  = AddBottomBarButton(bar.transform, "Apply",  LocalizationService.Get("settings.bottom.apply"),  BtnPrimary, true, OnApplyClicked);
        }

        private TextMeshProUGUI AddBottomBarButton(Transform parent, string name, string label, Color bg, bool emphasized, Action onClick)
        {
            var btnGO = NewGO(name, parent);
            var img = btnGO.AddComponent<Image>();
            UITheme.ApplySurface(img, bg, UIShapePreset.Inset);
            var btn = btnGO.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.transition = Selectable.Transition.ColorTint;
            btn.colors = UITheme.CreateColorBlock(
                bg,
                emphasized ? UITheme.Darken(bg, 0.05f) : UITheme.RaisedSurface,
                emphasized ? UITheme.Darken(bg, 0.12f) : UITheme.Border,
                bg,
                UITheme.WithAlpha(UITheme.Border, 0.55f));
            btn.onClick.AddListener(() => onClick());

            var le = btnGO.AddComponent<LayoutElement>();
            le.preferredWidth = 180f;
            le.preferredHeight = 46f;

            var lbl = MakeTMP("Lbl", btnGO.transform);
            lbl.text      = label;
            lbl.fontSize  = 18;
            lbl.fontStyle = FontStyles.Bold;
            lbl.color     = emphasized ? UITheme.InverseText : UITheme.PrimaryText;
            lbl.alignment = TextAlignmentOptions.Center;
            lbl.raycastTarget = false;
            FillRT(lbl.gameObject);

            return lbl;
        }
    }
}
