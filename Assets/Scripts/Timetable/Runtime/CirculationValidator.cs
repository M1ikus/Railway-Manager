using System.Collections.Generic;
using RailwayManager;
using RailwayManager.Core;

namespace RailwayManager.Timetable
{
    /// <summary>
    /// Walidacja sekwencji kursów w obiegu. Używane przez CirculationCreatorUI
    /// w trybie live (przy każdym drag&drop) + przy zatwierdzaniu obiegu.
    ///
    /// Dwa rodzaje wyników:
    /// - <see cref="SequenceIssue"/> — problem ze spójnością (blok lub ostrzeżenie)
    /// - <see cref="GetNextStepErrors"/> — czy dany Timetable można dodać jako kolejny krok
    ///
    /// Walidacja pojazdu (konflikty kalendarzy) jest osobno w <see cref="CirculationService.CheckVehicleAssignmentConflicts"/>.
    /// </summary>
    public static class CirculationValidator
    {
        // ── Reverse margin constants (z design spec) ────
        public const int ReverseMarginEmuMinutes = 2;
        public const int ReverseMarginLocoMinutes = 10;

        /// <summary>
        /// Klasa usługi rozkładu — grupuje kategorie IRJ w 'poziomy' które nie mogą
        /// być mieszane w jednym obiegu. Deadhead (PW/LP/LT/LS/ZN) jest neutralna —
        /// można łączyć z każdą inną klasą.
        /// </summary>
        public enum ServiceClass { Local, Fast, Express, Freight, Deadhead }

        /// <summary>Mapuje IrjGroup na ServiceClass.</summary>
        public static ServiceClass GetServiceClass(IrjGroup group)
        {
            switch (group)
            {
                // Local — osobowy
                case IrjGroup.RegionalLocal:
                case IrjGroup.InterregionalLocal:
                case IrjGroup.RegionalInternational:
                    return ServiceClass.Local;

                // Fast — pospieszny / aglomeracyjny
                case IrjGroup.RegionalFast:
                case IrjGroup.InterregionalFast:
                case IrjGroup.InterregionalFastNight:
                case IrjGroup.RegionalAgglomeration:
                    return ServiceClass.Fast;

                // Express — ekspresowy
                case IrjGroup.ExpressDomestic:
                case IrjGroup.ExpressInternational:
                case IrjGroup.ExpressInternationalNight:
                case IrjGroup.InternationalFast:
                    return ServiceClass.Express;

                // Freight — towarowy
                case IrjGroup.FreightIntlIntermodal:
                case IrjGroup.FreightIntlMass:
                case IrjGroup.FreightIntlNonMass:
                case IrjGroup.FreightDomesticIntermodal:
                case IrjGroup.FreightDomesticMass:
                case IrjGroup.FreightDomesticNonMass:
                case IrjGroup.FreightStationService:
                case IrjGroup.FreightEmptyTest:
                    return ServiceClass.Freight;

                // Deadhead — neutralna klasa łącząca (próżny pasażerski, lok luzem, utrzymanie)
                case IrjGroup.EmptyPassenger:
                case IrjGroup.EmptyPassengerTest:
                case IrjGroup.LoneLocoPassenger:
                case IrjGroup.LoneLocoFreight:
                case IrjGroup.LoneLocoShunt:
                case IrjGroup.MaintenanceInspection:
                    return ServiceClass.Deadhead;

                default:
                    return ServiceClass.Local;
            }
        }

        /// <summary>Human-readable nazwa klasy (do komunikatów błędu).</summary>
        public static string GetServiceClassName(ServiceClass cls)
        {
            return cls switch
            {
                ServiceClass.Local => "osobowy",
                ServiceClass.Fast => "pospieszny",
                ServiceClass.Express => "ekspresowy",
                ServiceClass.Freight => "towarowy",
                ServiceClass.Deadhead => "dojazd służbowy",
                _ => cls.ToString()
            };
        }

