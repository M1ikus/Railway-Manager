using NUnit.Framework;
using RailwayManager.Core.Difficulty;
using RailwayManager.GameCreator;
using UnityEngine;

namespace RailwayManager.Tests.EditMode
{
    public class GameCreatorMappingTests
    {
        [Test]
        public void AutosaveDropdownIndexToMinutes_MapsKnownValuesAndFallsBackToFive()
        {
            Assert.That(GameCreatorUI.AutosaveDropdownIndexToMinutes(0), Is.EqualTo(5));
            Assert.That(GameCreatorUI.AutosaveDropdownIndexToMinutes(1), Is.EqualTo(10));
            Assert.That(GameCreatorUI.AutosaveDropdownIndexToMinutes(2), Is.EqualTo(15));
            Assert.That(GameCreatorUI.AutosaveDropdownIndexToMinutes(3), Is.EqualTo(30));
            Assert.That(GameCreatorUI.AutosaveDropdownIndexToMinutes(-1), Is.EqualTo(5));
            Assert.That(GameCreatorUI.AutosaveDropdownIndexToMinutes(99), Is.EqualTo(5));
        }

        [Test]
        public void MaxPlayersDropdown_RoundTripsKnownValuesAndUsesFourPlayerFallback()
        {
            var expected = new[] { 2, 4, 6, 8, 12, 16 };

            for (int idx = 0; idx < expected.Length; idx++)
            {
                int count = GameCreatorUI.MaxPlayersDropdownIndexToCount(idx);

                Assert.That(count, Is.EqualTo(expected[idx]));
                Assert.That(GameCreatorUI.CountToMaxPlayersDropdownIndex(count), Is.EqualTo(idx));
            }

            Assert.That(GameCreatorUI.MaxPlayersDropdownIndexToCount(99), Is.EqualTo(4));
            Assert.That(GameCreatorUI.CountToMaxPlayersDropdownIndex(999), Is.EqualTo(1));
        }

        [Test]
        public void DifficultyPresetDropdown_RoundTripsAndFallsBackToNormal()
        {
            var expected = new[]
            {
                DifficultyPreset.Easy,
                DifficultyPreset.Normal,
                DifficultyPreset.Hard,
                DifficultyPreset.Realistic,
                DifficultyPreset.Custom
            };

            for (int idx = 0; idx < expected.Length; idx++)
            {
                var preset = GameCreatorUI.DropdownIndexToPreset(idx);

                Assert.That(preset, Is.EqualTo(expected[idx]));
                Assert.That(GameCreatorUI.PresetToDropdownIndex(preset), Is.EqualTo(idx));
            }

            Assert.That(GameCreatorUI.DropdownIndexToPreset(99), Is.EqualTo(DifficultyPreset.Normal));
        }

        [Test]
        public void DifficultyPresetKeys_AreNonEmptyForAllPresetValues()
        {
            foreach (DifficultyPreset preset in System.Enum.GetValues(typeof(DifficultyPreset)))
            {
                string locKey = GameCreatorUI.GetPresetLocKey(preset);
                string descKey = GameCreatorUI.GetPresetDescKey(preset);

                Assert.That(locKey, Is.Not.Empty);
                Assert.That(descKey, Is.Not.Empty);
                Assert.That(locKey, Does.StartWith("difficulty.preset"));
                Assert.That(descKey, Does.StartWith("difficulty.preset_desc"));
            }
        }

        [Test]
        public void DifficultyPresetCatalog_CustomAndNormalAreNeutralButHardIsNot()
        {
            AssertAllNeutral(DifficultyPresetCatalog.Get(DifficultyPreset.Custom));
            AssertAllNeutral(DifficultyPresetCatalog.Get(DifficultyPreset.Normal));

            var hard = DifficultyPresetCatalog.Get(DifficultyPreset.Hard);
            Assert.That(Mathf.Approximately(hard.StartBudgetMultiplier, 1f), Is.False);
            Assert.That(Mathf.Approximately(hard.OperationalCostMultiplier, 1f), Is.False);
            Assert.That(Mathf.Approximately(hard.BreakdownChanceMultiplier, 1f), Is.False);
        }

        [Test]
        public void FormatSeedFieldText_HidesZeroButKeepsOtherValues()
        {
            Assert.That(GameCreatorUI.FormatSeedFieldText(0), Is.EqualTo(""));
            Assert.That(GameCreatorUI.FormatSeedFieldText(42), Is.EqualTo("42"));
            Assert.That(GameCreatorUI.FormatSeedFieldText(-1), Is.EqualTo("-1"));
            Assert.That(GameCreatorUI.FormatSeedFieldText(int.MaxValue), Is.EqualTo(int.MaxValue.ToString()));
        }

        private static void AssertAllNeutral(DifficultyModifiers modifiers)
        {
            Assert.That(modifiers, Is.Not.Null);
            Assert.That(Mathf.Approximately(modifiers.StartBudgetMultiplier, 1f), Is.True);
            Assert.That(Mathf.Approximately(modifiers.OperationalCostMultiplier, 1f), Is.True);
            Assert.That(Mathf.Approximately(modifiers.BreakdownChanceMultiplier, 1f), Is.True);
            Assert.That(Mathf.Approximately(modifiers.PassengerDemandMultiplier, 1f), Is.True);
            Assert.That(Mathf.Approximately(modifiers.SalaryMultiplier, 1f), Is.True);
            Assert.That(Mathf.Approximately(modifiers.SubsidyMultiplier, 1f), Is.True);
            Assert.That(Mathf.Approximately(modifiers.DelayPropagationMultiplier, 1f), Is.True);
            Assert.That(Mathf.Approximately(modifiers.EventFrequencyMultiplier, 1f), Is.True);
            Assert.That(Mathf.Approximately(modifiers.HotelCostMultiplier, 1f), Is.True);
            Assert.That(Mathf.Approximately(modifiers.TicketPriceToleranceMultiplier, 1f), Is.True);
        }
    }
}
