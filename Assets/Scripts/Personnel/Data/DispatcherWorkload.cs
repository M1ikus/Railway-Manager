using System;

namespace RailwayManager.Personnel
{
    /// <summary>
    /// M8-1 / D27: Runtime snapshot obciazenia dyspozytorow w firmie.
    /// Liczony przez <see cref="Runtime.DispatcherService"/> co tick dniowy.
    ///
    /// Capacity: suma <c>50 + 5×(skill-1)</c> per aktywny dyspozytor.
    /// Headcount: liczba wszystkich pracownikow firmy (minus Dispatcher sami).
    ///
    /// Statusy:
    /// - Normal: headcount ≤ capacity → auto-akcje instant
    /// - Delayed: 1.0–1.5× → auto-akcje z delay 2-6h
    /// - Critical: &gt;1.5× → random 20% akcji missed
    /// </summary>
    [Serializable]
    public class DispatcherWorkload
    {
        /// <summary>Suma capacity aktywnych dyspozytorow (50 + 5×(skill-1) per osobe).</summary>
        public int totalCapacity;

        /// <summary>Liczba aktywnych dyspozytorow (Available / OnShift, nie Sick/Retired/Fired).</summary>
        public int activeDispatcherCount;

        /// <summary>Liczba pozostalych pracownikow firmy (bez Dispatcher — oni nie obciazaja sami siebie).</summary>
        public int currentHeadcount;

        /// <summary>Pending auto-akcji w kolejce (np. L4 replacements czekajace na delayed processing).</summary>
        public int pendingActionsCount;

        public DispatcherStatus status = DispatcherStatus.Normal;

        // ── Helpery ───────────────────────────────────

        /// <summary>Wskaznik obciazenia: headcount / capacity. 1.0 = na krawedzi.</summary>
        public float CapacityRatio => totalCapacity > 0 ? (float)currentHeadcount / totalCapacity : 0f;

        /// <summary>Czy brak dyspozytorow w firmie (wszystko manualne).</summary>
        public bool NoDispatchersInCompany => activeDispatcherCount == 0;
    }
}
