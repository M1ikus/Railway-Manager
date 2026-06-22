using UnityEngine;

namespace RailwayManager.Timetable.Simulation
{
    /// <summary>
    /// M-Dispatch Faza 4c: efektywna WAGA pociągu w decyzji dispatchera (im wyższa, tym
    /// bardziej nie chcemy go opóźniać). Czysta funkcja — łatwo testowalna i deterministyczna.
    ///
    /// waga = priorytetIRJ × DispatchPriorityScale + clamp(obłożenie, 0, DispatchMaxLoadWeight)
    ///
    /// Dzięki temu zapchany osobowy (dużo pasażerów) może przeważyć pusty pośpieszny — czego
    /// chciał user („ważenie obłożeniem"). Obłożenie czytane O(1) z PassengerManager.CountOnTrain
    /// w orchestracji (tu tylko formuła, bez zależności od sceny).
    ///
    /// UWAGA: pole <c>TrainForecast.priority</c> przechowuje TĘ wagę (nie surowy priorytet IRJ).
    /// </summary>
    public static class DispatchWeight
    {
        public static int Compute(int irjPriority, int onboardLoad)
        {
            int loadBonus = Mathf.Clamp(onboardLoad, 0, TimetableTuningConstants.DispatchMaxLoadWeight);
            return irjPriority * TimetableTuningConstants.DispatchPriorityScale + loadBonus;
        }
    }
}
