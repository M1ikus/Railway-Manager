using System.Collections.Generic;
using UnityEngine;
using RailwayManager.Core;

namespace DepotSystem
{
    public partial class DepotMovementSimulator
    {
        // ── Debug context-menu actions ───────────────────────────────

        [ContextMenu("Debug: Test move (first two parking tracks)")]
        public void DebugTestMove()
        {
            EnsureGraph();
            if (_graph == null) { Log.Warn("[DepotMovementSim] No TrackGraph in scene"); return; }

            var tracks = _graph.GetTracksByType(DepotTrackType.Parking);
            if (tracks.Count < 2)
            {
                Log.Warn($"[DepotMovementSim] Need 2+ parking tracks (got {tracks.Count})");
                return;
            }

            // isSelfMove:false — fake vehicleIds nie istnieją we flocie, więc pomiń regułę napędu (TD-031).
            EnqueueMove(999, new List<int> { 1, 2 }, tracks[0].TrackId, tracks[1].TrackId, isSelfMove: false);
        }

        [ContextMenu("Debug: Spawn consist at entry (wjazd z zewnątrz)")]
        public void DebugTestSpawnEntry()
        {
            EnsureGraph();
            if (_graph == null) { Log.Warn("[DepotMovementSim] No TrackGraph in scene"); return; }

            int testId = 888;
            // Jeśli już istnieje — użyj innego ID żeby symulować kolejne wjazdy
            while (_consistVisuals.ContainsKey(testId)) testId++;

            SpawnConsistAtEntry(testId, new List<int> { 101, 102 });
        }

        [ContextMenu("Debug: Park 3 consists on one track (TD-031)")]
        public void DebugPark3OnOneTrack()
        {
            EnsureGraph();
            if (_graph == null) { Log.Warn("[DepotMovementSim] No TrackGraph in scene"); return; }

            var tracks = _graph.GetTracksByType(DepotTrackType.Parking);
            if (tracks.Count == 0) { Log.Warn("[DepotMovementSim] No parking tracks"); return; }

            tracks.Sort((a, b) => a.TrackId.CompareTo(b.TrackId));
            var track = tracks[0];

            int baseId = 700;
            while (_consistVisuals.ContainsKey(baseId) || _consistVisuals.ContainsKey(baseId + 1) ||
                   _consistVisuals.ContainsKey(baseId + 2))
                baseId += 3;

            float len = ComputeConsistScale(new List<int>()).z; // fake ids → fallback length
            float gap = DepotOccupancyConstants.MinParkingGapM;
            float cursor = 0f;
            int placed = 0;
            for (int k = 0; k < 3; k++)
            {
                if (cursor + len > track.Length) break;
                int cid = baseId + k;
                var vids = new List<int> { 900 + k };
                _graph.SetOccupantInterval(track.TrackId, cid, vids, cursor, cursor + len, 1);
                SpawnParkedVisual(cid, vids, track);
                cursor += len + gap;
                placed++;
            }

            Log.Info($"[DepotMovementSim] TD-031 debug: zaparkowano {placed} składów na torze#{track.TrackId} " +
                     $"(Length={track.Length:F1}m, len={len:F1}m, gap={gap:F1}m). Sprawdź brak nachodzenia + osobne cube'y.");
        }

        [ContextMenu("Debug: Two consists same track — follow to contact (TD-031)")]
        public void DebugFollowToContact()
        {
            EnsureGraph();
            if (_graph == null) { Log.Warn("[DepotMovementSim] No TrackGraph in scene"); return; }

            var tracks = _graph.GetTracksByType(DepotTrackType.Parking);
            if (tracks.Count == 0) { Log.Warn("[DepotMovementSim] No parking tracks"); return; }
            tracks.Sort((a, b) => a.TrackId.CompareTo(b.TrackId));
            var track = tracks[0];

            float len = ComputeConsistScale(new List<int>()).z; // 20m fallback
            if (track.Length < len * 2f + 5f)
            {
                Log.Warn($"[DepotMovementSim] Tor#{track.TrackId} za krótki ({track.Length:F1}m) na demo (potrzeba >{len * 2f + 5f:F1}m)");
                return;
            }

            // A — stoi przy końcu toru
            int aId = 760; while (_consistVisuals.ContainsKey(aId)) aId++;
            float aFront = track.Length - len;
            _graph.SetOccupantInterval(track.TrackId, aId, new List<int> { 950 }, aFront, track.Length, 1);
            SpawnParkedVisual(aId, new List<int> { 950 }, track);

            // B — startuje przy początku, jedzie do końca → powinien zahamować przy styku za A
            int bId = aId + 1; while (_consistVisuals.ContainsKey(bId)) bId++;
            _graph.SetOccupantInterval(track.TrackId, bId, new List<int> { 951 }, 0f, len, 1);
            SpawnParkedVisual(bId, new List<int> { 951 }, track);

            var endPoint = _graph.GetPointOnTrack(track.TrackId, track.Length).position;
            EnqueueMove(bId, new List<int> { 951 }, track.TrackId, track.TrackId, endPoint, isSelfMove: false);

            Log.Info($"[DepotMovementSim] TD-031 demo: A#{aId} stoi @ [{aFront:F1},{track.Length:F1}] tor#{track.TrackId}; " +
                     $"B#{bId} jedzie do końca — powinien zahamować ~{DepotOccupancyConstants.ContactGapM:F2}m za A " +
                     $"(crawl na finiszu). Zmierz lukę cube-cube.");
        }
    }
}
