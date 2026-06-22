using System.Collections.Generic;
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
        //  ROW BUILDERS — InputRow / DropdownRow / SliderRow / ToggleRow / MakeRow / AddSpacer
        // ═══════════════════════════════════════════

        /// <summary>TD-022: overload z out-parameter dla TMP_InputField handle (do ApplyOnStart).</summary>
        private TextMeshProUGUI InputRow(string name, string labelKey, string placeholderKey, bool password,
                                         out TMP_InputField field)
        {
            var lbl = InputRow(name, labelKey, placeholderKey, password);
            // Field jest na samym row (added na inputGO które jest dzieckiem row)
            field = lbl.transform.parent.GetComponentInChildren<TMP_InputField>();
            return lbl;
        }

        /// <summary>Creates a row with label on left and TMP_InputField on right.
        /// <paramref name="labelKey"/> + <paramref name="placeholderKey"/> = i18n keys.
        /// Pass empty string dla placeholderKey jeśli nie potrzeba placeholdera.</summary>
        private TextMeshProUGUI InputRow(string name, string labelKey, string placeholderKey, bool password)
        {
            var lbl = MakeRow(name, LocalizationService.Get(labelKey), out var row);
            _activeLabels.Add((lbl, labelKey));

            var inputGO = NewGO("Input", row.transform);
            inputGO.GetComponent<RectTransform>().sizeDelta = new Vector2(320f, 36f);
            UITheme.ApplySurface(inputGO.AddComponent<Image>(), InputBg, UIShapePreset.Inset);
            inputGO.AddComponent<LayoutElement>().preferredWidth = 320f;

            var area = NewGO("Area", inputGO.transform);
            FillRT(area);
            var areaRT = area.GetComponent<RectTransform>();
            areaRT.offsetMin = new Vector2(8f, 2f);
            areaRT.offsetMax = new Vector2(-8f, -2f);

            var ph = MakeTMP("Placeholder", area.transform);
            ph.text      = string.IsNullOrEmpty(placeholderKey) ? "" : LocalizationService.Get(placeholderKey);
            ph.fontSize  = 20;
            ph.color     = TextMuted;
            ph.fontStyle = FontStyles.Italic;
            ph.raycastTarget = false;
            FillRT(ph.gameObject);

            var txt = MakeTMP("Text", area.transform);
            txt.fontSize = 20;
            txt.color    = TextPrimary;
            txt.raycastTarget = false;
            FillRT(txt.gameObject);

            var field = inputGO.AddComponent<TMP_InputField>();
            field.textComponent = txt;
            field.placeholder   = ph;
            field.textViewport  = area.GetComponent<RectTransform>();
            if (password)
                field.inputType = TMP_InputField.InputType.Password;

            field.onValueChanged.AddListener(_ => MarkDirty());
            return lbl;
        }

        /// <summary>TD-022: overload z out-parameter dla TMP_Dropdown handle.</summary>
        private TextMeshProUGUI DropdownRow(string name, string labelKey, string[] optionKeys,
                                            out TMP_Dropdown dropdown)
        {
            var lbl = DropdownRow(name, labelKey, optionKeys);
            dropdown = lbl.transform.parent.GetComponentInChildren<TMP_Dropdown>();
            return lbl;
        }

        /// <summary>Dropdown z label key + array option keys (resolved per LocalizationService.Get).</summary>
        private TextMeshProUGUI DropdownRow(string name, string labelKey, string[] optionKeys)
        {
            var lbl = MakeRow(name, LocalizationService.Get(labelKey), out var row);
            _activeLabels.Add((lbl, labelKey));

            var ddGO = NewGO("DD", row.transform);
            ddGO.GetComponent<RectTransform>().sizeDelta = new Vector2(220f, 36f);
            var ddImg = ddGO.AddComponent<Image>();
            UITheme.ApplySurface(ddImg, DropdownBg, UIShapePreset.Inset);

            var dd = ddGO.AddComponent<TMP_Dropdown>();
            dd.targetGraphic = ddImg;
            dd.ClearOptions();
            var opts = new List<TMP_Dropdown.OptionData>();
            foreach (var optKey in optionKeys) opts.Add(new TMP_Dropdown.OptionData(LocalizationService.Get(optKey)));
            dd.AddOptions(opts);

            var capLbl = MakeTMP("Lbl", ddGO.transform);
            capLbl.fontSize  = 18;
            capLbl.color     = TextPrimary;
            capLbl.alignment = TextAlignmentOptions.Left;
            capLbl.raycastTarget = false;
            var capRT = capLbl.GetComponent<RectTransform>();
            capRT.anchorMin = Vector2.zero;
            capRT.anchorMax = Vector2.one;
            capRT.offsetMin = new Vector2(10f, 2f);
            capRT.offsetMax = new Vector2(-30f, -2f);
            dd.captionText = capLbl;

            ddGO.AddComponent<LayoutElement>().preferredWidth = 220f;
            dd.onValueChanged.AddListener(_ => MarkDirty());
            return lbl;
        }

        /// <summary>TD-022: overload z out-parameter dla Slider handle.</summary>
        private TextMeshProUGUI SliderRow(string name, string labelKey,
            float min, float max, float val,
            out Slider slider,
            System.Func<float, string> formatter = null)
        {
            var lbl = SliderRow(name, labelKey, min, max, val, formatter);
            slider = lbl.transform.parent.GetComponentInChildren<Slider>();
            return lbl;
        }

        private TextMeshProUGUI SliderRow(string name, string labelKey,
            float min, float max, float val,
            System.Func<float, string> formatter = null)
        {
            var lbl = MakeRow(name, LocalizationService.Get(labelKey), out var row);
            _activeLabels.Add((lbl, labelKey));

            var sliderGO = NewGO("Slider", row.transform);

            var bg = NewGO("BG", sliderGO.transform);
            UITheme.ApplySurface(bg.AddComponent<Image>(), SliderBg, UIShapePreset.Pill);
            var bgRT = bg.GetComponent<RectTransform>();
            bgRT.anchorMin = new Vector2(0f, 0.25f);
            bgRT.anchorMax = new Vector2(1f, 0.75f);
            bgRT.offsetMin = Vector2.zero;
            bgRT.offsetMax = Vector2.zero;

            var fa = NewGO("Fill Area", sliderGO.transform);
            var faRT = fa.GetComponent<RectTransform>();
            faRT.anchorMin = new Vector2(0f, 0.25f);
            faRT.anchorMax = new Vector2(1f, 0.75f);
            faRT.offsetMin = new Vector2(5f, 0f);
            faRT.offsetMax = new Vector2(-15f, 0f);

            var fill = NewGO("Fill", fa.transform);
            UITheme.ApplySurface(fill.AddComponent<Image>(), Accent, UIShapePreset.Pill);
            var fillRT = fill.GetComponent<RectTransform>();
            fillRT.sizeDelta = Vector2.zero;

            var ha = NewGO("Handle Slide Area", sliderGO.transform);
            var haRT = ha.GetComponent<RectTransform>();
            haRT.anchorMin = Vector2.zero;
            haRT.anchorMax = Vector2.one;
            haRT.offsetMin = new Vector2(10f, 0f);
            haRT.offsetMax = new Vector2(-10f, 0f);

            var handle = NewGO("Handle", ha.transform);
            var handleImg = handle.AddComponent<Image>();
            handleImg.color = TextPrimary;
            handle.GetComponent<RectTransform>().sizeDelta = new Vector2(20f, 20f);

            var sliderComp = sliderGO.AddComponent<Slider>();
            sliderComp.minValue      = min;
            sliderComp.maxValue      = max;
            sliderComp.value         = val;
            sliderComp.fillRect      = fillRT;
            sliderComp.handleRect    = handle.GetComponent<RectTransform>();
            sliderComp.targetGraphic = handleImg;

            var le = sliderGO.AddComponent<LayoutElement>();
            le.preferredWidth  = 260f;
            le.preferredHeight = 36f;

            // Value label
            var valLbl = MakeTMP("Val", row.transform);
            valLbl.fontSize  = 18;
            valLbl.color     = Accent;
            valLbl.alignment = TextAlignmentOptions.Left;
            valLbl.raycastTarget = false;
            valLbl.gameObject.AddComponent<LayoutElement>().preferredWidth = 60f;
            valLbl.text = formatter != null ? formatter(val) : val.ToString("F1");

            sliderComp.onValueChanged.AddListener(v =>
            {
                valLbl.text = formatter != null ? formatter(v) : v.ToString("F1");
                MarkDirty();
            });

            return lbl;
        }

        /// <summary>TD-022: overload z out-parameter dla Toggle handle.</summary>
        private TextMeshProUGUI ToggleRow(string name, string labelKey, bool defaultOn,
                                          out Toggle toggle)
        {
            var lbl = ToggleRow(name, labelKey, defaultOn);
            toggle = lbl.transform.parent.GetComponentInChildren<Toggle>();
            return lbl;
        }

        private TextMeshProUGUI ToggleRow(string name, string labelKey, bool defaultOn)
        {
            var lbl = MakeRow(name, LocalizationService.Get(labelKey), out var row);
            _activeLabels.Add((lbl, labelKey));

            var tGO = NewGO("Toggle", row.transform);
            tGO.GetComponent<RectTransform>().sizeDelta = new Vector2(36f, 36f);

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
            return lbl;
        }

        // ── Row base ──────────────────────────────────

        private TextMeshProUGUI MakeRow(string name, string labelText, out GameObject row)
        {
            row = NewGO(name, _contentParent);
            row.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 52f);
            UITheme.ApplySurface(row.AddComponent<Image>(), RowBg, UIShapePreset.Panel);

            var hl = row.AddComponent<HorizontalLayoutGroup>();
            hl.padding  = UITheme.Padding(UITheme.Spacing.Xl, UITheme.Spacing.Sm);
            hl.spacing  = UITheme.Spacing.Xl;
            hl.childAlignment      = TextAnchor.MiddleLeft;
            hl.childControlWidth   = false;
            hl.childControlHeight  = false;
            hl.childForceExpandWidth  = false;
            hl.childForceExpandHeight = false;

            var lbl = MakeTMP("Lbl", row.transform);
            lbl.text     = labelText;
            lbl.fontSize = 20;
            lbl.color    = TextPrimary;
            lbl.raycastTarget = false;
            var le = lbl.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth  = 360f;
            le.preferredHeight = 36f;
            return lbl;
        }

        private void AddSpacer(float h)
        {
            var sp = NewGO("Spacer", _contentParent);
            sp.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, h);
            sp.AddComponent<LayoutElement>().preferredHeight = h;
        }
    }
}