        /// <summary>Severity walidacji.</summary>
        public enum IssueSeverity
        {
            /// <summary>Krok nie może być dodany / obieg nie może być zatwierdzony.</summary>
            Error,
            /// <summary>Krok może być dodany ale z ostrzeżeniem (np. krótki czas obrotu).</summary>
            Warning
        }

        /// <summary>Problem z sekwencją obiegu.</summary>
        public readonly struct SequenceIssue
        {
            public readonly int stepIndex;      // index kroku którego dotyczy (0-based)
            public readonly IssueSeverity severity;
            public readonly string message;

            public SequenceIssue(int stepIndex, IssueSeverity severity, string message)
            {
                this.stepIndex = stepIndex;
                this.severity = severity;
                this.message = message;
            }
        }

        /// <summary>
        /// Pełna walidacja sekwencji kroków obiegu. Zwraca wszystkie problemy.
        /// Pustą listę = wszystko OK.
        /// </summary>
        public static List<SequenceIssue> ValidateSequence(List<CirculationStep> steps)
        {
            var issues = new List<SequenceIssue>();
            if (steps == null || steps.Count < 2) return issues;

            for (int i = 0; i < steps.Count - 1; i++)
            {
                var current = steps[i];
                var next = steps[i + 1];
                var issuesForPair = ValidatePair(current, next, i + 1);
                issues.AddRange(issuesForPair);
            }
            return issues;
        }

        /// <summary>
        /// M9c walidacja: pierwszy krok obiegu MUSI startować z home depot station gracza.
        /// Ostrzeżenie gdy ostatni krok nie kończy w home (soft — pojazd po prostu zostanie
        /// na peronie końcowej stacji, nie wróci do depot).
        /// Pusta lista gdy <see cref="GameState.HomeDepotStationId"/> nie ustawione (grace period
        /// przed DepotLocationPicker).
        /// </summary>
        public static List<SequenceIssue> ValidateHomeStation(List<CirculationStep> steps)
        {
            var issues = new List<SequenceIssue>();
            if (GameState.HomeDepotStationId < 0) return issues;
            if (steps == null || steps.Count == 0) return issues;

            var firstTimetable = TimetableService.GetTimetable(steps[0].timetableId);
            if (firstTimetable != null)
            {
                var firstRoute = TimetableService.GetRoute(firstTimetable.routeId);
                if (firstRoute != null && firstRoute.stations != null && firstRoute.stations.Count > 0)
                {
                    int startStationId = firstRoute.stations[0].stationNodeId;
                    if (startStationId != GameState.HomeDepotStationId)
                    {
                        issues.Add(new SequenceIssue(0, IssueSeverity.Error,
                            $"Pierwszy krok obiegu musi startować z Twojej zajezdni " +
                            $"(station#{GameState.HomeDepotStationId}). Obecnie startuje z " +
                            $"station#{startStationId} ('{firstRoute.stations[0].stationName}')"));
                    }
                }
            }

            // Warning: ostatni krok kończy poza home
            int lastIdx = steps.Count - 1;
            var lastTimetable = TimetableService.GetTimetable(steps[lastIdx].timetableId);
            if (lastTimetable != null)
            {
                var lastRoute = TimetableService.GetRoute(lastTimetable.routeId);
                if (lastRoute != null && lastRoute.stations != null && lastRoute.stations.Count > 0)
                {
                    int endStationId = lastRoute.stations[lastRoute.stations.Count - 1].stationNodeId;
                    if (endStationId != GameState.HomeDepotStationId)
                    {
                        issues.Add(new SequenceIssue(lastIdx, IssueSeverity.Warning,
                            $"Ostatni krok obiegu nie kończy w Twojej zajezdni — " +
                            $"pojazd pozostanie na peronie stacji '{lastRoute.stations[lastRoute.stations.Count - 1].stationName}' " +
                            $"po zakończeniu kursu."));
                    }
                }
            }

            return issues;
        }

