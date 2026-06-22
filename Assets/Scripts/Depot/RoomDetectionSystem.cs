using System.Collections.Generic;
using UnityEngine;
using RailwayManager.Core;
using RailwayManager.Core.Rendering;

namespace DepotSystem
{
    /// <summary>
    /// Typ pomieszczenia w zajezdni.
    /// </summary>
    public enum RoomType
    {
        None,
        Hall,               // Hala (warsztat/myjnia)
        Storage,            // Magazyn
        Dispatcher,         // Dyspozytor
        Office,             // Biuro
        Social,             // Socjalny
        Supervisor,         // Naczelnik
        Bathroom,           // Łazienka
        Locker,             // Szatnia
        Corridor,           // Korytarz
        TrafficController   // Dyżurny ruchu
    }

    /// <summary>
    /// Dane wykrytego pomieszczenia.
    /// </summary>
    [System.Serializable]
    public class DetectedRoom
    {
        public int roomId;
        public RoomType roomType = RoomType.None;
        public List<Vector2Int> cells = new();
        public float areaSqM;
        public RectInt bounds;
        public GameObject floorObject;

        /// <summary>Identyfikator budynku (grupy ścian) do którego należy pokój.</summary>
        public int buildingId = -1;

        /// <summary>
        /// MF-6/MF-11 — cells gdzie postawiono drzwi (passable connection do innego pokoju
        /// lub na zewnątrz). Furniture placement validator (MF-6) sprawdza brak kolizji
        /// z door cells. Drzwi dodawane przez DoorPlacer w MF-11.
        /// W MF-6 zawsze pusta lista (żaden mechanizm jeszcze drzwi nie dodaje).
        /// </summary>
        public List<Vector2Int> doorCells = new();

        /// <summary>
        /// MM-1 — aktualny lvl pokoju (1-5). Domyślny <c>1</c> dla nowo wykrytych pokoi
        /// (decyzja MM-D2: brak downgrade). Dla typów nielvlable (None/Storage/Locker/Corridor)
        /// pole jest ignorowane przez RoomLevelService.
        ///
        /// Awansowane przez explicit klik gracza (MM-D1/D10) gdy spełnione wymagania
        /// w <see cref="DepotSystem.RoomLevel.RoomLevelCatalog"/>. Bonus konsumowany przez:
        /// - WorkshopManager (Hall lvl → P-poziom inspection)
        /// - OfficeService (Office lvl → cap biurowych + R&D speed)
        /// - DispatcherService (Dispatcher lvl → onboarding speed)
        /// - TrafficControlService (TrafficController lvl → cap headcount)
        /// - MoraleService (Supervisor/Social/Bathroom lvl → morale bonusy)
        /// </summary>
        public int level = 1;
    }

    /// <summary>
    /// Minimalne wymagania rozmiaru dla danego typu pomieszczenia.
    /// </summary>
    public static class RoomRequirements
    {
        public static readonly Dictionary<RoomType, (float minWidth, float minDepth, string label)> MinSize = new()
        {
            { RoomType.Hall,              (10f, 5f,  "Hala") },
            { RoomType.Storage,           (3f,  2f,  "Magazyn") },
            { RoomType.Dispatcher,        (3f,  3f,  "Dyspozytor") },
            { RoomType.Office,            (3f,  3f,  "Biuro") },
            { RoomType.Social,            (3f,  2f,  "Socjalny") },
            { RoomType.Supervisor,        (3f,  3f,  "Naczelnik") },
            { RoomType.Bathroom,          (2f,  2f,  "Łazienka") },
            { RoomType.Locker,            (2f,  2f,  "Szatnia") },
            { RoomType.Corridor,          (1f,  1f,  "Korytarz") },
            { RoomType.TrafficController, (4f,  3f,  "Dyżurny ruchu") },
        };

