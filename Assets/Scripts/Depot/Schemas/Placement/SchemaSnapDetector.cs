using System.Collections.Generic;
using UnityEngine;
using DepotSystem.Schemas.Generators;
using RailwayManager.Core;

namespace DepotSystem.Schemas.Placement
{
    /// <summary>
    /// Wynik detekcji snap'u dla schematu (multi-endpoint).
    /// </summary>
    public class SnapResult
    {
        /// <summary>True jeśli przynajmniej jeden endpoint schematu snap'uje się.</summary>
        public bool hasAnySnap;

        /// <summary>
        /// Translacja (w world coords) do apply na schemat — przesuwa centroid tak, żeby
        /// "anchor endpoint" pokrywał istniejący endpoint toru. Vector3.zero gdy brak snap.
        /// </summary>
        public Vector3 translation;

        /// <summary>Liczba endpointów schematu które są w tolerance po apply translation.</summary>
        public int snappedEndpointCount;

        /// <summary>
        /// Per endpoint: indeks w SchemaGeometry.endpoints + target world position
        /// (po apply translation). Empty gdy brak snap.
        /// </summary>
        public List<EndpointSnapInfo> snappedEndpoints = new List<EndpointSnapInfo>();

        /// <summary>
        /// Auto-rotation correction (deg). Jeśli != null, gracz prawie idealnie ustawił rotację
        /// (±5° od współliniowości) — system "dociąga" do dokładnej współliniowości najbliższego toru.
        /// Wartość = NOWA total rotation (nie delta).
        /// </summary>
        public float? proposedRotationDeg;

        /// <summary>
        /// A13 candidate — generative schemat ma niedopasowane międzytorze, ale zmiana
        /// <see cref="SchemaParameters.trackSpacing"/> dałaby lepszy multi-snap.
        /// </summary>
        public bool hasAdaptivePromptCandidate;

        /// <summary>Proponowana wartość spacing (jeśli hasAdaptivePromptCandidate).</summary>
        public float proposedSpacingMeters;

        /// <summary>Liczba endpointów snap'ujących się przy proposed spacing (informacyjne).</summary>
        public int proposedSnappedCount;

        /// <summary>
        /// TrackGraph node ID anchor endpoint'u — używane jako sticky reference w next frame.
        /// -1 gdy brak snap.
        /// </summary>
        public int anchorNodeId = -1;

        /// <summary>
        /// Indeks endpointu schematu który był wybrany jako "snap anchor" (= ten który pokrywa
        /// istniejący node). 0 = wjazd (default), inne wartości = parking end / koniec toru
        /// przewodniego (snap "z drugiej strony").
        ///
        /// Używane do auto-rotation i diagnostic. Cursor convention pozostaje endpoint[0]=wjazd
        /// (= cursor pozycja schematu w przestrzeni); ten field tylko mówi który endpoint jest
        /// magnetycznie przyciągnięty do toru.
        /// </summary>
        public int anchorEndpointIdx = -1;
    }

    /// <summary>
    /// Info per endpoint schematu — czy się snap'uje + gdzie + jaki kierunek miał istniejący tor.
    /// </summary>
    public struct EndpointSnapInfo
    {
        public int schemaEndpointIndex;
        public Vector3 schemaEndpointWorld;     // pozycja po apply translation
        public Vector3 targetWorld;              // istniejący endpoint toru
        public float distance;                   // dystans po apply translation
        public int trackGraphNodeId;             // referencja do TrackGraph node
        public Vector3 trackDirectionAtNode;     // kierunek istniejącego toru w tym node
    }

