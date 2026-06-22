using NUnit.Framework;
using RailwayManager.Timetable.Economy;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// M11 AS-P3: kontrakt fasady DemandQuery — null-safe przed bootstrapem sceny
    /// (planner/advisor wołają bez sprawdzania Instance; brak managera = wartości puste,
    /// nie NRE). Sama matematyka OD matrix pokryta w OriginDestinationMatrixTests.
    /// </summary>
    public class DemandQueryTests
    {
        [Test]
        public void WithoutPassengerManager_ReturnsEmptyValuesNotNre()
        {
            if (PassengerManager.Instance != null)
            {
                Assert.Pass("Scena z PassengerManagerem — null-path nietestowalny w tym środowisku");
            }

            Assert.That(DemandQuery.BaseDailyDemand(1, 2), Is.EqualTo(0f));
            Assert.That(DemandQuery.EstimatedDailyDemand(1, 2), Is.EqualTo(0f));
            Assert.That(DemandQuery.IsPairServedByOffer(1, 2), Is.False);
        }
    }
}
