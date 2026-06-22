using UnityEngine;
using UnityEngine.UI;

namespace RailwayManager.SharedUI
{
    /// <summary>
    /// MUI-1 cleanup (M-UIPolish 2026-05-06): complementary partial UITheme.
    ///
    /// <para><b>Co dorzuca ten partial (vs dev2 partial w TopBarUI.cs):</b></para>
    /// - <b>Typography</b>: rozmiary fontów (H1=24, H2=20, H3=18, Body=14, Small=12, Tiny=10) + line height presets
    /// - <b>Spacing</b>: Tailwind-like scale (Xxs=2, Xs=4, Sm=8, Md=12, Lg=16, Xl=24, Xxl=32, Xxxl=48)
    /// - <b>Sizing</b>: button heights, input heights, panel widths, icon sizes, border thickness
    /// - <b>Transitions</b>: Fast/Default/Slow timing (50/100/200ms)
    /// - <b>Train run status colors</b>: StatusOnTime/SlightlyLate/Late/BrokenDown/Cancelled (use case M5/M9 specific)
    ///
    /// <para><b>Co NIE dorzuca:</b></para>
    /// Główna paleta (`AppBackground`/`PrimarySurface`/`SecondarySurface`/`PrimaryAccent`/`Success`/`Warning`/
    /// `Danger`/`Border`/`PrimaryText`/`SecondaryText`/`DisabledText`/etc.) jest w dev2 partial w `TopBarUI.cs`
    /// (z `FromHex(string)` helper). Plus apply helpers: `ApplyTmpText`, `ApplyButtonStyle`,
    /// `ApplyTmpInputField`, `WithAlpha`, `Darken`, `CreateColorBlock`, etc.
    ///
    /// <para><b>Use case:</b></para>
    /// <code>
    /// // Theme palette (z dev2 partial):
    /// image.color = UITheme.PrimaryAccent;
    /// label.color = UITheme.PrimaryText;
    ///
    /// // Train status (z tego partial):
    /// label.color = isOnTime ? UITheme.StatusOnTime : UITheme.StatusLate;
    ///
    /// // Typography (z tego partial):
    /// tmp.fontSize = UITheme.Typography.Body;
    ///
    /// // Spacing/Sizing (z tego partial):
    /// layout.spacing = UITheme.Spacing.Md;
    /// rt.sizeDelta = new Vector2(rt.sizeDelta.x, UITheme.Sizing.ButtonMedium);
    /// </code>
    /// </summary>
    public static partial class UITheme
    {
        // ─────────────────────────────────────────────
        //  Canvas scaler config (MUI-10 standard)
        // ─────────────────────────────────────────────

        /// <summary>
        /// MUI-10 (M-UIPolish 2026-05-06): standardowa reference resolution dla wszystkich canvasów
        /// w projekcie (1920×1080, match 0.5). Default Unity (800×600) powodował UI 2.4× za duże
        /// na 1080p monitor — bug w 13+ plikach które nie ustawiały explicit referenceResolution.
        /// </summary>
        public static readonly Vector2 CanvasReferenceResolution = new Vector2(1920f, 1080f);

        /// <summary>MUI-10: balanced match (0.5 = average width/height) — works for 16:9 + 21:9 + 16:10.</summary>
        public const float CanvasMatchWidthOrHeight = 0.5f;

        /// <summary>
        /// MUI-10: aplikuje standard canvas scaler config (ScaleWithScreenSize, ref 1920×1080, match 0.5).
        /// Wywoływać na każdym <see cref="CanvasScaler"/> tworzonym proceduralnie zamiast manual config.
        ///
        /// <para><b>Use case:</b></para>
        /// <code>
        /// var scaler = canvasGo.AddComponent&lt;CanvasScaler&gt;();
        /// UITheme.ApplyCanvasScaler(scaler);
        /// </code>
        /// </summary>
        public static void ApplyCanvasScaler(CanvasScaler scaler)
        {
            if (scaler == null) return;
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = CanvasReferenceResolution;
            // Explicit screenMatchMode — default is MatchWidthOrHeight ale jeśli ktoś
            // wcześniej ustawił Expand/Shrink, helper musi to nadpisać (defense in depth).
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = CanvasMatchWidthOrHeight;
        }

        // ─────────────────────────────────────────────
        //  Train run / category status colors (M5/M9 specific)
        // ─────────────────────────────────────────────

        /// <summary>Pociąg jedzie wg planu (delay ≤ 5 min).</summary>
        public static readonly Color StatusOnTime = StatusFromHex("4CAF50");
        /// <summary>Pociąg z opóźnieniem 0-5 min (yellow flag).</summary>
        public static readonly Color StatusSlightlyLate = StatusFromHex("FFC107");
        /// <summary>Pociąg z opóźnieniem &gt;5 min (orange).</summary>
        public static readonly Color StatusLate = StatusFromHex("FF9800");
        /// <summary>Awaria, BrokenDown, AwaitingRescue (pink/red).</summary>
        public static readonly Color StatusBrokenDown = StatusFromHex("E91E63");
        /// <summary>Cancelled / despawned przed completion (red).</summary>
        public static readonly Color StatusCancelled = StatusFromHex("F44336");

        /// <summary>Helper analogiczny do dev2 `FromHex(string)`, separate name żeby nie kolidował przy partial compile order.</summary>
        static Color StatusFromHex(string hex)
        {
            if (string.IsNullOrEmpty(hex) || hex.Length < 6) return Color.magenta;
            int r = System.Convert.ToInt32(hex.Substring(0, 2), 16);
            int g = System.Convert.ToInt32(hex.Substring(2, 2), 16);
            int b = System.Convert.ToInt32(hex.Substring(4, 2), 16);
            return new Color(r / 255f, g / 255f, b / 255f, 1f);
        }

