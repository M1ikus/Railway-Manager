using System.Collections.Generic;
using UnityEngine;
using RailwayManager.Core;

namespace DepotSystem
{
    public partial class PrefabTrackBuilder
    {
        // ═══════════════════════════════════════════
        //  REBUILD + REMOVE — tory + rozjazdy + merge
        // ═══════════════════════════════════════════

        /// <summary>
        /// Przebudowuje istniejący tor z nową polyline.
        /// Zachowuje metadane (nazwa, typ, sieć trakcyjna).
        /// Zwraca nowy graphTrackId.
        /// </summary>
        public int RebuildTrackWithPolyline(
            int oldTrackId, List<Vector3> newPolyline,
            string trackName, DepotTrackType trackType, bool hasCatenary)
        {
            // Usuń stary tor (wizualnie i z grafu)
            PlacedTrackSegment found = null;
            for (int i = 0; i < placedTracks.Count; i++)
            {
                if (placedTracks[i].GraphTrackId == oldTrackId)
                {
                    found = placedTracks[i];
                    placedTracks.RemoveAt(i);
                    break;
                }
            }

            if (found != null && found.TrackObject != null)
                Destroy(found.TrackObject);

            if (trackGraph != null)
                trackGraph.RemoveTrack(oldTrackId);

            // Utwórz nowy tor z nową polyline. M-Economy Faza 5: rebuild = przebudowa wewnętrzna
            // (split/merge), NIE nowa budowa — suppress charge (gracz już zapłacił za oryginał).
            bool prevSuppress = ConstructionBilling.SuppressCharging;
            ConstructionBilling.SuppressCharging = true;
            PlacedTrackSegment segment;
            try { segment = PlaceTrackWithPolyline(newPolyline, trackName, trackType); }
            finally { ConstructionBilling.SuppressCharging = prevSuppress; }

            Log.Info($"[PrefabTrackBuilder] Rebuilt track: old={oldTrackId} -> new={segment?.GraphTrackId}");
            return segment?.GraphTrackId ?? -1;
        }

        /// <summary>
        /// Usuwa tor po graphTrackId.
        /// Permanent tracks (M9b Etap 5) są chronione przed usunięciem.
        /// Tor będący częścią rozjazdu → usuwa cały rozjazd.
        /// </summary>
        public void RemoveTrack(int graphTrackId)
        {
            // M9b Etap 5: blokuj usuwanie torów permanentnych (wygenerowanych przez system)
            if (trackGraph != null)
            {
                var trackData = trackGraph.GetTrack(graphTrackId);
                if (trackData != null && trackData.IsPermanent)
                {
                    Log.Warn($"[PrefabTrackBuilder] Nie można usunąć toru permanentnego #{graphTrackId} ('{trackData.Name}')");
                    return;
                }
            }

            // If this track is part of a turnout, remove the ENTIRE turnout
            // (RemoveTurnout odtwarza oryginalny prosty tor z OriginalPolyline)
            if (trackIdToTurnoutId.TryGetValue(graphTrackId, out int turnoutId)
                && turnoutEntities.ContainsKey(turnoutId))
            {
                RemoveTurnout(turnoutId);
                return;
            }

            RemoveTrackInternal(graphTrackId);
        }

