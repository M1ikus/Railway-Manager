using UnityEngine;

namespace RailwayManager.Fleet
{
    /// <summary>
    /// Wycena wartości odsprzedaży pojazdu z floty gracza. Pure — testowalne w EditMode
    /// (wzór jak <see cref="FleetFuelMath"/>). Bazuje na cenie zakupu z historii pojazdu
    /// × współczynnik kondycji × haircut odsprzedaży (anty-arbitraż kup→sprzedaj).
    /// </summary>
    public static class FleetResaleMath
    {
        /// <summary>Cena zakupu [zł] z historii pojazdu (rekord typu Zakup*). 0 gdy brak/darmowy.</summary>
        public static long PurchasePriceZl(FleetVehicleData v)
        {
            if (v?.history == null) return 0;
            foreach (var rec in v.history)
            {
                if (rec == null) continue;
                if (rec.recordType == MaintenanceRecordTypes.Purchase
                 || rec.recordType == MaintenanceRecordTypes.PurchaseNew
                 || rec.recordType == MaintenanceRecordTypes.PurchaseUsed
                 || rec.recordType == MaintenanceRecordTypes.PurchaseNewConfigurable)
                    return rec.cost > 0 ? rec.cost : 0;
            }
            // Fallback: zakup jest zwykle pierwszym rekordem historii.
            return v.history.Count > 0 && v.history[0] != null && v.history[0].cost > 0
                ? v.history[0].cost : 0;
        }

        /// <summary>
        /// Wartość odsprzedaży [gr] = cena zakupu × (kondycja/100) × haircut.
        /// Haircut &lt; 1 zapobiega arbitrażowi kup→sprzedaj. 0 gdy brak ceny zakupu.
        /// </summary>
        public static long ResaleValueGroszy(FleetVehicleData v)
        {
            if (v == null) return 0;
            long purchaseZl = PurchasePriceZl(v);
            if (purchaseZl <= 0) return 0;
            float condFactor = Mathf.Clamp01(v.conditionPercent / 100f);
            double valueZl = purchaseZl * condFactor * FleetBalanceConstants.ResaleValueHaircut;
            return (long)(valueZl * 100.0);
        }
    }
}
