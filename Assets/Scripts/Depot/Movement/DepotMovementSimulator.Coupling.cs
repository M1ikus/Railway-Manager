using System.Collections.Generic;
using UnityEngine;
using RailwayManager.Core;
using RailwayManager.Fleet;
using RailwayManager.SharedUI.Localization;

namespace DepotSystem
{
    public partial class DepotMovementSimulator
    {
        // ── TD-032: sprzęganie / rozprzęganie składów ────────────────────────────

        /// <summary>
        /// TD-032: hook ostrzeżenia obiegowego (Depot NIE widzi Timetable). Instalowany przez
        /// Timetable (CouplingCirculationBootstrapper) → CirculationService.IsConsistInActiveCirculationToday.
        /// Null w scenach Depot-only/test. UI couple/decouple pyta przez <see cref="IsConsistInActiveCirculation"/>.
        /// </summary>
        public static System.Func<List<int>, bool> CirculationWarnHook;

        public static bool IsConsistInActiveCirculation(List<int> vehicleIds)
            => CirculationWarnHook != null && vehicleIds != null && CirculationWarnHook(vehicleIds);

        /// <summary>TD-032: vehicleIds składu z jego ConsistMarker (visual). Null gdy brak visuala.</summary>
        public List<int> GetConsistVehicleIds(int consistId)
        {
            if (_consistVisuals.TryGetValue(consistId, out var go) && go != null)
            {
                var marker = go.GetComponent<ConsistMarker>();
                if (marker != null && marker.vehicleIds != null) return marker.vehicleIds;
            }
            return null;
        }

        /// <summary>TD-032: czytelna nazwa składu do UI — FleetConsistData.name (match po nosie) lub fallback „#id".</summary>
        public string GetConsistDisplayName(int consistId)
        {
            var vids = GetConsistVehicleIds(consistId);
            if (vids != null && vids.Count > 0)
            {
                var fcd = ConsistCouplingMath.FindConsistByVehicleId(FleetService.Consists, vids[0]);
                if (fcd != null && !string.IsNullOrWhiteSpace(fcd.name)) return fcd.name;
            }
            return string.Format(LocalizationService.Get("popup_couple.consist_fallback"), consistId);
        }

        /// <summary>
        /// TD-032 (H): znajduje STOJĄCY skład sąsiadujący stykiem z danym na tym samym torze — dla
        /// ręcznego „Połącz z sąsiednim" PO WCZYTANIU (dwa osobne składy stojące przy sobie nie odpalają
        /// eventu dojazdu, bo żaden nie jechał). Zwraca consistId sąsiada lub -1. Oba muszą stać.
        /// </summary>
        public int FindAdjacentCouplableConsist(int consistId)
        {
            EnsureGraph();
            if (_graph == null || HasTaskForConsist(consistId)) return -1;

            int trackId = FindConsistTrack(consistId);
            if (trackId < 0) return -1;
            var self = _graph.GetOccupant(trackId, consistId);
            if (self == null) return -1;

            float tol = DepotOccupancyConstants.ContactGapM + 0.5f; // styk + tolerancja po load
            foreach (var o in _graph.GetOccupants(trackId))
            {
                if (o == null || o.ConsistId == consistId) continue;
                if (HasTaskForConsist(o.ConsistId)) continue;
                float gapAhead = o.FrontDistM - self.RearDistM;   // sąsiad tuż za (wyżej) self
                float gapBehind = self.FrontDistM - o.RearDistM;  // self tuż za sąsiadem
                if ((gapAhead >= -0.5f && gapAhead <= tol) || (gapBehind >= -0.5f && gapBehind <= tol))
                    return o.ConsistId;
            }
            return -1;
        }

