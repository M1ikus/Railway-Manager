using System.Collections.Generic;
using UnityEngine;

namespace DepotSystem
{
    public partial class DepotMovementSimulator
    {
        // ── Wizualizacja consist'u ───────────────────────────────────

        void EnsureVisualForConsist(DepotMoveTask task)
        {
            if (_consistVisuals.TryGetValue(task.consistId, out var existing) && existing != null)
            {
                // Aktualizuj ConsistMarker (w razie gdyby vehicleIds się zmieniły)
                var existingMarker = existing.GetComponent<ConsistMarker>();
                if (existingMarker != null)
                {
                    existingMarker.consistId = task.consistId;
                    existingMarker.vehicleIds = task.vehicleIds;
                }
                return;
            }

            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = $"Consist_{task.consistId}";
            go.transform.SetParent(_visualsContainer);
            go.transform.localScale = ComputeConsistScale(task.vehicleIds);

            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null && _visualMaterial != null)
                mr.sharedMaterial = _visualMaterial;

            // Zachowaj collider (potrzebny do raycast click-detection).
            // isTrigger = true żeby nie interferował z fizyką.
            var col = go.GetComponent<Collider>();
            if (col != null) col.isTrigger = true;

            // Dodaj ConsistMarker — nośnik danych dla selection handlera
            var marker = go.AddComponent<ConsistMarker>();
            marker.consistId = task.consistId;
            marker.vehicleIds = task.vehicleIds;
            marker.currentTrackId = task.fromTrackId;
            marker.meshRenderer = mr;
            marker.originalMaterial = _visualMaterial;

            _consistVisuals[task.consistId] = go;

            // Pozycja startowa — pierwszy punkt polyline
            if (task.polyline != null && task.polyline.Count > 0)
            {
                var p = task.polyline[0];
                go.transform.position = new Vector3(p.x, p.y + VehicleYHeight, p.z);
            }
        }

        void UpdateVisualPosition(DepotMoveTask task)
        {
            if (!_consistVisuals.TryGetValue(task.consistId, out var visual) || visual == null)
                return;

            // Binary search na cumDistM
            var (pos, tangent) = SampleAtDistance(task, task.currentDistanceM);
            visual.transform.position = new Vector3(pos.x, pos.y + VehicleYHeight, pos.z);

            if (tangent.sqrMagnitude > 0.001f)
                visual.transform.rotation = Quaternion.LookRotation(tangent, Vector3.up);
        }

        public void RestoreParkedVisualsFromGraph(TrackGraph graph)
        {
            foreach (var kv in _consistVisuals)
                if (kv.Value != null) Destroy(kv.Value);
            _consistVisuals.Clear();
            _tasks.Clear();
            _pendingNextTask.Clear();

            _graph = graph;
            if (_visualsContainer == null)
            {
                var containerGo = new GameObject("ConsistVisuals");
                containerGo.transform.SetParent(transform);
                _visualsContainer = containerGo.transform;
            }

            if (graph == null) return;

            int maxConsistId = _nextConsistId - 1;

            // TD-031: każdy tor może mieć WIELE occupantów (interwałów). Consist może też straddle'ować
            // dwa tory (ten sam consistId na 2 torach) — wybierz reprezentanta z większym footprintem
            // (tie-break niższy trackId przez sortowaną iterację) i spawn raz per consist.
            var best = new Dictionary<int, (DepotTrackData track, TrackOccupant occ)>();
            var trackIdsSorted = new List<int>(graph.Tracks.Keys);
            trackIdsSorted.Sort();
            foreach (int tid in trackIdsSorted)
            {
                var track = graph.Tracks[tid];
                if (track?.Occupants == null) continue;
                foreach (var occ in track.Occupants)
                {
                    if (occ == null || occ.ConsistId < 0) continue;
                    if (!best.TryGetValue(occ.ConsistId, out var cur) || occ.LengthM > cur.occ.LengthM)
                        best[occ.ConsistId] = (track, occ);
                    if (occ.ConsistId > maxConsistId) maxConsistId = occ.ConsistId;
                }
            }

            foreach (var kv in best)
                SpawnParkedVisual(kv.Key, kv.Value.occ.VehicleIds, kv.Value.track);

            if (maxConsistId >= _nextConsistId)
                _nextConsistId = maxConsistId + 1;
        }

