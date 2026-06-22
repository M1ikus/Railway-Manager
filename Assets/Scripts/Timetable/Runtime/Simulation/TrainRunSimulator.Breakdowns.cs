using UnityEngine;
using RailwayManager.Core;

namespace RailwayManager.Timetable.Simulation
{
    public partial class TrainRunSimulator
    {
        // ── MP-9: deterministic RNG per-service (seedowane) ────────
        static readonly DeterministicRng s_breakdownRng = RandomRegistry.GetRng("TrainRunSimulator.Breakdowns");

        // ── M7-3: Breakdown detection + handling ────────────────────

        void CheckForBreakdown(SimulatedTrain st, float deltaM)
        {
            float deltaSec = deltaM / Mathf.Max(0.1f, st.currentSpeedMps); // czas ruchu
            if (deltaSec <= 0f) return;

            var bd = RailwayManager.Fleet.BreakdownService.CheckForBreakdown(
                st.trainRun.runningVehicleIds, deltaSec);
            if (!bd.HasValue) return;

            var ev = bd.Value;
            ApplyBreakdown(st, ev);
        }

        void ApplyBreakdown(SimulatedTrain st, RailwayManager.Fleet.BreakdownEvent ev)
        {
            var v = RailwayManager.Fleet.FleetService.GetOwnedById(ev.vehicleId);
            st.brokenVehicleId = ev.vehicleId;
            st.brokenComponentIndex = (int)ev.component;
            st.breakdownStartedGameTime = (long)GameState.GameTimeSeconds
                                        + GameState.GameDay * 86400L;

            string name = v != null ? $"{v.series}-{v.number}" : $"vehicle#{ev.vehicleId}";
            Log.Warn($"[BreakdownService] AWARIA! {name} — {ev.component} ({ev.severity}) " +
                     $"w trasie run#{st.trainRun.id}");

            // Reputation hit
            var rep = RailwayManager.Timetable.Economy.ReputationManager.Instance;
            if (rep != null)
            {
                var stationIds = CollectStationIdsOnRoute(st);
                rep.ApplyEvent(RailwayManager.Timetable.Economy.ReputationEventType.BreakdownOnRoute,
                               stationIds, null, $"run#{st.trainRun.id} {ev.component}");
            }

            switch (ev.severity)
            {
                case RailwayManager.Fleet.BreakdownSeverity.Critical:
                    // Pociąg staje na szlaku — self-repair attempt
                    st.currentSpeedMps = 0f;
                    st.state = TrainState.BrokenDown;
                    long now = (long)GameState.GameTimeSeconds + GameState.GameDay * 86400L;
                    int selfRepairDelay = s_breakdownRng.Range(300, 900); // 5-15 min game time. MP-9: seedowane
                    st.selfRepairAttemptGameTime = now + selfRepairDelay;
                    Log.Info($"[BreakdownService] {name} — self-repair attempt za {selfRepairDelay / 60}min");
                    break;

                case RailwayManager.Fleet.BreakdownSeverity.Safety:
                    // Brake failure — safety stop: kurs natychmiast cancelled, dodaj duży delay
                    // Docelowo: pociąg dojeżdża do najbliższej stacji (M7-3c pathfinding)
                    st.trainRun.currentDelaySec += 1800; // +30 min
                    st.trainRun.isCancelled = true;
                    st.state = TrainState.Completed; // despawn
                    Log.Warn($"[BreakdownService] {name} — SAFETY STOP (brake failure): kurs ODWOŁANY");
                    // Extra reputation hit za emergency stop
                    if (rep != null)
                    {
                        var stations = CollectStationIdsOnRoute(st);
                        rep.ApplyEvent(RailwayManager.Timetable.Economy.ReputationEventType.DelayMajor,
                                       stations, null, $"run#{st.trainRun.id} brake safety stop");
                    }
                    break;

                case RailwayManager.Fleet.BreakdownSeverity.Inconvenience:
                    // Doors → przyszły postój +60s (flagowane na SimulatedTrain)
                    // Wheels → speed limit 80 km/h (obsłużone w fizyce — sprawdzenie per-tick)
                    // Lights → night speed limit 60 km/h (placeholder: nocą też 80)
                    // Coupling → no direct driving penalty (blocks shunting w Depot)
                    // Electrical → random side effect (losowe)
                    if (ev.component == RailwayManager.Fleet.ComponentType.Doors)
                        st.doorsBroken = true;
                    else if (ev.component == RailwayManager.Fleet.ComponentType.Wheels)
                        st.wheelsSpeedLimitedKmh = 80;
                    else if (ev.component == RailwayManager.Fleet.ComponentType.Lights)
                        st.wheelsSpeedLimitedKmh = Mathf.Min(st.wheelsSpeedLimitedKmh <= 0 ? 80 : st.wheelsSpeedLimitedKmh, 80);
                    st.trainRun.currentDelaySec += 120;
                    Log.Info($"[BreakdownService] {name} — {ev.component} (Inconvenience)");
                    break;

                case RailwayManager.Fleet.BreakdownSeverity.Comfort:
                    // AC/toilets/interior — revenue/reputation penalty
                    // Reputation hit już był; flag pojazd jako Comfort-degraded (M6-7 / post-polish)
                    Log.Info($"[BreakdownService] {name} — {ev.component} (Comfort penalty)");
                    break;
            }
        }

