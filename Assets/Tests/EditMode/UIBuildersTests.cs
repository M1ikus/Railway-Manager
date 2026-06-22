using NUnit.Framework;
using RailwayManager.SharedUI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// M-UIPolish: testy UIBuilders — fabryki themed komponentów UI. EditMode (tworzenie GameObject
    /// działa bez Play mode). Weryfikuje że fabryki produkują oczekiwane komponenty + parenting +
    /// content, a NIE pełne E2E (klik→akcja — to manualny smoke/PlayMode scena).
    /// </summary>
    public class UIBuildersTests
    {
        Transform _root;

        [SetUp]
        public void SetUp() => _root = new GameObject("UIBuildersTests_Root").transform;

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root.gameObject);
        }

        // ── Bez TMP (Image/RectTransform — najbezpieczniejsze) ───────

        [Test]
        public void MakePanel_ProducesImageParentedToRoot()
        {
            var img = UIBuilders.MakePanel(_root, UIBuilders.PanelRole.Background);
            Assert.That(img, Is.Not.Null);
            Assert.That(img.GetComponent<Image>(), Is.Not.Null, "Panel ma komponent Image.");
            Assert.That(img.transform.parent, Is.EqualTo(_root), "Panel parentowany do roota.");
        }

        [Test]
        public void MakeContainer_Vertical_HasVerticalLayoutGroup()
        {
            var rt = UIBuilders.MakeContainer(_root, UIBuilders.ContainerLayout.Vertical);
            Assert.That(rt, Is.Not.Null);
            Assert.That(rt.GetComponent<VerticalLayoutGroup>(), Is.Not.Null, "Vertical → VerticalLayoutGroup.");
            Assert.That(rt.parent, Is.EqualTo(_root));
        }

        [Test]
        public void MakeContainer_Horizontal_HasHorizontalLayoutGroup()
        {
            var rt = UIBuilders.MakeContainer(_root, UIBuilders.ContainerLayout.Horizontal);
            Assert.That(rt.GetComponent<HorizontalLayoutGroup>(), Is.Not.Null, "Horizontal → HorizontalLayoutGroup.");
        }

        [Test]
        public void MakeSeparator_ProducesImage()
        {
            var sep = UIBuilders.MakeSeparator(_root);
            Assert.That(sep, Is.Not.Null);
            Assert.That(sep.GetComponent<Image>(), Is.Not.Null);
            Assert.That(sep.transform.parent, Is.EqualTo(_root));
        }

        // ── TMP (Label/Button) ───────────────────────────────────────

        [Test]
        public void MakeLabel_ProducesTmpWithText()
        {
            var label = UIBuilders.MakeLabel(_root, "Witaj");
            Assert.That(label, Is.Not.Null);
            Assert.That(label, Is.InstanceOf<TextMeshProUGUI>());
            Assert.That(label.text, Is.EqualTo("Witaj"), "Label ma ustawiony tekst.");
            Assert.That(label.transform.parent, Is.EqualTo(_root));
        }

        [Test]
        public void MakeLabel_AppliesColor()
        {
            var label = UIBuilders.MakeLabel(_root, "X", UIBuilders.TypographyRole.Body, Color.red);
            Assert.That(label.color, Is.EqualTo(Color.red), "Przekazany kolor jest stosowany.");
        }

        [Test]
        public void MakeButton_ProducesButtonWithChildLabel()
        {
            var btn = UIBuilders.MakeButton(_root, "Zatwierdź", UIButtonTone.Primary);
            Assert.That(btn, Is.Not.Null);
            Assert.That(btn.GetComponent<Button>(), Is.Not.Null);
            Assert.That(btn.GetComponent<Image>(), Is.Not.Null, "Button ma tło (Image).");
            Assert.That(btn.transform.parent, Is.EqualTo(_root));

            var label = btn.GetComponentInChildren<TextMeshProUGUI>();
            Assert.That(label, Is.Not.Null, "Button ma dziecko-label TMP.");
            Assert.That(label.text, Is.EqualTo("Zatwierdź"), "Label przyjmuje tekst przycisku.");
        }

        [Test]
        public void MakeButton_AppliesToneColorBlock()
        {
            var primary = UIBuilders.MakeButton(_root, "P", UIButtonTone.Primary);
            var danger = UIBuilders.MakeButton(_root, "D", UIButtonTone.Danger);

            Assert.That(primary.colors.normalColor, Is.EqualTo(UITheme.PrimaryAccent),
                "Primary button color block = akcent.");
            Assert.That(danger.colors.normalColor, Is.EqualTo(UITheme.Danger),
                "Danger button color block = czerwony.");
        }

        [Test]
        public void MakeButton_DefaultTone_IsPrimary()
        {
            var btn = UIBuilders.MakeButton(_root, "Default");
            Assert.That(btn.colors.normalColor, Is.EqualTo(UITheme.PrimaryAccent),
                "Domyślny tone = Primary.");
        }
    }
}
