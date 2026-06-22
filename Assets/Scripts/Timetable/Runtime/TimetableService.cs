using System;
using System.Collections.Generic;
using RailwayManager.Core;

namespace RailwayManager.Timetable
{
    /// <summary>
    /// Centralny runtime state modułu Timetable — trasy, rozkłady, kategorie handlowe,
    /// pamięć postojów, rejestry rezerwacji. Statyczny singleton w stylu FleetService,
    /// przeżywa zmiany sceny (Depot ↔ Map), żyje do końca sesji gry.
    /// </summary>
    public static class TimetableService
    {
        // ── State ──────────────────────────────────────
        public static List<Route> Routes { get; } = new();
        public static List<Timetable> Timetables { get; } = new();
        public static List<CommercialCategory> CommercialCategories { get; } = new();
        public static List<TrainRun> TrainRuns { get; } = new();

        /// <summary>Rezerwacje peronów per platformId (int).</summary>
        public static ReservationRegistry<int> PlatformReservations { get; } = new();

        /// <summary>Rezerwacje segmentów toru per segmentId (int z RailwayGraph). LEGACY — używaj BlockSectionReservations.</summary>
        public static ReservationRegistry<int> SegmentReservations { get; } = new();

        /// <summary>Rezerwacje odcinków blokowych per sectionId (z BlockSectionGraph).</summary>
        public static ReservationRegistry<int> BlockSectionReservations { get; } = new();

        /// <summary>
        /// Pamięć postojów per (routeHash, commercialCategoryId). Gdy gracz robi drugi
        /// rozkład tej samej trasy w tej samej kategorii handlowej, pre-fill postojów.
        /// Globalna per gracz, dzielona przez wszystkie zajezdnie.
        /// </summary>
        private static readonly Dictionary<string, List<int>> _stopMemory = new();

        public static int NextRouteId { get; private set; } = 1;
        public static int NextTimetableId { get; private set; } = 1;
        public static int NextTrainRunId { get; private set; } = 1;

        /// <summary>Rezerwuje kolejne id TrainRun'a i zwraca je. Używane przez TrainRunGenerator.</summary>
        public static int AllocateTrainRunId()
        {
            return NextTrainRunId++;
        }
        public static bool IsInitialized { get; private set; }

        // ── Events ─────────────────────────────────────
        public static event Action OnTimetablesChanged;
        public static event Action OnRoutesChanged;

        // ── Initialization ─────────────────────────────
        public static void ResetForSaveLoad()
        {
            Routes.Clear();
            Timetables.Clear();
            TrainRuns.Clear();
            CommercialCategories.Clear();
            PlatformReservations.Clear();
            SegmentReservations.Clear();
            BlockSectionReservations.Clear();
            _stopMemory.Clear();
            NextRouteId = 1;
            NextTimetableId = 1;
            NextTrainRunId = 1;
            IsInitialized = false;
            OnRoutesChanged?.Invoke();
            OnTimetablesChanged?.Invoke();
        }

        public static void ResetForNewGame()
        {
            ResetForSaveLoad();
        }

        public static void MarkInitializedFromSave()
        {
            IsInitialized = true;
            OnRoutesChanged?.Invoke();
            OnTimetablesChanged?.Invoke();
        }

        /// <summary>
        /// Public API dla TimetableSavable — restore counter'ów po load. Zastępuje wcześniejszy
        /// reflection na backing fields auto-properties (`<NextRouteId>k__BackingField`...) który
        /// był silently no-op gdy property zostałoby zrenamed.
        ///
        /// Wartości &lt;1 są klampowane do 1 (counter musi być &gt;=1 bo allokuje pre-increment).
        /// </summary>
        public static void RestoreCountersFromSave(int nextRouteId, int nextTimetableId, int nextTrainRunId)
        {
            NextRouteId = nextRouteId > 0 ? nextRouteId : 1;
            NextTimetableId = nextTimetableId > 0 ? nextTimetableId : 1;
            NextTrainRunId = nextTrainRunId > 0 ? nextTrainRunId : 1;
        }

        /// <summary>
        /// TD-037: public API dla TrainRunsSavable — restore pełnej listy TrainRuns (statyczne +
        /// runtime pola, Z ICH ID — `CrewDuty.referencedTrainRunId` w personnel linkuje po id,
        /// więc runy serializujemy zamiast regenerować). Wołane PO TimetableSavable
        /// (ResetForSaveLoad już wyczyścił listę) — kolejność z SaveRegistry.ModuleOrder.
        /// </summary>
        public static void RestoreTrainRunsFromSave(IList<TrainRun> runs)
        {
            TrainRuns.Clear();
            if (runs != null)
            {
                foreach (var r in runs)
                    if (r != null) TrainRuns.Add(r);
            }
        }

