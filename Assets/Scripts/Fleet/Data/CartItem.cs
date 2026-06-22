using System;

namespace RailwayManager.Fleet
{
    /// <summary>Pojedyncza pozycja w koszyku zamówień taboru.</summary>
    [Serializable]
    public class CartItem
    {
        public int cartId;
        public bool isNewVehicle;               // true = konfigurator, false = rynek wtórny

        public int quantity;

        // M-FC-2: nowy tabor z konfiguratora (wagon w M-FC-2, loko/EZT w M-FC-3+).
        // Gdy != null, CartProcessor generuje FleetVehicleData z VehicleConfiguration zamiast z NewVehicleModel.
        public VehicleConfiguration vehicleConfiguration;

        // Używany tabor
        public FleetMarketVehicle marketVehicle;

        // Dostawa
        public CartDeliveryMode deliveryMode;
        public string deliveryDepotName;
        public long deliveryCost;

        // Obliczone
        public long unitPrice;
        public long TotalPrice => unitPrice * (isNewVehicle ? quantity : 1) + deliveryCost;

        public string DisplayName => isNewVehicle
            ? $"Wagon konfigurowalny x{quantity}"
            : marketVehicle?.number ?? "?";
    }
}
