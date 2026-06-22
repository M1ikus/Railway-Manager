using System.Collections.Generic;
using NUnit.Framework;
using RailwayManager.Fleet;
using DepotSystem;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// TD-031 Etap D: testy reguły napędu (DepotMovementSimulator.ConsistHasTraction). Seeduje flotę
    /// (FleetService.OwnedVehicles) i sprząta po sobie. Loco/EMU/DMU → true; sam wagon / PassengerCar /
    /// powerKw 0 / [None] / pusty / nieistniejący → false.
    /// </summary>
    public class ConsistTractionTests
    {
        readonly List<int> _ids = new();
        int _nextId = 950000;

        [TearDown]
        public void TearDown()
        {
            foreach (int id in _ids) FleetService.RemoveOwnedVehicle(id);
            _ids.Clear();
        }

        int Add(FleetVehicleType type, int powerKw, params TractionType[] tractions)
        {
            int id = _nextId++;
            FleetService.AddOwnedVehicle(new FleetVehicleData
            {
                id = id,
                type = type,
                powerKw = powerKw,
                supportedTractions = new List<TractionType>(tractions)
            });
            _ids.Add(id);
            return id;
        }

        [Test]
        public void Empty_NoTraction()
        {
            Assert.That(DepotMovementSimulator.ConsistHasTraction(new List<int>()), Is.False);
            Assert.That(DepotMovementSimulator.ConsistHasTraction(null), Is.False);
        }

        [Test]
        public void SingleWagon_NoTraction()
        {
            int w = Add(FleetVehicleType.PassengerCar, 0, TractionType.None);
            Assert.That(DepotMovementSimulator.ConsistHasTraction(new List<int> { w }), Is.False, "Sam wagon nie ma napędu.");
        }

        [Test]
        public void TwoWagonsOnly_NoTraction()
        {
            int w1 = Add(FleetVehicleType.PassengerCar, 0, TractionType.None);
            int w2 = Add(FleetVehicleType.PassengerCar, 0, TractionType.None);
            Assert.That(DepotMovementSimulator.ConsistHasTraction(new List<int> { w1, w2 }), Is.False);
        }

        [Test]
        public void ElectricLoco_HasTraction()
        {
            int l = Add(FleetVehicleType.ElectricLocomotive, 2000, TractionType.Electric);
            Assert.That(DepotMovementSimulator.ConsistHasTraction(new List<int> { l }), Is.True);
        }

        [Test]
        public void Emu_HasTraction()
        {
            int e = Add(FleetVehicleType.EMU, 580, TractionType.Electric);
            Assert.That(DepotMovementSimulator.ConsistHasTraction(new List<int> { e }), Is.True);
        }

        [Test]
        public void Dmu_HasTraction()
        {
            int d = Add(FleetVehicleType.DMU, 390, TractionType.Diesel);
            Assert.That(DepotMovementSimulator.ConsistHasTraction(new List<int> { d }), Is.True);
        }

        [Test]
        public void LocoPlusWagons_HasTraction()
        {
            int l = Add(FleetVehicleType.ElectricLocomotive, 2000, TractionType.Electric);
            int w1 = Add(FleetVehicleType.PassengerCar, 0, TractionType.None);
            int w2 = Add(FleetVehicleType.PassengerCar, 0, TractionType.None);
            Assert.That(DepotMovementSimulator.ConsistHasTraction(new List<int> { l, w1, w2 }), Is.True, "Loko + wagony → ma napęd.");
        }

        [Test]
        public void NonexistentIds_NoTraction()
        {
            Assert.That(DepotMovementSimulator.ConsistHasTraction(new List<int> { 123456789 }), Is.False);
        }
    }
}
