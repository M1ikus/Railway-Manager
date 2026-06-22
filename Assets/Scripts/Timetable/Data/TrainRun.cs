using System;
using System.Collections.Generic;

namespace RailwayManager.Timetable
{
    /// <summary>
    /// Pojedyncze wystąpienie rozkładu w konkretnym dniu i godzinie.
    /// Generowane z Timetable × FrequencySpec × DayMask.
    /// Fizyczny byt poruszający się po mapie w symulacji (runtime state).
    /// </summary>
    [Serializable]
    public class TrainRun
    {
        public int id;
        public int timetableId;                    // → Timetable.id

        /// <summary>ID obiegu który generuje ten TrainRun. -1 = standalone (M4 legacy).</summary>
        public int circulationId = -1;

        /// <summary>Indeks kroku w sekwencji obiegu (0-based). -1 = standalone.</summary>
        public int circulationStepIndex = -1;

        /// <summary>
        /// M9c-D F4: kurs dostawczy (punkt zakupu → home depot). Pomija crew check (deadhead bez
        /// załogi gracza). Po dojeździe do home handshake wprowadza pojazd do zajezdni; syntetyczny
        /// Route/Timetable/TrainRun jest sprzątany przez DeliveryService po despawnie.
        /// </summary>
        public bool isDeliveryRun;

        /// <summary>Data wystąpienia w formacie ISO (YYYY-MM-DD) — key do vehicleAssignmentsPerDay obiegu.</summary>
        public string runDateIso;

        /// <summary>Dzień wystąpienia (czas gry w sekundach, zaokrąglony do 00:00).</summary>
        public long runDateGameTime;

        /// <summary>Godzina startu (minuty od północy, 0..1439).</summary>
        public int startMinutesFromMidnight;

        /// <summary>Numer pociągu z Timetable w momencie generacji (snapshot na wypadek zmiany Timetable).</summary>
        public string trainNumberSnapshot;

        // ── Runtime (M9+) ────────────────────────
        /// <summary>Obecne opóźnienie w sekundach (0 = punktualnie, &gt;0 = spóźnienie).</summary>
        public int currentDelaySec;

        /// <summary>Odległość przejechana od startu trasy (metry).</summary>
        public float currentPositionOnRouteM;

        /// <summary>ID segmentu toru na którym pociąg się znajduje (-1 = nie wystartował / skończył).</summary>
        public int currentSegmentId = -1;

        /// <summary>Czy kurs został wykonany (zakończony w stacji końcowej).</summary>
        public bool isCompleted;

        /// <summary>Czy kurs został anulowany (np. awaria taboru, brak obsady).</summary>
        public bool isCancelled;

        /// <summary>
        /// M9c handshake: lista vehicleId które aktualnie wykonują ten kurs.
        /// Zapełnione gdy pociąg startuje (przez TrainRunSimulator.SpawnTrain lub
        /// SpawnTrainFromVehicles z handshake'u depot→map). Pusta przed startem
        /// i po zakończeniu. Używana przez VehicleLocationService do tranzycji state'u
        /// po OnRunDespawned.
        /// </summary>
        public List<int> runningVehicleIds = new();
    }
}
