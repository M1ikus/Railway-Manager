using System.Collections.Generic;
using NUnit.Framework;
using RailwayManager.Core.Assistant;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// M11 AS-1a: kontrakty AssistantCapabilityRegistry + AssistantSignals.
    /// FakeCapability demonstruje wzorzec implementacji dla przyszłych adapterów
    /// (AS-2 obiegi, AS-4 grafiki) — w tym walidację cudzego planu w Apply.
    /// </summary>
    public class AssistantCoreTests
    {
        private class FakeCapability : IAssistantCapability
        {
            public string Id { get; }
            public AssistantCapabilityCategory Category => AssistantCapabilityCategory.Timetable;
            public bool CanAutoExecute { get; }

            public bool canExecuteResult = true;
            public int applyCalls;
            public AssistantPlan lastAppliedPlan;

            public FakeCapability(string id, bool canAutoExecute = true)
            {
                Id = id;
                CanAutoExecute = canAutoExecute;
            }

            public bool CanExecute() => canExecuteResult;

            public AssistantGuidance GetGuidance() => new AssistantGuidance
            {
                steps = { new AssistantGuidanceStep { messageKey = "assistant." + Id + ".step1" } }
            };

            public AssistantPlan Plan()
            {
                if (!CanAutoExecute) return null;
                return new AssistantPlan
                {
                    capabilityId = Id,
                    title = "fake plan",
                    previewLines = { "linia 1" },
                    payload = "payload-data"
                };
            }

            public bool Apply(AssistantPlan plan)
            {
                if (plan == null || plan.capabilityId != Id) return false;
                applyCalls++;
                lastAppliedPlan = plan;
                return true;
            }
        }

        [SetUp]
        public void SetUp() => AssistantCapabilityRegistry.Clear();

        [TearDown]
        public void TearDown() => AssistantCapabilityRegistry.Clear();

        // ────────────────────────── Registry ──────────────────────────

        [Test]
        public void Register_ThenGet_ReturnsSameInstance()
        {
            var cap = new FakeCapability("test.alpha");

            Assert.That(AssistantCapabilityRegistry.Register(cap), Is.True);
            Assert.That(AssistantCapabilityRegistry.Count, Is.EqualTo(1));
            Assert.That(AssistantCapabilityRegistry.Get("test.alpha"), Is.SameAs(cap));
        }

        [Test]
        public void Register_NullOrEmptyId_ReturnsFalse()
        {
            Assert.That(AssistantCapabilityRegistry.Register(null), Is.False);
            Assert.That(AssistantCapabilityRegistry.Register(new FakeCapability("")), Is.False);
            Assert.That(AssistantCapabilityRegistry.Count, Is.EqualTo(0));
        }

        [Test]
        public void Register_DuplicateId_KeepsFirstRegistration()
        {
            var first = new FakeCapability("test.dup");
            var second = new FakeCapability("test.dup");

            Assert.That(AssistantCapabilityRegistry.Register(first), Is.True);
            Assert.That(AssistantCapabilityRegistry.Register(second), Is.False);
            Assert.That(AssistantCapabilityRegistry.Count, Is.EqualTo(1));
            Assert.That(AssistantCapabilityRegistry.Get("test.dup"), Is.SameAs(first));
        }

        [Test]
        public void Unregister_RemovesCapability_UnknownIdReturnsFalse()
        {
            AssistantCapabilityRegistry.Register(new FakeCapability("test.gone"));

            Assert.That(AssistantCapabilityRegistry.Unregister("test.gone"), Is.True);
            Assert.That(AssistantCapabilityRegistry.Get("test.gone"), Is.Null);
            Assert.That(AssistantCapabilityRegistry.Unregister("test.gone"), Is.False);
            Assert.That(AssistantCapabilityRegistry.Unregister(null), Is.False);
        }

        [Test]
        public void Clear_EmptiesRegistry()
        {
            AssistantCapabilityRegistry.Register(new FakeCapability("test.a"));
            AssistantCapabilityRegistry.Register(new FakeCapability("test.b"));

            AssistantCapabilityRegistry.Clear();

            Assert.That(AssistantCapabilityRegistry.Count, Is.EqualTo(0));
            Assert.That(AssistantCapabilityRegistry.Get("test.a"), Is.Null);
        }

        [Test]
        public void OnChanged_FiresOnRegisterUnregisterAndClear()
        {
            int events = 0;
            System.Action handler = () => events++;
            AssistantCapabilityRegistry.OnChanged += handler;
            try
            {
                AssistantCapabilityRegistry.Register(new FakeCapability("test.ev"));   // +1
                AssistantCapabilityRegistry.Unregister("test.ev");                      // +1
                AssistantCapabilityRegistry.Register(new FakeCapability("test.ev2"));  // +1
                AssistantCapabilityRegistry.Clear();                                    // +1
                AssistantCapabilityRegistry.Clear();                                    // no-op (pusty) — bez eventu

                Assert.That(events, Is.EqualTo(4));
            }
            finally
            {
                AssistantCapabilityRegistry.OnChanged -= handler;
            }
        }

        [Test]
        public void GetAll_ClearsAndFillsCallerBuffer()
        {
            AssistantCapabilityRegistry.Register(new FakeCapability("test.one"));
            AssistantCapabilityRegistry.Register(new FakeCapability("test.two"));

            var buffer = new List<IAssistantCapability> { new FakeCapability("stale.junk") };
            AssistantCapabilityRegistry.GetAll(buffer);

            Assert.That(buffer.Count, Is.EqualTo(2));
            Assert.That(buffer.Exists(c => c.Id == "test.one"), Is.True);
            Assert.That(buffer.Exists(c => c.Id == "test.two"), Is.True);
        }

        // ──────────────── Kontrakt Plan→Apply (wzorzec dla adapterów) ────────────────

        [Test]
        public void GuidanceOnlyCapability_PlanReturnsNull_GuidanceAlwaysPresent()
        {
            var cap = new FakeCapability("depot.buildTrack", canAutoExecute: false);

            Assert.That(cap.Plan(), Is.Null, "Capability bez mózgu nie proponuje (sufit AS-D5 na [1])");
            var guidance = cap.GetGuidance();
            Assert.That(guidance, Is.Not.Null);
            Assert.That(guidance.steps.Count, Is.GreaterThan(0), "Każda capability uczy (AS-D5)");
        }

        [Test]
        public void Apply_RejectsPlanFromDifferentCapability()
        {
            var capA = new FakeCapability("test.capA");
            var capB = new FakeCapability("test.capB");

            var planB = capB.Plan();

            Assert.That(capA.Apply(planB), Is.False);
            Assert.That(capA.applyCalls, Is.EqualTo(0));
            Assert.That(capA.Apply(null), Is.False);
        }

        [Test]
        public void Apply_AcceptsOwnPlan_PayloadRoundtripsIntact()
        {
            var cap = new FakeCapability("test.roundtrip");

            var plan = cap.Plan();
            Assert.That(plan, Is.Not.Null);
            Assert.That(plan.capabilityId, Is.EqualTo("test.roundtrip"));

            Assert.That(cap.Apply(plan), Is.True);
            Assert.That(cap.applyCalls, Is.EqualTo(1));
            Assert.That(cap.lastAppliedPlan.payload, Is.EqualTo("payload-data"),
                "Payload z Plan() musi wrócić nietknięty do Apply()");
        }

        // ────────────────────────── Signals ──────────────────────────

        [Test]
        public void Signals_Emit_DeliversToSubscriberWithPayload()
        {
            AssistantSignal received = default;
            int count = 0;
            System.Action<AssistantSignal> handler = s => { received = s; count++; };

            AssistantSignals.OnSignal += handler;
            try
            {
                AssistantSignals.Emit(AssistantSignalKind.Suggestion, "TestSource",
                    contextKey: "ctx:42", messageKey: "assistant.test.msg", payload: 42);

                Assert.That(count, Is.EqualTo(1));
                Assert.That(received.kind, Is.EqualTo(AssistantSignalKind.Suggestion));
                Assert.That(received.sourceId, Is.EqualTo("TestSource"));
                Assert.That(received.contextKey, Is.EqualTo("ctx:42"));
                Assert.That(received.messageKey, Is.EqualTo("assistant.test.msg"));
                Assert.That(received.payload, Is.EqualTo(42));
            }
            finally
            {
                AssistantSignals.OnSignal -= handler;
            }
        }

        [Test]
        public void Signals_Emit_WithoutSubscribers_DoesNotThrow()
        {
            Assert.DoesNotThrow(() =>
                AssistantSignals.Emit(AssistantSignalKind.Custom, "NoListeners"));
        }
    }
}
