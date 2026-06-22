using System.Collections.Generic;
using UnityEngine;
using RailwayManager.Core.Rendering;

namespace DepotSystem
{
    public partial class WallBuildingSystem
    {
        // ═══════════════════════════════════════════
        //  MESH ŚCIAN
        // ═══════════════════════════════════════════

        private GameObject BuildWallMesh(WallSegment seg)
        {
            var obj = new GameObject($"Wall_{seg.wallId}");
            obj.layer = LayerMask.NameToLayer("Default");
            try { obj.tag = "Wall"; } catch { /* Tag niezdefiniowany */ }

            RegenerateWallMesh(seg, obj);
            return obj;
        }

        /// <summary>
        /// Przerysowuje mesh ściany uwzględniając otwory (drzwi/okna).
        /// Ściana = seria Box segmentów z przerwami w miejscach otworów.
        /// </summary>
        public void RegenerateWallMesh(WallSegment seg, GameObject parent = null)
        {
            if (parent == null) parent = seg.wallObject;
            if (parent == null) return;

            // Wyczyść poprzednie dzieci
            for (int i = parent.transform.childCount - 1; i >= 0; i--)
                Destroy(parent.transform.GetChild(i).gameObject);

            Vector3 dir = seg.Direction;
            float length = seg.Length;
            Vector3 center = (seg.startPos + seg.endPos) / 2f;

            if (seg.openings.Count == 0)
            {
                // Prosta ściana — jeden Box jako child
                CreateWallBox(parent, center, dir, length, seg.height, wallColor);
            }
            else
            {
                // Ściana z otworami — dzielona na segmenty
                BuildWallWithOpenings(seg, parent, dir, length);
            }
        }

        private void CreateWallBox(GameObject parent, Vector3 center, Vector3 dir, float length, float height, Color color, bool isChild = false)
        {
            // Zawsze tworzymy Cube jako dziecko parent — prostsze i niezawodne
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = isChild ? "WallPart" : "WallBody";
            box.transform.SetParent(parent.transform, true);
            try { box.tag = "Wall"; } catch { /* Tag niezdefiniowany */ }

            // Orientacja: scale Z = długość ściany (wzdłuż dir), X = grubość
            float angle = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            box.transform.position = new Vector3(center.x, height / 2f, center.z);
            box.transform.rotation = Quaternion.Euler(0, angle, 0);
            box.transform.localScale = new Vector3(wallThickness, height, length);

            // Material
            var renderer = box.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                var mat = MaterialFactory.CreateLit();
                MaterialFactory.SetBaseColor(mat, color);
                renderer.material = mat;
            }
        }

