using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RailwayManager.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RailwayManager.Tests.PlayMode
{
    /// <summary>
    /// End-to-end cold start ("wlaczenie gry i pierwsza sesja"): przechodzi realna sekwencje
    /// scen MainMenu -> GameCreator -> (ApplyOnStart = commit nowej gry) -> Depot, weryfikujac
    /// ze stan pierwszej sesji jest sensowny i ze NIC nie wybucha po drodze.
    ///
    /// "Zero wyjatkow" egzekwuje domyslny LogAssert Unity Test Framework — nieoczekiwany
    /// LogError/LogException podczas bootu sceny failuje test. To wlasnie chcemy: smoke ze
    /// surowy launch + new game nie sypie sie blędami.
    ///
    /// ApplyOnStart jest private (button handler) — wywolujemy refleksja na zywej instancji
    /// GameCreatorUI (bez referencji asmdef GameCreator z testow PlayMode).
    /// </summary>
    public class FirstSessionBootTests
    {
        [UnityTest]
        public IEnumerator NewGame_MainMenu_To_GameCreator_To_FirstSession_Depot()
        {
            // ── 1. Launch: MainMenu boots ──
            yield return LoadSceneSingle("MainMenu");
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("MainMenu"),
                "Launch: MainMenu powinno sie zaladowac.");
            yield return Settle(5);

            // ── 2. New game: GameCreator boots ──
            yield return LoadSceneSingle("GameCreator");
            yield return Settle(12); // procedural UI build (Awake/Start) + i18n
            var gc = FindByTypeName("GameCreatorUI");
            Assert.That(gc, Is.Not.Null, "GameCreatorUI powinno istniec w scenie GameCreator.");

            // ── 3. Commit nowej gry (prawdziwy ApplyOnStart: reset modulow + seed + difficulty + Money) ──
            InvokePrivate(gc, "ApplyOnStart");

            Assert.That(GameState.Money, Is.GreaterThan(0L),
                "Po ApplyOnStart budzet startowy ustawiony (difficulty preset x BaseStartingBudget).");
            Assert.That(GameState.Seed, Is.EqualTo(0),
                "Domyslny seed = 0 (puste pole = deterministyczne baseline).");

            long moneyAfterCreate = GameState.Money;
            string depotNameAfterCreate = GameState.DepotName;

            // ── 4. Pierwsza sesja: wejscie do Depot ──
            yield return LoadSceneSingle("Depot");
            yield return Settle(15); // depot init (track graph / services / picker overlay)

            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("Depot"),
                "Pierwsza sesja: scena Depot powinna sie zaladowac.");
            Assert.That(GameState.Money, Is.EqualTo(moneyAfterCreate),
                "Budzet zachowany przy przejsciu GameCreator -> Depot (stan cross-scene).");
            Assert.That(GameState.DepotName, Is.EqualTo(depotNameAfterCreate),
                "Nazwa gry/depotu zachowana cross-scene.");
            Assert.That(GameState.GameDay, Is.GreaterThanOrEqualTo(0),
                "GameDay w sensownym zakresie (pierwsza sesja).");
            // Pierwsza sesja PRZED wyborem lokalizacji: HomeDepot jeszcze nieustawiony
            // (DepotLocationPickerUI pokaze sie graczowi). To kontrakt, nie bug.
            Assert.That(GameState.IsHomeDepotSet, Is.False,
                "Pierwsza sesja: HomeDepot nieustawiony przed wyborem (picker overlay).");

            // Brak nieoczekiwanego LogError/LogException w calej sekwencji = test przechodzi
            // (egzekwowane przez domyslny LogAssert frameworka).
        }

        // ── Helpers ──────────────────────────────────────────────────

        static IEnumerator LoadSceneSingle(string name)
        {
            if (SceneManager.GetActiveScene().name == name) yield break;
            var op = SceneManager.LoadSceneAsync(name, LoadSceneMode.Single);
            while (op != null && !op.isDone) yield return null;
        }

        static IEnumerator Settle(int frames)
        {
            for (int i = 0; i < frames; i++) yield return null;
        }

        static MonoBehaviour FindByTypeName(string typeName)
        {
            return Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
                .FirstOrDefault(m => m != null && m.GetType().Name == typeName);
        }

        static void InvokePrivate(object target, string method)
        {
            var mi = target.GetType().GetMethod(method,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.That(mi, Is.Not.Null, $"Metoda '{method}' powinna istniec na {target.GetType().Name}.");
            mi.Invoke(target, null);
        }
    }
}