        void HandleBrokenDown(SimulatedTrain st, float deltaGameSec)
        {
            long now = (long)GameState.GameTimeSeconds + GameState.GameDay * 86400L;
            st.trainRun.currentDelaySec += (int)Mathf.Max(deltaGameSec, 1f);

            if (now < st.selfRepairAttemptGameTime) return;

            // Czas na self-repair attempt
            var v = RailwayManager.Fleet.FleetService.GetOwnedById(st.brokenVehicleId);
            if (v == null)
            {
                st.state = TrainState.AwaitingRescue;
                Log.Warn($"[BreakdownService] Vehicle#{st.brokenVehicleId} nie znaleziony — AwaitingRescue");
                return;
            }

            float health = GetComponentHealth(v, (RailwayManager.Fleet.ComponentType)st.brokenComponentIndex);
            // M8-12: vehicleId aktywuje hook Personnel (skill mechanika z najbliższego depotu)
            float successChance = RailwayManager.Fleet.BreakdownService.SelfRepairSuccessChance(health, v.id);

            if (s_breakdownRng.Value < successChance)   // MP-9: seedowane
            {
                // Sukces — komponent +20% health, pociąg rusza
                SetComponentHealth(v, (RailwayManager.Fleet.ComponentType)st.brokenComponentIndex,
                                   Mathf.Min(100f, health + 20f));
                st.state = TrainState.Running;
                st.brokenComponentIndex = -1;
                st.brokenVehicleId = -1;
                st.selfRepairAttemptGameTime = 0;
                Log.Info($"[BreakdownService] Run#{st.trainRun.id} — self-repair SUCCESS. Rusza z delayed.");
            }
            else
            {
                // Fail — czeka na rescue
                st.state = TrainState.AwaitingRescue;
                Log.Warn($"[BreakdownService] Run#{st.trainRun.id} — self-repair FAIL (health {health:F0}%, " +
                         $"chance {successChance:P1}). Wymaga rescue loko.");
            }
        }

        System.Collections.Generic.List<int> CollectStationIdsOnRoute(SimulatedTrain st)
        {
            var result = new System.Collections.Generic.List<int>();
            if (st.timetable?.stops == null) return result;
            var init = RailwayManager.Timetable.TimetableInitializer.Instance;
            if (init?.Stations == null) return result;
            foreach (var stop in st.timetable.stops)
                foreach (var rs in init.Stations)
                    if (rs.pathNodeId == stop.stationNodeId)
                    {
                        result.Add(rs.stationId);
                        break;
                    }
            return result;
        }

        static float GetComponentHealth(RailwayManager.Fleet.FleetVehicleData v, RailwayManager.Fleet.ComponentType type)
        {
            if (v?.components == null) return 0f;
            return type switch
            {
                RailwayManager.Fleet.ComponentType.Engine => v.components.engineCondition,
                RailwayManager.Fleet.ComponentType.Brake => v.components.brakeCondition,
                RailwayManager.Fleet.ComponentType.Doors => v.components.doorsCondition,
                RailwayManager.Fleet.ComponentType.AC => v.components.acCondition,
                RailwayManager.Fleet.ComponentType.Body => v.components.bodyCondition,
                RailwayManager.Fleet.ComponentType.Wheels => v.components.wheelsCondition,
                RailwayManager.Fleet.ComponentType.Electrical => v.components.electricalCondition,
                RailwayManager.Fleet.ComponentType.Interior => v.components.interiorCondition,
                RailwayManager.Fleet.ComponentType.Lights => v.components.lightsCondition,
                RailwayManager.Fleet.ComponentType.Toilets => v.components.toiletsCondition,
                RailwayManager.Fleet.ComponentType.Pantograph => v.components.pantographCondition,
                RailwayManager.Fleet.ComponentType.Coupling => v.components.couplingCondition,
                _ => 0f
            };
        }

        static void SetComponentHealth(RailwayManager.Fleet.FleetVehicleData v, RailwayManager.Fleet.ComponentType type, float value)
        {
            if (v?.components == null) return;
            switch (type)
            {
                case RailwayManager.Fleet.ComponentType.Engine: v.components.engineCondition = value; break;
                case RailwayManager.Fleet.ComponentType.Brake: v.components.brakeCondition = value; break;
                case RailwayManager.Fleet.ComponentType.Doors: v.components.doorsCondition = value; break;
                case RailwayManager.Fleet.ComponentType.AC: v.components.acCondition = value; break;
                case RailwayManager.Fleet.ComponentType.Body: v.components.bodyCondition = value; break;
                case RailwayManager.Fleet.ComponentType.Wheels: v.components.wheelsCondition = value; break;
                case RailwayManager.Fleet.ComponentType.Electrical: v.components.electricalCondition = value; break;
                case RailwayManager.Fleet.ComponentType.Interior: v.components.interiorCondition = value; break;
                case RailwayManager.Fleet.ComponentType.Lights: v.components.lightsCondition = value; break;
                case RailwayManager.Fleet.ComponentType.Toilets: v.components.toiletsCondition = value; break;
                case RailwayManager.Fleet.ComponentType.Pantograph: v.components.pantographCondition = value; break;
                case RailwayManager.Fleet.ComponentType.Coupling: v.components.couplingCondition = value; break;
            }
            v.conditionPercent = v.components.Average();
        }
    }
}
