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
        //  MODIFIER GROUPS BUILDER — 5 grup × 10 modifierów (slidery + 1 input field)
        // ═══════════════════════════════════════════

        private void BuildModifierGroups(Transform parent)
        {
            // Modifiers title (subheading)
            var modTitle = MakeTMP("ModTitle", parent);
            modTitle.text     = LocalizationService.Get("difficulty.modifiers_title");
            modTitle.fontSize = 18;
            modTitle.fontStyle = FontStyles.Bold;
            modTitle.color    = Accent;
            modTitle.gameObject.AddComponent<LayoutElement>().preferredHeight = 26f;
            _activeLabels.Add((modTitle, "difficulty.modifiers_title"));

            // Iteruj groups w stałej kolejności
            foreach (var groupKey in _modifierGroupOrder)
            {
                BuildGroupHeader(parent, groupKey);

                // Build wszystkie modifiery z tej grupy
                for (int i = 0; i < _modifierDefs.Length; i++)
                {
                    if (_modifierDefs[i].groupKey != groupKey) continue;
                    int captured = i;
                    if (_modifierDefs[i].isInputField)
                        BuildBudgetInputRow(captured, parent);
                    else
                        BuildModifierSliderRow(captured, parent);
                }
            }
        }

        private void BuildGroupHeader(Transform parent, string groupKey)
        {
            var card = NewGO($"GroupCard_{groupKey}", parent);
            UITheme.ApplySurface(card.AddComponent<Image>(), UITheme.WithAlpha(UITheme.SecondarySurface, 0.78f), UIShapePreset.Inset);
            card.AddComponent<LayoutElement>().preferredHeight = 34f;
            var layout = card.AddComponent<HorizontalLayoutGroup>();
            layout.padding = UITheme.Padding(UITheme.Spacing.Lg, UITheme.Spacing.Sm);
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = false;
            layout.childControlHeight = false;

            var hdr = MakeTMP($"GroupHdr_{groupKey}", card.transform);
            hdr.text     = LocalizationService.Get(groupKey);
            hdr.fontSize = 16;
            hdr.fontStyle = FontStyles.Bold;
            hdr.color    = Accent;
            hdr.gameObject.AddComponent<LayoutElement>().preferredHeight = 20f;
            _activeLabels.Add((hdr, groupKey));
        }

        private void BuildBudgetInputRow(int sliderIdx, Transform parent)
        {
            var def = _modifierDefs[sliderIdx];
            var row = NewGO($"ModRow_Budget_{sliderIdx}", parent);
            row.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 44f);
            UITheme.ApplySurface(row.AddComponent<Image>(), RowBg, UIShapePreset.Panel);
            row.AddComponent<LayoutElement>().preferredHeight = 44f;
            var hl = row.AddComponent<HorizontalLayoutGroup>();
            hl.padding  = UITheme.Padding(UITheme.Spacing.Lg, UITheme.Spacing.Sm);
            hl.spacing  = UITheme.Spacing.Md;
            hl.childAlignment      = TextAnchor.MiddleLeft;
            hl.childControlWidth   = false;
            hl.childControlHeight  = false;

            // Label
            var lbl = MakeTMP("Lbl", row.transform);
            lbl.text     = LocalizationService.Get(def.i18nKey);
            lbl.fontSize = 16;
            lbl.color    = TextPrimary;
            var lblLE = lbl.gameObject.AddComponent<LayoutElement>();
            lblLE.preferredWidth  = 250f;
            lblLE.preferredHeight = 28f;
            _activeLabels.Add((lbl, def.i18nKey));

            // "?" tooltip button
            BuildTooltipButton(row.transform, def.tooltipKey);

            // TMP_InputField z PLN
            var inputGO = NewGO("BudgetInput", row.transform);
            inputGO.GetComponent<RectTransform>().sizeDelta = new Vector2(180f, 32f);
            inputGO.AddComponent<Image>().color = InputBg;
            var inputLE = inputGO.AddComponent<LayoutElement>();
            inputLE.preferredWidth = 180f;
            inputLE.preferredHeight = 32f;

            var area = NewGO("Area", inputGO.transform);
            FillRT(area);
            var areaRT = area.GetComponent<RectTransform>();
            areaRT.offsetMin = new Vector2(8f, 2f);
            areaRT.offsetMax = new Vector2(-8f, -2f);

            var ph = MakeTMP("Placeholder", area.transform);
            ph.text         = BASE_STARTING_BUDGET_PLN.ToString("N0");
            ph.fontSize     = 18;
            ph.color        = TextMuted;
            ph.fontStyle    = FontStyles.Italic;
            ph.raycastTarget = false;
            FillRT(ph.gameObject);

            var txt = MakeTMP("Text", area.transform);
            txt.fontSize     = 18;
            txt.color        = TextPrimary;
            txt.raycastTarget = false;
            FillRT(txt.gameObject);

            var field = inputGO.AddComponent<TMP_InputField>();
            field.textComponent = txt;
            field.placeholder   = ph;
            field.textViewport  = areaRT;
            field.contentType   = TMP_InputField.ContentType.IntegerNumber;
            field.characterLimit = 8; // do 99 999 999 PLN

            _budgetInputField = field;

            // PLN suffix
            var sufLbl = MakeTMP("Suffix", row.transform);
            sufLbl.fontSize  = 16;
            sufLbl.color     = Accent;
            sufLbl.text      = "PLN";
            sufLbl.gameObject.AddComponent<LayoutElement>().preferredWidth = 40f;

            // onEndEdit (commit) — switch na Custom + recompute multiplier
            field.onEndEdit.AddListener(value =>
            {
                if (_suppressAutoCustom) return;
                if (long.TryParse(value, out var pln) && pln > 0)
                {
                    float multiplier = (float)pln / BASE_STARTING_BUDGET_PLN;
                    multiplier = Mathf.Clamp(multiplier, def.min, def.max);
                    SwitchToCustomKeepingValues(sliderIdx, multiplier);
                }
                MarkDirty();
                RefreshLivePreview();
            });

            // Brak slidera — index 0 zostaje null w _customSliders
            _customSliderValueLabels[sliderIdx] = txt; // używane przy preset switch
        }

        private void BuildModifierSliderRow(int sliderIdx, Transform parent)
        {
            var def = _modifierDefs[sliderIdx];
            var row = NewGO($"ModRow_{sliderIdx}", parent);
            row.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 44f);
            UITheme.ApplySurface(row.AddComponent<Image>(), RowBg, UIShapePreset.Panel);
            row.AddComponent<LayoutElement>().preferredHeight = 44f;
            var hl = row.AddComponent<HorizontalLayoutGroup>();
            hl.padding  = UITheme.Padding(UITheme.Spacing.Lg, UITheme.Spacing.Sm);
            hl.spacing  = UITheme.Spacing.Md;
            hl.childAlignment      = TextAnchor.MiddleLeft;
            hl.childControlWidth   = false;
            hl.childControlHeight  = false;

            // Label
            var lbl = MakeTMP("Lbl", row.transform);
            lbl.text     = LocalizationService.Get(def.i18nKey);
            lbl.fontSize = 16;
            lbl.color    = TextPrimary;
            var lblLE = lbl.gameObject.AddComponent<LayoutElement>();
            lblLE.preferredWidth  = 250f;
            lblLE.preferredHeight = 28f;
            _activeLabels.Add((lbl, def.i18nKey));

            // "?" tooltip button
            BuildTooltipButton(row.transform, def.tooltipKey);

            // Slider
            var sliderGO = NewGO("Slider", row.transform);
            var bg = NewGO("BG", sliderGO.transform);
            UITheme.ApplySurface(bg.AddComponent<Image>(), SliderBg, UIShapePreset.Pill);
            var bgRT = bg.GetComponent<RectTransform>();
            bgRT.anchorMin = new Vector2(0f, 0.4f);
            bgRT.anchorMax = new Vector2(1f, 0.6f);
            bgRT.offsetMin = Vector2.zero;
            bgRT.offsetMax = Vector2.zero;

            var fa = NewGO("Fill Area", sliderGO.transform);
            var faRT = fa.GetComponent<RectTransform>();
            faRT.anchorMin = new Vector2(0f, 0.4f);
            faRT.anchorMax = new Vector2(1f, 0.6f);
            faRT.offsetMin = new Vector2(5f, 0f);
            faRT.offsetMax = new Vector2(-15f, 0f);
            var fill = NewGO("Fill", fa.transform);
            UITheme.ApplySurface(fill.AddComponent<Image>(), Accent, UIShapePreset.Pill);

            var ha = NewGO("Handle Slide Area", sliderGO.transform);
            var haRT = ha.GetComponent<RectTransform>();
            haRT.anchorMin = Vector2.zero;
            haRT.anchorMax = Vector2.one;
            haRT.offsetMin = new Vector2(10f, 0f);
            haRT.offsetMax = new Vector2(-10f, 0f);
            var handle = NewGO("Handle", ha.transform);
            var handleImg = handle.AddComponent<Image>();
            UITheme.ApplySurface(handleImg, UITheme.RaisedSurface, UIShapePreset.Pill);
            handle.GetComponent<RectTransform>().sizeDelta = new Vector2(18f, 18f);

            var slider = sliderGO.AddComponent<Slider>();
            slider.minValue      = def.min;
            slider.maxValue      = def.max;
            slider.value         = 1f;
            slider.fillRect      = fill.GetComponent<RectTransform>();
            slider.handleRect    = handle.GetComponent<RectTransform>();
            slider.targetGraphic = handleImg;

            var sliderLE = sliderGO.AddComponent<LayoutElement>();
            sliderLE.preferredWidth  = 180f;
            sliderLE.preferredHeight = 28f;

            // Value label
            var valLbl = MakeTMP("Val", row.transform);
            valLbl.fontSize  = 14;
            valLbl.color     = Accent;
            valLbl.alignment = TextAlignmentOptions.Left;
            valLbl.text      = "1.00x";
            var valLE = valLbl.gameObject.AddComponent<LayoutElement>();
            valLE.preferredWidth = 60f;
            valLE.preferredHeight = 28f;

            slider.onValueChanged.AddListener(v =>
            {
                valLbl.text = $"{v:F2}x";
                if (!_suppressAutoCustom)
                {
                    SwitchToCustomKeepingValues(sliderIdx, v);
                    MarkDirty();
                }
                RefreshLivePreview();
            });

            _customSliders[sliderIdx] = slider;
            _customSliderValueLabels[sliderIdx] = valLbl;
        }
    }
}
