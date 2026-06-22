using System.Collections.Generic;
using UnityEngine;

namespace RailwayManager.Core
{
    /// <summary>
    /// Statyczny kontener na stan gry współdzielony między scenami.
    /// Pola statyczne żyją w pamięci do końca sesji — nie wymagają DontDestroyOnLoad.
    /// </summary>
    public static class GameState
    {
        // ── Czas gry (świat) ────────────────────────────────────────

        /// <summary>Czas gry w ramach doby [0–86400). Inkrementowany w <see cref="GameClock.Update"/>.</summary>
        public static float GameTimeSeconds = 6f * 3600f; // 06:00

        /// <summary>
        /// Mnożnik prędkości świata (x1/x5/x25/x150/x500).
        /// Używany przez <see cref="GameClock"/> do awansowania GameTimeSeconds
        /// oraz przez TrainRunSimulator (M9a) do symulacji ruchu.
        /// </summary>
        public static float TimeScale = 1f;

        /// <summary>
        /// True gdy świat jest zatrzymany (GameClock nie inkrementuje GameTimeSeconds,
        /// simulatory return early). Pause aktywne gdy:
        /// - <c>_legacyPause</c> = true (TopBar pause button, GameCreator pauseOnStart), LUB
        /// - <see cref="PauseStack"/> ma jakichkolwiek właścicieli (popup'y, alerty)
        ///
        /// Setter pisze tylko do `_legacyPause` (backwards compat). Nowe call sites
        /// powinny używać <see cref="PauseStack.Push"/>/<see cref="PauseStack.Pop"/>
        /// dla explicit ownership — eliminuje bug-pattern gdzie zamknięcie jednego popup'a
        /// odpauzowuje świat z aktywnym alertem.
        /// </summary>
        public static bool IsPaused
        {
            get => _legacyPause || PauseStack.HasOwners;
            set => _legacyPause = value;
        }
        private static bool _legacyPause = false;

        /// <summary>Bieżący dzień gry (0 = pierwszy dzień, inkrementowany o północy).</summary>
        public static int GameDay = 0;

        /// <summary>Data startu gry w formacie ISO (YYYY-MM-DD). Ustawiana przy tworzeniu nowej gry.</summary>
        public static string GameStartDateIso = "2026-03-15";

        /// <summary>Bieżąca data gry jako ISO string (computed z GameStartDateIso + GameDay).</summary>
        public static string CurrentDateIso =>
            IsoTime.ParseDate(GameStartDateIso).AddDays(GameDay).ToString("yyyy-MM-dd");

        /// <summary>
        /// MONOTONICZNY absolutny czas gry w sekundach = GameDay×86400 + GameTimeSeconds.
        /// W PRZECIWIEŃSTWIE do <see cref="GameTimeSeconds"/> NIE resetuje się o północy.
        /// **Używać dla wszystkich timerów wielodobowych** (produkcja 30 dni, dostawa, przeglądy,
        /// odświeżanie rynku). Porównywanie estimatedCompletionGameTime z gołym GameTimeSeconds
        /// (wewnątrz-dobowym) to bug — target nigdy nieosiągalny (crash-hunt: produkcja się nie kończy).
        /// Wzorzec wcześniej zduplikowany w ~10 miejscach (FleetMarketRefreshService, FleetPanelUI itd.).
        /// </summary>
        public static long TotalGameSeconds => GameDay * 86400L + (long)GameTimeSeconds;

        // ── Czas zajezdni (osobny TimeScale, max x5) ────────────────

        /// <summary>
        /// Niezależny TimeScale dla zajezdni 3D — cap na x5 (decyzja z M9 design spec).
        /// Zajezdnia WSPÓŁDZIELI GameTimeSeconds (ta sama pora dnia), ale manewry
        /// nie mogą się wykonywać szybciej niż x5 (problemy z fizyką w 3D).
        /// M9b DepotMovementSimulator będzie czytał tę wartość zamiast TimeScale.
        /// </summary>
        public static float DepotTimeScale => Mathf.Min(TimeScale, 5f);

        // ── Ekonomia + nazwa ────────────────────────────────────────

        /// <summary>
        /// Bilans gracza w PLN (zł). Mutowany przez wiele systemów (EconomyManager,
        /// PaymentService, PersonnelService, FleetService, *JobService).
        ///
        /// Property zamiast bare field żeby setter wyemitował <see cref="OnMoneyChanged"/>
        /// dla obserwatorów UI (TopBarUI bilans, NotificationService alert kasa &lt; X).
        /// `Money +=`/`-=` działa identycznie — kompilator rozszerza do get/set.
        ///
        /// **Nie waliduje na ujemne wartości** — niektóre flow (severance pay, kary)
        /// muszą móc pójść w minus. Wymuszenie non-negative wymagałoby decyzji
        /// gameplay'owej (game over przy minusie?) → M-Balance.
        /// </summary>
        public static long Money
        {
            get => _money;
            set
            {
                if (_money == value) return;
                long old = _money;
                _money = value;
                OnMoneyChanged?.Invoke(old, value);
            }
        }
        private static long _money = 150000;

