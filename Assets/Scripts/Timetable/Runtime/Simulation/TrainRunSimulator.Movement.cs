using Unity.Profiling;
using UnityEngine;
using RailwayManager.Core;

namespace RailwayManager.Timetable.Simulation
{
    public partial class TrainRunSimulator
    {
        // ── MP-2: Profiler markers ──────────────────────────────
        static readonly ProfilerMarker s_Advance = new("TrainRunSimulator.Advance");

        // ── Advance (fizyka: rozpęd → cruise at segment Vmax → hamowanie) ──

        void Advance(SimulatedTrain st, float deltaGameSec)
        {
            using var _profMarker = s_Advance.Auto();

            if (st.state == TrainState.Completed) return;

            // TD-013: lazy sync wpisu w _trainsWaitingForBlock — O(1) per call,
            // eliminuje konieczność wrapper'owania wszystkich `state = X` set sites.
            SyncBlockWaitIndex(st);

            var tr = st.trainRun;
            var stops = st.timetable.stops;

            // ── M7-3: BrokenDown — self-repair timer ──
            if (st.state == TrainState.BrokenDown)
            {
                HandleBrokenDown(st, deltaGameSec);
                UpdateVisualPosition(st);
                return;
            }

            // ── M7-3: AwaitingRescue — czeka na rescue loko (placeholder dla M7-3c) ──
            if (st.state == TrainState.AwaitingRescue)
            {
                // Pociąg stoi, blokuje sekcję. Reputation tick -1 co N minut?
                // Full rescue UI + dispatch w M7-3c.
                tr.currentDelaySec += (int)Mathf.Max(deltaGameSec, 1f);
                UpdateVisualPosition(st);
                return;
            }

            // ── Blocked by signal — czeka na zwolnienie bloku LUB platformy ──
            if (st.state == TrainState.BlockedBySignal)
            {
                int nextBlkIdx = st.currentBlockIndex + 1;
                bool blockClear = nextBlkIdx >= st.routeBlockCount ||
                                  IsBlockFree(st.routeBlockKeys[nextBlkIdx], tr.id);

                int targetPlatformId = st.currentStopIndex < stops.Count
                    ? stops[st.currentStopIndex].platformId : -1;
                bool platformClear = IsPlatformFree(targetPlatformId, tr.id);

                if (blockClear && platformClear)
                {
                    // Blok wolny — ale czy inny pociąg z wyższym priorytetem też czeka?
                    int myPriority = GetTrainPriority(st);
                    int nextBlockKey = nextBlkIdx < st.routeBlockCount
                        ? st.routeBlockKeys[nextBlkIdx] : -1;

                    if (nextBlockKey >= 0 && HasHigherPriorityWaiting(nextBlockKey, tr.id, myPriority))
                    {
                        // Ustąp — pociąg z wyższym priorytetem jedzie pierwszy
                        tr.currentDelaySec += (int)Mathf.Max(deltaGameSec, 1f);
                        UpdateVisualPosition(st);
                        return;
                    }

                    st.state = TrainState.Running;
                    Log.Info($"[TrainRunSimulator] Train '{tr.trainNumberSnapshot}' resumed " +
                             $"(prio={myPriority}, delay={tr.currentDelaySec}s)");
                }
                else
                {
                    tr.currentDelaySec += (int)Mathf.Max(deltaGameSec, 1f);
                    UpdateVisualPosition(st);
                    return;
                }
            }

            // ── Postój na stacji (z compression — Etap 7) ──
            if (st.state == TrainState.StoppedAtStation)
            {
                if (st.currentStopIndex >= stops.Count) { st.state = TrainState.Completed; return; }

                var currentStop = stops[st.currentStopIndex];
                float plannedDepAbs = st.departureTimeOfDaySec + currentStop.plannedDepartureSec;

                // M7-3b: doorsBroken → +60s dwell
                if (st.doorsBroken) plannedDepAbs += 60f;

                // Compression: jeśli pociąg jest opóźniony, skróć postój do minimum
                // technicznego (30s) zamiast pełnego planowanego czasu.
                if (tr.currentDelaySec > 0 && st.currentStopIndex > 0 && st.currentStopIndex < stops.Count - 1)
                {
                    float plannedArrAbs = st.departureTimeOfDaySec + currentStop.plannedArrivalSec;
                    float plannedDwell = currentStop.plannedDepartureSec - currentStop.plannedArrivalSec;
                    float minDwell = TimetableTuningConstants.DefaultMinStopSeconds;

                    if (plannedDwell > minDwell)
                    {
                        // Odjazd wcześniej: arrival + minDwell zamiast planned departure
                        float compressedDepAbs = plannedArrAbs + minDwell;

                        if (GameState.GameTimeSeconds >= compressedDepAbs && GameState.GameTimeSeconds < plannedDepAbs)
                        {
                            // M-Dispatch Faza 2: nawet przy kompresji nie wyjeżdżaj na wspólny blok
                            // przed wyżej-ważonym rywalem (decider i tak puści, jeśli trzymanie spóźnionego
                            // pociągu jest zbyt kosztowne — koszt rośnie z jego priorytetem×czas).
                            if (ShouldHoldForPredictiveDispatch(st, GameState.GameTimeSeconds))
                            {
                                tr.currentDelaySec += (int)Mathf.Max(deltaGameSec, 1f);
                                UpdateVisualPosition(st);
                                return;
                            }

                            // Nadrobione sekundy
                            int recovered = (int)(plannedDepAbs - compressedDepAbs);
                            tr.currentDelaySec = Mathf.Max(0, tr.currentDelaySec - recovered);

                            int depPlatformId = currentStop.platformId;
                            SchedulePlatformRelease(depPlatformId, tr.id);
                            int departingFromIdx = st.currentStopIndex;
                            int departingStationNodeId = currentStop.stationNodeId;
                            st.currentStopIndex++;
                            if (st.currentStopIndex >= stops.Count) { st.state = TrainState.Completed; return; }
                            st.state = TrainState.Running;
                            OnTrainDepartingFromStop?.Invoke(tr, departingFromIdx, departingStationNodeId);
                            return;
                        }
                    }
                }

                // Normalne oczekiwanie na planowy odjazd
                if (GameState.GameTimeSeconds < plannedDepAbs)
                {
                    UpdateVisualPosition(st);
                    return;
                }

                // M-Dispatch Faza 2: predykcyjne trzymanie — gotów do odjazdu, ale przepuść
                // wyżej-ważonego rywala na wspólnym bloku (jeśli to się opłaca i mieści w limicie).
                if (ShouldHoldForPredictiveDispatch(st, GameState.GameTimeSeconds))
                {
                    tr.currentDelaySec += (int)Mathf.Max(deltaGameSec, 1f);
                    UpdateVisualPosition(st);
                    return;
                }

                // Odjazd — release platform z marginesem
                int platformId = currentStop.platformId;
                SchedulePlatformRelease(platformId, tr.id);

                int normalDepartingFromIdx = st.currentStopIndex;
                int normalDepartingStationNodeId = currentStop.stationNodeId;
                st.currentStopIndex++;
                if (st.currentStopIndex >= stops.Count) { st.state = TrainState.Completed; return; }
                st.state = TrainState.Running;
                OnTrainDepartingFromStop?.Invoke(tr, normalDepartingFromIdx, normalDepartingStationNodeId);
            }

            // ── Jazda ──
            if (st.state != TrainState.Running) return;

            float nextStopDist = st.stopDistancesM[st.currentStopIndex];
            float remaining = nextStopDist - tr.currentPositionOnRouteM;

            // Snap gdy blisko i wolno
            if (remaining <= 20f && st.currentSpeedMps < 2f)
            {
                // Sprawdź czy tor peronowy jest wolny (jeśli assigned)
                int arrPlatformId = stops[st.currentStopIndex].platformId;
                if (!IsPlatformFree(arrPlatformId, tr.id))
                {
                    // Tor zajęty — czekaj przed stacją (jak blok)
                    st.currentSpeedMps = 0f;
                    st.state = TrainState.BlockedBySignal;
                    tr.currentDelaySec += (int)Mathf.Max(deltaGameSec, 1f);
                    Log.Info($"[TrainRunSimulator] Train '{tr.trainNumberSnapshot}' " +
                             $"BLOCKED — platform #{arrPlatformId} occupied at '{stops[st.currentStopIndex].stationName}'");
                    UpdateVisualPosition(st);
                    return;
                }

                tr.currentPositionOnRouteM = nextStopDist;
                st.currentSpeedMps = 0f;

                // Occupy platform
                OccupyPlatform(arrPlatformId, tr.id);

                if (st.currentStopIndex >= stops.Count - 1)
                    st.state = TrainState.Completed;
                else
                    st.state = TrainState.StoppedAtStation;

                // M6-2: emit arrival event (alighting + boarding przez PassengerManager)
                if (st.state == TrainState.StoppedAtStation || st.state == TrainState.Completed)
                {
                    var arrivedStop = stops[st.currentStopIndex];
                    OnTrainArrivingAtStop?.Invoke(tr, st.currentStopIndex, arrivedStop.stationNodeId);

                    // M-TimetableUX F1.2: dla ZD stops fire dodatkowy event dla Personnel
                    // (CrewSwapSuggestionService / crew handover analytics).
                    if (arrivedStop.stopType == StopType.ZD)
                        OnCrewSwap?.Invoke(tr, st.currentStopIndex, arrivedStop.stationNodeId);

                    // M6-4: platform fee (opłata za użytkowanie peronu)
                    var init = TimetableInitializer.Instance;
                    if (init != null && init.Stations != null)
                    {
                        RailwayStation rs = null;
                        foreach (var s in init.Stations)
                            if (s.pathNodeId == arrivedStop.stationNodeId) { rs = s; break; }
                        if (rs != null)
                        {
                            // TD-036a: opłata per kategoria stacji (StationImportance → tier OIU PLK);
                            // przed zbudowaniem OD matrix importance = -1 → fallback isMajorStation.
                            float importance = RailwayManager.Timetable.Economy.PassengerManager.Instance != null
                                ? RailwayManager.Timetable.Economy.PassengerManager.Instance.GetStationImportance(rs.stationId)
                                : -1f;
                            int fee = RailwayManager.Timetable.Economy.CostCalculator.GetPlatformFeeGroszy(rs, importance);
                            if (fee > 0)
                            {
                                var econ = RailwayManager.Timetable.Economy.EconomyManager.Instance;
                                econ?.AddCost(tr.circulationId, fee, "platform_fee", rs.name);
                            }
                        }
                    }
                }

                UpdateVisualPosition(st);
                return;
            }

            // ── Block section transition check (Etap 3) ──
            if (st.routeBlockCount > 0)
            {
                // Aktualizuj bieżący section index
                int newBlkIdx = st.GetBlockIndexAtDistance(tr.currentPositionOnRouteM);
                if (newBlkIdx != st.currentBlockIndex && newBlkIdx >= 0)
                {
                    // Wjechaliśmy w nową sekcję — occupy/release
                    if (newBlkIdx < st.routeBlockCount)
                    {
                        ReleaseBlock(st.routeBlockKeys[st.currentBlockIndex], tr.id);
                        OccupyBlock(st.routeBlockKeys[newBlkIdx], tr.id);
                        st.currentBlockIndex = newBlkIdx;
                    }
                }

                // Sprawdź czy NASTĘPNA sekcja jest wolna (look-ahead)
                int nextBlkIdx = st.currentBlockIndex + 1;
                if (nextBlkIdx < st.routeBlockCount)
                {
                    float nextBoundaryDist = st.blockEntryDistM[nextBlkIdx];
                    float distToBoundary = nextBoundaryDist - tr.currentPositionOnRouteM;
                    float curBrakingDist = (st.currentSpeedMps * st.currentSpeedMps)
                                           / (2f * TimetableTuningConstants.DefaultDecelerationMs2);

                    // Jeśli zbliżamy się do granicy — sprawdź czy następny blok wolny
                    if (distToBoundary <= curBrakingDist + st.currentSpeedMps * deltaGameSec + 20f)
                    {
                        if (!IsBlockFree(st.routeBlockKeys[nextBlkIdx], tr.id))
                        {
                            // Blok zajęty — hamuj do granicy sekcji
                            if (distToBoundary <= 20f && st.currentSpeedMps < 2f)
                            {
                                // Zatrzymany przy granicy
                                tr.currentPositionOnRouteM = Mathf.Min(tr.currentPositionOnRouteM,
                                    nextBoundaryDist - 5f); // stań 5m przed granicą
                                st.currentSpeedMps = 0f;
                                st.state = TrainState.BlockedBySignal;
                                tr.currentDelaySec += (int)Mathf.Max(deltaGameSec, 1f);
                                Log.Info($"[TrainRunSimulator] Train '{tr.trainNumberSnapshot}' " +
                                         $"BLOCKED at section boundary " +
                                         $"(next=#{st.routeBlockKeys[nextBlkIdx]}, " +
                                         $"pos={tr.currentPositionOnRouteM / 1000f:F1}km)");
                                UpdateVisualPosition(st);
                                return;
                            }
                            // Jeszcze nie przy granicy — hamuj
                            remaining = Mathf.Min(remaining, distToBoundary - 5f);
                        }
                    }
                }
            }

            // Vmax z OSM per segment (binary search)
            float vmaxMps = st.GetVmaxAtDistance(tr.currentPositionOnRouteM);

            // Cap do maxSpeed composition (pojazd nie może jechać szybciej niż jego limit)
            int compMaxKmh = st.timetable.composition.maxSpeedKmh;
            if (compMaxKmh > 0)
                vmaxMps = Mathf.Min(vmaxMps, compMaxKmh / 3.6f);

            // M7-3b: speed limit po awarii (wheels/lights)
            if (st.wheelsSpeedLimitedKmh > 0)
                vmaxMps = Mathf.Min(vmaxMps, st.wheelsSpeedLimitedKmh / 3.6f);

            // Oblicz dystans hamowania do zera z bieżącej prędkości
            float decel = TimetableTuningConstants.DefaultDecelerationMs2;
            float accel = TimetableTuningConstants.DefaultAccelerationMs2;
            float brakingDist = (st.currentSpeedMps * st.currentSpeedMps) / (2f * decel);

            // Target prędkość: Vmax albo 0 jeśli trzeba hamować do stacji lub bloku
            float targetMps = vmaxMps;
            if (remaining <= brakingDist + st.currentSpeedMps * deltaGameSec)
                targetMps = 0f;

            // Aktualizuj prędkość
            if (st.currentSpeedMps < targetMps)
            {
                st.currentSpeedMps += accel * deltaGameSec;
                if (st.currentSpeedMps > targetMps)
                    st.currentSpeedMps = targetMps;
            }
            else if (st.currentSpeedMps > targetMps)
            {
                st.currentSpeedMps -= decel * deltaGameSec;
                if (st.currentSpeedMps < 0f)
                    st.currentSpeedMps = 0f;
            }

            // Aktualizuj pozycję
            tr.currentPositionOnRouteM += st.currentSpeedMps * deltaGameSec;

            // Przekroczył stację? Snap
            if (tr.currentPositionOnRouteM >= nextStopDist)
            {
                tr.currentPositionOnRouteM = nextStopDist;
                st.currentSpeedMps = 0f;

                if (st.currentStopIndex >= stops.Count - 1)
                    st.state = TrainState.Completed;
                else
                    st.state = TrainState.StoppedAtStation;
            }

            UpdateVisualPosition(st);
        }