        /// <summary>
        /// Sprawdza czy podany Timetable może być dodany jako kolejny krok po ostatnim
        /// kroku sekwencji. Zwraca listę błędów (pusta = OK do dodania).
        /// Używane przez CirculationCreatorUI na żywo przy drag&drop.
        /// </summary>
        public static List<SequenceIssue> GetNextStepErrors(
            List<CirculationStep> currentSteps, int candidateTimetableId)
        {
            var issues = new List<SequenceIssue>();
            var candidate = TimetableService.GetTimetable(candidateTimetableId);
            if (candidate == null)
            {
                issues.Add(new SequenceIssue(-1, IssueSeverity.Error, $"Rozkład #{candidateTimetableId} nie istnieje"));
                return issues;
            }

            if (currentSteps == null || currentSteps.Count == 0)
            {
                // Pierwszy krok — wszystko OK, nie ma z czym walidować
                return issues;
            }

            // Walidacja compositionMode: wszystkie rozkłady w obiegu muszą być tego samego typu taboru
            var compositionError = ValidateCompositionMode(currentSteps, candidate);
            if (compositionError.HasValue)
            {
                issues.Add(compositionError.Value);
                return issues;
            }

            // Walidacja ServiceClass: nie można mieszać Local + Express itp. (deadhead jest neutralny)
            var classError = ValidateServiceClass(currentSteps, candidate);
            if (classError.HasValue)
            {
                issues.Add(classError.Value);
                return issues;
            }

            // Walidacja sekwencji (stacje się spinają + ordering czasowy + reverse margin)
            var lastStep = currentSteps[currentSteps.Count - 1];
            var pseudoNext = new CirculationStep(candidateTimetableId, StepKind.Commercial);
            return ValidatePair(lastStep, pseudoNext, currentSteps.Count);
        }

        /// <summary>
        /// Czy candidate ma composition.mode zgodny z ustalonym już trybem obiegu.
        /// Zwraca Error jeśli nie. Null jeśli OK lub brak danych composition.
        /// </summary>
        private static SequenceIssue? ValidateCompositionMode(
            List<CirculationStep> currentSteps, Timetable candidate)
        {
            if (candidate?.composition == null) return null;

            // Znajdź pierwszy rozkład w obiegu z określonym compositionMode
            CompositionMode? establishedMode = null;
            foreach (var step in currentSteps)
            {
                var tt = TimetableService.GetTimetable(step.timetableId);
                if (tt?.composition == null) continue;
                establishedMode = tt.composition.mode;
                break;
            }

            if (!establishedMode.HasValue) return null;
            if (establishedMode.Value == candidate.composition.mode) return null;

            string established = establishedMode.Value == CompositionMode.MultipleUnit ? "EMU/DMU" : "Lok+Wagony";
            string newType = candidate.composition.mode == CompositionMode.MultipleUnit ? "EMU/DMU" : "Lok+Wagony";
            return new SequenceIssue(currentSteps.Count, IssueSeverity.Error,
                $"Niezgodny typ taboru: obieg wymaga {established}, nowy rozkład to {newType}");
        }

        /// <summary>
        /// Czy candidate ma zgodną ServiceClass. Deadhead zawsze OK. Inaczej: tylko jeśli
        /// żaden dotychczasowy rozkład nie jest w innej klasie (pomijając deadheady).
        /// </summary>
        private static SequenceIssue? ValidateServiceClass(
            List<CirculationStep> currentSteps, Timetable candidate)
        {
            var candidateClass = GetServiceClass(candidate.irjCategory.group);
            // Deadhead zawsze dozwolony
            if (candidateClass == ServiceClass.Deadhead) return null;

            // Znajdź pierwszą ustaloną klasę w obiegu (pomijając deadheady)
            ServiceClass? establishedClass = null;
            foreach (var step in currentSteps)
            {
                var tt = TimetableService.GetTimetable(step.timetableId);
                if (tt == null) continue;
                var cls = GetServiceClass(tt.irjCategory.group);
                if (cls == ServiceClass.Deadhead) continue;
                establishedClass = cls;
                break;
            }

            if (!establishedClass.HasValue) return null;
            if (establishedClass.Value == candidateClass) return null;

            return new SequenceIssue(currentSteps.Count, IssueSeverity.Error,
                $"Niezgodna klasa usługi: obieg ma rozkłady klasy '{GetServiceClassName(establishedClass.Value)}', "
                + $"nowy rozkład to '{GetServiceClassName(candidateClass)}' (nie można mieszać)");
        }

