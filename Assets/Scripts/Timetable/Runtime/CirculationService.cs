using System;
using System.Collections.Generic;
using RailwayManager.Core;
using RailwayManager.Fleet;

namespace RailwayManager.Timetable
{
    /// <summary>
    /// Statyczny singleton zarządzający obiegami składów. CRUD + rejestr + events.
    /// Walidacje konfliktów są w <see cref="CirculationValidator"/> (Etap 7).
    /// Generator TrainRun'ów jest w <c>TrainRunGenerator</c> (Etap 10).
    ///
    /// Patrz <c>memory/circulations_m5_design.md</c>.
    /// </summary>
    public static class CirculationService
    {
        /// <summary>Wszystkie obiegi (Draft + Active + Paused + Archived).</summary>
        public static List<Circulation> Circulations { get; } = new();

        private static int _nextId = 1;

        // ── Events ─────────────────────────────────────
        public static event Action OnCirculationsChanged;
        public static event Action<Circulation> OnCirculationAdded;
        public static event Action<Circulation> OnCirculationRemoved;
        public static event Action<Circulation> OnCirculationStatusChanged;

        // ── CRUD ────────────────────────────────────────

        /// <summary>Dodaje nowy obieg. Przydziela id, wywołuje events.</summary>
        public static Circulation AddCirculation(Circulation circulation)
        {
            if (circulation == null) return null;
            circulation.id = _nextId++;
            Circulations.Add(circulation);
            Log.Info($"[CirculationService] Dodano obieg #{circulation.id} '{circulation.name}' "
                     + $"({circulation.StepCount} kroków, status {circulation.status})");
            OnCirculationAdded?.Invoke(circulation);
            OnCirculationsChanged?.Invoke();
            return circulation;
        }

        /// <summary>Znajduje obieg po id.</summary>
        public static Circulation GetCirculation(int id)
        {
            foreach (var c in Circulations)
                if (c != null && c.id == id) return c;
            return null;
        }

        /// <summary>
        /// Crash-hunt #1A: naprawia dangling cross-module referencje po load (zwł. PartialLoad, gdzie
        /// jeden moduł padł i zostawił niespójność): pojazd z assignedCirculationId do nieistniejącego
        /// obiegu → -1; obieg z vehicleId spoza floty → usunięty z assignmentu. Wołane przez
        /// SaveOrchestrator PO wszystkich modułach (order-independent). Zwraca liczbę napraw.
        /// </summary>
        public static int RepairDanglingReferences()
        {
            int repaired = 0;

            // Pojazd → nieistniejący obieg.
            foreach (var v in RailwayManager.Fleet.FleetService.OwnedVehicles)
            {
                if (v != null && v.assignedCirculationId >= 0 && GetCirculation(v.assignedCirculationId) == null)
                {
                    v.assignedCirculationId = -1;
                    repaired++;
                }
            }

            // Obieg → vehicleId spoza floty (usuwamy z per-day assignmentu).
            foreach (var c in Circulations)
            {
                if (c?.vehicleAssignmentsPerDay == null) continue;
                foreach (var kv in c.vehicleAssignmentsPerDay)
                {
                    var list = kv.Value;
                    if (list == null) continue;
                    int before = list.Count;
                    list.RemoveAll(vid => RailwayManager.Fleet.FleetService.GetOwnedById(vid) == null);
                    repaired += before - list.Count;
                }
            }

            if (repaired > 0)
                Log.Info($"[CirculationService] RepairDanglingReferences: naprawiono {repaired} dangling cross-module ref po load.");
            return repaired;
        }

