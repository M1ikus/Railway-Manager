using System.Collections.Generic;
using NUnit.Framework;
using DepotSystem.Furniture;
using DepotSystem.RoomLevel;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// M-Modernization MM-D9: testy WorkstationDefinitions — compound "stanowisko pracy"
    /// (biurko + monitor + krzesło = 1 komplet). Greedy count = min(liczba każdego komponentu).
    /// Czysta logika, EditMode. To "działanie obiektów" — meble składają się w funkcjonalne stanowiska.
    /// </summary>
    public class WorkstationCompoundTests
    {
        static List<PlacedFurnitureItem> Items(params string[] itemIds)
        {
            var list = new List<PlacedFurnitureItem>();
            int id = 1;
            foreach (var iid in itemIds)
                list.Add(new PlacedFurnitureItem { instanceId = id++, itemId = iid, depotId = 0 });
            return list;
        }

        [Test]
        public void IsCompound_RecognizesKnownCompounds()
        {
            Assert.That(WorkstationDefinitions.IsCompound("WorkstationOfficeComplete"), Is.True);
            Assert.That(WorkstationDefinitions.IsCompound("WorkstationTrafficComplete"), Is.True);
            Assert.That(WorkstationDefinitions.IsCompound("desk_office"), Is.False, "Pojedynczy mebel to nie compound.");
            Assert.That(WorkstationDefinitions.IsCompound("nieistniejący"), Is.False);
        }

        [Test]
        public void GetComponents_OfficeWorkstation_HasDeskMonitorChair()
        {
            var comps = WorkstationDefinitions.GetComponents("WorkstationOfficeComplete");
            Assert.That(comps, Does.Contain("desk_office"));
            Assert.That(comps, Does.Contain("monitor_desk"));
            Assert.That(comps, Does.Contain("chair_basic"), "Stanowisko biurowe wymaga krzesła.");
        }

        [Test]
        public void GetComponents_UnknownCompound_Empty()
        {
            Assert.That(WorkstationDefinitions.GetComponents("nieznany"), Is.Empty);
        }

        [Test]
        public void CountCompounds_CompleteSet_CountsOne()
        {
            // 1 biurko + 1 monitor + 1 krzesło = 1 kompletne stanowisko.
            var room = Items("desk_office", "monitor_desk", "chair_basic");
            Assert.That(WorkstationDefinitions.CountCompounds(room, "WorkstationOfficeComplete"), Is.EqualTo(1));
        }

        [Test]
        public void CountCompounds_ThreeOfEach_CountsThree()
        {
            var room = Items(
                "desk_office", "desk_office", "desk_office",
                "monitor_desk", "monitor_desk", "monitor_desk",
                "chair_basic", "chair_basic", "chair_basic");
            Assert.That(WorkstationDefinitions.CountCompounds(room, "WorkstationOfficeComplete"), Is.EqualTo(3),
                "3 komplety części → 3 stanowiska.");
        }

        [Test]
        public void CountCompounds_GreedyMin_LimitedByScarcestComponent()
        {
            // 3 biurka + 3 monitory ale tylko 2 krzesła → tylko 2 kompletne stanowiska (krzesło = wąskie gardło).
            var room = Items(
                "desk_office", "desk_office", "desk_office",
                "monitor_desk", "monitor_desk", "monitor_desk",
                "chair_basic", "chair_basic");
            Assert.That(WorkstationDefinitions.CountCompounds(room, "WorkstationOfficeComplete"), Is.EqualTo(2),
                "Greedy min — brak 3. krzesła ogranicza do 2 stanowisk.");
        }

        [Test]
        public void CountCompounds_MissingComponent_Zero()
        {
            // Biurka + monitory ale BRAK krzeseł → 0 kompletnych stanowisk.
            var room = Items("desk_office", "desk_office", "monitor_desk", "monitor_desk");
            Assert.That(WorkstationDefinitions.CountCompounds(room, "WorkstationOfficeComplete"), Is.EqualTo(0),
                "Brak krzesła → niekompletne stanowisko → 0.");
        }

        [Test]
        public void CountCompounds_IgnoresIrrelevantItems()
        {
            // Dodatkowe niezwiązane meble (np. szafka) nie liczą się do compound.
            var room = Items("desk_office", "monitor_desk", "chair_basic", "cabinet", "plant");
            Assert.That(WorkstationDefinitions.CountCompounds(room, "WorkstationOfficeComplete"), Is.EqualTo(1),
                "Obce meble ignorowane — nadal 1 stanowisko.");
        }

        [Test]
        public void CountCompounds_TrafficUsesConsole_NotDesk()
        {
            // Stanowisko dyżurnego = traffic_console (NIE desk_office) + monitor + krzesło.
            var officeParts = Items("desk_office", "monitor_desk", "chair_basic");
            Assert.That(WorkstationDefinitions.CountCompounds(officeParts, "WorkstationTrafficComplete"), Is.EqualTo(0),
                "Biurko biurowe nie tworzy stanowiska dyżurnego (wymaga konsoli).");

            var trafficParts = Items("traffic_console", "monitor_desk", "chair_basic");
            Assert.That(WorkstationDefinitions.CountCompounds(trafficParts, "WorkstationTrafficComplete"), Is.EqualTo(1));
        }

        [Test]
        public void CountCompounds_NullOrUnknown_Zero()
        {
            Assert.That(WorkstationDefinitions.CountCompounds(null, "WorkstationOfficeComplete"), Is.EqualTo(0));
            Assert.That(WorkstationDefinitions.CountCompounds(Items("desk_office"), "nieznany"), Is.EqualTo(0));
        }
    }
}
