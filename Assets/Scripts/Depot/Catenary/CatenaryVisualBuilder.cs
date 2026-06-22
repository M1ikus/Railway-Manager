using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DepotSystem
{
    /// <summary>
    /// Buduje wizualizacje 3D sieci trakcyjnej: słupy, bramki, przewody, wieszaki.
    /// Wydzielony z logiki — operuje na gotowych danych z SupportOptimizer.
    /// </summary>
    public static class CatenaryVisualBuilder
    {
        // Geometria słupów/bramek
        public const float PoleRadius = 0.08f;
        public const float ContactWireHeight = 5.5f;
        public const float BasePoleOffset = 3.5f;

        // Geometria drutów
        public const float MessengerWireHeight = 6.5f;
        public const float SagDepth = 0.5f;
        public const float WireRadius = 0.015f;
        public const float MessengerRadius = 0.012f;
        public const float DropperRadius = 0.008f;
        public const float DropperSpacing = 10f;

        /// <summary>
        /// Buduje kompletną wizualizację sieci: struktury wsporcze + druty.
        /// </summary>
        public static void BuildAllVisuals(
            CatenaryNetwork network,
            Transform parent,
            Material poleMat, Material armMat, Material wireMat)
        {
            // 1. Struktury wsporcze
            foreach (var support in network.Supports)
            {
                if (support.Type == SupportType.Pole)
                    BuildPoleVisual(support, parent, poleMat, armMat);
                else
                    BuildGantryVisual(support, parent, poleMat, armMat);

                // Dodaj komponent debugowy
                if (support.Visual != null)
                {
                    var debug = support.Visual.AddComponent<SupportDebugInfo>();
                    debug.Init(support);
                }
            }

            // 2. Przęsła drutowe
            foreach (var span in network.WireSpans)
                BuildWireSpanVisual(span, parent, wireMat);
        }

        /// <summary>
        /// Tworzy WireSpany z listy SupportPointów — łączy kolejne punkty tego samego toru.
        /// </summary>
        public static List<WireSpan> CreateWireSpans(List<SupportPoint> allPoints)
        {
            var spans = new List<WireSpan>();
            if (allPoints == null || allPoints.Count < 2) return spans;

            // Fallback: punkty bez Support podpinamy do najbliższego istniejącego
            var withSupport = allPoints.Where(p => p.Support != null).ToList();
            foreach (var p in allPoints)
            {
                if (p.Support != null) continue;
                if (withSupport.Count == 0) continue;

                SupportStructure nearest = null;
                float nearestDist = float.MaxValue;
                foreach (var other in withSupport)
                {
                    float d = Vector3.Distance(p.Position, other.Position);
                    if (d < nearestDist) { nearestDist = d; nearest = other.Support; }
                }
                if (nearest != null)
                {
                    p.Support = nearest;
                    p.AttachPosition = p.Position;
                    nearest.Points.Add(p);
                }
            }

            var byTrack = new Dictionary<int, List<SupportPoint>>();
            foreach (var p in allPoints)
            {
                if (p.Support == null) continue;
                if (!byTrack.ContainsKey(p.TrackId))
                    byTrack[p.TrackId] = new List<SupportPoint>();
                byTrack[p.TrackId].Add(p);
            }

            foreach (var kvp in byTrack)
            {
                var points = kvp.Value;
                points.Sort((a, b) => a.DistAlongTrack.CompareTo(b.DistAlongTrack));

                for (int i = 0; i < points.Count - 1; i++)
                {
                    Vector3 pA = points[i].Position;
                    Vector3 pB = points[i + 1].Position;
                    float spanLen = Vector2.Distance(
                        new Vector2(pA.x, pA.z),
                        new Vector2(pB.x, pB.z));

                    if (spanLen < 1f) continue;

                    spans.Add(new WireSpan
                    {
                        TrackId = kvp.Key,
                        From = points[i],
                        To = points[i + 1],
                        SpanLength = spanLen
                    });
                }
            }

            return spans;
        }

        // ═══════════════════════════════════════════
        //  SŁUPY
        // ═══════════════════════════════════════════

        private static void BuildPoleVisual(
            SupportStructure support, Transform parent,
            Material poleMat, Material armMat)
        {
            var root = new GameObject("Pole");
            root.transform.SetParent(parent);
            support.Visual = root;

            Vector3 perp = Vector3.Cross(support.Tangent, Vector3.up).normalized;
            Vector3 poleWorldPos = support.Position + perp * support.PoleSide * support.PoleOffset;

            // Słup
            var pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pole.name = "PoleBody";
            pole.transform.SetParent(root.transform);
            pole.transform.position = new Vector3(
                poleWorldPos.x, support.PoleHeight / 2f, poleWorldPos.z);
            pole.transform.localScale = new Vector3(
                PoleRadius * 2f, support.PoleHeight / 2f, PoleRadius * 2f);
            pole.GetComponent<MeshRenderer>().material = poleMat;
            Object.Destroy(pole.GetComponent<CapsuleCollider>());

            // AttachPosition dla wszystkich punktów słupa
            foreach (var pt in support.Points)
                pt.AttachPosition = pt.Position;

            if (support.Points.Count > 0)
            {
                var point = support.Points[0];
                Vector3 wirePos = new Vector3(point.Position.x, 0, point.Position.z);
                Vector3 armDir = (wirePos - new Vector3(poleWorldPos.x, 0, poleWorldPos.z)).normalized;
                float armLen = support.CantileverLength;
                float armY = support.PoleHeight - 0.3f;

                Vector3 armStart = new Vector3(poleWorldPos.x, armY, poleWorldPos.z);
                Vector3 armEnd = new Vector3(
                    poleWorldPos.x + armDir.x * armLen, armY,
                    poleWorldPos.z + armDir.z * armLen);

                var armPath = new List<Vector3> { armStart, armEnd };
                TubeMeshGenerator.CreateTubeObject(
                    "Cantilever", armPath, 0.04f, armMat, root.transform, 4);

                // Izolator
                var insulator = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                insulator.name = "Insulator";
                insulator.transform.SetParent(root.transform);
                insulator.transform.position = new Vector3(
                    point.Position.x, armY, point.Position.z);
                insulator.transform.localScale = Vector3.one * 0.1f;
                insulator.GetComponent<MeshRenderer>().material = armMat;
                Object.Destroy(insulator.GetComponent<SphereCollider>());

                // Lina odciągowa
                if (support.HasStayWire)
                {
                    Vector3 stayTop = armEnd;
                    Vector3 stayBottom = new Vector3(
                        poleWorldPos.x, armY - armLen * 0.5f, poleWorldPos.z);
                    var stayPath = new List<Vector3> { stayTop, stayBottom };
                    TubeMeshGenerator.CreateTubeObject(
                        "StayWire", stayPath, 0.012f, armMat, root.transform, 4);
                }
            }
        }

        // ═══════════════════════════════════════════
        //  BRAMKI
        // ═══════════════════════════════════════════

        private static void BuildGantryVisual(
            SupportStructure support, Transform parent,
            Material poleMat, Material armMat)
        {
            var root = new GameObject("Gantry");
            root.transform.SetParent(parent);
            support.Visual = root;

            float h = support.PoleHeight;

            // Nogi
            BuildLeg(support.LeftLegPosition, h, "LegLeft", root.transform, poleMat);
            BuildLeg(support.RightLegPosition, h, "LegRight", root.transform, poleMat);

            // Belka
            Vector3 left = support.LeftLegPosition;
            Vector3 right = support.RightLegPosition;
            Vector3 beamDir = (right - left);
            float beamLen = beamDir.magnitude;
            if (beamLen > 0.01f)
            {
                beamDir /= beamLen;
                float minProj = 0f, maxProj = beamLen;
                foreach (var p in support.Points)
                {
                    float proj = Vector3.Dot(p.Position - left, beamDir);
                    if (proj - BasePoleOffset < minProj) minProj = proj - BasePoleOffset;
                    if (proj + BasePoleOffset > maxProj) maxProj = proj + BasePoleOffset;
                }
                left = support.LeftLegPosition + beamDir * minProj;
                right = support.LeftLegPosition + beamDir * maxProj;
            }

            Vector3 beamStart = new Vector3(left.x, h, left.z);
            Vector3 beamEnd = new Vector3(right.x, h, right.z);

            var beamPath = new List<Vector3> { beamStart, beamEnd };
            TubeMeshGenerator.CreateTubeObject(
                "Beam", beamPath, 0.08f, poleMat, root.transform, 6);

            // Wieszaki od belki do punktów podwieszenia
            Vector3 beamVec = beamEnd - beamStart;
            float beamLength = beamVec.magnitude;
            Vector3 beamDirN = beamLength > 0.01f ? beamVec / beamLength : Vector3.right;

            // Projekcja punktów na belkę
            var projections = new List<(SupportPoint point, Vector3 attachPoint)>();
            foreach (var point in support.Points)
            {
                Vector3 pointOnBeamPlane = new Vector3(point.Position.x, h, point.Position.z);
                float t = Vector3.Dot(pointOnBeamPlane - beamStart, beamDirN);
                t = Mathf.Clamp(t, 0f, beamLength);
                Vector3 attachPoint = beamStart + beamDirN * t;
                projections.Add((point, attachPoint));
            }

            // Merge bliskich projekcji (≤31cm) — uśrednij pozycję, zachowaj oba punkty
            const float MergeThreshold = 0.31f;
            var merged = new bool[projections.Count];
            for (int i = 0; i < projections.Count; i++)
            {
                if (merged[i]) continue;
                var cluster = new List<int> { i };
                for (int j = i + 1; j < projections.Count; j++)
                {
                    if (merged[j]) continue;
                    if (Vector3.Distance(projections[i].attachPoint, projections[j].attachPoint) <= MergeThreshold)
                        cluster.Add(j);
                }
                if (cluster.Count > 1)
                {
                    Vector3 avg = Vector3.zero;
                    foreach (int idx in cluster)
                        avg += projections[idx].attachPoint;
                    avg /= cluster.Count;
                    for (int k = 0; k < cluster.Count; k++)
                    {
                        var (pt, _) = projections[cluster[k]];
                        projections[cluster[k]] = (pt, avg);
                        merged[cluster[k]] = true;
                    }
                    merged[cluster[0]] = true;
                }
            }

            // Ustaw AttachPosition i rysuj wieszaki (jeden per unikalna pozycja)
            float hangerTop = h - 0.1f;
            float hangerBottom = ContactWireHeight + 1f;
            var drawnHangers = new HashSet<Vector3>();

            foreach (var (point, attachPoint) in projections)
            {
                point.AttachPosition = new Vector3(attachPoint.x, 0f, attachPoint.z);

                if (hangerTop - hangerBottom < 0.1f) continue;

                // Rysuj wieszak tylko raz per unikalna pozycja
                Vector3 roundedPos = new Vector3(
                    Mathf.Round(attachPoint.x * 100f) / 100f,
                    0f,
                    Mathf.Round(attachPoint.z * 100f) / 100f);
                if (drawnHangers.Contains(roundedPos)) continue;
                drawnHangers.Add(roundedPos);

                Vector3 top = new Vector3(attachPoint.x, hangerTop, attachPoint.z);
                Vector3 bottom = new Vector3(attachPoint.x, hangerBottom, attachPoint.z);
                var hangerPath = new List<Vector3> { top, bottom };
                TubeMeshGenerator.CreateTubeObject(
                    $"Hanger_T{point.TrackId}", hangerPath, 0.02f, armMat, root.transform, 4);
            }
        }

        private static void BuildLeg(Vector3 position, float height, string name,
            Transform parent, Material mat)
        {
            var leg = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            leg.name = name;
            leg.transform.SetParent(parent);
            leg.transform.position = new Vector3(position.x, height / 2f, position.z);
            leg.transform.localScale = new Vector3(PoleRadius * 2f, height / 2f, PoleRadius * 2f);
            leg.GetComponent<MeshRenderer>().material = mat;
            Object.Destroy(leg.GetComponent<CapsuleCollider>());
        }

        // ═══════════════════════════════════════════
        //  DRUTY
        // ═══════════════════════════════════════════

        private static void BuildWireSpanVisual(WireSpan span, Transform parent, Material mat)
        {
            if (span?.From == null || span.To == null) return;
            if (span.SpanLength < 0.5f) return;

            var spanRoot = new GameObject($"Wire_T{span.TrackId}");
            spanRoot.transform.SetParent(parent);
            span.Visual = spanRoot;

            Vector3 fromPos = span.From.AttachPosition.sqrMagnitude > 0.001f
                ? span.From.AttachPosition : span.From.Position;
            Vector3 toPos = span.To.AttachPosition.sqrMagnitude > 0.001f
                ? span.To.AttachPosition : span.To.Position;

            // Przewód jezdny
            BuildContactWire(fromPos, toPos, span.SpanLength, spanRoot.transform, mat);

            // Lina nośna
            BuildMessengerWire(fromPos, toPos, span.SpanLength, spanRoot.transform, mat);

            // Wieszaki
            BuildDroppers(fromPos, toPos, span.SpanLength, spanRoot.transform, mat);
        }

        private static void BuildContactWire(
            Vector3 from, Vector3 to, float spanLen,
            Transform parent, Material mat)
        {
            var path = new List<Vector3>();
            int samples = Mathf.Max(2, Mathf.CeilToInt(spanLen / 3f));

            for (int i = 0; i <= samples; i++)
            {
                float t = (float)i / samples;
                Vector3 pos = Vector3.Lerp(from, to, t);
                pos.y = ContactWireHeight;
                path.Add(pos);
            }

            TubeMeshGenerator.CreateTubeObject(
                "ContactWire", path, WireRadius, mat, parent, 4);
        }

        private static void BuildMessengerWire(
            Vector3 from, Vector3 to, float spanLen,
            Transform parent, Material mat)
        {
            var path = new List<Vector3>();
            int samples = Mathf.Max(4, Mathf.CeilToInt(spanLen / 2f));

            for (int i = 0; i <= samples; i++)
            {
                float t = (float)i / samples;
                Vector3 pos = Vector3.Lerp(from, to, t);
                float sag = -4f * SagDepth * t * (1f - t);
                pos.y = MessengerWireHeight + sag;
                path.Add(pos);
            }

            TubeMeshGenerator.CreateTubeObject(
                "MessengerWire", path, MessengerRadius, mat, parent, 4);
        }

        private static void BuildDroppers(
            Vector3 from, Vector3 to, float spanLen,
            Transform parent, Material mat)
        {
            if (spanLen < DropperSpacing) return;

            int dropperCount = Mathf.FloorToInt(spanLen / DropperSpacing);
            float offset = (spanLen - dropperCount * DropperSpacing) / 2f;

            for (int i = 0; i <= dropperCount; i++)
            {
                float dist = offset + i * DropperSpacing;
                float t = dist / spanLen;
                if (t < 0.05f || t > 0.95f) continue;

                Vector3 posXZ = Vector3.Lerp(from, to, t);

                float sag = -4f * SagDepth * t * (1f - t);
                float messengerY = MessengerWireHeight + sag;

                Vector3 top = new Vector3(posXZ.x, messengerY, posXZ.z);
                Vector3 bottom = new Vector3(posXZ.x, ContactWireHeight, posXZ.z);

                if (messengerY - ContactWireHeight < 0.05f) continue;

                var dropperPath = new List<Vector3> { top, bottom };
                TubeMeshGenerator.CreateTubeObject(
                    $"Dropper_{i}", dropperPath, DropperRadius, mat, parent, 3);
            }
        }
    }
}
