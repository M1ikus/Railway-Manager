using System.Collections.Generic;
using UnityEngine;
using RailwayManager.Core;
using DepotSystem.Undo;

namespace DepotSystem.Schemas.Placement
{
    /// <summary>
    /// Partial: public placement API (Start/Cancel/Confirm) + PHASE 1 (PlaceTrackWithPolyline)
    /// + PHASE 2 (PlaceTurnoutOnChain) + chain trim helpers.
    /// </summary>
    public partial class TurnoutSchemaPlacer
    {
        // ════════════════════════════════════════
        //  PUBLIC API
        // ════════════════════════════════════════

        /// <summary>
        /// Aktywuje placement mode dla podanego schematu. Generuje geometrię, tworzy preview,
        /// rozpoczyna Update tick.
        /// </summary>
        public void StartPlacement(TurnoutSchemaDefinition def)
        {
            if (def == null)
            {
                Log.Error("[TurnoutSchemaPlacer] StartPlacement: definition is null");
                return;
            }

            CleanupPreview();

            _currentSchema = def;
            _currentSchemaName = def.name;
            _currentRotationDeg = 0f;
            _currentMirror = def.parameters?.mirror ?? false;
            _hasSnap = false;
            _snappedEndpointCount = 0;
            _snapTranslation = Vector3.zero;
            _autoRotationApplied = false;
            _hasAdaptiveProposal = false;
            _proposedSpacingMeters = 0f;
            _lastSnapResult = null;
            _adaptiveCheckCounter = 0;

            RegenerateGeometry();
            CreatePreviewRenderer();

            _isActive = true;
            Log.Info($"[TurnoutSchemaPlacer] Started placement: '{def.name}' (id={def.id}, category={def.category}, type={def.type})");
        }

        /// <summary>
        /// Anuluje placement (Esc lub external call). Czyści preview.
        /// </summary>
        public void CancelPlacement()
        {
            if (!_isActive) return;
            Log.Info($"[TurnoutSchemaPlacer] Cancelled placement: '{_currentSchemaName}'");
            CleanupPreview();
            _isActive = false;
            _currentSchema = null;
            _currentSchemaName = null;
        }

        /// <summary>
        /// Zatwierdza placement — replay geometrii w global coords.
        ///
        /// PHASE 1: PlaceTrackWithPolyline per każdy SchemaTrackEntry (= łuk start, skos,
        /// łuk powrotny, prosta postojowa) → tworzy PlacedTrackSegment.
        /// PHASE 2: Per SchemaTurnoutEntry, FindStraightChain + PlaceTurnoutOnChain →
        /// tworzy real TurnoutEntity z body split + switchable iglice.
        ///
        /// Po confirm: refresh snap pointów. Tool zostaje aktywny (gracz może postawić
        /// kolejny w innym miejscu lub Esc cancel).
        /// </summary>
        public void ConfirmPlacement()
        {
            if (!_isActive || _currentGeometry == null)
            {
                Log.Warn("[TurnoutSchemaPlacer] ConfirmPlacement: no active placement");
                return;
            }

            Vector3 placementPos = GetPreviewWorldPosition();

            // TD-010: silence per-element undo (TrackPlacedCommand/TurnoutPlacedCommand) — całość
            // schematu nagrywamy jako JEDEN atomowy SchemaPlacementCommand (jeden Ctrl+Z cofa wszystko).
            var placedTrackIds = new List<int>();
            var placedTurnoutIds = new List<int>();
            int placedTracks;
            bool prevSilenced = UndoManager.Silenced;
            UndoManager.Silenced = true;
            try
            {
                placedTracks = PlaceSchemaInWorld(placedTrackIds, placedTurnoutIds);
            }
            finally
            {
                UndoManager.Silenced = prevSilenced;
            }

            // Nie nagrywaj gdy caller już silenced (load/replay) lub nic nie postawiono.
            if (!prevSilenced && (placedTrackIds.Count > 0 || placedTurnoutIds.Count > 0))
            {
                UndoManager.Record(UndoCategory.Tory,
                    new SchemaPlacementCommand(placedTrackIds, placedTurnoutIds));
            }

            Log.Info($"[TurnoutSchemaPlacer] CONFIRM placed schema '{_currentSchemaName}' at {placementPos} (rotation={_currentRotationDeg:F1}°, mirror={_currentMirror}, snap={_hasSnap}): {placedTracks} tracks placed, atomic undo ({placedTrackIds.Count} tory + {placedTurnoutIds.Count} rozjazdy)");

            // Refresh snap pointów żeby kolejne placement działały na nowo postawionych torach
            if (_snapPointSystem != null) _snapPointSystem.RefreshAllSnapPoints();

            // Tool zostaje aktywny — gracz może postawić kolejny w innym miejscu (Esc anuluje).
        }

