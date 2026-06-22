using System.Collections.Generic;
using NUnit.Framework;
using RailwayManager.Timetable;
using RailwayManager.Timetable.Economy;
using UnityEngine;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// M6-1: testy StationImportance — factor ważności stacji (wejście do gravity modelu popytu).
    /// Czysta logika (wszystkie zależności jako parametry), EditMode. Testuje RELACJE między
    /// czynnikami (major/named/perony/populacja), nie zamraża dokładnych wag (M6.5 kalibracja).
    /// Graf podajemy null — Calculate ma guard (junction bonus pomijany).
    /// </summary>
    public class StationImportanceTests
    {
        static readonly IReadOnlyList<CityPlace> NoPlaces = new List<CityPlace>();
        static readonly IReadOnlyList<StationPlatform> NoPlatforms = new List<StationPlatform>();

        static RailwayStation Station(string name, bool major, int pathNodeId = -1, Vector2 pos = default)
            => new RailwayStation { name = name, isMajorStation = major, pathNodeId = pathNodeId, position = pos };

        [Test]
        public void MajorStation_HigherThanHalt()
        {
            float major = StationImportance.Calculate(Station("Stacja", true), NoPlaces, NoPlatforms, null);
            float halt = StationImportance.Calculate(Station("Stacja", false), NoPlaces, NoPlatforms, null);
            Assert.That(major, Is.GreaterThan(halt), "Major station > halt.");
        }

        [Test]
        public void NamedStation_GetsKeywordBonus()
        {
            // "Główna" daje bonus nazwy — wyższa niż ta sama stacja bez słowa kluczowego.
            float named = StationImportance.Calculate(Station("Warszawa Główna", false), NoPlaces, NoPlatforms, null);
            float plain = StationImportance.Calculate(Station("Warszawa Wschodnia", false), NoPlaces, NoPlatforms, null);
            Assert.That(named, Is.GreaterThan(plain), "Stacja z 'Główna' dostaje bonus nazwy.");
        }

        [Test]
        public void MorePlatforms_HigherImportance()
        {
            const int node = 42;
            var st = Station("S", false, pathNodeId: node);
            var fewPlatforms = new List<StationPlatform> { new() { stationNodeId = node } };
            var manyPlatforms = new List<StationPlatform>
            {
                new() { stationNodeId = node }, new() { stationNodeId = node },
                new() { stationNodeId = node }, new() { stationNodeId = node }
            };

            float few = StationImportance.Calculate(st, NoPlaces, fewPlatforms, null);
            float many = StationImportance.Calculate(st, NoPlaces, manyPlatforms, null);
            Assert.That(many, Is.GreaterThan(few), "Więcej peronów = większa ważność.");
        }

        [Test]
        public void CityPopulation_RaisesImportance()
        {
            var pos = new Vector2(1000f, 1000f);
            var st = Station("Miejska", false, pos: pos);
            // Miasto 500k w zasięgu (3km) vs brak miasta.
            var withCity = new List<CityPlace> { new() { name = "Miasto", position = pos, population = 500000 } };

            float city = StationImportance.Calculate(st, withCity, NoPlatforms, null);
            float noCity = StationImportance.Calculate(st, NoPlaces, NoPlatforms, null);
            Assert.That(city, Is.GreaterThan(noCity), "Stacja przy dużym mieście ważniejsza.");
        }

        [Test]
        public void DistantCity_NotCounted()
        {
            var st = Station("Polna", false, pos: new Vector2(0f, 0f));
            // Miasto 50km daleko (poza CitySearchRadiusM 3km) — nie wlicza się.
            var farCity = new List<CityPlace> { new() { name = "Daleko", position = new Vector2(50000f, 0f), population = 1000000 } };

            float far = StationImportance.Calculate(st, farCity, NoPlatforms, null);
            float none = StationImportance.Calculate(st, NoPlaces, NoPlatforms, null);
            Assert.That(far, Is.EqualTo(none), "Odległe miasto (>3km) nie wpływa na ważność.");
        }

        [Test]
        public void BaseValue_HaltInEmptyField_IsLowest()
        {
            // Halt bez nazwy, bez peronów, bez miasta — wartość bazowa, najniższa.
            float bare = StationImportance.Calculate(Station("Polustanek", false), NoPlaces, NoPlatforms, null);
            float major = StationImportance.Calculate(Station("Główna", true), NoPlaces, NoPlatforms, null);
            Assert.That(bare, Is.GreaterThan(0f), "Bazowa wartość > 0.");
            Assert.That(bare, Is.LessThan(major), "Goły halt < major named station.");
        }

        [Test]
        public void CalculateAll_ReturnsEntryPerStation()
        {
            var stations = new List<RailwayStation>
            {
                new() { stationId = 1, name = "A Główna", isMajorStation = true },
                new() { stationId = 2, name = "B", isMajorStation = false }
            };
            var result = StationImportance.CalculateAll(stations, NoPlaces, NoPlatforms, null);

            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result.ContainsKey(1) && result.ContainsKey(2), Is.True);
            Assert.That(result[1], Is.GreaterThan(result[2]), "A (major+named) > B (halt).");
        }

        [Test]
        public void CalculateAll_NullStations_EmptyDict()
        {
            Assert.That(StationImportance.CalculateAll(null, NoPlaces, NoPlatforms, null).Count, Is.EqualTo(0));
        }
    }
}
