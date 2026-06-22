using System.Collections.Generic;
using UnityEngine;
using RailwayManager.Core;

namespace RailwayManager.Timetable.Simulation
{
    public partial class TrainRunSimulator
    {
        // ── Priorytet IRJ + rush hour (Etap 6) ─────────────────────

        /// <summary>
        /// Oblicza efektywny priorytet pociągu wg IRJ. Wyższy = większe pierwszeństwo.
        ///
        /// Hierarchia (z decyzji M9a 2026-04-15):
        ///   10 — Międzynarodowy (EC, EN, MM, RM) — przyszłość, najwyższy
        ///    9 — Osobowy w rush hour (pn-pt 6-9/13-16) — za międzynarodowymi
        ///    7 — Ekspres (EI)
        ///    5 — Pospieszny (MP, MH, RP)
        ///    3 — Osobowy poza rush (RO, RA, MO)
        ///    2 — Służbowy / luzem (PW, PX, LP, LT, LS, ZN)
        ///    1 — Towarowy (T*)
        /// </summary>
        int GetTrainPriority(SimulatedTrain st)
        {
            int basePriority = GetIrjBasePriority(st.timetable.irjCategory.group);

            // Rush hour boost: osobowe (priorytet 3) → skok do 9 w szczycie
            if (basePriority == 3 && IsRushHour())
                return 9;

            return basePriority;
        }

        /// <summary>Kolor paska w liście pociągów wg kategorii IRJ.</summary>
        static Color GetCategoryColor(IrjGroup group)
        {
            switch (group)
            {
                case IrjGroup.ExpressInternational:
                case IrjGroup.ExpressInternationalNight:
                case IrjGroup.InternationalFast:
                case IrjGroup.RegionalInternational:
                    return new Color(0.9f, 0.3f, 0.9f); // fiolet — międzynarodowe
                case IrjGroup.ExpressDomestic:
                    return new Color(0.95f, 0.2f, 0.2f); // czerwony — ekspres
                case IrjGroup.InterregionalFast:
                case IrjGroup.InterregionalFastNight:
                case IrjGroup.RegionalFast:
                    return new Color(1f, 0.6f, 0f); // pomarańczowy — pospieszny
                case IrjGroup.RegionalLocal:
                case IrjGroup.RegionalAgglomeration:
                case IrjGroup.InterregionalLocal:
                    return new Color(0.3f, 0.7f, 1f); // niebieski — osobowy
                case IrjGroup.EmptyPassenger:
                case IrjGroup.EmptyPassengerTest:
                case IrjGroup.LoneLocoPassenger:
                case IrjGroup.LoneLocoFreight:
                case IrjGroup.LoneLocoShunt:
                case IrjGroup.MaintenanceInspection:
                    return new Color(0.6f, 0.6f, 0.6f); // szary — służbowy
                default:
                    return new Color(0.5f, 0.4f, 0.2f); // brąz — towarowy
            }
        }

        static int GetIrjBasePriority(IrjGroup group)
        {
            switch (group)
            {
                // Międzynarodowe — najwyższy priorytet
                case IrjGroup.ExpressInternational:
                case IrjGroup.ExpressInternationalNight:
                case IrjGroup.InternationalFast:
                case IrjGroup.RegionalInternational:
                    return 10;

                // Ekspresowe krajowe
                case IrjGroup.ExpressDomestic:
                    return 7;

                // Pospieszne
                case IrjGroup.InterregionalFast:
                case IrjGroup.InterregionalFastNight:
                case IrjGroup.RegionalFast:
                    return 5;

                // Osobowe (bazowy — boostowany w rush hour do 9)
                case IrjGroup.RegionalLocal:
                case IrjGroup.RegionalAgglomeration:
                case IrjGroup.InterregionalLocal:
                    return 3;

                // Służbowe / luzem / utrzymaniowe
                case IrjGroup.EmptyPassenger:
                case IrjGroup.EmptyPassengerTest:
                case IrjGroup.LoneLocoPassenger:
                case IrjGroup.LoneLocoFreight:
                case IrjGroup.LoneLocoShunt:
                case IrjGroup.MaintenanceInspection:
                    return 2;

                // Towarowe — najniższy
                case IrjGroup.FreightIntlIntermodal:
                case IrjGroup.FreightIntlMass:
                case IrjGroup.FreightIntlNonMass:
                case IrjGroup.FreightDomesticIntermodal:
                case IrjGroup.FreightDomesticMass:
                case IrjGroup.FreightDomesticNonMass:
                case IrjGroup.FreightStationService:
                case IrjGroup.FreightEmptyTest:
                    return 1;

                default:
                    return 3;
            }
        }

        /// <summary>Czy teraz jest godzina szczytu (pn-pt 6-9 lub 13-16)?</summary>
        static bool IsRushHour()
        {
            // Dzień tygodnia z kalendarza gry
            if (!IsoTime.TryParseDate(GameState.CurrentDateIso, out var date))
                return false;

            // Weekend = brak rush hour
            if (date.DayOfWeek == System.DayOfWeek.Saturday ||
                date.DayOfWeek == System.DayOfWeek.Sunday)
                return false;

            // Godzina gry
            int hour = Mathf.FloorToInt(GameState.GameTimeSeconds / 3600f);
            return (hour >= 6 && hour < 9) || (hour >= 13 && hour < 16);
        }

