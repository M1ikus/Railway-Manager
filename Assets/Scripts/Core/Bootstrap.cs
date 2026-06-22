using System;
using UnityEngine;

namespace RailwayManager.Core
{
    /// <summary>
    /// Centralny entry point inicjalizacji Core services (2026-05-13).
    ///
    /// Wcześniej każdy serwis miał własny <c>[RuntimeInitializeOnLoadMethod]</c>
    /// + ad-hoc `EnsureExists()` (kto pierwszy zawoła, ten tworzy). 30+ atrybutów
    /// rozsianych po projekcie, brak czytelnej kolejności init.
    ///
    /// <see cref="Bootstrap"/> agreguje Core services w 2 fazach + emit hook events
    /// dla wyższych warstw (SaveLoad/SharedUI/gameplay asmdef mogą subscribe'ować
    /// zamiast utrzymywać własny <c>RuntimeInitializeOnLoadMethod</c>):
    ///
    /// <b>EarlyInit</b> (<c>BeforeSceneLoad</c>) — przed Awake jakiegokolwiek MonoBehaviour:
    /// - <see cref="AppPaths.EnsureCreated"/> (tworzy persistent dirs)
    /// - <see cref="Settings.SettingsService.EnsureExists"/> (ładuje PlayerPrefs)
    /// - emit <see cref="OnEarlyInit"/>
    ///
    /// <b>LateInit</b> (<c>AfterSceneLoad</c>) — po Awake/Start scene roots:
    /// - <see cref="GameClock.EnsureExists"/>
    /// - <see cref="VehicleLocationService.EnsureExists"/>
    /// - emit <see cref="OnLateInit"/>
    ///
    /// <b>Kontrakty:</b>
    /// - Subskrypcje na <see cref="OnEarlyInit"/>/<see cref="OnLateInit"/> w <c>static</c> field
    ///   inicjalizatorze (przed Bootstrap.EarlyInit). Idealnie: `static MyService() { Bootstrap.OnLateInit += MyInit; }`
    /// - Jeśli moduł subscribe'uje PO emit (np. lazy init), event nie poleci ponownie —
    ///   dlatego eksponujemy <see cref="EarlyInitFired"/>/<see cref="LateInitFired"/> flagi
    ///   żeby late subscriber mógł sprawdzić i wywołać handler manualnie.
    ///
    /// Wyższe asmdef mogą zostać przy własnym <c>RuntimeInitializeOnLoadMethod</c> —
    /// to nie jest wymuszenie, tylko centralizacja Core init i opt-in hook dla porządku.
    /// </summary>
    public static class Bootstrap
    {
        /// <summary>True po pomyślnym wykonaniu <see cref="EarlyInit"/>. Diagnostyczne.</summary>
        public static bool EarlyInitFired { get; private set; }

        /// <summary>True po pomyślnym wykonaniu <see cref="LateInit"/>. Diagnostyczne.</summary>
        public static bool LateInitFired { get; private set; }

        /// <summary>
        /// Emitowane na końcu <see cref="EarlyInit"/> (BeforeSceneLoad). Subskrybenci:
        /// wyższe warstwy potrzebujące PRZED Awake scene (np. LocalizationService —
        /// ładuje strings przed UI Awake'em). Aktualnie SharedUI/SaveLoad/Timetable
        /// trzymają własne <c>RuntimeInitializeOnLoadMethod(BeforeSceneLoad)</c>; migracja
        /// na ten hook jest opt-in.
        /// </summary>
        public static event Action OnEarlyInit;

        /// <summary>
        /// Emitowane na końcu <see cref="LateInit"/> (AfterSceneLoad). Subskrybenci:
        /// SaveLoad bootstrap, gameplay services które potrzebują GameClock/VLS.
        /// </summary>
        public static event Action OnLateInit;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void EarlyInit()
        {
            // Idempotent guard (Unity wywołuje raz, ale defensive na edge cases:
            // domain reload + automatic re-init).
            if (EarlyInitFired) return;

            try
            {
                AppPaths.EnsureCreated();
                Settings.SettingsService.EnsureExists();
            }
            catch (Exception e)
            {
                Log.Error($"[Bootstrap] EarlyInit threw: {e}");
            }

            EarlyInitFired = true;
            try
            {
                OnEarlyInit?.Invoke();
            }
            catch (Exception e)
            {
                Log.Error($"[Bootstrap] OnEarlyInit subscriber threw: {e}");
            }
            Log.Info("[Bootstrap] EarlyInit done (BeforeSceneLoad)");
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void LateInit()
        {
            if (LateInitFired) return;

            try
            {
                GameClock.EnsureExists();
                VehicleLocationService.EnsureExists();
            }
            catch (Exception e)
            {
                Log.Error($"[Bootstrap] LateInit threw: {e}");
            }

            LateInitFired = true;
            try
            {
                OnLateInit?.Invoke();
            }
            catch (Exception e)
            {
                Log.Error($"[Bootstrap] OnLateInit subscriber threw: {e}");
            }
            Log.Info("[Bootstrap] LateInit done (AfterSceneLoad)");
        }
    }
}
