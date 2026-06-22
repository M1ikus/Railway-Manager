namespace RailwayManager.Fleet
{
    /// <summary>Stałe globalne dla systemu taboru.</summary>
    public static class FleetConstants
    {
        // Seat layout grid
        public const int SLOTS_PER_COACH = 24;
        public const int VESTIBULE_SLOTS = 2;   // per end
        public const int WC_SLOTS = 2;          // per coach
        public const int GRID_COLS = 5;         // cross-section columns

        // Cart
        public const long DELIVERY_COST_PER_VEHICLE = 50_000;
    }
}
