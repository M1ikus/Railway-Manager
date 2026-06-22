using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using RailwayManager.SharedUI;

namespace DepotSystem.Schemas.UI
{
    /// <summary>
    /// Partial: pure-function helpers (Clone/Average/GetCurrentType) + generic UI widget
    /// builders (Label/Slider/Dropdown/Toggle/Button/HorizontalRow/Layout preferences).
    /// </summary>
    public partial class SchemaPanelUI
    {
        // ════════════════════════════════════════
        //  DATA HELPERS
        // ════════════════════════════════════════

        private static SchemaParameters ClonePameters(SchemaParameters src)
        {
            // Snapshot definitions mają parameters=null (geometria zaszyta w snapshotGeometry).
            // Zwracamy null żeby zachować spójność.
            if (src == null) return null;

            var clone = new SchemaParameters
            {
                trackCount = src.trackCount,
                trackSpacing = src.trackSpacing,
                trackSpacings = src.trackSpacings != null ? (float[])src.trackSpacings.Clone() : null,
                turnoutType = src.turnoutType,
                turnoutTypes = src.turnoutTypes != null ? (string[])src.turnoutTypes.Clone() : null,
                mirror = src.mirror,
            };
            return clone;
        }

        private static TurnoutSchemaDefinition CloneDefinition(TurnoutSchemaDefinition src)
        {
            return new TurnoutSchemaDefinition
            {
                schemaFormatVersion = src.schemaFormatVersion,
                id = src.id + "_edit",
                name = src.name + " (edited)",
                description = src.description,
                category = src.category,
                type = src.type,
                author = src.author,
                tags = src.tags,
                version = src.version,
                createdAt = src.createdAt,
                modifiedAt = src.modifiedAt,
                workshopId = 0,
                previewPngBase64 = "",
                parameters = ClonePameters(src.parameters),
                snapshotGeometry = src.snapshotGeometry,  // shared reference (nieedytowalny po zapisie)
            };
        }

        private static float AverageSpacing(SchemaParameters p)
        {
            if (p.trackSpacings == null || p.trackSpacings.Length == 0)
                return p.trackSpacing > 0 ? p.trackSpacing : SchemaParameters.DefaultSpacing;
            float sum = 0f;
            for (int i = 0; i < p.trackSpacings.Length; i++) sum += p.trackSpacings[i];
            return sum / p.trackSpacings.Length;
        }

        private static string GetCurrentTurnoutType(SchemaParameters p)
        {
            if (!string.IsNullOrEmpty(p.turnoutType)) return p.turnoutType;
            if (p.turnoutTypes != null && p.turnoutTypes.Length > 0) return p.turnoutTypes[0];
            return SchemaTurnoutType.R190;
        }

        // ════════════════════════════════════════
        //  UI WIDGET BUILDERS (generic, reusable)
        // ════════════════════════════════════════

        private TMP_Text CreateLabel(Transform parent, string text, int fontSize, FontStyles style = FontStyles.Normal)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0, fontSize + 8);

            var label = go.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = fontSize;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.fontStyle = style;
            UITheme.ApplyTmpText(label, style == FontStyles.Italic ? UIThemeTextRole.Secondary : UIThemeTextRole.Primary);

            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = fontSize + 10;

