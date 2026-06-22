using UnityEngine;
using UnityEngine.InputSystem;

namespace DepotSystem
{
    public partial class WallBuildingSystem
    {
        // ═══════════════════════════════════════════
        //  OTWORY — drzwi / okna
        // ═══════════════════════════════════════════

        private void HandleOpeningPlacement(OpeningType type)
        {
            if (DepotUIManager.Instance.IsPointerOverUI()) { HideOpeningPreview(); return; }

            // ESC / RMB → powrót do Select (Cancel action)
            if (_toolBuild.Cancel.WasPressedThisFrame())
            {
                DepotUIManager.Instance.CurrentTool = ToolMode.Select;
                HideOpeningPreview();
                return;
            }

            // Raycast na ścianę
            if (Mouse.current == null) return;
            Ray ray = mainCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
            if (Physics.Raycast(ray, out RaycastHit hit, 500f))
            {
                var wall = FindWallByHit(hit);

                if (wall != null)
                {
                    hoveredWall = wall;
                    float dist = GetDistanceOnWall(wall, hit.point);
                    float openingW = GetOpeningWidth(type);
                    dist = SnapDistanceToGrid(dist, openingW, wall.Length);

                    bool valid = IsOpeningValid(wall, dist, type);
                    ShowOpeningPreview(wall, dist, type, valid);

                    if (_toolBuild.Primary.WasPressedThisFrame() && valid)
                    {
                        AddOpening(wall, dist, type);
                    }
                }
                else
                {
                    hoveredWall = null;
                    HideOpeningPreview();
                }
            }
            else
            {
                hoveredWall = null;
                HideOpeningPreview();
            }
        }

        private bool IsOpeningValid(WallSegment wall, float dist, OpeningType type)
        {
            float openingW = GetOpeningWidth(type);
            float halfW = openingW / 2f;
            float length = wall.Length;

            if (dist - halfW < 0.1f || dist + halfW > length - 0.1f) return false;

            foreach (var existing in wall.openings)
            {
                float existW = GetOpeningWidth(existing.type);
                float minDist = (openingW + existW) / 2f + 0.1f;
                if (Mathf.Abs(dist - existing.distanceOnWall) < minDist)
                    return false;
            }

            return true;
        }

        private void AddOpening(WallSegment wall, float dist, OpeningType type)
        {
            if (!IsOpeningValid(wall, dist, type)) return;

            var opening = new WallOpening
            {
                openingId = nextOpeningId++,
                type = type,
                distanceOnWall = dist
            };

            wall.openings.Add(opening);
            RegenerateWallMesh(wall);
            OnWallsChanged?.Invoke();
        }

        /// <summary>Odległość punktu trafienia od startPos wzdłuż ściany (w metrach).</summary>
        private float GetDistanceOnWall(WallSegment wall, Vector3 hitPoint)
        {
            Vector3 dir = wall.endPos - wall.startPos;
            float length = dir.magnitude;
            if (length < 0.01f) return 0f;

            Vector3 local = hitPoint - wall.startPos;
            float dot = Vector3.Dot(local, dir.normalized);
            return Mathf.Clamp(dot, 0f, length);
        }

        /// <summary>Snap pozycji otworu do siatki, z uwzględnieniem granic ściany.</summary>
        private float SnapDistanceToGrid(float dist, float openingWidth, float wallLength)
        {
            float snapped = Mathf.Round(dist / gridSize) * gridSize;
            float halfW = openingWidth / 2f;
            // Clamp żeby otwór nie wystawał poza ścianę
            snapped = Mathf.Clamp(snapped, halfW + 0.1f, wallLength - halfW - 0.1f);
            return snapped;
        }

        private void ShowOpeningPreview(WallSegment wall, float dist, OpeningType type, bool valid)
        {
            if (openingPreview == null) return;

            float width = GetOpeningWidth(type);
            // MM-15: TrackGate ma pełną wysokość ściany (przejazdowa, brak nadproża dla pojazdów)
            float height = type switch
            {
                OpeningType.Door => doorHeight,
                OpeningType.Window => windowHeight,
                OpeningType.TrackGate => wall.height,
                _ => doorHeight,
            };
            float bottomY = type == OpeningType.Window ? windowBottomOffset : 0f;

            Vector3 worldPos = wall.startPos + wall.Direction * dist;
            worldPos.y = bottomY + height / 2f;

            float angle = Mathf.Atan2(wall.Direction.x, wall.Direction.z) * Mathf.Rad2Deg;

            openingPreview.SetActive(true);
            openingPreview.transform.position = worldPos;
            openingPreview.transform.rotation = Quaternion.Euler(0, angle, 0);
            openingPreview.transform.localScale = new Vector3(wallThickness + 0.1f, height, width);

            var mr = openingPreview.GetComponent<MeshRenderer>();
            if (mr != null)
                mr.material.color = valid
                    ? new Color(0.3f, 1f, 0.3f, 0.5f)
                    : new Color(1f, 0.2f, 0.2f, 0.6f);
        }

