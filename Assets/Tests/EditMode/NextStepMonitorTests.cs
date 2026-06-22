using NUnit.Framework;
using RailwayManager.Core.Assistant;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// M11 AS-1b: kontrakty NextStepMonitor + AssistantRuleRegistry — wybór kandydata
    /// (priorytety, determinizm remisu), filtr ofert, stuck-detector (czas / sygnały),
    /// ToolActive (przesuwanie timera), sekwencja onboardingu stanowa (preset-aware).
    /// </summary>
    public class NextStepMonitorTests
    {
        private AssistantRule MakeRule(string id, int priority, System.Func<bool> isActive,
            string capabilityId = null, AssistantRuleKind kind = AssistantRuleKind.Onboarding)
        {
            return new AssistantRule
            {
                id = id,
                kind = kind,
                priority = priority,
                isActive = isActive,
                capabilityId = capabilityId ?? id,
                messageKey = "assistant." + id
            };
        }

        [SetUp]
        public void SetUp()
        {
            AssistantRuleRegistry.Clear();
            NextStepMonitor.Shutdown();
            NextStepMonitor.Reset();
            NextStepMonitor.SetOfferFilter(null);
            NextStepMonitor.Initialize();
        }

        [TearDown]
        public void TearDown()
        {
            NextStepMonitor.Shutdown();
            NextStepMonitor.Reset();
            NextStepMonitor.SetOfferFilter(null);
            AssistantRuleRegistry.Clear();
        }

        // ────────────────────────── Rejestr reguł ──────────────────────────

        [Test]
        public void RuleRegistry_RejectsNullEmptyIdMissingPredicateAndDuplicates()
        {
            Assert.That(AssistantRuleRegistry.Register(null), Is.False);
            Assert.That(AssistantRuleRegistry.Register(MakeRule("", 1, () => true)), Is.False);
            Assert.That(AssistantRuleRegistry.Register(new AssistantRule { id = "no.predicate", priority = 1 }), Is.False);

            Assert.That(AssistantRuleRegistry.Register(MakeRule("dup", 1, () => true)), Is.True);
            Assert.That(AssistantRuleRegistry.Register(MakeRule("dup", 2, () => true)), Is.False);
            Assert.That(AssistantRuleRegistry.Count, Is.EqualTo(1));
            Assert.That(AssistantRuleRegistry.Get("dup").priority, Is.EqualTo(1), "Pierwsza rejestracja wygrywa");
        }

        [Test]
        public void RuleRegistry_PreservesRegistrationOrder()
        {
            AssistantRuleRegistry.Register(MakeRule("first", 5, () => true));
            AssistantRuleRegistry.Register(MakeRule("second", 9, () => true));
            AssistantRuleRegistry.Register(MakeRule("third", 1, () => true));

            Assert.That(AssistantRuleRegistry.All[0].id, Is.EqualTo("first"));
            Assert.That(AssistantRuleRegistry.All[1].id, Is.EqualTo("second"));
            Assert.That(AssistantRuleRegistry.All[2].id, Is.EqualTo("third"));
        }

        // ────────────────────────── Wybór kandydata ──────────────────────────

        [Test]
        public void Tick_PicksHighestPriorityActiveRule()
        {
            AssistantRuleRegistry.Register(MakeRule("low", 100, () => true));
            AssistantRuleRegistry.Register(MakeRule("high", 900, () => true));
            AssistantRuleRegistry.Register(MakeRule("inactive", 950, () => false));

            NextStepMonitor.Tick(0);

            Assert.That(NextStepMonitor.CurrentCandidate, Is.Not.Null);
            Assert.That(NextStepMonitor.CurrentCandidate.id, Is.EqualTo("high"));
        }

        [Test]
        public void Tick_TieBreak_FirstRegisteredWins_StableAcrossTicks()
        {
            AssistantRuleRegistry.Register(MakeRule("tie.a", 500, () => true));
            AssistantRuleRegistry.Register(MakeRule("tie.b", 500, () => true));

            int changes = 0;
            System.Action<AssistantRule> handler = _ => changes++;
            NextStepMonitor.OnCandidateChanged += handler;
            try
            {
                NextStepMonitor.Tick(0);
                NextStepMonitor.Tick(1);
                NextStepMonitor.Tick(2);

                Assert.That(NextStepMonitor.CurrentCandidate.id, Is.EqualTo("tie.a"));
                Assert.That(changes, Is.EqualTo(1), "Stabilny kandydat = jeden event, zero flappingu");
            }
            finally
            {
                NextStepMonitor.OnCandidateChanged -= handler;
            }
        }

        [Test]
        public void Tick_RuleDeactivates_MovesToNextActive_ThenNull()
        {
            bool highActive = true;
            bool lowActive = true;
            AssistantRuleRegistry.Register(MakeRule("high", 900, () => highActive));
            AssistantRuleRegistry.Register(MakeRule("low", 100, () => lowActive));

            AssistantRule lastEvent = null;
            int events = 0;
            System.Action<AssistantRule> handler = r => { lastEvent = r; events++; };
            NextStepMonitor.OnCandidateChanged += handler;
            try
            {
                NextStepMonitor.Tick(0);
                Assert.That(NextStepMonitor.CurrentCandidate.id, Is.EqualTo("high"));

                highActive = false;
                NextStepMonitor.Tick(1);
                Assert.That(NextStepMonitor.CurrentCandidate.id, Is.EqualTo("low"));

                lowActive = false;
                NextStepMonitor.Tick(2);
                Assert.That(NextStepMonitor.CurrentCandidate, Is.Null);
                Assert.That(lastEvent, Is.Null, "Event z null = nic do zaoferowania");
                Assert.That(events, Is.EqualTo(3));
            }
            finally
            {
                NextStepMonitor.OnCandidateChanged -= handler;
            }
        }

        [Test]
        public void OfferFilter_ExcludedRule_NextOneChosen()
        {
            AssistantRuleRegistry.Register(MakeRule("snoozed", 900, () => true));
            AssistantRuleRegistry.Register(MakeRule("allowed", 100, () => true));

            NextStepMonitor.SetOfferFilter(rule => rule.id != "snoozed");
            NextStepMonitor.Tick(0);

            Assert.That(NextStepMonitor.CurrentCandidate.id, Is.EqualTo("allowed"),
                "Filtr SharedUI (ShouldShow) pomija snoozed regułę przy wyborze");
        }

        [Test]
        public void OnboardingSequence_StateDriven_PresetSkipsSatisfiedSteps()
        {
            // Symulacja presetu zajezdni: krok 1 (tory) JUŻ spełniony na starcie gry.
            bool hasTrack = true;        // preset zbudował tory
            bool hasVehicle = false;
            bool hasTimetable = false;

            AssistantRuleRegistry.Register(MakeRule("ob.track", 1000, () => !hasTrack, "depot.buildTrack"));
            AssistantRuleRegistry.Register(MakeRule("ob.vehicle", 990, () => !hasVehicle, "fleet.buy"));
            AssistantRuleRegistry.Register(MakeRule("ob.timetable", 980, () => !hasTimetable, "timetable.create"));

            NextStepMonitor.Tick(0);
            Assert.That(NextStepMonitor.CurrentCandidate.id, Is.EqualTo("ob.vehicle"),
                "Preset-aware: onboarding zaczyna od pierwszego NIEspełnionego kroku");

            hasVehicle = true;           // gracz kupił pojazd
            NextStepMonitor.Tick(1);
            Assert.That(NextStepMonitor.CurrentCandidate.id, Is.EqualTo("ob.timetable"));

            hasTimetable = true;         // gracz ułożył rozkład
            NextStepMonitor.Tick(2);
            Assert.That(NextStepMonitor.CurrentCandidate, Is.Null, "Wszystkie kroki spełnione");
        }

        // ────────────────────────── Stuck-detector ──────────────────────────

        [Test]
        public void StuckByTime_FiresOnceAfterThreshold()
        {
            AssistantRuleRegistry.Register(MakeRule("step", 900, () => true, "fleet.buy"));

            AssistantRule stuckRule = null;
            int stuckEvents = 0;
            System.Action<AssistantRule> handler = r => { stuckRule = r; stuckEvents++; };
            NextStepMonitor.OnStuckDetected += handler;
            try
            {
                NextStepMonitor.Tick(0);
                NextStepMonitor.Tick(AssistantConstants.StuckTimeOnStepRealSec - 1);
                Assert.That(stuckEvents, Is.EqualTo(0), "Przed progiem brak stuck");

                NextStepMonitor.Tick(AssistantConstants.StuckTimeOnStepRealSec);
                Assert.That(stuckEvents, Is.EqualTo(1));
                Assert.That(stuckRule.id, Is.EqualTo("step"));

                NextStepMonitor.Tick(AssistantConstants.StuckTimeOnStepRealSec + 100);
                Assert.That(stuckEvents, Is.EqualTo(1), "Stuck jednorazowo per kandydat");
            }
            finally
            {
                NextStepMonitor.OnStuckDetected -= handler;
            }
        }

        [Test]
        public void StuckByTime_ToolActive_SlidesTimer()
        {
            AssistantRuleRegistry.Register(MakeRule("step", 900, () => true, "depot.buildTrack"));

            int stuckEvents = 0;
            System.Action<AssistantRule> handler = _ => stuckEvents++;
            NextStepMonitor.OnStuckDetected += handler;
            try
            {
                NextStepMonitor.Tick(0);

                // Gracz aktywnie buduje — czas płynie daleko za próg, ale stuck nie strzela.
                AssistantSignals.Emit(AssistantSignalKind.ToolActive, "TrackBuildStateMachine");
                NextStepMonitor.Tick(200);
                Assert.That(stuckEvents, Is.EqualTo(0), "Aktywny gracz nie jest stuck");

                // Odłożył narzędzie — timer liczy od ostatniej aktywności (t=200).
                AssistantSignals.Emit(AssistantSignalKind.ToolIdle, "TrackBuildStateMachine");
                NextStepMonitor.Tick(200 + AssistantConstants.StuckTimeOnStepRealSec - 1);
                Assert.That(stuckEvents, Is.EqualTo(0), "Pełny próg liczony od końca aktywności");

                NextStepMonitor.Tick(200 + AssistantConstants.StuckTimeOnStepRealSec);
                Assert.That(stuckEvents, Is.EqualTo(1));
            }
            finally
            {
                NextStepMonitor.OnStuckDetected -= handler;
            }
        }

        [Test]
        public void StuckByPanelAbandoned_MatchingContextKey()
        {
            AssistantRuleRegistry.Register(MakeRule("step", 900, () => true, "fleet.buy"));

            int stuckEvents = 0;
            System.Action<AssistantRule> handler = _ => stuckEvents++;
            NextStepMonitor.OnStuckDetected += handler;
            try
            {
                NextStepMonitor.Tick(0);

                for (int i = 0; i < AssistantConstants.StuckPanelAbandonedThreshold - 1; i++)
                {
                    AssistantSignals.Emit(AssistantSignalKind.PanelAbandoned, "FleetPanelUI", contextKey: "fleet.buy");
                }
                Assert.That(stuckEvents, Is.EqualTo(0), "Poniżej progu");

                AssistantSignals.Emit(AssistantSignalKind.PanelAbandoned, "FleetPanelUI", contextKey: "fleet.buy");
                Assert.That(stuckEvents, Is.EqualTo(1), "Próg porzuconych paneli osiągnięty");
            }
            finally
            {
                NextStepMonitor.OnStuckDetected -= handler;
            }
        }

        [Test]
        public void StuckSignals_NonMatchingOrNullContextKey_DoNotCount()
        {
            AssistantRuleRegistry.Register(MakeRule("step", 900, () => true, "fleet.buy"));

            int stuckEvents = 0;
            System.Action<AssistantRule> handler = _ => stuckEvents++;
            NextStepMonitor.OnStuckDetected += handler;
            try
            {
                NextStepMonitor.Tick(0);

                for (int i = 0; i < 10; i++)
                {
                    AssistantSignals.Emit(AssistantSignalKind.PanelAbandoned, "WorkshopsPanelUI", contextKey: "inny.krok");
                    AssistantSignals.Emit(AssistantSignalKind.PanelAbandoned, "AnonPanel");
                    AssistantSignals.Emit(AssistantSignalKind.ActionCanceled, "AnonPanel");
                }

                Assert.That(stuckEvents, Is.EqualTo(0), "Sygnały spoza bieżącego kroku nie liczą się do stuck");
            }
            finally
            {
                NextStepMonitor.OnStuckDetected -= handler;
            }
        }

        [Test]
        public void CandidateChange_ResetsStuckCountersAndTimer()
        {
            bool highActive = false;
            AssistantRuleRegistry.Register(MakeRule("high", 900, () => highActive, "timetable.create"));
            AssistantRuleRegistry.Register(MakeRule("low", 100, () => true, "fleet.buy"));

            int stuckEvents = 0;
            System.Action<AssistantRule> handler = _ => stuckEvents++;
            NextStepMonitor.OnStuckDetected += handler;
            try
            {
                NextStepMonitor.Tick(0);
                Assert.That(NextStepMonitor.CurrentCandidate.id, Is.EqualTo("low"));

                // Jeden sygnał poniżej progu + prawie cały próg czasu.
                AssistantSignals.Emit(AssistantSignalKind.PanelAbandoned, "FleetPanelUI", contextKey: "fleet.buy");
                NextStepMonitor.Tick(AssistantConstants.StuckTimeOnStepRealSec - 1);

                // Zmiana kandydata (wyższy priorytet aktywny) → pełny reset stanu stuck.
                highActive = true;
                NextStepMonitor.Tick(AssistantConstants.StuckTimeOnStepRealSec + 5);
                Assert.That(NextStepMonitor.CurrentCandidate.id, Is.EqualTo("high"));
                Assert.That(stuckEvents, Is.EqualTo(0), "Reset timera przy zmianie kandydata");
                Assert.That(NextStepMonitor.StuckFiredForCurrent, Is.False);

                // Stary licznik PanelAbandoned nie przenosi się na nowego kandydata.
                AssistantSignals.Emit(AssistantSignalKind.PanelAbandoned, "TimetableUI", contextKey: "timetable.create");
                Assert.That(stuckEvents, Is.EqualTo(0), "Licznik liczony od zera dla nowego kandydata");
            }
            finally
            {
                NextStepMonitor.OnStuckDetected -= handler;
            }
        }

        [Test]
        public void ToolActiveIdle_TogglesFlag()
        {
            Assert.That(NextStepMonitor.IsToolActive, Is.False);

            AssistantSignals.Emit(AssistantSignalKind.ToolActive, "WallBuildingSystem");
            Assert.That(NextStepMonitor.IsToolActive, Is.True);

            AssistantSignals.Emit(AssistantSignalKind.ToolIdle, "WallBuildingSystem");
            Assert.That(NextStepMonitor.IsToolActive, Is.False);
        }
    }
}