        // ════════════════════════════════════════
        //  PHASE 1 + PHASE 2 placement
        // ════════════════════════════════════════

        /// <summary>
        /// MD-9 + real TurnoutEntity creation — replay geometrii schematu w global coords.
        ///
        /// PHASE 1: Place tracks przez <see cref="PrefabTrackBuilder.PlaceTrackWithPolyline"/>
        /// PHASE 2: Place turnouts przez <see cref="TurnoutPlacer.PlaceTurnoutOnChain"/>
        /// (chain detection przez FindStraightChain, distAlongChain przez projection na chain.Direction)
        ///
        /// Zwraca liczbę pomyślnie postawionych torów (turnouts liczone w log).
        ///
        /// TD-010: <paramref name="outTrackIds"/> zbiera GraphTrackId WSZYSTKICH torów schematu
        /// (standalone + members rozjazdów = finalne mySchemaSegments), <paramref name="outTurnoutIds"/>
        /// id postawionych rozjazdów — do atomowego undo (SchemaPlacementCommand).
        /// </summary>
        private int PlaceSchemaInWorld(List<int> outTrackIds, List<int> outTurnoutIds)
        {
            if (_trackBuilder == null) _trackBuilder = DepotServices.Get<PrefabTrackBuilder>();
            if (_trackBuilder == null)
            {
                Log.Error("[TurnoutSchemaPlacer] PlaceSchemaInWorld: no PrefabTrackBuilder in scene");
                return 0;
            }
            if (_currentGeometry == null) return 0;

            // Transform local → global. Anchor convention DYNAMIC (= snap endpoint when snap
            // active, else wjazd). Musi MATCHOWAĆ UpdatePreviewTransform anchor żeby placement
            // visual ↔ actual placement były spójne.
            Vector3 worldPos = GetPreviewWorldPosition();
            Quaternion rot = Quaternion.Euler(0, _currentRotationDeg, 0);
            int anchorIdx = _hasSnap && _lastSnapResult != null
                && _lastSnapResult.anchorEndpointIdx >= 0
                && _lastSnapResult.anchorEndpointIdx < _currentGeometry.endpoints.Count
                ? _lastSnapResult.anchorEndpointIdx : 0;
            Vector3 anchorLocal = _currentGeometry.endpoints != null && _currentGeometry.endpoints.Count > 0
                ? _currentGeometry.endpoints[anchorIdx]
                : _currentGeometry.centroid;
            Vector3 originOffset = worldPos - rot * anchorLocal;

            // === PHASE 1: Place tracks ===
            // Tory z placeAsTrack=false są pomijane — odpowiednie geometrie powstaną w PHASE 2
            // przez TurnoutPlacer.PlaceTurnoutOnChain (diverging leg generowany automatycznie).
            // Zapobiega duplikatom (= "rozerwane" tory: 2 segmenty w tym samym miejscu).
            //
            // mySchemaSegments — set torów postawionych PRZEZ TEN schemat. Używany w PHASE 2
            // do ograniczenia chain detection — bez tego rozjazd schematu 2 mógłby usunąć
            // body rozjazdu schematu 1 (= "znikający rozjazd"), gdy schemat 2 jest snap'nięty
            // do końca schematu 1 i ich tory są kolinearne (wspólny 2-edge node).
            int placedTracks = 0;
            int skippedTracks = 0;
            var mySchemaSegments = new HashSet<PlacedTrackSegment>();
            foreach (var trackEntry in _currentGeometry.tracks)
            {
                if (trackEntry.polyline == null || trackEntry.polyline.Count < 2) continue;
                if (!trackEntry.placeAsTrack)
                {
                    skippedTracks++;
                    continue;  // tor zostanie utworzony przez TurnoutPlacer w PHASE 2
                }

                var worldPolyline = new List<Vector3>(trackEntry.polyline.Count);
                for (int i = 0; i < trackEntry.polyline.Count; i++)
                    worldPolyline.Add(originOffset + rot * trackEntry.polyline[i]);

                var trackType = trackEntry.ParseTrackType();
                string trackName = string.IsNullOrEmpty(trackEntry.name) ? "Schema track" : trackEntry.name;

                var result = _trackBuilder.PlaceTrackWithPolyline(worldPolyline, trackName, trackType);
                if (result != null)
                {
                    placedTracks++;
                    mySchemaSegments.Add(result);
                }
            }

            // === PHASE 2: Place turnouts ===
            int placedTurnouts = PlaceTurnoutsInWorld(originOffset, rot, mySchemaSegments, outTurnoutIds);

            // TD-010: zbierz finalne id torów schematu (standalone + members rozjazdów) do atomowego undo.
            if (outTrackIds != null)
            {
                foreach (var seg in mySchemaSegments)
                    if (seg != null && seg.GraphTrackId >= 0)
                        outTrackIds.Add(seg.GraphTrackId);
            }

            Log.Info($"[TurnoutSchemaPlacer] Placement summary: {placedTracks}/{_currentGeometry.tracks.Count} tracks (skipped {skippedTracks} z placeAsTrack=false) + {placedTurnouts}/{_currentGeometry.turnouts.Count} turnouts");

            return placedTracks;
        }

