using System.Collections.Generic;
using RailwayManager.Core;
using RailwayManager.Fleet;

namespace RailwayManager.Timetable
{
    /// <summary>
    /// Automatyczny generator obiegów z istniejących rozkładów. Używane przez
    /// CirculationListUI [Wygeneruj auto] — settings modal → preview → akceptacja.
    ///
    /// Algorytm (zachłanny chain-building z filtrami):
    /// 1. Pula Active Timetable'i nie przypisanych do żadnego obiegu, opcjonalnie
    ///    dodatkowo zawężona przez allowedTimetableIds z settings.
    /// 2. Seed: rozkład który nie jest następnikiem żadnego innego (pierwsze w chainie).
    /// 3. Chain building z zachłannym wyborem najkrótszego gap'u. Filtry:
    ///    - Stacje: end(N) == start(N+1)
    ///    - Kalendarze: intersection != 0
    ///    - Czas: start(N+1) >= end(N) + max(reverse_margin, minGap)
    ///    - Composition.mode (gdy respectCompositionMode=true)
    ///    - ServiceClass (gdy respectServiceClass=true, deadhead neutralny)
    /// 4. Konwersja chainu na ProposedCirculation.
    /// 5. Opcjonalnie auto-assign pojazdów (gdy autoAssignVehicles=true) — najlepszy
    ///    pasujący z floty wg composition, availability i kondycji.
    /// </summary>
    public static class CirculationAutoGenerator
    {
        /// <summary>Ustawienia trybu auto — konfigurowalne z settings modal'a.</summary>
        public class GeneratorSettings
        {
            /// <summary>Minimalna dodatkowa przerwa między kursami w minutach (poza reverse margin).</summary>
            public int minGapMinutes = 0;

            /// <summary>
            /// BUG-069: maksymalna przerwa w minutach między kolejnymi kursami chainu.
            /// Default 240 min (4h) — pojazd nie powinien stać bezczynnie więcej niż 4h.
            /// User-konfigurowalne dla realistycznych obiegów (poranny + popołudniowy split).
            /// </summary>
            public int maxGapMinutes = 240;

            /// <summary>
            /// Set ID rozkładów do rozważenia. Null = wszystkie aktywne, inaczej = tylko te.
            /// Używane przez settings modal (checkboxy per rozkład).
            /// </summary>
            public HashSet<int> allowedTimetableIds = null;

            /// <summary>Filtr composition.mode — nie łącz EMU+LocoWithCars.</summary>
            public bool respectCompositionMode = true;

            /// <summary>Filtr ServiceClass — nie mieszaj Local+Express (deadhead neutralny).</summary>
            public bool respectServiceClass = true;

            /// <summary>Po wygenerowaniu chainu spróbuj przypisać pojazd z floty.</summary>
            public bool autoAssignVehicles = true;
        }

        /// <summary>Propozycja obiegu przed akceptacją przez gracza.</summary>
        public class ProposedCirculation
        {
            public string suggestedName;
            public List<int> timetableIds = new();
            public byte dayMaskBits; // intersection kalendarzy wszystkich rozkładów
            public string startStationName;
            public string endStationName;
            public int totalDurationMinutes; // od pierwszego odjazdu do ostatniego przyjazdu

            /// <summary>ID pojazdu auto-assigned (lub -1 jeśli brak pasującego).</summary>
            public int suggestedVehicleId = -1;
            /// <summary>Info dla UI o auto-assign — np. 'EN57 001' lub 'brak pasującego pojazdu'.</summary>
            public string vehicleAssignmentInfo;
        }

        /// <summary>Wynik auto-generacji — propozycje + sieroty.</summary>
        public class GenerationResult
        {
            public List<ProposedCirculation> proposedCirculations = new();
            public List<int> orphanTimetableIds = new();
        }