            return label;
        }

        private GameObject CreateHorizontalRow(Transform parent)
        {
            var go = new GameObject("Row");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0, 30);

            var hlg = go.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = UITheme.Spacing.Md;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = false;
            hlg.childAlignment = TextAnchor.MiddleLeft;

            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 30;

            return go;
        }

        private Slider CreateSlider(Transform parent, float min, float max, float value)
        {
            var go = new GameObject("Slider");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(200, 30);

            var bg = go.AddComponent<Image>();
            UITheme.ApplySurface(bg, UITheme.WithAlpha(UITheme.SecondarySurface, 0.82f), UIShapePreset.Inset);

            var slider = go.AddComponent<Slider>();
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = value;

            // Fill
            var fillAreaGO = new GameObject("FillArea");
            fillAreaGO.transform.SetParent(go.transform, false);
            var faRT = fillAreaGO.AddComponent<RectTransform>();
            faRT.anchorMin = new Vector2(0, 0.25f);
            faRT.anchorMax = new Vector2(1, 0.75f);
            faRT.offsetMin = new Vector2(5, 0);
            faRT.offsetMax = new Vector2(-5, 0);

            var fillGO = new GameObject("Fill");
            fillGO.transform.SetParent(fillAreaGO.transform, false);
            var fillRT = fillGO.AddComponent<RectTransform>();
            fillRT.anchorMin = Vector2.zero;
            fillRT.anchorMax = Vector2.one;
            fillRT.offsetMin = fillRT.offsetMax = Vector2.zero;
            var fillImg = fillGO.AddComponent<Image>();
            UITheme.ApplySurface(fillImg, UITheme.PrimaryAccent, UIShapePreset.Inset);

            slider.fillRect = fillRT;

            // Handle
            var handleAreaGO = new GameObject("HandleArea");
            handleAreaGO.transform.SetParent(go.transform, false);
            var haRT = handleAreaGO.AddComponent<RectTransform>();
            haRT.anchorMin = Vector2.zero;
            haRT.anchorMax = Vector2.one;
            haRT.offsetMin = new Vector2(5, 0);
            haRT.offsetMax = new Vector2(-5, 0);

            var handleGO = new GameObject("Handle");
            handleGO.transform.SetParent(handleAreaGO.transform, false);
            var handleRT = handleGO.AddComponent<RectTransform>();
            handleRT.sizeDelta = new Vector2(15, 30);
            var handleImg = handleGO.AddComponent<Image>();
            UITheme.ApplySurface(handleImg, UITheme.PrimaryText, UIShapePreset.Pill);

            slider.targetGraphic = handleImg;
            slider.handleRect = handleRT;
            slider.direction = Slider.Direction.LeftToRight;

            var le = go.AddComponent<LayoutElement>();
            le.flexibleWidth = 1;
            le.preferredHeight = 30;

            return slider;
        }

        private TMP_Dropdown CreateDropdown(Transform parent, List<string> options, int defaultIdx)
        {
            var go = new GameObject("Dropdown");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0, 35);

            var img = go.AddComponent<Image>();
            UITheme.ApplySurface(img, UITheme.SecondarySurface, UIShapePreset.Button);

            var dropdown = go.AddComponent<TMP_Dropdown>();
            dropdown.targetGraphic = img;
            dropdown.ClearOptions();
            dropdown.AddOptions(options);
            dropdown.value = defaultIdx;

            // Label
            var labelGO = new GameObject("Label");
            labelGO.transform.SetParent(go.transform, false);
            var labelRT = labelGO.AddComponent<RectTransform>();
            labelRT.anchorMin = Vector2.zero;
            labelRT.anchorMax = Vector2.one;
            labelRT.offsetMin = new Vector2(10, 0);
            labelRT.offsetMax = new Vector2(-30, 0);
            var label = labelGO.AddComponent<TextMeshProUGUI>();
            label.fontSize = 13;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            UITheme.ApplyTmpText(label, UIThemeTextRole.Primary);
            dropdown.captionText = label;

            // Arrow placeholder (caret)
            var arrowGO = new GameObject("Arrow");
            arrowGO.transform.SetParent(go.transform, false);
            var arrowRT = arrowGO.AddComponent<RectTransform>();
            arrowRT.anchorMin = new Vector2(1, 0.5f);
            arrowRT.anchorMax = new Vector2(1, 0.5f);
            arrowRT.pivot = new Vector2(1, 0.5f);
            arrowRT.sizeDelta = new Vector2(20, 20);
            arrowRT.anchoredPosition = new Vector2(-5, 0);
            var arrowText = arrowGO.AddComponent<TextMeshProUGUI>();
            arrowText.text = "v";
            arrowText.fontSize = 14;
            arrowText.alignment = TextAlignmentOptions.Center;
            UITheme.ApplyTmpText(arrowText, UIThemeTextRole.Secondary);

            // Template (dropdown opens this)
            var templateGO = CreateDropdownTemplate(go.transform);
            dropdown.template = templateGO.GetComponent<RectTransform>();
            dropdown.itemText = templateGO.transform.Find("Viewport/Content/Item/Item Label").GetComponent<TMP_Text>();

            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 35;

            dropdown.RefreshShownValue();
            return dropdown;
        }

        private GameObject CreateDropdownTemplate(Transform parent)
        {
            var template = new GameObject("Template");
            template.transform.SetParent(parent, false);
            var rt = template.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(1, 0);
            rt.pivot = new Vector2(0.5f, 1);
            rt.sizeDelta = new Vector2(0, 150);

            var bg = template.AddComponent<Image>();
            UITheme.ApplySurface(bg, UITheme.OverlayPanelStrong, UIShapePreset.Panel);
            template.AddComponent<ScrollRect>();

            var viewport = new GameObject("Viewport");
            viewport.transform.SetParent(template.transform, false);
            var vrt = viewport.AddComponent<RectTransform>();
            vrt.anchorMin = Vector2.zero;
            vrt.anchorMax = Vector2.one;
            vrt.offsetMin = vrt.offsetMax = Vector2.zero;
            var viewportBg = viewport.AddComponent<Image>();
            UITheme.ApplySurface(viewportBg, UITheme.WithAlpha(UITheme.AppBackground, 0.12f), UIShapePreset.Inset);
            viewport.AddComponent<Mask>().showMaskGraphic = false;

            var content = new GameObject("Content");
            content.transform.SetParent(viewport.transform, false);
            var crt = content.AddComponent<RectTransform>();
            crt.anchorMin = new Vector2(0, 1);
            crt.anchorMax = new Vector2(1, 1);
            crt.pivot = new Vector2(0.5f, 1);
            crt.sizeDelta = new Vector2(0, 30);

            var item = new GameObject("Item");
            item.transform.SetParent(content.transform, false);
            var irt = item.AddComponent<RectTransform>();
            irt.anchorMin = new Vector2(0, 0.5f);
            irt.anchorMax = new Vector2(1, 0.5f);
            irt.sizeDelta = new Vector2(0, 25);

            item.AddComponent<Toggle>();

            var bgItem = new GameObject("Item Background");
            bgItem.transform.SetParent(item.transform, false);
            var brt = bgItem.AddComponent<RectTransform>();
            brt.anchorMin = Vector2.zero;
            brt.anchorMax = Vector2.one;
            brt.offsetMin = brt.offsetMax = Vector2.zero;
            var itemBg = bgItem.AddComponent<Image>();
            UITheme.ApplySurface(itemBg, UITheme.SecondarySurface, UIShapePreset.Inset);

            var labelGO = new GameObject("Item Label");
            labelGO.transform.SetParent(item.transform, false);
            var lrt = labelGO.AddComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = new Vector2(10, 0);
            lrt.offsetMax = new Vector2(-10, 0);
            var label = labelGO.AddComponent<TextMeshProUGUI>();
            label.fontSize = 13;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            UITheme.ApplyTmpText(label, UIThemeTextRole.Primary);

            var sr = template.GetComponent<ScrollRect>();
            sr.viewport = vrt;
            sr.content = crt;
            sr.vertical = true;
            sr.horizontal = false;

            template.SetActive(false);
            return template;
        }

        private Toggle CreateToggle(Transform parent, string label, bool defaultValue)
        {
            var go = new GameObject("Toggle");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0, 30);

            var toggle = go.AddComponent<Toggle>();
            toggle.isOn = defaultValue;

            // Background (checkbox bg)
            var bgGO = new GameObject("Background");
            bgGO.transform.SetParent(go.transform, false);
            var bgRT = bgGO.AddComponent<RectTransform>();
            bgRT.anchorMin = new Vector2(0, 0.5f);
            bgRT.anchorMax = new Vector2(0, 0.5f);
            bgRT.pivot = new Vector2(0, 0.5f);
            bgRT.sizeDelta = new Vector2(20, 20);
            bgRT.anchoredPosition = new Vector2(5, 0);
            var bgImg = bgGO.AddComponent<Image>();
            UITheme.ApplySurface(bgImg, UITheme.SecondarySurface, UIShapePreset.Inset);

            // Checkmark
            var checkGO = new GameObject("Checkmark");
            checkGO.transform.SetParent(bgGO.transform, false);
            var checkRT = checkGO.AddComponent<RectTransform>();
            checkRT.anchorMin = Vector2.zero;
            checkRT.anchorMax = Vector2.one;
            checkRT.offsetMin = new Vector2(2, 2);
            checkRT.offsetMax = new Vector2(-2, -2);
            var checkImg = checkGO.AddComponent<Image>();
            UITheme.ApplySurface(checkImg, UITheme.PrimaryAccent, UIShapePreset.Inset);

            toggle.targetGraphic = bgImg;
            toggle.graphic = checkImg;

            // Label
            var labelGO = new GameObject("Label");
            labelGO.transform.SetParent(go.transform, false);
            var labelRT = labelGO.AddComponent<RectTransform>();
            labelRT.anchorMin = Vector2.zero;
            labelRT.anchorMax = Vector2.one;
            labelRT.offsetMin = new Vector2(35, 0);
            labelRT.offsetMax = new Vector2(0, 0);
            var labelText = labelGO.AddComponent<TextMeshProUGUI>();
            labelText.text = label;
            labelText.fontSize = 13;
            labelText.alignment = TextAlignmentOptions.MidlineLeft;
            UITheme.ApplyTmpText(labelText, UIThemeTextRole.Primary);

            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 30;

            return toggle;
        }

        private Button CreateButton(Transform parent, string text, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject("Button");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(120, 40);

            var img = go.AddComponent<Image>();
            UITheme.ApplySurface(img, UITheme.PrimaryAccent, UIShapePreset.Pill);

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.colors = UITheme.CreateColorBlock(
                UITheme.PrimaryAccent,
                UITheme.PrimaryAccentHover,
                UITheme.Darken(UITheme.PrimaryAccentHover, 0.18f),
                UITheme.PrimaryAccentHover,
                UITheme.WithAlpha(UITheme.Border, 0.55f));
            btn.onClick.AddListener(onClick);

            var labelGO = new GameObject("Label");
            labelGO.transform.SetParent(go.transform, false);
            var labelRT = labelGO.AddComponent<RectTransform>();
            labelRT.anchorMin = Vector2.zero;
            labelRT.anchorMax = Vector2.one;
            labelRT.offsetMin = labelRT.offsetMax = Vector2.zero;

            var label = labelGO.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = 14;
            label.alignment = TextAlignmentOptions.Center;
            label.fontStyle = FontStyles.Bold;
            UITheme.ApplyTmpText(label, UIThemeTextRole.Primary);

            var le = go.AddComponent<LayoutElement>();
            le.flexibleWidth = 1;

            return btn;
        }

        private static void SetLayoutPreferredWidth(GameObject go, float width)
        {
            var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.flexibleWidth = 0;
        }

        private static void SetLayoutPreferredHeight(GameObject go, float height)
        {
            var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            le.preferredHeight = height;
        }
    }
}
