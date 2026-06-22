using UnityEngine;
using UnityEngine.UI;
using TMPro;
using RailwayManager.SharedUI;
using RailwayManager.SharedUI.Localization;

namespace MainMenu
{
    /// <summary>
    /// SettingsScreenUI — partial: lewy sidebar z 5 zakładkami (Sterowanie / Grafika / Dźwięk / Język / Ogólne).
    /// </summary>
    public partial class SettingsScreenUI
    {
        private void BuildSidebar()
        {
            var sidebar = NewGO("Sidebar", _root.transform);
            sidebar.AddComponent<Image>().color = SidebarBg;
            var sideRT = sidebar.GetComponent<RectTransform>();
            sideRT.anchorMin = Vector2.zero;
            sideRT.anchorMax = new Vector2(0f, 1f);
            sideRT.offsetMin = new Vector2(0f, BottomBarHeight);
            sideRT.offsetMax = new Vector2(SidebarWidth, -TopBarHeight);

            var vl = sidebar.AddComponent<VerticalLayoutGroup>();
            vl.padding  = UITheme.Padding(0f, UITheme.Spacing.Lg);
            vl.spacing  = UITheme.Spacing.Xxs;
            vl.childAlignment      = TextAnchor.UpperLeft;
            vl.childControlWidth   = true;
            vl.childControlHeight  = false;
            vl.childForceExpandWidth  = true;
            vl.childForceExpandHeight = false;

            for (int i = 0; i < SectionCount; i++)
            {
                int idx = i;
                var btn = NewGO($"Sec{i}", sidebar.transform);
                btn.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 56f);

                var bgImg = btn.AddComponent<Image>();
                bgImg.color = BtnNormal;
                _secBgs[i] = bgImg;

                var ub = btn.AddComponent<Button>();
                ub.targetGraphic = bgImg;
                ub.transition    = Selectable.Transition.ColorTint;
                ub.colors = UITheme.CreateColorBlock(
                    BtnNormal,
                    UITheme.RaisedSurface,
                    UITheme.Border,
                    BtnActive,
                    UITheme.WithAlpha(UITheme.Border, 0.55f));
                ub.onClick.AddListener(() =>
                {
                    _activeSection = idx;
                    ApplySidebarState();
                    PopulateSection(idx);
                    RefreshRowLabels();
                });

                // Accent stripe — left edge, 4 px wide
                var acGO = NewGO("Accent", btn.transform);
                var acImg = acGO.AddComponent<Image>();
                acImg.color = Accent;
                var acRT = acGO.GetComponent<RectTransform>();
                acRT.anchorMin = new Vector2(0f, 0f);
                acRT.anchorMax = new Vector2(0f, 1f);
                acRT.pivot     = new Vector2(0f, 0.5f);
                acRT.anchoredPosition = Vector2.zero;
                acRT.sizeDelta = new Vector2(4f, 0f);
                _secAccents[i] = acImg;

                // Label
                var lbl = MakeTMP("Lbl", btn.transform);
                lbl.text      = LocalizationService.Get(_secKeys[i]);
                lbl.fontSize  = 22;
                lbl.alignment = TextAlignmentOptions.Left;
                lbl.raycastTarget = false;
                var lblRT = lbl.GetComponent<RectTransform>();
                lblRT.anchorMin = Vector2.zero;
                lblRT.anchorMax = Vector2.one;
                lblRT.offsetMin = new Vector2(20f, 0f);
                lblRT.offsetMax = Vector2.zero;
                _secLbls[i] = lbl;
            }
        }

        private void ApplySidebarState()
        {
            for (int i = 0; i < SectionCount; i++)
            {
                bool on = i == _activeSection;
                _secBgs[i].color      = on ? BtnActive : BtnNormal;
                _secLbls[i].color     = on ? Accent    : TextMuted;
                _secLbls[i].fontStyle = on ? FontStyles.Bold : FontStyles.Normal;
                _secAccents[i].gameObject.SetActive(on);
            }
        }
    }
}
