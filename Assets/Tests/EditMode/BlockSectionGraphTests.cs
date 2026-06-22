using System.Collections.Generic;
using NUnit.Framework;
using RailwayManager.Timetable;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// M4: testy BlockSectionGraph — lookup nad odcinkami blokowymi (rezerwacja szlakowa).
    /// Czysta część (konstruktor + GetSection + GetSectionForEdge) testowana EditMode z ręcznie
    /// zbudowanym BuildResult (POCO). GetSectionsForRoute wymaga PathfindingGraph → PlayMode (osobno).
    ///
    /// Rdzeń ryzyka to indeksowanie tablic (bounds, edge→section mapping, -1 dla nieprzypisanych).
    /// </summary>
    public class BlockSectionGraphTests
    {
        static BlockSection Sec(int id, int start, int end, float len = 1000f, int vmax = 120)
            => new BlockSection
            {
                id = id, startNodeId = start, endNodeId = end,
                lengthM = len, maxSpeedKmh = vmax, edgeCount = 1,
                startBoundary = BoundaryType.Station, endBoundary = BoundaryType.Signal
            };

        static BlockSectionGraph Build(List<BlockSection> sections, int[] edgeToSection)
            => new BlockSectionGraph(new BlockSectionBuilder.BuildResult
            {
                sections = sections,
                edgeToSection = edgeToSection
            });

        [Test]
        public void SectionCount_MatchesInput()
        {
            var g = Build(new List<BlockSection> { Sec(0, 1, 2), Sec(1, 2, 3) }, new[] { 0, 0, 1 });
            Assert.That(g.SectionCount, Is.EqualTo(2));
        }

        [Test]
        public void GetSection_ValidId_ReturnsSection()
        {
            var g = Build(new List<BlockSection> { Sec(0, 10, 20, len: 500f, vmax: 160) }, new[] { 0 });
            var s = g.GetSection(0);
            Assert.That(s.id, Is.EqualTo(0));
            Assert.That(s.startNodeId, Is.EqualTo(10));
            Assert.That(s.endNodeId, Is.EqualTo(20));
            Assert.That(s.lengthM, Is.EqualTo(500f));
            Assert.That(s.maxSpeedKmh, Is.EqualTo(160));
        }

        [Test]
        public void GetSection_OutOfRange_ReturnsDefault()
        {
            var g = Build(new List<BlockSection> { Sec(0, 1, 2) }, new[] { 0 });
            // Negatywny i poza zakresem → default (BlockSection struct, id=0 default).
            Assert.That(g.GetSection(-1).lengthM, Is.EqualTo(0f), "Negatywny id → default struct.");
            Assert.That(g.GetSection(99).lengthM, Is.EqualTo(0f), "Id poza zakresem → default struct.");
        }

        [Test]
        public void GetSectionForEdge_MapsCorrectly()
        {
            // edge 0,1 → section 0; edge 2,3 → section 1.
            var g = Build(new List<BlockSection> { Sec(0, 1, 2), Sec(1, 2, 3) }, new[] { 0, 0, 1, 1 });
            Assert.That(g.GetSectionForEdge(0), Is.EqualTo(0));
            Assert.That(g.GetSectionForEdge(1), Is.EqualTo(0));
            Assert.That(g.GetSectionForEdge(2), Is.EqualTo(1));
            Assert.That(g.GetSectionForEdge(3), Is.EqualTo(1));
        }

        [Test]
        public void GetSectionForEdge_Unassigned_ReturnsMinusOne()
        {
            // edge 1 nieprzypisany (-1 w tablicy).
            var g = Build(new List<BlockSection> { Sec(0, 1, 2) }, new[] { 0, -1, 0 });
            Assert.That(g.GetSectionForEdge(1), Is.EqualTo(-1), "Nieprzypisany edge → -1.");
        }

        [Test]
        public void GetSectionForEdge_OutOfRange_ReturnsMinusOne()
        {
            var g = Build(new List<BlockSection> { Sec(0, 1, 2) }, new[] { 0 });
            Assert.That(g.GetSectionForEdge(-1), Is.EqualTo(-1), "Negatywny edgeId → -1.");
            Assert.That(g.GetSectionForEdge(999), Is.EqualTo(-1), "edgeId poza zakresem → -1.");
        }

        [Test]
        public void EmptyBuildResult_NoCrash()
        {
            var g = Build(new List<BlockSection>(), new int[0]);
            Assert.That(g.SectionCount, Is.EqualTo(0));
            Assert.That(g.GetSectionForEdge(0), Is.EqualTo(-1));
            Assert.That(g.GetSection(0).lengthM, Is.EqualTo(0f));
        }

        [Test]
        public void NullArrays_HandledGracefully()
        {
            // BuildResult z null sections/edgeToSection — konstruktor ma fallback do pustych tablic.
            var g = new BlockSectionGraph(new BlockSectionBuilder.BuildResult { sections = null, edgeToSection = null });
            Assert.That(g.SectionCount, Is.EqualTo(0));
            Assert.That(g.GetSectionForEdge(5), Is.EqualTo(-1));
            Assert.DoesNotThrow(() => g.GetSection(0));
        }

        [Test]
        public void BoundaryTypes_PreservedInSection()
        {
            var sec = new BlockSection
            {
                id = 0, startNodeId = 1, endNodeId = 2,
                startBoundary = BoundaryType.Junction, endBoundary = BoundaryType.LineEnd
            };
            var g = Build(new List<BlockSection> { sec }, new[] { 0 });
            var r = g.GetSection(0);
            Assert.That(r.startBoundary, Is.EqualTo(BoundaryType.Junction));
            Assert.That(r.endBoundary, Is.EqualTo(BoundaryType.LineEnd));
        }
    }
}