    /// <summary>
    /// MD-4 — detektor multi-endpoint snap dla schematów.
    ///
    /// Algorytm:
    /// 1. Dla każdego endpointu schematu w world coords (cursor + rotation), znajdź najbliższy
    ///    istniejący endpoint w SnapPointSystem w tolerance promieniu
    /// 2. Wybierz "anchor" — endpoint schematu z najbliższym matching'iem (= najmniejsza dist)
    /// 3. Translate schemat tak, żeby anchor pokrywał target
    /// 4. Po translacji: re-evaluate ile innych endpointów też trafiło w tolerance (multi-snap)
    /// 5. Auto-rotation: weź anchor endpoint, sprawdź kierunek istniejącego toru; jeśli różni
    ///    się od kierunku schematu o ≤5°, propose rotation correction
    /// 6. A13 candidate: tylko dla generative — iteruj kilka kandydatów spacing (current ±0.25,
    ///    ±0.5), check czy któryś daje lepszy multi-snap; jeśli tak, propose
    /// </summary>
    public static class SchemaSnapDetector
    {
        /// <summary>Domyślne kandydaty spacing do A13 detection (delty od current).</summary>
        private static readonly float[] AdaptiveSpacingDeltas = new[] {
            -0.5f, -0.4f, -0.3f, -0.25f, -0.2f, -0.1f,
             0.1f,  0.2f,  0.25f,  0.3f,  0.4f,  0.5f
        };

        /// <summary>
        /// Threshold dla auto-rotation (stopnie) — jeśli różnica ≤ ta wartość, dociągamy.
        /// Ustawione na 180° (= maksimum) co oznacza że auto-rotation ZAWSZE wymusza alignment
        /// (= kąt 0° między schematem a istniejącym torem). User explicit request: snap musi
        /// wymuszać "kąt 0 na prosto między torami", niezależnie od bieżącej rotacji schematu.
        ///
        /// Sprawdzane są obie orientacje (= track outDir oraz track outDir+180°), więc
        /// efektywnie threshold 90° wystarcza, ale 180° guarantees coverage edge cases.
        /// </summary>
        public const float AutoRotationThresholdDeg = 180.0f;