        private void HideOpeningPreview()
        {
            if (openingPreview != null)
                openingPreview.SetActive(false);
            hoveredWall = null;
        }

        // ═══════════════════════════════════════════
        //  PUBLIC API dla DoorPlacer (MF-11)
        // ═══════════════════════════════════════════

        /// <summary>MF-11: szuka najbliższej ściany do world position w 2D (XZ plane).
        /// Zwraca null gdy żadna ściana w promieniu maxDistance.</summary>
        public WallSegment FindClosestWall(Vector3 worldPos, float maxDistance)
        {
            WallSegment closest = null;
            float minDist = maxDistance;
            foreach (var w in allWalls)
            {
                if (w == null) continue;
                float dist = ClosestDistance2D(worldPos, w.startPos, w.endPos);
                if (dist < minDist) { minDist = dist; closest = w; }
            }
            return closest;
        }

        /// <summary>MF-11: oblicza distance pivot-on-wall (od startPos wzdłuż ściany) dla world point.</summary>
        public float ComputeDistanceOnWall(WallSegment wall, Vector3 worldPos)
            => GetDistanceOnWall(wall, worldPos);

        /// <summary>MF-11: snap dystansu do siatki + clamp żeby drzwi mieściły się w ścianie.</summary>
        public float SnapDoorDistance(WallSegment wall, float rawDist)
            => SnapDistanceToGrid(rawDist, doorWidth, wall.Length);

        /// <summary>MF-11: walidacja czy w danym dystansie można postawić drzwi (kolizja z innymi opening, granice ściany).</summary>
        public bool IsDoorPlacementValid(WallSegment wall, float distOnWall)
        {
            if (wall == null) return false;
            return IsOpeningValid(wall, distOnWall, OpeningType.Door);
        }

        /// <summary>MF-11: tworzy drzwi w ścianie (cuts hole through RegenerateWallMesh + emits OnWallsChanged).
        /// Zwraca true gdy się udało.</summary>
        public bool TryAddDoorOpening(WallSegment wall, float distOnWall)
        {
            if (!IsDoorPlacementValid(wall, distOnWall)) return false;
            AddOpening(wall, distOnWall, OpeningType.Door);
            return true;
        }

        // ═══════════════════════════════════════════
        //  MM-15 PUBLIC API dla TrackGate (brama wjazdowa)
        // ═══════════════════════════════════════════

        /// <summary>MM-15: snap dystansu do siatki + clamp dla bramy wjazdowej (4m wide).</summary>
        public float SnapTrackGateDistance(WallSegment wall, float rawDist)
            => SnapDistanceToGrid(rawDist, trackGateWidth, wall.Length);

        /// <summary>MM-15: walidacja czy w danym dystansie można postawić bramę wjazdową
        /// (kolizja z innymi opening, granice ściany).</summary>
        public bool IsTrackGatePlacementValid(WallSegment wall, float distOnWall)
        {
            if (wall == null) return false;
            // Track gate wymaga długości ściany ≥ 4m (trackGateWidth + margin)
            if (wall.Length < trackGateWidth + 0.2f) return false;
            return IsOpeningValid(wall, distOnWall, OpeningType.TrackGate);
        }

        /// <summary>MM-15: tworzy bramę wjazdową w ścianie (cuts hole through RegenerateWallMesh
        /// + emits OnWallsChanged). Brama jest 4m szeroka i sięga pełnej wysokości ściany —
        /// pojazdy mogą przejeżdżać. Zwraca true gdy się udało.</summary>
        public bool TryAddTrackGateOpening(WallSegment wall, float distOnWall)
        {
            if (!IsTrackGatePlacementValid(wall, distOnWall)) return false;
            AddOpening(wall, distOnWall, OpeningType.TrackGate);
            return true;
        }

        /// <summary>MM-15: szerokość bramy wjazdowej w metrach (read-only dla DoorPlacer/UI).</summary>
        public float TrackGateWidth => trackGateWidth;

        /// <summary>Zamknięty 2D distance (XZ plane) point-to-segment.</summary>
        private static float ClosestDistance2D(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector2 p2 = new Vector2(p.x, p.z);
            Vector2 a2 = new Vector2(a.x, a.z);
            Vector2 b2 = new Vector2(b.x, b.z);
            Vector2 ab = b2 - a2;
            float lenSq = ab.sqrMagnitude;
            if (lenSq < 0.0001f) return Vector2.Distance(p2, a2);
            float t = Mathf.Clamp01(Vector2.Dot(p2 - a2, ab) / lenSq);
            Vector2 closest = a2 + t * ab;
            return Vector2.Distance(p2, closest);
        }
    }
}