        /// <summary>
        /// Generuje propozycje bez ich zatwierdzania. Caller (CirculationListUI)
        /// pokazuje preview i dopiero po akceptacji wywołuje ApplyProposal.
        /// </summary>
        public static GenerationResult Generate(GeneratorSettings settings = null)
        {
            settings ??= new GeneratorSettings();
            var result = new GenerationResult();

            // Pula rozkładów — Active, nie w żadnym obiegu, opcjonalnie whitelisted przez settings
            var availableIds = new HashSet<int>();
            foreach (var t in TimetableService.Timetables)
            {
                if (t == null) continue;
                if (t.status != TimetableStatus.Active) continue;
                if (settings.allowedTimetableIds != null
                    && !settings.allowedTimetableIds.Contains(t.id)) continue;
                availableIds.Add(t.id);
            }
            foreach (var c in CirculationService.Circulations)
            {
                if (c?.steps == null) continue;
                foreach (var s in c.steps)
                    availableIds.Remove(s.timetableId);
            }

            // BUG-064: tracking pojazdów zarezerwowanych w bieżącym batch'u — AssignBestVehicle
            // wcześniej sprawdzało tylko CirculationService.Circulations (istniejące), nie wiedział
            // o innych proposed w tym samym Generate() call'u. Skutek: ten sam pojazd przypisany
            // do 2+ propozycji → invariant "max 1 obieg per pojazd per dzień" złamany po Apply.
            var batchReservedVehicles = new System.Collections.Generic.HashSet<int>();

            // Chain building
            while (availableIds.Count > 0)
            {
                int seedId = PickChainSeed(availableIds, settings);
                if (seedId < 0) break;

                var chain = BuildGreedyChain(seedId, availableIds, settings);
                foreach (var tid in chain)
                    availableIds.Remove(tid);

                if (chain.Count == 0) continue;

                var proposed = BuildProposedFromChain(chain);
                if (proposed == null) continue;

                // Auto-assign pojazdu (BUG-064: pass batch reserved set)
                if (settings.autoAssignVehicles)
                    AssignBestVehicle(proposed, batchReservedVehicles);

                result.proposedCirculations.Add(proposed);
            }

            Log.Info($"[CirculationAutoGenerator] Wygenerowano {result.proposedCirculations.Count} propozycji obiegów "
                     + $"(settings: minGap={settings.minGapMinutes}min, compMode={settings.respectCompositionMode}, "
                     + $"svcCls={settings.respectServiceClass}, autoAssign={settings.autoAssignVehicles})");
            return result;
        }

        /// <summary>Zatwierdza jedną propozycję — tworzy Circulation w stanie Draft.</summary>
        public static Circulation ApplyProposal(ProposedCirculation proposed)
        {
            if (proposed == null || proposed.timetableIds.Count == 0) return null;

            var circulation = new Circulation
            {
                name = proposed.suggestedName,
                calendar = new DayMask { bits = proposed.dayMaskBits },
                status = CirculationStatus.Draft,
                weeksValid = 4,
                steps = new List<CirculationStep>(proposed.timetableIds.Count),
                assignedVehicleIds = new List<int>(),
                vehicleAssignmentsPerDay = new Dictionary<string, List<int>>()
            };

            foreach (var tid in proposed.timetableIds)
                circulation.steps.Add(new CirculationStep(tid, StepKind.Commercial));

            var added = CirculationService.AddCirculation(circulation);

            // Auto-assign pojazdu do wszystkich dni obiegu (jeśli generator znalazł pasujący)
            if (added != null && proposed.suggestedVehicleId >= 0)
            {
                var dates = added.GetActiveDates();
                foreach (var date in dates)
                {
                    string iso = date.ToString("yyyy-MM-dd");
                    added.vehicleAssignmentsPerDay[iso] = new List<int> { proposed.suggestedVehicleId };
                }
                added.assignedVehicleIds = new List<int> { proposed.suggestedVehicleId };
                Log.Info($"[CirculationAutoGenerator] Auto-assigned vehicle #{proposed.suggestedVehicleId} "
                         + $"do obiegu #{added.id} we wszystkich {dates.Count} dni");
            }
            return added;
        }

        /// <summary>Zatwierdza wszystkie propozycje z wyniku.</summary>
        public static int ApplyAll(GenerationResult result)
        {
            if (result == null) return 0;
            int created = 0;
            foreach (var proposed in result.proposedCirculations)
            {
                if (ApplyProposal(proposed) != null) created++;
            }
            return created;
        }

        // ─────────────────────────────────────────────
        //  Internal
        // ─────────────────────────────────────────────

        /// <summary>Wybiera seed — preferuje rozkłady które nie są następnikiem żadnego innego.</summary>
        private static int PickChainSeed(HashSet<int> availableIds, GeneratorSettings settings)
        {
            var followerIds = new HashSet<int>();
            foreach (var aId in availableIds)
            {
                var a = TimetableService.GetTimetable(aId);
                if (a == null) continue;
                foreach (var bId in availableIds)
                {
                    if (aId == bId) continue;
                    if (!IsCompatibleNext(a, TimetableService.GetTimetable(bId), settings)) continue;
                    followerIds.Add(bId);
                }
            }

            foreach (var id in availableIds)
                if (!followerIds.Contains(id)) return id;

            foreach (var id in availableIds) return id;
            return -1;
        }