        /// <summary>
        /// Internal: usuwa tor bez sprawdzania turnout membership (używane przez RemoveTurnout)
        /// </summary>
        private void RemoveTrackInternal(int graphTrackId)
        {
            // Zapisz dane toru do undo (przed usunięciem)
            List<Vector3> savedPolyline = null;
            string savedName = null;
            DepotTrackType savedType = DepotTrackType.Parking;
            bool wasMemberOfTurnout = trackIdToTurnoutId.ContainsKey(graphTrackId);

            if (trackGraph != null && !wasMemberOfTurnout)
            {
                var trackData = trackGraph.GetTrack(graphTrackId);
                if (trackData != null)
                {
                    savedPolyline = trackGraph.GetTrackPolyline(graphTrackId);
                    savedName = trackData.Name;
                    savedType = trackData.TrackType;
                }
            }

            // Cleanup turnout mapping
            if (trackIdToTurnoutId.ContainsKey(graphTrackId))
                trackIdToTurnoutId.Remove(graphTrackId);

            PlacedTrackSegment found = null;
            foreach (var track in placedTracks)
            {
                if (track.GraphTrackId == graphTrackId)
                {
                    found = track;
                    break;
                }
            }

            if (found != null)
            {
                // Powiadom system sieci trakcyjnej PRZED usunięciem z grafu
                var catenaryGen = DepotServices.Get<CatenaryGenerator>();
                if (catenaryGen != null)
                    catenaryGen.OnTrackRemoved(graphTrackId);

                if (trackGraph != null)
                    trackGraph.RemoveTrack(found.GraphTrackId);

                if (found.TrackObject != null)
                    Destroy(found.TrackObject);

                placedTracks.Remove(found);

                // M-Economy Faza 5 + TD-035: zwrot kosztu toru — KAŻDEGO (plain i member rozjazdu).
                // Member tracks idą tędy wyłącznie z RemoveTurnout (direct RemoveTrack na memberze
                // przekierowuje na RemoveTurnout) → brak podwójnego zwrotu. Razem z charge'owanym
                // restore daje lustro place'a: cykl place→remove = net-zero. Suppress-aware (load).
                ConstructionBilling.Refund(ConstructionCosts.TrackGroszy(found.Length), "construction_track_refund", "tor");

                Log.Info($"[PrefabTrackBuilder] Removed track graphId={graphTrackId}");

                // Nagraj undo — tylko dla NIE-turnout tracków (turnout ma własny command)
                if (savedPolyline != null && savedPolyline.Count >= 2)
                {
                    DepotSystem.Undo.UndoManager.Record(
                        DepotSystem.Undo.UndoCategory.Tory,
                        new DepotSystem.Undo.TrackRemovedCommand(savedPolyline, savedName, savedType));
                }
            }
        }

        /// <summary>
        /// Usuwa cały rozjazd — wszystkie segmenty należące do TurnoutEntity.
        /// Dla rozjazdu na straight chain: odtwarza oryginalny prosty tor.
        /// </summary>
        public void RemoveTurnout(int turnoutId)
        {
            if (!turnoutEntities.TryGetValue(turnoutId, out var entity)) return;

            // M-Economy Faza 5 + TD-035: zwrot kosztu MECHANIZMU. Geometria = pełne lustro place'a:
            // members + pre/post refundowane w RemoveTrackInternal, restore oryginału CHARGED →
            // remove zwraca netto mechanizm + T(odnóg) = dokładna odwrotność place. Suppress-aware (load).
            ConstructionBilling.Refund(ConstructionCosts.TurnoutGroszy(entity.DefinitionName), "construction_turnout_refund", entity.DefinitionName);

            // Zachowaj dane oryginalnego toru przed usunięciem
            var originalPoly = entity.OriginalPolyline;
            string originalName = entity.OriginalTrackName;
            var originalType = entity.OriginalTrackType;

            // Zachowaj dane do undo
            var undoDef = entity.Definition;
            bool undoDivergeLeft = entity.DivergeLeft;
            bool undoFlip = entity.FlipDirection;
            float undoDist = entity.DistAlongChain;

            // Czy rozjazd został postawiony na prawdziwym torze (straight chain) czy na synthetic chain?
            // Synthetic chain = postawienie na końcu innego toru (np. krzywego) — brak pre/post segmentów
            float originalLength = originalPoly != null ? TrackGeometry.CalculatePolylineLength(originalPoly) : 0f;
            bool isRealTrack = originalLength >= 1f;

            // Silence undo podczas wewnętrznych operacji — zapiszemy JEDEN TurnoutRemovedCommand na końcu
            DepotSystem.Undo.UndoManager.Silenced = true;
            try
            {
                // Usuń member tracks (body + diverging)
                var trackIds = new List<int>(entity.MemberTrackIds);
                foreach (int trackId in trackIds)
                    RemoveTrackInternal(trackId);

                // Usuń pre/post segmenty TYLKO dla straight chain (synthetic = nie ma pre/post)
                if (isRealTrack)
                {
                    RemovePrePostSegments(entity);

                    // Odtwórz oryginalny prosty tor
                    if (originalPoly != null && originalPoly.Count >= 2)
                    {
                        // TD-035: restore CHARGED (lustro place'a, gdzie usunięcie chain dało refund).
                        // Refundy mech+members+pre/post wylądowały wyżej w tej samej operacji → stać.
                        // Wcześniejszy suppress + brak refundu members dawał drukarkę pieniędzy
                        // (+T(pre+post) − T(odnóg) za cykl place→remove).
                        PlaceTrackWithPolyline(originalPoly, originalName ?? "Tor", originalType);
                        Log.Info($"[PrefabTrackBuilder] Restored original track after turnout removal");
                    }
                }

                // Wyczyść entity
                turnoutEntities.Remove(turnoutId);
            }
            finally
            {
                DepotSystem.Undo.UndoManager.Silenced = false;
            }

            // Nagraj JEDEN command — cofnięcie odtworzy tor + postawi rozjazd
            // Synthetic chain case: pomijamy nagrywanie (rozjazd był postawiony na końcu innego toru,
            // jego usunięcie wystarczy, nie ma nic do odtworzenia)
            float polyLen = originalPoly != null ? TrackGeometry.CalculatePolylineLength(originalPoly) : 0f;
            if (originalPoly != null && originalPoly.Count >= 2 && polyLen >= 1f)
            {
                DepotSystem.Undo.UndoManager.Record(
                    DepotSystem.Undo.UndoCategory.Tory,
                    new DepotSystem.Undo.TurnoutRemovedCommand(
                        originalPoly, originalName, originalType,
                        undoDef, undoDivergeLeft, undoFlip, undoDist));
            }
        }

