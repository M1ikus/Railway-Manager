using System.Collections.Generic;

namespace RailwayManager.Timetable.Simulation
{
    /// <summary>
    /// M-Dispatch Faza 1 (predykcyjny dispatcher mapy OSM — AUTONOMICZNY, osobny od
    /// depotowego TrafficControlService). Prognozowana zajętość pojedynczego bloku przez
    /// jeden pociąg. Czasy w sekundach symulacji (caller podaje spójny zegar dla wszystkich).
    /// </summary>
    public readonly struct BlockReservation
    {
        public readonly int blockKey;
        public readonly double enterSec;
        public readonly double exitSec;

        public BlockReservation(int blockKey, double enterSec, double exitSec)
        {
            this.blockKey = blockKey;
            this.enterSec = enterSec;
            this.exitSec = exitSec;
        }

        /// <summary>Czy interwał zajętości nakłada się z innym (półotwarte: dotknięcie końców = brak kolizji).</summary>
        public bool OverlapsWith(BlockReservation other) =>
            enterSec < other.exitSec && other.enterSec < exitSec;
    }

    /// <summary>
    /// M-Dispatch Faza 1: prognoza czasoprzestrzenna jednego pociągu — sekwencja rezerwacji
    /// bloków na nadchodzącej trasie w horyzoncie. <see cref="priority"/> = efektywny priorytet
    /// IRJ (cache do scoringu w fazach decyzyjnych).
    /// </summary>
    public sealed class TrainForecast
    {
        public readonly int trainRunId;
        public readonly int priority;
        public readonly List<BlockReservation> reservations;

        public TrainForecast(int trainRunId, int priority, List<BlockReservation> reservations)
        {
            this.trainRunId = trainRunId;
            this.priority = priority;
            this.reservations = reservations ?? new List<BlockReservation>();
        }
    }

    /// <summary>
    /// M-Dispatch Faza 1: wykryty konflikt — dwa pociągi prognozują nakładającą się zajętość
    /// tego samego bloku. <see cref="firstTrainRunId"/> wjeżdża wcześniej (wg prognozy);
    /// <see cref="secondTrainRunId"/> wjechałby zanim pierwszy zwolni blok.
    /// </summary>
    public readonly struct BlockConflict
    {
        public readonly int blockKey;
        public readonly int firstTrainRunId;
        public readonly int secondTrainRunId;
        public readonly double overlapStartSec;
        public readonly double overlapEndSec;

        public BlockConflict(int blockKey, int firstTrainRunId, int secondTrainRunId,
                             double overlapStartSec, double overlapEndSec)
        {
            this.blockKey = blockKey;
            this.firstTrainRunId = firstTrainRunId;
            this.secondTrainRunId = secondTrainRunId;
            this.overlapStartSec = overlapStartSec;
            this.overlapEndSec = overlapEndSec;
        }
    }
}