        /// <summary>Chain building zachłanny od seed'a.</summary>
        private static List<int> BuildGreedyChain(int seedId, HashSet<int> availableIds, GeneratorSettings settings)
        {
            var chain = new List<int> { seedId };
            var usedInChain = new HashSet<int> { seedId };
            int currentId = seedId;

            while (true)
            {
                var current = TimetableService.GetTimetable(currentId);
                if (current == null) break;

                int bestNextId = -1;
                int bestGap = int.MaxValue;

                foreach (var candId in availableIds)
                {
                    if (usedInChain.Contains(candId)) continue;
                    var cand = TimetableService.GetTimetable(candId);
                    if (cand == null) continue;
                    if (!IsCompatibleNext(current, cand, settings)) continue;

                    // Plus sprawdź czy nowy krok pasuje do ustalonych już properties
                    // chainu (composition/serviceClass z pierwszego rozkładu)
                    if (!IsCompatibleWithChain(chain, cand, settings)) continue;

                    int gap = ComputeGapMinutes(current, cand);
                    if (gap < bestGap)
                    {
                        bestGap = gap;
                        bestNextId = candId;
                    }
                }

                if (bestNextId < 0) break;

                chain.Add(bestNextId);
                usedInChain.Add(bestNextId);
                currentId = bestNextId;

                if (chain.Count >= 20) break;
            }

            return chain;
        }

        /// <summary>
        /// Czy B może być kolejnym krokiem po A? Sprawdza: stacje, czas, kalendarz, minGap.
        /// Composition.mode i ServiceClass sprawdzane w IsCompatibleWithChain (bo muszą
        /// patrzeć na CAŁY chain ustalony dotychczas, nie tylko na A).
        /// </summary>
        private static bool IsCompatibleNext(Timetable a, Timetable b, GeneratorSettings settings)
        {
            if (a == null || b == null) return false;
            if (a.LastStop == null || b.FirstStop == null) return false;
            if (a.LastStop.stationName != b.FirstStop.stationName) return false;
            if ((a.calendar.bits & b.calendar.bits) == 0) return false;

            int gap = ComputeGapMinutes(a, b);
            if (gap < 0) return false;

            // Minimum gap z settings (0 = tylko reverse margin)
            if (gap < settings.minGapMinutes) return false;

            // BUG-069 fix: użyj settings.maxGapMinutes (default 240, user-konfigurowalne)
            if (gap > settings.maxGapMinutes) return false;

            return true;
        }