        // ── Polyline helpers (binary search na cached polyline) ─────

        /// <summary>
        /// Oblicza pozycję 2D na szczegółowej polyline trasy (z edge geometry OSM).
        /// Binary search O(log N) na polylineCumulDist zamiast liniowego O(N) scan'u.
        /// </summary>
        static Vector2 GetPositionOnPolyline(SimulatedTrain st, float distanceM)
        {
            var poly = st.cachedPolyline;
            var cumDist = st.polylineCumulDist;

            if (poly == null || poly.Length < 2)
                return Vector2.zero;

            if (distanceM <= 0f) return poly[0];
            float totalLen = cumDist[cumDist.Length - 1];
            if (distanceM >= totalLen) return poly[poly.Length - 1];

            // Binary search: znajdź segment zawierający distanceM
            int lo = 0, hi = cumDist.Length - 1;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (cumDist[mid] < distanceM) lo = mid + 1;
                else hi = mid;
            }
            int idx = Mathf.Max(lo - 1, 0);

            float segStart = cumDist[idx];
            float segEnd = cumDist[idx + 1];
            float t = (distanceM - segStart) / Mathf.Max(segEnd - segStart, 0.01f);
            return Vector2.Lerp(poly[idx], poly[idx + 1], t);
        }

        /// <summary>
        /// Oblicza kierunek jazdy (tangent) na szczegółowej polyline.
        /// Binary search O(log N).
        /// </summary>
        static Vector2 GetDirectionOnPolyline(SimulatedTrain st, float distanceM)
        {
            var poly = st.cachedPolyline;
            var cumDist = st.polylineCumulDist;

            if (poly == null || poly.Length < 2)
                return Vector2.up;

            float totalLen = cumDist[cumDist.Length - 1];
            if (distanceM >= totalLen)
            {
                return (poly[poly.Length - 1] - poly[poly.Length - 2]).normalized;
            }

            float d = Mathf.Max(distanceM, 0f);
            int lo = 0, hi = cumDist.Length - 1;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (cumDist[mid] < d) lo = mid + 1;
                else hi = mid;
            }
            int idx = Mathf.Max(lo - 1, 0);

            var dir = (poly[idx + 1] - poly[idx]);
            return dir.sqrMagnitude > 0.001f ? dir.normalized : Vector2.up;
        }
    }
}
