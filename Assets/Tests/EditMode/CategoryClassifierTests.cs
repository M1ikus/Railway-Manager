using System.Collections.Generic;
using NUnit.Framework;
using RailwayManager.Timetable;
using UnityEngine;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// M4: testy CategoryClassifier — auto-klasyfikacja kategorii IRJ (grupa + trakcja).
    /// Czysta logika decyzyjna (struct input, zero zależności od sceny). EditMode.
    ///
    /// Pokrywa: macierz trakcji (EMU/Loco × elektryczny/diesel → E/J/S/M letter) oraz
    /// grupę wojewódzką (resolver=null → zawsze wojewódzki): RO (osobowy, stops≥80%),
    /// RP (pospieszny, <40%), EI (ekspres, <40% + Vmax≥160), RA (aglomeracyjny, 1 miasto).
    /// Międzywojewódzki/nocny wymaga VoivodeshipResolver → poza tym zestawem.
    /// </summary>
    public class CategoryClassifierTests
    {
        /// <summary>Input wojewódzki (resolver=null → CrossesVoivodeshipBorder=false zawsze).</summary>
        static CategoryClassifier.ClassificationInput Input(
            int stops, int total, int vmax, int startMin = 8 * 60,
            CompositionMode mode = CompositionMode.MultipleUnit, bool electric = true,
            HashSet<string> agglomerations = null, List<string> cityNames = null)
            => new CategoryClassifier.ClassificationInput
            {
                routePolyline = new List<Vector2> { Vector2.zero, new Vector2(50000f, 0f) },
                stopsOnRoute = stops,
                totalStationsOnRoute = total,
                startMinutesFromMidnight = startMin,
                maxSpeedKmh = vmax,
                compositionMode = mode,
                isElectric = electric,
                voivodeshipResolver = null,
                agglomerations = agglomerations,
                stationCityNames = cityNames
            };

        // ── Trakcja (3. litera) ──────────────────────────────────────

        [Test]
        public void Traction_ElectricEmu()
        {
            var c = CategoryClassifier.Classify(Input(8, 10, 120, mode: CompositionMode.MultipleUnit, electric: true));
            Assert.That(c.traction, Is.EqualTo(TractionLetter.ElectricUnit));
        }

        [Test]
        public void Traction_DieselEmu()
        {
            var c = CategoryClassifier.Classify(Input(8, 10, 120, mode: CompositionMode.MultipleUnit, electric: false));
            Assert.That(c.traction, Is.EqualTo(TractionLetter.DieselUnit));
        }

        [Test]
        public void Traction_ElectricLoco()
        {
            var c = CategoryClassifier.Classify(Input(8, 10, 120, mode: CompositionMode.LocoWithCars, electric: true));
            Assert.That(c.traction, Is.EqualTo(TractionLetter.ElectricLoco));
        }

        [Test]
        public void Traction_DieselLoco()
        {
            var c = CategoryClassifier.Classify(Input(8, 10, 120, mode: CompositionMode.LocoWithCars, electric: false));
            Assert.That(c.traction, Is.EqualTo(TractionLetter.DieselLoco));
        }

        // ── Grupa wojewódzka ─────────────────────────────────────────

        [Test]
        public void Group_ManyStops_RegionalLocal_RO()
        {
            // stopRatio = 9/10 = 0.9 ≥ 0.80 → RO (osobowy, staje na wszystkich).
            var c = CategoryClassifier.Classify(Input(stops: 9, total: 10, vmax: 120));
            Assert.That(c.group, Is.EqualTo(IrjGroup.RegionalLocal));
        }

        [Test]
        public void Group_FewStops_RegionalFast_RP()
        {
            // stopRatio = 3/10 = 0.3 < 0.40, Vmax 120 < 160 → RP (pospieszny, nie ekspres).
            var c = CategoryClassifier.Classify(Input(stops: 3, total: 10, vmax: 120));
            Assert.That(c.group, Is.EqualTo(IrjGroup.RegionalFast));
        }

        [Test]
        public void Group_FewStopsHighSpeed_Express_EI()
        {
            // stopRatio 0.3 < 0.40 ORAZ Vmax 160 ≥ 160 → EI (ekspres).
            var c = CategoryClassifier.Classify(Input(stops: 3, total: 10, vmax: 160));
            Assert.That(c.group, Is.EqualTo(IrjGroup.ExpressDomestic));
        }

        [Test]
        public void Group_MidStops_NotExpress_RP()
        {
            // stopRatio 5/10 = 0.5 (między 0.40 a 0.80) → RP (nie RO bo <0.80, nie EI bo nie <0.40).
            var c = CategoryClassifier.Classify(Input(stops: 5, total: 10, vmax: 200));
            Assert.That(c.group, Is.EqualTo(IrjGroup.RegionalFast),
                "Średnia liczba postojów → RP nawet przy wysokiej Vmax (EI wymaga <40% postojów).");
        }

        [Test]
        public void Group_SingleAgglomeration_RA()
        {
            // Wszystkie stacje w jednym mieście aglomeracyjnym → RA (aglomeracyjny).
            var aggl = new HashSet<string> { "Warszawa" };
            var cities = new List<string> { "Warszawa", "Warszawa", "Warszawa" };
            var c = CategoryClassifier.Classify(Input(stops: 3, total: 10, vmax: 120,
                agglomerations: aggl, cityNames: cities));
            Assert.That(c.group, Is.EqualTo(IrjGroup.RegionalAgglomeration));
        }

        [Test]
        public void Group_MultipleCities_NotAgglomeration()
        {
            // Stacje w różnych miastach → NIE RA (mimo że oba w secie aglomeracji).
            var aggl = new HashSet<string> { "Warszawa", "Pruszkow" };
            var cities = new List<string> { "Warszawa", "Pruszkow" };
            var c = CategoryClassifier.Classify(Input(stops: 9, total: 10, vmax: 120,
                agglomerations: aggl, cityNames: cities));
            Assert.That(c.group, Is.Not.EqualTo(IrjGroup.RegionalAgglomeration),
                "Różne miasta → nie aglomeracja jednomiastowa → klasyfikacja wg stop ratio (RO).");
            Assert.That(c.group, Is.EqualTo(IrjGroup.RegionalLocal), "9/10 stops → RO.");
        }

        [Test]
        public void Group_ZeroStations_DefaultsToFast()
        {
            // total=0 → stopRatio=0 (<0.40), Vmax 120<160 → RP (nie dzieli przez zero).
            var c = CategoryClassifier.Classify(Input(stops: 0, total: 0, vmax: 120));
            Assert.That(c.group, Is.EqualTo(IrjGroup.RegionalFast), "0 stacji → bezpieczny fallback RP.");
        }
    }
}