        // ─────────────────────────────────────────────
        //  Internal
        // ─────────────────────────────────────────────

        /// <summary>Waliduje parę (krok N, krok N+1). stepIndex = index kroku N+1.</summary>
        private static List<SequenceIssue> ValidatePair(
            CirculationStep prev, CirculationStep next, int nextStepIndex)
        {
            var issues = new List<SequenceIssue>();

            var prevTt = TimetableService.GetTimetable(prev.timetableId);
            var nextTt = TimetableService.GetTimetable(next.timetableId);

            if (prevTt == null || nextTt == null)
            {
                issues.Add(new SequenceIssue(nextStepIndex, IssueSeverity.Error,
                    $"Nieistniejący rozkład w kroku {nextStepIndex}"));
                return issues;
            }

            // 1. Spójność stacji: stacja końcowa N musi być = startowej N+1
            var prevEnd = prevTt.LastStop?.stationName;
            var nextStart = nextTt.FirstStop?.stationName;
            if (string.IsNullOrEmpty(prevEnd) || string.IsNullOrEmpty(nextStart))
            {
                issues.Add(new SequenceIssue(nextStepIndex, IssueSeverity.Error,
                    "Rozkład nie ma zdefiniowanych postojów"));
                return issues;
            }
            if (prevEnd != nextStart)
            {
                issues.Add(new SequenceIssue(nextStepIndex, IssueSeverity.Error,
                    $"Stacje się nie spinają: '{prevEnd}' → '{nextStart}'"));
                return issues;
            }

            // 2. Czas: N+1 musi startować po zakończeniu N
            int prevEndMin = prevTt.EndMinutes;
            int nextStartMin = nextTt.StartMinutes;
            // Jeśli N+1 jest w następnej dobie (np. noc → ranek), rolluj
            int effectiveNextStart = nextStartMin;
            if (effectiveNextStart < prevEndMin)
                effectiveNextStart += 24 * 60;

            int gapMin = effectiveNextStart - prevEndMin;
            if (gapMin < 0)
            {
                issues.Add(new SequenceIssue(nextStepIndex, IssueSeverity.Error,
                    $"Kurs startuje przed końcem poprzedniego (przyjazd {FmtHHMM(prevEndMin)}, odjazd {FmtHHMM(nextStartMin)})"));
                return issues;
            }

            // 3. Reverse margin (ostrzeżenie, nie blok)
            int requiredMargin = GetReverseMargin(prevTt, nextTt);
            if (gapMin < requiredMargin)
            {
                issues.Add(new SequenceIssue(nextStepIndex, IssueSeverity.Warning,
                    $"Krótki czas obrotu ({gapMin} min, wymagane minimum {requiredMargin} min) — kurs może startować z opóźnieniem"));
                // Nie return — ostrzeżenie jest informacyjne, gracz może zatwierdzić
            }

            return issues;
        }

        /// <summary>Określa minimum czasu obrotu między dwoma rozkładami.</summary>
        private static int GetReverseMargin(Timetable prev, Timetable next)
        {
            // Jeśli oba są EMU (MultipleUnit) → 2 min, inaczej 10 min (Lok+Wagon)
            bool prevEmu = prev.composition?.mode == CompositionMode.MultipleUnit;
            bool nextEmu = next.composition?.mode == CompositionMode.MultipleUnit;
            return (prevEmu && nextEmu) ? ReverseMarginEmuMinutes : ReverseMarginLocoMinutes;
        }

        private static string FmtHHMM(int totalMinutes)
        {
            int h = (totalMinutes / 60) % 24;
            int m = totalMinutes % 60;
            return $"{h:D2}:{m:D2}";
        }
    }
}