        /// <summary>
        /// TD-032: łączy dwa STOJĄCE składy w jeden (survivor = mover). Scala footprint (re-anchor do
        /// sumy długości — styk domknięty do 0), kolejność vehicleIds nos→tył wg geometrii, FleetConsistData.
        /// Zwraca true gdy scalono. Brak ruchu — reuse dojazdu do styku z TD-031 PRZED tym wywołaniem.
        /// </summary>
        public bool CoupleConsists(int moverConsistId, int blockerConsistId)
        {
            EnsureGraph();
            if (_graph == null || moverConsistId == blockerConsistId) return false;

            if (HasTaskForConsist(moverConsistId) || HasTaskForConsist(blockerConsistId))
            {
                Log.Warn($"[DepotMovementSim] Couple odrzucony: consist#{moverConsistId}/#{blockerConsistId} w ruchu (tylko gdy stoi).");
                return false;
            }

            // Wspólny tor (punkt styku) — gdzie OBA mają occupanta.
            int trackId = FindSharedTrack(moverConsistId, blockerConsistId);
            if (trackId < 0)
            {
                Log.Warn($"[DepotMovementSim] Couple odrzucony: consist#{moverConsistId} i #{blockerConsistId} nie na wspólnym torze.");
                return false;
            }

            var occM = _graph.GetOccupant(trackId, moverConsistId);
            var occB = _graph.GetOccupant(trackId, blockerConsistId);
            if (occM == null || occB == null) return false;

            int mergedDir = occM.DirSign;
            float mNose = ConsistCouplingMath.NoseCoord(occM.FrontDistM, occM.RearDistM, occM.DirSign);
            float bNose = ConsistCouplingMath.NoseCoord(occB.FrontDistM, occB.RearDistM, occB.DirSign);
            bool moverFront = ConsistCouplingMath.IsAFront(mNose, bNose, mergedDir);

            List<int> frontIds, rearIds; int frontDir, rearDir;
            if (moverFront) { frontIds = occM.VehicleIds; frontDir = occM.DirSign; rearIds = occB.VehicleIds; rearDir = occB.DirSign; }
            else { frontIds = occB.VehicleIds; frontDir = occB.DirSign; rearIds = occM.VehicleIds; rearDir = occM.DirSign; }

            var mergedIds = ConsistCouplingMath.MergeVehicleOrder(frontIds, frontDir, rearIds, rearDir, mergedDir);

            float spanMin = Mathf.Min(occM.FrontDistM, occB.FrontDistM);
            float spanMax = Mathf.Max(occM.RearDistM, occB.RearDistM);
            float summedLen = ComputeConsistScale(mergedIds).z;
            var (mFront, mRear) = ConsistCouplingMath.MergeFootprint(spanMin, spanMax, mergedDir, summedLen);

            // Pre-merge nose vids do dopasowania FleetConsistData (przed nadpisaniem occupantów).
            int moverNoseVid = occM.VehicleIds != null && occM.VehicleIds.Count > 0 ? occM.VehicleIds[0] : -1;
            int blockerNoseVid = occB.VehicleIds != null && occB.VehicleIds.Count > 0 ? occB.VehicleIds[0] : -1;

            // Occupancy: wyczyść oba (także ew. straddle), ustaw scalony footprint pod mover.
            _graph.RemoveConsistEverywhere(blockerConsistId);
            _graph.RemoveConsistEverywhere(moverConsistId);
            _graph.SetOccupantInterval(trackId, moverConsistId, mergedIds, mFront, mRear, mergedDir);

            // Visuals: zniszcz oba, respawn scalony pod mover (świeży cube = poprawna skala + pozycja).
            DestroyVisual(blockerConsistId);
            DestroyVisual(moverConsistId);
            SpawnParkedVisual(moverConsistId, mergedIds, _graph.GetTrack(trackId));

            MergeFleetConsist(moverNoseVid, blockerNoseVid, mergedIds);

            Log.Info($"[DepotMovementSim] Sprzęgnięto consist#{blockerConsistId} → #{moverConsistId} " +
                     $"({mergedIds.Count} pojazdów, tor#{trackId}, footprint [{mFront:F1},{mRear:F1}]).");
            return true;
        }