        // ─────────────────────────────────────────────
        //  Typography — font sizes (px)
        // ─────────────────────────────────────────────

        public static class Typography
        {
            /// <summary>Panel titles, modal'e (24px).</summary>
            public const float H1 = 24f;
            /// <summary>Section headers (20px).</summary>
            public const float H2 = 20f;
            /// <summary>Sub-section, list group (18px).</summary>
            public const float H3 = 18f;
            /// <summary>Default body text, button labels (14px).</summary>
            public const float Body = 14f;
            /// <summary>Captions, hints, timestamps (12px).</summary>
            public const float Small = 12f;
            /// <summary>Debug overlays, micro labels (10px).</summary>
            public const float Tiny = 10f;

            /// <summary>Default line spacing multiplier dla TMP.</summary>
            public const float LineHeightDefault = 1.2f;
            /// <summary>Tighter line spacing (compact lists).</summary>
            public const float LineHeightTight = 1.0f;
            /// <summary>Larger line spacing (prose / readable).</summary>
            public const float LineHeightLoose = 1.5f;
        }

        // ─────────────────────────────────────────────
        //  Spacing — Tailwind-like scale (px)
        // ─────────────────────────────────────────────

        public static class Spacing
        {
            /// <summary>2px — ultra-tight gaps (dense list rows, inline chips). Dodane w MUI-13.</summary>
            public const float Xxs = 2f;
            /// <summary>4px — micro gaps (icon padding).</summary>
            public const float Xs = 4f;
            /// <summary>8px — small gaps (button internal).</summary>
            public const float Sm = 8f;
            /// <summary>12px — medium-small (form field gaps).</summary>
            public const float Md = 12f;
            /// <summary>16px — default panel padding, list item gaps.</summary>
            public const float Lg = 16f;
            /// <summary>24px — section spacing.</summary>
            public const float Xl = 24f;
            /// <summary>32px — major panel separations.</summary>
            public const float Xxl = 32f;
            /// <summary>48px — landmark sections (modal margins).</summary>
            public const float Xxxl = 48f;
        }

        // ─────────────────────────────────────────────
        //  Sizing — component dimensions (px)
        // ─────────────────────────────────────────────

        public static class Sizing
        {
            // ── Buttons ──
            /// <summary>Small button height (28px) — toolbar, inline.</summary>
            public const float ButtonSmall = 28f;
            /// <summary>Medium button height (36px) — default.</summary>
            public const float ButtonMedium = 36f;
            /// <summary>Large button height (44px) — primary CTAs.</summary>
            public const float ButtonLarge = 44f;

            // ── Inputs ──
            /// <summary>Input field height (32px).</summary>
            public const float InputHeight = 32f;
            /// <summary>Dropdown height (32px).</summary>
            public const float DropdownHeight = 32f;

            // ── Panels ──
            /// <summary>Min width sidebar/list panel (240px).</summary>
            public const float PanelSidebarMinWidth = 240f;
            /// <summary>Min width main panel (480px).</summary>
            public const float PanelMainMinWidth = 480f;
            /// <summary>Modal default width (640px).</summary>
            public const float ModalDefaultWidth = 640f;

            // ── Icons ──
            /// <summary>Mały icon (16px) — inline z tekstem.</summary>
            public const float IconSmall = 16f;
            /// <summary>Default icon (24px) — buttons, list items.</summary>
            public const float IconMedium = 24f;
            /// <summary>Duży icon (32px) — toolbar primary.</summary>
            public const float IconLarge = 32f;

            // ── Borders ──
            /// <summary>Default border thickness (1px).</summary>
            public const float BorderThin = 1f;
            /// <summary>Emphasis border (2px) — focused state.</summary>
            public const float BorderMedium = 2f;
        }

        // ─────────────────────────────────────────────
        //  Padding — RectOffset factory ze skali Spacing (MUI-13)
        // ─────────────────────────────────────────────

        /// <summary>RectOffset z jednakowym marginesem na 4 bokach. Karm stałymi <see cref="Spacing"/>.</summary>
        public static RectOffset Padding(float all)
        {
            int v = Mathf.RoundToInt(all);
            return new RectOffset(v, v, v, v);
        }

        /// <summary>RectOffset z osobnym poziomym (left=right) i pionowym (top=bottom) marginesem.</summary>
        public static RectOffset Padding(float horizontal, float vertical)
        {
            int h = Mathf.RoundToInt(horizontal);
            int v = Mathf.RoundToInt(vertical);
            return new RectOffset(h, h, v, v);
        }

        /// <summary>RectOffset z pełną kontrolą czterech boków (left, right, top, bottom).</summary>
        public static RectOffset Padding(float left, float right, float top, float bottom)
        {
            return new RectOffset(
                Mathf.RoundToInt(left), Mathf.RoundToInt(right),
                Mathf.RoundToInt(top), Mathf.RoundToInt(bottom));
        }

        // ─────────────────────────────────────────────
        //  Transitions — animation timing
        // ─────────────────────────────────────────────

        public static class Transitions
        {
            /// <summary>Quick transition (button press feedback) — 50ms.</summary>
            public const float Fast = 0.05f;
            /// <summary>Default transition (hover) — 100ms.</summary>
            public const float Default = 0.1f;
            /// <summary>Smooth transition (panel slide) — 200ms.</summary>
            public const float Slow = 0.2f;
        }
    }
}