        /// <summary>
        /// Czy candidate pasuje do reszty chainu pod kątem composition.mode i ServiceClass.
        /// Chain ustala "profile" na podstawie pierwszego rozkładu — kolejne muszą się zgadzać.
        /// Deadhead (PW/LP/ZN) jest neutralny w kontekście ServiceClass.
        /// </summary>
        private static bool IsCompatibleWithChain(List<int> chain, Timetable candidate, GeneratorSettings settings)
        {
            if (chain == null || chain.Count == 0) return true;
            if (candidate == null) return false;

            // Znajdź pierwszy rozkład z ustaloną composition/ServiceClass
            CompositionMode? establishedMode = null;
            CirculationValidator.ServiceClass? establishedClass = null;

            foreach (var tid in chain)
            {
                var tt = TimetableService.GetTimetable(tid);
                if (tt == null) continue;

                if (settings.respectCompositionMode && !establishedMode.HasValue && tt.composition != null)
                    establishedMode = tt.composition.mode;

                if (settings.respectServiceClass && !establishedClass.HasValue)
                {
                    var cls = CirculationValidator.GetServiceClass(tt.irjCategory.group);
                    if (cls != CirculationValidator.ServiceClass.Deadhead)
                        establishedClass = cls;
                }

                if (establishedMode.HasValue && establishedClass.HasValue) break;
            }

            // Filtr composition
            if (settings.respectCompositionMode && establishedMode.HasValue
                && candidate.composition != null
                && candidate.composition.mode != establishedMode.Value)
                return false;

            // Filtr ServiceClass (deadhead candidate zawsze OK)
            if (settings.respectServiceClass && establishedClass.HasValue)
            {
                var candClass = CirculationValidator.GetServiceClass(candidate.irjCategory.group);
                if (candClass != CirculationValidator.ServiceClass.Deadhead
                    && candClass != establishedClass.Value)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Przypisuje najlepszy pasujący pojazd z floty do propozycji. Kryteria:
        /// 1. Typ pasuje do composition.mode pierwszego rozkładu (EMU→EMU/DMU, LocoWithCars→Loko)
        /// 2. Pojazd wolny (status != InRepair/OutOfService, conditionPercent >= 10)
        /// 3. Nie przypisany do innego aktywnego obiegu w dni obiegu (kalendarz rozłączny)
        /// 4. Najlepsza kondycja wśród pasujących
        /// </summary>
        private static void AssignBestVehicle(ProposedCirculation proposed,
            System.Collections.Generic.HashSet<int> batchReservedVehicles = null)
        {
            if (proposed == null || proposed.timetableIds.Count == 0) return;

            var firstTt = TimetableService.GetTimetable(proposed.timetableIds[0]);
            if (firstTt?.composition == null)
            {
                proposed.vehicleAssignmentInfo = "brak info o taborze pierwszego rozkładu";
                return;
            }

            var targetMode = firstTt.composition.mode;
            var targetMask = new DayMask { bits = proposed.dayMaskBits };

            FleetVehicleData best = null;
            float bestCondition = -1f;

            foreach (var v in FleetService.OwnedVehicles)
            {
                if (v == null) continue;

                // BUG-064: skip pojazd już zarezerwowany w bieżącym batch'u Generate()
                if (batchReservedVehicles != null && batchReservedVehicles.Contains(v.id)) continue;

                // Filtr typu pod composition.mode
                bool typeMatches = targetMode == CompositionMode.MultipleUnit
                    ? (v.type == FleetVehicleType.EMU || v.type == FleetVehicleType.DMU)
                    : (v.type == FleetVehicleType.ElectricLocomotive || v.type == FleetVehicleType.DieselLocomotive);
                if (!typeMatches) continue;

                // Filtr sprawności
                if (v.status == FleetVehicleStatus.InRepair
                 || v.status == FleetVehicleStatus.OutOfService
                 || v.conditionPercent < 10f) continue;

                // Filtr dostępności kalendarzowej — pojazd nie może być w innym aktywnym obiegu
                // z nakładającym się kalendarzem
                bool calendarConflict = false;
                foreach (var other in CirculationService.Circulations)
                {
                    if (other == null) continue;
                    if (other.status != CirculationStatus.Active && other.status != CirculationStatus.Paused) continue;
                    if (!other.ContainsVehicle(v.id)) continue;
                    if ((other.calendar.bits & targetMask.bits) != 0)
                    {
                        calendarConflict = true;
                        break;
                    }
                }
                if (calendarConflict) continue;

                // Najlepsza kondycja wygrywa
                if (v.conditionPercent > bestCondition)
                {
                    bestCondition = v.conditionPercent;
                    best = v;
                }
            }

            if (best != null)
            {
                proposed.suggestedVehicleId = best.id;
                proposed.vehicleAssignmentInfo = $"{best.series} {best.number} (kond. {best.conditionPercent:F0}%)";
                // BUG-064: rezerwuj pojazd dla bieżącego batch'u
                batchReservedVehicles?.Add(best.id);
            }
            else
            {
                proposed.suggestedVehicleId = -1;
                string typeStr = targetMode == CompositionMode.MultipleUnit ? "EMU/DMU" : "lokomotywy";
                proposed.vehicleAssignmentInfo = $"brak wolnych {typeStr} w flocie";
            }
        }

        /// <summary>Gap w minutach między końcem A i startem B (z rolloverem).</summary>
        private static int ComputeGapMinutes(Timetable a, Timetable b)
        {
            int aEnd = a.EndMinutes;
            int bStart = b.StartMinutes;
            if (bStart < aEnd) bStart += 24 * 60;
            return bStart - aEnd;
        }

        /// <summary>Konwertuje łańcuch rozkładów na ProposedCirculation.</summary>
        private static ProposedCirculation BuildProposedFromChain(List<int> chain)
        {
            if (chain == null || chain.Count == 0) return null;

            var first = TimetableService.GetTimetable(chain[0]);
            var last = TimetableService.GetTimetable(chain[chain.Count - 1]);
            if (first == null || last == null) return null;

            string startName = first.FirstStop?.stationName ?? "?";
            string endName = last.LastStop?.stationName ?? "?";

            // Intersection kalendarzy
            byte maskBits = first.calendar.bits;
            foreach (var tid in chain)
            {
                var t = TimetableService.GetTimetable(tid);
                if (t != null) maskBits &= t.calendar.bits;
            }

            // Duration: od startu pierwszego do końca ostatniego (z rolloverem)
            int firstStart = first.StartMinutes;
            int lastEnd = last.EndMinutes;
            if (lastEnd < firstStart) lastEnd += 24 * 60;
            int durationMin = lastEnd - firstStart;

            string name;
            if (chain.Count == 1)
                name = $"Obieg {startName} → {endName}";
            else if (startName == endName)
                name = $"Obieg pętla {startName} ({chain.Count} kursów)";
            else
                name = $"Obieg {startName} → ... → {endName} ({chain.Count} kursów)";

            return new ProposedCirculation
            {
                suggestedName = name,
                timetableIds = new List<int>(chain),
                dayMaskBits = maskBits,
                startStationName = startName,
                endStationName = endName,
                totalDurationMinutes = durationMin
            };
        }
    }
}