        /// <summary>
        /// Usuwa pre i post segmenty rozjazdu (tory przylegające do Origin i FarEnd)
        /// </summary>
        private void RemovePrePostSegments(TurnoutEntity entity)
        {
            if (trackGraph == null) return;

            Vector3 origin = entity.Origin;
            Vector3 farEnd = origin + entity.Direction * entity.Definition.Length;

            // Znajdź tory dotykające Origin i FarEnd
            var tracksToRemove = new HashSet<int>();

            foreach (var seg in new List<PlacedTrackSegment>(placedTracks))
            {
                if (seg.GraphTrackId < 0) continue;
                if (entity.MemberTrackIds.Contains(seg.GraphTrackId)) continue; // skip body/diverging

                var poly = trackGraph.GetTrackPolyline(seg.GraphTrackId);
                if (poly == null || poly.Count < 2) continue;

                Vector3 start = poly[0];
                Vector3 end = poly[poly.Count - 1];

                // Tor dotyka Origin lub FarEnd?
                bool touchesOrigin = Vector3.Distance(start, origin) < 0.5f || Vector3.Distance(end, origin) < 0.5f;
                bool touchesFarEnd = Vector3.Distance(start, farEnd) < 0.5f || Vector3.Distance(end, farEnd) < 0.5f;

                if (touchesOrigin || touchesFarEnd)
                    tracksToRemove.Add(seg.GraphTrackId);
            }

            foreach (int trackId in tracksToRemove)
                RemoveTrackInternal(trackId);
        }

        // TD-035: skasowano dead-code TryMergeTracksAtPosition (nigdy nie wołane) — restore
        // OriginalPolyline w RemoveTurnout robi tę robotę (odtwarza oryginalny prosty tor).

        /// <summary>
        /// Usuwa tor najbliższy podanej pozycji (max 5m)
        /// </summary>
        public void RemoveNearestTrack(Vector3 position)
        {
            float minDist = 5f;
            PlacedTrackSegment nearest = null;

            foreach (var track in placedTracks)
            {
                if (track.TrackObject == null) continue;
                float dist = Vector3.Distance(position, (track.StartPosition + track.EndPosition) / 2f);
                if (dist < minDist)
                {
                    minDist = dist;
                    nearest = track;
                }
            }

            if (nearest != null)
            {
                RemoveTrack(nearest.GraphTrackId);
            }
        }
    }
}
