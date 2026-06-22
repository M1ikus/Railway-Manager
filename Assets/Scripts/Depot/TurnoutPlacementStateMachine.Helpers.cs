using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using RailwayManager.Core;
using RailwayManager.Core.Rendering;

namespace DepotSystem
{
    public partial class TurnoutPlacementStateMachine
    {
        // ═══════════════════════════════════════════
        //  HELPERS — chain finding (mouse → track/endpoint)
        //  + preview line construction (LineRenderer)
        // ═══════════════════════════════════════════

        private (PlacedTrackSegment track, float distAlong) FindTrackUnderMouse()
        {
            Ray ray = mainCamera.ScreenPointToRay(Mouse.current != null ? (Vector3)Mouse.current.position.ReadValue() : Vector3.zero);
            if (!Physics.Raycast(ray, out RaycastHit hit, 1000f))
                return (null, -1f);

            Transform trackRoot = FindTrackRoot(hit.collider.transform);
            if (trackRoot == null) return (null, -1f);

            if (trackBuilder == null) return (null, -1f);

            foreach (var placed in trackBuilder.PlacedTracks)
            {
                if (placed.TrackObject == trackRoot.gameObject)
                {
                    float dist = TrackGeometry.ProjectPointOnPolyline(placed.Polyline, hit.point);
                    return (placed, dist);
                }
            }

            return (null, -1f);
        }

        /// <summary>
        /// Fallback gdy raycast nie trafił w tor: szuka wolnego końca toru (degree-1 node)
        /// w pobliżu kursora. Pozwala stawiać rozjazdy na końcu toru bez pixel-perfect aim.
        /// </summary>
        private Vector3? GetMouseGroundPos()
        {
            if (mainCamera == null) return null;
            Ray ray = mainCamera.ScreenPointToRay(Mouse.current != null ? (Vector3)Mouse.current.position.ReadValue() : Vector3.zero);
            Plane groundPlane = new Plane(Vector3.up, Vector3.zero);
            if (!groundPlane.Raycast(ray, out float dist)) return null;
            return ray.GetPoint(dist);
        }

        private StraightChain CreateFreestandingChain(Vector3 worldPos)
        {
            // Kąt 0° = Vector3.right (od lewej do prawej), rośnie zgodnie z ruchem wskazówek zegara
            float rad = freestandingAngleDeg * Mathf.Deg2Rad;
            Vector3 dir = new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad)).normalized;