        /// <summary>
        /// PHASE 2 — placement TurnoutEntity per rozjazd w geometrii. Per rozjazd:
        /// 1. Compute world position + direction
        /// 2. Find PlacedTrackSegment zawierający tę pozycję (LIMITED do mySchemaSegments —
        ///    żeby nie używać foreign segmentów schematu obok)
        /// 3. FindStraightChain(seg) → fullChain (może obejmować foreign segments gdy schemat
        ///    jest snap'nięty kolinearnie do innego)
        /// 4. TrimChainToSet(fullChain, mySchemaSegments) → localChain (tylko nasze)
        /// 5. distAlongChain = projection (turnoutPos - localChain.StartPos) na localChain.Direction
        /// 6. flipDirection = czy direction rozjazdu jest przeciwny do localChain.Direction
        /// 7. divergeLeft adjustment (jeśli flipDirection, lewo↔prawo zamienione)
        /// 8. Wywołaj TurnoutPlacer.PlaceTurnoutOnChain(localChain, ...) — usuwa TYLKO nasze
        ///    segmenty z localChain.Segments + tworzy nowe pre/body/post/diverging
        /// 9. Update mySchemaSegments: usuń skasowane (= localChain.Segments) + dodaj nowe
        ///    (diff PlacedTracks before/after)
        ///
        /// Krytyczne: bez TRIM body rozjazdu schematu 1 (= turnout member) byłoby usuwane
        /// przy placement R1 schematu 2 (chain rozszerza się przez 2-edge node na granicy snap'u
        /// = end of MainTrack1 + start of MainTrack2).
        ///
        /// Kolejność rozjazdów ma znaczenie — każdy PlaceTurnoutOnChain modyfikuje chain,
        /// kolejny rozjazd musi re-find chain dla swojej pozycji.
        /// </summary>
        private int PlaceTurnoutsInWorld(Vector3 originOffset, Quaternion rot, HashSet<PlacedTrackSegment> mySchemaSegments, List<int> outTurnoutIds)
        {
            if (_currentGeometry.turnouts == null || _currentGeometry.turnouts.Count == 0) return 0;

            var turnoutPlacer = DepotServices.Get<TurnoutPlacer>();
            if (turnoutPlacer == null)
            {
                Log.Warn("[TurnoutSchemaPlacer] PlaceTurnoutsInWorld: no TurnoutPlacer in scene — skipping TurnoutEntity creation");
                return 0;
            }

            int placed = 0;
            foreach (var turnoutEntry in _currentGeometry.turnouts)
            {
                var defOpt = turnoutEntry.ResolveDefinition();
                if (!defOpt.HasValue)
                {
                    Log.Warn($"[TurnoutSchemaPlacer] Unknown turnout type '{turnoutEntry.turnoutTypeName}'");
                    continue;
                }
                var def = defOpt.Value;

                // World position + direction rozjazdu
                Vector3 turnoutWorldPos = originOffset + rot * turnoutEntry.origin;
                Vector3 turnoutWorldDir = (rot * turnoutEntry.direction).normalized;

                // Find segment zawierający turnout position — TYLKO w naszych torach.
                // Bez tego mogłoby trafić na tor sąsiada (gdy schematy się dotykają).
                PlacedTrackSegment containingSeg = FindSegmentAtPositionInSet(turnoutWorldPos, maxPerpDist: 1.0f, allowedSet: mySchemaSegments);
                if (containingSeg == null)
                {
                    Log.Warn($"[TurnoutSchemaPlacer] {turnoutEntry.name}: no segment at world pos {turnoutWorldPos} (w naszym set, size={mySchemaSegments.Count})");
                    continue;
                }

                // Find FULL chain (może obejmować foreign segments schematu obok)
                var fullChain = turnoutPlacer.FindStraightChain(containingSeg);
                if (fullChain == null)
                {
                    Log.Warn($"[TurnoutSchemaPlacer] {turnoutEntry.name}: FindStraightChain returned null");
                    continue;
                }

                // TRIM chain — wytnij tylko contiguous sub-chain w naszym schemacie.
                // Krytyczne: zapobiega usuwaniu body rozjazdów innych schematów.
                StraightChain chain = TrimChainToSet(fullChain, containingSeg, mySchemaSegments);
                if (chain == null || chain.Segments.Count == 0)
                {
                    Log.Warn($"[TurnoutSchemaPlacer] {turnoutEntry.name}: TrimChainToSet zwróciło null (containingSeg poza set?)");
                    continue;
                }
                if (chain.Segments.Count != fullChain.Segments.Count)
                {
                    Log.Info($"[TurnoutSchemaPlacer] {turnoutEntry.name}: chain trimmed {fullChain.Segments.Count}→{chain.Segments.Count} segmentów (foreign segmenty wykluczone)");
                }

                // Compute distAlongChain — projection od LOCAL chain.StartPos na LOCAL chain.Direction
                Vector3 toTurnout = turnoutWorldPos - chain.StartPos;
                float distAlongChain = Vector3.Dot(toTurnout, chain.Direction);
                if (distAlongChain < -0.5f || distAlongChain > chain.TotalLength + 0.5f)
                {
                    Log.Warn($"[TurnoutSchemaPlacer] {turnoutEntry.name}: distAlongChain {distAlongChain:F2}m out of range [0, {chain.TotalLength:F2}]");
                    continue;
                }
                distAlongChain = Mathf.Clamp(distAlongChain, 0f, chain.TotalLength);

                // flipDirection — używamy WPROST z turnoutEntry (generator wie lepiej).
                // Wcześniej auto-compute z Dot(direction, chain.Direction) ignorował explicit
                // flip set przez generator (np. Scissors R3/R4 z body w -X kierunku). Auto
                // zwracał false → forward placement → CanPlaceTurnoutOnChain failed na remaining
                // < def.Length → R3/R4 nie były placed.
                //
                // Sanity check: jeśli direction po rotacji NIE pasuje do chain.Direction, dodatkowy
                // flip "for chain orientation" jest zwrócony przez computedFlipForChain. Final flip
                // = turnoutEntry.flipDirection XOR computedFlipForChain. Dla schematów bez rotacji
                // (= większość) computedFlipForChain=false i final = turnoutEntry.flipDirection.
                bool computedFlipForChain = Vector3.Dot(turnoutWorldDir, chain.Direction) < 0f;
                bool flipDirection = turnoutEntry.flipDirection ^ computedFlipForChain;

                // divergeLeft — używamy WPROST. Generator wie którą stronę chce odgałęzienie.
                // Adjustment dla rotacji jest niepotrzebny — divergeLeft jest semantyczny względem
                // body direction (po flip), nie względem chain.Direction.
                bool divergeLeft = turnoutEntry.divergeLeft;

                // Snapshot PlacedTracks PRZED placement — żeby diff'em znaleźć nowo utworzone
                var beforeTracks = new HashSet<PlacedTrackSegment>(_trackBuilder.PlacedTracks);

                Log.Info($"[TurnoutSchemaPlacer] >>> Attempting placement {turnoutEntry.name}: chain.TotalLength={chain.TotalLength:F2}m, chain.Start={chain.StartPos}, chain.Direction={chain.Direction}, distAlongChain={distAlongChain:F2}m, def={def.Name} (length={def.Length:F2}m), divergeLeft={divergeLeft}, flip={flipDirection}");

                try
                {
                    // UWAGA: PlaceTurnoutOnChain ZWRACA id toru ODGAŁĘZIAJĄCEGO (divergingTrackId),
                    // NIE id encji rozjazdu (turnoutEntity.TurnoutId).
                    int divergingTrackId = turnoutPlacer.PlaceTurnoutOnChain(chain, distAlongChain, def, divergeLeft, flipDirection);
                    if (divergingTrackId >= 0)
                    {
                        placed++;
                        // TD-010: atomowy undo woła RemoveTurnout(entityId) — rozwiąż encję rozjazdu
                        // przez członkostwo toru odgałęziającego (return value to NIE jest entityId).
                        if (outTurnoutIds != null && _trackBuilder.TryGetTurnoutForTrack(divergingTrackId, out var placedEntity))
                            outTurnoutIds.Add(placedEntity.TurnoutId);
                        int newSegmentsCount = 0;
                        foreach (var seg in _trackBuilder.PlacedTracks)
                        {
                            if (!beforeTracks.Contains(seg)) newSegmentsCount++;
                        }
                        Log.Info($"[TurnoutSchemaPlacer] ✓ Placed {turnoutEntry.name} (divergingTrackId={divergingTrackId}, +{newSegmentsCount} new track segments)");

                        // Update mySchemaSegments: usuń skasowane (= chain.Segments) i dodaj nowo utworzone
                        foreach (var seg in chain.Segments) mySchemaSegments.Remove(seg);
                        foreach (var seg in _trackBuilder.PlacedTracks)
                        {
                            if (!beforeTracks.Contains(seg)) mySchemaSegments.Add(seg);
                        }
                    }
                    else
                    {
                        Log.Warn($"[TurnoutSchemaPlacer] ✗ {turnoutEntry.name}: PlaceTurnoutOnChain returned -1 (validation failed). chain.TotalLength={chain.TotalLength:F2}m, distAlongChain={distAlongChain:F2}m, def.Length={def.Length:F2}m, remaining={chain.TotalLength - distAlongChain:F2}m, flip={flipDirection}");
                    }
                }
                catch (System.Exception e)
                {
                    Log.Error($"[TurnoutSchemaPlacer] ✗ {turnoutEntry.name}: PlaceTurnoutOnChain exception: {e.Message}\n{e.StackTrace}");
                }
            }

            return placed;
        }