        /// <summary>
        /// Główna metoda — detekuje snap, multi-endpoint match + auto-rotation + A13 candidate.
        ///
        /// <paramref name="currentParams"/> używane tylko dla A13 detection (re-generuje kandydatów
        /// spacing). Można podać null jeśli A13 nie jest potrzebne (np. snapshot).
        /// <paramref name="generator"/> j.w. — null jeśli A13 nie potrzebne.
        /// </summary>
        public static SnapResult DetectSnaps(
            SchemaGeometry geometry,
            Vector3 cursorWorldPos,
            float currentRotationDeg,
            float toleranceMeters,
            SnapPointSystem snapPointSystem,
            TrackGraph trackGraph,
            SchemaParameters currentParams = null,
            ITurnoutSchemaGenerator generator = null,
            bool checkAdaptive = false,
            int stickyNodeId = -1,
            float stickyReleaseDistance = 8f,
            int previousAnchorEndpointIdx = -1)
        {
            var result = new SnapResult();

            if (geometry == null || geometry.endpoints == null || geometry.endpoints.Count == 0)
                return result;
            if (snapPointSystem == null)
                return result;

            Quaternion rot = Quaternion.Euler(0, currentRotationDeg, 0);

            // CURSOR convention: endpoint[0] = wjazd schematu = pozycja cursora w przestrzeni
            // (preview transform centruje wjazd na cursor). To NIE oznacza że wjazd musi być
            // snap'nięty — może być inny endpoint magnetycznie przyciągnięty (parking end,
            // koniec toru przewodniego = snap "z drugiej strony").
            const int CursorAnchorIdx = 0;
            Vector3 cursorAnchorLocal = geometry.endpoints[CursorAnchorIdx];

            // Faza 0: Sticky snap — jeśli był snap w poprzedniej klatce, ZACHOWAJ poprzedni
            // anchor endpoint i SPRAWDŹ czy player nadal trzyma cursor blisko snap target.
            //
            // Sticky distance = cursor-to-stickyTarget (= jak daleko player przesunął mysz od
            // snap target), NIE stickyEpWorld (= position of anchor endpoint without translation).
            // Powód: dla parking_end anchor, stickyEpWorld byłby ~mainTrackLength od stickyTarget
            // (= cursor convention puts wjazd at cursor, parking_end FAR via rot*relative). Sticky
            // przy stickyEpWorld zawsze fail dla parking_end → fallback acquire → "odpychanie".
            //
            // Z cursor-to-stickyTarget: as long as player keeps cursor blisko snap target (= 8m),
            // sticky maintained, anchor stable across frames.
            if (stickyNodeId >= 0 && trackGraph != null && trackGraph.Nodes.ContainsKey(stickyNodeId)
                && previousAnchorEndpointIdx >= 0 && previousAnchorEndpointIdx < geometry.endpoints.Count)
            {
                Vector3 stickyTarget = trackGraph.Nodes[stickyNodeId].Position;
                int stickyEpIdx = previousAnchorEndpointIdx;
                Vector3 stickyEpLocal = geometry.endpoints[stickyEpIdx] - cursorAnchorLocal;
                Vector3 stickyEpWorld = cursorWorldPos + rot * stickyEpLocal;
                float cursorToTarget = Vector3.Distance(cursorWorldPos, stickyTarget);

                if (cursorToTarget <= stickyReleaseDistance
                    && IsSchemaOutwardValid(geometry, cursorAnchorLocal, stickyEpIdx, stickyTarget, stickyEpWorld, rot, trackGraph, stickyNodeId))
                {
                    // Translation = SIMPLE delta (= stickyTarget - cursor). Anchor convention:
                    // schemat rotuje wokół stickyEpIdx (snap endpoint), nie wjazd. Snap endpoint
                    // pozostaje at target niezależnie od rotacji.
                    result.translation = stickyTarget - cursorWorldPos;
                    result.hasAnySnap = true;
                    result.anchorNodeId = stickyNodeId;
                    result.anchorEndpointIdx = stickyEpIdx;

                    // Auto-rotation correction — translation NIE re-compute (= constant).
                    Quaternion stickyFinalRot = rot;
                    result.proposedRotationDeg = ComputeAutoRotationCorrection(
                        geometry, stickyEpIdx, currentRotationDeg, trackGraph, stickyNodeId);
                    if (result.proposedRotationDeg.HasValue)
                        stickyFinalRot = Quaternion.Euler(0, result.proposedRotationDeg.Value, 0);

                    PopulateSnappedEndpoints(geometry, cursorWorldPos, result.translation, stickyFinalRot,
                        toleranceMeters, snapPointSystem, trackGraph, result);

                    return result;
                }
                // Sticky released — najbliższy endpoint za daleko od stickyTarget LUB outward
                // validation failed (= schemat by lądował INWARD). Fallback do acquire.
            }

            // Faza 1: Standard acquire — szukaj NAJBLIŻSZEGO match'u dla DOWOLNEGO endpointu
            // schematu (nie tylko wjazdu). Pozwala na snap "z drugiej strony" — gdy gracz prowadzi
            // kursor blisko parking ends istniejących torów, parking ends schematu się przyciągają.
            //
            // PLUS outward validation ("półendpoint") — reject candidates gdzie schemat by lądował
            // INWARD (= overlap z istniejącym torem). Sortujemy candidates by distance, próbujemy
            // każdego, akceptujemy pierwszego który passes outward check.
            var candidates = new List<(int epIdx, int nodeId, Vector3 nodePos, Vector3 epWorld, float dist)>();
            for (int i = 0; i < geometry.endpoints.Count; i++)
            {
                Vector3 epLocal = geometry.endpoints[i] - cursorAnchorLocal;
                Vector3 epWorld = cursorWorldPos + rot * epLocal;

                var (nid, npos) = snapPointSystem.FindNearestSnapPoint(epWorld, toleranceMeters);
                if (nid < 0) continue;

                float dist = Vector3.Distance(epWorld, npos);
                candidates.Add((i, nid, npos, epWorld, dist));
            }
            candidates.Sort((a, b) => a.dist.CompareTo(b.dist));

            int bestEpIdx = -1;
            int bestNodeId = -1;
            Vector3 bestNodePos = Vector3.zero;
            Vector3 bestEpWorld = Vector3.zero;

            foreach (var c in candidates)
            {
                if (!IsSchemaOutwardValid(geometry, cursorAnchorLocal, c.epIdx, c.nodePos, c.epWorld, rot, trackGraph, c.nodeId))
                    continue;

                bestEpIdx = c.epIdx;
                bestNodeId = c.nodeId;
                bestNodePos = c.nodePos;
                bestEpWorld = c.epWorld;
                break;
            }

            if (bestEpIdx < 0)
            {
                // No valid snap — sprawdź A13 candidate (może inny spacing dałby snap)
                if (checkAdaptive && currentParams != null && generator != null)
                {
                    EvaluateAdaptiveCandidate(geometry, cursorWorldPos, currentRotationDeg,
                        toleranceMeters, snapPointSystem, currentParams, generator, result);
                }
                return result;
            }

            // Faza 2: Apply translation żeby snap endpoint pokrywał target.
            // CONVENTION: anchor = snap endpoint (NIE wjazd). Translation jest SIMPLE delta
            // (= bestNodePos - cursorWorldPos), niezależna od rotacji. UpdatePreviewTransform
            // (w TurnoutSchemaPlacer) używa anchorEndpointIdx jako pivot — schemat rotuje wokół
            // snap endpoint, NIE wjazd.
            //
            // Skutek: snap endpoint pozostaje magnetycznie przyklejony do target podczas
            // rotacji. Schemat reorientuje się wokół target. Brak "odpychania".
            result.translation = bestNodePos - cursorWorldPos;
            result.hasAnySnap = true;
            result.anchorNodeId = bestNodeId;
            result.anchorEndpointIdx = bestEpIdx;

            // Faza 4: Auto-rotation correction (z perspektywy WYBRANEGO anchor endpointu).
            // Translation NIE zmienia się od rotacji (= simple delta), więc snap endpoint
            // pozostaje at target niezależnie od rot. ✓
            Quaternion finalRot = rot;
            if (trackGraph != null)
            {
                float? proposed = ComputeAutoRotationCorrection(
                    geometry, bestEpIdx, currentRotationDeg, trackGraph, bestNodeId);
                result.proposedRotationDeg = proposed;

                if (proposed.HasValue)
                {
                    finalRot = Quaternion.Euler(0, proposed.Value, 0);
                }
            }

            // Faza 3: Re-evaluate per endpoint po apply translation + final rotation (= multi-snap
            // detection z final config po auto-rotation).
            PopulateSnappedEndpoints(geometry, cursorWorldPos, result.translation, finalRot,
                toleranceMeters, snapPointSystem, trackGraph, result);

            // Faza 5: A13 candidate (sensowne tylko dla schematów z >=3 endpointami)
            if (checkAdaptive && currentParams != null && generator != null
                && geometry.endpoints.Count >= 3
                && result.snappedEndpointCount < geometry.endpoints.Count)
            {
                EvaluateAdaptiveCandidate(geometry, cursorWorldPos, currentRotationDeg,
                    toleranceMeters, snapPointSystem, currentParams, generator, result);
            }

            return result;
        }

