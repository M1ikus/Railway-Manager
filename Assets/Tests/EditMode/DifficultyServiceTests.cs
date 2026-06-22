using NUnit.Framework;
using RailwayManager.Core.Difficulty;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// TD-008: kontrakt ShowInspectionColumnHint — Easy ukrywa kolumnę "Przegląd" (mniej złożoności
    /// dla nowego gracza), pozostałe presety pokazują. Konsumowane przez FleetPanelUI.ShowInspectionColumn.
    /// DifficultyService jest static → reset presetu w TearDown by uniknąć przecieku stanu między testami.
    /// </summary>
    public class DifficultyServiceTests
    {
        [TearDown]
        public void Reset() => DifficultyService.ResetToDefault();

        [Test]
        public void Easy_HidesInspectionColumn()
        {
            DifficultyService.ApplyNewGameConfig(DifficultyPreset.Easy);
            Assert.IsFalse(DifficultyService.ShowInspectionColumnHint);
        }

        [Test]
        public void Normal_ShowsInspectionColumn()
        {
            DifficultyService.ApplyNewGameConfig(DifficultyPreset.Normal);
            Assert.IsTrue(DifficultyService.ShowInspectionColumnHint);
        }

        [Test]
        public void Hard_ShowsInspectionColumn()
        {
            DifficultyService.ApplyNewGameConfig(DifficultyPreset.Hard);
            Assert.IsTrue(DifficultyService.ShowInspectionColumnHint);
        }

        [Test]
        public void Realistic_ShowsInspectionColumn()
        {
            DifficultyService.ApplyNewGameConfig(DifficultyPreset.Realistic);
            Assert.IsTrue(DifficultyService.ShowInspectionColumnHint);
        }

        [Test]
        public void Custom_ShowsInspectionColumn()
        {
            DifficultyService.ApplyNewGameConfig(DifficultyPreset.Custom);
            Assert.IsTrue(DifficultyService.ShowInspectionColumnHint);
        }
    }
}