        /// <summary>
        /// M9c-D F2 / TD-031: tworzy (lub odświeża) visual zaparkowanego consist'u na pozycji z jego
        /// footprintu (środek interwału na osi toru), a nie na środku całego toru — pozwala wielu
        /// składom stać na jednym torze. Wspólna logika dla <see cref="RestoreParkedVisualsFromGraph"/>
        /// (load save) oraz <see cref="ParkConsistOnFreeTrack"/> (initial fleet / fallback dostawy).
        /// Fallback na środek toru gdy occupant nie znaleziony (np. wywołanie bez wcześniejszego SetOccupantInterval).
        /// </summary>
        void SpawnParkedVisual(int consistId, List<int> vehicleIds, DepotTrackData track)
        {
            var polyline = _graph.GetTrackPolyline(track.TrackId);
            if (polyline == null || polyline.Count < 2)
                polyline = new List<Vector3> { track.StartPosition, track.EndPosition };

            var task = new DepotMoveTask
            {
                consistId = consistId,
                vehicleIds = vehicleIds != null ? new List<int>(vehicleIds) : new List<int>(),
                fromTrackId = track.TrackId,
                toTrackId = track.TrackId,
                polyline = polyline
            };
            EnsureVisualForConsist(task);

            if (_consistVisuals.TryGetValue(consistId, out var visual) && visual != null)
            {
                // TD-031: pozycja z footprintu occupanta — anchor = środek interwału [Front,Rear].
                var occ = _graph.GetOccupant(track.TrackId, consistId);
                Vector3 pos, tangent;
                if (occ != null)
                {
                    float anchor = (occ.FrontDistM + occ.RearDistM) * 0.5f;
                    (pos, tangent) = _graph.GetPointOnTrack(track.TrackId, anchor);
                    if (occ.DirSign < 0) tangent = -tangent;
                }
                else
                {
                    (pos, tangent) = SamplePolylineMidpoint(polyline);
                }

                visual.transform.position = new Vector3(pos.x, pos.y + VehicleYHeight, pos.z);
                if (tangent.sqrMagnitude > 0.001f)
                    visual.transform.rotation = Quaternion.LookRotation(tangent, Vector3.up);

                var marker = visual.GetComponent<ConsistMarker>();
                if (marker != null)
                    marker.currentTrackId = track.TrackId;
            }
        }

        private static (Vector3 pos, Vector3 tangent) SamplePolylineMidpoint(List<Vector3> polyline)
        {
            if (polyline == null || polyline.Count == 0) return (Vector3.zero, Vector3.forward);
            if (polyline.Count == 1) return (polyline[0], Vector3.forward);

            float total = 0f;
            for (int i = 1; i < polyline.Count; i++)
                total += Vector3.Distance(polyline[i - 1], polyline[i]);

            float target = total * 0.5f;
            float acc = 0f;
            for (int i = 1; i < polyline.Count; i++)
            {
                Vector3 a = polyline[i - 1];
                Vector3 b = polyline[i];
                float len = Vector3.Distance(a, b);
                if (acc + len >= target)
                {
                    float t = len > 0.001f ? (target - acc) / len : 0f;
                    return (Vector3.Lerp(a, b, t), SafeTangent(b - a));
                }
                acc += len;
            }

            int last = polyline.Count - 1;
            return (polyline[last], SafeTangent(polyline[last] - polyline[last - 1]));
        }

        /// <summary>Crash-hunt: zdegenerowany segment (identyczne punkty) → wektor zerowy → .normalized=zero
        /// → LookRotation(zero) loguje błąd Unity + identity rotation. Fallback na forward gdy ~zero.</summary>
        private static Vector3 SafeTangent(Vector3 seg)
        {
            return seg.sqrMagnitude > 0.0001f ? seg.normalized : Vector3.forward;
        }
    }
}
