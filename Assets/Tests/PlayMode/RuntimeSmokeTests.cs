using System.Collections;
using NUnit.Framework;
using RailwayManager.Core;
using UnityEngine;
using UnityEngine.TestTools;

namespace RailwayManager.Tests.PlayMode
{
    /// <summary>
    /// M9c-D: minimalny PlayMode smoke test — weryfikuje że runtime faktycznie tikuje
    /// (GameClock advancuje GameTimeSeconds przez klatki gry). Sens: potwierdza że ścieżka
    /// PlayMode (Tools/run-tests.ps1 -Platform PlayMode) działa end-to-end + że Core runtime
    /// bootstrapuje się w trybie Play. Cięższe scenariusze (ruch pociągu, handshake depot↔map)
    /// wymagają załadowanych scen + grafu — dochodzą stopniowo gdy będzie potrzeba.
    /// </summary>
    public class RuntimeSmokeTests
    {
        [UnityTest]
        public IEnumerator GameClock_AdvancesGameTimeAcrossFrames()
        {
            // Izolacja: usuń istniejące zegary, ustaw znany stan.
            foreach (var c in Resources.FindObjectsOfTypeAll<GameClock>())
                Object.DestroyImmediate(c.gameObject);

            float timeBackup = GameState.GameTimeSeconds;
            float scaleBackup = GameState.TimeScale;
            bool pausedBackup = GameState.IsPaused;
            try
            {
                GameState.GameTimeSeconds = 0f;
                GameState.TimeScale = 1f;
                GameState.IsPaused = false;

                var go = new GameObject("GameClock_PlayModeTest");
                go.AddComponent<GameClock>();

                // GameClock tickuje w Update (60Hz wall-clock) → przeczekaj kilka klatek.
                for (int i = 0; i < 10; i++)
                    yield return null;

                Assert.That(GameState.GameTimeSeconds, Is.GreaterThan(0f),
                    "GameClock powinien zaawansować GameTimeSeconds przez ~10 klatek Play mode.");

                Object.DestroyImmediate(go);
            }
            finally
            {
                GameState.GameTimeSeconds = timeBackup;
                GameState.TimeScale = scaleBackup;
                GameState.IsPaused = pausedBackup;
            }
        }
    }
}
