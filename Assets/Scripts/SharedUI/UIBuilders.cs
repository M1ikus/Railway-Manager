using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RailwayManager.SharedUI
{
    /// <summary>
    /// Themed components builders — wrapper'y na <see cref="UIPrimitives"/> + <see cref="UITheme"/>
    /// theme application. W odróżnieniu od <c>UIPrimitives</c> (low-level LEGO bricks),
    /// <c>UIBuilders</c> zwraca elementy z aplikowanym theme'em (kolor surface'u, typografia,
    /// button color block).
    ///
    /// <para><b>Zasada (Krok 2 adoption pass 2026-05-15):</b> żaden builder NIE ustawia sizeDelta
    /// — caller decyduje czy używa LayoutElement, anchors, czy explicit sizeDelta. Wcześniejsze
    /// MakeButton hardcode'owało sizeDelta=(160, 36) co konfliktowało z każdym panelem.</para>
    ///
    /// <para><b>Co skipnięte:</b></para>
    /// <list type="bullet">
    ///   <item><c>MakeInputField</c> — 60 LOC skomplikowanej implementacji, nikt nie używał. TopBarUI/MenuScreen
    ///     mają własne CreateInputField z viewport+placeholder layoutem.</item>
    ///   <item><c>ButtonSize</c> enum — caller ustawia size jak woli (LayoutElement.preferredWidth itp.).</item>
    /// </list>
    ///
    /// <para><b>Use case:</b></para>
    /// <code>
    /// var panel = UIBuilders.MakePanel(parent, UIBuilders.PanelRole.Background);
    /// var container = UIBuilders.MakeContainer(panel.transform, UIBuilders.ContainerLayout.Vertical);
    /// var title = UIBuilders.MakeLabel(container, "Wybierz tabor:", UIBuilders.TypographyRole.H2);
    /// var btn = UIBuilders.MakeButton(container, "OK", UIButtonTone.Primary);
    /// btn.GetComponent&lt;LayoutElement&gt;().preferredHeight = 36f; // caller decyduje sizing
    /// </code>
    /// </summary>
    public static class UIBuilders
    {
        // ─────────────────────────────────────────────
        //  Enums
        // ─────────────────────────────────────────────

        public enum TypographyRole { H1, H2, H3, Body, Small, Tiny }

        public enum PanelRole
        {
            Background,  // PrimarySurface — main panel
            Subpanel,    // SecondarySurface — modal / sub
            Inset,       // RaisedSurface — input wells
        }

        public enum ContainerLayout { Vertical, Horizontal }

        // ─────────────────────────────────────────────
        //  Buttons (BEZ sizeDelta — caller decyduje)
        // ─────────────────────────────────────────────

        /// <summary>
        /// Tworzy themed button z text label. Wykorzystuje <see cref="UITheme.ApplyButtonStyle"/>
        /// dla ColorBlock + ApplySurface (rounded shape). <b>NIE ustawia sizeDelta</b> — caller
        /// dodaje LayoutElement lub manualnie.
        /// </summary>
        public static Button MakeButton(Transform parent, string text, UIButtonTone tone = UIButtonTone.Primary)
        {
            var go = new GameObject($"Button_{SafeName(text)}",
                typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);

            var img = go.GetComponent<Image>();
            var btn = go.GetComponent<Button>();
            UITheme.ApplyButtonStyle(btn, img, tone, UIShapePreset.Button);
            go.AddComponent<ButtonPressFeedback>(); // MUI-11: subtelny press feel

            // TMP label as child, fills full button rect
            var textRole = UITheme.GetButtonTextRole(tone);
            var label = MakeLabel(go.transform, text, TypographyRole.Body, UITheme.GetTextColor(textRole));
            var labelRt = label.rectTransform;
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = new Vector2(UITheme.Spacing.Md, 0f);
            labelRt.offsetMax = new Vector2(-UITheme.Spacing.Md, 0f);
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;

            return btn;
        }

        /// <summary>
        /// Tworzy icon button (sprite-only, optional tooltip auto-attach). <b>NIE ustawia sizeDelta.</b>
        /// </summary>
        public static Button MakeIconButton(Transform parent, Sprite icon, UIButtonTone tone = UIButtonTone.Secondary, string tooltipText = null)
        {
            var go = new GameObject($"IconButton_{SafeName(icon != null ? icon.name : "empty")}",
                typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);

            var img = go.GetComponent<Image>();
            var btn = go.GetComponent<Button>();
            UITheme.ApplyButtonStyle(btn, img, tone, UIShapePreset.Button);
            go.AddComponent<ButtonPressFeedback>(); // MUI-11: subtelny press feel

            // Icon image as child, centered
            if (icon != null)
            {
                var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
                iconGo.transform.SetParent(go.transform, false);
                var iconImg = iconGo.GetComponent<Image>();
                iconImg.sprite = icon;
                iconImg.preserveAspect = true;
                var textRole = UITheme.GetButtonTextRole(tone);
                iconImg.color = UITheme.GetTextColor(textRole);
                iconImg.raycastTarget = false; // tooltip pointer events przechodzą do button parent
                var iconRt = (RectTransform)iconGo.transform;
                iconRt.anchorMin = new Vector2(0.5f, 0.5f);
                iconRt.anchorMax = new Vector2(0.5f, 0.5f);
                iconRt.sizeDelta = new Vector2(UITheme.Sizing.IconMedium, UITheme.Sizing.IconMedium);
                iconRt.anchoredPosition = Vector2.zero;
            }

            // MUI-3: auto-attach TooltipTrigger gdy tooltipText set
            if (!string.IsNullOrEmpty(tooltipText))
            {
                var trigger = go.AddComponent<TooltipTrigger>();
                trigger.text = tooltipText;
            }

            return btn;
        }

        /// <summary>
        /// MUI-3: helper do attach'u tooltip'a do dowolnego istniejącego UI elementu (poza UIBuilders flow).
        /// Wymaga że target ma Graphic z <c>raycastTarget=true</c>.
        /// </summary>
        public static TooltipTrigger AttachTooltip(GameObject target, string text, float hoverDelay = 0.5f)
        {
            if (target == null || string.IsNullOrEmpty(text)) return null;
            var trigger = target.GetComponent<TooltipTrigger>();
            if (trigger == null) trigger = target.AddComponent<TooltipTrigger>();
            trigger.text = text;
            trigger.hoverDelay = hoverDelay;
            return trigger;
        }

        // ─────────────────────────────────────────────
        //  Labels — delegate do UIPrimitives.MakeTMP z TypographyRole
        // ─────────────────────────────────────────────

        /// <summary>
        /// TMP_Text z theme typography size + role color. Cienki wrapper na
        /// <see cref="UIPrimitives.MakeTMP"/> mapujący <see cref="TypographyRole"/> →
        /// <see cref="UITheme.Typography"/> font size.
        /// </summary>
        public static TextMeshProUGUI MakeLabel(
            Transform parent,
            string text,
            TypographyRole role = TypographyRole.Body,
            Color? color = null)
        {
            var tmp = UIPrimitives.MakeTMP(parent: parent, name: $"Label_{SafeName(text)}",
                fontSize: GetTypographySize(role));
            tmp.text = text;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            if (color.HasValue)
                tmp.color = color.Value;
            return tmp;
        }

        // ─────────────────────────────────────────────
        //  Panels — rounded surface z theme palette
        // ─────────────────────────────────────────────

        /// <summary>
        /// Tworzy panel z rounded background (theme palette per <see cref="PanelRole"/>).
        /// <b>NIE ustawia sizeDelta</b> — caller decyduje.
        /// </summary>
        public static Image MakePanel(
            Transform parent,
            PanelRole role = PanelRole.Background,
            UIShapePreset shape = UIShapePreset.Panel)
        {
            var go = UIPrimitives.NewGO($"Panel_{role}", parent);
            var img = go.AddComponent<Image>();
            UITheme.ApplySurface(img, GetPanelColor(role), shape);
            return img;
        }

        // ─────────────────────────────────────────────
        //  Containers — z LayoutGroup, default spacing/padding z theme
        // ─────────────────────────────────────────────

        /// <summary>
        /// Tworzy GameObject z VerticalLayoutGroup lub HorizontalLayoutGroup + spacing/padding
        /// z theme. Default: <see cref="UITheme.Spacing.Md"/> padding, <see cref="UITheme.Spacing.Sm"/> spacing.
        /// </summary>
        public static RectTransform MakeContainer(
            Transform parent,
            ContainerLayout layout = ContainerLayout.Vertical,
            float? padding = null,
            float? spacing = null)
        {
            float pad = padding ?? UITheme.Spacing.Md;
            float spc = spacing ?? UITheme.Spacing.Sm;

            var go = UIPrimitives.NewGO($"Container_{layout}", parent);

            if (layout == ContainerLayout.Vertical)
            {
                var vlg = go.AddComponent<VerticalLayoutGroup>();
                vlg.padding = UITheme.Padding(pad);
                vlg.spacing = spc;
                vlg.childAlignment = TextAnchor.UpperLeft;
                vlg.childControlWidth = true;
                vlg.childControlHeight = false;
                vlg.childForceExpandWidth = true;
                vlg.childForceExpandHeight = false;
            }
            else
            {
                var hlg = go.AddComponent<HorizontalLayoutGroup>();
                hlg.padding = UITheme.Padding(pad);
                hlg.spacing = spc;
                hlg.childAlignment = TextAnchor.MiddleLeft;
                hlg.childControlWidth = false;
                hlg.childControlHeight = true;
                hlg.childForceExpandWidth = false;
                hlg.childForceExpandHeight = true;
            }

            return (RectTransform)go.transform;
        }

        // ─────────────────────────────────────────────
        //  Separators / dividers
        // ─────────────────────────────────────────────

        /// <summary>
        /// Separator line (horizontal lub vertical), color <see cref="UITheme.Border"/>.
        /// </summary>
        public static Image MakeSeparator(Transform parent, ContainerLayout orientation = ContainerLayout.Horizontal)
        {
            var go = UIPrimitives.NewGO("Separator", parent);
            var img = go.AddComponent<Image>();
            img.color = UITheme.Border;

            var rt = (RectTransform)go.transform;
            if (orientation == ContainerLayout.Horizontal)
            {
                rt.sizeDelta = new Vector2(0f, UITheme.Sizing.BorderThin);
                rt.anchorMin = new Vector2(0f, 0.5f);
                rt.anchorMax = new Vector2(1f, 0.5f);
            }
            else
            {
                rt.sizeDelta = new Vector2(UITheme.Sizing.BorderThin, 0f);
                rt.anchorMin = new Vector2(0.5f, 0f);
                rt.anchorMax = new Vector2(0.5f, 1f);
            }

            return img;
        }

        // ─────────────────────────────────────────────
        //  Helpers (private)
        // ─────────────────────────────────────────────

        static Color GetPanelColor(PanelRole role) => role switch
        {
            PanelRole.Background => UITheme.PrimarySurface,
            PanelRole.Subpanel => UITheme.SecondarySurface,
            PanelRole.Inset => UITheme.RaisedSurface,
            _ => UITheme.PrimarySurface,
        };

        static float GetTypographySize(TypographyRole role) => role switch
        {
            TypographyRole.H1 => UITheme.Typography.H1,
            TypographyRole.H2 => UITheme.Typography.H2,
            TypographyRole.H3 => UITheme.Typography.H3,
            TypographyRole.Body => UITheme.Typography.Body,
            TypographyRole.Small => UITheme.Typography.Small,
            TypographyRole.Tiny => UITheme.Typography.Tiny,
            _ => UITheme.Typography.Body,
        };

        /// <summary>Sanitize string dla GameObject.name (max 30 chars, no newlines).</summary>
        static string SafeName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "empty";
            string trimmed = s.Length > 30 ? s.Substring(0, 30) : s;
            return trimmed.Replace('\n', '_').Replace('\r', '_');
        }
    }
}
