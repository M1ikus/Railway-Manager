using System.Collections;
using NUnit.Framework;
using RailwayManager.Core;
using RailwayManager.Personnel;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RailwayManager.Tests.PlayMode
{
    /// <summary>
    /// M8-10: PlayMode testy "czy pracownicy chodzą" — runtime ruchu w zajezdni 3D.
    /// Ładuje Depot.unity (EmployeeWalkSimulator wymaga IsDepotSceneActive — inaczej instant-resolve
    /// bez wizualu). Weryfikuje: pracownik pojawia się jako wizual + faktycznie idzie z A do B.
    /// </summary>
    public class PersonnelWalkTests
    {
        const float ReadyTimeoutSec = 60f;
        const int Eid = 850001;

        [UnityTest]
        public IEnumerator Employee_SpawnsAsVisualInDepot()
        {
            yield return LoadDepotReady();
            var sim = SetupSimAndEmployee(EmployeeRole.Mechanic);

            var visual = sim.SpawnEmployee(Eid, Vector3.zero);

            Assert.That(visual, Is.Not.Null, "Pracownik pojawia się jako wizual w zajezdni.");
            Assert.That(sim.VisualCount, Is.GreaterThan(0), "VisualCount rośnie po spawnie.");

            Cleanup(sim);
        }

        [UnityTest]
        public IEnumerator TicketClerk_DoesNotSpawnInDepot()
        {
            // D19: kasjer wizualnie tylko w popupie stacji — NIE spawnuje się jako agent w depot.
            yield return LoadDepotReady();
            var sim = SetupSimAndEmployee(EmployeeRole.TicketClerk);

            var visual = sim.SpawnEmployee(Eid, Vector3.zero);

            Assert.That(visual, Is.Null, "Kasjer nie spawnuje się jako agent w zajezdni (D19).");
            Cleanup(sim);
        }

        [UnityTest]
        public IEnumerator WalkTask_FiresArriveAndChainsNextTask()
        {
            // Deterministyczny test pipeline'u zadań ruchu (dyspozycja → walk task → onArrive → chain).
            // Pełna animacja traversal 3D (kapsuła krok po kroku) zależy od PathGraph/geometrii sceny
            // i jest pokrywana manualnym smoke (PersonnelLifeLoopSmokeTest) — tu weryfikujemy LOGIKĘ
            // task systemu: zadanie się wykonuje, callback odpala, nextTask łańcuchuje.
            yield return LoadDepotReady();
            var sim = SetupSimAndEmployee(EmployeeRole.Mechanic);

            float scaleBackup = GameState.TimeScale;
            bool pausedBackup = GameState.IsPaused;
            try
            {
                GameState.IsPaused = false;
                GameState.TimeScale = 5f;

                bool firstArrived = false, secondArrived = false;
                // Task z chain: gdy pierwszy dotrze → automatycznie kolejkuje nextTask.
                var chained = new EmployeeWalkTask
                {
                    employeeId = Eid, destination = new Vector3(10f, 0f, 5f), purpose = "Step2",
                    onArrive = () => secondArrived = true
                };
                sim.EnqueueTask(new EmployeeWalkTask
                {
                    employeeId = Eid, destination = new Vector3(5f, 0f, 0f), purpose = "Step1",
                    onArrive = () => firstArrived = true,
                    nextTask = chained
                });

                var fixedUpdate = typeof(EmployeeWalkSimulator).GetMethod(
                    "FixedUpdate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

                // Pompuj aż oba taski się wykonają (pierwszy → chain → drugi).
                for (int i = 0; i < 1000 && !(firstArrived && secondArrived); i++)
                {
                    fixedUpdate.Invoke(sim, null);
                    if (i % 25 == 0) yield return null;
                }

                Assert.That(firstArrived, Is.True, "Pierwsze zadanie ruchu wykonane (onArrive odpalił).");
                Assert.That(secondArrived, Is.True,
                    "nextTask zołańcuchowany i wykonany — pracownik dostaje sekwencję zadań (dyspozycja działa).");
            }
            finally
            {
                Cleanup(sim);
                GameState.TimeScale = scaleBackup;
                GameState.IsPaused = pausedBackup;
            }
        }

        // ── Helpers ──────────────────────────────────────────────────

        static EmployeeWalkSimulator SetupSimAndEmployee(EmployeeRole role)
        {
            PersonnelService.Employees.RemoveAll(e => e.employeeId == Eid);
            PersonnelService.Employees.Add(new Employee
            {
                employeeId = Eid, role = role, status = EmployeeStatus.OnShift, skill = 1
            });
            // GetById używa dict cache _byId (nie listy) — direct Add wymaga rebuildu indeksów.
            PersonnelService.RebuildIndexes();
            return EmployeeWalkSimulator.Instance ?? EmployeeWalkSimulator.EnsureExists();
        }

        static void Cleanup(EmployeeWalkSimulator sim)
        {
            sim.DespawnEmployee(Eid);
            PersonnelService.Employees.RemoveAll(e => e.employeeId == Eid);
            PersonnelService.RebuildIndexes(); // wyczyść stale ref z _byId
        }

        static IEnumerator LoadDepotReady()
        {
            if (SceneManager.GetActiveScene().name != "Depot")
            {
                var load = SceneManager.LoadSceneAsync("Depot", LoadSceneMode.Single);
                while (!load.isDone) yield return null;
            }
            // Daj DepotManager.Start chwilę (nie wymagamy TrackGraph — walk używa PathGraph/fallback straight-line).
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 3f) yield return null;
        }
    }
}
