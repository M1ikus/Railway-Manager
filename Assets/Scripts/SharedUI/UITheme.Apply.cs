using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RailwayManager.SharedUI
{
    /// <summary>
    /// MUI-1 partial UITheme — apply helpers (text/button/input field) + button color blocks.
    /// Wydzielone z <see cref="TopBarUI"/>.cs 2026-05-15.
    /// </summary>
    public static partial class UITheme
    {
        // ─────────────────────────────────────────────
        //  Text apply
        // ─────────────────────────────────────────────

        public static void ApplyTmpText(TMP_Text text, UIThemeTextRole role)
        {
            if (text == null)
                return;

            text.font = TmpFont;
            text.color = GetTextColor(role);
        }

        // ─────────────────────────────────────────────
        //  Button apply
        // ─────────────────────────────────────────────

        public static void ApplyButtonStyle(Button button, Image background, UIButtonTone tone, UIShapePreset shape = UIShapePreset.Pill)
        {
            if (button == null || background == null)
                return;

            ApplySurface(background, GetButtonNormalColor(tone), shape);
            button.targetGraphic = background;
            button.transition = Selectable.Transition.ColorTint;
            button.colors = CreateButtonColorBlock(tone);
        }

        public static UIThemeTextRole GetButtonTextRole(UIButtonTone tone)
        {
            return tone switch
            {
                UIButtonTone.Primary => UIThemeTextRole.Inverse,
                UIButtonTone.Secondary => UIThemeTextRole.Primary,
                UIButtonTone.Danger => UIThemeTextRole.Inverse,
                UIButtonTone.Success => UIThemeTextRole.Inverse,
                UIButtonTone.Ghost => UIThemeTextRole.Primary,
                _ => UIThemeTextRole.Primary,
            };
        }

        private static Color GetButtonNormalColor(UIButtonTone tone)
        {
            return tone switch
            {
                UIButtonTone.Primary => PrimaryAccent,
                UIButtonTone.Secondary => SecondarySurface,
                UIButtonTone.Danger => Danger,
                UIButtonTone.Success => Success,
                UIButtonTone.Ghost => Color.clear,
                _ => SecondarySurface,
            };
        }

        // ─────────────────────────────────────────────
        //  Input field apply
        // ─────────────────────────────────────────────

        /// <summary>
        /// MUI-5: TMP_InputField apply helper. Stosuje surface + color block +
        /// selection/caret colors, oraz aplikuje TmpFont na text + placeholder.
        /// (MUI-9.5: zastąpiło usunięte ApplyLegacyInputField z 2026-05-17.)
        /// </summary>
        public static void ApplyTmpInputField(TMP_InputField input, Image background, TMP_Text text, TMP_Text placeholder)
        {
            if (input == null || background == null)
                return;

            ApplySurface(background, TopBarInset, UIShapePreset.Inset);
            input.targetGraphic = background;
            input.transition = Selectable.Transition.ColorTint;
            input.colors = CreateInputColorBlock();
            input.selectionColor = WithAlpha(PrimaryAccent, 0.35f);
            input.customCaretColor = true;
            input.caretColor = PrimaryText;

            if (text != null)
            {
                text.font = TmpFont;
                text.color = PrimaryText;
            }

            if (placeholder != null)
            {
                placeholder.font = TmpFont;
                placeholder.color = SecondaryText;
            }
        }

        // ─────────────────────────────────────────────
        //  Color blocks
        // ─────────────────────────────────────────────

        public static ColorBlock CreateButtonColorBlock(UIButtonTone tone)
        {
            return tone switch
            {
                UIButtonTone.Primary => CreateColorBlock(
                    PrimaryAccent,
                    PrimaryAccentHover,
                    Darken(PrimaryAccentHover, 0.18f),
                    PrimaryAccent,
                    WithAlpha(Border, 0.55f)),
                UIButtonTone.Secondary => CreateColorBlock(
                    SecondarySurface,
                    RaisedSurface,
                    Border,
                    RaisedSurface,
                    WithAlpha(Border, 0.55f)),
                UIButtonTone.Danger => CreateColorBlock(
                    Danger,
                    Darken(Danger, 0.04f),
                    Darken(Danger, 0.12f),
                    Danger,
                    WithAlpha(Border, 0.55f)),
                UIButtonTone.Success => CreateColorBlock(
                    Success,
                    Darken(Success, 0.03f),
                    Darken(Success, 0.1f),
                    Success,
                    WithAlpha(Border, 0.55f)),
                UIButtonTone.Ghost => CreateColorBlock(
                    Color.clear,
                    WithAlpha(SecondarySurface, 0.82f),
                    WithAlpha(RaisedSurface, 0.92f),
                    WithAlpha(SecondarySurface, 0.9f),
                    WithAlpha(Border, 0.25f)),
                _ => CreateColorBlock(
                    SecondarySurface,
                    RaisedSurface,
                    Border,
                    RaisedSurface,
                    WithAlpha(Border, 0.55f)),
            };
        }

        public static ColorBlock CreateInputColorBlock()
        {
            return CreateColorBlock(
                TopBarInset,
                RaisedSurface,
                WithAlpha(Border, 0.95f),
                WithAlpha(PrimaryAccent, 0.2f),
                WithAlpha(Border, 0.55f));
        }

        public static ColorBlock CreateColorBlock(Color normal, Color highlighted, Color pressed, Color selected, Color disabled)
        {
            var colors = ColorBlock.defaultColorBlock;
            colors.normalColor = normal;
            colors.highlightedColor = highlighted;
            colors.pressedColor = pressed;
            colors.selectedColor = selected;
            colors.disabledColor = disabled;
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.1f;
            return colors;
        }
    }
}
