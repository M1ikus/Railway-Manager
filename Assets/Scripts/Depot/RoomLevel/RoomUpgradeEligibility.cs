using System.Collections.Generic;
using System.Text;

namespace DepotSystem.RoomLevel
{
    /// <summary>
    /// MM-2 — wynik sprawdzenia eligibility awansu pokoju do konkretnego lvla.
    ///
    /// Zwracany przez <see cref="RoomLevelService.CheckEligibility"/>. Konsumenci:
    /// - UI <c>RoomTypePopupUI</c> (MM-3) — wyświetla checklist mebli + summary
    /// - <see cref="RoomLevelService.TryUpgrade"/> — sprawdza CanUpgrade przed actual upgrade
    /// </summary>
    public struct RoomUpgradeEligibility
    {
        /// <summary>Czy pokój może być teraz awansowany do <see cref="targetLevel"/>.</summary>
        public bool canUpgrade;

        /// <summary>Aktualny lvl pokoju (przed awansem).</summary>
        public int currentLevel;

        /// <summary>Docelowy lvl (currentLevel + 1, lub explicit).</summary>
        public int targetLevel;

        /// <summary>Pokój już na max lvl (RoomLevelCatalog.MaxLevel = 5) — brak dalszych awansów.</summary>
        public bool isMaxLevel;

        /// <summary>Typ pokoju nie ma lvlowania (None/Storage/Locker/Corridor) — awans niemożliwy.</summary>
        public bool roomTypeNotLvlable;

        /// <summary>Wymagana powierzchnia spełniona (areaSqM ≥ minAreaSqM).</summary>
        public bool sizeOk;
        public float currentAreaSqM;
        public float requiredAreaSqM;

        /// <summary>
        /// Per-requirement check'i. Lista długa = pełen overview dla UI checklist
        /// "biurko 4/6 ✗, kanapa 1/3 ✗".
        /// </summary>
        public List<RequirementCheck> furnitureChecks;

        /// <summary>Human-readable summary dla logów / tooltip / debug.</summary>
        public string Summary
        {
            get
            {
                if (roomTypeNotLvlable) return "Typ pokoju bez lvlowania";
                if (isMaxLevel) return $"Już na max lvl ({currentLevel})";
                var sb = new StringBuilder();
                sb.Append($"Lvl {currentLevel} → {targetLevel}: ");
                if (canUpgrade) { sb.Append("✓ wszystkie wymagania spełnione"); return sb.ToString(); }

                var missing = new List<string>();
                if (!sizeOk) missing.Add($"area {currentAreaSqM:F0}/{requiredAreaSqM:F0}m²");
                if (furnitureChecks != null)
                {
                    foreach (var c in furnitureChecks)
                        if (!c.ok) missing.Add($"{c.requirement.id} {c.actualCount}/{c.requirement.count}");
                }
                sb.Append("brakuje: ").Append(string.Join(", ", missing));
                return sb.ToString();
            }
        }

        public static RoomUpgradeEligibility NotLvlable(int currentLevel)
            => new RoomUpgradeEligibility { roomTypeNotLvlable = true, currentLevel = currentLevel };

        public static RoomUpgradeEligibility MaxLevel(int currentLevel)
            => new RoomUpgradeEligibility { isMaxLevel = true, currentLevel = currentLevel };
    }

    /// <summary>
    /// MM-2 — pojedynczy check wymagania (z <see cref="RoomLevelRequirements.furnitureRequirements"/>).
    /// </summary>
    public struct RequirementCheck
    {
        public FurnitureRequirement requirement;
        public int actualCount;
        public bool ok;  // actualCount >= requirement.count
    }
}
