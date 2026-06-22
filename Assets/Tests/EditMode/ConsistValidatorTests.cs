using System.Collections.Generic;
using NUnit.Framework;
using RailwayManager.Fleet;
using RailwayManager.Timetable; // ConsistValidator żyje w Timetable namespace

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// M5: testy ConsistValidator — reguły łączenia pojazdów w skład (realne PKP). Czysta logika,
    /// EditMode. Resolve typu po FleetService.OwnedVehicles, więc test seeduje flotę i sprząta.
    ///
    /// Reguły: pusty→Error, sam wagon→Error, EMU/DMU solo→Ok, loko luzem→Warning, loko+wagony→Ok
    /// (loko na przodzie), loko+EMU→Error, EMU+wagon→Error, EMU+DMU→Error, DMU+wagon→Ok (na przodzie).
    /// </summary>
    public class ConsistValidatorTests
    {
        readonly List<int> _ids = new();
        int _nextId = 940000;

        [TearDown]
        public void TearDown()
        {
            foreach (int id in _ids) FleetService.RemoveOwnedVehicle(id);
            _ids.Clear();
        }

        /// <summary>Dodaje pojazd danego typu do floty, zwraca jego id.</summary>
        int Add(FleetVehicleType type)
        {
            int id = _nextId++;
            FleetService.AddOwnedVehicle(new FleetVehicleData { id = id, type = type, series = type.ToString() });
            _ids.Add(id);
            return id;
        }

        static ConsistValidator.Severity Sev(List<int> consist) => ConsistValidator.Validate(consist).severity;

        [Test]
        public void EmptyConsist_IsError()
        {
            Assert.That(ConsistValidator.Validate(new List<int>()).severity,
                Is.EqualTo(ConsistValidator.Severity.Error));
            Assert.That(ConsistValidator.Validate(null).severity,
                Is.EqualTo(ConsistValidator.Severity.Error));
        }

        [Test]
        public void NonexistentVehicle_IsError()
        {
            Assert.That(ConsistValidator.Validate(new List<int> { 123456789 }).severity,
                Is.EqualTo(ConsistValidator.Severity.Error), "Pojazd spoza floty → Error.");
        }

        [Test]
        public void SingleEmu_IsOk()
        {
            Assert.That(Sev(new List<int> { Add(FleetVehicleType.EMU) }),
                Is.EqualTo(ConsistValidator.Severity.Ok), "Samoczynny EMU → Ok.");
        }

        [Test]
        public void SingleDmu_IsOk()
        {
            Assert.That(Sev(new List<int> { Add(FleetVehicleType.DMU) }),
                Is.EqualTo(ConsistValidator.Severity.Ok));
        }

        [Test]
        public void SingleLocomotive_IsWarning()
        {
            // Loko luzem — OK tylko dla kursów służbowych → Warning (nie Error, nie Ok).
            Assert.That(Sev(new List<int> { Add(FleetVehicleType.ElectricLocomotive) }),
                Is.EqualTo(ConsistValidator.Severity.Warning));
        }

        [Test]
        public void SingleCar_IsError()
        {
            Assert.That(Sev(new List<int> { Add(FleetVehicleType.PassengerCar) }),
                Is.EqualTo(ConsistValidator.Severity.Error), "Sam wagon nie jedzie → Error.");
        }

        [Test]
        public void LocoWithCars_LocoAtFront_IsOk()
        {
            var c = new List<int> { Add(FleetVehicleType.ElectricLocomotive),
                                    Add(FleetVehicleType.PassengerCar), Add(FleetVehicleType.PassengerCar) };
            Assert.That(Sev(c), Is.EqualTo(ConsistValidator.Severity.Ok), "Loko na przodzie + wagony → Ok.");
        }

        [Test]
        public void CarsBeforeLoco_IsError()
        {
            // Wagon na pozycji 0, loko za nim → loko musi być na przodzie → Error.
            var c = new List<int> { Add(FleetVehicleType.PassengerCar), Add(FleetVehicleType.ElectricLocomotive) };
            Assert.That(Sev(c), Is.EqualTo(ConsistValidator.Severity.Error));
        }

        [Test]
        public void LocoMixedWithEmu_IsError()
        {
            var c = new List<int> { Add(FleetVehicleType.ElectricLocomotive), Add(FleetVehicleType.EMU) };
            Assert.That(Sev(c), Is.EqualTo(ConsistValidator.Severity.Error),
                "Loko + EMU → Error (nie mieszać trakcji).");
        }

        [Test]
        public void EmuWithCar_IsError()
        {
            var c = new List<int> { Add(FleetVehicleType.EMU), Add(FleetVehicleType.PassengerCar) };
            Assert.That(Sev(c), Is.EqualTo(ConsistValidator.Severity.Error),
                "EMU nie ciągnie wagonów → Error.");
        }

        [Test]
        public void EmuWithDmu_IsError()
        {
            var c = new List<int> { Add(FleetVehicleType.EMU), Add(FleetVehicleType.DMU) };
            Assert.That(Sev(c), Is.EqualTo(ConsistValidator.Severity.Error), "EMU + DMU → Error.");
        }

        [Test]
        public void MultipleEmu_IsOk()
        {
            var c = new List<int> { Add(FleetVehicleType.EMU), Add(FleetVehicleType.EMU) };
            Assert.That(Sev(c), Is.EqualTo(ConsistValidator.Severity.Ok), "Wielokrotne EMU → Ok.");
        }

        [Test]
        public void DmuWithCar_DmuAtFront_IsOk()
        {
            // Szynobus ciągnący wagon — DMU na przodzie → Ok.
            var c = new List<int> { Add(FleetVehicleType.DMU), Add(FleetVehicleType.PassengerCar) };
            Assert.That(Sev(c), Is.EqualTo(ConsistValidator.Severity.Ok), "DMU + wagon (DMU z przodu) → Ok.");
        }

        [Test]
        public void CanAdd_DetectsIncompatibleBeforeAdding()
        {
            // CanAdd na hipotetycznym wyniku: do EMU próbujemy dodać wagon → Error.
            int emu = Add(FleetVehicleType.EMU);
            int car = Add(FleetVehicleType.PassengerCar);
            Assert.That(ConsistValidator.CanAdd(new List<int> { emu }, car).IsBlocking, Is.True,
                "CanAdd wykrywa niekompatybilność przed fizycznym dodaniem.");
        }
    }
}
