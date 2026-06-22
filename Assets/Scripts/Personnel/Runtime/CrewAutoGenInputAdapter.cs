using System.Collections.Generic;
using UnityEngine;
using RailwayManager.Timetable;
using TimetableObj = RailwayManager.Timetable.Timetable;

namespace RailwayManager.Personnel
{
    /// <summary>
    /// M11 AS-P2: planowany „M8-11 adapter" — most z realnych TrainRunów (Timetable) do
    /// wejścia generatora turnusów. Do tej pory oba call-site'y karmiły generator wyłącznie
    /// fixture debugowym (DebugCreateSampleTimetable), a toggle OFF dawał pustą listę.
    ///
    /// Kursy dostawcze (isDeliveryRun) pomijane — deadhead bez załogi gracza (M9c-D F4).
    /// Czysty odczyt: NIE mutuje TimetableService (bezpieczne dla Plan() asystenta — AS-4).
    /// </summary>
    public static class CrewAutoGenInputAdapter
    {
        /// <summary>Buduje wejścia generatora dla wszystkich kursów danego dnia (yyyy-MM-dd).</summary>
        public static List<CrewAutoGenTrainRunInput> BuildInputsFromTrainRuns(string dateIso)
        {
            var result = new List<CrewAutoGenTrainRunInput>();
            if (string.IsNullOrEmpty(dateIso)) return result;

            foreach (var run in TimetableService.TrainRuns)
            {
                if (run == null || run.isDeliveryRun) continue;
                if (run.runDateIso != dateIso) continue;

                var tt = TimetableService.GetTimetable(run.timetableId);
                if (tt == null || tt.stops == null || tt.stops.Count < 2) continue;

                int durationMin = Mathf.Max(0, tt.EndMinutes - tt.StartMinutes);
                int startMin = run.startMinutesFromMidnight;
                // Rollover nocny: HH:MM:SS zawsze w obrębie doby (generator porównuje czasy w dobie).
                int endMin = (startMin + durationMin) % (24 * 60);

                var input = new CrewAutoGenTrainRunInput
                {
                    trainRunId = run.id,
                    circulationId = run.circulationId,
                    startStation = tt.FirstStop?.stationName ?? "?",
                    endStation = tt.LastStop?.stationName ?? "?",
                    startTimeIso = FormatTime(startMin),
                    endTimeIso = FormatTime(endMin),
                    dateIso = dateIso,
                    irjCategory = tt.irjCategory.ToString() // kod EI/RO/... (IrjCategoryCatalog.GetCode)
                };
                FillCompositionCounts(tt, input);
                result.Add(input);
            }
            return result;
        }

        /// <summary>Minuty od północy → "HH:MM:00" (format wejścia generatora).</summary>
        public static string FormatTime(int minutesFromMidnight)
        {
            int m = ((minutesFromMidnight % 1440) + 1440) % 1440;
            return $"{m / 60:00}:{m % 60:00}:00";
        }

        /// <summary>
        /// Liczniki składu dla heurystyk generatora (D16: &gt;3 wagony = wymaga konduktora).
        /// Concrete → realne typy pojazdów z floty; Symbolic → mode + litera trakcji
        /// (EZT/SZT = 1 jednostka), loko+wagony → parse zapisu symbolicznego ("3B+WR+2A" → 6).
        /// </summary>
        public static void FillCompositionCounts(TimetableObj tt, CrewAutoGenTrainRunInput input)
        {
            var comp = tt.composition;

            if (comp != null && comp.assignedVehicleIds != null && comp.assignedVehicleIds.Count > 0)
            {
                foreach (var vid in comp.assignedVehicleIds)
                {
                    var v = RailwayManager.Fleet.FleetService.GetOwnedById(vid);
                    if (v == null) continue;
                    switch (v.type)
                    {
                        case RailwayManager.Fleet.FleetVehicleType.EMU: input.emuCount++; break;
                        case RailwayManager.Fleet.FleetVehicleType.DMU: input.dmuCount++; break;
                        case RailwayManager.Fleet.FleetVehicleType.PassengerCar: input.passengerCarsCount++; break;
                    }
                }
                if (input.emuCount + input.dmuCount + input.passengerCarsCount > 0) return;
            }

            if (comp == null || comp.mode == CompositionMode.MultipleUnit)
            {
                if (tt.irjCategory.traction == TractionLetter.DieselUnit) input.dmuCount = 1;
                else input.emuCount = 1;
            }
            else
            {
                input.passengerCarsCount = ParseSymbolicCarCount(comp.symbolicNotation);
            }
        }

        /// <summary>"3B+WR+2A" → 6 (suma członów; token bez liczby = 1). Null/pusty → 0.</summary>
        public static int ParseSymbolicCarCount(string notation)
        {
            if (string.IsNullOrWhiteSpace(notation)) return 0;
            int total = 0;
            foreach (var rawToken in notation.Split('+'))
            {
                var token = rawToken.Trim();
                if (token.Length == 0) continue;
                int digits = 0;
                while (digits < token.Length && char.IsDigit(token[digits])) digits++;
                total += digits > 0 && int.TryParse(token.Substring(0, digits), out int n) ? n : 1;
            }
            return total;
        }
    }
}
