using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using DepotSystem.Furniture;
using DepotSystem.Furniture.Placement;
using RailwayManager.Core;

namespace RailwayManager.Tests.PlayMode
{
    /// <summary>
    /// TD-034 PlayMode: runtime semantyka rezerwacji <see cref="FurnitureOccupancyService"/> (sloty per-mebel).
    /// Czysty wybór (FurnitureOccupancyMath) + harmonogram (PersonalNeedSchedule) pokryte EditMode;
    /// pełny łańcuch wizyty + kolejka meldunku wymagają sceny Depot z meblami → weryfikacja manualna
    /// (Depot.unity, sekcja Weryfikacja w planie). Tu: singleton lifecycle + dict rezerwacji + graceful no-furniture.
    /// </summary>
    public class DepotPersonnelActivityPlayTests
    {
        GameObject _go;
        FurnitureOccupancyService _svc;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            PauseStack.Clear();
            GameState.IsPaused = false;
            // Izolacja: poprzedni singleton mógłby przeżyć → duplicate-guard zniszczyłby świeży.
            if (FurnitureOccupancyService.Instance != null)
                Object.DestroyImmediate(FurnitureOccupancyService.Instance.gameObject);

            _go = new GameObject("TestOccupancy");
            _svc = _go.AddComponent<FurnitureOccupancyService>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
            LogAssert.ignoreFailingMessages = false;
        }

        [UnityTest]
        public IEnumerator Reserve_OccupiesAndTracksOwner()
        {
            Assert.IsTrue(_svc.IsFree(5), "Świeży mebel wolny.");
            Assert.IsTrue(_svc.TryReserve(5, 100), "Rezerwacja wolnego → true.");
            Assert.IsFalse(_svc.IsFree(5), "Po rezerwacji zajęty.");
            Assert.AreEqual(100, _svc.GetOwner(5), "Owner zapamiętany.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator Reserve_SameOwnerIdempotent_OtherOwnerFails()
        {
            Assert.IsTrue(_svc.TryReserve(7, 100));
            Assert.IsTrue(_svc.TryReserve(7, 100), "Ten sam owner ponownie → idempotentne true.");
            Assert.IsFalse(_svc.TryReserve(7, 200), "Inny owner na zajętym → false.");
            Assert.AreEqual(100, _svc.GetOwner(7), "Owner niezmieniony.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator Release_ClearsAllOwnerReservations_LeavesOthers()
        {
            _svc.TryReserve(1, 100);
            _svc.TryReserve(2, 100);
            _svc.TryReserve(3, 200);
            _svc.Release(100);
            Assert.IsTrue(_svc.IsFree(1), "Mebel 1 ownera 100 zwolniony.");
            Assert.IsTrue(_svc.IsFree(2), "Mebel 2 ownera 100 zwolniony.");
            Assert.IsFalse(_svc.IsFree(3), "Rezerwacja innego ownera (200) nietknięta.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator ReleaseInstance_FreesOne()
        {
            _svc.TryReserve(9, 100);
            _svc.ReleaseInstance(9);
            Assert.IsTrue(_svc.IsFree(9), "ReleaseInstance zwalnia konkretny mebel.");
            Assert.AreEqual(-1, _svc.GetOwner(9), "Brak ownera po zwolnieniu.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator FindAndSeat_GracefulWithoutFurniture()
        {
            // Bez instancji mebli (brak/empty FurniturePlacer) — brak wyjątku, sensowne wartości.
            int found = _svc.FindNearestFreeByFunction(ObjectFunction.Sanitary, Vector3.zero, null);
            Assert.GreaterOrEqual(found, -1, "Find nie rzuca, zwraca instanceId lub -1.");
            Assert.IsNull(_svc.GetSeatPoint(987654), "Nieistniejąca instancja → null seat point (bez NRE).");
            yield return null;
        }
    }
}