        /// <summary>
        /// Oblicza kierunek INTERIOR schematu przy danym endpoint — direction od endpoint TOWARD
        /// schematu wnętrze. Wyciągany z polyline tangent któregoś toru który ma ten endpoint.
        ///
        /// Convention:
        /// - Endpoint at polyline[0] → interior tangent = polyline[1] - polyline[0] (= +tangent).
        /// - Endpoint at polyline[^1] → interior tangent = polyline[^2] - polyline[^1] (= -tangent).
        ///
        /// Tolerance 0.5m dla matching endpoint position to polyline endpoints.
        /// </summary>
        private static Vector3 ComputeEndpointInteriorTangent(SchemaGeometry geometry, int endpointIdx)
        {
            if (endpointIdx < 0 || endpointIdx >= geometry.endpoints.Count)
                return Vector3.right;  // fallback

            Vector3 epPos = geometry.endpoints[endpointIdx];
            const float matchTol = 0.5f;

            foreach (var track in geometry.tracks)
            {
                if (track.polyline == null || track.polyline.Count < 2) continue;

                // Endpoint matches polyline START?
                if (Vector3.Distance(track.polyline[0], epPos) < matchTol)
                {
                    Vector3 tangent = track.polyline[1] - track.polyline[0];
                    if (tangent.sqrMagnitude > 0.0001f) return tangent.normalized;
                }

                // Endpoint matches polyline END?
                int last = track.polyline.Count - 1;
                if (Vector3.Distance(track.polyline[last], epPos) < matchTol)
                {
                    Vector3 tangent = track.polyline[last - 1] - track.polyline[last];
                    if (tangent.sqrMagnitude > 0.0001f) return tangent.normalized;
                }
            }

            return Vector3.right;  // fallback (no matching polyline found)
        }

