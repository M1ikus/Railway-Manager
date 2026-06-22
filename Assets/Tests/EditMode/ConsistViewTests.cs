using System.Collections.Generic;
using NUnit.Framework;
using DepotSystem;
using RailwayManager.Fleet;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>M-Windows P3: adaptery ConsistView (czyste, bez sceny).</summary>
    public class ConsistViewTests
    {
        [Test]
        public void Ctor_NullVehicleIds_BecomesEmptyNotNull()
        {
            var v = new ConsistView("k", "t", "c", "s", null);
            Assert.IsNotNull(v.VehicleIds);
            Assert.AreEqual(0, v.VehicleIds.Count);
        }

        [Test]
        public void FromFleetConsist_MapsAllFields()
        {
            var c = new FleetConsistData
            {
                name = "IC Krakowiak",
                route = "Linia R1",
                vehicleIds = new List<int> { 3, 7, 9 },
                status = FleetVehicleStatus.MovingOnMap
            };
            var v = ConsistView.FromFleetConsist(c);
            Assert.AreEqual("consist:fleet:IC Krakowiak", v.Key);
            Assert.AreEqual("IC Krakowiak", v.Title);
            Assert.AreEqual("Linia R1", v.Context);
            Assert.AreEqual("W trasie", v.Status);
            CollectionAssert.AreEqual(new[] { 3, 7, 9 }, new List<int>(v.VehicleIds));
        }

        [Test]
        public void FromFleetConsist_NullRoute_BecomesDash()
        {
            var c = new FleetConsistData
            {
                name = "X",
                route = null,
                vehicleIds = new List<int>(),
                status = FleetVehicleStatus.StoppedInDepot
            };
            var v = ConsistView.FromFleetConsist(c);
            Assert.AreEqual("—", v.Context);
            Assert.AreEqual("W zajezdni", v.Status);
        }

        [Test]
        public void FromFleetConsist_Null_SafeFallback()
        {
            var v = ConsistView.FromFleetConsist(null);
            Assert.AreEqual("consist:fleet:null", v.Key);
            Assert.AreEqual(0, v.VehicleIds.Count);
        }
    }
}
