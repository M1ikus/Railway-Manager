using System.Collections.Generic;

namespace RailwayManager.Timetable.Simulation
{
    /// <summary>Wynik decyzji dispatchera dla pociągu gotowego do odjazdu.</summary>
    public readonly struct DispatchDecision
    {
        public readonly bool hold;
        public readonly int rivalTrainRunId;   // -1 = brak
        public readonly int conflictBlockKey;  // -1 = brak
        public readonly double holdUntilSec;   // szacowany czas, do którego trzymamy

        public DispatchDecision(bool hold, int rivalTrainRunId, int conflictBlockKey, double holdUntilSec)
        {
            this.hold = hold;
            this.rivalTrainRunId = rivalTrainRunId;
            this.conflictBlockKey = conflictBlockKey;
            this.holdUntilSec = holdUntilSec;
        }

        public static DispatchDecision Release => new DispatchDecision(false, -1, -1, 0.0);
    }

    /// <summary>
    /// M-Dispatch Faza 2: decyzja TRZYMAJ/PUŚĆ dla pociągu gotowego do odjazdu z postoju.
    /// Czysta, deterministyczna funkcja na prognozach (<see cref="TrainForecast"/>) — bez sceny/RNG.
    ///
    /// Logika: jeśli `me`, wyjeżdżając teraz, wszedłby na blok PRZED wyżej-ważonym rywalem i
    /// zablokował go (jego prognoza tego bloku nakłada się), porównaj ważony koszt:
    ///   releaseCost = waga(rywal) × opóźnienie rywala gdy me jedzie
    ///   holdCost    = waga(me)    × ile me poczeka, ustępując
    /// TRZYMAJ tylko gdy holdCost &lt; releaseCost ORAZ czas trzymania ≤ maxExtraHoldSec
    /// (twardy limit — nie trzymamy w nieskończoność, anti-starvation / anti-kaskada).
    ///
    /// Waga = priorytet IRJ (Faza 2). Obłożenie pasażerami / ochrona przesiadek / pełny
    /// anty-deadlock (banker safe-state) dochodzą w Fazach 3-4.
    /// </summary>
    public static class PredictiveDispatchDecider
    {
        public static DispatchDecision Decide(TrainForecast me, IReadOnlyList<TrainForecast> others,
                                              double nowSec, float maxExtraHoldSec, float holdBias = 1f)
        {
            if (me == null || me.reservations == null || me.reservations.Count == 0 || others == null)
                return DispatchDecision.Release;

            // Pełny zbiór prognoz (do sprawdzenia anty-deadlock).
            var all = new List<TrainForecast>(others.Count + 1);
            all.AddRange(others);
            all.Add(me);

            // Bloki me w kolejności przejazdu — pierwszy opłacalny i BEZPIECZNY konflikt decyduje.
            foreach (var meRes in me.reservations)
            {
                foreach (var other in others)
                {
                    if (other == null || other.trainRunId == me.trainRunId) continue;
                    if (other.priority <= me.priority) continue; // tylko wyżej-ważeni rywale

                    // Faza 4a: efektywna waga = priorytet + suma priorytetów pociągów w kolejce ZA
                    // (kaskada). Trzymanie/puszczenie opóźnia też tych z tyłu — to ich tu wlicza.
                    int effWeightMe = me.priority + DispatchSafeState.SumWaiterPriorities(me, all, other.trainRunId);
                    int effWeightRival = other.priority + DispatchSafeState.SumWaiterPriorities(other, all, me.trainRunId);

                    var oReservations = other.reservations;
                    for (int k = 0; k < oReservations.Count; k++)
                    {
                        var oRes = oReservations[k];
                        if (oRes.blockKey != meRes.blockKey) continue;
                        if (!meRes.OverlapsWith(oRes)) continue;

                        // me wjeżdża wcześniej/równo i swoją zajętością blokuje rywala?
                        if (meRes.enterSec <= oRes.enterSec && meRes.exitSec > oRes.enterSec)
                        {
                            double rivalDelay = meRes.exitSec - oRes.enterSec;   // ile rywal czeka, jeśli me jedzie
                            double myHold = oRes.exitSec - meRes.enterSec;       // ile me czeka, ustępując
                            if (myHold <= 0.0) continue;

                            double releaseCost = (double)effWeightRival * rivalDelay;
                            double holdCost = (double)effWeightMe * myHold;

                            if (holdCost < releaseCost * holdBias && myHold <= maxExtraHoldSec)
                            {
                                // Faza 3a: trzymaj TYLKO jeśli nie domknie to cyklu wait-for
                                // (inaczej zakleszczenie — wtedy lepiej puścić, by rozbić korek).
                                if (!DispatchSafeState.HoldWouldDeadlock(me.trainRunId, other.trainRunId, all))
                                    return new DispatchDecision(true, other.trainRunId, meRes.blockKey, nowSec + myHold);
                            }
                            // Nieopłacalne / za długo / zakleszczyłoby — nie trzymaj na tym konflikcie, szukaj dalej.
                        }
                    }
                }
            }
            return DispatchDecision.Release;
        }
    }
}