        public static void Initialize()
        {
            if (IsInitialized) return;

            // Domyślne kategorie handlowe — gracz może później modyfikować i dodawać własne.
            CommercialCategories.Clear();
            CommercialCategories.Add(new CommercialCategory
            {
                id = "os", displayName = "Osobowy", shortCode = "Os",
                basePriceZl = 8f, pricePerKmZl = 0.25f,
                minStopSeconds = 30, trafficPriority = 1,
                defaultCompositionMode = CompositionMode.MultipleUnit,
                suggestedMaxSpeedKmh = 120,
                defaultStopPolicy = StopPolicy.AllStations
            });
            CommercialCategories.Add(new CommercialCategory
            {
                id = "rp", displayName = "Przyspieszony", shortCode = "Przysp",
                basePriceZl = 12f, pricePerKmZl = 0.35f,
                minStopSeconds = 60, trafficPriority = 2,
                defaultCompositionMode = CompositionMode.MultipleUnit,
                suggestedMaxSpeedKmh = 140,
                defaultStopPolicy = StopPolicy.MajorStationsOnly
            });
            CommercialCategories.Add(new CommercialCategory
            {
                id = "ic", displayName = "InterCity", shortCode = "IC",
                basePriceZl = 30f, pricePerKmZl = 0.5f,
                minStopSeconds = 120, trafficPriority = 5,
                defaultCompositionMode = CompositionMode.LocoWithCars,
                suggestedMaxSpeedKmh = 160,
                requiresAirConditioning = true, requiresCatering = true,
                defaultStopPolicy = StopPolicy.ManualPerRoute
            });
            CommercialCategories.Add(new CommercialCategory
            {
                id = "eip", displayName = "Express InterCity Premium", shortCode = "EIP",
                basePriceZl = 60f, pricePerKmZl = 0.7f,
                minStopSeconds = 120, trafficPriority = 7,
                defaultCompositionMode = CompositionMode.MultipleUnit,
                suggestedMaxSpeedKmh = 200,
                requiresAirConditioning = true, requiresWiFi = true,
                requiresPowerSockets = true, requiresCatering = true,
                defaultStopPolicy = StopPolicy.ManualPerRoute
            });
            // Kategoria 'Służbowy' — używana przez obiegi M5 jako dojazd pusty (PW/LP).
            // Gracz może stworzyć własną w CategoryEditorUI, ta domyślna to fallback.
            CommercialCategories.Add(new CommercialCategory
            {
                id = "sluzbowy", displayName = "Kurs służbowy (dojazd)", shortCode = "Służb.",
                basePriceZl = 0f, pricePerKmZl = 0f,
                minStopSeconds = 15, trafficPriority = 1,
                defaultCompositionMode = CompositionMode.MultipleUnit,
                suggestedMaxSpeedKmh = 120,
                defaultStopPolicy = StopPolicy.MajorStationsOnly,
                notes = "Dojazd pusty (PW) lub lok luzem (LP/LT) dla obiegów M5. Nie generuje przychodu."
            });

            IsInitialized = true;
            Log.Info("[TimetableService] Initialized with "
                     + $"{CommercialCategories.Count} commercial categories");
        }

        // ── Route management ──────────────────────────
        public static Route AddRoute(Route route)
        {
            route.id = NextRouteId++;
            Routes.Add(route);
            OnRoutesChanged?.Invoke();
            return route;
        }

        public static Route GetRoute(int id)
        {
            foreach (var r in Routes) if (r.id == id) return r;
            return null;
        }

        // ── Timetable management ──────────────────────
        public static Timetable AddTimetable(Timetable tt)
        {
            tt.id = NextTimetableId++;
            Timetables.Add(tt);
            // M-TimetableUX F1.17: increment progress counter (unlock Advanced mode po N timetables)
            RailwayManager.SharedUI.PlayerProgressService.RecordTimetableCreated();
            OnTimetablesChanged?.Invoke();
            return tt;
        }

        public static Timetable GetTimetable(int id)
        {
            foreach (var t in Timetables) if (t.id == id) return t;
            return null;
        }

        public static CommercialCategory GetCommercialCategory(string id)
        {
            foreach (var c in CommercialCategories) if (c.id == id) return c;
            return null;
        }

