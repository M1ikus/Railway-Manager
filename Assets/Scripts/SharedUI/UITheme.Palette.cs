using UnityEngine;

namespace RailwayManager.SharedUI
{
    /// <summary>
    /// Rola tekstowa dla theme color lookup (`UITheme.GetTextColor`).
    /// Mapowanie na konkretne kolory palety w <see cref="UITheme"/>.
    /// </summary>
    public enum UIThemeTextRole
    {
        Primary,
        Secondary,
        Disabled,
        Inverse,
        Accent,
        Success,
        Warning,
        Danger,
    }

    /// <summary>
    /// Wariant kolorystyczny przycisku — dev2 paleta z `ApplyButtonStyle`/`CreateButtonColorBlock`.
    /// </summary>
    public enum UIButtonTone
    {
        Primary,
        Secondary,
        Danger,
        Success,
        Ghost
    }

    /// <summary>
    /// MUI-1 partial UITheme — paleta kolorów + base color helpers (FromHex/Darken/WithAlpha).
    /// Wcześniej osadzone w <see cref="TopBarUI"/>.cs (dev2 partial), wydzielone 2026-05-15
    /// żeby <see cref="TopBarUI"/> nie ważył 800 linii palette + HUD MonoBehaviour.
    /// </summary>
    public static partial class UITheme
    {
        // ─────────────────────────────────────────────
        //  Surfaces / backgrounds (dev2 dark palette)
        // ─────────────────────────────────────────────

        public static readonly Color AppBackground = FromHex("14141F");
        public static readonly Color PrimarySurface = FromHex("1F1F29");
        public static readonly Color SecondarySurface = FromHex("40404D");
        public static readonly Color RaisedSurface = FromHex("2E3340");
        public static readonly Color Border = FromHex("596273");

        // ─────────────────────────────────────────────
        //  Text colors (contrast levels)
        // ─────────────────────────────────────────────

        public static readonly Color PrimaryText = FromHex("EEF2F7");
        public static readonly Color SecondaryText = FromHex("B9C3CF");
        public static readonly Color DisabledText = FromHex("7F8995");
        public static readonly Color InverseText = Color.white;

        // ─────────────────────────────────────────────
        //  Accent + semantic status
        // ─────────────────────────────────────────────

        public static readonly Color PrimaryAccent = FromHex("4DB3FF");
        public static readonly Color PrimaryAccentHover = FromHex("3499EC");
        public static readonly Color Success = FromHex("77C16D");
        public static readonly Color Warning = FromHex("E3AA4C");
        public static readonly Color Danger = FromHex("D86E60");

        /// <summary>Focus ring / selected state color — alias dla <see cref="PrimaryAccent"/>, żeby zmiana akcentu propagowała się automatycznie do focus stylu.</summary>
        public static Color Focus => PrimaryAccent;

        // ─────────────────────────────────────────────
        //  TopBar / overlay tints (with alpha)
        // ─────────────────────────────────────────────

        public static readonly Color TopBarBackground = WithAlpha(FromHex("14141F"), 0.94f);
        public static readonly Color TopBarInset = WithAlpha(FromHex("1F1F29"), 0.97f);
        public static readonly Color TopBarDivider = WithAlpha(FromHex("4DB3FF"), 0.28f);
        public static readonly Color OverlayPanel = WithAlpha(FromHex("1F1F29"), 0.90f);
        public static readonly Color OverlayPanelStrong = WithAlpha(FromHex("14141F"), 0.95f);

        // ─────────────────────────────────────────────
        //  Lookup helpers
        // ─────────────────────────────────────────────

        public static Color GetTextColor(UIThemeTextRole role)
        {
            return role switch
            {
                UIThemeTextRole.Primary => PrimaryText,
                UIThemeTextRole.Secondary => SecondaryText,
                UIThemeTextRole.Disabled => DisabledText,
                UIThemeTextRole.Inverse => InverseText,
                UIThemeTextRole.Accent => PrimaryAccent,
                UIThemeTextRole.Success => Success,
                UIThemeTextRole.Warning => Warning,
                UIThemeTextRole.Danger => Danger,
                _ => PrimaryText,
            };
        }

        public static Color GetReputationColor(int reputation)
        {
            if (reputation >= 70)
                return Success;

            if (reputation >= 40)
                return Warning;

            return Danger;
        }

        // ─────────────────────────────────────────────
        //  Color math
        // ─────────────────────────────────────────────

        public static Color Darken(Color color, float amount)
        {
            return new Color(
                Mathf.Clamp01(color.r - amount),
                Mathf.Clamp01(color.g - amount),
                Mathf.Clamp01(color.b - amount),
                color.a);
        }

        public static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        private static Color FromHex(string hex)
        {
            if (ColorUtility.TryParseHtmlString($"#{hex}", out var color))
                return color;

            return Color.magenta;
        }
    }
}
