using System;
using RailwayManager.Core;
using System.Collections.Generic;

namespace RailwayManager.Timetable
{
    /// <summary>
    /// Obieg składu — łańcuch rozkładów (TimetableId) wykonywanych sekwencyjnie przez ten sam
    /// fizyczny tabor w ciągu doby. Obieg istnieje jako szablon sekwencji niezależnie od pojazdu:
    /// <c>Draft</c> (bez pojazdu, nie generuje TrainRun'ów) → <c>Active</c> (z pojazdem, działa).
    ///
    /// Model: jeden obieg = jeden <see cref="DayMask"/>. Wariancja "w poniedziałek inaczej niż
    /// we wtorek" realizowana przez **wiele obiegów z rozłącznymi kalendarzami** (Podejście B
    /// z design spec) — walidacja konfliktów pojazdów uwzględnia intersection kalendarzy.
    ///
    /// Patrz <c>memory/circulations_m5_design.md</c>.
    /// </summary>
    [Serializable]
    public class Circulation
    {
        public int id;
        public string name;                       // "Obieg Olsztyn pn-pt" lub user-defined

        /// <summary>Sekwencja kursów w obiegu. Kolejność istotna — N+1 startuje po zakończeniu N.</summary>
        public List<CirculationStep> steps = new();

        /// <summary>
        /// (LEGACY — do usunięcia gdy per-day assignment się ustabilizuje)
        /// Globalna lista przypisanych pojazdów, używana przez stary flow assignu.
        /// Nowy flow: <see cref="vehicleAssignmentsPerDay"/>.
        /// </summary>
        public List<int> assignedVehicleIds = new();

        /// <summary>
        /// Nowy model przypisania pojazdów (M5 Etap 9 refactor): per-day dict.
        /// Klucz = data ISO "YYYY-MM-DD", wartość = lista vehicleId tworzących skład
        /// (pociąg) dla tego dnia. Może być np. [lok_id, wagon_id, wagon_id].
        /// Pusty dict = obieg Draft bez przypisań.
        /// </summary>
        public Dictionary<string, List<int>> vehicleAssignmentsPerDay = new();

        /// <summary>
        /// Kalendarz kursowania obiegu — dominuje (Podejście B). Nakłada się na DayMask
        /// poszczególnych rozkładów, ale walidacja sprawdza tylko rozłączność kalendarzy
        /// między obiegami tego samego pojazdu.
        /// </summary>
        public DayMask calendar = DayMask.Daily();

        /// <summary>Cykliczność tygodniowa — analogiczna do <see cref="Timetable.weeksValid"/>.</summary>
        public int weeksValid = 4;

        /// <summary>
        /// Jeśli true, obieg jest jednorazowy — kursuje tylko jeden konkretny dzień
        /// (<see cref="oneTimeDateIso"/>) i potem automatycznie przechodzi w Archived.
        /// Gdy true, <see cref="calendar"/> i <see cref="weeksValid"/> są ignorowane.
        /// </summary>
        public bool isOneTime;

        /// <summary>Data jednorazowego kursu w formacie ISO (YYYY-MM-DD). Pusta gdy <see cref="isOneTime"/>=false.</summary>
        public string oneTimeDateIso;

        /// <summary>Status obiegu. Draft → Active → Paused → Archived.</summary>
        public CirculationStatus status = CirculationStatus.Draft;

        /// <summary>Notatki gracza.</summary>
        public string notes;

        // ── Helpery ──────────────────────────────

        /// <summary>Liczba kroków w obiegu.</summary>
        public int StepCount => steps?.Count ?? 0;

        /// <summary>Czy obieg ma przypisany co najmniej jeden pojazd w dowolnym dniu.</summary>
        public bool HasVehicle
        {
            get
            {
                if (vehicleAssignmentsPerDay != null)
                    foreach (var list in vehicleAssignmentsPerDay.Values)
                        if (list != null && list.Count > 0) return true;
                return assignedVehicleIds != null && assignedVehicleIds.Count > 0;
            }
        }