            return new StraightChain
            {
                Segments = new List<PlacedTrackSegment>(),
                MergedPolyline = new List<Vector3> { worldPos, worldPos + dir * 0.1f },
                TotalLength = 0.1f,
                StartPos = worldPos,
                EndPos = worldPos + dir * 0.1f,
                Direction = dir
            };
        }

        /// <summary>
        /// MULTI-ANCHOR snap dla nowego rozjazdu — sprawdza czy któryś z 3 endpointów
        /// NOWEGO rozjazdu (Origin / BodyFarEnd / DivergingEnd) jest blisko istniejącego
        /// endpoint toru. Jeśli tak, snap = przesuń NEW.origin tak by ten endpoint pokrywał
        /// target.
        ///
        /// Działa dla freestanding placement (= cursor poza torami). Player kontroluje rotację
        /// (Ctrl+Scroll), kierunek skewu (R = mirror), a snap auto-detect który endpoint NEW
        /// jest najbliższy istniejącemu (smallest cursor-to-anchor distance vs target).
        ///
        /// Existing endpoints uwzględniane:
        /// - 1-edge nodes w grafie (= wolne końce zwykłych torów).
        /// - 3 endpointy każdej TurnoutEntity (PreStart, BodyFarEnd, DivergingEnd).
        ///
        /// Zwraca synthetic chain z chain.StartPos = NEW.origin (po snap shift), direction = dir.
        /// </summary>
        private (StraightChain chain, float distAlong, PlacedTrackSegment track) FindMultiAnchorSnap(TurnoutData.TurnoutDefinition def)
        {
            const float searchRadius = 5f;

            if (mainCamera == null || trackBuilder == null) return (null, -1f, null);
            var tg = DepotServices.Get<TrackGraph>();
            if (tg == null) return (null, -1f, null);

            Ray ray = mainCamera.ScreenPointToRay(Mouse.current != null ? (Vector3)Mouse.current.position.ReadValue() : Vector3.zero);
            Plane groundPlane = new Plane(Vector3.up, Vector3.zero);
            if (!groundPlane.Raycast(ray, out float dist)) return (null, -1f, null);
            Vector3 cursorWorld = ray.GetPoint(dist);

            // Direction freestanding (= current rotation, używana dla "where would NEW endpoints land
            // if origin=cursor" — to player intent detection).
            float rad = freestandingAngleDeg * Mathf.Deg2Rad;
            Vector3 currentDir = new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad)).normalized;

            // 3 anchor positions PRZY założeniu że NEW.origin = cursorWorld z current dir (= player intent)
            Vector3 originAnchor = cursorWorld;
            Vector3 bodyFarEndAnchor = cursorWorld + currentDir * def.Length;
            Vector3 divEndAnchor = TurnoutData.GetDivergingEndpoint(cursorWorld, currentDir, def, divergeLeft);

            // Collect existing endpoints WITH outward directions (= "kąt 0°" continuation direction).
            // OUTWARD direction = direction tor "kontynuowałby się" za endpoint.
            var existingEndpoints = new List<(Vector3 pos, Vector3 outwardDir, string label)>();

            // 1) Regular tor 1-edge endpoints
            foreach (var kvp in tg.Nodes)
            {
                if (kvp.Value.EdgeIds.Count != 1) continue;
                Vector3 outwardDir = tg.GetNodeDirection(kvp.Key).normalized;
                existingEndpoints.Add((kvp.Value.Position, outwardDir, $"TorEnd(node{kvp.Key})"));
            }

            // 2) TurnoutEntity endpoints (3 per turnout) z explicit outward directions
            if (trackBuilder.TurnoutEntities != null)
            {
                foreach (var kvp in trackBuilder.TurnoutEntities)
                {
                    var entity = kvp.Value;
                    if (entity == null || entity.Definition.Length <= 0) continue;
                    Vector3 d = entity.Direction.normalized;

                    // PreStart: 1m PRZED origin, outward = -d (kontynuacja w stronę +X dawałaby
                    // kolizję z body, więc outward to przeciwnie).
                    existingEndpoints.Add((entity.Origin + (-d) * 1.0f, -d, $"T{kvp.Key}.PreStart"));

                    // BodyFarEnd: koniec body, outward = +d (continuation forward).
                    existingEndpoints.Add((entity.Origin + d * entity.Definition.Length, d, $"T{kvp.Key}.BodyFarEnd"));

                    // DivergingEnd: koniec łuku, outward = direction tangenta na końcu łuku.
                    Vector3 divEndDir = TurnoutData.GetDivergingEndDirection(d, entity.Definition, entity.DivergeLeft);
                    existingEndpoints.Add((TurnoutData.GetDivergingEndpoint(entity.Origin, d, entity.Definition, entity.DivergeLeft), divEndDir, $"T{kvp.Key}.DivEnd"));
                }
            }

            // For each anchor, find best (smallest cursor-to-anchor distance from existing endpoint).
            // PLUS validation: NEW geometry musi leżeć po OUTWARD stronie istniejącego endpoint
            // (= "półendpoint" — snap zablokowany gdy NEW byłby po stronie gdzie tor JUŻ JEST).
            float bestDist = searchRadius;
            Vector3 bestNewOrigin = Vector3.zero;
            Vector3 bestNewDir = currentDir;
            string bestLabel = "";

            float sign = divergeLeft ? 1f : -1f;
            float frogAngleRad = def.FrogAngle;

            foreach (var (eePos, eeOutDir, eeLabel) in existingEndpoints)
            {
                // ── Anchor=Origin ──
                // NEW.origin pokrywa eePos. NEW.dir = eeOutDir (kontynuacja outward = kąt 0°).
                float distToOrigin = Vector3.Distance(eePos, originAnchor);
                if (distToOrigin < bestDist)
                {
                    Vector3 candNewOrigin = eePos;
                    Vector3 candNewDir = eeOutDir;
                    if (IsNewGeometryOutward(candNewOrigin, candNewDir, eePos, eeOutDir, def, AnchorType.Origin))
                    {
                        bestDist = distToOrigin;
                        bestNewOrigin = candNewOrigin;
                        bestNewDir = candNewDir;
                        bestLabel = $"NEW.Origin↔{eeLabel}";
                    }
                }

                // ── Anchor=BodyFarEnd ──
                // NEW.bodyFarEnd pokrywa eePos. NEW.dir = -eeOutDir (body grows from outward-side
                // origin TOWARDS target). Body region = po outward stronie ✓.
                float distToBodyFarEnd = Vector3.Distance(eePos, bodyFarEndAnchor);
                if (distToBodyFarEnd < bestDist)
                {
                    Vector3 candNewDir = -eeOutDir;
                    Vector3 candNewOrigin = eePos + eeOutDir * def.Length;
                    if (IsNewGeometryOutward(candNewOrigin, candNewDir, eePos, eeOutDir, def, AnchorType.BodyFarEnd))
                    {
                        bestDist = distToBodyFarEnd;
                        bestNewOrigin = candNewOrigin;
                        bestNewDir = candNewDir;
                        bestLabel = $"NEW.BodyFarEnd↔{eeLabel}";
                    }
                }

                // ── Anchor=DivergingEnd ──
                // NEW.divEnd pokrywa eePos. NEW.divEndDir = -eeOutDir (incoming z outward strony,
                // NIE +eeOutDir które dawałoby body INWARD).
                //
                // Z arc tangent przy divEnd = -eeOutDir, pociąg jadący po NEW arc dochodzi do
                // divEnd z direction -eeOutDir (przeciwnie do istniejącego toru outward). Body
                // NEW wypada OUTWARD (= za tor.End, lateral offset hMain ≈ 1.83m). NEW.body NIE
                // koliduje z istniejącym torem.
                //
                // Solve: GetDivergingEndDirection(NEW.dir, def, divergeLeft) = -eeOutDir
                // → Q(0, sign*α, 0) * NEW.dir = -eeOutDir
                // → NEW.dir = Q(0, -sign*α, 0) * -eeOutDir = -Q(0, -sign*α, 0) * eeOutDir.
                float distToDivEnd = Vector3.Distance(eePos, divEndAnchor);
                if (distToDivEnd < bestDist)
                {
                    Vector3 candNewDir = (-(Quaternion.Euler(0, -sign * frogAngleRad * Mathf.Rad2Deg, 0) * eeOutDir)).normalized;
                    Vector3 divOffsetWithNewDir = TurnoutData.GetDivergingEndpoint(Vector3.zero, candNewDir, def, divergeLeft);
                    Vector3 candNewOrigin = eePos - divOffsetWithNewDir;
                    if (IsNewGeometryOutward(candNewOrigin, candNewDir, eePos, eeOutDir, def, AnchorType.DivergingEnd))
                    {
                        bestDist = distToDivEnd;
                        bestNewOrigin = candNewOrigin;
                        bestNewDir = candNewDir;
                        bestLabel = $"NEW.DivergingEnd↔{eeLabel}";
                    }
                }
            }

            if (bestDist >= searchRadius) return (null, -1f, null);

            Log.Info($"[TurnoutPlacement] Multi-anchor snap: {bestLabel}, dist={bestDist:F2}m, NEW.origin={bestNewOrigin}, NEW.dir={bestNewDir} (kąt 0° z target)");

            // Build synthetic chain z NEW.origin jako chain.Start, direction = OVERRIDE (kąt 0°)
            var chain = new StraightChain
            {
                Segments = new List<PlacedTrackSegment>(),
                MergedPolyline = new List<Vector3> { bestNewOrigin, bestNewOrigin + bestNewDir * 0.1f },
                TotalLength = 0.1f,
                StartPos = bestNewOrigin,
                EndPos = bestNewOrigin + bestNewDir * 0.1f,
                Direction = bestNewDir
            };
            return (chain, 0f, null);
        }

        /// <summary>
        /// "Półendpoint" walidacja — sprawdza czy NEW geometria leży po OUTWARD stronie
        /// istniejącego endpoint (= po stronie gdzie jeszcze NIE MA toru). Strict dla wszystkich
        /// anchorów po tym jak DivergingEnd używa flipped formula (= incoming direction = body OUTWARD).
        ///
        /// Wymaga że WSZYSTKIE NEW key positions (origin, bodyFarEnd, divEnd) są w outward
        /// half-space: Dot(P - eePos, outDir) ≥ -tolerance.
        ///
        /// Tolerance 0.5m dopuszcza floating-point i minor junction overlap.
        /// </summary>
        private bool IsNewGeometryOutward(
            Vector3 newOrigin, Vector3 newDir,
            Vector3 eePos, Vector3 eeOutDir,
            TurnoutData.TurnoutDefinition def,
            AnchorType anchor)
        {
            const float tolerance = 0.5f;

            Vector3 bodyFarEnd = newOrigin + newDir * def.Length;
            Vector3 divEnd = TurnoutData.GetDivergingEndpoint(newOrigin, newDir, def, divergeLeft);

            float dotOrigin = Vector3.Dot(newOrigin - eePos, eeOutDir);
            float dotBodyFarEnd = Vector3.Dot(bodyFarEnd - eePos, eeOutDir);
            float dotDivEnd = Vector3.Dot(divEnd - eePos, eeOutDir);

            // Strict for all anchors — NEW body fully OUTWARD.
            // - Origin: body extends +outDir z eePos.
            // - BodyFarEnd: body extends -outDir z (eePos + outDir*length).
            // - DivergingEnd (with flipped formula): body extends from origin ≈ eePos+outDir*hMainX
            //   in direction ≈ -outDir, ending at bodyFarEnd ≈ eePos+outDir*0.17m. Lateral offset
            //   hMain ≈ 1.83m. Body fully OUTWARD by construction.
            return dotOrigin >= -tolerance && dotBodyFarEnd >= -tolerance && dotDivEnd >= -tolerance;
        }

        private enum AnchorType
        {
            Origin,
            BodyFarEnd,
            DivergingEnd
        }

        /// <summary>
        /// Snap "magnetyczny" do KAŻDEGO endpointu istniejącego rozjazdu (= TurnoutEntity).
        /// Sprawdza 4 endpointy per rozjazd:
        /// - Origin (= początek body, junction 3-edge — SKIP, niejednoznaczny direction).
        /// - BodyFarEnd (= koniec body od strony +X względem effectiveDir).
        /// - DivergingEnd (= koniec łuku odgałęziającego).
        /// - PreStart (= 1 metr PRZED origin w kierunku -effectiveDir, = wjazd do rozjazdu).
        ///
        /// Nie wymaga node graph degree — explicit iterate TurnoutEntities. Działa zawsze gdy
        /// istnieje TurnoutEntity, niezależnie czy endpoint jest 1-edge, 2-edge boundary czy
        /// junction.
        ///
        /// Zwraca synthetic chain extending OUTWARD od endpoint (= w kierunku gdzie nowy rozjazd
        /// powinien się rozszerzyć). flipDirection auto-managed przez UpdateHover (synthetic
        /// chain → flip=false).
        /// </summary>
        private (StraightChain chain, float distAlong, PlacedTrackSegment track) FindNearestTurnoutEndpointMagnet()
        {
            const float searchRadius = 10f;

            if (mainCamera == null || trackBuilder == null) return (null, -1f, null);

            Ray ray = mainCamera.ScreenPointToRay(Mouse.current != null ? (Vector3)Mouse.current.position.ReadValue() : Vector3.zero);
            Plane groundPlane = new Plane(Vector3.up, Vector3.zero);
            if (!groundPlane.Raycast(ray, out float dist)) return (null, -1f, null);
            Vector3 mouseWorld = ray.GetPoint(dist);

            float bestDist = searchRadius;
            Vector3 bestPos = Vector3.zero;
            Vector3 bestOutwardDir = Vector3.zero;
            string bestLabel = "";

            foreach (var kvp in trackBuilder.TurnoutEntities)
            {
                var entity = kvp.Value;
                if (entity == null || entity.Definition.Length <= 0) continue;

                Vector3 origin = entity.Origin;
                Vector3 dir = entity.Direction.normalized;
                Vector3 bodyFarEnd = origin + dir * entity.Definition.Length;
                Vector3 divEnd = TurnoutData.GetDivergingEndpoint(origin, dir, entity.Definition, entity.DivergeLeft);
                Vector3 divEndDir = TurnoutData.GetDivergingEndDirection(dir, entity.Definition, entity.DivergeLeft);

                // OUTWARD direction per endpoint:
                // - PreStart: outward = -dir (kontynuacja toru wjazdowego dalej w przeciwną stronę).
                //   Pre extends from origin in -dir over jakąś długość; jego "external" end jest -dir od origin.
                // - BodyFarEnd: outward = +dir (kontynuacja body forward).
                // - DivEnd: outward = divEndDir (kontynuacja arc tangent).

                // PreStart approximated: 1m PRZED origin (= -dir direction). Outward from there = -dir.
                Vector3 preStart = origin + (-dir) * 1.0f;  // approximation; actual depends on Pre length

                TryCandidate(mouseWorld, preStart, -dir, $"{entity.DefinitionName}.PreStart",
                    ref bestDist, ref bestPos, ref bestOutwardDir, ref bestLabel);
                TryCandidate(mouseWorld, bodyFarEnd, dir, $"{entity.DefinitionName}.BodyFarEnd",
                    ref bestDist, ref bestPos, ref bestOutwardDir, ref bestLabel);
                TryCandidate(mouseWorld, divEnd, divEndDir, $"{entity.DefinitionName}.DivergingEnd",
                    ref bestDist, ref bestPos, ref bestOutwardDir, ref bestLabel);
            }

            if (bestDist >= searchRadius) return (null, -1f, null);

            // Build synthetic chain extending OUTWARD from snap target
            var chain = new StraightChain
            {
                Segments = new List<PlacedTrackSegment>(),
                MergedPolyline = new List<Vector3> { bestPos, bestPos + bestOutwardDir * 0.1f },
                TotalLength = 0.1f,
                StartPos = bestPos,
                EndPos = bestPos + bestOutwardDir * 0.1f,
                Direction = bestOutwardDir
            };
            return (chain, 0f, null);
        }

        private static void TryCandidate(Vector3 mouse, Vector3 pos, Vector3 outwardDir, string label,
            ref float bestDist, ref Vector3 bestPos, ref Vector3 bestOutwardDir, ref string bestLabel)
        {
            float d = Vector3.Distance(mouse, pos);
            if (d < bestDist)
            {
                bestDist = d;
                bestPos = pos;
                bestOutwardDir = outwardDir;
                bestLabel = label;
            }
        }

        /// <summary>
        /// Sprawdza czy 2-edge node jest BOUNDARY rozjazdu — czyli jeden edge to turnout member
        /// (Body lub Diverging), drugi edge to zwykły tor (Pre/Post lub niezwiązany segment).
        ///
        /// Zwraca true dla node'ów typu "Body.farEnd ↔ Post.start" (= koniec ciała rozjazdu od
        /// strony +X) lub "Diverging.end ↔ jakiś tor" (= koniec odgałęzienia podłączony do
        /// kolejnego toru). Te są snap targets dla nowego rozjazdu kontynuującego tor za istniejący.
        ///
        /// Zwraca false dla 2-edge w środku zwykłego toru (= dwa straight segments collinearly
        /// joined, brak związku z rozjazdem).
        /// </summary>
        private bool IsTurnoutBoundaryNode(TrackNode node, TrackGraph tg)
        {
            if (trackBuilder == null) return false;
            if (node.EdgeIds.Count != 2) return false;

            int turnoutMemberCount = 0;
            int nonMemberCount = 0;
            foreach (int edgeId in node.EdgeIds)
            {
                var seg = FindSegmentByEdgeId(edgeId, tg);
                if (seg == null) continue;
                if (trackBuilder.TryGetTurnoutForTrack(seg.GraphTrackId, out _))
                    turnoutMemberCount++;
                else
                    nonMemberCount++;
            }
            // Boundary = mix (1 member, 1 non-member). Inne kombinacje (2 members albo 2 non-members)
            // = wewnątrz rozjazdu lub wewnątrz zwykłego toru — NIE boundary.
            return turnoutMemberCount == 1 && nonMemberCount == 1;
        }

        private PlacedTrackSegment FindSegmentByEdgeId(int edgeId, TrackGraph tg)
        {
            foreach (var placed in trackBuilder.PlacedTracks)
            {
                if (placed.GraphTrackId < 0) continue;
                var trackData = tg.GetTrack(placed.GraphTrackId);
                if (trackData != null && trackData.EdgeIds.Contains(edgeId))
                    return placed;
            }
            return null;
        }

        private (StraightChain chain, float distAlong, PlacedTrackSegment track) FindNearbyEndpointChain()
        {
            const float searchRadius = 8f;

            if (mainCamera == null || trackBuilder == null || turnoutPlacer == null)
                return (null, -1f, null);

            var tg = DepotServices.Get<TrackGraph>();
            if (tg == null) return (null, -1f, null);

            // Pozycja kursora na płaszczyźnie XZ
            Ray ray = mainCamera.ScreenPointToRay(Mouse.current != null ? (Vector3)Mouse.current.position.ReadValue() : Vector3.zero);
            Plane groundPlane = new Plane(Vector3.up, Vector3.zero);
            if (!groundPlane.Raycast(ray, out float dist))
                return (null, -1f, null);
            Vector3 mouseWorld = ray.GetPoint(dist);

            // Szukaj najbliższego SNAP TARGET w promieniu. Akceptujemy:
            // 1. Wolne endpointy (degree=1) — np. Pre.start, Post.end, Diverging.end gdy rozjazd
            //    jest "luzem" (= bez połączeń dalej).
            // 2. Boundary node rozjazdu (degree=2 gdzie 1 edge to turnout member): Body.farEnd
            //    z połączeniem do Post (= gdy rozjazd ma kontynuację toru za body).
            //    To pozwala snap do KAŻDEGO końca rozjazdu (= 3 endpoints: Pre, Post, Diverging),
            //    nawet gdy rozjazd jest podłączony do dłuższego toru.
            float bestDist = searchRadius;
            int bestNodeId = -1;

            foreach (var kvp in tg.Nodes)
            {
                var node = kvp.Value;
                if (node.EdgeIds.Count == 0) continue;
                if (node.EdgeIds.Count > 2) continue; // junctions (origin rozjazdu) skip — niejednoznaczny direction

                // Dla 2-edge: tylko jeśli to BOUNDARY rozjazdu (= 1 edge member, 1 nie).
                if (node.EdgeIds.Count == 2 && !IsTurnoutBoundaryNode(node, tg))
                    continue;

                float d = Vector3.Distance(mouseWorld, node.Position);
                if (d < bestDist)
                {
                    bestDist = d;
                    bestNodeId = kvp.Key;
                }
            }

            if (bestNodeId < 0) return (null, -1f, null);

            // Znajdź segment toru przy tym node. Dla 2-edge boundary preferuj NON-MEMBER edge
            // (= zwykły tor poza rozjazdem — to jest "outward" direction, gdzie nowy rozjazd
            // może być postawiony jako kontynuacja). Member edges (body/diverging) są krótkie i
            // służą do internals rozjazdu, nie do snap targets.
            var endpointNode = tg.Nodes[bestNodeId];
            if (endpointNode.EdgeIds.Count == 0) return (null, -1f, null);

            PlacedTrackSegment segment = null;
            foreach (int edgeIdCandidate in endpointNode.EdgeIds)
            {
                var seg = FindSegmentByEdgeId(edgeIdCandidate, tg);
                if (seg == null) continue;

                bool isMember = trackBuilder.TryGetTurnoutForTrack(seg.GraphTrackId, out _);
                if (isMember && segment == null)
                {
                    // Tymczasowy fallback — jeśli nie znajdziemy non-member, użyjemy member.
                    segment = seg;
                    continue;
                }
                if (!isMember)
                {
                    // Non-member edge → preferowany.
                    segment = seg;
                    break;
                }
            }

            if (segment == null) return (null, -1f, null);

            var chain = turnoutPlacer.FindStraightChain(segment);
            if (chain != null)
            {
                // Segment jest prosty — ustal czy endpoint jest na początku czy końcu chain
                float distToStart = Vector3.Distance(endpointNode.Position, chain.StartPos);
                float distToEnd = Vector3.Distance(endpointNode.Position, chain.EndPos);
                float distAlong = distToStart < distToEnd ? 0f : chain.TotalLength;
                return (chain, distAlong, segment);
            }

            // Segment jest krzywy (łuk, diverging leg) → syntetyczny chain w punkcie endpoint.
            // Rozjazd rozpocznie się w tym punkcie i przedłuży tor w nową przestrzeń.
            Vector3 ep = endpointNode.Position;
            Vector3 nodeDir = tg.GetNodeDirection(bestNodeId).normalized;

            var synthChain = new StraightChain
            {
                Segments = new List<PlacedTrackSegment>(),
                MergedPolyline = new List<Vector3> { ep, ep + nodeDir * 0.1f },
                TotalLength = 0.1f,
                StartPos = ep,
                EndPos = ep + nodeDir * 0.1f,
                Direction = nodeDir
            };
            return (synthChain, 0f, segment);
        }

        private Transform FindTrackRoot(Transform target)
        {
            Transform current = target;
            Transform lastTagged = null;

            while (current != null)
            {
                if (current.CompareTag("Track"))
                    lastTagged = current;
                current = current.parent;
            }

            return lastTagged;
        }

        private void EnsurePreviewLine(ref GameObject obj, ref LineRenderer line, string name)
        {
            if (obj == null)
            {
                obj = new GameObject(name);
                line = obj.AddComponent<LineRenderer>();
                line.material = MaterialFactory.CreateLine();
                line.startWidth = previewWidth;
                line.endWidth = previewWidth;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.positionCount = 0;
            }
            obj.SetActive(true);
        }

        private void SetLinePositions(LineRenderer line, List<Vector3> polyline, Color color)
        {
            if (line == null || polyline == null || polyline.Count < 2) return;

            line.startColor = color;
            line.endColor = color;
            line.positionCount = polyline.Count;

            for (int i = 0; i < polyline.Count; i++)
                line.SetPosition(i, polyline[i] + Vector3.up * previewHeight);
        }

        private void ShowOriginMarker(Vector3 position, bool valid)
        {
            if (originMarker == null)
            {
                originMarker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                originMarker.name = "TurnoutOriginMarker";
                originMarker.transform.localScale = Vector3.one * 0.5f;
                Destroy(originMarker.GetComponent<SphereCollider>());

                Material mat = MaterialFactory.CreateLit();
                MaterialFactory.SetEmission(mat, Color.black);
                originMarker.GetComponent<MeshRenderer>().material = mat;
            }

            originMarker.SetActive(true);
            originMarker.transform.position = position + Vector3.up * 0.3f;

            Color c = valid ? Color.green : Color.red;
            var renderer = originMarker.GetComponent<MeshRenderer>();
            MaterialFactory.SetBaseColor(renderer.material, c);
            MaterialFactory.SetEmission(renderer.material, c * 0.4f);
        }
    }
}
