using Newtonsoft.Json.Linq;
using UnityEngine;
using RailwayManager;
using RailwayManager.Core;
using RailwayManager.Core.Difficulty;
using RailwayManager.Core.GameRules;

namespace RailwayManager.SaveLoad.Modules
{
    /// <summary>
    /// M13-7: Persystencja świata (czas gry, money, depot identity, reputacja).
    /// M13-13: Rozszerzone o difficulty (D33+D35) i game rules (D36) — per-save,
    /// niemodyfikowalne mid-game.
    ///
    /// Module ID: "world". Schema v2. Pierwszy moduł w bundle (też jako sanity check
    /// — jeśli world failuje, gracz dostaje sensowny default zamiast crash'a).
    ///
    /// Pola w SaveData:
    /// - gameTimeSeconds (float) — pora dnia [0..86400)
    /// - gameDay (int) — dzień gry od start
    /// - gameStartDateIso (string) — data startu nowej gry
    /// - timeScale (float) — aktualna prędkość czasu (x1/x5/x25/x150/x500)
    /// - isPaused (bool) — load wczyta jako false (nie chcemy pauza-on-load surprise)
    /// - money (long) — kasa firmy
    /// - depotName (string) — nazwa zajezdni gracza
    /// - homeDepotStationId (int) — stacja OSM gdzie jest zajezdnia (M9c)
    /// - globalReputation (int) — aktualna reputacja mirror z ReputationManager
    /// - difficulty (JObject) — preset + modifiers (M13-13)
    /// - gameRules (JObject) — disabled toggles (M13-13)
    /// - seed (int) — globalny seed deterministic RNG (MP-9, M-Performance 2026-05-06).
    ///   Krytyczny dla M10 MP — sync między klientami wymaga tego samego seeda.
    /// - rngStates (JObject) — persystowalne stany per-system RNG. Stary save bez tego
    ///   pola → fallback do seed-derived initial state. Restore musi być przed innymi
    ///   modułami, bo część Deserialize może używać RandomRegistry.
    ///
    /// Auto-rejestracja: <see cref="WorldSavableBootstrap"/> robi
    /// SaveRegistry.Register w RuntimeInitializeOnLoadMethod (BeforeSceneLoad).
    /// </summary>
    /// <summary>Defaulty dla nowej gry / failed load. GameCreator nadpisuje je po user input.</summary>
    public static class WorldSavableDefaults
    {
        /// <summary>Domyślny startowy budżet (zł). GameCreator z Difficulty modyfiery
        /// nadpisuje (BASE_STARTING_BUDGET_PLN × startBudgetMult).</summary>
        public const long DefaultStartingMoney = 150_000L;

        /// <summary>Domyślna nazwa zajezdni — fallback gdy gracz nie poda własnej w GameCreator.</summary>
        public const string DefaultDepotName = "Zajezdnia Mokotow";

        /// <summary>Data startu nowej gry (ISO yyyy-MM-dd). Można zmieniać per release season
        /// (np. M14 EA: "2026-09-01"). Aktualnie marzec 2026.</summary>
        public const string DefaultGameStartDateIso = "2026-03-15";

        /// <summary>Domyślna pora startu dnia gry (sek od północy). 6 × 3600 = 06:00.</summary>
        public const float DefaultGameTimeSeconds = 6f * 3600f;

        /// <summary>Domyślna globalna reputacja 50/100 (centrum skali).</summary>
        public const int DefaultGlobalReputation = 50;
    }

    public class WorldSavable : ISavable
    {
        public string ModuleId => "world";
        public int SchemaVersion => 1; // pre-EA reset 2026-05-15; bump po EA = real migrator

        public JObject Serialize()
        {
            return new JObject
            {
                ["gameTimeSeconds"]    = GameState.GameTimeSeconds,
                ["gameDay"]            = GameState.GameDay,
                ["gameStartDateIso"]   = GameState.GameStartDateIso,
                ["timeScale"]          = GameState.TimeScale,
                ["isPaused"]           = GameState.IsPaused,
                ["money"]              = GameState.Money,
                ["depotName"]          = GameState.DepotName,
                ["homeDepotStationId"] = GameState.HomeDepotStationId,
                ["globalReputation"]   = GameState.GlobalReputation,
                ["difficulty"]         = DifficultyService.ToJson(),
                ["gameRules"]          = GameRulesService.ToJson(),
                ["seed"]               = GameState.Seed,      // MP-9 (M-Performance 2026-05-06)
                ["dispatchPolicy"]     = (int)GameState.MapDispatchPolicy, // M-Dispatch Faza 4b
                ["rngStates"]          = RandomRegistry.ToJson(),
                // Total playtime (poprzednie sesje + aktywna), żeby manifest.Playtime
                // kontynuował się między restartami gry zamiast resetować do
                // Time.realtimeSinceStartup od process start. Patrz GameState.GetTotalPlaytimeSec.
                ["accumulatedPlaytime"] = GameState.GetTotalPlaytimeSec()
            };
        }

