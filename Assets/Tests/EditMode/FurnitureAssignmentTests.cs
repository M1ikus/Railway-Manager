using NUnit.Framework;
using DepotSystem;                       // RoomType (RoomDetectionSystem)
using DepotSystem.Furniture;             // ObjectFunction
using RailwayManager.Personnel;          // EmployeeRole
using RailwayManager.Personnel.Furniture; // FurnitureAssignmentService

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// M-Furniture: testy FurnitureAssignmentService — mapping rola pracownika → wymagany mebel/pokój.
    /// Czysta logika (switch), EditMode. To "działanie pracowników ↔ obiektów": kto potrzebuje biurka
    /// i jakiego (Office/Dispatcher → biurko biurowe, TrafficController → pulpit dyżurnego, reszta → brak).
    /// </summary>
    public class FurnitureAssignmentTests
    {
        // ── GetRequiredFunction ──────────────────────────────────────

        [Test]
        public void DeskRoles_RequireWorkstationOffice()
        {
            Assert.That(FurnitureAssignmentService.GetRequiredFunction(EmployeeRole.Office),
                Is.EqualTo(ObjectFunction.WorkstationOffice));
            Assert.That(FurnitureAssignmentService.GetRequiredFunction(EmployeeRole.Research),
                Is.EqualTo(ObjectFunction.WorkstationOffice));
            Assert.That(FurnitureAssignmentService.GetRequiredFunction(EmployeeRole.Dispatcher),
                Is.EqualTo(ObjectFunction.WorkstationOffice),
                "Dyspozytor też siedzi przy biurku biurowym (różni go pokój, nie funkcja).");
        }

        [Test]
        public void TrafficController_RequiresTrafficConsole()
        {
            Assert.That(FurnitureAssignmentService.GetRequiredFunction(EmployeeRole.TrafficController),
                Is.EqualTo(ObjectFunction.WorkstationTraffic),
                "Dyżurny ruchu używa pulpitu (WorkstationTraffic), nie zwykłego biurka.");
        }

        [Test]
        public void FieldRoles_RequireNoFurniture()
        {
            // Maszynista/konduktor/mechanik/sprzątacz/myjnia/kasjer — nie siedzą przy biurku.
            Assert.That(FurnitureAssignmentService.GetRequiredFunction(EmployeeRole.Driver), Is.Null);
            Assert.That(FurnitureAssignmentService.GetRequiredFunction(EmployeeRole.Conductor), Is.Null);
            Assert.That(FurnitureAssignmentService.GetRequiredFunction(EmployeeRole.Mechanic), Is.Null);
            Assert.That(FurnitureAssignmentService.GetRequiredFunction(EmployeeRole.Cleaner), Is.Null);
            Assert.That(FurnitureAssignmentService.GetRequiredFunction(EmployeeRole.WashBay), Is.Null);
            Assert.That(FurnitureAssignmentService.GetRequiredFunction(EmployeeRole.TicketClerk), Is.Null);
        }

        // ── GetPreferredRoomType ─────────────────────────────────────

        [Test]
        public void PreferredRoom_OfficeRolesGoToOffice()
        {
            Assert.That(FurnitureAssignmentService.GetPreferredRoomType(EmployeeRole.Office),
                Is.EqualTo(RoomType.Office));
            Assert.That(FurnitureAssignmentService.GetPreferredRoomType(EmployeeRole.Research),
                Is.EqualTo(RoomType.Office));
        }

        [Test]
        public void PreferredRoom_DispatcherAndTraffic_HaveOwnRooms()
        {
            // Dyspozytor i dyżurny mają DEDYKOWANE pokoje (mimo że dyspozytor używa biurka biurowego).
            Assert.That(FurnitureAssignmentService.GetPreferredRoomType(EmployeeRole.Dispatcher),
                Is.EqualTo(RoomType.Dispatcher));
            Assert.That(FurnitureAssignmentService.GetPreferredRoomType(EmployeeRole.TrafficController),
                Is.EqualTo(RoomType.TrafficController));
        }

        [Test]
        public void PreferredRoom_FieldRoles_NoPreference()
        {
            Assert.That(FurnitureAssignmentService.GetPreferredRoomType(EmployeeRole.Driver), Is.Null);
            Assert.That(FurnitureAssignmentService.GetPreferredRoomType(EmployeeRole.Mechanic), Is.Null);
            Assert.That(FurnitureAssignmentService.GetPreferredRoomType(EmployeeRole.Cleaner), Is.Null);
        }

        [Test]
        public void RequiredFunction_And_PreferredRoom_Consistent()
        {
            // Spójność: każda rola wymagająca biurka ma też preferowany pokój (i odwrotnie).
            foreach (EmployeeRole role in System.Enum.GetValues(typeof(EmployeeRole)))
            {
                bool needsFurniture = FurnitureAssignmentService.GetRequiredFunction(role).HasValue;
                bool hasPreferredRoom = FurnitureAssignmentService.GetPreferredRoomType(role).HasValue;
                Assert.That(needsFurniture, Is.EqualTo(hasPreferredRoom),
                    $"Rola {role}: wymaganie mebla i preferencja pokoju muszą być spójne.");
            }
        }
    }
}