        /// <summary>
        /// "Półendpoint" walidacja dla schematu — sprawdza czy schemat MOŻE być outward
        /// (= w bieżącej rot albo po flip 180°). Po accept, auto-rotation wymusi correct
        /// orientation (= AutoRotationThresholdDeg=180° gwarantuje rotation alignment).
        ///
        /// Algorytm: znajduje OPPOSITE endpoint schematu (= najdalszy od snap endpoint w lokalnych
        /// coords). Sprawdza dla CURRENT rot ORAZ flipped rot (= +180°) czy opposite endpoint
        /// world position leży po OUTWARD stronie target. Akceptuje jeśli EITHER passes.
        ///
        /// Powód: outward validation runs PRZED auto-rotation. Jeśli wymagaliśmy strict outward
        /// w current rot, parking-end-snap (= outward valid TYLKO po flip 180°) byłby blocked
        /// nawet gdy auto-rotation by zaraz flip'nęło. Akceptujemy obie orientacje, pozwalamy
        /// auto-rotation handle proper alignment.
        ///
        /// Tolerance 1.0m dopuszcza minor floating-point i marginal cases.
        /// </summary>
        private static bool IsSchemaOutwardValid(
            SchemaGeometry geometry,
            Vector3 cursorAnchorLocal,
            int snapEpIdx,
            Vector3 targetPos,
            Vector3 epWorld,
            Quaternion rot,
            TrackGraph trackGraph,
            int targetNodeId)
        {
            const float tolerance = 1.0f;

            if (trackGraph == null || !trackGraph.Nodes.ContainsKey(targetNodeId))
                return true;  // brak info o target → akceptuj

            Vector3 targetOutDir = trackGraph.GetNodeDirection(targetNodeId).normalized;
            if (targetOutDir.sqrMagnitude < 0.001f)
                return true;  // brak direction info → akceptuj

            // Znajdź OPPOSITE endpoint (= najdalszy od snap endpoint w lokalnych coords)
            Vector3 snapEpLocal = geometry.endpoints[snapEpIdx];
            int oppositeIdx = -1;
            float maxDist = 0f;
            for (int i = 0; i < geometry.endpoints.Count; i++)
            {
                if (i == snapEpIdx) continue;
                float d = Vector3.Distance(geometry.endpoints[i], snapEpLocal);
                if (d > maxDist)
                {
                    maxDist = d;
                    oppositeIdx = i;
                }
            }
            if (oppositeIdx < 0) return true;  // single-endpoint schema → accept

            Vector3 oppositeLocal = geometry.endpoints[oppositeIdx];
            Vector3 oppositeRelToSnap = oppositeLocal - snapEpLocal;

            // Test current rot
            Vector3 oppositeWorldCurrent = targetPos + rot * oppositeRelToSnap;
            float dotCurrent = Vector3.Dot(oppositeWorldCurrent - targetPos, targetOutDir);

            // Test flipped rot (= rot * 180° around Y)
            Quaternion flippedRot = rot * Quaternion.Euler(0, 180f, 0);
            Vector3 oppositeWorldFlipped = targetPos + flippedRot * oppositeRelToSnap;
            float dotFlipped = Vector3.Dot(oppositeWorldFlipped - targetPos, targetOutDir);

            // Accept if EITHER orientation gives outward (auto-rotation will pick correct one)
            return dotCurrent >= -tolerance || dotFlipped >= -tolerance;
        }

