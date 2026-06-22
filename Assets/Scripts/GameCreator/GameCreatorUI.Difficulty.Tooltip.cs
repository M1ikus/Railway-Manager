using UnityEngine;
using UnityEngine.UI;
using TMPro;
using RailwayManager.SharedUI;
using RailwayManager.SharedUI.Localization;

namespace RailwayManager.GameCreator
{
    public partial class GameCreatorUI
    {
        // ═══════════════════════════════════════════
        //  TOOLTIP "?" BUTTON — używa centralnego SharedUI/TooltipManager (MUI-3)
        // ═══════════════════════════════════════════

        /// <summary>
        /// Buduje "?" mini-button który pokazuje tooltip na hover. Tooltip body delegowany
        /// do <see cref="TooltipManager"/> (SharedUI MUI-3) — own ad-hoc system (pre-2026-05-14)
        /// zastąpiony żeby uniknąć duplikatu (auto-fade, screen-edge clamp, hover delay,
        /// DontDestroyOnLoad singleton — wszystko dostarcza central).
        ///
        /// I18n hot-reload: trigger.text resolved raz przy create. Zmiana języka triggeruje
        /// PopulateSection rebuild → BuildTooltipButton znowu wywołany → fresh text.
        /// </summary>
        private void BuildTooltipButton(Transform parent, string tooltipKey)
        {
            var btnGO = NewGO("Tooltip?", parent);
            btnGO.GetComponent<RectTransform>().sizeDelta = new Vector2(24f, 24f);
            var bgImg = btnGO.AddComponent<Image>();
            UITheme.ApplySurface(bgImg, UITheme.WithAlpha(UITheme.PrimaryAccent, 0.22f), UIShapePreset.Pill);
            bgImg.raycastTarget = true; // wymagane dla TooltipTrigger.OnPointerEnter
            var buttonLayout = btnGO.AddComponent<LayoutElement>();
            buttonLayout.preferredWidth  = 24f;
            buttonLayout.preferredHeight = 24f;

            var qlbl = MakeTMP("Q", btnGO.transform);
            qlbl.text      = "?";
            qlbl.fontSize  = 14;
            qlbl.fontStyle = FontStyles.Bold;
            qlbl.alignment = TextAlignmentOptions.Center;
            qlbl.color     = TextPrimary;
            qlbl.raycastTarget = false;
            FillRT(qlbl.gameObject);

            var trigger = btnGO.AddComponent<TooltipTrigger>();
            trigger.text = LocalizationService.Get(tooltipKey);
        }
    }
}
