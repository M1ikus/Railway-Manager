using NUnit.Framework;
using RailwayManager.SharedUI;
using UnityEngine;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// M-UIPolish: testy UITheme — czyste funkcje palety/koloru (zero GameObject/TMP, deterministyczne).
    /// Color math (Darken/WithAlpha), role→color mapping (GetTextColor), reputation→color,
    /// button color blocks per tone. Pełne panele UI = manualny smoke (proceduralne, scena-zależne).
    /// </summary>
    public class UIThemeTests
    {
        // ── Color math ───────────────────────────────────────────────

        [Test]
        public void Darken_ReducesRgb_PreservesAlpha()
        {
            var c = new Color(0.5f, 0.5f, 0.5f, 0.8f);
            var d = UITheme.Darken(c, 0.2f);
            Assert.That(d.r, Is.EqualTo(0.3f).Within(0.0001f));
            Assert.That(d.g, Is.EqualTo(0.3f).Within(0.0001f));
            Assert.That(d.b, Is.EqualTo(0.3f).Within(0.0001f));
            Assert.That(d.a, Is.EqualTo(0.8f).Within(0.0001f), "Alpha nietknięta przez Darken.");
        }

        [Test]
        public void Darken_ClampsAtZero()
        {
            var c = new Color(0.1f, 0.1f, 0.1f, 1f);
            var d = UITheme.Darken(c, 0.5f);
            Assert.That(d.r, Is.EqualTo(0f), "RGB nie schodzi poniżej 0.");
            Assert.That(d.g, Is.EqualTo(0f));
            Assert.That(d.b, Is.EqualTo(0f));
        }

        [Test]
        public void WithAlpha_SetsAlpha_PreservesRgb()
        {
            var c = new Color(0.2f, 0.4f, 0.6f, 1f);
            var a = UITheme.WithAlpha(c, 0.5f);
            Assert.That(a.r, Is.EqualTo(0.2f).Within(0.0001f));
            Assert.That(a.g, Is.EqualTo(0.4f).Within(0.0001f));
            Assert.That(a.b, Is.EqualTo(0.6f).Within(0.0001f));
            Assert.That(a.a, Is.EqualTo(0.5f).Within(0.0001f), "Alpha ustawiona.");
        }

        // ── Role → color ─────────────────────────────────────────────

        [Test]
        public void GetTextColor_MapsRoles()
        {
            Assert.That(UITheme.GetTextColor(UIThemeTextRole.Primary), Is.EqualTo(UITheme.PrimaryText));
            Assert.That(UITheme.GetTextColor(UIThemeTextRole.Secondary), Is.EqualTo(UITheme.SecondaryText));
            Assert.That(UITheme.GetTextColor(UIThemeTextRole.Danger), Is.EqualTo(UITheme.Danger));
            Assert.That(UITheme.GetTextColor(UIThemeTextRole.Accent), Is.EqualTo(UITheme.PrimaryAccent));
        }

        // ── Reputation → color (gameplay) ────────────────────────────

        [Test]
        public void GetReputationColor_Thresholds()
        {
            Assert.That(UITheme.GetReputationColor(85), Is.EqualTo(UITheme.Success), "≥70 → zielony (dobra).");
            Assert.That(UITheme.GetReputationColor(70), Is.EqualTo(UITheme.Success), "próg 70 włącznie.");
            Assert.That(UITheme.GetReputationColor(50), Is.EqualTo(UITheme.Warning), "40-69 → żółty (średnia).");
            Assert.That(UITheme.GetReputationColor(40), Is.EqualTo(UITheme.Warning), "próg 40 włącznie.");
            Assert.That(UITheme.GetReputationColor(20), Is.EqualTo(UITheme.Danger), "<40 → czerwony (zła).");
            Assert.That(UITheme.GetReputationColor(0), Is.EqualTo(UITheme.Danger));
        }

        // ── Button color blocks ──────────────────────────────────────

        [Test]
        public void CreateButtonColorBlock_PrimaryUsesAccent()
        {
            var cb = UITheme.CreateButtonColorBlock(UIButtonTone.Primary);
            Assert.That(cb.normalColor, Is.EqualTo(UITheme.PrimaryAccent),
                "Primary button normalColor = akcent.");
        }

        [Test]
        public void CreateButtonColorBlock_DangerUsesDanger()
        {
            var cb = UITheme.CreateButtonColorBlock(UIButtonTone.Danger);
            Assert.That(cb.normalColor, Is.EqualTo(UITheme.Danger), "Danger button normalColor = czerwony.");
        }

        [Test]
        public void CreateButtonColorBlock_GhostIsTransparent()
        {
            var cb = UITheme.CreateButtonColorBlock(UIButtonTone.Ghost);
            Assert.That(cb.normalColor.a, Is.EqualTo(0f), "Ghost button przezroczysty w stanie normalnym.");
        }

        [Test]
        public void CreateButtonColorBlock_TonesDiffer()
        {
            var primary = UITheme.CreateButtonColorBlock(UIButtonTone.Primary);
            var danger = UITheme.CreateButtonColorBlock(UIButtonTone.Danger);
            var secondary = UITheme.CreateButtonColorBlock(UIButtonTone.Secondary);
            Assert.That(primary.normalColor, Is.Not.EqualTo(danger.normalColor), "Primary ≠ Danger.");
            Assert.That(primary.normalColor, Is.Not.EqualTo(secondary.normalColor), "Primary ≠ Secondary.");
        }

        [Test]
        public void CreateColorBlock_MapsAllStates()
        {
            var cb = UITheme.CreateColorBlock(Color.red, Color.green, Color.blue, Color.yellow, Color.gray);
            Assert.That(cb.normalColor, Is.EqualTo(Color.red));
            Assert.That(cb.highlightedColor, Is.EqualTo(Color.green));
            Assert.That(cb.pressedColor, Is.EqualTo(Color.blue));
            Assert.That(cb.selectedColor, Is.EqualTo(Color.yellow));
            Assert.That(cb.disabledColor, Is.EqualTo(Color.gray));
        }

        // ── Paleta — sanity ──────────────────────────────────────────

        [Test]
        public void StatusColors_AreDistinct()
        {
            // Success/Warning/Danger muszą być wizualnie rozróżnialne (3 różne kolory statusu).
            Assert.That(UITheme.Success, Is.Not.EqualTo(UITheme.Warning));
            Assert.That(UITheme.Warning, Is.Not.EqualTo(UITheme.Danger));
            Assert.That(UITheme.Success, Is.Not.EqualTo(UITheme.Danger));
        }

        [Test]
        public void SolidColors_FullyOpaque()
        {
            // Kolory tekstu/akcentu są solidne (alpha 1) — inaczej tekst byłby półprzezroczysty.
            Assert.That(UITheme.PrimaryText.a, Is.EqualTo(1f));
            Assert.That(UITheme.PrimaryAccent.a, Is.EqualTo(1f));
            Assert.That(UITheme.Danger.a, Is.EqualTo(1f));
        }

        [Test]
        public void Focus_IsAccentAlias()
        {
            // Focus ring = alias PrimaryAccent (zmiana akcentu propaguje się do focus).
            Assert.That(UITheme.Focus, Is.EqualTo(UITheme.PrimaryAccent));
        }
    }
}