        // ── CRUD kategorii handlowych (kreator gracza) ─────
        /// <summary>Dodaje nową kategorię handlową. Zwraca false jeśli id już istnieje.</summary>
        public static bool AddCommercialCategory(CommercialCategory cat)
        {
            if (cat == null || string.IsNullOrWhiteSpace(cat.id)) return false;
            if (GetCommercialCategory(cat.id) != null) return false;
            CommercialCategories.Add(cat);
            return true;
        }

        /// <summary>
        /// Usuwa kategorię handlową o podanym id. Zwraca false jeśli kategoria
        /// jest używana przez jakikolwiek rozkład (zabezpieczenie przed sierotami).
        /// </summary>
        public static bool RemoveCommercialCategory(string id)
        {
            var cat = GetCommercialCategory(id);
            if (cat == null) return false;
            foreach (var t in Timetables)
                if (t != null && t.commercialCategoryId == id) return false;
            CommercialCategories.Remove(cat);
            return true;
        }

        /// <summary>Czy kategoria jest używana przez jakikolwiek rozkład (do UI feedback).</summary>
        public static int CountTimetablesUsingCategory(string id)
        {
            int n = 0;
            foreach (var t in Timetables)
                if (t != null && t.commercialCategoryId == id) n++;
            return n;
        }

        /// <summary>Zwraca set wszystkich aktualnie używanych numerów pociągów (do walidacji).</summary>
        public static HashSet<string> GetActiveTrainNumbers()
        {
            var set = new HashSet<string>();
            foreach (var t in Timetables)
                if (!string.IsNullOrEmpty(t.trainNumber))
                    set.Add(t.trainNumber);
            return set;
        }

        // ── Stop memory ───────────────────────────────
        /// <summary>Zapamiętuje które stacje miały postój dla danej (trasa × kategoria handlowa).</summary>
        public static void SaveStopMemory(string routeHash, string commercialCategoryId, List<int> stationNodeIds)
        {
            if (string.IsNullOrEmpty(routeHash) || string.IsNullOrEmpty(commercialCategoryId)) return;
            string key = $"{routeHash}|{commercialCategoryId}";
            _stopMemory[key] = new List<int>(stationNodeIds);
        }

        /// <summary>Odczytuje zapamiętane postoje dla danej (trasa × kategoria). null jeśli brak.</summary>
        public static List<int> LoadStopMemory(string routeHash, string commercialCategoryId)
        {
            if (string.IsNullOrEmpty(routeHash) || string.IsNullOrEmpty(commercialCategoryId)) return null;
            string key = $"{routeHash}|{commercialCategoryId}";
            return _stopMemory.TryGetValue(key, out var list) ? list : null;
        }

        // ── Reservations ─────────────────────────────
        /// <summary>Rezerwuje peron dla rozkładu. Wywołuj po walidacji kolizji.</summary>
        public static void ReservePlatform(int platformId, long startGameTime, long endGameTime,
                                           int trainRunId, int timetableId)
        {
            PlatformReservations.Add(platformId, new Reservation
            {
                startGameTimeSec = startGameTime,
                endGameTimeSec = endGameTime,
                trainRunId = trainRunId,
                timetableId = timetableId
            });
        }

        /// <summary>Rezerwuje segment toru dla przejazdu. LEGACY — używaj ReserveBlockSection.</summary>
        public static void ReserveSegment(int segmentId, long startGameTime, long endGameTime,
                                          int trainRunId, int timetableId)
        {
            SegmentReservations.Add(segmentId, new Reservation
            {
                startGameTimeSec = startGameTime,
                endGameTimeSec = endGameTime,
                trainRunId = trainRunId,
                timetableId = timetableId
            });
        }

        /// <summary>Rezerwuje odcinek blokowy.</summary>
        public static void ReserveBlockSection(int sectionId, long startGameTime, long endGameTime,
                                               int trainRunId, int timetableId)
        {
            BlockSectionReservations.Add(sectionId, new Reservation
            {
                startGameTimeSec = startGameTime,
                endGameTimeSec = endGameTime,
                trainRunId = trainRunId,
                timetableId = timetableId
            });
        }

        /// <summary>Czyści wszystkie rezerwacje powiązane z rozkładem (np. przy usunięciu).</summary>
        public static int ClearReservationsForTimetable(int timetableId)
        {
            int total = 0;
            total += PlatformReservations.RemoveByTimetable(timetableId);
            total += SegmentReservations.RemoveByTimetable(timetableId);
            total += BlockSectionReservations.RemoveByTimetable(timetableId);
            return total;
        }
    }
}
