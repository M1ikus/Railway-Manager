using System.Collections.Generic;
using NUnit.Framework;
using RailwayManager.Core.Assistant;
using RailwayManager.SharedUI.Assistant;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// M11 AS-6 / AS-D6: opt-in auto-mode — stan per capability + persist w snapshot +
    /// brama AutoExecuteEligible (addytywność/opt-in/zero-koszt — kasa NIGDY auto).
    /// </summary>
    public class AssistantAutoModeTests
    {
        class AutoFake : IAssistantCapability
        {
            public string Id { get; }
            public AssistantCapabilityCategory Category => AssistantCapabilityCategory.Timetable;
            public bool CanAutoExecute { get; }
            public bool AutoModeAllowed { get; }

            public AutoFake(string id, bool canAuto, bool autoAllowed)
            {
                Id = id;
                CanAutoExecute = canAuto;
                AutoModeAllowed = autoAllowed;
            }

            public bool CanExecute() => true;
            public AssistantGuidance GetGuidance() => new AssistantGuidance();
            public AssistantPlan Plan() => null;
            public bool Apply(AssistantPlan plan) => true;
        }

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
        public void SetAutoMode_TogglesAndFiresEvent()
        {
            int events = 0;
            System.Action handler = () => events++;
            AssistantState.OnAutoModeChanged += handler;
            try
            {
                Assert.That(AssistantState.IsAutoModeEnabled("circulation.autogen"), Is.False, "Default OFF (AS-D6)");

                AssistantState.SetAutoMode("circulation.autogen", true);
                Assert.That(AssistantState.IsAutoModeEnabled("circulation.autogen"), Is.True);
                Assert.That(events, Is.EqualTo(1));

                AssistantState.SetAutoMode("circulation.autogen", true); // idempotent — bez eventu
                Assert.That(events, Is.EqualTo(1));

                AssistantState.SetAutoMode("circulation.autogen", false);
                Assert.That(AssistantState.IsAutoModeEnabled("circulation.autogen"), Is.False);
                Assert.That(events, Is.EqualTo(2));
            }
            finally
            {
                AssistantState.OnAutoModeChanged -= handler;
            }
        }

        [Test]
        public void AutoMode_SurvivesSnapshotRoundtrip()
        {
            AssistantState.SetAutoMode("circulation.autogen", true);
            AssistantState.SetAutoMode("crew.autogen", true);

            var snapshot = AssistantState.Snapshot();
            AssistantState.ResetForNewGame();
            Assert.That(AssistantState.IsAutoModeEnabled("circulation.autogen"), Is.False, "Reset czyści");

            AssistantState.RestoreFromSave(snapshot);
            Assert.That(AssistantState.IsAutoModeEnabled("circulation.autogen"), Is.True);
            Assert.That(AssistantState.IsAutoModeEnabled("crew.autogen"), Is.True);
        }

        [Test]
        public void AutoExecuteEligible_FullGateMatrix()
        {
            var plan = new AssistantPlan { capabilityId = "x", title = "t", costGroszy = 0 };
            var costlyPlan = new AssistantPlan { capabilityId = "x", title = "t", costGroszy = 100 };

            var notAllowed = new AutoFake("auto.notAllowed", canAuto: true, autoAllowed: false);
            var allowed = new AutoFake("auto.allowed", canAuto: true, autoAllowed: true);
            var noBrain = new AutoFake("auto.noBrain", canAuto: false, autoAllowed: true);

            // Bez opt-in gracza — nic nie przechodzi.
            Assert.That(AssistantState.AutoExecuteEligible(allowed, plan), Is.False, "Wymaga świadomego opt-in");

            AssistantState.SetAutoMode("auto.allowed", true);
            AssistantState.SetAutoMode("auto.notAllowed", true);
            AssistantState.SetAutoMode("auto.noBrain", true);

            Assert.That(AssistantState.AutoExecuteEligible(allowed, plan), Is.True);
            Assert.That(AssistantState.AutoExecuteEligible(notAllowed, plan), Is.False,
                "AutoModeAllowed=false (nieaddytywna) — flaga adaptera blokuje");
            Assert.That(AssistantState.AutoExecuteEligible(noBrain, plan), Is.False, "Bez mózgu brak auto");
            Assert.That(AssistantState.AutoExecuteEligible(allowed, costlyPlan), Is.False,
                "TWARDA ZASADA: plan z kosztem nigdy auto (kasa za zgodą)");
            Assert.That(AssistantState.AutoExecuteEligible(allowed, null), Is.False);
            Assert.That(AssistantState.AutoExecuteEligible(null, plan), Is.False);
        }

        [Test]
        public void DefaultInterfaceFlag_IsFalse()
        {
            // DIM default: capability bez jawnej deklaracji NIE kwalifikuje się do auto.
            var plain = new AutoFake("auto.plain", canAuto: true, autoAllowed: false);
            Assert.That(((IAssistantCapability)plain).AutoModeAllowed, Is.False);
        }
    }
}