        /// <summary>
        /// Helper — wypełnia <see cref="SnapResult.snappedEndpoints"/> per endpoint w tolerance
        /// po apply translation. Używane przez fazę sticky + standard acquire.
        ///
        /// CONVENTION: anchor = snap endpoint (= result.anchorEndpointIdx). Schemat preview
        /// pozycjonowany tak że snap endpoint at (cursor + translation). Other endpoints relative.
        /// </summary>
        private static void PopulateSnappedEndpoints(
            SchemaGeometry geometry,
            Vector3 cursorWorldPos,
            Vector3 translation,
            Quaternion rot,
            float toleranceMeters,
            SnapPointSystem snapPointSystem,
            TrackGraph trackGraph,
            SnapResult result)
        {
            // Anchor = snap endpoint (= result.anchorEndpointIdx) gdy snap aktywny.
            // Fallback do endpoint[0] (= wjazd) gdy brak snap.
            int anchorIdx = result.hasAnySnap && result.anchorEndpointIdx >= 0
                && result.anchorEndpointIdx < geometry.endpoints.Count
                ? result.anchorEndpointIdx : 0;
            Vector3 anchorLocal = geometry.endpoints != null && geometry.endpoints.Count > 0
                ? geometry.endpoints[anchorIdx]
                : geometry.centroid;

            for (int i = 0; i < geometry.endpoints.Count; i++)
            {
                Vector3 endpointLocal = geometry.endpoints[i] - anchorLocal;
                Vector3 endpointWorldAfterTrans = cursorWorldPos + translation + rot * endpointLocal;

                var (nodeId, nodePos) = snapPointSystem.FindNearestSnapPoint(endpointWorldAfterTrans, toleranceMeters);
                if (nodeId < 0) continue;

                float dist = Vector3.Distance(endpointWorldAfterTrans, nodePos);
                if (dist <= toleranceMeters)
                {
                    Vector3 trackDir = trackGraph != null ? trackGraph.GetNodeDirection(nodeId) : Vector3.right;
                    result.snappedEndpoints.Add(new EndpointSnapInfo
                    {
                        schemaEndpointIndex = i,
                        schemaEndpointWorld = endpointWorldAfterTrans,
                        targetWorld = nodePos,
                        distance = dist,
                        trackGraphNodeId = nodeId,
                        trackDirectionAtNode = trackDir,
                    });
                }
            }
            result.snappedEndpointCount = result.snappedEndpoints.Count;
        }

