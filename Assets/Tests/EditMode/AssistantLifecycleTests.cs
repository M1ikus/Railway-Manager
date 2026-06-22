using Newtonsoft.Json.Linq;
using NUnit.Framework;
using RailwayManager.Core.Assistant;
using RailwayManager.SharedUI.Assistant;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// M11 AS-7: lifecycle persony — skip/restart onboardingu (restart = reset DRIVERA,
    /// nie SuggestionMemory) + PEŁNY persist roundtrip przez JSON (ta sama ścieżka
    /// Newtonsoft co SharedUISavable: JObject.FromObject → ToObject).
    /// </summary>
    public class AssistantLifecycleTests
    {
        AssistantStateSnapshot _backup;

        [SetUp]
        public void SetUp()
        {
            _backup = AssistantState.Snapshot();
            AssistantState.ResetForNewGame();
        }

        [TearDown]
        public void TearDown() => AssistantState.RestoreFromSave(_backup);

        [Test]
        public void FullSnapshot_JsonRoundtrip_AllFieldsSurvive()
        {
            AssistantState.DisplayName = "pani Janina";
            AssistantState.IntroShown = true;
            AssistantState.OnboardingSkipped = true;
            AssistantState.AddHistory("test wpisu A");
            AssistantState.AddHistory("test wpisu B");
            AssistantState.SetAutoMode("circulation.autogen", true);

            // Ta sama ścieżka co save/load (SharedUISavable): obiekt → JSON → obiekt.
            var json = JObject.FromObject(AssistantState.Snapshot());
            var restored = json.ToObject<AssistantStateSnapshot>();

            AssistantState.ResetForNewGame();
            Assert.That(AssistantState.IntroShown, Is.False, "Sanity: reset wyczyścił stan");
            Assert.That(AssistantState.OnboardingSkipped, Is.False);

            AssistantState.RestoreFromSave(restored);

            Assert.That(AssistantState.DisplayName, Is.EqualTo("pani Janina"));
            Assert.That(AssistantState.IntroShown, Is.True);
            Assert.That(AssistantState.OnboardingSkipped, Is.True);
            Assert.That(AssistantState.History.Count, Is.EqualTo(2));
            Assert.That(AssistantState.History[0].text, Is.EqualTo("test wpisu B"), "Najnowszy na górze");
            Assert.That(AssistantState.IsAutoModeEnabled("circulation.autogen"), Is.True);
        }

        [Test]
        public void OldSaveWithoutNewFields_DefaultsApply()
        {
            // Save sprzed AS-6/AS-7 — JSON bez onboardingSkipped/autoModeCapabilityIds.
            var legacy = JObject.Parse("{\"displayName\":\"pan Tadeusz\",\"introShown\":true,\"history\":[]}");
            var snapshot = legacy.ToObject<AssistantStateSnapshot>();

            AssistantState.RestoreFromSave(snapshot);

            Assert.That(AssistantState.IntroShown, Is.True);
            Assert.That(AssistantState.OnboardingSkipped, Is.False, "Brak pola → default false");
            Assert.That(AssistantState.IsAutoModeEnabled("circulation.autogen"), Is.False);
        }

        [Test]
        public void RestartOnboarding_ResetsDriver_KeepsAutoModeAndHistory()
        {
            AssistantState.IntroShown = true;
            AssistantState.OnboardingSkipped = true;
            AssistantState.SetAutoMode("crew.autogen", true);
            AssistantState.AddHistory("przed restartem");

            AssistantState.RestartOnboarding();

            Assert.That(AssistantState.IntroShown, Is.False, "Intro poleci ponownie");
            Assert.That(AssistantState.OnboardingSkipped, Is.False, "Skip zdjęty");
            Assert.That(NextStepMonitor.CurrentCandidate, Is.Null, "Stan runtime monitora wyczyszczony");
            Assert.That(AssistantState.IsAutoModeEnabled("crew.autogen"), Is.True,
                "Restart onboardingu NIE rusza auto-mode (to nie pełny reset)");
            Assert.That(AssistantState.History.Count, Is.EqualTo(1), "Historia działań zostaje");
        }

        [Test]
        public void OfferFilter_SkipsOnboardingKind_WhenSkipped()
        {
            var onboarding = new AssistantRule
            {
                id = "test.ob",
                kind = AssistantRuleKind.Onboarding,
                contextKey = "lifecycle-test:ob:" + GetType().GetHashCode()
            };
            var reactive = new AssistantRule
            {
                id = "test.reactive",
                kind = AssistantRuleKind.Reactive,
                contextKey = "lifecycle-test:reactive:" + GetType().GetHashCode()
            };

            Assert.That(AssistantState.OfferFilterAllows(null), Is.False);
            Assert.That(AssistantState.OfferFilterAllows(onboarding), Is.True,
                "Bez skipa onboarding przechodzi (świeży contextKey → memoria pusta)");
            Assert.That(AssistantState.OfferFilterAllows(reactive), Is.True);

            AssistantState.OnboardingSkipped = true;
            Assert.That(AssistantState.OfferFilterAllows(onboarding), Is.False,
                "Skip wycina WYŁĄCZNIE reguły kind=Onboarding");
            Assert.That(AssistantState.OfferFilterAllows(reactive), Is.True,
                "Advisor (reaktywne) działa mimo skipa");
        }
    }
}
