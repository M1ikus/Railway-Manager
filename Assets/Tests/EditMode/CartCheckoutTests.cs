using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RailwayManager.Core;
using RailwayManager.Economy;
using RailwayManager.Fleet;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// M-Economy Faza 3: CartProcessor.TryCheckout — naprawa darmowego zakupu taboru. Sprawdza
    /// affordability gate (nie stać → blokada, zero pobrania, zero pojazdów) + pobranie kasy +
    /// dodanie pojazdu + zapis do bilansu. EditMode (cleanup dodanych pojazdów w TearDown).
    /// </summary>
    public class CartCheckoutTests
    {
        long _moneyBackup;
        List<int> _ownedBefore;

        [SetUp]
        public void SetUp()
        {
            _moneyBackup = GameState.Money;
            MoneyLedger.ResetAll();
            _ownedBefore = FleetService.OwnedVehicles.Select(v => v.id).ToList();
        }

        [TearDown]
        public void TearDown()
        {
            var added = FleetService.OwnedVehicles.Where(v => !_ownedBefore.Contains(v.id)).Select(v => v.id).ToList();
            foreach (var id in added) FleetService.RemoveOwnedVehicle(id);
            MoneyLedger.ResetAll();
            GameState.Money = _moneyBackup;
        }

        static CartItem UsedItem(long unitPriceZl) => new CartItem
        {
            isNewVehicle = false,
            quantity = 1,
            unitPrice = unitPriceZl,
            deliveryCost = 0,
            marketVehicle = new FleetMarketVehicle
            {
                seriesId = "TEST", family = "TEST", series = "Test", number = "T-1",
                type = FleetVehicleType.EMU, location = "Testowo",
            }
        };

        [Test]
        public void CartTotalZl_SumsItems()
        {
            var cart = new List<CartItem> { UsedItem(1000), UsedItem(2500) };
            Assert.That(CartProcessor.CartTotalZl(cart), Is.EqualTo(3500L));
        }

        [Test]
        public void TryCheckout_Insufficient_Blocks_NoCharge_NoVehicles()
        {
            GameState.Money = 500; // zł
            var cart = new List<CartItem> { UsedItem(1000) };
            int ownedCount = FleetService.OwnedVehicles.Count;

            var r = CartProcessor.TryCheckout(cart);

            Assert.That(r.InsufficientFunds, Is.True, "Nie stać → blokada.");
            Assert.That(r.Success, Is.False);
            Assert.That(GameState.Money, Is.EqualTo(500), "Money bez zmian (nie pobrano).");
            Assert.That(FleetService.OwnedVehicles.Count, Is.EqualTo(ownedCount), "Brak dodanych pojazdów (all-or-nothing).");
        }

        [Test]
        public void TryCheckout_Affordable_Charges_AndAddsVehicle_AndBilans()
        {
            GameState.Money = 100000; // zł
            var cart = new List<CartItem> { UsedItem(1000) };
            int ownedBefore = FleetService.OwnedVehicles.Count;

            var r = CartProcessor.TryCheckout(cart);

            Assert.That(r.Success, Is.True);
            Assert.That(r.Added, Is.EqualTo(1), "1 pojazd dodany.");
            Assert.That(r.TotalZl, Is.EqualTo(1000L));
            Assert.That(GameState.Money, Is.EqualTo(99000), "Money − 1000 zł (POBRANO — naprawa darmowego zakupu).");
            Assert.That(MoneyLedger.CostsTodayGroszy, Is.EqualTo(100000L), "Zakup w bilansie dziennym (1000 zł = 100000 gr).");
            Assert.That(FleetService.OwnedVehicles.Count, Is.EqualTo(ownedBefore + 1));
        }
    }
}
