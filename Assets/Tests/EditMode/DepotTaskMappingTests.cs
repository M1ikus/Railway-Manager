using NUnit.Framework;
using DepotSystem;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// TD-031 Etap B: testy mapowania dystans-na-polyline ↔ track-local (TaskTrackSegment +
    /// TrackOccupancyMath). Czysta logika, EditMode. Dwa tory: jeden zgodny z osią, drugi reversed.
    /// </summary>
    public class DepotTaskMappingTests
    {
        // Tor 1: polyline [0,100], zgodny z osią toru, długość 100.
        static TaskTrackSegment Seg1() => new TaskTrackSegment
        { trackId = 1, polyStartM = 0f, polyEndM = 100f, reversedVsTrack = false, trackLenM = 100f };

        // Tor 2: polyline [100,160], jazda PRZECIWNA do osi (reversed), długość 60.
        static TaskTrackSegment Seg2() => new TaskTrackSegment
        { trackId = 2, polyStartM = 100f, polyEndM = 160f, reversedVsTrack = true, trackLenM = 60f };

        [Test]
        public void Forward_Endpoints()
        {
            var s = Seg1();
            Assert.That(TrackOccupancyMath.TaskDistToTrackLocal(s, 0f), Is.EqualTo(0f).Within(1e-4));
            Assert.That(TrackOccupancyMath.TaskDistToTrackLocal(s, 100f), Is.EqualTo(100f).Within(1e-4));
            Assert.That(TrackOccupancyMath.TaskDistToTrackLocal(s, 30f), Is.EqualTo(30f).Within(1e-4));
        }

        [Test]
        public void Reversed_FlipsAxis()
        {
            var s = Seg2();
            // polyStart (100) = KONIEC toru → local = trackLen = 60.
            Assert.That(TrackOccupancyMath.TaskDistToTrackLocal(s, 100f), Is.EqualTo(60f).Within(1e-4));
            // polyEnd (160) = POCZĄTEK toru → local = 0.
            Assert.That(TrackOccupancyMath.TaskDistToTrackLocal(s, 160f), Is.EqualTo(0f).Within(1e-4));
            Assert.That(TrackOccupancyMath.TaskDistToTrackLocal(s, 130f), Is.EqualTo(30f).Within(1e-4));
        }

        [Test]
        public void Forward_InverseRoundTrip()
        {
            var s = Seg1();
            for (float d = 0f; d <= 100f; d += 25f)
            {
                float local = TrackOccupancyMath.TaskDistToTrackLocal(s, d);
                float back = TrackOccupancyMath.TrackLocalToTaskDist(s, local);
                Assert.That(back, Is.EqualTo(d).Within(1e-3), $"Round-trip forward przy d={d}.");
            }
        }

        [Test]
        public void Reversed_InverseRoundTrip()
        {
            var s = Seg2();
            for (float d = 100f; d <= 160f; d += 15f)
            {
                float local = TrackOccupancyMath.TaskDistToTrackLocal(s, d);
                float back = TrackOccupancyMath.TrackLocalToTaskDist(s, local);
                Assert.That(back, Is.EqualTo(d).Within(1e-3), $"Round-trip reversed przy d={d}.");
            }
        }

        [Test]
        public void Clamp_OutOfRange()
        {
            var s = Seg1();
            Assert.That(TrackOccupancyMath.TaskDistToTrackLocal(s, -10f), Is.EqualTo(0f).Within(1e-4));
            Assert.That(TrackOccupancyMath.TaskDistToTrackLocal(s, 200f), Is.EqualTo(100f).Within(1e-4));
        }
    }
}
