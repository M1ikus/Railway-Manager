using System;
using System.Globalization;
using System.Threading;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using RailwayManager.Core;

namespace RailwayManager.Tests.EditMode
{
    public class CoreTimeAndRandomTests
    {
        private CultureInfo _cultureBackup;
        private CultureInfo _uiCultureBackup;
        private int _seedBackup;

        [SetUp]
        public void SetUp()
        {
            _cultureBackup = Thread.CurrentThread.CurrentCulture;
            _uiCultureBackup = Thread.CurrentThread.CurrentUICulture;
            _seedBackup = GameState.Seed;
        }

        [TearDown]
        public void TearDown()
        {
            Thread.CurrentThread.CurrentCulture = _cultureBackup;
            Thread.CurrentThread.CurrentUICulture = _uiCultureBackup;
            GameState.Seed = _seedBackup;
            RandomRegistry.ApplyFromJson(null);
        }

        [Test]
        public void IsoTime_ParsesIsoValuesUnderNonInvariantCulture()
        {
            var culture = CultureInfo.GetCultureInfo("tr-TR");
            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;

            Assert.That(IsoTime.ParseDate("2026-05-30"), Is.EqualTo(new DateTime(2026, 5, 30)));
            Assert.That(IsoTime.ParseDateExact("2026-05-30", "yyyy-MM-dd"), Is.EqualTo(new DateTime(2026, 5, 30)));
            Assert.That(IsoTime.ParseTime("12:34:56"), Is.EqualTo(new TimeSpan(12, 34, 56)));
        }

        [Test]
        public void IsoTime_TryParseReturnsFalseForInvalidValues()
        {
            Assert.That(IsoTime.TryParseDate("not-a-date", out _), Is.False);
            Assert.That(IsoTime.TryParseTime("25:99:99", out _), Is.False);

            Assert.That(IsoTime.TryParseDate("2026-05-30", out var date), Is.True);
            Assert.That(date, Is.EqualTo(new DateTime(2026, 5, 30)));

            Assert.That(IsoTime.TryParseTime("06:07:08", out var time), Is.True);
            Assert.That(time, Is.EqualTo(new TimeSpan(6, 7, 8)));
        }

        [Test]
        public void RandomRegistry_ResetAllReplaysSameSequenceForSameSeedAndSystem()
        {
            const string systemId = "_editmode_rng_replay";
            GameState.Seed = 123456;

            RandomRegistry.ResetAll();
            var rng = RandomRegistry.GetRng(systemId);
            var firstRun = new[] { rng.NextInt(), rng.Range(0, 1000), rng.Value };

            RandomRegistry.ResetAll();
            rng = RandomRegistry.GetRng(systemId);
            var secondRun = new[] { rng.NextInt(), rng.Range(0, 1000), rng.Value };

            Assert.That(secondRun, Is.EqualTo(firstRun));
        }

        [Test]
        public void RandomRegistry_SystemIdsUseIndependentSeeds()
        {
            GameState.Seed = 42;
            RandomRegistry.ResetAll();

            var first = RandomRegistry.GetRng("_editmode_rng_a").ToJson();
            var second = RandomRegistry.GetRng("_editmode_rng_b").ToJson();

            Assert.That(first.Value<int>("seed"), Is.Not.EqualTo(second.Value<int>("seed")));
            Assert.That(first.Value<uint>("state"), Is.Not.EqualTo(second.Value<uint>("state")));
        }

        [Test]
        public void RandomRegistry_ApplyFromJsonRestoresExistingAndPendingStreams()
        {
            GameState.Seed = 9876;
            RandomRegistry.ResetAll();

            var existing = RandomRegistry.GetRng("_editmode_rng_existing");
            existing.NextInt();
            JObject snapshot = RandomRegistry.ToJson();

            int expectedNext = existing.NextInt();
            existing.NextInt();

            var pending = new DeterministicRng(13579);
            pending.NextInt();
            snapshot["_editmode_rng_pending"] = pending.ToJson();
            int expectedPendingNext = pending.NextInt();

            RandomRegistry.ApplyFromJson(snapshot);

            Assert.That(RandomRegistry.GetRng("_editmode_rng_existing").NextInt(), Is.EqualTo(expectedNext));
            Assert.That(RandomRegistry.GetRng("_editmode_rng_pending").NextInt(), Is.EqualTo(expectedPendingNext));
        }
    }
}