        // ════════════════════════════════════════
        //  Chain trim helpers
        // ════════════════════════════════════════

        /// <summary>
        /// Trim'uje chain do contiguous sub-chain (zawierającego containingSeg) złożonego
        /// WYŁĄCZNIE z segmentów w allowedSet I segmentów które NIE są turnout members.
        /// Zwraca null gdy containingSeg poza set'em.
        ///
        /// Krytyczne wykluczenie turnout members — Body/Diverging istniejących rozjazdów są
        /// STRAIGHT i kolinearne z resztą chain, więc bez wykluczenia FindStraightChain rozszerza
        /// chain przez Body wcześniejszego rozjazdu, a PlaceTurnoutOnChain go usuwa →
        /// pierwszy rozjazd traci body i znika. To problem dla wachlarza Throat gdzie sąsiednie
        /// rozjazdy są na tym samym torze przewodnim.
        ///
        /// Nowy StraightChain ma własną MergedPolyline (2-punktową prostą od subStart do subEnd,
        /// co działa dla straight chains — TrackGeometry.GetPointAtDistance interpoluje liniowo).
        /// </summary>
        private StraightChain TrimChainToSet(StraightChain fullChain, PlacedTrackSegment containingSeg, HashSet<PlacedTrackSegment> allowedSet)
        {
            if (fullChain == null || fullChain.Segments == null || fullChain.Segments.Count == 0) return null;
            if (!allowedSet.Contains(containingSeg)) return null;
            if (IsTurnoutMember(containingSeg)) return null;  // containingSeg sam jest body innego rozjazdu — nie wolno

            int idx = fullChain.Segments.IndexOf(containingSeg);
            if (idx < 0) return null;

            // Find min/max indices forming contiguous allowed sub-chain.
            // "Allowed" = w naszym schema set + NIE turnout member innego rozjazdu.
            int minIdx = idx, maxIdx = idx;
            while (minIdx > 0 && IsChainSegmentAllowed(fullChain.Segments[minIdx - 1], allowedSet)) minIdx--;
            while (maxIdx < fullChain.Segments.Count - 1 && IsChainSegmentAllowed(fullChain.Segments[maxIdx + 1], allowedSet)) maxIdx++;

            // No trimming → zwróć oryginał (perf optimization)
            if (minIdx == 0 && maxIdx == fullChain.Segments.Count - 1) return fullChain;

            var subSegments = fullChain.Segments.GetRange(minIdx, maxIdx - minIdx + 1);

            // Compute subStart/subEnd zgodnie z fullChain.Direction.
            // Każdy segment jest STRAIGHT i kolinearny z chain.Direction (= fullChain.Direction).
            // Endpoint "back" pierwszego segmentu = subStart; "front" ostatniego = subEnd.
            var first = subSegments[0];
            bool firstReversed = Vector3.Dot(first.EndPosition - first.StartPosition, fullChain.Direction) < 0;
            Vector3 subStart = firstReversed ? first.EndPosition : first.StartPosition;

            var last = subSegments[subSegments.Count - 1];
            bool lastReversed = Vector3.Dot(last.EndPosition - last.StartPosition, fullChain.Direction) < 0;
            Vector3 subEnd = lastReversed ? last.StartPosition : last.EndPosition;

            var mergedPolyline = new List<Vector3> { subStart, subEnd };
            return new StraightChain
            {
                Segments = subSegments,
                MergedPolyline = mergedPolyline,
                TotalLength = Vector3.Distance(subStart, subEnd),
                StartPos = subStart,
                EndPos = subEnd,
                Direction = (subEnd - subStart).normalized
            };
        }