        /// <summary>
        /// Event emitowany przy każdej zmianie <see cref="Money"/>. Args: (oldValue, newValue).
        /// Subskrybenci: TopBarUI (refresh bilans), NotificationService (alert kasa &lt; X),
        /// AchievementsService (milestone bogactwa).
        /// </summary>
        public static event System.Action<long, long> OnMoneyChanged;

        public static string DepotName = "Zajezdnia Mokotow";

        // ── Determinism (MP-9, M-Performance 2026-05-06) ────────────

        /// <summary>
        /// Globalny seed gry — używany przez `RandomRegistry` do dystrybucji per-system seedów
        /// (BreakdownService, PassengerManager spawn, TrainRunSimulator breakdowns).
        /// Default 0 = deterministic; nowa gra może ustawić Environment.TickCount albo user-chosen.
        /// Persistowany w save/load przez <c>WorldSavable</c> (klucz "seed").
        /// **Krytyczne dla M10 MP** — sync między klientami wymaga tego samego seeda.
        /// </summary>
        public static int Seed = 0;

        // ── Predykcyjny dispatcher mapy OSM (M-Dispatch Faza 4b) ────

        /// <summary>
        /// Polityka autonomicznego dispatchera ruchu na mapie OSM. Ustawiana w kreatorze gry,
        /// zmienialna w trakcie przez ustawienia ogólne. Persistowana w save/load przez
        /// <c>WorldSavable</c> (klucz "dispatchPolicy"). Czytana przez dispatcher w Timetable
        /// (DispatchPolicyService). NIE dotyczy ruchu w zajezdni (ten ma osobnego dyżurnego).
        /// </summary>
        public static DispatchPolicy MapDispatchPolicy = DispatchPolicy.Balanced;

        // ── Home depot (M9c) ────────────────────────────────────────

        /// <summary>
        /// ID stacji OSM gdzie zlokalizowana jest zajezdnia gracza. Wszystkie obiegi
        /// muszą zaczynać i kończyć dzień na tej stacji (walidacja w M5 + handshake M9c).
        /// -1 = nie ustawione (gra przed GameCreator completion). Persistowane w save/load.
        ///
        /// Wolisz <see cref="IsHomeDepotSet"/> niż ręczne porównanie z -1 — jednoznaczny
        /// kontrakt zamiast sentinel comparison rozsianego po 6+ plikach.
        /// </summary>
        public static int HomeDepotStationId = -1;

        /// <summary>
        /// True gdy gracz ukończył kreator i wybrał home depot. False przed completion
        /// GameCreator'a lub po reset do nowej gry. Tańszy i czytelniejszy odpowiednik
        /// dla `HomeDepotStationId >= 0` (poprzednia konwencja używała magicznego -1).
        /// </summary>
        public static bool IsHomeDepotSet => HomeDepotStationId >= 0;

        // ── Playtime tracking ───────────────────────────────────────
        //
        // Total playtime = AccumulatedFromPriorSessions + (current process realtime
        // od momentu StartNewSession/ResumeSession). `Time.realtimeSinceStartup` zlicza
        // od process start (zawiera czas w MainMenu, GameCreator), więc nie można go
        // używać bezpośrednio. Marker `_sessionStartRealtime` pozwala odjąć ten
        // pre-game czas. Bez tego mechanizmu manifest.Playtime resetował się przy
        // każdym restarcie gry (load 100h save → next save zapisywał 30min zamiast
        // 100h30min).

        private static double _accumulatedPlaytime;
        private static float _sessionStartRealtime;

        /// <summary>Sumaryczny playtime w sekundach (poprzednie sesje + bieżąca aktywna).
        /// Używane przez SaveOrchestrator do `manifest.Playtime`.</summary>
        public static double GetTotalPlaytimeSec()
            => _accumulatedPlaytime + (Time.realtimeSinceStartup - _sessionStartRealtime);

        /// <summary>Reset accumulator + start nowej sesji. Wywoływane przez
        /// `WorldSavable.InitializeDefault` (nowa gra).</summary>
        public static void StartNewPlaytimeSession()
        {
            _accumulatedPlaytime = 0;
            _sessionStartRealtime = Time.realtimeSinceStartup;
        }

        /// <summary>Resume sesji po load save'a. Accumulator dostaje total z manifestu,
        /// session start ustawiony na current realtime żeby kolejne save'y dodawały
        /// tylko czas SPĘDZONY w grze post-load (nie cały realtimeSinceStartup, który
        /// zawiera ewentualny czas w MainMenu przed loadem).</summary>
        public static void ResumePlaytimeSession(double accumulatedFromSave)
        {
            _accumulatedPlaytime = accumulatedFromSave;
            _sessionStartRealtime = Time.realtimeSinceStartup;
        }

        // ── DLC (M-DLC, foundation 2026-05-04) ──────────────────────

