using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;
using RailwayManager.Core;
using RailwayManager.SharedUI;
using RailwayManager.SharedUI.Localization;

namespace MainMenu
{
    /// <summary>
    /// SettingsScreenUI — partial: row builders (Slider/IntSlider/Toggle/Dropdown/Heading/Info)
    /// + niskopoziomowe helpery (MakeRow, BuildSliderInternal).
    /// Wszystkie kontrolki hook'ują się do _working przez getter/setter delegate'y.
    /// </summary>
    public partial class SettingsScreenUI
    {
        // ═══════════════════════════════════════════════
        //  HEADING + INFO (dla podsekcji w "Ogólne")
        // ═══════════════════════════════════════════════

        /// <summary>Heading dla podsekcji (Rozgrywka / Interfejs / Rebind maps). Literal text
        /// (<paramref name="textOrKey"/> bez resolve'a) gdy <paramref name="isI18nKey"/>=false —
        /// fallback dla raw nazw (np. unknown action map). I18n rejestracja w _activeLabels
        /// tylko gdy key'em, żeby literal nie próbowało się tłumaczyć przy zmianie języka.</summary>
        private void HeadingRow(string textOrKey, bool isI18nKey = true)
        {
            var row = NewGO("Heading", _contentParent);
            row.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 46f);
            UITheme.ApplySurface(row.AddComponent<Image>(), HeadingBg, UIShapePreset.Panel);

            var hl = row.AddComponent<HorizontalLayoutGroup>();
            hl.padding = UITheme.Padding(UITheme.Spacing.Lg, UITheme.Spacing.Sm);
            hl.childAlignment = TextAnchor.MiddleLeft;

            var lbl = MakeTMP("Lbl", row.transform);
            lbl.text      = isI18nKey ? LocalizationService.Get(textOrKey) : textOrKey;
            lbl.fontSize  = 22;
            lbl.fontStyle = FontStyles.Bold;
            lbl.color     = Accent;
            lbl.raycastTarget = false;
            FillRT(lbl.gameObject);

            if (isI18nKey)
                _activeLabels.Add((lbl, textOrKey));
        }

        /// <summary>Info text (placeholder note dla M13-2/M13-3/M12b feature'ów).</summary>
        private void InfoRow(string i18nKey)
        {
            var row = NewGO("Info", _contentParent);
            row.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 38f);
            UITheme.ApplySurface(row.AddComponent<Image>(), UITheme.WithAlpha(UITheme.TopBarInset, 0.9f), UIShapePreset.Inset);

            var hl = row.AddComponent<HorizontalLayoutGroup>();
            hl.padding = UITheme.Padding(UITheme.Spacing.Lg, UITheme.Spacing.Sm);
            hl.childAlignment = TextAnchor.MiddleLeft;

            var lbl = MakeTMP("Lbl", row.transform);
            lbl.text      = LocalizationService.Get(i18nKey);
            lbl.fontSize  = 14;
            lbl.fontStyle = FontStyles.Italic;
            lbl.color     = TextMuted;
            lbl.raycastTarget = false;
            FillRT(lbl.gameObject);