        /// <summary>
        /// Sprawdza czy segment należy do jakiegokolwiek istniejącego rozjazdu (= MemberTrackId
        /// w TurnoutEntity). Body i Diverging są zarejestrowane jako members rozjazdu który je
        /// stworzył. Pre i Post (= zwykłe straight segments toru przewodniego) nie są.
        ///
        /// W TrimChainToSet wykluczamy turnout members — bez tego drugi rozjazd na torze
        /// przewodnim usuwałby Body pierwszego rozjazdu (= znikał wizualnie).
        /// </summary>
        private bool IsTurnoutMember(PlacedTrackSegment seg)
        {
            if (seg == null || seg.GraphTrackId < 0) return false;
            if (_trackBuilder == null) return false;
            return _trackBuilder.TryGetTurnoutForTrack(seg.GraphTrackId, out _);
        }

        private bool IsChainSegmentAllowed(PlacedTrackSegment seg, HashSet<PlacedTrackSegment> allowedSet)
        {
            return allowedSet.Contains(seg) && !IsTurnoutMember(seg);
        }

        /// <summary>
        /// Wariant <see cref="FindSegmentAtPosition"/> ograniczony do <paramref name="allowedSet"/>.
        /// Używany w PHASE 2 żeby nie trafić na tor sąsiedniego schematu (gdy są snap'nięte).
        /// </summary>
        private PlacedTrackSegment FindSegmentAtPositionInSet(Vector3 worldPos, float maxPerpDist, HashSet<PlacedTrackSegment> allowedSet)
        {
            PlacedTrackSegment best = null;
            float bestDist = maxPerpDist;

            foreach (var seg in allowedSet)
            {
                if (seg == null || seg.Polyline == null || seg.Polyline.Count < 2) continue;
                if (!TrackGeometry.IsStraightPolyline(seg.Polyline)) continue;

                Vector3 segStart = seg.StartPosition;
                Vector3 segEnd = seg.EndPosition;
                Vector3 segDir = (segEnd - segStart).normalized;
                float segLength = Vector3.Distance(segStart, segEnd);

                Vector3 toPoint = worldPos - segStart;
                float t = Vector3.Dot(toPoint, segDir);
                if (t < -0.5f || t > segLength + 0.5f) continue;
                t = Mathf.Clamp(t, 0f, segLength);

                Vector3 closestPoint = segStart + segDir * t;
                float perpDist = Vector3.Distance(closestPoint, worldPos);
                if (perpDist < bestDist)
                {
                    bestDist = perpDist;
                    best = seg;
                }
            }

            return best;
        }