        private void BuildWallWithOpenings(WallSegment seg, GameObject parent, Vector3 dir, float totalLength)
        {
            // Sortuj otwory po pozycji
            var sorted = new List<WallOpening>(seg.openings);
            sorted.Sort((a, b) => a.distanceOnWall.CompareTo(b.distanceOnWall));

            float cursor = 0f;

            foreach (var opening in sorted)
            {
                float openingWidth = GetOpeningWidth(opening.type);
                float openingCenter = opening.distanceOnWall; // już w metrach
                float openingStart = openingCenter - openingWidth / 2f;
                float openingEnd = openingCenter + openingWidth / 2f;

                openingStart = Mathf.Max(openingStart, 0f);
                openingEnd = Mathf.Min(openingEnd, totalLength);

                // Segment ściany przed otworem
                if (openingStart > cursor + 0.01f)
                {
                    float segLen = openingStart - cursor;
                    float segCenter = cursor + segLen / 2f;
                    Vector3 pos = seg.startPos + dir * segCenter;
                    CreateWallBox(parent, pos, dir, segLen, seg.height, wallColor, true);
                }

                // Nad otworem (nadproże)
                if (opening.type == OpeningType.Door)
                {
                    float lintelHeight = seg.height - doorHeight;
                    if (lintelHeight > 0.05f)
                    {
                        float lintelY = doorHeight + lintelHeight / 2f;
                        Vector3 pos = seg.startPos + dir * openingCenter;
                        pos.y = lintelY;
                        var lintel = CreateChildBox(parent, pos, dir, openingWidth, lintelHeight, wallColor);
                    }

                    // Framuga drzwi
                    CreateDoorFrame(parent, seg.startPos + dir * openingCenter, dir, openingWidth, doorHeight);
                }
                else if (opening.type == OpeningType.TrackGate)
                {
                    // MM-15: brama wjazdowa = pełna wysokość, brak nadproża, brak collidera
                    // (pojazdy + tory mogą przejechać). Gracz fizycznie widzi "przerwę" w ścianie.
                    // Ramka wizualna (brak collidera) — tylko wskaźnik dla gracza.
                    CreateTrackGateFrame(parent, seg.startPos + dir * openingCenter, dir, openingWidth, seg.height);
                }
                else // Window
                {
                    // Pod oknem
                    if (windowBottomOffset > 0.05f)
                    {
                        Vector3 pos = seg.startPos + dir * openingCenter;
                        pos.y = windowBottomOffset / 2f;
                        CreateChildBox(parent, pos, dir, openingWidth, windowBottomOffset, wallColor);
                    }

                    // Nad oknem
                    float windowTop = windowBottomOffset + windowHeight;
                    float aboveHeight = seg.height - windowTop;
                    if (aboveHeight > 0.05f)
                    {
                        Vector3 pos = seg.startPos + dir * openingCenter;
                        pos.y = windowTop + aboveHeight / 2f;
                        CreateChildBox(parent, pos, dir, openingWidth, aboveHeight, wallColor);
                    }

                    // Szyba (półprzezroczysta)
                    CreateWindowGlass(parent, seg.startPos + dir * openingCenter, dir, openingWidth, windowHeight, windowBottomOffset);
                }

                cursor = openingEnd;
            }

            // Segment ściany po ostatnim otworze
            if (cursor < totalLength - 0.01f)
            {
                float segLen = totalLength - cursor;
                float segCenter = cursor + segLen / 2f;
                Vector3 pos = seg.startPos + dir * segCenter;
                CreateWallBox(parent, pos, dir, segLen, seg.height, wallColor, true);
            }

            // Każdy child Cube ma własny BoxCollider — parent nie potrzebuje osobnego
        }

        private GameObject CreateChildBox(GameObject parent, Vector3 worldPos, Vector3 dir, float width, float height, Color color)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.transform.SetParent(parent.transform, true);
            // BUG-032: tag "Wall" zarejestrowany w ProjectSettings/TagManager.asset
            // (używany przez TrackBuildStateMachine + TurnoutPlacementStateMachine collision check).
            // Wcześniej był silenced try/catch — niepotrzebne.
            box.tag = "Wall";
            float angle = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            box.transform.position = new Vector3(worldPos.x, worldPos.y == 0 ? height / 2f : worldPos.y, worldPos.z);
            box.transform.rotation = Quaternion.Euler(0, angle, 0);
            box.transform.localScale = new Vector3(wallThickness, height, width);

