using System.Collections.Generic;
using RailwayManager.Core;

namespace RailwayManager.Timetable.Simulation
{
    public partial class TrainRunSimulator
    {
        // ── Block occupancy helpers ─────────────────────────────────

        bool IsBlockFree(int sectionId, int myTrainRunId)
        {
            return !_blockOccupancy.TryGetValue(sectionId, out int owner) || owner == myTrainRunId;
        }

        void OccupyBlock(int sectionId, int trainRunId)
        {
            _blockOccupancy[sectionId] = trainRunId;
        }

        void ReleaseBlock(int sectionId, int trainRunId)
        {
            if (_blockOccupancy.TryGetValue(sectionId, out int owner) && owner == trainRunId)
                _blockOccupancy.Remove(sectionId);
        }

        void ReleaseAllBlocks(int trainRunId)
        {
            var toRemove = new List<int>();
            foreach (var kvp in _blockOccupancy)
                if (kvp.Value == trainRunId)
                    toRemove.Add(kvp.Key);
            foreach (var key in toRemove)
                _blockOccupancy.Remove(key);
        }

        // ── Platform occupancy helpers ──────────────────────────────

        /// <summary>Margines wyjazdu: tor zajęty jeszcze 30s gry po odjeździe.</summary>
        const float PlatformExitMarginSec = 30f;

        bool IsPlatformFree(int platformId, int myTrainRunId)
        {
            if (platformId < 0) return true; // unassigned = zawsze wolny
            return !_platformOccupancy.TryGetValue(platformId, out int owner) || owner == myTrainRunId;
        }

        void OccupyPlatform(int platformId, int trainRunId)
        {
            if (platformId < 0) return;
            _platformOccupancy[platformId] = trainRunId;
            // Skasuj ewentualny pending release timer
            _platformReleaseTimers.Remove(platformId);
        }

        void SchedulePlatformRelease(int platformId, int trainRunId)
        {
            if (platformId < 0) return;
            if (_platformOccupancy.TryGetValue(platformId, out int owner) && owner == trainRunId)
            {
                // Nie zwalniaj od razu — zaplanuj release za margines
                _platformReleaseTimers[platformId] = GameState.GameTimeSeconds + PlatformExitMarginSec;
            }
        }

        void ProcessPlatformReleaseTimers()
        {
            if (_platformReleaseTimers.Count == 0) return;

            float now = GameState.GameTimeSeconds;
            var expired = new List<int>();
            foreach (var kvp in _platformReleaseTimers)
            {
                if (now >= kvp.Value)
                    expired.Add(kvp.Key);
            }
            foreach (var platformId in expired)
            {
                _platformOccupancy.Remove(platformId);
                _platformReleaseTimers.Remove(platformId);
            }
        }

        void ReleaseAllPlatforms(int trainRunId)
        {
            var toRemove = new List<int>();
            foreach (var kvp in _platformOccupancy)
                if (kvp.Value == trainRunId)
                    toRemove.Add(kvp.Key);
            foreach (var key in toRemove)
            {
                _platformOccupancy.Remove(key);
                _platformReleaseTimers.Remove(key);
            }
        }
    }
}