        /// <summary>
        /// Znajduje PlacedTrackSegment którego polyline zawiera podaną world position
        /// (perp dist < maxPerpDist, segment musi być prosty).
        /// </summary>
        private PlacedTrackSegment FindSegmentAtPosition(Vector3 worldPos, float maxPerpDist)
        {
            PlacedTrackSegment best = null;
            float bestDist = maxPerpDist;

            foreach (var seg in _trackBuilder.PlacedTracks)
            {
                if (seg == null || seg.Polyline == null || seg.Polyline.Count < 2) continue;
                if (!TrackGeometry.IsStraightPolyline(seg.Polyline)) continue;

                Vector3 segStart = seg.StartPosition;
                Vector3 segEnd = seg.EndPosition;
                Vector3 segDir = (segEnd - segStart).normalized;
                float segLength = Vector3.Distance(segStart, segEnd);

                // Project worldPos na segment line
                Vector3 toPoint = worldPos - segStart;
                float t = Vector3.Dot(toPoint, segDir);
                if (t < -0.5f || t > segLength + 0.5f) continue;
                t = Mathf.Clamp(t, 0f, segLength);

                Vector3 closestPoint = segStart + segDir * t;
                float perpDist = Vector3.Distance(closestPoint, worldPos);
                if (perpDist < bestDist)
                {
                    bestDist = perpDist;
                    best = seg;
                }
            }

            return best;
        }
    }
}
