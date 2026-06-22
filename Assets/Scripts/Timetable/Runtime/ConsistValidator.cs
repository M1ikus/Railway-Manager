using System.Collections.Generic;
using RailwayManager.Core;
using RailwayManager.Fleet;

namespace RailwayManager.Timetable
{
    /// <summary>
    /// Walidator składu pociągu (consist = lista pojazdów tworzących jedną jednostkę operacyjną
    /// w jednym dniu obiegu). Reguły realne PKP:
    ///
    /// 1. **Lokomotywa + wagony**: lokomotywa (EL/DL) na pozycji 0, wagony w pozostałych.
    ///    Nie można mieszać z EMU/DMU.
    /// 2. **EMU + EMU**: zespoły trakcyjne elektryczne można łączyć tylko między sobą
    ///    (wielokrotne sterowanie tego samego typu). Bez wagonów.
    /// 3. **DMU + DMU**: analogicznie dla spalinowych zespołów.
    /// 4. **DMU + wagon**: dopuszczalne — szynobus może ciągnąć wagon pasażerski.
    /// 5. **Pojedynczy EMU/DMU**: OK (samoczynny skład).
    /// 6. **Pojedyncza lokomotywa bez wagonów**: LP/LT (lok luzem) — tolerowane ale zwykle
    ///    nie w normalnym obiegu (tylko dla dojazdów służbowych).
    /// 7. **Sam wagon bez lokomotywy/ZT**: NIEPRAWIDŁOWO — wagon nie jedzie samodzielnie.
    /// </summary>
    public static class ConsistValidator
    {
        public enum Severity { Ok, Warning, Error }

        public readonly struct ValidationResult
        {
            public readonly Severity severity;
            public readonly string message;
            public ValidationResult(Severity s, string m) { severity = s; message = m; }
            public bool IsOk => severity == Severity.Ok;
            public bool IsBlocking => severity == Severity.Error;
        }

        /// <summary>
        /// Waliduje skład (lista pojazdów w kolejności od przodu). Kolejność jest istotna
        /// dla sprawdzenia "lokomotywa na przodzie".
        /// </summary>
        public static ValidationResult Validate(List<int> vehicleIds)
        {
            if (vehicleIds == null || vehicleIds.Count == 0)
                return new ValidationResult(Severity.Error, "Pusty skład");

            // Resolve vehicle types
            var types = new List<FleetVehicleType>();
            foreach (var id in vehicleIds)
            {
                FleetVehicleData found = null;
                foreach (var v in FleetService.OwnedVehicles)
                {
                    if (v != null && v.id == id) { found = v; break; }
                }
                if (found == null)
                    return new ValidationResult(Severity.Error, $"Pojazd #{id} nie istnieje we flocie");
                types.Add(found.type);
            }

            // Liczba poszczególnych typów
            int locoCount = 0;
            int emuCount = 0;
            int dmuCount = 0;
            int carCount = 0;
            foreach (var t in types)
            {
                switch (t)
                {
                    case FleetVehicleType.ElectricLocomotive:
                    case FleetVehicleType.DieselLocomotive:
                        locoCount++; break;
                    case FleetVehicleType.EMU: emuCount++; break;
                    case FleetVehicleType.DMU: dmuCount++; break;
                    case FleetVehicleType.PassengerCar: carCount++; break;
                }
            }

            // ── Pojedynczy pojazd ──
            if (vehicleIds.Count == 1)
            {
                if (locoCount == 1)
                    return new ValidationResult(Severity.Warning,
                        "Sama lokomotywa (luzem) — OK tylko dla kursów służbowych");
                if (emuCount == 1 || dmuCount == 1)
                    return new ValidationResult(Severity.Ok, "Samoczynny skład");
                // Tylko wagon
                return new ValidationResult(Severity.Error,
                    "Sam wagon nie jedzie — dodaj lokomotywę lub zespół trakcyjny");
            }

            // ── Skład mieszany: sprawdź reguły łączenia ──

            // Zasada 1: Jest lokomotywa → musi być na pozycji 0, reszta to wagony
            if (locoCount > 0)
            {
                if (emuCount > 0 || dmuCount > 0)
                    return new ValidationResult(Severity.Error,
                        "Nie można mieszać lokomotywy z zespołem trakcyjnym (EMU/DMU)");

                if (locoCount > 1)
                    return new ValidationResult(Severity.Warning,
                        "Więcej niż jedna lokomotywa — typowo dopuszczalne tylko dla trakcji podwójnej");

                // Lokomotywa musi być na pozycji 0 (przód)
                bool locoAtFront = types[0] == FleetVehicleType.ElectricLocomotive
                                || types[0] == FleetVehicleType.DieselLocomotive;
                if (!locoAtFront)
                    return new ValidationResult(Severity.Error,
                        "Lokomotywa musi być na przodzie składu (pozycja 1)");

                // Musi mieć co najmniej jeden wagon
                if (carCount == 0)
                    return new ValidationResult(Severity.Error,
                        "Lokomotywa bez wagonów — dodaj wagon lub pozostaw samą lok");

                return new ValidationResult(Severity.Ok, $"Lokomotywa + {carCount} wagon(y)");
            }

            // Zasada 2: EMU + EMU (bez wagonów, bez DMU, bez lok)
            if (emuCount > 0)
            {
                if (dmuCount > 0)
                    return new ValidationResult(Severity.Error,
                        "Nie można mieszać EMU z DMU (różne typy trakcji)");
                if (carCount > 0)
                    return new ValidationResult(Severity.Error,
                        "EMU nie ciągnie wagonów — musi być sam lub z innym EMU");
                // All EMU
                return new ValidationResult(Severity.Ok, $"Wielokrotne EMU × {emuCount}");
            }

            // Zasada 3: DMU + DMU lub DMU + wagon
            if (dmuCount > 0)
            {
                // DMU może być z innymi DMU albo z wagonami (szynobus ciągnie wagon)
                // W PKP praktyce: DMU na przodzie, wagony za nim
                if (dmuCount >= 1 && carCount >= 1)
                {
                    // DMU musi być na pozycji 0
                    if (types[0] != FleetVehicleType.DMU)
                        return new ValidationResult(Severity.Error,
                            "DMU musi być na przodzie jeśli ciągnie wagon");
                    return new ValidationResult(Severity.Ok, $"DMU + {carCount} wagon(y)");
                }
                // Wiele DMU bez wagonów
                return new ValidationResult(Severity.Ok, $"Wielokrotne DMU × {dmuCount}");
            }

            // Same wagony bez lokomotywy/zespołu
            return new ValidationResult(Severity.Error,
                "Sam(e) wagon(y) bez lokomotywy — dodaj lokomotywę na przodzie");
        }

        /// <summary>
        /// Sprawdza czy można DODAĆ <paramref name="newVehicleId"/> do istniejącego składu
        /// <paramref name="currentIds"/>. Wykonuje Validate na hipotetycznym wyniku.
        /// Używane żeby zablokować drop zanim fizycznie się stanie — obecnie modal pozwala
        /// dodać i pokazuje błąd, ale callers mogą sami sprawdzić zanim wywołają Add.
        /// </summary>
        public static ValidationResult CanAdd(List<int> currentIds, int newVehicleId)
        {
            var hypothetical = new List<int>(currentIds);
            hypothetical.Add(newVehicleId);
            return Validate(hypothetical);
        }
    }
}