        /// <summary>Usuwa obieg. Wywołuje events.</summary>
        public static bool RemoveCirculation(int id)
        {
            var c = GetCirculation(id);
            if (c == null) return false;
            Circulations.Remove(c);
            Log.Info($"[CirculationService] Usunięto obieg #{id} '{c.name}'");
            OnCirculationRemoved?.Invoke(c);
            OnCirculationsChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// Zmienia status obiegu i wywołuje event. Nie waliduje (to robi UI/validator).
        /// Side effects:
        /// - Draft → Active: wywołuje <see cref="TrainRunGenerator.GenerateForCirculation"/>
        ///   + locks <c>Timetable.composition.assignment = Concrete</c> dla wszystkich
        ///   rozkładów w obiegu + wypełnia <c>assignedVehicleIds</c> z vehicleAssignmentsPerDay.
        /// - Active → Draft/Paused/Archived: czyści TrainRun'y tego obiegu + unlockuje
        ///   composition z powrotem do Symbolic.
        /// </summary>
        public static void SetStatus(int id, CirculationStatus newStatus)
        {
            var c = GetCirculation(id);
            if (c == null) return;
            if (c.status == newStatus) return;
            var oldStatus = c.status;
            c.status = newStatus;
            Log.Info($"[CirculationService] Obieg #{id} status {oldStatus} → {newStatus}");

            // Draft → Active: generate runs + lock composition
            if (oldStatus != CirculationStatus.Active && newStatus == CirculationStatus.Active)
            {
                // M9c-D F6: walidacja home station BLOKUJE aktywację. Wcześniej tylko log warn —
                // UI blokował, ale nie-UI callery (auto-generator, debug, save rehydration) mogły
                // aktywować niespójny obieg → pociąg spawnowałby się w złym miejscu.
                var homeIssues = CirculationValidator.ValidateHomeStation(c.steps);
                foreach (var issue in homeIssues)
                {
                    if (issue.severity == CirculationValidator.IssueSeverity.Error)
                    {
                        Log.Warn($"[CirculationService] Obieg #{id} NIE aktywowany — {issue.message}");
                        c.status = oldStatus; // revert — obieg pozostaje w poprzednim statusie
                        return;
                    }
                    Log.Info($"[CirculationService] Obieg #{id} aktywacja z ostrzeżeniem: {issue.message}");
                }

                TrainRunGenerator.GenerateForCirculation(c);
                LockCompositionForCirculation(c);
            }
            // Active → anything else: clear runs + unlock composition
            else if (oldStatus == CirculationStatus.Active && newStatus != CirculationStatus.Active)
            {
                TrainRunGenerator.ClearForCirculation(c.id);
                UnlockCompositionForCirculation(c);
            }

            OnCirculationStatusChanged?.Invoke(c);
            OnCirculationsChanged?.Invoke();
        }

        /// <summary>
        /// Locks composition.assignment = Concrete dla wszystkich Timetable'i w obiegu
        /// i wypełnia assignedVehicleIds na podstawie vehicleAssignmentsPerDay (union).
        /// Ten Timetable nie jest już "symboliczny" — gracz powiedział jakie pojazdy
        /// go wykonują.
        /// </summary>
        private static void LockCompositionForCirculation(Circulation c)
        {
            if (c?.steps == null || c.vehicleAssignmentsPerDay == null) return;

            // Union vehicleIds ze wszystkich dni
            var unionIds = new HashSet<int>();
            foreach (var list in c.vehicleAssignmentsPerDay.Values)
                if (list != null) foreach (var id in list) unionIds.Add(id);

            foreach (var step in c.steps)
            {
                var tt = TimetableService.GetTimetable(step.timetableId);
                if (tt?.composition == null) continue;
                tt.composition.assignment = CompositionAssignment.Concrete;
                tt.composition.assignedVehicleIds = new List<int>(unionIds);
            }

            // Sync primary assignment w FleetVehicleData
            foreach (var vid in unionIds)
            {
                foreach (var v in RailwayManager.Fleet.FleetService.OwnedVehicles)
                {
                    if (v != null && v.id == vid)
                    {
                        if (v.assignedCirculationId < 0) v.assignedCirculationId = c.id;
                        break;
                    }
                }
            }
            Log.Info($"[CirculationService] Lock composition.Concrete dla obiegu #{c.id}: "
                     + $"{c.steps.Count} rozkładów, {unionIds.Count} unikalnych pojazdów");
        }

        /// <summary>
        /// Unlocks composition dla Timetable'i w obiegu — z powrotem do Symbolic.
        /// Jeśli pojazd ma assignedCirculationId = ten obieg, czyścimy primary.
        /// </summary>
        private static void UnlockCompositionForCirculation(Circulation c)
        {
            if (c?.steps == null) return;
            foreach (var step in c.steps)
            {
                var tt = TimetableService.GetTimetable(step.timetableId);
                if (tt?.composition == null) continue;
                // UWAGA: zostaw Concrete jeśli ten Timetable jest w innym obiegu też
                // (edge case — obecnie pool schedule to pojedynczy obieg per rozkład).
                tt.composition.assignment = CompositionAssignment.Symbolic;
                tt.composition.assignedVehicleIds = new List<int>();
            }

            // Wyczyść primary FleetVehicleData.assignedCirculationId
            foreach (var v in RailwayManager.Fleet.FleetService.OwnedVehicles)
            {
                if (v != null && v.assignedCirculationId == c.id)
                    v.assignedCirculationId = -1;
            }
            Log.Info($"[CirculationService] Unlock composition dla obiegu #{c.id} → Symbolic");
        }

        // ── Query helpers ───────────────────────────────

        /// <summary>Zwraca wszystkie obiegi używające podanego rozkładu (w jakimkolwiek kroku).</summary>
        public static List<Circulation> GetCirculationsUsingTimetable(int timetableId)
        {
            var result = new List<Circulation>();
            foreach (var c in Circulations)
            {
                if (c?.steps == null) continue;
                foreach (var s in c.steps)
                {
                    if (s.timetableId == timetableId)
                    {
                        result.Add(c);
                        break;
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// Zwraca wszystkie obiegi (Active lub Paused) do których przypisany jest podany pojazd.
        /// Draft i Archived są pomijane — nie generują TrainRun'ów.
        /// </summary>
        public static List<Circulation> GetCirculationsForVehicle(int vehicleId)
        {
            var result = new List<Circulation>();
            foreach (var c in Circulations)
            {
                if (c == null) continue;
                if (c.status == CirculationStatus.Draft || c.status == CirculationStatus.Archived) continue;
                if (c.assignedVehicleIds != null && c.assignedVehicleIds.Contains(vehicleId))
                    result.Add(c);
            }
            return result;
        }

        /// <summary>
        /// Zwraca listę niesprawnych pojazdów przypisanych do obiegu. Niesprawność to:
        /// - FleetVehicleStatus.InRepair, OutOfService
        /// - Lub conditionPercent &lt; 10 (awaria per M7 design spec)
        /// Używane przez CirculationListUI do wyświetlenia wykrzyknika w wierszu obiegu.
        /// </summary>
        public static List<FleetVehicleData> GetBrokenVehiclesInCirculation(Circulation c)
        {
            var result = new List<FleetVehicleData>();
            if (c?.vehicleAssignmentsPerDay == null) return result;

            var seen = new HashSet<int>();
            foreach (var kvp in c.vehicleAssignmentsPerDay)
            {
                if (kvp.Value == null) continue;
                foreach (var vid in kvp.Value)
                {
                    if (!seen.Add(vid)) continue;
                    foreach (var v in FleetService.OwnedVehicles)
                    {
                        if (v == null || v.id != vid) continue;
                        bool broken = v.status == FleetVehicleStatus.InRepair
                                   || v.status == FleetVehicleStatus.OutOfService
                                   || v.conditionPercent < 10f;
                        if (broken) result.Add(v);
                        break;
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// Znajduje listę rozkładów które nie są w żadnym obiegu (sieroty).
        /// Używane przez pasek ostrzeżeń w CirculationListUI.
        /// </summary>
        public static List<Timetable> GetOrphanedTimetables()
        {
            var inUse = new HashSet<int>();
            foreach (var c in Circulations)
            {
                if (c?.steps == null) continue;
                foreach (var s in c.steps)
                    inUse.Add(s.timetableId);
            }

            var orphans = new List<Timetable>();
            foreach (var t in TimetableService.Timetables)
            {
                if (t == null) continue;
                if (t.status != TimetableStatus.Active) continue; // ignoruj suspended/archived
                if (!inUse.Contains(t.id))
                    orphans.Add(t);
            }
            return orphans;
        }

        /// <summary>
        /// Sprawdza czy przypisanie pojazdu V do obiegu C byłoby konfliktem z istniejącymi obiegami.
        /// (LEGACY API — zwraca ogólną listę kolidujących obiegów na podstawie DayMask.)
        /// Nowszy flow używa <see cref="GetVehicleConflictForDate"/> per-date.
        /// </summary>
        public static List<Circulation> CheckVehicleAssignmentConflicts(int vehicleId, Circulation targetCirculation)
        {
            var conflicts = new List<Circulation>();
            if (targetCirculation == null) return conflicts;

            foreach (var other in Circulations)
            {
                if (other == null || other == targetCirculation) continue;
                if (other.status != CirculationStatus.Active && other.status != CirculationStatus.Paused) continue;
                if (!other.ContainsVehicle(vehicleId)) continue;

                // Konflikt jeśli kalendarze się pokrywają
                if ((other.calendar.bits & targetCirculation.calendar.bits) != 0)
                    conflicts.Add(other);
            }
            return conflicts;
        }

        /// <summary>
        /// Zwraca set dozwolonych typów pojazdów na podstawie <c>composition.mode</c> rozkładów
        /// obiegu. Gdy obieg jest pusty (bez kroków) lub ma mix trybów → null = wszystkie
        /// typy dozwolone. Inaczej:
        /// - Tylko MultipleUnit w rozkładach → EMU + DMU
        /// - Tylko LocoWithCars w rozkładach → ElectricLocomotive + DieselLocomotive + PassengerCar
        /// </summary>
        public static HashSet<FleetVehicleType> GetAllowedVehicleTypes(Circulation circulation)
        {
            if (circulation?.steps == null || circulation.steps.Count == 0) return null;

            bool hasMultipleUnit = false;
            bool hasLocoWithCars = false;
            foreach (var step in circulation.steps)
            {
                var tt = TimetableService.GetTimetable(step.timetableId);
                if (tt?.composition == null) continue;
                if (tt.composition.mode == CompositionMode.MultipleUnit) hasMultipleUnit = true;
                else if (tt.composition.mode == CompositionMode.LocoWithCars) hasLocoWithCars = true;
            }

            if (hasMultipleUnit && hasLocoWithCars) return null; // mix — wszystko dopuszczalne
            if (!hasMultipleUnit && !hasLocoWithCars) return null; // brak info o kompozycji

            var allowed = new HashSet<FleetVehicleType>();
            if (hasMultipleUnit)
            {
                allowed.Add(FleetVehicleType.EMU);
                allowed.Add(FleetVehicleType.DMU);
            }
            if (hasLocoWithCars)
            {
                allowed.Add(FleetVehicleType.ElectricLocomotive);
                allowed.Add(FleetVehicleType.DieselLocomotive);
                allowed.Add(FleetVehicleType.PassengerCar);
            }
            return allowed;
        }

        /// <summary>
        /// Usuwa z <c>vehicleAssignmentsPerDay</c> pojazdy których typ jest niezgodny
        /// z wymaganiami rozkładów obiegu (zgodnie z <see cref="GetAllowedVehicleTypes"/>).
        /// Wywoływane przy dodaniu rozkładu do obiegu — user nie musi ręcznie czyścić.
        /// Zwraca liczbę usuniętych przypisań.
        /// </summary>
        public static int PruneIncompatibleVehicles(Circulation circulation)
        {
            if (circulation?.vehicleAssignmentsPerDay == null) return 0;
            var allowed = GetAllowedVehicleTypes(circulation);
            if (allowed == null) return 0; // wszystkie dozwolone, nic nie usuwamy

            int removed = 0;
            var emptyDays = new List<string>();
            foreach (var kvp in circulation.vehicleAssignmentsPerDay)
            {
                if (kvp.Value == null) continue;
                for (int i = kvp.Value.Count - 1; i >= 0; i--)
                {
                    int vid = kvp.Value[i];
                    FleetVehicleData v = null;
                    foreach (var fv in FleetService.OwnedVehicles)
                        if (fv != null && fv.id == vid) { v = fv; break; }
                    if (v == null) { kvp.Value.RemoveAt(i); removed++; continue; }
                    if (!allowed.Contains(v.type))
                    {
                        kvp.Value.RemoveAt(i);
                        removed++;
                    }
                }
                if (kvp.Value.Count == 0) emptyDays.Add(kvp.Key);
            }
            foreach (var d in emptyDays) circulation.vehicleAssignmentsPerDay.Remove(d);

            // Sync legacy assignedVehicleIds (union wszystkich dni)
            if (removed > 0)
            {
                var union = new HashSet<int>();
                foreach (var list in circulation.vehicleAssignmentsPerDay.Values)
                    if (list != null) foreach (var id in list) union.Add(id);
                circulation.assignedVehicleIds = new List<int>(union);
                Log.Info($"[CirculationService] PruneIncompatible: usunięto {removed} przypisań z obiegu #{circulation.id} (niepasujący typ)");
            }
            return removed;
        }

        /// <summary>
        /// Sprawdza czy dany pojazd jest zajęty przez inny obieg w konkretnej dacie.
        /// Zwraca pierwszy znaleziony obieg konfliktu LUB null jeśli wolny.
        /// Pomija <paramref name="excludeCirculationId"/> (sam obieg edytowany).
        /// </summary>
        public static Circulation GetVehicleConflictForDate(int vehicleId, string dateIso, int excludeCirculationId = -1)
        {
            foreach (var c in Circulations)
            {
                if (c == null) continue;
                if (c.id == excludeCirculationId) continue;
                if (c.status == CirculationStatus.Archived) continue;
                var list = c.GetVehiclesForDate(dateIso);
                if (list != null && list.Contains(vehicleId))
                    return c;
            }
            return null;
        }

        /// <summary>
        /// TD-032: czy KTÓRYKOLWIEK z pojazdów jest dziś w obiegu Active/Paused (warn sprzęg/rozprzęg
        /// — operacja zmieni kompozycję kursu; gracz może kontynuować = „pozwól + ostrzeż"). Instalowane
        /// jako <c>DepotMovementSimulator.CirculationWarnHook</c> przez <see cref="CouplingCirculationBootstrapper"/>.
        /// </summary>
        public static bool IsConsistInActiveCirculationToday(List<int> vehicleIds)
        {
            if (vehicleIds == null || vehicleIds.Count == 0) return false;
            string today = GameState.CurrentDateIso;
            foreach (var c in Circulations)
            {
                if (c == null) continue;
                if (c.status != CirculationStatus.Active && c.status != CirculationStatus.Paused) continue;
                var list = c.GetVehiclesForDate(today);
                if (list == null) continue;
                for (int i = 0; i < vehicleIds.Count; i++)
                    if (list.Contains(vehicleIds[i])) return true;
            }
            return false;
        }

        // ── Reset (np. na nowy save) ────────────────────
        /// <summary>Czyści wszystkie obiegi. Używane przy ładowaniu save'a lub nowym game.</summary>
        public static void Reset()
        {
            Circulations.Clear();
            _nextId = 1;
            OnCirculationsChanged?.Invoke();
        }

        // ── BUG-078: Save/Load support ──────────────────
        /// <summary>BUG-078: Aktualny licznik ID dla save (żeby nie kolizjować po load).</summary>
        public static int GetNextId() => _nextId;

        /// <summary>
        /// BUG-078: restore state z save. Zachowuje _nextId żeby nowe obiegi nie kolizjowały
        /// z istniejącymi ID po load. Wzorzec analogiczny do
        /// <see cref="ModernizationJobService.RestoreFromSave"/>.
        /// </summary>
        public static void RestoreFromSave(System.Collections.Generic.IList<Circulation> circulations, int nextId)
        {
            Circulations.Clear();
            if (circulations != null)
            {
                foreach (var c in circulations)
                    if (c != null) Circulations.Add(c);
            }
            _nextId = nextId > 0 ? nextId : 1;
            OnCirculationsChanged?.Invoke();
        }
    }
}
