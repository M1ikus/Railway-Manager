using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using RailwayManager.Core;
using RailwayManager.Timetable.Economy;
using UnityEngine;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// M6-5: testy ReputationManager — multi-level reputacja (global/woj/stacja), eventy od opóźnień,
    /// clamp [0,100], demand factor. EditMode. Tworzymy przez AddComponent+Awake (nie EnsureExists —
    /// to robi DontDestroyOnLoad, rzuca w EditMode). Awake instaluje hook na ModernizationJobService
    /// — sprzątamy w TearDown przez OnDestroy.
    /// </summary>
    public class ReputationManagerTests
    {
        int _repBackup;
        ReputationManager _rm;

        [SetUp]
        public void SetUp()
        {
            _repBackup = GameState.GlobalReputation;
            DestroyExisting();
            var go = new GameObject("ReputationManager_Test");
            _rm = go.AddComponent<ReputationManager>();
            typeof(ReputationManager).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(_rm, null);
        }

        [TearDown]
        public void TearDown()
        {
            DestroyExisting();
            GameState.GlobalReputation = _repBackup;
        }

        static void DestroyExisting()
        {
            foreach (var r in Resources.FindObjectsOfTypeAll<ReputationManager>())
                Object.DestroyImmediate(r.gameObject);
        }

        [Test]
        public void StartsAt50()
        {
            Assert.That(_rm.Global, Is.EqualTo(ReputationManager.RepStart));
            Assert.That(ReputationManager.RepStart, Is.EqualTo(50));
        }

        [Test]
        public void DelayMajor_DecreasesMoreThanMinor()
        {
            _rm.ApplyEvent(ReputationEventType.DelayMinor, null, null);
            int afterMinor = _rm.Global;
            _rm.ResetRuntime();
            _rm.ApplyEvent(ReputationEventType.DelayMajor, null, null);
            int afterMajor = _rm.Global;

            Assert.That(afterMajor, Is.LessThan(afterMinor), "DelayMajor uderza mocniej niż DelayMinor.");
        }

        [Test]
        public void PunctualDay_IncreasesReputation()
        {
            int before = _rm.Global;
            _rm.ApplyEvent(ReputationEventType.PunctualDay, null, null);
            Assert.That(_rm.Global, Is.GreaterThan(before), "Punktualny dzień podnosi reputację.");
        }

        [Test]
        public void ClampedToMax100()
        {
            // Wiele PunctualDay (+1 każdy) nie przekroczy 100.
            for (int i = 0; i < 200; i++)
                _rm.ApplyEvent(ReputationEventType.PunctualDay, null, null);
            Assert.That(_rm.Global, Is.EqualTo(ReputationManager.RepMax));
        }

        [Test]
        public void ClampedToMin0()
        {
            // Wiele Accident (-30) nie spadnie poniżej 0.
            for (int i = 0; i < 20; i++)
                _rm.ApplyEvent(ReputationEventType.Accident, null, null);
            Assert.That(_rm.Global, Is.EqualTo(ReputationManager.RepMin));
        }

        [Test]
        public void GlobalMirroredToGameState()
        {
            _rm.ApplyEvent(ReputationEventType.DelayMajor, null, null);
            Assert.That(GameState.GlobalReputation, Is.EqualTo(_rm.Global),
                "Global reputacja jest mirrorowana do GameState dla TopBarUI.");
        }

        [Test]
        public void PerStation_AffectedOnlyForListedStations()
        {
            _rm.ApplyEvent(ReputationEventType.DelayMedium, new List<int> { 100 }, null);

            Assert.That(_rm.GetForStation(100), Is.LessThan(ReputationManager.RepStart),
                "Stacja na liście dostała penalty.");
            Assert.That(_rm.GetForStation(999), Is.EqualTo(ReputationManager.RepStart),
                "Stacja spoza listy ma domyślne 50.");
        }

        [Test]
        public void PerVoivodeship_Affected()
        {
            _rm.ApplyEvent(ReputationEventType.CanceledRun, null, new List<string> { "Mazowieckie" });
            Assert.That(_rm.GetForVoivodeship("Mazowieckie"), Is.LessThan(ReputationManager.RepStart));
            Assert.That(_rm.GetForVoivodeship("Pomorskie"), Is.EqualTo(ReputationManager.RepStart));
        }

        [Test]
        public void Bug056_StationAtZero_DoesNotResetTo50()
        {
            // BUG-056: stacja sprowadzona do 0 wieloma Accident — kolejny event NIE może
            // jej zresetować do RepStart (klucz istnieje, choć wartość 0).
            var stations = new List<int> { 50 };
            for (int i = 0; i < 10; i++)
                _rm.ApplyEvent(ReputationEventType.Accident, stations, null); // -30 ×10 → 0
            Assert.That(_rm.GetForStation(50), Is.EqualTo(0), "Stacja na dnie.");

            _rm.ApplyEvent(ReputationEventType.DelayMinor, stations, null); // -1
            Assert.That(_rm.GetForStation(50), Is.EqualTo(0),
                "Stacja przy 0 zostaje 0 po kolejnym penalty (NIE reset do 50 — BUG-056).");
        }

        [Test]
        public void GetDemandFactor_HigherRepHigherFactor()
        {
            // Domyślnie wszystko 50 → factor ~1.0. Podnieś rep stacji → wyższy factor.
            float baseline = _rm.GetDemandFactor(1, 2);
            for (int i = 0; i < 40; i++)
                _rm.ApplyEvent(ReputationEventType.PunctualDay, new List<int> { 1, 2 }, null);
            float improved = _rm.GetDemandFactor(1, 2);

            Assert.That(improved, Is.GreaterThan(baseline), "Wyższa reputacja → większy popyt.");
        }

        [Test]
        public void GetDemandFactor_MapsRepRangeTo_Half_To_OnePointFive()
        {
            // Kontrakt: rep 50 (default) → ~1.0. Sprawdzamy że baseline jest blisko 1.0.
            float factor = _rm.GetDemandFactor(1, 2);
            Assert.That(factor, Is.EqualTo(1.0f).Within(0.01f),
                "Rep 50 (global+stacje) → demand factor 1.0 (neutralny).");
        }

        [Test]
        public void RestoreFromSave_ClampsValues()
        {
            _rm.RestoreFromSave(global: 150, // poza zakresem → clamp do 100
                byVoivodeship: new[] { new KeyValuePair<string, int>("Slaskie", 80) },
                byStation: new[] { new KeyValuePair<int, int>(7, -20) }); // → clamp do 0

            Assert.That(_rm.Global, Is.EqualTo(100), "global 150 → clamp 100.");
            Assert.That(_rm.GetForVoivodeship("Slaskie"), Is.EqualTo(80));
            Assert.That(_rm.GetForStation(7), Is.EqualTo(0), "station -20 → clamp 0.");
        }
    }
}
