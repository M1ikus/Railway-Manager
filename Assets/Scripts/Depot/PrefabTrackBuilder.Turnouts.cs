using System.Collections.Generic;
using RailwayManager.Core;
using UnityEngine;

namespace DepotSystem
{
    public partial class PrefabTrackBuilder
    {
        // ═══════════════════════════════════════════
        //  REJESTR ROZJAZDÓW — track ↔ turnout mapping
        // ═══════════════════════════════════════════

        /// <summary>
        /// Rejestruje rozjazd — grupuje segmenty toru w jeden logiczny rozjazd.
        /// </summary>
        public void RegisterTurnout(TurnoutEntity entity)
        {
            entity.TurnoutId = nextTurnoutId++;
            turnoutEntities[entity.TurnoutId] = entity;
            foreach (int trackId in entity.MemberTrackIds)
                trackIdToTurnoutId[trackId] = entity.TurnoutId;

            Log.Info($"[PrefabTrackBuilder] Registered turnout '{entity.DefinitionName}' id={entity.TurnoutId} with {entity.MemberTrackIds.Count} segments: [{string.Join(", ", entity.MemberTrackIds)}]");
        }

        /// <summary>
        /// Sprawdza czy segment toru należy do rozjazdu.
        /// </summary>
        public bool TryGetTurnoutForTrack(int graphTrackId, out TurnoutEntity entity)
        {
            entity = null;
            if (trackIdToTurnoutId.TryGetValue(graphTrackId, out int turnoutId))
            {
                bool found = turnoutEntities.TryGetValue(turnoutId, out entity);
                Log.Info($"[PrefabTrackBuilder] TryGetTurnoutForTrack({graphTrackId}) → turnoutId={turnoutId}, found={found}");
                return found;
            }
            Log.Info($"[PrefabTrackBuilder] TryGetTurnoutForTrack({graphTrackId}) → not in any turnout. Registry has {trackIdToTurnoutId.Count} entries.");
            return false;
        }

        public List<TurnoutEntitySnapshot> GetTurnoutSnapshot()
        {
            var result = new List<TurnoutEntitySnapshot>(turnoutEntities.Count);
            foreach (var kv in turnoutEntities)
            {
                var entity = kv.Value;
                if (entity == null) continue;
                result.Add(new TurnoutEntitySnapshot
                {
                    turnoutId = entity.TurnoutId,
                    definitionName = entity.DefinitionName,
                    type = entity.Type,
                    memberTrackIds = entity.MemberTrackIds != null
                        ? new List<int>(entity.MemberTrackIds)
                        : new List<int>(),
                    origin = entity.Origin,
                    direction = entity.Direction,
                    divergeLeft = entity.DivergeLeft,
                    originalPolyline = entity.OriginalPolyline != null
                        ? new List<Vector3>(entity.OriginalPolyline)
                        : new List<Vector3>(),
                    originalTrackName = entity.OriginalTrackName,
                    originalTrackType = entity.OriginalTrackType,
                    flipDirection = entity.FlipDirection,
                    distAlongChain = entity.DistAlongChain,
                    currentPosition = entity.CurrentPosition
                });
            }
            return result;
        }

        public void RestoreTurnoutsFromSave(IList<TurnoutEntitySnapshot> snapshots)
        {
            turnoutEntities.Clear();
            trackIdToTurnoutId.Clear();
            nextTurnoutId = 0;

            if (snapshots != null)
            {
                foreach (var snap in snapshots)
                {
                    if (snap == null || snap.turnoutId < 0) continue;
                    var entity = new TurnoutEntity(snap.definitionName ?? "", snap.type)
                    {
                        TurnoutId = snap.turnoutId,
                        MemberTrackIds = snap.memberTrackIds != null
                            ? new List<int>(snap.memberTrackIds)
                            : new List<int>(),
                        Origin = snap.origin,
                        Direction = snap.direction,
                        DivergeLeft = snap.divergeLeft,
                        Definition = ResolveDefinition(snap.definitionName),
                        OriginalPolyline = snap.originalPolyline != null
                            ? new List<Vector3>(snap.originalPolyline)
                            : new List<Vector3>(),
                        OriginalTrackName = snap.originalTrackName,
                        OriginalTrackType = snap.originalTrackType,
                        FlipDirection = snap.flipDirection,
                        DistAlongChain = snap.distAlongChain,
                        CurrentPosition = snap.currentPosition
                    };

                    turnoutEntities[entity.TurnoutId] = entity;
                    foreach (int trackId in entity.MemberTrackIds)
                        trackIdToTurnoutId[trackId] = entity.TurnoutId;

                    if (entity.TurnoutId >= nextTurnoutId)
                        nextTurnoutId = entity.TurnoutId + 1;
                }
            }

            Log.Info($"[PrefabTrackBuilder] Restored {turnoutEntities.Count} turnout registry item(s), nextTurnoutId={nextTurnoutId}");
        }

        private static TurnoutData.TurnoutDefinition ResolveDefinition(string name)
        {
            if (name == TurnoutData.R190_1_9.Name) return TurnoutData.R190_1_9;
            if (name == TurnoutData.R300_1_9.Name) return TurnoutData.R300_1_9;
            if (name == TurnoutData.Crossover_R190.Name) return TurnoutData.Crossover_R190;
            return TurnoutData.R190_1_9;
        }
    }

    [System.Serializable]
    public class TurnoutEntitySnapshot
    {
        public int turnoutId;
        public string definitionName;
        public TurnoutEntityType type;
        public List<int> memberTrackIds = new();
        public Vector3 origin;
        public Vector3 direction;
        public bool divergeLeft;
        public List<Vector3> originalPolyline = new();
        public string originalTrackName;
        public DepotTrackType originalTrackType;
        public bool flipDirection;
        public float distAlongChain;
        public SwitchPosition currentPosition = SwitchPosition.Straight;
    }
}