        /// <summary>
        /// Liczy auto-rotation correction. Sprawdza kierunek istniejącego toru przy snap point
        /// vs kierunek INTERIOR SCHEMATU (= INTO schemat z anchor endpoint). Jeśli różni się o
        /// ≤AutoRotationThresholdDeg, propose rotation. Cel: po auto-rotation, schemat axis
        /// at anchor endpoint jest dokładnie współliniowy z istniejącym torem (kąt 0°).
        ///
        /// Schema interior direction (= INTO schemat from anchor endpoint) computed dynamically
        /// z polyline tangent:
        /// - Endpoint na początku polyline → interior tangent = polyline[1] - polyline[0].
        /// - Endpoint na końcu polyline → interior tangent = polyline[^2] - polyline[^1].
        /// (Kierunek od endpoint TOWARD interior schematu.)
        ///
        /// Snap aligned: schemaInteriorWorld == ±trackDirWorld (schema interior matches target
        /// outward, lub opposite — sprawdzamy obie orientacje).
        /// </summary>
        private static float? ComputeAutoRotationCorrection(
            SchemaGeometry geometry,
            int anchorEndpointIdx,
            float currentRotationDeg,
            TrackGraph trackGraph,
            int anchorNodeId)
        {
            if (anchorEndpointIdx < 0 || anchorEndpointIdx >= geometry.endpoints.Count) return null;
            if (!trackGraph.Nodes.ContainsKey(anchorNodeId)) return null;

            // Kierunek istniejącego toru przy snap point (XZ plane)
            Vector3 trackDirWorld = trackGraph.GetNodeDirection(anchorNodeId);
            trackDirWorld.y = 0;
            if (trackDirWorld.sqrMagnitude < 0.0001f) return null;
            trackDirWorld.Normalize();

            // Kierunek SCHEMA INTERIOR przy anchor endpoint (= INTO schemat) — computed z polyline.
            Vector3 schemaDirLocal = ComputeEndpointInteriorTangent(geometry, anchorEndpointIdx);
            if (schemaDirLocal.sqrMagnitude < 0.0001f) return null;
            schemaDirLocal.Normalize();

            Quaternion currentRot = Quaternion.Euler(0, currentRotationDeg, 0);
            Vector3 schemaDirWorld = currentRot * schemaDirLocal;

            // Convention: Unity Y-rotation jest CW gdy patrzymy z +Y w dół (left-handed).
            // Standardowy atan2(z, x) jest CCW (right-handed). Aby diff w stopniach pasował
            // do Quaternion.Euler(0, diff, 0) jako Y-rotation, trzeba flipować znak atan2.
            // Czyli "rotation Y" w Unity space ≡ -atan2(z, x).
            float angleSchemaDeg = -Mathf.Atan2(schemaDirWorld.z, schemaDirWorld.x) * Mathf.Rad2Deg;
            float angleTrackDeg = -Mathf.Atan2(trackDirWorld.z, trackDirWorld.x) * Mathf.Rad2Deg;
            float diff = Mathf.DeltaAngle(angleSchemaDeg, angleTrackDeg);

            // Track może iść w przeciwnym kierunku (schema wchodzi do node = wjazd; tor wychodzi
            // z node = outward direction). Sprawdź obie orientacje i wybierz mniejszą bezwzględną.
            float diffOpposite = Mathf.DeltaAngle(angleSchemaDeg, angleTrackDeg + 180f);
            if (Mathf.Abs(diffOpposite) < Mathf.Abs(diff))
                diff = diffOpposite;

            if (Mathf.Abs(diff) > AutoRotationThresholdDeg) return null;

            // Propose new rotation = current + diff (po apply, schema wjazd jest dokładnie
            // współliniowy z istniejącym torem)
            return Mathf.Repeat(currentRotationDeg + diff, 360f);
        }

        /// <summary>
        /// A13 — sprawdza czy zmiana <see cref="SchemaParameters.trackSpacing"/> dałaby lepszy
        /// multi-snap. Iteruje po kandydatach delta i wybiera najlepszy. Modyfikuje
        /// <paramref name="result"/> jeśli znaleziono kandydata.
        /// </summary>
        private static void EvaluateAdaptiveCandidate(
            SchemaGeometry currentGeometry,
            Vector3 cursorWorldPos,
            float currentRotationDeg,
            float toleranceMeters,
            SnapPointSystem snapPointSystem,
            SchemaParameters currentParams,
            ITurnoutSchemaGenerator generator,
            SnapResult result)
        {
            if (currentParams == null || generator == null) return;

            // Aktualne spacing (sredni jeśli per-pair różne)
            float currentSpacing = AverageSpacing(currentParams);
            int currentSnappedCount = result.snappedEndpointCount;

            float bestSpacing = currentSpacing;
            int bestSnappedCount = currentSnappedCount;

            foreach (float delta in AdaptiveSpacingDeltas)
            {
                float candidateSpacing = SchemaParameters.ClampSpacing(currentSpacing + delta);
                if (Mathf.Approximately(candidateSpacing, currentSpacing)) continue;

                int snappedCount = EvaluateSnapsForSpacing(
                    candidateSpacing, currentParams, generator,
                    cursorWorldPos, currentRotationDeg, toleranceMeters, snapPointSystem);

                if (snappedCount > bestSnappedCount)
                {
                    bestSnappedCount = snappedCount;
                    bestSpacing = candidateSpacing;
                }
            }

            // Propose tylko jeśli kandydat daje WIĘCEJ snap'ów niż current (≥2 dodatkowych)
            if (bestSnappedCount >= currentSnappedCount + 2 && bestSpacing != currentSpacing)
            {
                result.hasAdaptivePromptCandidate = true;
                result.proposedSpacingMeters = bestSpacing;
                result.proposedSnappedCount = bestSnappedCount;
            }
        }