        /// <summary>Kolory podłóg wg typu.</summary>
        public static readonly Dictionary<RoomType, Color> FloorColors = new()
        {
            { RoomType.None,              new Color(0.5f, 0.5f, 0.5f, 0.6f) },
            { RoomType.Hall,              new Color(0.6f, 0.5f, 0.3f, 0.6f) },
            { RoomType.Storage,           new Color(0.5f, 0.4f, 0.3f, 0.6f) },
            { RoomType.Dispatcher,        new Color(0.4f, 0.4f, 0.6f, 0.6f) },
            { RoomType.Office,            new Color(0.3f, 0.6f, 0.4f, 0.6f) },
            { RoomType.Social,            new Color(0.6f, 0.6f, 0.3f, 0.6f) },
            { RoomType.Supervisor,        new Color(0.5f, 0.3f, 0.6f, 0.6f) },
            { RoomType.Bathroom,          new Color(0.3f, 0.6f, 0.6f, 0.6f) },
            { RoomType.Locker,            new Color(0.5f, 0.5f, 0.4f, 0.6f) },
            { RoomType.Corridor,          new Color(0.4f, 0.4f, 0.4f, 0.6f) },
            { RoomType.TrafficController, new Color(0.3f, 0.5f, 0.7f, 0.6f) },
        };
    }