        /// <summary>
        /// TD-013 (M-Performance follow-up 2026-05-06): synchronizuje wpis pociągu w
        /// <see cref="_trainsWaitingForBlock"/> z aktualnym stanem. Wywołać na początku <c>Advance</c>
        /// per pociąg — lazy sync O(1) eliminuje konieczność wrapper'owania wszystkich
        /// `state = TrainState.X` set sites.
        /// </summary>
        void SyncBlockWaitIndex(SimulatedTrain st)
        {
            int trainId = st.trainRun.id;

            // Compute target blockKey: -1 jeśli nie czeka, else routeBlockKeys[currentBlockIndex+1]
            int wantBlockKey = -1;
            if (st.state == TrainState.BlockedBySignal)
            {
                int nextIdx = st.currentBlockIndex + 1;
                if (nextIdx < st.routeBlockCount)
                    wantBlockKey = st.routeBlockKeys[nextIdx];
            }

            // Compare with stored — early return jeśli sync OK
            bool hasStored = _currentlyWaitingForBlock.TryGetValue(trainId, out int storedKey);
            if (hasStored && storedKey == wantBlockKey) return;
            if (!hasStored && wantBlockKey < 0) return;

            // Remove from old block set jeśli był
            if (hasStored)
            {
                if (_trainsWaitingForBlock.TryGetValue(storedKey, out var oldSet))
                {
                    oldSet.Remove(trainId);
                    if (oldSet.Count == 0)
                        _trainsWaitingForBlock.Remove(storedKey); // GC slot
                }
            }

            // Register in new block set jeśli czeka
            if (wantBlockKey >= 0)
            {
                if (!_trainsWaitingForBlock.TryGetValue(wantBlockKey, out var newSet))
                {
                    newSet = new HashSet<int>();
                    _trainsWaitingForBlock[wantBlockKey] = newSet;
                }
                newSet.Add(trainId);
                _currentlyWaitingForBlock[trainId] = wantBlockKey;
            }
            else
            {
                _currentlyWaitingForBlock.Remove(trainId);
            }
        }

        /// <summary>
        /// TD-013: cleanup z indexu przy despawn — wywołać z <c>DespawnTrain</c>.
        /// </summary>
        void UnregisterFromBlockWaitIndex(int trainId)
        {
            if (!_currentlyWaitingForBlock.TryGetValue(trainId, out int storedKey)) return;
            if (_trainsWaitingForBlock.TryGetValue(storedKey, out var set))
            {
                set.Remove(trainId);
                if (set.Count == 0) _trainsWaitingForBlock.Remove(storedKey);
            }
            _currentlyWaitingForBlock.Remove(trainId);
        }

        /// <summary>
        /// Sprawdza czy inny oczekujący pociąg ma wyższy priorytet do tego samego bloku.
        /// Jeśli tak — ten pociąg nie powinien wjeżdżać nawet gdy blok wolny.
        ///
        /// TD-013 fix 2026-05-06: lookup po <see cref="_trainsWaitingForBlock"/> zamiast O(N) skanu
        /// wszystkich pociągów. Skala: O(N²) → O(k²) gdzie k = pociągów czekających na ten konkretny
        /// blok (rzadko >2-3, bo bloków jest dużo). Przy 1000 trains × 50Hz: ~50M ops/s → ~150k ops/s.
        /// </summary>
        bool HasHigherPriorityWaiting(int blockKey, int myTrainRunId, int myPriority)
        {
            // TD-013: O(1) lookup zamiast O(N) skanu _activeTrains
            if (!_trainsWaitingForBlock.TryGetValue(blockKey, out var candidates) || candidates.Count == 0)
                return false;

            // Cache moje currentDelaySec do tie-break (uniknij Dictionary lookup w pętli)
            int myDelaySec = _activeTrains.TryGetValue(myTrainRunId, out var mySt)
                ? mySt.trainRun.currentDelaySec : 0;

            foreach (int otherId in candidates)
            {
                if (otherId == myTrainRunId) continue;
                if (!_activeTrains.TryGetValue(otherId, out var other)) continue;

                // Defensive: stan/block mogłyby się rozjechać między sync a tu — re-validate
                // (stress test 1000 trains może mieć race między state set a SyncBlockWaitIndex call)
                if (other.state != TrainState.BlockedBySignal) continue;
                int otherNextBlkIdx = other.currentBlockIndex + 1;
                if (otherNextBlkIdx >= other.routeBlockCount) continue;
                if (other.routeBlockKeys[otherNextBlkIdx] != blockKey) continue;

                int otherPriority = GetTrainPriority(other);
                if (otherPriority > myPriority)
                    return true;

                // Tie-break: ten sam priorytet — kto czeka dłużej jedzie pierwszy
                if (otherPriority == myPriority &&
                    other.trainRun.currentDelaySec > myDelaySec)
                    return true;
            }
            return false;
        }
    }
}