            var renderer = box.GetComponent<MeshRenderer>();
            var childMat = MaterialFactory.CreateLit();
            MaterialFactory.SetBaseColor(childMat, color);
            renderer.material = childMat;
            return box;
        }

        private void CreateDoorFrame(GameObject parent, Vector3 worldPos, Vector3 dir, float width, float height)
        {
            float frameThick = 0.05f;
            float angle = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            Vector3 perp = new Vector3(-dir.z, 0, dir.x); // perpendicular

            // Lewy słupek
            var left = GameObject.CreatePrimitive(PrimitiveType.Cube);
            left.transform.SetParent(parent.transform, true);
            left.transform.position = worldPos - dir * (width / 2f) + Vector3.up * (height / 2f);
            left.transform.rotation = Quaternion.Euler(0, angle, 0);
            left.transform.localScale = new Vector3(wallThickness + 0.02f, height, frameThick);
            var leftMat = MaterialFactory.CreateLit();
            MaterialFactory.SetBaseColor(leftMat, doorFrameColor);
            left.GetComponent<MeshRenderer>().material = leftMat;
            var leftCol = left.GetComponent<BoxCollider>();
            if (leftCol != null) Destroy(leftCol);

            // Prawy słupek
            var right = GameObject.CreatePrimitive(PrimitiveType.Cube);
            right.transform.SetParent(parent.transform, true);
            right.transform.position = worldPos + dir * (width / 2f) + Vector3.up * (height / 2f);
            right.transform.rotation = Quaternion.Euler(0, angle, 0);
            right.transform.localScale = new Vector3(wallThickness + 0.02f, height, frameThick);
            var rightMat = MaterialFactory.CreateLit();
            MaterialFactory.SetBaseColor(rightMat, doorFrameColor);
            right.GetComponent<MeshRenderer>().material = rightMat;
            var rightCol = right.GetComponent<BoxCollider>();
            if (rightCol != null) Destroy(rightCol);

            // Górna belka
            var top = GameObject.CreatePrimitive(PrimitiveType.Cube);
            top.transform.SetParent(parent.transform, true);
            top.transform.position = worldPos + Vector3.up * height;
            top.transform.rotation = Quaternion.Euler(0, angle, 0);
            top.transform.localScale = new Vector3(wallThickness + 0.02f, frameThick, width);
            var topMat = MaterialFactory.CreateLit();
            MaterialFactory.SetBaseColor(topMat, doorFrameColor);
            top.GetComponent<MeshRenderer>().material = topMat;
            var topCol = top.GetComponent<BoxCollider>();
            if (topCol != null) Destroy(topCol);
        }

        /// <summary>MM-15: ramka bramy wjazdowej (TrackGate). Tylko 2 słupki boczne (brak nadproża),
        /// brak collidera — tor + pojazd przejeżdżają. Pełna wysokość ściany. Kolor jak doorFrameColor.</summary>
        private void CreateTrackGateFrame(GameObject parent, Vector3 worldPos, Vector3 dir, float width, float height)
        {
            float frameThick = 0.08f;  // grubsze niż drzwi (industrial)
            float angle = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;

            // Lewy słupek (tylko visual, brak collidera)
            var left = GameObject.CreatePrimitive(PrimitiveType.Cube);
            left.name = "TrackGateLeft";
            left.transform.SetParent(parent.transform, true);
            left.transform.position = worldPos - dir * (width / 2f) + Vector3.up * (height / 2f);
            left.transform.rotation = Quaternion.Euler(0, angle, 0);
            left.transform.localScale = new Vector3(wallThickness + 0.04f, height, frameThick);
            var leftMat = MaterialFactory.CreateLit();
            MaterialFactory.SetBaseColor(leftMat, doorFrameColor);
            left.GetComponent<MeshRenderer>().material = leftMat;
            var leftCol = left.GetComponent<BoxCollider>();
            if (leftCol != null) Destroy(leftCol);

            // Prawy słupek
            var right = GameObject.CreatePrimitive(PrimitiveType.Cube);
            right.name = "TrackGateRight";
            right.transform.SetParent(parent.transform, true);
            right.transform.position = worldPos + dir * (width / 2f) + Vector3.up * (height / 2f);
            right.transform.rotation = Quaternion.Euler(0, angle, 0);
            right.transform.localScale = new Vector3(wallThickness + 0.04f, height, frameThick);
            var rightMat = MaterialFactory.CreateLit();
            MaterialFactory.SetBaseColor(rightMat, doorFrameColor);
            right.GetComponent<MeshRenderer>().material = rightMat;
            var rightCol = right.GetComponent<BoxCollider>();
            if (rightCol != null) Destroy(rightCol);

            // BRAK górnej belki — pojazd musi przejechać. Brak nadproża to świadoma decyzja MM-15
            // (gdyby były nadproża, locomotywa z pantografem nie wjechałaby — pozostawiamy "open sky").
        }

        private void CreateWindowGlass(GameObject parent, Vector3 worldPos, Vector3 dir, float width, float height, float bottomY)
        {
            var glass = GameObject.CreatePrimitive(PrimitiveType.Cube);
            glass.transform.SetParent(parent.transform, true);
            float angle = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            glass.transform.position = worldPos + Vector3.up * (bottomY + height / 2f);
            glass.transform.rotation = Quaternion.Euler(0, angle, 0);
            glass.transform.localScale = new Vector3(wallThickness * 0.5f, height, width);

            var renderer = glass.GetComponent<MeshRenderer>();
            var mat = MaterialFactory.CreateLit();
            MaterialFactory.SetTransparent(mat);
            MaterialFactory.SetBaseColor(mat, windowGlassColor);
            renderer.material = mat;

            var col = glass.GetComponent<BoxCollider>();
            if (col != null) Destroy(col);
        }
    }
}