    /// <summary>
    /// Wykrywa zamknięte pomieszczenia na podstawie ścian (flood-fill na siatce 1x1m).
    /// Generuje podłogi wewnątrz zamkniętych pokojów.
    /// </summary>
    public class RoomDetectionSystem : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] private float gridSize = 1f;
        [SerializeField] private float floorY = 0.01f;

        private WallBuildingSystem wallSystem;
        private List<DetectedRoom> rooms = new();
        private int nextRoomId = 1;
        private GameObject roomsParent;

        /// <summary>Wywoływany gdy lista pokojów się zmieni.</summary>
        public event System.Action OnRoomsChanged;

        /// <summary>Wywoływany gdy wykryto nowy pokój (roomType == None) — do pokazania popup.</summary>
        public event System.Action<DetectedRoom> OnNewRoomDetected;

        public IReadOnlyList<DetectedRoom> Rooms => rooms;

        // ═══════════════════════════════════════════
        //  SAVE/LOAD API (zamiast reflection w DepotSavable)
        // ═══════════════════════════════════════════

        /// <summary>
        /// Bulk replace pokoi + counter z save'a. Public API zamiast reflection na private
        /// fieldy (`rooms`, `nextRoomId`).
        /// </summary>
        public void RestoreFromSave(IList<DetectedRoom> roomsIn, int nextRoomIdIn)
        {
            rooms.Clear();
            if (roomsIn != null) rooms.AddRange(roomsIn);
            nextRoomId = nextRoomIdIn > 0 ? nextRoomIdIn : 1;
            OnRoomsChanged?.Invoke();
        }

        /// <summary>Reset pokoi (jak nowa gra).</summary>
        public void ClearAllForReset()
        {
            rooms.Clear();
            nextRoomId = 1;
            OnRoomsChanged?.Invoke();
        }

        void Start()
        {
            wallSystem = DepotServices.Get<WallBuildingSystem>();
            if (wallSystem != null)
                wallSystem.OnWallsChanged += OnWallsChanged;

            roomsParent = new GameObject("DetectedRooms");
        }

        void OnDestroy()
        {
            if (wallSystem != null)
                wallSystem.OnWallsChanged -= OnWallsChanged;
        }

        private void OnWallsChanged()
        {
            DetectRooms();
        }

        /// <summary>
        /// Główna metoda wykrywania pokojów.
        /// Podejście: każdy buildingId (4 ściany = 1 prostokąt) to 1 pokój z podłogą.
        /// Wewnętrzne ściany (inny buildingId wewnątrz) tworzą osobne pokoje.
        /// </summary>
        [ContextMenu("Detect Rooms")]
        public void DetectRooms()
        {
            var walls = wallSystem != null ? wallSystem.AllWalls : null;
            if (walls == null || walls.Count == 0)
            {
                ClearAllRooms();
                return;
            }

            // 1. Zgrupuj ściany wg buildingId
            var buildingWalls = new Dictionary<int, List<WallSegment>>();
            foreach (var w in walls)
            {
                if (w.buildingId < 0) continue;
                if (!buildingWalls.ContainsKey(w.buildingId))
                    buildingWalls[w.buildingId] = new List<WallSegment>();
                buildingWalls[w.buildingId].Add(w);
            }

            // 2. Dla każdego buildingId oblicz bounds i utwórz pokój
            var oldRooms = new List<DetectedRoom>(rooms);
            ClearAllRooms();

            foreach (var kvp in buildingWalls)
            {
                int buildingId = kvp.Key;
                var bWalls = kvp.Value;

                // Oblicz bounds z pozycji ścian
                float bMinX = float.MaxValue, bMaxX = float.MinValue;
                float bMinZ = float.MaxValue, bMaxZ = float.MinValue;
                foreach (var w in bWalls)
                {
                    bMinX = Mathf.Min(bMinX, w.startPos.x, w.endPos.x);
                    bMaxX = Mathf.Max(bMaxX, w.startPos.x, w.endPos.x);
                    bMinZ = Mathf.Min(bMinZ, w.startPos.z, w.endPos.z);
                    bMaxZ = Mathf.Max(bMaxZ, w.startPos.z, w.endPos.z);
                }

                float roomW = bMaxX - bMinX;
                float roomH = bMaxZ - bMinZ;
                if (roomW < 1f || roomH < 1f) continue;

                // Szukaj istniejącego pokoju z tym samym buildingId
                var existing = oldRooms.Find(r => r.buildingId == buildingId);

                var room = new DetectedRoom
                {
                    roomId = existing != null ? existing.roomId : nextRoomId++,
                    roomType = existing != null ? existing.roomType : RoomType.None,
                    cells = new List<Vector2Int>(), // nie używane bezpośrednio
                    areaSqM = roomW * roomH,
                    bounds = new RectInt(
                        Mathf.RoundToInt(bMinX / gridSize),
                        Mathf.RoundToInt(bMinZ / gridSize),
                        Mathf.RoundToInt(roomW / gridSize),
                        Mathf.RoundToInt(roomH / gridSize)
                    ),
                    buildingId = buildingId
                };

                rooms.Add(room);
                room.floorObject = CreateFloorMesh(room);

                // Auto-assign typu z aktualnego BuildRoom sub-mode (decyzja 2026-05-03):
                // gracz świadomie wszedł w "Buduj X" sub-menu, więc popup wyboru typu jest
                // redundantny. Auto-assign tylko gdy pokój wystarczająco duży dla danego typu.
                // Fallback do popup'u (OnNewRoomDetected) gdy:
                //  - pokój istniał wcześniej (zachowuje stary typ)
                //  - tool aktywny nie jest BuildRoom (np. ściana usunięta i powstał nowy pokój)
                //  - sub-mode nie mapuje się na konkretny RoomType
                //  - pokój jest za mały dla sub-mode'a
                if (room.roomType == RoomType.None)
                {
                    RoomType autoType = ResolveAutoRoomType(room);
                    // M-Economy Faza 5: auto-typ = budowa pokoju → pobierz koszt. Nie stać → fallback do
                    // popupu (pokój zostaje None, gracz wybierze/zapłaci później przez SetRoomType).
                    if (autoType != RoomType.None && TryApplyRoomCostDelta(room, autoType))
                    {
                        room.roomType = autoType;
                        UpdateFloorColor(room);
                    }
                    else
                    {
                        OnNewRoomDetected?.Invoke(room);
                    }
                }
            }

            OnRoomsChanged?.Invoke();
        }

        /// <summary>
        /// Próbuje określić auto-typ dla nowego pokoju z aktualnego sub-mode BuildRoom.
        /// Zwraca RoomType.None gdy auto-assign nie ma sensu (gracz wybiera ręcznie przez popup).
        /// </summary>
        private RoomType ResolveAutoRoomType(DetectedRoom room)
        {
            if (DepotUIManager.Instance == null) return RoomType.None;
            if (DepotUIManager.Instance.CurrentTool != ToolMode.BuildRoom) return RoomType.None;

            RoomType candidate = MapSubModeToRoomType(DepotUIManager.Instance.CurrentRoomSubMode);
            if (candidate == RoomType.None) return RoomType.None;

            // Walidacja size — auto-assign tylko gdy pokój wystarczająco duży
            if (!IsRoomLargeEnough(room, candidate)) return RoomType.None;

            return candidate;
        }

        private static RoomType MapSubModeToRoomType(RoomBuildSubMode mode) => mode switch
        {
            RoomBuildSubMode.Hall => RoomType.Hall,
            RoomBuildSubMode.Storage => RoomType.Storage,
            RoomBuildSubMode.Dispatcher => RoomType.Dispatcher,
            RoomBuildSubMode.Office => RoomType.Office,
            RoomBuildSubMode.Social => RoomType.Social,
            RoomBuildSubMode.Supervisor => RoomType.Supervisor,
            RoomBuildSubMode.Bathroom => RoomType.Bathroom,
            RoomBuildSubMode.Locker => RoomType.Locker,
            RoomBuildSubMode.Corridor => RoomType.Corridor,
            RoomBuildSubMode.TrafficController => RoomType.TrafficController,
            _ => RoomType.None
        };

        // ─── Grid operations ───

        private void MarkWallCells(bool[,] grid, IReadOnlyList<WallSegment> walls,
            int gridMinX, int gridMinZ, int width, int height)
        {
            foreach (var wall in walls)
            {
                // Rasteryzuj ścianę na grid
                Vector3 start = wall.startPos;
                Vector3 end = wall.endPos;
                float length = wall.Length;
                Vector3 dir = wall.Direction;

                int steps = Mathf.CeilToInt(length / (gridSize * 0.5f));
                for (int i = 0; i <= steps; i++)
                {
                    float t = (float)i / steps;
                    Vector3 point = Vector3.Lerp(start, end, t);

                    int gx = Mathf.RoundToInt(point.x / gridSize) - gridMinX;
                    int gz = Mathf.RoundToInt(point.z / gridSize) - gridMinZ;

                    if (gx >= 0 && gx < width && gz >= 0 && gz < height)
                        grid[gx, gz] = true;
                }
            }
        }

        private void FloodFillOutside(bool[,] wallGrid, bool[,] outside, int width, int height)
        {
            var queue = new Queue<Vector2Int>();

            // Dodaj krawędzie do kolejki
            for (int x = 0; x < width; x++)
            {
                TryEnqueue(queue, outside, wallGrid, x, 0, width, height);
                TryEnqueue(queue, outside, wallGrid, x, height - 1, width, height);
            }
            for (int z = 0; z < height; z++)
            {
                TryEnqueue(queue, outside, wallGrid, 0, z, width, height);
                TryEnqueue(queue, outside, wallGrid, width - 1, z, width, height);
            }

            // BFS
            while (queue.Count > 0)
            {
                var cell = queue.Dequeue();
                TryEnqueue(queue, outside, wallGrid, cell.x + 1, cell.y, width, height);
                TryEnqueue(queue, outside, wallGrid, cell.x - 1, cell.y, width, height);
                TryEnqueue(queue, outside, wallGrid, cell.x, cell.y + 1, width, height);
                TryEnqueue(queue, outside, wallGrid, cell.x, cell.y - 1, width, height);
            }
        }

        private void TryEnqueue(Queue<Vector2Int> queue, bool[,] outside, bool[,] wallGrid,
            int x, int z, int width, int height)
        {
            if (x < 0 || x >= width || z < 0 || z >= height) return;
            if (outside[x, z] || wallGrid[x, z]) return;

            outside[x, z] = true;
            queue.Enqueue(new Vector2Int(x, z));
        }

        private List<Vector2Int> FloodFillRoom(bool[,] wallGrid, bool[,] visited,
            int startX, int startZ, int width, int height)
        {
            var cells = new List<Vector2Int>();
            var queue = new Queue<Vector2Int>();

            visited[startX, startZ] = true;
            queue.Enqueue(new Vector2Int(startX, startZ));

            while (queue.Count > 0)
            {
                var cell = queue.Dequeue();
                cells.Add(cell);

                TryEnqueueRoom(queue, wallGrid, visited, cell.x + 1, cell.y, width, height);
                TryEnqueueRoom(queue, wallGrid, visited, cell.x - 1, cell.y, width, height);
                TryEnqueueRoom(queue, wallGrid, visited, cell.x, cell.y + 1, width, height);
                TryEnqueueRoom(queue, wallGrid, visited, cell.x, cell.y - 1, width, height);
            }

            return cells;
        }

        private void TryEnqueueRoom(Queue<Vector2Int> queue, bool[,] wallGrid, bool[,] visited,
            int x, int z, int width, int height)
        {
            if (x < 0 || x >= width || z < 0 || z >= height) return;
            if (visited[x, z] || wallGrid[x, z]) return;

            visited[x, z] = true;
            queue.Enqueue(new Vector2Int(x, z));
        }

        // ─── Floor mesh ───

        private GameObject CreateFloorMesh(DetectedRoom room)
        {
            if (room.areaSqM < 1f) return null;

            var floorObj = new GameObject($"RoomFloor_{room.roomId}");
            floorObj.transform.SetParent(roomsParent.transform, false);
            floorObj.layer = LayerMask.NameToLayer("Default");

            // Oblicz podłogę z pozycji ścian budynku (nie z flood-fill bounds)
            float floorX, floorZ, floorW, floorH;
            if (room.buildingId >= 0 && wallSystem != null)
            {
                // Znajdź min/max pozycji ścian tego budynku
                float wMinX = float.MaxValue, wMaxX = float.MinValue;
                float wMinZ = float.MaxValue, wMaxZ = float.MinValue;
                foreach (var wall in wallSystem.AllWalls)
                {
                    if (wall.buildingId != room.buildingId) continue;
                    wMinX = Mathf.Min(wMinX, wall.startPos.x, wall.endPos.x);
                    wMaxX = Mathf.Max(wMaxX, wall.startPos.x, wall.endPos.x);
                    wMinZ = Mathf.Min(wMinZ, wall.startPos.z, wall.endPos.z);
                    wMaxZ = Mathf.Max(wMaxZ, wall.startPos.z, wall.endPos.z);
                }
                // Podłoga = wewnątrz ścian (od krawędzi do krawędzi, ściany mają grubość 0.2m
                // ale pozycje ścian to linie środkowe — podłoga sięga do tych linii)
                floorX = wMinX;
                floorZ = wMinZ;
                floorW = wMaxX - wMinX;
                floorH = wMaxZ - wMinZ;
            }
            else
            {
                // Fallback na flood-fill bounds
                floorX = room.bounds.x * gridSize;
                floorZ = room.bounds.y * gridSize;
                floorW = room.bounds.width * gridSize;
                floorH = room.bounds.height * gridSize;
            }

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.transform.SetParent(floorObj.transform, false);
            quad.transform.position = new Vector3(floorX + floorW / 2f, floorY, floorZ + floorH / 2f);
            quad.transform.rotation = Quaternion.Euler(90, 0, 0);
            quad.transform.localScale = new Vector3(floorW, floorH, 1f);

            var renderer = quad.GetComponent<MeshRenderer>();
            Color floorColor = RoomRequirements.FloorColors.GetValueOrDefault(room.roomType, Color.gray);
            var mat = MaterialFactory.CreateLine();
            MaterialFactory.SetBaseColor(mat, floorColor);
            renderer.material = mat;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // Collider do klikania
            var col = quad.GetComponent<MeshCollider>();
            if (col == null) col = quad.AddComponent<MeshCollider>();

            return floorObj;
        }

        private void UpdateRoomBoundsFromWalls(DetectedRoom room, IReadOnlyList<WallSegment> walls)
        {
            if (room.buildingId < 0) return;

            float wMinX = float.MaxValue, wMaxX = float.MinValue;
            float wMinZ = float.MaxValue, wMaxZ = float.MinValue;
            bool found = false;

            foreach (var wall in walls)
            {
                if (wall.buildingId != room.buildingId) continue;
                found = true;
                wMinX = Mathf.Min(wMinX, wall.startPos.x, wall.endPos.x);
                wMaxX = Mathf.Max(wMaxX, wall.startPos.x, wall.endPos.x);
                wMinZ = Mathf.Min(wMinZ, wall.startPos.z, wall.endPos.z);
                wMaxZ = Mathf.Max(wMaxZ, wall.startPos.z, wall.endPos.z);
            }

            if (!found) return;

            float roomW = wMaxX - wMinX;
            float roomH = wMaxZ - wMinZ;
            room.areaSqM = roomW * roomH;
            room.bounds = new RectInt(
                Mathf.RoundToInt(wMinX / gridSize),
                Mathf.RoundToInt(wMinZ / gridSize),
                Mathf.RoundToInt(roomW / gridSize),
                Mathf.RoundToInt(roomH / gridSize)
            );
        }

        // ─── Helpers ───

        private DetectedRoom FindExistingRoom(List<Vector2Int> cells)
        {
            if (cells.Count == 0) return null;

            foreach (var existing in rooms)
            {
                if (existing.cells.Count == 0) continue;
                // Porównaj centrum
                Vector2Int newCenter = cells[cells.Count / 2];
                Vector2Int oldCenter = existing.cells[existing.cells.Count / 2];
                if (newCenter == oldCenter) return existing;

                // Lub: > 50% komórek wspólnych
                int overlap = 0;
                var existingSet = new HashSet<Vector2Int>(existing.cells);
                foreach (var c in cells)
                    if (existingSet.Contains(c)) overlap++;

                if (overlap > cells.Count / 2)
                    return existing;
            }
            return null;
        }

        private int FindBuildingIdForRoom(List<Vector2Int> cells, IReadOnlyList<WallSegment> walls)
        {
            if (cells.Count == 0) return -1;

            // Znajdź ścianę najbliższą centrum pokoju
            Vector2Int center = cells[cells.Count / 2];
            Vector3 worldCenter = new Vector3(center.x * gridSize, 0, center.y * gridSize);

            float bestDist = float.MaxValue;
            int bestBuildingId = -1;

            foreach (var wall in walls)
            {
                Vector3 wallCenter = (wall.startPos + wall.endPos) / 2f;
                float dist = Vector3.Distance(worldCenter, wallCenter);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestBuildingId = wall.buildingId;
                }
            }

            return bestBuildingId;
        }

        private void ClearAllRooms()
        {
            foreach (var room in rooms)
            {
                if (room.floorObject != null)
                    Destroy(room.floorObject);
            }
            rooms.Clear();
        }

        // ─── Public API ───

        /// <summary>
        /// Zmień typ pokoju i zaktualizuj kolor podłogi.
        /// </summary>
        public void SetRoomType(int roomId, RoomType newType)
        {
            var room = rooms.Find(r => r.roomId == roomId);
            if (room == null) return;

            // M-Economy Faza 5: rozlicz zmianę typu (delta). Brak środków na upgrade → nie zmieniaj.
            if (!TryApplyRoomCostDelta(room, newType))
            {
                Log.Warn($"[RoomDetectionSystem] SetRoomType: brak srodkow na {newType} (pokoj #{roomId}) → blocked");
                return;
            }

            room.roomType = newType;
            UpdateFloorColor(room);
            OnRoomsChanged?.Invoke();
        }

        /// <summary>M-Economy Faza 5: płać/zwróć RÓŻNICĘ kosztu (per m²) przy zmianie typu pokoju.
        /// Upgrade i nie stać → false (caller NIE zmienia typu). Downgrade/None → refund różnicy.
        /// Demolicja (usunięcie ścian → re-detekcja czyści pokój) NIE zwraca kosztu (realistyczne, limitacja).</summary>
        private bool TryApplyRoomCostDelta(DetectedRoom room, RoomType newType)
        {
            long oldCost = ConstructionCosts.RoomGroszy(room.roomType, room.areaSqM);
            long newCost = ConstructionCosts.RoomGroszy(newType, room.areaSqM);
            if (newCost > oldCost)
            {
                if (!ConstructionBilling.TryCharge(newCost - oldCost, "room_build", newType.ToString())) return false;
            }
            else if (newCost < oldCost)
            {
                ConstructionBilling.Refund(oldCost - newCost, "room_refund", room.roomType.ToString());
            }
            return true;
        }

        /// <summary>
        /// Sprawdza czy dany typ pasuje do rozmiaru pokoju.
        /// </summary>
        public bool IsRoomLargeEnough(DetectedRoom room, RoomType type)
        {
            if (!RoomRequirements.MinSize.ContainsKey(type)) return true;

            var (minW, minD, _) = RoomRequirements.MinSize[type];
            float roomW = room.bounds.width * gridSize;
            float roomD = room.bounds.height * gridSize;

            // Sprawdź oba obroty
            return (roomW >= minW && roomD >= minD) || (roomW >= minD && roomD >= minW);
        }

        /// <summary>
        /// Znajdź pokój po kliknięciu na podłogę (GameObject).
        /// </summary>
        public DetectedRoom FindRoomByFloor(GameObject floorObj)
        {
            foreach (var room in rooms)
            {
                if (room.floorObject == null) continue;
                if (room.floorObject == floorObj || floorObj.transform.IsChildOf(room.floorObject.transform))
                    return room;
            }
            return null;
        }

        private void UpdateFloorColor(DetectedRoom room)
        {
            if (room.floorObject == null) return;

            var renderers = room.floorObject.GetComponentsInChildren<MeshRenderer>();
            Color color = RoomRequirements.FloorColors.GetValueOrDefault(room.roomType, Color.gray);
            foreach (var r in renderers)
                r.material.color = color;
        }
    }
}
