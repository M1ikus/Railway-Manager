using UnityEngine;
using DepotSystem;
using DepotSystem.RoomLevel;

namespace RailwayManager.Personnel
{
    /// <summary>
    /// MM-7 / MM-D7/D8/D15 — bonusy morale z lvla pomieszczeń (Supervisor/Social/Bathroom).
    ///
    /// Hook w <see cref="FatigueMoraleTickService.ApplyDailyTick"/> — per pracownik
    /// dorzuca <see cref="GetTotalDailyMoraleBonus"/>.
    ///
    /// Reguły:
    /// <list type="bullet">
    /// <item><b>Supervisor</b> (MM-D7): globalny bonus dla WSZYSTKICH active pracowników.
    /// MM-D15: tylko najwyższy lvl liczy się (best-lvl-wins).
    /// Lvl 1=+1, lvl 5=+5 morale/dzień.</item>
    /// <item><b>Social</b> (MM-D8): bonus tylko dla pracowników z accessSide free
    /// do pokoju Social. Lvl 1=+1, lvl 5=+5 morale/dzień.</item>
    /// <item><b>Bathroom</b> (MM-D8): bonus tylko dla pracowników z accessSide free.
    /// Lvl 1=+0.5, lvl 5=+2.5 morale/dzień. Float zaokrąglany w ApplyDailyTick.</item>
    /// </list>
    ///
    /// MM-D6: morale binary — pokój istnieje + ma drzwi (doorCells.Count > 0) → bonus.
    /// Brak distance falloff (uproszczenie EA, post-EA może dorzucić pathfinding-based).
    /// </summary>
    public static class MoraleBonusService
    {
        // ════════════════════════════════════════════════════════
        //  Per-room mappings (z spec'a sekcja 2.2)
        // ════════════════════════════════════════════════════════

        /// <summary>Supervisor lvl → globalny morale bonus per dzień (int).</summary>
        public static int SupervisorBonusForLvl(int lvl) => lvl switch
        {
            1 => 1, 2 => 2, 3 => 3, 4 => 4, 5 => 5,
            _ => 0,
        };

        /// <summary>Social lvl → bonus per dzień dla pracowników z dojściem (int).</summary>
        public static int SocialBonusForLvl(int lvl) => lvl switch
        {
            1 => 1, 2 => 2, 3 => 3, 4 => 4, 5 => 5,
            _ => 0,
        };

        /// <summary>
        /// Bathroom lvl → bonus per dzień dla pracowników z dojściem (float, 0.5..2.5).
        /// Zaokrąglony do najbliższej int przed apply do <c>currentMorale</c> (akumulator
        /// nie potrzebny — UX cap 0/1/1/2/2 wystarczy w EA, M-Balance dopracuje).
        /// </summary>
        public static float BathroomBonusForLvl(int lvl) => lvl switch
        {
            1 => 0.5f, 2 => 1.0f, 3 => 1.5f, 4 => 2.0f, 5 => 2.5f,
            _ => 0f,
        };

        // ════════════════════════════════════════════════════════
        //  Global access (best-lvl-wins, MM-D15)
        // ════════════════════════════════════════════════════════

        public static int GetSupervisorLvl()
        {
            var svc = RoomLevelService.Instance;
            return svc == null ? 0 : svc.GetBestLevelForType(RoomType.Supervisor);
        }

        public static int GetSocialLvl()
        {
            var svc = RoomLevelService.Instance;
            return svc == null ? 0 : svc.GetBestLevelForType(RoomType.Social);
        }

        public static int GetBathroomLvl()
        {
            var svc = RoomLevelService.Instance;
            return svc == null ? 0 : svc.GetBestLevelForType(RoomType.Bathroom);
        }

        // ════════════════════════════════════════════════════════
        //  Access check (MM-D8 — pokój istnieje + ma drzwi)
        // ════════════════════════════════════════════════════════

        /// <summary>
        /// MM-D8: czy istnieje co najmniej jeden pokój danego typu z lvl ≥ 1 i co najmniej
        /// jednymi drzwiami (<c>doorCells.Count &gt; 0</c>).
        ///
        /// MVP w EA: brak per-employee pathfinding, sprawdzamy tylko presence + drzwi
        /// (izolowany pokój bez drzwi → brak dostępu, "pracownik nie umie wejść").
        /// Post-EA może dorzucić pełny pathfind od employee position do pokoju.
        /// </summary>
        public static bool AnyRoomReachable(RoomType type)
        {
            var roomSys = UnityEngine.Object.FindAnyObjectByType<RoomDetectionSystem>();
            if (roomSys == null) return false;

            foreach (var r in roomSys.Rooms)
            {
                if (r == null) continue;
                if (r.roomType != type) continue;
                if (r.level < 1) continue;
                if (r.doorCells != null && r.doorCells.Count > 0) return true;
            }
            return false;
        }

        // ════════════════════════════════════════════════════════
        //  Daily morale bonus (entry point dla FatigueMoraleTickService)
        // ════════════════════════════════════════════════════════

        /// <summary>
        /// Suma dziennych bonusów morale dla pracownika. Wywoływane w
        /// <see cref="FatigueMoraleTickService.ApplyDailyTick"/> per pracownik.
        ///
        /// Aktualnie nie różnicuje per-employee (Supervisor globalny, Social/Bathroom
        /// per-presence). Cache values raz per tick dla performance.
        /// </summary>
        public static int GetTotalDailyMoraleBonus(Employee e)
        {
            if (e == null) return 0;
            int bonus = 0;

            // Supervisor — globalny dla wszystkich active
            bonus += SupervisorBonusForLvl(GetSupervisorLvl());

            // Social — tylko dla pracowników z dojściem
            if (AnyRoomReachable(RoomType.Social))
                bonus += SocialBonusForLvl(GetSocialLvl());

            // Bathroom — tylko dla pracowników z dojściem; float → round to int
            if (AnyRoomReachable(RoomType.Bathroom))
            {
                float bath = BathroomBonusForLvl(GetBathroomLvl());
                bonus += UnityEngine.Mathf.RoundToInt(bath);
            }

            return bonus;
        }

        /// <summary>Convenience dla diagnostyki — verbose breakdown bonus.</summary>
        public static (int supervisor, int social, int bathroom, int total) GetBreakdown(Employee e)
        {
            int sv = SupervisorBonusForLvl(GetSupervisorLvl());
            int so = AnyRoomReachable(RoomType.Social) ? SocialBonusForLvl(GetSocialLvl()) : 0;
            int ba = AnyRoomReachable(RoomType.Bathroom)
                ? UnityEngine.Mathf.RoundToInt(BathroomBonusForLvl(GetBathroomLvl()))
                : 0;
            return (sv, so, ba, sv + so + ba);
        }
    }
}
