using System.Collections.Generic;
using NUnit.Framework;
using Newtonsoft.Json;
using DepotSystem;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// TD-031 Etap A: testy czystej algebry interwałów zajętości toru (TrackOccupancyMath) +
    /// round-trip serializacji TrackOccupant. Czysta logika, EditMode.
    /// </summary>
    public class DepotTrackOccupancyTests
    {
        const float Gap = 0.05f;

        static TrackOccupant Occ(int id, float front, float rear, int dir = 1)
            => new TrackOccupant { ConsistId = id, FrontDistM = front, RearDistM = rear, DirSign = dir };

        // ── RangeOverlaps ────────────────────────────────────────────

        [Test]
        public void RangeOverlaps_Disjoint_False()
            => Assert.That(TrackOccupancyMath.RangeOverlaps(0f, 10f, 20f, 30f, 0f), Is.False);

        [Test]
        public void RangeOverlaps_TouchingExactly_IsFree()
            // [0,10] vs [10,20] — styk dokładnie na granicy przy gap=0 → wolne (półotwarte).
            => Assert.That(TrackOccupancyMath.RangeOverlaps(0f, 10f, 10f, 20f, 0f), Is.False);

        [Test]
        public void RangeOverlaps_WithinGap_True()
            // odstęp 0.03 < gap 0.05 → za blisko → traktowane jako overlap.
            => Assert.That(TrackOccupancyMath.RangeOverlaps(0f, 10f, 10.03f, 20f, Gap), Is.True);

        [Test]
        public void RangeOverlaps_Overlapping_True()
            => Assert.That(TrackOccupancyMath.RangeOverlaps(0f, 10f, 5f, 15f, 0f), Is.True);

        // ── IsRangeFree ──────────────────────────────────────────────

        [Test]
        public void IsRangeFree_Empty_True()
            => Assert.That(TrackOccupancyMath.IsRangeFree(new List<TrackOccupant>(), 0f, 10f, -1, Gap), Is.True);

        [Test]
        public void IsRangeFree_IgnoresSelf_ButOtherBlocks()
        {
            var occ = new List<TrackOccupant> { Occ(7, 0f, 10f) };
            Assert.That(TrackOccupancyMath.IsRangeFree(occ, 0f, 10f, 7, Gap), Is.True, "Własny consist ignorowany.");
            Assert.That(TrackOccupancyMath.IsRangeFree(occ, 0f, 10f, 9, Gap), Is.False, "Inny consist blokuje.");
        }

        [Test]
        public void IsRangeFree_UnorderedArgs_Handled()
        {
            var occ = new List<TrackOccupant> { Occ(1, 40f, 50f) };
            Assert.That(TrackOccupancyMath.IsRangeFree(occ, 50f, 45f, -1, Gap), Is.False, "to<from też wykrywa overlap.");
        }

        // ── TryFindFreeGap ──────────────────────────────────────────

        [Test]
        public void TryFindFreeGap_EmptyTrack_FromZero()
        {
            Assert.That(TrackOccupancyMath.TryFindFreeGap(new List<TrackOccupant>(), 100f, 20f, 0.5f, out float s), Is.True);
            Assert.That(s, Is.EqualTo(0f));
        }

        [Test]
        public void TryFindFreeGap_TooLong_False()
            => Assert.That(TrackOccupancyMath.TryFindFreeGap(new List<TrackOccupant>(), 10f, 20f, 0.5f, out _), Is.False);

        [Test]
        public void TryFindFreeGap_AfterOccupant()
        {
            var occ = new List<TrackOccupant> { Occ(1, 0f, 40f) };
            Assert.That(TrackOccupancyMath.TryFindFreeGap(occ, 100f, 20f, 0.5f, out float s), Is.True);
            Assert.That(s, Is.EqualTo(40.5f).Within(1e-4), "Za occupantem [0,40] + gap 0.5.");
        }

        [Test]
        public void TryFindFreeGap_BetweenOccupants()
        {
            var occ = new List<TrackOccupant> { Occ(1, 0f, 20f), Occ(2, 60f, 100f) };
            Assert.That(TrackOccupancyMath.TryFindFreeGap(occ, 100f, 30f, 0.5f, out float s), Is.True);
            Assert.That(s, Is.EqualTo(20.5f).Within(1e-4), "Luka [20.5, 59.5] mieści 30.");
        }

        [Test]
        public void TryFindFreeGap_GapTooSmall_False()
        {
            var occ = new List<TrackOccupant> { Occ(1, 0f, 20f), Occ(2, 30f, 100f) };
            Assert.That(TrackOccupancyMath.TryFindFreeGap(occ, 100f, 30f, 0.5f, out _), Is.False,
                "Luka [20.5,29.5]=9m < 30, brak miejsca po ostatnim.");
        }

        [Test]
        public void TryFindFreeGap_UnsortedInput_Deterministic()
        {
            var occ = new List<TrackOccupant> { Occ(2, 60f, 100f), Occ(1, 0f, 20f) }; // odwrotna kolejność
            Assert.That(TrackOccupancyMath.TryFindFreeGap(occ, 100f, 30f, 0.5f, out float s), Is.True);
            Assert.That(s, Is.EqualTo(20.5f).Within(1e-4), "Sortowanie wewn. → wynik niezależny od kolejności wejścia.");
        }

        // ── FindNearestOccupantAhead ─────────────────────────────────

        [Test]
        public void FindNearestAhead_Forward_PicksSmallestFront()
        {
            var occ = new List<TrackOccupant> { Occ(1, 80f, 90f), Occ(2, 40f, 50f) };
            int id = TrackOccupancyMath.FindNearestOccupantAhead(occ, 10f, +1, -1, out float edge);
            Assert.That(id, Is.EqualTo(2));
            Assert.That(edge, Is.EqualTo(40f).Within(1e-4), "Krawędź = Front najbliższego z przodu.");
        }

        [Test]
        public void FindNearestAhead_Reverse_PicksLargestRear()
        {
            var occ = new List<TrackOccupant> { Occ(1, 10f, 20f), Occ(2, 40f, 50f) };
            int id = TrackOccupancyMath.FindNearestOccupantAhead(occ, 80f, -1, -1, out float edge);
            Assert.That(id, Is.EqualTo(2));
            Assert.That(edge, Is.EqualTo(50f).Within(1e-4), "Przy ruchu wstecz krawędź = Rear.");
        }

        [Test]
        public void FindNearestAhead_IgnoresSelfAndBehind()
        {
            var occ = new List<TrackOccupant> { Occ(5, 40f, 50f) };
            Assert.That(TrackOccupancyMath.FindNearestOccupantAhead(occ, 10f, +1, 5, out _), Is.EqualTo(-1), "Self ignorowany.");
            Assert.That(TrackOccupancyMath.FindNearestOccupantAhead(occ, 60f, +1, -1, out _), Is.EqualTo(-1), "Occupant z tyłu nie liczy się.");
        }

        // ── Serializacja ─────────────────────────────────────────────

        // ── Backward-compat legacy → pozycyjna (Etap F) ──────────────

        [Test]
        public void SynthesizeLegacyOccupant_FromBinaryFields_WholeTrack()
        {
            var track = new DepotTrackData
            {
                TrackId = 1,
                Length = 80f,
                IsOccupied = true,
                OccupyingConsistId = 5,
                OccupyingVehicleIds = new List<int> { 100, 101 },
                Occupants = new List<TrackOccupant>()
            };
            bool added = TrackOccupancyMath.SynthesizeLegacyOccupant(track);
            Assert.That(added, Is.True);
            Assert.That(track.Occupants.Count, Is.EqualTo(1));
            Assert.That(track.Occupants[0].ConsistId, Is.EqualTo(5));
            Assert.That(track.Occupants[0].FrontDistM, Is.EqualTo(0f));
            Assert.That(track.Occupants[0].RearDistM, Is.EqualTo(80f), "Footprint = cały tor (model binarny).");
            Assert.That(track.Occupants[0].VehicleIds, Is.EquivalentTo(new[] { 100, 101 }));
        }

        [Test]
        public void SynthesizeLegacyOccupant_NoopWhenHasOccupants()
        {
            var track = new DepotTrackData
            {
                TrackId = 1, Length = 80f, IsOccupied = true, OccupyingConsistId = 5,
                Occupants = new List<TrackOccupant> { Occ(9, 10f, 30f) }
            };
            Assert.That(TrackOccupancyMath.SynthesizeLegacyOccupant(track), Is.False);
            Assert.That(track.Occupants.Count, Is.EqualTo(1));
            Assert.That(track.Occupants[0].ConsistId, Is.EqualTo(9), "Istniejące Occupants nietknięte.");
        }

        [Test]
        public void SynthesizeLegacyOccupant_NoopWhenFree()
        {
            var track = new DepotTrackData
            {
                TrackId = 1, Length = 80f, IsOccupied = false, OccupyingConsistId = -1,
                Occupants = new List<TrackOccupant>()
            };
            Assert.That(TrackOccupancyMath.SynthesizeLegacyOccupant(track), Is.False);
            Assert.That(track.Occupants.Count, Is.EqualTo(0));
        }

        [Test]
        public void TrackOccupant_RoundTrip_PreservesFields()
        {
            var occ = new TrackOccupant
            {
                ConsistId = 22,
                VehicleIds = new List<int> { 200, 201 },
                FrontDistM = 50f,
                RearDistM = 70f,
                DirSign = -1
            };
            string json = JsonConvert.SerializeObject(occ);
            var back = JsonConvert.DeserializeObject<TrackOccupant>(json);

            Assert.That(back.ConsistId, Is.EqualTo(22));
            Assert.That(back.VehicleIds, Is.EquivalentTo(new[] { 200, 201 }));
            Assert.That(back.FrontDistM, Is.EqualTo(50f));
            Assert.That(back.RearDistM, Is.EqualTo(70f));
            Assert.That(back.DirSign, Is.EqualTo(-1));
            Assert.That(back.LengthM, Is.EqualTo(20f).Within(1e-4));
        }
    }
}