        /// <summary>
        /// TD-032: dzieli STOJĄCY skład na dwa w miejscu cięcia cutIndex (front = vehicleIds[0..cutIndex)
        /// zachowuje consistId + FleetConsistData; tail = reszta = nowy consistId + nowy FleetConsistData
        /// „&lt;nazwa&gt; (2)"). Footprinty przyległe wewnątrz dotychczasowego. Zwraca true gdy podzielono.
        /// </summary>
        public bool DecoupleConsist(int consistId, int cutIndex)
        {
            EnsureGraph();
            if (_graph == null) return false;

            if (HasTaskForConsist(consistId))
            {
                Log.Warn($"[DepotMovementSim] Decouple odrzucony: consist#{consistId} w ruchu (tylko gdy stoi).");
                return false;
            }

            int trackId = FindConsistTrack(consistId);
            if (trackId < 0) return false;
            var occ = _graph.GetOccupant(trackId, consistId);
            if (occ?.VehicleIds == null || occ.VehicleIds.Count < 2)
            {
                Log.Warn($"[DepotMovementSim] Decouple odrzucony: consist#{consistId} ma <2 pojazdy.");
                return false;
            }
            if (cutIndex < 1 || cutIndex > occ.VehicleIds.Count - 1)
            {
                Log.Warn($"[DepotMovementSim] Decouple odrzucony: zły cutIndex={cutIndex} (count={occ.VehicleIds.Count}).");
                return false;
            }

            var frontIds = occ.VehicleIds.GetRange(0, cutIndex);
            var tailIds = occ.VehicleIds.GetRange(cutIndex, occ.VehicleIds.Count - cutIndex);
            float frontLen = ComputeConsistScale(frontIds).z;
            float tailLen = ComputeConsistScale(tailIds).z;
            var (frontFp, tailFp) = ConsistCouplingMath.SplitFootprint(occ.FrontDistM, occ.RearDistM, occ.DirSign, frontLen, tailLen);
            int dir = occ.DirSign;

            int tailId = GenerateConsistId();

            // Occupancy: front zachowuje id, tail nowy id (oba na tym torze, przyległe).
            _graph.SetOccupantInterval(trackId, consistId, frontIds, frontFp.front, frontFp.rear, dir);
            _graph.SetOccupantInterval(trackId, tailId, tailIds, tailFp.front, tailFp.rear, dir);

            // Visuals: respawn front (rescale) + spawn tail.
            DestroyVisual(consistId);
            SpawnParkedVisual(consistId, frontIds, _graph.GetTrack(trackId));
            SpawnParkedVisual(tailId, tailIds, _graph.GetTrack(trackId));

            SplitFleetConsist(frontIds, tailIds);

            Log.Info($"[DepotMovementSim] Rozprzęgnięto consist#{consistId} @ cut={cutIndex} → front#{consistId} ({frontIds.Count}) + tail#{tailId} ({tailIds.Count}), tor#{trackId}.");
            return true;
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        /// <summary>Pierwszy tor (sort po id) na którym consist ma occupanta. -1 gdy żaden.</summary>
        int FindConsistTrack(int consistId)
        {
            var ids = new List<int>(_graph.Tracks.Keys);
            ids.Sort();
            for (int i = 0; i < ids.Count; i++)
                if (_graph.GetOccupant(ids[i], consistId) != null) return ids[i];
            return -1;
        }

        /// <summary>Tor (sort po id) na którym OBA składy mają occupanta (punkt styku). -1 gdy brak wspólnego.</summary>
        int FindSharedTrack(int consistA, int consistB)
        {
            var ids = new List<int>(_graph.Tracks.Keys);
            ids.Sort();
            for (int i = 0; i < ids.Count; i++)
                if (_graph.GetOccupant(ids[i], consistA) != null && _graph.GetOccupant(ids[i], consistB) != null)
                    return ids[i];
            return -1;
        }

        void DestroyVisual(int consistId)
        {
            if (_consistVisuals.TryGetValue(consistId, out var go) && go != null) Destroy(go);
            _consistVisuals.Remove(consistId);
        }

        /// <summary>
        /// TD-032: zapisuje w tasku że ruch kończy się dojazdem DO STYKU za innym składem (capBinding +
        /// blocker) — FinalizeTask wyemituje wtedy <see cref="OnConsistArrivedAtContact"/>. World-pos = nos.
        /// </summary>
        void CaptureContactArrival(DepotMoveTask task, bool capBinding, bool forward, float halfLen)
        {
            if (capBinding && task.dynamicStopBlockerId >= 0)
            {
                task.arrivedContactBlockerId = task.dynamicStopBlockerId;
                float noseDist = task.currentDistanceM + (forward ? halfLen : -halfLen);
                var (pos, _) = SampleAtDistance(task, noseDist);
                task.arrivedContactWorldPos = new Vector3(pos.x, pos.y + VehicleYHeight, pos.z);
            }
            else
            {
                task.arrivedContactBlockerId = -1;
            }
        }

        void MergeFleetConsist(int moverNoseVid, int blockerNoseVid, List<int> mergedIds)
        {
            var consists = FleetService.Consists;
            var fcdM = ConsistCouplingMath.FindConsistByVehicleId(consists, moverNoseVid);
            var fcdB = ConsistCouplingMath.FindConsistByVehicleId(consists, blockerNoseVid);

            if (fcdM != null)
            {
                fcdM.vehicleIds = new List<int>(mergedIds);
                if (fcdB != null && !ReferenceEquals(fcdB, fcdM))
                    FleetService.RemoveConsists(c => ReferenceEquals(c, fcdB)); // odpala OnOwnedChanged
                else
                    FleetService.NotifyOwnedChanged();
            }
            else if (fcdB != null)
            {
                fcdB.vehicleIds = new List<int>(mergedIds);
                FleetService.NotifyOwnedChanged();
            }
            // else: brak FleetConsistData (consist depot-only / test) — occupancy już scalone, pomijamy warstwę Fleet.
        }

        void SplitFleetConsist(List<int> frontIds, List<int> tailIds)
        {
            var consists = FleetService.Consists;
            int frontNoseVid = frontIds.Count > 0 ? frontIds[0] : -1;
            var fcd = ConsistCouplingMath.FindConsistByVehicleId(consists, frontNoseVid);
            if (fcd == null) return; // brak FleetConsistData — occupancy już rozdzielone

            var names = new List<string>();
            foreach (var c in consists) if (c?.name != null) names.Add(c.name);

            fcd.vehicleIds = new List<int>(frontIds);
            FleetService.AddConsist(new FleetConsistData
            {
                name = ConsistCouplingMath.DedupConsistName(names, string.IsNullOrEmpty(fcd.name) ? "Skład" : fcd.name),
                vehicleIds = new List<int>(tailIds),
                route = fcd.route,
                status = fcd.status
            }); // odpala OnOwnedChanged
        }
    }
}