        public void Deserialize(JObject data, int sourceVersion)
        {
            // Każde pole z fallback do default — gdyby brakowało w starym save.
            GameState.GameTimeSeconds    = data.Value<float?>("gameTimeSeconds")    ?? WorldSavableDefaults.DefaultGameTimeSeconds;
            GameState.GameDay            = data.Value<int?>("gameDay")              ?? 0;
            // Crash-hunt V11/V12 (systemowy fix): waliduj FORMAT, nie tylko null. Uszkodzona/edytowana
            // GameStartDateIso ("garbage") przeszłaby przez `?? default` i wywaliła setki callerów
            // robiących ParseDate(GameStartDateIso) (GameState.CurrentDateIso, DemandModifiers,
            // CrewDuty, ...). Sanityzacja w jedynym choke poincie load → runtime value zawsze parsowalny.
            var startIso = data.Value<string>("gameStartDateIso");
            GameState.GameStartDateIso   = RailwayManager.Core.IsoTime.TryParseDate(startIso, out _)
                ? startIso : WorldSavableDefaults.DefaultGameStartDateIso;
            GameState.TimeScale          = data.Value<float?>("timeScale")          ?? 1f;
            GameState.IsPaused           = false; // celowo: nie wczytujemy pausa
            PauseStack.Clear();                   // czyść popup ownerów po starej sesji
            GameState.Money              = data.Value<long?>("money")               ?? WorldSavableDefaults.DefaultStartingMoney;
            GameState.DepotName          = data.Value<string>("depotName")          ?? WorldSavableDefaults.DefaultDepotName;
            GameState.HomeDepotStationId = data.Value<int?>("homeDepotStationId")   ?? -1;
            GameState.GlobalReputation   = data.Value<int?>("globalReputation")     ?? WorldSavableDefaults.DefaultGlobalReputation;

            // M13-13: difficulty + game rules (brak w save → defaulty Normal/all-on)
            DifficultyService.ApplyFromJson(data.Value<JObject>("difficulty"));
            GameRulesService.ApplyFromJson(data.Value<JObject>("gameRules"));

            // MP-9 (M-Performance 2026-05-06): deterministic seed + per-system RNG states.
            // Stary save bez pola "rngStates" → registry resetuje istniejące referencje do
            // seed-derived state. WorldSavable jest pierwszy w bundle, więc inne moduły
            // używające RandomRegistry przy deserialize dostaną już poprawny seed/state.
            GameState.Seed = data.Value<int?>("seed") ?? 0;
            // M-Dispatch Faza 4b: polityka dispatchera (brak w starym save → Balanced).
            GameState.MapDispatchPolicy = (DispatchPolicy)(data.Value<int?>("dispatchPolicy") ?? (int)DispatchPolicy.Balanced);
            RandomRegistry.ApplyFromJson(data.Value<JObject>("rngStates"));

            // Resume playtime accumulator. Stary save bez pola → 0 (de facto reset, ale
            // kontynuacja "od teraz" — gracz nie traci historycznego playtime jeśli
            // istniał, tylko ten z load nie jest known). Pole dodane 2026-05-15.
            GameState.ResumePlaytimeSession(data.Value<double?>("accumulatedPlaytime") ?? 0);
        }

        /// <summary>Reset do defaultów. Używane w 2 sytuacjach:
        /// 1. Po failed load (graceful degradation — gracz dostaje grywalny stan zamiast crash).
        /// 2. Przez `SaveLoadServiceBootstrap.ResetRegisteredModulesForNewGame` (nowa gra).
        ///
        /// Po (2) **GameCreator nadpisuje** te defaulty na user choices przez `ApplyOnStart`:
        /// Seed (z user input), Money (z Difficulty BASE × startBudgetMult), DepotName (z input),
        /// HomeDepotStationId (z station picker). Defaulty tutaj służą tylko jako fallback gdy
        /// któryś z tych etapów GameCreator zostanie pominięty (lub po failed load).</summary>
        public void InitializeDefault()
        {
            GameState.GameTimeSeconds    = WorldSavableDefaults.DefaultGameTimeSeconds;
            GameState.GameDay            = 0;
            GameState.GameStartDateIso   = WorldSavableDefaults.DefaultGameStartDateIso;
            GameState.TimeScale          = 1f;
            GameState.IsPaused           = false;
            PauseStack.Clear();
            GameState.Money              = WorldSavableDefaults.DefaultStartingMoney;
            GameState.DepotName          = WorldSavableDefaults.DefaultDepotName;
            GameState.HomeDepotStationId = -1;
            GameState.GlobalReputation   = WorldSavableDefaults.DefaultGlobalReputation;
            GameState.Seed               = 0; // MP-9: default deterministic. GameCreator nadpisuje user-chosen.
            GameState.MapDispatchPolicy  = DispatchPolicy.Balanced; // M-Dispatch Faza 4b; GameCreator może nadpisać
            GameState.StartNewPlaytimeSession(); // accumulator reset, sesja od teraz
            RandomRegistry.ResetAll();
            DifficultyService.ResetToDefault();
            GameRulesService.ResetToDefault();
        }
    }

    // Migrator v1→v2 usunięty 2026-05-15 (identity, bez wartości — Deserialize
    // używa fallbacków). Zob. CLAUDE.md "Schema versioning".

    /// <summary>
    /// Auto-rejestracja WorldSavable w SaveRegistry — uruchamiana przed sceną
    /// żeby pierwszy SaveAsync/LoadAsync miał już moduł zarejestrowany.
    ///
    /// Bootstrap pattern jak w SettingsService.cs.
    /// </summary>
    public static class WorldSavableBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            SaveRegistry.Register(new WorldSavable());
        }
    }
}
