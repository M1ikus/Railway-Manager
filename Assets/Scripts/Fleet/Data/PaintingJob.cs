using System;

namespace RailwayManager.Fleet
{
    /// <summary>
    /// M-FC-9: Aktywne zlecenie malowania pojazdu w ZNTK.
    /// Po wysłaniu pojazd ma status OutOfService aż do completionGameTime.
    /// </summary>
    [Serializable]
    public class PaintingJob
    {
        public int vehicleId;
        public string workshopId;
        public long scheduledGameTime;
        public long completionGameTime;
        public long costPln;
        /// <summary>Paint definition do zaaplikowania po skończeniu (gracz albo zachowuje obecny, albo edytuje przed wysłaniem).</summary>
        public PaintDefinition newPaint;
    }
}