            _activeLabels.Add((lbl, i18nKey));
        }

        // ═══════════════════════════════════════════════
        //  KONTROLKI: SLIDER / TOGGLE / DROPDOWN
        // ═══════════════════════════════════════════════

        /// <summary>Slider z float [min, max], hook'owany do getter/setter w _working.</summary>
        private void SliderRow(string name, string i18nKey, float min, float max,
            Func<float> getter, Action<float> setter)
        {
            var lbl = MakeRow(name, LocalizationService.Get(i18nKey), out var row);
            _activeLabels.Add((lbl, i18nKey));

            var sliderGO = BuildSliderInternal(row.transform, min, max, getter(), false, out var valText);
            valText.text = getter().ToString("F2");

            var slider = sliderGO.GetComponent<Slider>();
            slider.onValueChanged.AddListener(v =>
            {
                setter(v);
                valText.text = v.ToString("F2");
            });
        }

        /// <summary>Slider z int [min, max] (whole numbers).</summary>
        private void IntSliderRow(string name, string i18nKey, int min, int max,
            Func<int> getter, Action<int> setter)
        {
            var lbl = MakeRow(name, LocalizationService.Get(i18nKey), out var row);
            _activeLabels.Add((lbl, i18nKey));

            var sliderGO = BuildSliderInternal(row.transform, min, max, getter(), true, out var valText);
            valText.text = getter().ToString();

            var slider = sliderGO.GetComponent<Slider>();
            slider.onValueChanged.AddListener(v =>
            {
                int i = Mathf.RoundToInt(v);
                setter(i);
                valText.text = i.ToString();
            });
        }

        /// <summary>Toggle on/off, hook'owany do getter/setter.</summary>
        private void ToggleRow(string name, string i18nKey,
            Func<bool> getter, Action<bool> setter)
        {
            var lbl = MakeRow(name, LocalizationService.Get(i18nKey), out var row);
            _activeLabels.Add((lbl, i18nKey));

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
            toggle.isOn    = getter();
            toggle.onValueChanged.AddListener(v => setter(v));

            var le = tGO.AddComponent<LayoutElement>();
            le.preferredWidth  = 36f;
            le.preferredHeight = 36f;
        }

        /// <summary>
        /// Dropdown z opcjami. <paramref name="optionKeys"/> to i18n keys per opcja
        /// (resolved przez LocalizationService.Get). Wartość = index.
        ///
        /// TMP_Dropdown wymaga template z item Toggle component — buduje się go ręcznie
        /// w <see cref="BuildDropdownTemplate"/> bo programmatic creation nie ma tego z prefab'a.
        ///
        /// Po zmianie języka dropdown options NIE są aktualizowane in-place — wymagany
        /// repopulate sekcji (handled w <see cref="OnLocaleChanged"/>).
        /// </summary>
        private void DropdownRow(string name, string i18nKey, string[] optionKeys,
            Func<int> getter, Action<int> setter)
        {
            var lbl = MakeRow(name, LocalizationService.Get(i18nKey), out var row);
            _activeLabels.Add((lbl, i18nKey));

            var ddGO = NewGO("Dropdown", row.transform);
            ddGO.GetComponent<RectTransform>().sizeDelta = new Vector2(260f, 36f);

            var ddImg = ddGO.AddComponent<Image>();
            UITheme.ApplySurface(ddImg, DropdownBg, UIShapePreset.Inset);

            var dd = ddGO.AddComponent<TMP_Dropdown>();
            dd.targetGraphic = ddImg;

            // Caption label (widoczna gdy dropdown zwinięty)
            var capLbl = MakeTMP("Label", ddGO.transform);
            capLbl.fontSize  = 16;
            capLbl.color     = TextPrimary;
            capLbl.alignment = TextAlignmentOptions.Left;
            capLbl.raycastTarget = false;
            var capRT = capLbl.GetComponent<RectTransform>();
            capRT.anchorMin = Vector2.zero;
            capRT.anchorMax = Vector2.one;
            capRT.offsetMin = new Vector2(10f, 2f);
            capRT.offsetMax = new Vector2(-30f, -2f);
            dd.captionText = capLbl;

            // Arrow indicator (▼) po prawej stronie
            var arrow = MakeTMP("Arrow", ddGO.transform);
            arrow.text      = "▼";
            arrow.fontSize  = 12;
            arrow.color     = TextMuted;
            arrow.alignment = TextAlignmentOptions.Center;
            arrow.raycastTarget = false;
            var arrowRT = arrow.GetComponent<RectTransform>();
            arrowRT.anchorMin = new Vector2(1f, 0f);
            arrowRT.anchorMax = new Vector2(1f, 1f);
            arrowRT.pivot     = new Vector2(1f, 0.5f);
            arrowRT.anchoredPosition = new Vector2(-8f, 0f);
            arrowRT.sizeDelta = new Vector2(20f, 0f);

            // Template (rozwijana lista)
            BuildDropdownTemplate(dd, ddGO.transform);

            // Options — resolved przez LocalizationService.Get per key
            dd.ClearOptions();
            var opts = new List<TMP_Dropdown.OptionData>();
            foreach (var optKey in optionKeys)
                opts.Add(new TMP_Dropdown.OptionData(LocalizationService.Get(optKey)));
            dd.AddOptions(opts);

            int initial = Mathf.Clamp(getter(), 0, optionKeys.Length - 1);
            dd.SetValueWithoutNotify(initial);
            dd.RefreshShownValue();
            dd.onValueChanged.AddListener(v => setter(v));

            // Rejestracja dla in-place language refresh (OnLocaleChanged → RefreshDropdownOptions).
            _activeDropdowns.Add((dd, optionKeys));

            var le = ddGO.AddComponent<LayoutElement>();
            le.preferredWidth  = 260f;
            le.preferredHeight = 36f;
        }

        /// <summary>
        /// Buduje template dla TMP_Dropdown — pełna struktura wymagana przez Unity:
        /// Template (ScrollRect) → Viewport (Mask) → Content → Item (Toggle).
        /// Wywoływane raz per dropdown przy tworzeniu DropdownRow.
        /// </summary>
        private void BuildDropdownTemplate(TMP_Dropdown dd, Transform parent)
        {
            // Template root — initially inactive, anchored bottom (rozwija w dół)
            var template = NewGO("Template", parent);
            template.SetActive(false);
            var templateImg = template.AddComponent<Image>();
            templateImg.color = UITheme.OverlayPanelStrong;
            var templateRT = template.GetComponent<RectTransform>();
            templateRT.anchorMin = new Vector2(0f, 0f);
            templateRT.anchorMax = new Vector2(1f, 0f);
            templateRT.pivot     = new Vector2(0.5f, 1f);
            templateRT.anchoredPosition = new Vector2(0f, 2f);
            templateRT.sizeDelta = new Vector2(0f, 200f); // max wysokość, scroll w środku

            var templateScroll = template.AddComponent<ScrollRect>();
            templateScroll.horizontal = false;
            templateScroll.vertical   = true;
            templateScroll.scrollSensitivity = 20f;

            // Viewport (mask)
            var vp = NewGO("Viewport", template.transform);
            vp.AddComponent<Image>().color = UITheme.WithAlpha(UITheme.PrimaryText, 0.01f);
            vp.AddComponent<Mask>().showMaskGraphic = false;
            var vpRT = vp.GetComponent<RectTransform>();
            vpRT.anchorMin = Vector2.zero;
            vpRT.anchorMax = Vector2.one;
            vpRT.offsetMin = Vector2.zero;
            vpRT.offsetMax = Vector2.zero;
            templateScroll.viewport = vpRT;

            // Content
            var content = NewGO("Content", vp.transform);
            var contentRT = content.GetComponent<RectTransform>();
            contentRT.anchorMin = new Vector2(0f, 1f);
            contentRT.anchorMax = new Vector2(1f, 1f);
            contentRT.pivot     = new Vector2(0.5f, 1f);
            contentRT.anchoredPosition = Vector2.zero;
            contentRT.sizeDelta = new Vector2(0f, 32f);
            templateScroll.content = contentRT;

            // Item (Toggle) — Unity klonuje to per opcję
            var item = NewGO("Item", content.transform);
            var itemRT = item.GetComponent<RectTransform>();
            itemRT.anchorMin = new Vector2(0f, 0.5f);
            itemRT.anchorMax = new Vector2(1f, 0.5f);
            itemRT.sizeDelta = new Vector2(0f, 32f);

            // Item Background (Toggle.targetGraphic)
            var itemBg = NewGO("Item Background", item.transform);
            var itemBgImg = itemBg.AddComponent<Image>();
            itemBgImg.color = UITheme.SecondarySurface;
            FillRT(itemBg);

            // Item Checkmark (Toggle.graphic) — pokazuje się dla zaznaczonej opcji
            var itemChk = NewGO("Item Checkmark", item.transform);
            var itemChkImg = itemChk.AddComponent<Image>();
            itemChkImg.color = Accent;
            var itemChkRT = itemChk.GetComponent<RectTransform>();
            itemChkRT.anchorMin = new Vector2(0f, 0.5f);
            itemChkRT.anchorMax = new Vector2(0f, 0.5f);
            itemChkRT.pivot     = new Vector2(0f, 0.5f);
            itemChkRT.anchoredPosition = new Vector2(8f, 0f);
            itemChkRT.sizeDelta = new Vector2(14f, 14f);

            // Item Label
            var itemLbl = MakeTMP("Item Label", item.transform);
            itemLbl.fontSize  = 16;
            itemLbl.color     = TextPrimary;
            itemLbl.alignment = TextAlignmentOptions.Left;
            itemLbl.raycastTarget = false;
            var itemLblRT = itemLbl.GetComponent<RectTransform>();
            itemLblRT.anchorMin = Vector2.zero;
            itemLblRT.anchorMax = Vector2.one;
            itemLblRT.offsetMin = new Vector2(28f, 0f);
            itemLblRT.offsetMax = new Vector2(-10f, 0f);

            // Toggle component (musi być na "Item" GO)
            var itemToggle = item.AddComponent<Toggle>();
            itemToggle.targetGraphic = itemBgImg;
            itemToggle.graphic       = itemChkImg;
            itemToggle.transition    = Selectable.Transition.ColorTint;
            var colors = itemToggle.colors;
            colors.normalColor      = UITheme.SecondarySurface;
            colors.highlightedColor = UITheme.RaisedSurface;
            colors.selectedColor    = UITheme.PrimarySurface;
            colors.pressedColor     = UITheme.Border;
            itemToggle.colors = colors;

            // Assign refs to dropdown
            dd.template  = templateRT;
            dd.itemText  = itemLbl;
        }

        /// <summary>
        /// Wiersz z przyciskiem akcji (efekt natychmiastowy, poza Apply/Cancel — wzorzec
        /// jak rebindy/DispatchPolicy). M11 AS-7: restart onboardingu asystenta.
        /// </summary>
        private void ActionRow(string name, string i18nKey, string buttonI18nKey, Action onClick)
        {
            var lbl = MakeRow(name, LocalizationService.Get(i18nKey), out var row);
            _activeLabels.Add((lbl, i18nKey));

            var btnGO = NewGO("ActionBtn", row.transform);
            var btnImg = btnGO.AddComponent<Image>();
            UITheme.ApplySurface(btnImg, DropdownBg, UIShapePreset.Inset);
            var btn = btnGO.AddComponent<Button>();
            btn.targetGraphic = btnImg;
            btn.onClick.AddListener(() => onClick?.Invoke());
            var le = btnGO.AddComponent<LayoutElement>();
            le.preferredWidth  = 160f;
            le.preferredHeight = 32f;

            var btnLbl = MakeTMP("Lbl", btnGO.transform);
            btnLbl.text      = LocalizationService.Get(buttonI18nKey);
            btnLbl.fontSize  = 15;
            btnLbl.color     = TextPrimary;
            btnLbl.alignment = TextAlignmentOptions.Center;
            btnLbl.raycastTarget = false;
            FillRT(btnLbl.gameObject);
            _activeLabels.Add((btnLbl, buttonI18nKey));
        }

        // ═══════════════════════════════════════════════
        //  REBIND ROW (M13-2b)
        // ═══════════════════════════════════════════════

        /// <summary>
        /// Wiersz dla pojedynczego binding'u w zakładce Sterowanie. Pokazuje:
        /// label akcji + binding part name + display string aktualnego klawisza
        /// + button "Zmień..." + button reset (text "R" — patrz BUG-006).
        /// </summary>
        private void RebindRow(string actionLabel, string bindingPartLabel,
            InputAction action, int bindingIndex,
            Action onRebindClick, Action onResetClick)
        {
            var row = NewGO($"Rebind_{action.name}_{bindingIndex}", _contentParent);
            row.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 44f);
            UITheme.ApplySurface(row.AddComponent<Image>(), RowBg, UIShapePreset.Panel);

            var hl = row.AddComponent<HorizontalLayoutGroup>();
            hl.padding  = UITheme.Padding(UITheme.Spacing.Lg, UITheme.Spacing.Sm);
            hl.spacing  = UITheme.Spacing.Md;
            hl.childAlignment      = TextAnchor.MiddleLeft;
            // 2026-05-17: childControlWidth false→true (childControlHeight też). Bez tego
            // klawisz literka (DisplayShell preferredWidth=180) + button "Zmień"
            // (changeLE.preferredWidth=100) + ResetBtn (36) nie miały poprawnej szerokości
            // → nakładały się na siebie.
            hl.childControlWidth   = true;
            hl.childControlHeight  = true;
            hl.childForceExpandWidth  = false;
            hl.childForceExpandHeight = false;

            // Label "AkcjaName / Part" (np. "Pan / W")
            var lbl = MakeTMP("Lbl", row.transform);
            lbl.text     = string.IsNullOrEmpty(bindingPartLabel)
                ? actionLabel
                : $"{actionLabel} / {bindingPartLabel}";
            lbl.fontSize = 16;
            lbl.color    = TextPrimary;
            lbl.raycastTarget = false;
            var lblLE = lbl.gameObject.AddComponent<LayoutElement>();
            lblLE.preferredWidth  = 320f;
            lblLE.preferredHeight = 32f;

            // Display: current binding (np. "W", "Mouse Left", "Tab")
            string displayStr = "—";
            try
            {
                displayStr = action.GetBindingDisplayString(bindingIndex,
                    InputBinding.DisplayStringOptions.DontUseShortDisplayNames);
                if (string.IsNullOrEmpty(displayStr)) displayStr = "—";
            }
            catch (Exception e)
            {
                Log.Warn($"[RebindRow] GetBindingDisplayString failed for {action.name}[{bindingIndex}]: {e.Message}");
            }

            var displayGO = NewGO("DisplayShell", row.transform);
            UITheme.ApplySurface(displayGO.AddComponent<Image>(), UITheme.WithAlpha(UITheme.TopBarInset, 0.94f), UIShapePreset.Inset);
            var dispLE = displayGO.AddComponent<LayoutElement>();
            dispLE.preferredWidth  = 180f;
            dispLE.preferredHeight = 32f;
            var displayLbl = MakeTMP("Display", displayGO.transform);
            displayLbl.text     = displayStr;
            displayLbl.fontSize = 16;
            displayLbl.fontStyle = FontStyles.Bold;
            displayLbl.color    = Accent;
            displayLbl.alignment = TextAlignmentOptions.Center;
            displayLbl.raycastTarget = false;
            FillRT(displayLbl.gameObject);

            // "Zmień..." button
            var changeGO = NewGO("ChangeBtn", row.transform);
            var changeImg = changeGO.AddComponent<Image>();
            UITheme.ApplySurface(changeImg, DropdownBg, UIShapePreset.Inset);
            var changeBtn = changeGO.AddComponent<Button>();
            changeBtn.targetGraphic = changeImg;
            changeBtn.onClick.AddListener(() => onRebindClick?.Invoke());
            var changeLE = changeGO.AddComponent<LayoutElement>();
            changeLE.preferredWidth  = 100f;
            changeLE.preferredHeight = 32f;

            var changeLbl = MakeTMP("Lbl", changeGO.transform);
            changeLbl.text     = LocalizationService.Get("settings.rebind.change");
            changeLbl.fontSize = 14;
            changeLbl.color    = TextPrimary;
            changeLbl.alignment = TextAlignmentOptions.Center;
            changeLbl.raycastTarget = false;
            FillRT(changeLbl.gameObject);

            // Reset button — procedural rotation arrow icon (BUG-006).
            // Żaden z 4 TTF projektu nie zawiera glyphu U+21BA, więc rysujemy ikonę
            // programatycznie przez IconGenerator (Texture2D + Sprite cache, single-instance).
            // Ostateczna proper PNG icon planowana w M-UIPolish MUI-11 (Unity AI Generators).
            var resetGO = NewGO("ResetBtn", row.transform);
            var resetImg = resetGO.AddComponent<Image>();
            UITheme.ApplySurface(resetImg, ToggleBg, UIShapePreset.Pill);
            var resetBtn = resetGO.AddComponent<Button>();
            resetBtn.targetGraphic = resetImg;
            resetBtn.onClick.AddListener(() => onResetClick?.Invoke());
            var resetLE = resetGO.AddComponent<LayoutElement>();
            resetLE.preferredWidth  = 36f;
            resetLE.preferredHeight = 32f;

            // Icon overlay — child Image z procedural sprite, kolor TextMuted (theme-aware tint)
            var iconGO = NewGO("Icon", resetGO.transform);
            var iconImg = iconGO.AddComponent<Image>();
            iconImg.sprite = IconGenerator.GetResetSprite();
            iconImg.color = TextMuted;
            iconImg.preserveAspect = true;
            iconImg.raycastTarget = false;
            var iconRT = iconGO.GetComponent<RectTransform>();
            iconRT.anchorMin = new Vector2(0.5f, 0.5f);
            iconRT.anchorMax = new Vector2(0.5f, 0.5f);
            iconRT.pivot     = new Vector2(0.5f, 0.5f);
            iconRT.anchoredPosition = Vector2.zero;
            iconRT.sizeDelta = new Vector2(20f, 20f);
        }

        // ═══════════════════════════════════════════════
        //  ROW PRIMITIVES
        // ═══════════════════════════════════════════════

        /// <summary>Tworzy poziomy wiersz z labelem po lewej; zwraca TMP labela.</summary>
        private TextMeshProUGUI MakeRow(string name, string labelText, out GameObject row)
        {
            row = NewGO(name, _contentParent);
            row.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 52f);
            UITheme.ApplySurface(row.AddComponent<Image>(), RowBg, UIShapePreset.Panel);

            var hl = row.AddComponent<HorizontalLayoutGroup>();
            hl.padding  = UITheme.Padding(UITheme.Spacing.Lg, UITheme.Spacing.Sm);
            hl.spacing  = UITheme.Spacing.Xl;
            hl.childAlignment      = TextAnchor.MiddleLeft;
            // 2026-05-17: childControlWidth false→true (childControlHeight też).
            // Master row builder dla wszystkich sekcji Settings (Sterowanie/Grafika/Dźwięk/Język/Ogólne).
            // Bez tego label/slider/dropdown/value mieli default sizeDelta=100 → labels ucięte,
            // slidery wąskie (thumb przy lewej krawędzi).
            hl.childControlWidth   = true;
            hl.childControlHeight  = true;
            hl.childForceExpandWidth  = false;
            hl.childForceExpandHeight = false;

            var lbl = MakeTMP("Lbl", row.transform);
            lbl.text     = labelText;
            lbl.fontSize = 18;
            lbl.color    = TextPrimary;
            lbl.raycastTarget = false;
            var le = lbl.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth  = 380f;
            le.preferredHeight = 36f;
            return lbl;
        }

        /// <summary>Buduje slider z value text. Zwraca slider GO; valText jako out.</summary>
        private GameObject BuildSliderInternal(Transform parent, float min, float max, float val,
            bool wholeNumbers, out TextMeshProUGUI valText)
        {
            var sliderGO = NewGO("Slider", parent);

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
            // 2026-05-17: offsets symmetric z Handle Slide Area (oba 10/-10) żeby
            // fill visual matched thumb position. Wcześniej 5/-15 asymetria powodowała
            // że niebieski fill rozszerzał się 5px w lewo poza thumb track + kończył
            // się 5px za thumb na prawo → thumb visualnie nie był na granicy fill/track.
            faRT.offsetMin = new Vector2(10f, 0f);
            faRT.offsetMax = new Vector2(-10f, 0f);

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

            // 2026-05-17: explicit handle anchors center (0.5, 0.5). Bez tego handle dziedziczyło
            // anchors z NewGO defaults i Slider component NIE wymusza anchors handle przy runtime
            // (manipuluje tylko anchoredPosition.x). Skutek: handle rect stretched do całej HSA →
            // thumb wyglądał jak podłużny prostokąt zamiast kółka 20×20.
            var handle = NewGO("Handle", ha.transform);
            var handleRT = handle.GetComponent<RectTransform>();
            handleRT.anchorMin = new Vector2(0.5f, 0.5f);
            handleRT.anchorMax = new Vector2(0.5f, 0.5f);
            handleRT.pivot = new Vector2(0.5f, 0.5f);
            handleRT.sizeDelta = new Vector2(20f, 20f);
            var handleImg = handle.AddComponent<Image>();
            UITheme.ApplySurface(handleImg, UITheme.RaisedSurface, UIShapePreset.Pill);

            var sliderComp = sliderGO.AddComponent<Slider>();
            sliderComp.minValue      = min;
            sliderComp.maxValue      = max;
            sliderComp.wholeNumbers  = wholeNumbers;
            sliderComp.value         = val;
            sliderComp.fillRect      = fillRT;
            sliderComp.handleRect    = handle.GetComponent<RectTransform>();
            sliderComp.targetGraphic = handleImg;

            var le = sliderGO.AddComponent<LayoutElement>();
            le.preferredWidth  = 260f;
            le.preferredHeight = 36f;

            // Value text on the right
            var valGO = NewGO("Value", parent);
            var valLe = valGO.AddComponent<LayoutElement>();
            valLe.preferredWidth = 60f;
            valLe.preferredHeight = 36f;
            valText = valGO.AddComponent<TextMeshProUGUI>();
            valText.color = Accent;
            valText.fontSize = 16;
            valText.fontStyle = FontStyles.Bold;
            valText.alignment = TextAlignmentOptions.Center;

            return sliderGO;
        }

        private void AddSpacer(float height)
        {
            var spacer = NewGO("Spacer", _contentParent);
            spacer.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, height);
            spacer.AddComponent<LayoutElement>().preferredHeight = height;
        }
    }
}
