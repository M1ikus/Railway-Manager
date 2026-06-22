using System;
using NUnit.Framework;
using RailwayManager.Fleet;
using RailwayManager.Personnel;
using RailwayManager.Timetable;
using RailwayManager.Timetable.Economy;
using DepotSystem;
using DepotSystem.Furniture;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// ZAPORA na cichą korupcję save'ów (crash-hunt #6): enumy serializowane do save'a jako int.
    /// Wstawienie/przestawienie wartości w ŚRODKU enuma → stary save z int=N mapuje na INNĄ wartość
    /// → cicha korupcja danych (pojazd zmienia status, pracownik rolę, itp.).
    ///
    /// Test pinuje KOLEJNOŚĆ nazw (= mapowanie int, bo implicit ordering). Każda zmiana save-enuma
    /// (nawet bezpieczny append) zapala test → zmusza do ŚWIADOMEJ decyzji: append na końcu (OK) vs
    /// insert/reorder (psuje save'y → wymaga migracji). Pin = forsuje review, nie blokuje rozwoju.
    /// </summary>
    public class EnumStabilityTests
    {
        static void AssertOrder<T>(params string[] expected) where T : Enum
        {
            Assert.That(Enum.GetNames(typeof(T)), Is.EqualTo(expected),
                $"Kolejność/wartości {typeof(T).Name} zmieniona — to enum SAVE'OWY. Append na końcu = OK " +
                "(dopisz nazwę tutaj). Insert/reorder = PSUJE stare save'y (wymaga migratora/explicit int).");
        }

        [Test] public void FleetVehicleStatus_Stable() => AssertOrder<FleetVehicleStatus>(
            "MovingOnMap", "StoppedOnMap", "StoppedInDepot", "MovingInDepot", "InRepair",
            "OutOfService", "InProduction", "InTransit", "AwaitingPickup");

        [Test] public void FleetVehicleType_Stable() => AssertOrder<FleetVehicleType>(
            "ElectricLocomotive", "DieselLocomotive", "EMU", "DMU", "PassengerCar");

        [Test] public void TractionType_Stable() => AssertOrder<TractionType>(
            "Electric", "Diesel", "None");

        [Test] public void EmployeeRole_Stable() => AssertOrder<EmployeeRole>(
            "Driver", "Conductor", "Mechanic", "Cleaner", "WashBay", "Office", "Research",
            "TicketClerk", "Dispatcher", "TrafficController");

        [Test] public void EmployeeStatus_Stable() => AssertOrder<EmployeeStatus>(
            "Available", "OnShift", "Resting", "Sick", "LongSick", "Training",
            "Retired", "Fired", "Onboarding");

        [Test] public void ShiftType_Stable() => AssertOrder<ShiftType>(
            "Morning", "Afternoon", "Night");

        [Test] public void WorkCyclePattern_Stable() => AssertOrder<WorkCyclePattern>(
            "Cycle5_2", "Cycle4_2", "Cycle6_2", "Cycle7_7", "Custom");

        [Test] public void ScheduleOverrideType_Stable() => AssertOrder<ScheduleOverrideType>(
            "Vacation", "SickLeave", "Training", "ShiftSwap", "ExtraDutyDay", "FreeDay");

        [Test] public void CompositionMode_Stable() => AssertOrder<CompositionMode>(
            "MultipleUnit", "LocoWithCars");

        [Test] public void TractionLetter_Stable() => AssertOrder<TractionLetter>(
            "ElectricLoco", "ElectricUnit", "DieselLoco", "DieselUnit");

        [Test] public void BoundaryType_Stable() => AssertOrder<BoundaryType>(
            "Junction", "Signal", "LineEnd", "Station");

        [Test] public void PassengerState_Stable() => AssertOrder<PassengerState>(
            "WaitingAtStation", "Boarding", "OnTrain", "Alighting", "Arrived", "Abandoned");

        [Test] public void PassengerPreference_Stable() => AssertOrder<PassengerPreference>(
            "Cheapest", "Fastest", "MostComfort");

        [Test] public void RoomType_Stable() => AssertOrder<RoomType>(
            "None", "Hall", "Storage", "Dispatcher", "Office", "Social",
            "Supervisor", "Bathroom", "Locker", "Corridor", "TrafficController");

        [Test] public void IrjGroup_Stable() => AssertOrder<IrjGroup>(
            "ExpressDomestic", "ExpressInternational", "ExpressInternationalNight", "InterregionalFast",
            "InterregionalFastNight", "InternationalFast", "InterregionalLocal", "RegionalFast",
            "RegionalAgglomeration", "RegionalInternational", "RegionalLocal", "EmptyPassenger",
            "EmptyPassengerTest", "FreightIntlIntermodal", "FreightIntlMass", "FreightIntlNonMass",
            "FreightDomesticIntermodal", "FreightDomesticMass", "FreightDomesticNonMass",
            "FreightStationService", "FreightEmptyTest", "LoneLocoPassenger", "LoneLocoFreight",
            "LoneLocoShunt", "MaintenanceInspection");

        [Test] public void ObjectFunction_Stable() => AssertOrder<ObjectFunction>(
            "None", "WorkstationDispatcher", "WorkstationOffice", "WorkstationSupervisor",
            "WorkstationTraffic", "ServicePit", "WashStation", "Refueling", "Painting", "WaterService",
            "ToolStorage", "StorageGoods", "StoragePersonal", "SeatingRest", "SeatingMeal",
            "Kitchen", "Sanitary", "Passage", "Decoration");
    }
}
