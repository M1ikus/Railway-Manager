using DepotSystem;
using DepotSystem.Furniture;
using DepotSystem.Furniture.Placement;
using UnityEngine;
using RailwayManager.Personnel.Furniture;

namespace RailwayManager.Personnel.Workflows
{
    /// <summary>
    /// TD-025: Lookup helpers dla destinacji workflow'ów —
    /// dispatcher's desk, social room (idle), gate, generic room center.
    ///
    /// <para>Wszystko statyczne i bezstanowe. Lookup'y robione przez
    /// <c>FindAnyObjectByType&lt;RoomDetectionSystem&gt;()</c> + iteracja
    /// po <c>Rooms</c>/PlacedFurniture — N na ~50 pokoi to OK runtime.</para>
    /// </summary>
    public static class WorkflowLocations
    {
        /// <summary>
        /// Zwraca world position dyspozytora (access cell <c>WorkstationOffice</c> w
        /// <see cref="RoomType.Dispatcher"/>). Fallback: środek pokoju. Null gdy brak dispatcher room.
        /// </summary>
        public static Vector3? GetDispatcherDeskPosition()
        {
            int inst = GetDispatcherDeskInstanceId();
            if (inst >= 0)
            {
                var pos = FurnitureAssignmentService.GetAccessSideWorldPosition(inst);
                if (pos.HasValue) return pos;
            }

            // Fallback: środek dispatcher room
            var rooms = Object.FindAnyObjectByType<RoomDetectionSystem>();
            if (rooms != null)
                foreach (var r in rooms.Rooms)
                    if (r != null && r.roomType == RoomType.Dispatcher)
                        return new Vector3(r.bounds.x + r.bounds.width * 0.5f, 0f, r.bounds.y + r.bounds.height * 0.5f);
            return null;
        }

        /// <summary>TD-034 G: instanceId biurka dyspozytora (WorkstationOffice w Dispatcher room), -1 gdy brak.
        /// Używane jako token kolejki meldunku (rezerwacja w FurnitureOccupancyService → 1 meldujący naraz).</summary>
        public static int GetDispatcherDeskInstanceId()
        {
            var placer = FurniturePlacer.Instance;
            if (placer == null) return -1;
            var rooms = Object.FindAnyObjectByType<RoomDetectionSystem>();
            if (rooms == null) return -1;

            DetectedRoom dispatcherRoom = null;
            foreach (var r in rooms.Rooms)
                if (r != null && r.roomType == RoomType.Dispatcher) { dispatcherRoom = r; break; }
            if (dispatcherRoom == null) return -1;

            foreach (var instance in placer.PlacedInstances)
            {
                if (instance == null) continue;
                var item = FurnitureCatalog.FindById(instance.itemId);
                if (item == null || !item.HasFunction(ObjectFunction.WorkstationOffice)) continue;
                int cellX = Mathf.FloorToInt(instance.position.x);
                int cellZ = Mathf.FloorToInt(instance.position.z);
                if (!dispatcherRoom.bounds.Contains(new Vector2Int(cellX, cellZ))) continue;
                return instance.instanceId;
            }
            return -1;
        }

        /// <summary>TD-034 G: czy jest dyspozytor na zmianie (warunek meldunku). EA: presence OnShift
        /// wystarczy (post-EA można zaostrzyć do WorkingAtStation przy biurku).</summary>
        public static bool IsDispatcherAvailable()
        {
            foreach (var emp in PersonnelService.Employees)
                if (emp != null && emp.role == EmployeeRole.Dispatcher && emp.status == EmployeeStatus.OnShift)
                    return true;
            return false;
        }

        /// <summary>TD-034 G: pozycja w kolejce za biurkiem dyspozytora (offset wzdłuż -X, 1.2 m odstępu).</summary>
        public static Vector3? GetDispatcherQueuePosition(int queueIndex)
        {
            var deskPos = GetDispatcherDeskPosition();
            if (!deskPos.HasValue) return null;
            return deskPos.Value + new Vector3(-1.2f * (queueIndex + 1), 0f, 0f);
        }

        /// <summary>
        /// Zwraca random cell w <see cref="RoomType.Social"/> (deterministic per employeeId).
        /// Używane jako idle destination dla pracowników bez celu.
        /// Null gdy brak Social room w zajezdni.
        /// </summary>
        public static Vector3? GetSocialRoomIdlePosition(int employeeId)
        {
            var rooms = Object.FindAnyObjectByType<RoomDetectionSystem>();
            if (rooms == null) return null;

            DetectedRoom social = null;
            foreach (var r in rooms.Rooms)
            {
                if (r != null && r.roomType == RoomType.Social)
                {
                    social = r;
                    break;
                }
            }
            if (social == null) return null;

            int xRange = Mathf.Max(1, social.bounds.width);
            int zRange = Mathf.Max(1, social.bounds.height);
            int dx = (employeeId * 31) % xRange;
            int dz = (employeeId * 47) % zRange;
            float wx = social.bounds.x + dx + 0.5f;
            float wz = social.bounds.y + dz + 0.5f;
            return new Vector3(wx, 0f, wz);
        }

        /// <summary>
        /// Sprawdza czy rola wymaga meldunku u dyspozytora przed rozpoczęciem pracy
        /// (user decision 2026-05-11 pkt 7).
        /// <list type="bullet">
        /// <item>Cleaner — nie melduje się (od razu do brudnego pojazdu)</item>
        /// <item>TicketClerk — nie spawnuje się w 3D w ogóle</item>
        /// <item>Dispatcher — nie melduje się sam u siebie</item>
        /// <item>Pozostali — meldunek wymagany</item>
        /// </list>
        /// </summary>
        public static bool RequiresDispatcherMeldunek(EmployeeRole role)
        {
            return role switch
            {
                EmployeeRole.Cleaner       => false,
                EmployeeRole.TicketClerk   => false, // i tak nie spawnuje
                EmployeeRole.Dispatcher    => false,
                _ => true
            };
        }
    }
}