        /// <summary>
        /// Średnia wartość spacing (gdy per-pair różne, weź mean).
        /// </summary>
        private static float AverageSpacing(SchemaParameters p)
        {
            if (p.trackSpacings == null || p.trackSpacings.Length == 0)
                return p.trackSpacing > 0 ? p.trackSpacing : SchemaParameters.DefaultSpacing;
            float sum = 0f;
            for (int i = 0; i < p.trackSpacings.Length; i++) sum += p.trackSpacings[i];
            return sum / p.trackSpacings.Length;
        }

        /// <summary>
        /// Re-genuje geometrię z podanym spacing i liczy ile endpointów snap'uje się.
        /// Heavy operation — używać oszczędnie (max ~10 razy per frame, lub throttle to checkpoint per N frames).
        /// </summary>
        private static int EvaluateSnapsForSpacing(
            float candidateSpacing,
            SchemaParameters originalParams,
            ITurnoutSchemaGenerator generator,
            Vector3 cursorWorldPos,
            float currentRotationDeg,
            float toleranceMeters,
            SnapPointSystem snapPointSystem)
        {
            // Klonuj parameters z modyfikacją spacing
            var candidateParams = new SchemaParameters
            {
                trackCount = originalParams.trackCount,
                trackSpacing = candidateSpacing,
                trackSpacings = null,  // force shorthand → expand all to candidateSpacing
                turnoutType = originalParams.turnoutType,
                turnoutTypes = originalParams.turnoutTypes != null ? (string[])originalParams.turnoutTypes.Clone() : null,
                mirror = originalParams.mirror,
            };

            int turnoutCount = generator.ComputeTurnoutCount(candidateParams.trackCount);
            candidateParams.Normalize(turnoutCount);

            var candidateGeom = generator.Generate(candidateParams);
            if (candidateGeom == null || candidateGeom.endpoints.Count == 0) return 0;

            Quaternion rot = Quaternion.Euler(0, currentRotationDeg, 0);

            // Anchor convention: endpoint[0] = wjazd, cursor = pozycja anchor
            Vector3 anchorLocal = candidateGeom.endpoints[0];
            Vector3 anchorWorld = cursorWorldPos;

            // Sprawdź czy wjazd snapuje
            var (nodeId, nodePos) = snapPointSystem.FindNearestSnapPoint(anchorWorld, toleranceMeters);
            if (nodeId < 0) return 0;

            Vector3 translation = nodePos - anchorWorld;

            // Count snapped endpoints po apply translation
            int count = 0;
            for (int i = 0; i < candidateGeom.endpoints.Count; i++)
            {
                Vector3 epLocal = candidateGeom.endpoints[i] - anchorLocal;
                Vector3 epWorld = cursorWorldPos + translation + rot * epLocal;
                var (nid, _) = snapPointSystem.FindNearestSnapPoint(epWorld, toleranceMeters);
                if (nid >= 0) count++;
            }

            return count;
        }
    }
}
