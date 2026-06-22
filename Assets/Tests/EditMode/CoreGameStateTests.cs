using NUnit.Framework;
using RailwayManager.Core;

namespace RailwayManager.Tests.EditMode
{
    public class CoreGameStateTests
    {
        private long _moneyBackup;
        private int _reputationBackup;
        private int _homeDepotBackup;

        [SetUp]
        public void SetUp()
        {
            _moneyBackup = GameState.Money;
            _reputationBackup = GameState.GlobalReputation;
            _homeDepotBackup = GameState.HomeDepotStationId;
        }

        [TearDown]
        public void TearDown()
        {
            GameState.Money = _moneyBackup;
            GameState.GlobalReputation = _reputationBackup;
            GameState.HomeDepotStationId = _homeDepotBackup;
        }

        [Test]
        public void Money_SetterRaisesEventOnlyWhenValueChanges()
        {
            int eventCount = 0;
            long lastOld = -1;
            long lastNew = -1;
            System.Action<long, long> handler = (oldValue, newValue) =>
            {
                eventCount++;
                lastOld = oldValue;
                lastNew = newValue;
            };

            GameState.OnMoneyChanged += handler;
            try
            {
                long target = _moneyBackup == 12345L ? 54321L : 12345L;

                GameState.Money = target;
                Assert.That(eventCount, Is.EqualTo(1));
                Assert.That(lastOld, Is.EqualTo(_moneyBackup));
                Assert.That(lastNew, Is.EqualTo(target));

                GameState.Money = target;
                Assert.That(eventCount, Is.EqualTo(1), "Setting the same value should not emit another event.");
            }
            finally
            {
                GameState.OnMoneyChanged -= handler;
            }
        }

        [Test]
        public void GlobalReputation_ClampsToZeroHundredRange()
        {
            GameState.GlobalReputation = 9999;
            Assert.That(GameState.GlobalReputation, Is.EqualTo(100));

            GameState.GlobalReputation = -50;
            Assert.That(GameState.GlobalReputation, Is.EqualTo(0));

            GameState.GlobalReputation = 50;
            Assert.That(GameState.GlobalReputation, Is.EqualTo(50));
        }

        [Test]
        public void IsHomeDepotSet_UsesNegativeOneAsOnlyUnsetSentinel()
        {
            GameState.HomeDepotStationId = -1;
            Assert.That(GameState.IsHomeDepotSet, Is.False);

            GameState.HomeDepotStationId = 0;
            Assert.That(GameState.IsHomeDepotSet, Is.True);

            GameState.HomeDepotStationId = 42;
            Assert.That(GameState.IsHomeDepotSet, Is.True);
        }
    }
}
