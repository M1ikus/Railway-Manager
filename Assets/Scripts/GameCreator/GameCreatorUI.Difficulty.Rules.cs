using UnityEngine;
using UnityEngine.UI;
using TMPro;
using RailwayManager.Core.GameRules;
using RailwayManager.SharedUI;
using RailwayManager.SharedUI.Localization;

namespace RailwayManager.GameCreator
{
    public partial class GameCreatorUI
    {
        // ═══════════════════════════════════════════
        //  GAME RULES SECTION (6 toggles z tooltipami)
        // ═══════════════════════════════════════════

        private void PopulateGameRulesSection()
        {
            // Section header
            BuildSectionHeaderCard("RulesHdrCard", "game_rules.section_title");

            // Info note
            BuildSectionInfoCard("RulesInfoCard", "game_rules.info_locked");

            _ruleToggles.Clear();
            for (int i = 0; i < _ruleDefs.Length; i++)
            {
                var (rule, i18nKey, tooltipKey, defaultOn) = _ruleDefs[i];
                bool isOn = _gameRuleValues.TryGetValue(rule, out var stored) ? stored : defaultOn;
                BuildGameRuleRow(rule, i18nKey, tooltipKey, isOn);
            }
        }

        private void BuildGameRuleRow(GameRule rule, string i18nKey, string tooltipKey, bool defaultOn)
        {
            var row = NewGO($"Rule_{rule}", _contentParent);
            row.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 44f);
            row.AddComponent<LayoutElement>().preferredHeight = 44f;
            UITheme.ApplySurface(row.AddComponent<Image>(), RowBg, UIShapePreset.Panel);
            var hl = row.AddComponent<HorizontalLayoutGroup>();
            hl.padding  = UITheme.Padding(UITheme.Spacing.Lg, UITheme.Spacing.Sm);
            hl.spacing  = UITheme.Spacing.Md;
            hl.childAlignment      = TextAnchor.MiddleLeft;
            hl.childControlWidth   = false;
            hl.childControlHeight  = false;

            // Label
            var lbl = MakeTMP("Lbl", row.transform);
            lbl.text     = LocalizationService.Get(i18nKey);
            lbl.fontSize = 16;
            lbl.color    = TextPrimary;
            var lblLE = lbl.gameObject.AddComponent<LayoutElement>();
            lblLE.preferredWidth  = 280f;
            lblLE.preferredHeight = 28f;
            _activeLabels.Add((lbl, i18nKey));

            // "?" tooltip button
            BuildTooltipButton(row.transform, tooltipKey);

            // Toggle (po prawej)
            var tGO = NewGO("Toggle", row.transform);
            tGO.GetComponent<RectTransform>().sizeDelta = new Vector2(36f, 30f);
            var bgGO = NewGO("BG", tGO.transform);
            var bgImg = bgGO.AddComponent<Image>();
            UITheme.ApplySurface(bgImg, ToggleBg, UIShapePreset.Inset);
            FillRT(bgGO);

            var chkGO = NewGO("Check", bgGO.transform);
            var chkImg = chkGO.AddComponent<Image>();
            chkImg.color = Accent;
            var chkRT = chkGO.GetComponent<RectTransform>();
            chkRT.anchorMin = new Vector2(0.2f, 0.2f);
            chkRT.anchorMax = new Vector2(0.8f, 0.8f);
            chkRT.offsetMin = Vector2.zero;
            chkRT.offsetMax = Vector2.zero;

            var toggle = tGO.AddComponent<Toggle>();
            toggle.targetGraphic = bgImg;
            toggle.graphic = chkImg;
            toggle.isOn    = defaultOn;
            tGO.AddComponent<LayoutElement>().preferredWidth = 36f;
            toggle.onValueChanged.AddListener(_ => MarkDirty());

            _ruleToggles[rule] = toggle;
        }
    }
}
