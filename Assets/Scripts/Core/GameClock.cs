using UnityEngine;

namespace RailwayManager.Core
{
    /// <summary>
    /// Centralny tick zegara świata gry — inkrementuje <see cref="GameState.GameTimeSeconds"/>
    /// i obsługuje day rollover wywołując <see cref="GameState.InvokeDayEnded"/>.
    ///
    /// Wcześniej (do 2026-05-10) tick siedział w <c>TopBarUI.Update()</c>. Skutkowało to
    /// tym, że zegar nie tika gdy TopBar nie istnieje w hierarchii (headless boot,
    /// MapScene-only edge case, test scena bez SharedUI). Wyciągnięcie do osobnego
    /// MonoBehaviour w Core daje jedno źródło prawdy.
    ///
    /// Bootstrap: <see cref="SceneController.Initialize"/> woła <see cref="EnsureExists"/>
    /// razem z <c>VehicleLocationService.EnsureExists</c>. Singleton DontDestroyOnLoad —
    /// żyje jedna instancja przez całą sesję, niezależnie od scenowych przeładowań.
    ///
    /// Update vs FixedUpdate: zachowane Update (60Hz) bo to zegar wall-clock, nie
    /// symulacja deterministyczna. Zmiana na FixedUpdate (50Hz) zmieniłaby precyzję
    /// czasu w subtelny sposób — symulatory (TrainRunSimulator/PassengerManager) i tak
    /// czytają <c>GameState.GameTimeSeconds</c> jako migawkę, nie tikują same z siebie.
    /// </summary>
    public class GameClock : MonoBehaviour
    {
        public static GameClock Instance { get; private set; }

        public static GameClock EnsureExists()
        {
            if (Instance != null) return Instance;

            var go = new GameObject("GameClock");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<GameClock>();
            Log.Info("[GameClock] Bootstrapped");
            return Instance;
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// Bezpiecznik dla multi-day rollover w jednym ticku. Przy ekstremalnej kombinacji
        /// TimeScale × lag spike teoretycznie można skoczyć kilka dni; cap chroni przed
        /// zawieszeniem w pętli (NaN, Inf, niespodziewane setpoint).
        /// </summary>
        const int MaxRolloversPerTick = 30;

        void Update()
        {
            if (GameState.IsPaused) return;

            GameState.GameTimeSeconds += Time.deltaTime * GameState.TimeScale;

            // While (nie if) — przy x500 i frame drop wartość delty może przekroczyć 86400s
            // i pominąć całe doby. Bez pętli OnDayEnded wystrzeliwałby tylko raz, a GameDay
            // inkrementował o 1 zamiast o właściwy delta_dni → EconomyManager / Subsidy /
            // Reputation traciłyby bilanse pominiętych dni.
            int rolloverCount = 0;
            while (GameState.GameTimeSeconds >= 86400f)
            {
                GameState.GameTimeSeconds -= 86400f;
                string prevDate = GameState.CurrentDateIso;
                GameState.GameDay++;
                GameState.InvokeDayEnded(prevDate);

                if (++rolloverCount >= MaxRolloversPerTick)
                {
                    Log.Error($"[GameClock] Rollover capped at {MaxRolloversPerTick} per tick — " +
                              $"GameTimeSeconds={GameState.GameTimeSeconds}, TimeScale={GameState.TimeScale}. " +
                              "Resetting to 0 to break loop.");
                    GameState.GameTimeSeconds = 0f;
                    break;
                }
            }
        }
    }
}