        /// <summary>
        /// Zwraca listę dat (System.DateTime, jako date-only) w których obieg obowiązuje.
        /// Dla jednorazowego: [parsed oneTimeDateIso] albo pusty jeśli data jest nieparsowalna.
        /// Dla powtarzalnego: od dziś przez weeksValid * 7 dni, filtrowane wg DayMask.
        /// weeksValid = 0 (bezterminowo) → default 4 tygodnie do wyświetlenia.
        /// </summary>
        public List<System.DateTime> GetActiveDates()
        {
            var result = new List<System.DateTime>();
            if (isOneTime)
            {
                if (!string.IsNullOrEmpty(oneTimeDateIso)
                    && IsoTime.TryParseDate(oneTimeDateIso, out var parsedDate))
                {
                    result.Add(parsedDate.Date);
                }
                return result;
            }

            // Recurring — daty w kalendarzu gry (GameStartDateIso), nie realnym.
            // Guard (crash-hunt V2): GameStartDateIso z uszkodzonego/edytowanego save'a może być
            // niepoprawna — TryParseDate zamiast rzucającego ParseDate (symetrycznie do oneTime wyżej).
            if (!IsoTime.TryParseDate(RailwayManager.Core.GameState.GameStartDateIso, out var start))
                return result;
            int totalDays = weeksValid > 0 ? weeksValid * 7 : 28;
            for (int i = 0; i < totalDays; i++)
            {
                var date = start.AddDays(i);
                // C# DayOfWeek: Sunday=0, Monday=1..Saturday=6
                // Nasz DayMask: Monday=bit0, Sunday=bit6
                int dayIdx = date.DayOfWeek == System.DayOfWeek.Sunday
                    ? 6
                    : (int)date.DayOfWeek - 1;
                if (calendar.Runs(dayIdx))
                    result.Add(date);
            }
            return result;
        }

        /// <summary>
        /// Zwraca listę pojazdów przypisanych do konkretnej daty (jeśli są).
        /// Pusta lista jeśli brak przypisania. Nigdy nie zwraca null.
        /// </summary>
        public List<int> GetVehiclesForDate(string dateIso)
        {
            if (vehicleAssignmentsPerDay != null
                && vehicleAssignmentsPerDay.TryGetValue(dateIso, out var list)
                && list != null)
                return list;
            return new List<int>();
        }

        /// <summary>Czy pojazd jest przypisany do tego obiegu w dowolnym dniu.</summary>
        public bool ContainsVehicle(int vehicleId)
        {
            if (vehicleAssignmentsPerDay != null)
                foreach (var list in vehicleAssignmentsPerDay.Values)
                    if (list != null && list.Contains(vehicleId)) return true;
            return assignedVehicleIds != null && assignedVehicleIds.Contains(vehicleId);
        }

        /// <summary>Zwraca listę dat ISO w których dany pojazd jest przypisany.</summary>
        public List<string> GetDatesForVehicle(int vehicleId)
        {
            var result = new List<string>();
            if (vehicleAssignmentsPerDay != null)
                foreach (var kvp in vehicleAssignmentsPerDay)
                    if (kvp.Value != null && kvp.Value.Contains(vehicleId))
                        result.Add(kvp.Key);
            return result;
        }

        /// <summary>Czy obieg jest trywialny (1-krokowy) — utworzony przez bezpośrednie przypisanie pojazd→rozkład w FleetPanelUI.</summary>
        public bool IsTrivial => StepCount == 1;
    }

    /// <summary>
    /// Pojedynczy krok obiegu — odniesienie do istniejącego <see cref="Timetable"/>.
    /// Kroki są sekwencyjne; walidator sprawdza czy stacja końcowa N == start N+1 oraz
    /// czy N+1 startuje po N (z marginesem reverse time).
    /// </summary>
    [Serializable]
    public class CirculationStep
    {
        /// <summary>ID rozkładu z <c>TimetableService.Timetables</c>.</summary>
        public int timetableId;

        /// <summary>Typ kroku (tag UX — logicznie Commercial i Deadhead są równe).</summary>
        public StepKind kind = StepKind.Commercial;

        public CirculationStep() { }
        public CirculationStep(int timetableId, StepKind kind = StepKind.Commercial)
        {
            this.timetableId = timetableId;
            this.kind = kind;
        }
    }

    /// <summary>Typ kroku w obiegu — rozróżnienie UX między zwykłym kursem a dojazdem służbowym.</summary>
    public enum StepKind
    {
        /// <summary>Normalny kurs handlowy (pasażerski/towarowy).</summary>
        Commercial,

        /// <summary>Kurs służbowy — dojazd pusty (PW) / lok luzem (LP/LT). Kategoria handlowa "sluzbowy".</summary>
        Deadhead
    }

    /// <summary>Status obiegu wpływający na generację TrainRun'ów i UI.</summary>
    public enum CirculationStatus
    {
        /// <summary>Sekwencja ułożona, brak pojazdu. Nie generuje TrainRun'ów.</summary>
        Draft,

        /// <summary>Obieg aktywny, pojazd przypisany, generuje TrainRun'y w oknie 7 dni.</summary>
        Active,

        /// <summary>Tymczasowo wstrzymany (np. przegląd pojazdu). Zachowuje przypisanie pojazdu.</summary>
        Paused,

        /// <summary>Zakończony/historyczny — nie generuje nowych TrainRun'ów.</summary>
        Archived
    }
}