        /// <summary>
        /// Lista aktywnych krajów DLC (ISO 3166-1 alpha-2, np. ["PL"], ["PL", "DE"]).
        /// Default: ["PL"] — base game zawiera Polskę.
        /// Po zakupie DLC dodawany jest kolejny kod kraju (np. "DE" dla Niemiec).
        ///
        /// Używane do filtrowania content'u (stacje/miasta widoczne w UI, dostępność
        /// home depot, render lockerów na unowned regions).
        ///
        /// Persistowane w save/load (DlcSavable — TODO M-DLC). Migration: stary save
        /// bez tego pola dostaje default ["PL"]. Save z DLC ładowany bez DLC dostaje
        /// graceful fallback (alert + ignored content).
        ///
        /// Patrz: docs/design/dlc-multi-country.md
        ///
        /// BUG-024: read-only view (IReadOnlyList) + dedicated mutator API. Wcześniej
        /// był public mutable List, dowolny caller mógł `Clear()` — łamie invariants.
        /// </summary>
        public static IReadOnlyList<string> ActiveDlcCountries => _activeDlcCountries;
        private static readonly List<string> _activeDlcCountries = new() { "PL" };

        /// <summary>Dodaje kraj do active DLC (idempotent — no-op gdy już aktywny).</summary>
        public static void AddDlcCountry(string iso3166_1)
        {
            if (string.IsNullOrEmpty(iso3166_1)) return;
            if (_activeDlcCountries.Contains(iso3166_1)) return;
            _activeDlcCountries.Add(iso3166_1);
        }

        /// <summary>
        /// Usuwa kraj z active DLC. Blokuje usunięcie ostatniego kraju (musi być min. 1
        /// dla działającej gry). Returns true jeśli usunięto.
        /// </summary>
        public static bool RemoveDlcCountry(string iso3166_1)
        {
            if (string.IsNullOrEmpty(iso3166_1)) return false;
            if (_activeDlcCountries.Count <= 1) return false; // protect last country
            return _activeDlcCountries.Remove(iso3166_1);
        }

        /// <summary>Reset DLC countries do base game (PL only) — używane przy nowej grze.</summary>
        public static void ResetDlcCountries()
        {
            _activeDlcCountries.Clear();
            _activeDlcCountries.Add("PL");
        }

        /// <summary>True gdy dany kraj jest aktywnie odblokowany (DLC posiadany lub base game).</summary>
        public static bool IsCountryActive(string iso3166_1)
        {
            if (string.IsNullOrEmpty(iso3166_1)) return false;
            return _activeDlcCountries.Contains(iso3166_1);
        }

        // ── Day cycle events (M6) ────────────────────────────────────

        /// <summary>
        /// Event: właśnie się zmienił GameDay (dzień zakończył się o północy).
        /// Args: iso data dnia właśnie zakończonego (przed increment'em).
        /// Subskrybują: EconomyManager (archiwizacja bilansu, dotacje),
        /// SubsidyCalculator (wypłaty), ReputationManager (daily summary).
        /// </summary>
        public static event System.Action<string> OnDayEnded;

        /// <summary>Wywołać gdy GameDay właśnie inkrementował (TopBarUI).</summary>
        public static void InvokeDayEnded(string dateJustEnded) => OnDayEnded?.Invoke(dateJustEnded);

        // ── Reputation (M6-6) ────────────────────────────────────────

        /// <summary>
        /// M6-6: Aktualna reputacja globalna 0-100 (mirror z Timetable.Economy.ReputationManager.Global).
        /// Duplikowane w Core żeby SharedUI.TopBarUI mógł wyświetlić pasek bez
        /// referencji do Timetable assembly.
        ///
        /// Property + event analogiczne do <see cref="Money"/> — TopBarUI nie musi pollować.
        /// </summary>
        public static int GlobalReputation
        {
            get => _globalReputation;
            set
            {
                // Clamp [0, 100] — invariant z M6-6 design (komentarz wyzej). Wartosci spoza
                // zakresu wczesniej byly silently akceptowane: np. ReputationManager mogl
                // delta'owac do 9999 albo -50 bez wymuszenia ograniczen. Setter chroni TopBarUI
                // (paski 0..100 normalize'owane przez Mathf.Clamp01(rep/100f)).
                int clamped = value < 0 ? 0 : value > 100 ? 100 : value;
                if (_globalReputation == clamped) return;
                int old = _globalReputation;
                _globalReputation = clamped;
                OnGlobalReputationChanged?.Invoke(old, clamped);
            }
        }
        private static int _globalReputation = 50;

        /// <summary>
        /// Event emitowany przy zmianie <see cref="GlobalReputation"/>. Args: (oldValue, newValue).
        /// Subskrybenci: TopBarUI (refresh pasek), NotificationService (alert spadek &lt; 30).
        ///
        /// Nazwa z prefiksem `Global` żeby odróżnić od per-instance
        /// <c>Timetable.Economy.ReputationManager.OnReputationChanged</c> (3-arg event z delta + reason).
        /// Ten Core event jest tylko mirror'em finalnej wartości dla UI.
        /// </summary>
        public static event System.Action<int, int> OnGlobalReputationChanged;
    }
}
