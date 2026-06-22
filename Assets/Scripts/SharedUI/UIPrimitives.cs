using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RailwayManager.SharedUI
{
    /// <summary>
    /// Low-level helpery do proceduralnego budowania UI — generic, bez opinii o sizingu.
    ///
    /// <para><b>Cel:</b> jeden source-of-truth dla operacji które wcześniej były zduplikowane
    /// w <c>MainMenu.MenuScreenPrimitives</c> + <c>DepotSystem.DepotUIPanelPrimitives</c>
    /// (NewGO/Stretch/Fill/MakeTMP). Domain-specific wzorce (DepotOptionButton,
    /// MenuTopBar, BuildVerticalScrollArea) zostają w swoich asmdef bo są związane
    /// z konkretnym layoutem.</para>
    ///
    /// <para><b>Różnica vs <see cref="UIBuilders"/>:</b> <c>UIPrimitives</c> to "low-level
    /// LEGO bricks" (sam GameObject + RectTransform + Stretch + TMP creation), bez sizingu.
    /// <c>UIBuilders</c> to "themed components" (button/label/input z UITheme.Sizing).
    /// Większość paneli potrzebuje LEGO bricks + własne sizingu z layoutu — używa
    /// UIPrimitives. Tylko gdy pasuje preset z UITheme.Sizing → UIBuilders.</para>
    ///
    /// <para><b>Use case:</b></para>
    /// <code>
    /// var row = UIPrimitives.NewGO("Row", parent);
    /// row.AddComponent&lt;HorizontalLayoutGroup&gt;();
    /// var label = UIPrimitives.MakeTMP("Label", row.transform, fontSize: 14f);
    /// label.text = "Nazwa stacji:";
    /// </code>
    /// </summary>
    public static class UIPrimitives
    {
        /// <summary>
        /// Tworzy pusty GameObject z <see cref="RectTransform"/> jako dziecko parent.
        /// Wcześniej zduplikowane jako <c>private static NewGO</c> w 4+ plikach UI.
        /// </summary>
        public static GameObject NewGO(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            return go;
        }

        /// <summary>
        /// Rozciąga <see cref="RectTransform"/> na cały parent: anchors (0,0)..(1,1), offsets 0.
        /// Null-safe.
        /// </summary>
        public static void Stretch(RectTransform rectTransform)
        {
            if (rectTransform == null)
                return;

            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;
        }

        /// <summary>
        /// Alias <see cref="Stretch"/> przyjmujący GameObject. Null-safe (no-op gdy GameObject nie ma RectTransform).
        /// </summary>
        public static void Fill(GameObject go)
        {
            if (go == null) return;
            Stretch(go.GetComponent<RectTransform>());
        }

        /// <summary>
        /// Tworzy <see cref="TextMeshProUGUI"/> z theme font + role color + ustawieniami typowymi dla UI label'a
        /// (raycastTarget=false, NoWrap). Wcześniej zduplikowane w MenuScreenPrimitives.CreateTMP/MakeTMP
        /// + DepotUIPanelPrimitives.CreateTMP.
        /// </summary>
        /// <param name="fontSize">Rozmiar fontu (px). Default 18.</param>
        /// <param name="role">Theme role kolorystyczne (mapowane na <see cref="UITheme.GetTextColor"/>).</param>
        /// <param name="alignment">TMP alignment. Default Left.</param>
        /// <param name="style">TMP font style. Default Normal.</param>
        public static TextMeshProUGUI MakeTMP(
            string name,
            Transform parent,
            float fontSize = 18f,
            UIThemeTextRole role = UIThemeTextRole.Primary,
            TextAlignmentOptions alignment = TextAlignmentOptions.Left,
            FontStyles style = FontStyles.Normal)
        {
            var go = NewGO(name, parent);

            var tmp = go.AddComponent<TextMeshProUGUI>();
            UITheme.ApplyTmpText(tmp, role);
            tmp.fontSize = fontSize;
            tmp.fontStyle = style;
            tmp.alignment = alignment;
            tmp.raycastTarget = false;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;

            return tmp;
        }

        // ─────────────────────────────────────────────
        //  TD-029: helpery przeciw powtarzającym się bug-patternom layoutu
        // ─────────────────────────────────────────────

        /// <summary>
        /// Anchored "top-stretch" dla zawartości scrolla: pełna szerokość parenta, rośnie w DÓŁ
        /// od górnej krawędzi (pivot y=1). Wysokość zwykle steruje <c>ContentSizeFitter</c>/LayoutGroup.
        /// Eliminuje bug-pattern (sizeDelta nie wyzerowane / zła kotwica → content nie wypełnia
        /// szerokości lub rośnie w złą stronę). Null-safe.
        /// </summary>
        public static RectTransform StretchTop(GameObject go)
            => go != null ? StretchTop(go.GetComponent<RectTransform>()) : null;

        /// <summary>Wariant <see cref="StretchTop(GameObject)"/> na RectTransform. Null-safe.</summary>
        public static RectTransform StretchTop(RectTransform rt)
        {
            if (rt == null) return null;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = Vector2.zero;
            rt.anchoredPosition = Vector2.zero;
            return rt;
        }

        /// <summary>
        /// Dodaje <see cref="HorizontalLayoutGroup"/> na ISTNIEJĄCY GameObject z
        /// <c>childControlWidth/Height=true</c> (najczęściej zapomniany default → dzieci nie dostają
        /// wymiarów z layoutu). Low-level: bez theme padding. Dla themed kontenera (theme spacing/
        /// padding + nowy GO) użyj <see cref="UIBuilders.MakeContainer"/>.
        /// </summary>
        public static HorizontalLayoutGroup AddHLG(GameObject go, float spacing = 0f,
            bool controlWidth = true, bool controlHeight = true,
            bool forceExpandWidth = false, bool forceExpandHeight = false)
        {
            var hlg = go.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = spacing;
            hlg.childControlWidth = controlWidth;
            hlg.childControlHeight = controlHeight;
            hlg.childForceExpandWidth = forceExpandWidth;
            hlg.childForceExpandHeight = forceExpandHeight;
            return hlg;
        }

        /// <summary>Pionowy wariant <see cref="AddHLG"/> — <see cref="VerticalLayoutGroup"/> z childControl=true.</summary>
        public static VerticalLayoutGroup AddVLG(GameObject go, float spacing = 0f,
            bool controlWidth = true, bool controlHeight = true,
            bool forceExpandWidth = false, bool forceExpandHeight = false)
        {
            var vlg = go.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = spacing;
            vlg.childControlWidth = controlWidth;
            vlg.childControlHeight = controlHeight;
            vlg.childForceExpandWidth = forceExpandWidth;
            vlg.childForceExpandHeight = forceExpandHeight;
            return vlg;
        }

        /// <summary>
        /// Buduje funkcjonalny <see cref="Slider"/> (BG + Fill Area/Fill + Handle Slide Area/Handle)
        /// z POPRAWNYMI kotwicami — wyciągnięte ze sprawdzonego <c>SettingsScreenUI.BuildSliderInternal</c>
        /// (fixy 2026-05-17): symmetric offsets 10/-10 Fill Area ↔ Handle Slide Area (fill wizualnie
        /// matchuje thumb) + explicit handle anchors center (0.5,0.5) — bez nich Slider stretchuje handle
        /// do podłużnego prostokąta zamiast kółka. Theme surfaces jako sensowne defaulty (caller może
        /// przemalować Image'e). <b>NIE ustawia sizeDelta/LayoutElement</b> — caller sizuje (konwencja jak UIBuilders).
        /// </summary>
        public static Slider MakeSlider(string name, Transform parent, float min, float max, float value,
            bool wholeNumbers = false)
        {
            var sliderGO = NewGO(name, parent);

            var bg = NewGO("BG", sliderGO.transform);
            UITheme.ApplySurface(bg.AddComponent<Image>(), UITheme.RaisedSurface, UIShapePreset.Pill);
            var bgRT = (RectTransform)bg.transform;
            bgRT.anchorMin = new Vector2(0f, 0.25f);
            bgRT.anchorMax = new Vector2(1f, 0.75f);
            bgRT.offsetMin = Vector2.zero;
            bgRT.offsetMax = Vector2.zero;

            var fa = NewGO("Fill Area", sliderGO.transform);
            var faRT = (RectTransform)fa.transform;
            faRT.anchorMin = new Vector2(0f, 0.25f);
            faRT.anchorMax = new Vector2(1f, 0.75f);
            faRT.offsetMin = new Vector2(10f, 0f);
            faRT.offsetMax = new Vector2(-10f, 0f);

            var fill = NewGO("Fill", fa.transform);
            UITheme.ApplySurface(fill.AddComponent<Image>(), UITheme.PrimaryAccent, UIShapePreset.Pill);
            var fillRT = (RectTransform)fill.transform;
            fillRT.sizeDelta = Vector2.zero;

            var ha = NewGO("Handle Slide Area", sliderGO.transform);
            var haRT = (RectTransform)ha.transform;
            haRT.anchorMin = Vector2.zero;
            haRT.anchorMax = Vector2.one;
            haRT.offsetMin = new Vector2(10f, 0f);
            haRT.offsetMax = new Vector2(-10f, 0f);

            var handle = NewGO("Handle", ha.transform);
            var handleRT = (RectTransform)handle.transform;
            handleRT.anchorMin = new Vector2(0.5f, 0.5f);
            handleRT.anchorMax = new Vector2(0.5f, 0.5f);
            handleRT.pivot = new Vector2(0.5f, 0.5f);
            handleRT.sizeDelta = new Vector2(20f, 20f);
            var handleImg = handle.AddComponent<Image>();
            UITheme.ApplySurface(handleImg, UITheme.RaisedSurface, UIShapePreset.Pill);

            var slider = sliderGO.AddComponent<Slider>();
            slider.minValue = min;
            slider.maxValue = max;
            slider.wholeNumbers = wholeNumbers;
            slider.value = Mathf.Clamp(value, min, max);
            slider.fillRect = fillRT;
            slider.handleRect = handleRT;
            slider.targetGraphic = handleImg;

            return slider;
        }
    }
}
