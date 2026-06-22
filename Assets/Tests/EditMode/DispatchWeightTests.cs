using NUnit.Framework;
using RailwayManager.Timetable.Simulation;
using RailwayManager.Timetable;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// M-Dispatch Faza 4c: testy formuly wagi (DispatchWeight.Compute) — priorytet IRJ
    /// skalowany + obloznie pasazerami (z capem). Czysta funkcja.
    /// </summary>
    public class DispatchWeightTests
    {
        readonly int _scale = TimetableTuningConstants.DispatchPriorityScale;
        readonly int _cap = TimetableTuningConstants.DispatchMaxLoadWeight;

        [Test]
        public void NoLoad_IsPriorityTimesScale()
        {
            Assert.That(DispatchWeight.Compute(3, 0), Is.EqualTo(3 * _scale));
            Assert.That(DispatchWeight.Compute(5, 0), Is.EqualTo(5 * _scale));
        }

        [Test]
        public void Load_AddsLinearlyUpToCap()
        {
            Assert.That(DispatchWeight.Compute(3, 150), Is.EqualTo(3 * _scale + 150));
            Assert.That(DispatchWeight.Compute(3, 100000), Is.EqualTo(3 * _scale + _cap), "Oblozenie capowane.");
        }

        [Test]
        public void NegativeLoad_ClampedToZero()
        {
            Assert.That(DispatchWeight.Compute(4, -50), Is.EqualTo(4 * _scale));
        }

        [Test]
        public void FullLocal_CanOutweighEmptyExpress()
        {
            // Osobowy (prio 3) zapchany pasazerami vs pospieszny (prio 5) pusty.
            int fullLocal = DispatchWeight.Compute(3, _cap);  // 3*100 + 300 = 600
            int emptyExpress = DispatchWeight.Compute(5, 0);  // 5*100      = 500
            Assert.That(fullLocal, Is.GreaterThan(emptyExpress),
                "Zapchany osobowy przewaza pusty pospieszny — obloznie ma znaczenie.");
        }
    }
}
