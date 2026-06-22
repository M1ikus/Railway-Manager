using System.Collections.Generic;
using NUnit.Framework;
using RailwayManager.Timetable;
using RailwayManager.Timetable.Assistant;
using RailwayManager.Timetable.UI;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// M11 AS-5c (część czysta): elektryfikacja per metadata krawędzi, mapowania
    /// archetyp→kategoria/IRJ, konwersja wariantu częstotliwości→FrequencySpec
    /// (kontrakt: RunsPerDay taktu == runsPerDay wariantu). Ścieżka scenowa
    /// (Gather/Create z grafem) = smoke w AS-5c part 2 (UI).
    /// </summary>
    public class TimetableFromRelationPureTests
    {
        static Dictionary<string, string> Meta(string electrified) =>
            electrified == null
                ? new Dictionary<string, string>()
                : new Dictionary<string, string> { ["electrified"] = electrified };

        [Test]
        public void IsEdgeElectrified_OsmTagValues()
        {
            Assert.That(RelationFactsGatherer.IsEdgeElectrified(Meta("yes")), Is.True);
            Assert.That(RelationFactsGatherer.IsEdgeElectrified(Meta("contact_line")), Is.True);
            Assert.That(RelationFactsGatherer.IsEdgeElectrified(Meta("rail")), Is.True);
            Assert.That(RelationFactsGatherer.IsEdgeElectrified(Meta("3rd_rail")), Is.True);

            Assert.That(RelationFactsGatherer.IsEdgeElectrified(Meta("no")), Is.False);
            Assert.That(RelationFactsGatherer.IsEdgeElectrified(Meta("")), Is.False);
            Assert.That(RelationFactsGatherer.IsEdgeElectrified(Meta(null)), Is.False, "Brak tagu = brak sieci");
            Assert.That(RelationFactsGatherer.IsEdgeElectrified(null), Is.False);
        }

        [Test]
        public void ArchetypeMappings_CategoryAndIrj()
        {
            Assert.That(TimetableFromRelationService.MapArchetypeToCategoryId(RelationArchetype.Agglomeration), Is.EqualTo("os"));
            Assert.That(TimetableFromRelationService.MapArchetypeToCategoryId(RelationArchetype.Regional), Is.EqualTo("os"));
            Assert.That(TimetableFromRelationService.MapArchetypeToCategoryId(RelationArchetype.Interregional), Is.EqualTo("rp"));

            Assert.That(TimetableFromRelationService.MapArchetypeToIrjGroup(RelationArchetype.Agglomeration), Is.EqualTo(IrjGroup.RegionalAgglomeration));
            Assert.That(TimetableFromRelationService.MapArchetypeToIrjGroup(RelationArchetype.Regional), Is.EqualTo(IrjGroup.RegionalLocal));
            Assert.That(TimetableFromRelationService.MapArchetypeToIrjGroup(RelationArchetype.Interregional), Is.EqualTo(IrjGroup.InterregionalFast));
        }

        [Test]
        public void BuildFrequency_TaktRunsMatchVariant()
        {
            var variant = new FrequencyVariant { variantKey = "balanced", runsPerDay = 9, taktMinutes = 120 };
            var freq = TimetableFromRelationService.BuildFrequency(variant,
                TimetableFromRelationService.FirstRunMinutesFromMidnight);

            Assert.That(freq.type, Is.EqualTo(FrequencyType.Takt));
            Assert.That(freq.intervalMinutes, Is.EqualTo(120));
            Assert.That(freq.firstRunMinutesFromMidnight, Is.EqualTo(300), "Pierwszy kurs 05:00");
            Assert.That(freq.lastRunMinutesFromMidnight, Is.EqualTo(300 + 8 * 120));
            Assert.That(freq.RunsPerDay(), Is.EqualTo(9),
                "KONTRAKT: RunsPerDay taktu == runsPerDay wariantu (preview nie kłamie)");
        }

        [Test]
        public void BuildFrequency_SingleRunAndDegenerateTakt()
        {
            var single = new FrequencyVariant { runsPerDay = 1, taktMinutes = 0 };
            Assert.That(TimetableFromRelationService.BuildFrequency(single, 300).type,
                Is.EqualTo(FrequencyType.Single));

            var degenerate = new FrequencyVariant { runsPerDay = 2, taktMinutes = 0 };
            var freq = TimetableFromRelationService.BuildFrequency(degenerate, 300);
            Assert.That(freq.intervalMinutes, Is.GreaterThanOrEqualTo(1), "Interval clampowany — bez dzielenia przez zero");
            Assert.That(freq.RunsPerDay(), Is.EqualTo(2));
        }

        // ────────────── AS-5c cz.2: autocomplete stacji (pure) ──────────────

        static RailwayStation St(string name) => new RailwayStation { name = name };

        [Test]
        public void FilterStations_PrefixFirstThenContains_CapAndMinLength()
        {
            var stations = new List<RailwayStation>
            {
                St("Kraków Główny"), St("Warszawa Centralna"), St("Nowy Kraków"),
                St("Kraków Płaszów"), St("Olsztyn")
            };
            var results = new List<RailwayStation>();

            RelationPlannerUI.FilterStations(stations, "Kra", results, max: 8);
            Assert.That(results.Count, Is.EqualTo(3));
            Assert.That(results[0].name, Does.StartWith("Kraków"), "Prefiksy przed zawieraniem");
            Assert.That(results[1].name, Does.StartWith("Kraków"));
            Assert.That(results[2].name, Is.EqualTo("Nowy Kraków"), "Zawieranie po prefiksach");

            RelationPlannerUI.FilterStations(stations, "K", results, max: 8);
            Assert.That(results, Is.Empty, "Min 2 znaki — bez zalewu przy pierwszej literze");

            RelationPlannerUI.FilterStations(stations, "kraków", results, max: 2);
            Assert.That(results.Count, Is.EqualTo(2), "Cap max wyników + case-insensitive");
        }
    }
}
