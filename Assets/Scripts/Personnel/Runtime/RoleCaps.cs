using DepotSystem;
using DepotSystem.RoomLevel;
using RailwayManager.Core;

namespace RailwayManager.Personnel
{
    /// <summary>
    /// MM-5/MM-6/MM-6b — cap headcount per rola w zależności od lvla pomieszczeń.
    ///
    /// Lookup: <see cref="RoomLevelService.GetBestLevelForType"/> (MM-D15 multi-rooms = best wins)
    /// → mapping na cap z spec'a sekcja 2.2.
    ///
    /// Role z capem:
    /// <list type="bullet">
    /// <item><see cref="EmployeeRole.Office"/> i <see cref="EmployeeRole.Research"/>
    ///   dzielą cap z RoomType.Office (biurka w biurze)</item>
    /// <item><see cref="EmployeeRole.Dispatcher"/> — RoomType.Dispatcher</item>
    /// <item><see cref="EmployeeRole.TrafficController"/> — RoomType.TrafficController</item>
    /// </list>
    ///
    /// Pozostałe role (Driver/Conductor/Mechanic/Cleaner/WashBay/TicketClerk) bez capu —
    /// <see cref="GetMaxForRole"/> zwraca <c>int.MaxValue</c>.
    ///
    /// MM-5: cap dla Office. MM-6/MM-6b dorzuca pełen runtime support dla Dispatcher/Traffic.
    /// </summary>
    public static class RoleCaps
    {
        /// <summary>
        /// Max liczba pracowników danej roli (Active) jaką gracz może zatrudnić.
        /// Brak RoomLevelService (np. spoza Depot scene) → int.MaxValue (defensive — no cap).
        /// </summary>
        public static int GetMaxForRole(EmployeeRole role)
        {
            var svc = RoomLevelService.Instance;
            if (svc == null) return int.MaxValue;

            return role switch
            {
                EmployeeRole.Office or EmployeeRole.Research
                    => OfficeCapForLvl(svc.GetBestLevelForType(RoomType.Office)),
                EmployeeRole.Dispatcher
                    => DispatcherCapForLvl(svc.GetBestLevelForType(RoomType.Dispatcher)),
                EmployeeRole.TrafficController
                    => TrafficCapForLvl(svc.GetBestLevelForType(RoomType.TrafficController)),
                _ => int.MaxValue,
            };
        }

        /// <summary>Czy rola dzielona z Office room (Office + Research) jest wpisana razem do capu.</summary>
        public static bool IsRoleSharedWithOffice(EmployeeRole role)
            => role == EmployeeRole.Office || role == EmployeeRole.Research;

        /// <summary>
        /// Liczba aktywnych pracowników danej roli (Active = nie Fired/Retired).
        /// Office i Research liczone razem (dzielą cap).
        /// </summary>
        public static int GetCurrentHeadcountForRole(EmployeeRole role)
        {
            int count = 0;
            foreach (var e in PersonnelService.Employees)
            {
                if (!e.IsActive) continue;
                if (IsRoleSharedWithOffice(role))
                {
                    if (IsRoleSharedWithOffice(e.role)) count++;
                }
                else if (e.role == role)
                {
                    count++;
                }
            }
            return count;
        }

        /// <summary>Czy gracz ma już max pracowników tej roli (cap reached, nie da się hire'ować nowego).</summary>
        public static bool IsAtCap(EmployeeRole role)
        {
            int max = GetMaxForRole(role);
            if (max == int.MaxValue) return false;
            return GetCurrentHeadcountForRole(role) >= max;
        }

        /// <summary>Human-readable label "X/Y" dla UI display.</summary>
        public static string FormatHeadcount(EmployeeRole role)
        {
            int current = GetCurrentHeadcountForRole(role);
            int max = GetMaxForRole(role);
            return max == int.MaxValue ? $"{current}" : $"{current}/{max}";
        }

        // ════════════════════════════════════════════════════════
        //  Per-RoomType cap mappings (z spec'a sekcja 2.2)
        // ════════════════════════════════════════════════════════

        /// <summary>Office lvl → max biurowych (Office + Research łącznie).</summary>
        public static int OfficeCapForLvl(int lvl) => lvl switch
        {
            1 => 2,
            2 => 4,
            3 => 6,
            4 => 8,
            5 => 12,
            _ => 0,  // brak biura → 0 (gracz nie może hire'ować biurowych bez biura)
        };

        /// <summary>Dispatcher lvl → max dyspozytorów.</summary>
        public static int DispatcherCapForLvl(int lvl) => lvl switch
        {
            1 => 1,
            2 => 2,
            3 => 3,
            4 => 4,
            5 => 5,
            _ => 0,
        };

        /// <summary>TrafficController lvl → max dyżurnych.</summary>
        public static int TrafficCapForLvl(int lvl) => lvl switch
        {
            1 => 1,
            2 => 2,
            3 => 3,
            4 => 4,
            5 => 5,
            _ => 0,
        };
    }
}
