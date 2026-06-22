using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DepotSystem
{
    public partial class TrackGraph
    {
        // ═══════════════════════════════════════════
        //  TORY (TRACKS - logiczne jednostki)
        // ═══════════════════════════════════════════

        /// <summary>Dodaje tor (logiczną jednostkę - np. "Tor postojowy 1")</summary>
        public int AddTrack(string name, DepotTrackType type, List<int> edgeIds, bool hasCatenary = false)
        {
            int id = nextTrackId++;

            Vector3 start = Vector3.zero;
            Vector3 end = Vector3.zero;
            Vector3 startDir = Vector3.forward;
            Vector3 endDir = Vector3.forward;
            float totalLength = 0f;

            if (edgeIds.Count > 0)
            {
                var firstEdge = edges[edgeIds[0]];
                start = nodes[firstEdge.FromNodeId].Position;

                var lastEdge = edges[edgeIds[^1]];
                end = nodes[lastEdge.ToNodeId].Position;

                // Kierunki z polyline
                if (firstEdge.Polyline != null && firstEdge.Polyline.Count >= 2)
                    startDir = TrackGeometry.GetStartTangent(firstEdge.Polyline);
                else
                    startDir = (end - start).normalized;

                if (lastEdge.Polyline != null && lastEdge.Polyline.Count >= 2)
                    endDir = TrackGeometry.GetEndTangent(lastEdge.Polyline);
                else
                    endDir = (end - start).normalized;

                foreach (int eid in edgeIds)
                {
                    if (edges.ContainsKey(eid))
                        totalLength += edges[eid].Length;
                }
            }

            tracks[id] = new DepotTrackData
            {
                TrackId = id,
                Name = name,
                TrackType = type,
                EdgeIds = new List<int>(edgeIds),
                HasCatenary = hasCatenary,
                StartPosition = start,
                EndPosition = end,
                StartDirection = startDir,
                EndDirection = endDir,
                Length = totalLength,
                IsOccupied = false,
                OccupyingTrainId = -1
            };

            if (hasCatenary)
            {
                foreach (int eid in edgeIds)
                {
                    if (edges.ContainsKey(eid))
                        edges[eid].HasCatenary = true;
                }
            }

            OnTopologyChanged?.Invoke();
            return id;
        }

        /// <summary>Usuwa tor i jego krawędzie z grafu</summary>
        public void RemoveTrack(int trackId)
        {
            if (!tracks.ContainsKey(trackId)) return;

            var track = tracks[trackId];
            HashSet<int> affectedNodes = new();

            foreach (int eid in track.EdgeIds)
            {
                if (edges.ContainsKey(eid))
                {
                    var edge = edges[eid];
                    if (nodes.ContainsKey(edge.FromNodeId))
                    {
                        nodes[edge.FromNodeId].EdgeIds.Remove(eid);
                        affectedNodes.Add(edge.FromNodeId);
                    }
                    if (nodes.ContainsKey(edge.ToNodeId))
                    {
                        nodes[edge.ToNodeId].EdgeIds.Remove(eid);
                        affectedNodes.Add(edge.ToNodeId);
                    }
                    edges.Remove(eid);
                }
            }

            // Przelicz typy node'ów
            foreach (int nodeId in affectedNodes)
                UpdateNodeType(nodeId);

            tracks.Remove(trackId);
            OnTopologyChanged?.Invoke();
        }

        /// <summary>Ustawia/usuwa sieć trakcyjną na torze</summary>
        public void SetTrackCatenary(int trackId, bool hasCatenary)
        {
            if (!tracks.ContainsKey(trackId)) return;

            tracks[trackId].HasCatenary = hasCatenary;

            foreach (int eid in tracks[trackId].EdgeIds)
            {
                if (edges.ContainsKey(eid))
                    edges[eid].HasCatenary = hasCatenary;
            }

            OnCatenaryChanged?.Invoke();
        }

        /// <summary>Zwraca listę zelektryfikowanych torów</summary>
        public List<int> GetElectrifiedTrackIds()
        {
            return tracks.Values
                .Where(t => t.HasCatenary)
                .Select(t => t.TrackId)
                .ToList();
        }

        // TD-031: legacy 1-arg OccupyTrack(trackId,trainId)/FreeTrack(trackId) USUNIĘTE (2026-06-08) —
        // 0 callerów + omijały listę Occupants (mogły zdesync'ować mirror). Zajętość idzie wyłącznie
        // przez OccupyTrackByConsist / SetOccupantInterval / FreeTrackForConsist (per-consist, pozycyjnie).

        /// <summary>Zmienia typ istniejącego toru (np. Parking → Exit dla integracji M9b).</summary>
        public void SetTrackType(int trackId, DepotTrackType type)
        {
            if (!tracks.ContainsKey(trackId)) return;
            tracks[trackId].TrackType = type;
        }

        // ═══════════════════════════════════════════════════════════════
        //  TD-031: Per-consist occupancy POZYCYJNA (interwały) + legacy mirror
        // ═══════════════════════════════════════════════════════════════

        /// <summary>Margines styku [m] dla zapytań o wolny zakres (TD-031).</summary>
        public float SafetyGapM => DepotOccupancyConstants.ContactGapM;

        /// <summary>Occupanci toru (footprinty). Pusta lista = tor wolny. Tylko do odczytu.</summary>
        public IReadOnlyList<TrackOccupant> GetOccupants(int trackId)
            => tracks.TryGetValue(trackId, out var t) ? t.Occupants : System.Array.Empty<TrackOccupant>();

        /// <summary>Footprint danego consist'u na torze, lub null gdy go tam nie ma.</summary>
        public TrackOccupant GetOccupant(int trackId, int consistId)
        {
            if (!tracks.TryGetValue(trackId, out var t)) return null;
            for (int i = 0; i < t.Occupants.Count; i++)
                if (t.Occupants[i].ConsistId == consistId) return t.Occupants[i];
            return null;
        }

        /// <summary>
        /// Ustawia/aktualizuje footprint consist'u na torze (upsert po consistId), clamp do [0,Length].
        /// front/rear w track-local metrach (kolejność dowolna), dirSign = orientacja nosa.
        /// Wołane co tick przy ruchu — dlatego: vehicleIds kopiowane TYLKO przy create/zmianie zbioru,
        /// a legacy-mirror przeliczany TYLKO przy zmianie strukturalnej (mirror zależy od „kto", nie „gdzie").
        /// Zero alokacji na czystej aktualizacji pozycji.
        /// </summary>
        public void SetOccupantInterval(int trackId, int consistId, List<int> vehicleIds,
                                        float frontM, float rearM, int dirSign)
        {
            if (!tracks.TryGetValue(trackId, out var track)) return;

            float lo = frontM <= rearM ? frontM : rearM;
            float hi = frontM <= rearM ? rearM : frontM;
            lo = Mathf.Clamp(lo, 0f, track.Length);
            hi = Mathf.Clamp(hi, 0f, track.Length);

            TrackOccupant occ = null;
            for (int i = 0; i < track.Occupants.Count; i++)
                if (track.Occupants[i].ConsistId == consistId) { occ = track.Occupants[i]; break; }

            bool structuralChange = false;
            if (occ == null)
            {
                occ = new TrackOccupant { ConsistId = consistId, VehicleIds = CopyIds(vehicleIds) };
                track.Occupants.Add(occ);
                structuralChange = true;
            }
            else if (!SameIntList(occ.VehicleIds, vehicleIds))
            {
                occ.VehicleIds = CopyIds(vehicleIds); // skład zmieniony (np. sprzęg TD-032) — rzadkie
                structuralChange = true;
            }

            occ.FrontDistM = lo;
            occ.RearDistM = hi;
            occ.DirSign = dirSign >= 0 ? 1 : -1;

            // Mirror (IsOccupied/Occupying*) = „kto stoi na torze", nie pozycja — recompute tylko gdy
            // zmienił się zbiór occupantów / vehicleIds. Czysty ruch (front/rear) go nie dotyka.
            if (structuralChange) RecomputeLegacyMirror(track);
        }

        private static List<int> CopyIds(List<int> src)
            => src != null ? new List<int>(src) : new List<int>();

        private static bool SameIntList(List<int> a, List<int> b)
        {
            int ca = a?.Count ?? 0, cb = b?.Count ?? 0;
            if (ca != cb) return false;
            for (int i = 0; i < ca; i++)
                if (a[i] != b[i]) return false;
            return true;
        }

        /// <summary>Usuwa footprint danego consist'u z toru (no-op gdy go nie ma). Przelicza mirror.</summary>
        public void RemoveOccupant(int trackId, int consistId)
        {
            if (!tracks.TryGetValue(trackId, out var track)) return;
            int removed = track.Occupants.RemoveAll(o => o.ConsistId == consistId);
            if (removed > 0) RecomputeLegacyMirror(track);
        }

        /// <summary>
        /// Usuwa consist ze WSZYSTKICH torów (straddle / despawn / exit). Iteracja po trackId
        /// posortowana (determinizm MP-9).
        /// </summary>
        public void RemoveConsistEverywhere(int consistId)
        {
            var ids = new List<int>(tracks.Keys);
            ids.Sort();
            for (int i = 0; i < ids.Count; i++)
                RemoveOccupant(ids[i], consistId);
        }

        /// <summary>Czy zakres [fromM,toM] na torze jest wolny od INNYCH consistów (z marginesem styku).</summary>
        public bool IsRangeFreeFor(int trackId, float fromM, float toM, int consistId)
        {
            if (!tracks.TryGetValue(trackId, out var track)) return false;
            return TrackOccupancyMath.IsRangeFree(track.Occupants, fromM, toM, consistId, SafetyGapM);
        }

        /// <summary>
        /// Szuka wolnej luki długości requiredLengthM na torze (parkowanie/pakowanie). Zwraca lewy
        /// koniec footprintu w gapStartM. False gdy nie mieści.
        /// </summary>
        public bool TryFindFreeGapForLength(int trackId, float requiredLengthM, out float gapStartM)
        {
            gapStartM = 0f;
            if (!tracks.TryGetValue(trackId, out var track)) return false;
            return TrackOccupancyMath.TryFindFreeGap(track.Occupants, track.Length, requiredLengthM,
                                                     DepotOccupancyConstants.MinParkingGapM, out gapStartM);
        }

        /// <summary>
        /// Zajmuje CAŁY tor przez consist (footprint [0,Length]). Kompat-wrapper starego binarnego
        /// API — nowy kod używa <see cref="SetOccupantInterval"/> z realnym footprintem. Utrzymuje mirror.
        /// </summary>
        public void OccupyTrackByConsist(int trackId, int consistId, List<int> vehicleIds)
        {
            if (!tracks.TryGetValue(trackId, out var track)) return;
            SetOccupantInterval(trackId, consistId, vehicleIds, 0f, track.Length, 1);
        }

        /// <summary>
        /// Zwalnia footprint danego consist'u z toru (kompat-wrapper). Z konstrukcji nie rusza
        /// occupantów innych składów (ochrona przed race jak w starym API).
        /// </summary>
        public void FreeTrackForConsist(int trackId, int consistId)
        {
            RemoveOccupant(trackId, consistId);
        }

        /// <summary>
        /// Czy tor jest wolny dla podanego consist'u (binarnie: pusty lub zajęty wyłącznie przez ten
        /// consist). Legacy — czyta mirror. Pełna semantyka pozycyjna w <see cref="IsRangeFreeFor"/>.
        /// </summary>
        public bool IsTrackFreeFor(int trackId, int consistId)
        {
            if (!tracks.ContainsKey(trackId)) return false;
            var track = tracks[trackId];
            if (!track.IsOccupied) return true;
            return track.OccupyingConsistId == consistId;
        }

        /// <summary>
        /// Przelicza pochodne pola legacy (IsOccupied / Occupying*) z listy Occupants — reprezentantem
        /// jest pierwszy occupant. Dla świata binarnego (1 occupant) = dokładnie stare zachowanie.
        /// </summary>
        private static void RecomputeLegacyMirror(DepotTrackData track)
        {
            if (track.Occupants != null && track.Occupants.Count > 0)
            {
                var first = track.Occupants[0];
                track.IsOccupied = true;
                track.OccupyingConsistId = first.ConsistId;
                track.OccupyingVehicleIds = new List<int>(first.VehicleIds ?? new List<int>());
                track.OccupyingTrainId = first.ConsistId;
            }
            else
            {
                track.IsOccupied = false;
                track.OccupyingConsistId = -1;
                track.OccupyingVehicleIds?.Clear();
                track.OccupyingTrainId = -1;
            }
        }

        // ═══════════════════════════════════════════
        //  QUERY METHODS
        // ═══════════════════════════════════════════

        public List<DepotTrackData> GetAvailableParkingTracks(bool requireCatenary = false)
        {
            return tracks.Values
                .Where(t => t.TrackType == DepotTrackType.Parking && !t.IsOccupied)
                .Where(t => !requireCatenary || t.HasCatenary)
                .ToList();
        }

        public List<DepotTrackData> GetTracksByType(DepotTrackType type)
        {
            return tracks.Values.Where(t => t.TrackType == type).ToList();
        }

        public DepotTrackData GetTrack(int trackId)
        {
            return tracks.ContainsKey(trackId) ? tracks[trackId] : null;
        }

        public void RenameTrack(int trackId, string newName)
        {
            if (!tracks.ContainsKey(trackId)) return;
            tracks[trackId].Name = newName;
        }

        /// <summary>Pobiera polyline dla krawędzi toru</summary>
        public List<Vector3> GetTrackPolyline(int trackId)
        {
            if (!tracks.ContainsKey(trackId)) return null;

            var track = tracks[trackId];
            List<Vector3> fullPolyline = new();

            foreach (int eid in track.EdgeIds)
            {
                if (!edges.ContainsKey(eid)) continue;
                var edge = edges[eid];

                if (edge.Polyline != null)
                {
                    int startIdx = fullPolyline.Count == 0 ? 0 : 1; // Pomiń duplikat
                    for (int i = startIdx; i < edge.Polyline.Count; i++)
                        fullPolyline.Add(edge.Polyline[i]);
                }
            }

            return fullPolyline;
        }

        // ═══════════════════════════════════════════
        //  HELPERY DLA SIECI TRAKCYJNEJ + spatial queries
        // ═══════════════════════════════════════════

        /// <summary>Zwraca obiekty krawędzi dla danego toru.</summary>
        public List<TrackEdge> GetEdgesForTrack(int trackId)
        {
            var result = new List<TrackEdge>();
            if (!tracks.ContainsKey(trackId)) return result;
            foreach (int eid in tracks[trackId].EdgeIds)
                if (edges.ContainsKey(eid))
                    result.Add(edges[eid]);
            return result;
        }

        /// <summary>Zwraca ID torów sąsiadujących (dzielących node) z danym torem.</summary>
        public List<int> GetAdjacentTrackIds(int trackId)
        {
            var result = new HashSet<int>();
            if (!tracks.ContainsKey(trackId)) return new List<int>();

            // Zbierz node'y tego toru
            var trackNodeIds = new HashSet<int>();
            foreach (int eid in tracks[trackId].EdgeIds)
            {
                if (!edges.ContainsKey(eid)) continue;
                trackNodeIds.Add(edges[eid].FromNodeId);
                trackNodeIds.Add(edges[eid].ToNodeId);
            }

            // Znajdź inne tory dzielące te node'y
            foreach (var otherTrack in tracks.Values)
            {
                if (otherTrack.TrackId == trackId) continue;
                foreach (int eid in otherTrack.EdgeIds)
                {
                    if (!edges.ContainsKey(eid)) continue;
                    if (trackNodeIds.Contains(edges[eid].FromNodeId) ||
                        trackNodeIds.Contains(edges[eid].ToNodeId))
                    {
                        result.Add(otherTrack.TrackId);
                        break;
                    }
                }
            }

            return new List<int>(result);
        }

        /// <summary>Zwraca ID torów mających krawędź dotykającą danego node'a.</summary>
        public List<int> GetTracksAtNode(int nodeId)
        {
            var result = new List<int>();
            if (!nodes.ContainsKey(nodeId)) return result;

            var nodeEdgeIds = new HashSet<int>(nodes[nodeId].EdgeIds);
            foreach (var track in tracks.Values)
            {
                foreach (int eid in track.EdgeIds)
                {
                    if (nodeEdgeIds.Contains(eid))
                    {
                        result.Add(track.TrackId);
                        break;
                    }
                }
            }
            return result;
        }

        /// <summary>Convenience: pozycja i tangenta na torze w danej odległości.</summary>
        public (Vector3 position, Vector3 tangent) GetPointOnTrack(int trackId, float distance)
        {
            var poly = GetTrackPolyline(trackId);
            if (poly == null || poly.Count < 2) return (Vector3.zero, Vector3.forward);
            return TrackGeometry.GetPointAtDistance(poly, distance);
        }
    }
}
