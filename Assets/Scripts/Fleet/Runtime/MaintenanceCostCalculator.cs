namespace RailwayManager.Fleet
{
    /// <summary>
    /// Oblicza miesięczny koszt utrzymania pojazdu na podstawie typu, wieku i stanu.
    /// </summary>
    public static class MaintenanceCostCalculator
    {
        // Bazowe koszty + multipliery — patrz FleetBalanceConstants.MaintenanceMonthly*.

        /// <summary>
        /// Miesięczny koszt utrzymania (PLN/miesiąc).
        ///
        /// B6 (2026-05-13): preferowana ścieżka to formuła per-pojazd z
        /// <see cref="FleetVehicleData.operationalCostPerKmGroszy"/> (catalog: families.json /
        /// initial_market.json / new_models.json) × avgMonthlyMileage. Wartości real-world PKP
        /// (290-900 gr/km). Fallback gdy `operationalCostPerKmGroszy = 0` (np. starting fleet) —
        /// per-type heurystyka.
        ///
        /// Rośnie wraz z wiekiem i pogarszającym się stanem (multipliery wspólne dla obu ścieżek).
        /// </summary>
        public static long Calculate(FleetVehicleData v, int currentGameYear)
        {
            int age = currentGameYear - v.productionYear;
            if (age < 0) age = 0;
            float ageMultiplier = 1f + (age * FleetBalanceConstants.MaintenanceAgeMultiplierPerYear);
            if (ageMultiplier > FleetBalanceConstants.MaintenanceAgeMultiplierMax)
                ageMultiplier = FleetBalanceConstants.MaintenanceAgeMultiplierMax;

            float conditionMultiplier = FleetBalanceConstants.MaintenanceConditionMultiplierMax - (v.conditionPercent / 100f);
            if (conditionMultiplier < 1f) conditionMultiplier = 1f;

            long baseCost;
            if (v.operationalCostPerKmGroszy > 0)
            {
                // Catalog-driven: avgMileage × cost/km → monthly
                long monthlyGroszy = (long)FleetBalanceConstants.MaintenanceAvgMonthlyMileageKm * v.operationalCostPerKmGroszy;
                baseCost = monthlyGroszy / 100L; // groszy → PLN
            }
            else
            {
                baseCost = GetBaseCost(v.type);
            }

            return (long)(baseCost * ageMultiplier * conditionMultiplier);
        }

        public static long GetBaseCost(FleetVehicleType type) => type switch
        {
            FleetVehicleType.ElectricLocomotive => FleetBalanceConstants.MaintenanceMonthlyBaseElectricLoco,
            FleetVehicleType.DieselLocomotive   => FleetBalanceConstants.MaintenanceMonthlyBaseDieselLoco,
            FleetVehicleType.EMU                => FleetBalanceConstants.MaintenanceMonthlyBaseEmu,
            FleetVehicleType.DMU                => FleetBalanceConstants.MaintenanceMonthlyBaseDmu,
            FleetVehicleType.PassengerCar       => FleetBalanceConstants.MaintenanceMonthlyBasePassengerCar,
            _                                   => FleetBalanceConstants.MaintenanceMonthlyBaseOther,
        };
    }
}
