using System.Collections.Generic;
using NUnit.Framework;
using RailwayManager.Timetable;
using RailwayManager.Timetable.Economy;
using UnityEngine;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// M6-1 + TD-016: testy OriginDestinationMatrix — gravity model popytu + spatial grid cutoff.
    /// Czysta logika (instancja, Build z listy stacji+importance), EditMode. Pozycje stacji w metrach.
    ///
    /// Pokrywa: symetrię OD (A→B == B→A), Gaussian peak ~80km, cutoff 800km (TD-016),
    /// za-blisko <1km reject, skalowanie importance, demand-threshold 0.1.
    /// </summary>
    public class OriginDestinationMatrixTests
    {
        const float Km = 1000f; // metrów w km

        static RailwayStation St(int id, int nodeId, Vector2 pos)
            => new RailwayStation { stationId = id, pathNodeId = nodeId, position = pos };

        /// <summary>Buduje macierz z 2 stacji oddalonych o distKm, każda z importance imp.</summary>
        static OriginDestinationMatrix Build2(float distKm, float imp = 10f)
        {
            var stations = new List<RailwayStation>
            {
                St(1, 1, new Vector2(0f, 0f)),
                St(2, 2, new Vector2(distKm * Km, 0f))
            };
            var importance = new Dictionary<int, float> { { 1, imp }, { 2, imp } };
            var m = new OriginDestinationMatrix();
            m.Build(stations, importance);
            return m;
        }

        [Test]
        public void Demand_IsSymmetric()
        {
            var m = Build2(80f);
            Assert.That(m.GetBaseDemand(1, 2), Is.EqualTo(m.GetBaseDemand(2, 1)),
                "OD symetryczne — A→B tak samo częste jak B→A.");
            Assert.That(m.GetBaseDemand(1, 2), Is.GreaterThan(0f), "Para 80km z importance 10 → demand > 0.");
        }

        [Test]
        public void Demand_PeakAround80km_HigherThanLongDistance()
        {
            // 80km = peak Gaussa; 400km = już daleko (samolot/auto). Przy tym samym importance.
            float peak = Build2(80f).GetBaseDemand(1, 2);
            float far = Build2(400f).GetBaseDemand(1, 2);
            Assert.That(peak, Is.GreaterThan(far), "Demand przy peak (~80km) > demand na dalekim dystansie.");
        }

        [Test]
        public void Demand_BeyondCutoff800km_IsZero()
        {
            // TD-016: pary >800km mają demand <0.1 nawet dla dużego importance → odrzucone.
            var m = Build2(900f, imp: 30f); // max szacowane importance
            Assert.That(m.GetBaseDemand(1, 2), Is.EqualTo(0f), "Para >800km → demand 0 (cutoff TD-016).");
            Assert.That(m.PairCount, Is.EqualTo(0), "Nie dodano żadnej pary poza cutoff.");
        }

        [Test]
        public void Demand_TooClose_Under1km_IsZero()
        {
            var m = Build2(0.5f); // 500m — ludzie chodzą, nie jadą pociągiem
            Assert.That(m.GetBaseDemand(1, 2), Is.EqualTo(0f), "Para <1km → demand 0.");
        }

        [Test]
        public void Demand_ScalesWithImportance()
        {
            // Wyższe importance obu stacji → większy demand (imp×imp w formule).
            float low = Build2(80f, imp: 5f).GetBaseDemand(1, 2);
            float high = Build2(80f, imp: 20f).GetBaseDemand(1, 2);
            Assert.That(high, Is.GreaterThan(low), "Większe importance → większy popyt bazowy.");
        }

        [Test]
        public void Build_SkipsStationsWithoutGraphNode()
        {
            // pathNodeId < 0 → stacja pominięta (nieosiągalna w grafie).
            var stations = new List<RailwayStation>
            {
                St(1, -1, new Vector2(0f, 0f)),          // brak node
                St(2, 2, new Vector2(80f * Km, 0f))
            };
            var importance = new Dictionary<int, float> { { 1, 10f }, { 2, 10f } };
            var m = new OriginDestinationMatrix();
            m.Build(stations, importance);
            Assert.That(m.PairCount, Is.EqualTo(0), "Stacja bez pathNodeId pominięta → brak par.");
        }

        [Test]
        public void Build_SkipsStationsWithoutImportance()
        {
            var stations = new List<RailwayStation> { St(1, 1, Vector2.zero), St(2, 2, new Vector2(80f * Km, 0f)) };
            var importance = new Dictionary<int, float> { { 1, 10f } }; // stacja 2 bez importance
            var m = new OriginDestinationMatrix();
            m.Build(stations, importance);
            Assert.That(m.PairCount, Is.EqualTo(0), "Stacja bez importance pominięta.");
        }

        [Test]
        public void Build_NullArgs_NoCrash_EmptyMatrix()
        {
            var m = new OriginDestinationMatrix();
            Assert.DoesNotThrow(() => m.Build(null, null));
            Assert.That(m.PairCount, Is.EqualTo(0));
        }

        [Test]
        public void GetBaseDemand_UnknownPair_ReturnsZero()
        {
            var m = Build2(80f);
            Assert.That(m.GetBaseDemand(99, 100), Is.EqualTo(0f), "Nieznana para → 0.");
        }

        [Test]
        public void MultipleStations_BuildsAllValidPairs()
        {
            // 3 stacje w trójkącie ~80km bok → 3 pary (1-2, 1-3, 2-3), wszystkie w cutoff.
            var stations = new List<RailwayStation>
            {
                St(1, 1, new Vector2(0f, 0f)),
                St(2, 2, new Vector2(80f * Km, 0f)),
                St(3, 3, new Vector2(40f * Km, 60f * Km))
            };
            var importance = new Dictionary<int, float> { { 1, 10f }, { 2, 10f }, { 3, 10f } };
            var m = new OriginDestinationMatrix();
            m.Build(stations, importance);
            Assert.That(m.PairCount, Is.EqualTo(3), "3 stacje w zasięgu → 3 unikalne pary.");
        }
    }
}
