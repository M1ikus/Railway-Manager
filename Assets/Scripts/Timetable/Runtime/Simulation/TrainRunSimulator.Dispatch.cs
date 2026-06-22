using System.Collections.Generic;

namespace RailwayManager.Timetable.Simulation
{
    public partial class TrainRunSimulator
    {
        // ── M-Dispatch Faza 2: predykcyjne trzymanie przed odjazdem ──

        /// <summary>
        /// Globalny włącznik autonomicznego predykcyjnego dispatchera mapy OSM.
        /// (Faza 4: podpiąć pod ustawienie polityki w kreatorze gry, zmienialne w trakcie gry.)
        /// OSOBNY od depotowego TrafficControlService — ten steruje TYLKO ruchem depot.
        /// </summary>
        public static bool PredictiveDispatchEnabled = true;

        // Bufory reużywalne — unik alokacji w zdarzeniach odjazdu.
        readonly HashSet<int> _dispatchMeBlocksBuf = new();
        readonly List<SimulatedTrain> _dispatchClusterBuf = new();
        readonly List<TrainForecast> _dispatchOthersBuf = new();

        /// <summary>
        /// Czy pociąg gotowy do odjazdu z postoju ma poczekać, by przepuścić wyżej-ważonego
        /// rywala na wspólnym bloku. Buduje prognozę `me` + lokalnego klastra (pociągi dzielące
        /// nadchodzący blok) i pyta <see cref="PredictiveDispatchDecider"/>.
        ///
        /// Event-driven: wołane TYLKO gdy pociąg jest fizycznie gotów do odjazdu (rzadkie), nie co tick.
        /// </summary>
        bool ShouldHoldForPredictiveDispatch(SimulatedTrain me, double nowSec)
        {
            if (!PredictiveDispatchEnabled || !DispatchPolicyService.HoldingEnabled
                || me == null || me.routeBlockCount <= 0) return false;

            float horizon = TimetableTuningConstants.DispatchForecastHorizonSec;
            var meForecast = TrainForecastService.Compute(me, DispatchWeightOf(me), nowSec, horizon);
            if (meForecast.reservations.Count == 0) return false;

            _dispatchMeBlocksBuf.Clear();
            foreach (var r in meForecast.reservations) _dispatchMeBlocksBuf.Add(r.blockKey);

            // Prefilter do lokalnego klastra: pociągi dzielące którykolwiek nadchodzący blok z `me`.
            _dispatchClusterBuf.Clear();
            foreach (var kv in _activeTrains)
            {
                var o = kv.Value;
                if (o == me || o.state == TrainState.Completed || o.routeBlockCount <= 0) continue;

                bool shares = false;
                for (int i = o.currentBlockIndex; i < o.routeBlockCount; i++)
                {
                    if (_dispatchMeBlocksBuf.Contains(o.routeBlockKeys[i])) { shares = true; break; }
                }
                if (shares) _dispatchClusterBuf.Add(o);
            }
            if (_dispatchClusterBuf.Count == 0) return false;

            // Determinizm: stabilna kolejność (po trainRunId) niezależnie od iteracji słownika.
            _dispatchClusterBuf.Sort((a, b) => a.trainRun.id.CompareTo(b.trainRun.id));

            _dispatchOthersBuf.Clear();
            foreach (var o in _dispatchClusterBuf)
                _dispatchOthersBuf.Add(TrainForecastService.Compute(o, DispatchWeightOf(o), nowSec, horizon));

            var decision = PredictiveDispatchDecider.Decide(
                meForecast, _dispatchOthersBuf, nowSec, TimetableTuningConstants.DispatchMaxExtraHoldSec,
                DispatchPolicyService.HoldBias);

            return decision.hold;
        }

        /// <summary>
        /// Faza 4c: efektywna waga pociągu = priorytet IRJ × skala + obłożenie (O(1) z
        /// PassengerManager). Zapchany osobowy może przeważyć pusty pośpieszny.
        /// </summary>
        int DispatchWeightOf(SimulatedTrain t)
        {
            int load = 0;
            var pm = RailwayManager.Timetable.Economy.PassengerManager.Instance;
            if (pm != null && t.trainRun != null) load = pm.CountOnTrain(t.trainRun.id);
            return DispatchWeight.Compute(GetTrainPriority(t), load);
        }
    }
}
