using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RailwayManager.Core;
using UnityEngine;

namespace RailwayManager.Tests.EditMode
{
    public class CoreUtilityContractsTests
    {
        private const string UndoPrefsKey = "depot.undo.maxCount";

        private bool _hadUndoPref;
        private int _undoPrefBackup;
        private bool _pausedBackup;
        private float _gameTimeBackup;
        private float _timeScaleBackup;
        private int _gameDayBackup;
        private string _gameStartDateBackup;

        [SetUp]
        public void SetUp()
        {
            _hadUndoPref = PlayerPrefs.HasKey(UndoPrefsKey);
            _undoPrefBackup = PlayerPrefs.GetInt(UndoPrefsKey, UndoSettings.DEFAULT);
            _pausedBackup = GameState.IsPaused;
            _gameTimeBackup = GameState.GameTimeSeconds;
            _timeScaleBackup = GameState.TimeScale;
            _gameDayBackup = GameState.GameDay;
            _gameStartDateBackup = GameState.GameStartDateIso;

            PauseStack.Clear();
            GameState.IsPaused = false;
            MinimapAgentRegistry.ClearAll();
            DestroyExistingGameClocks();
        }

        [TearDown]
        public void TearDown()
        {
            if (_hadUndoPref)
                PlayerPrefs.SetInt(UndoPrefsKey, _undoPrefBackup);
            else
                PlayerPrefs.DeleteKey(UndoPrefsKey);
            PlayerPrefs.Save();

            PauseStack.Clear();
            GameState.IsPaused = _pausedBackup;
            GameState.GameTimeSeconds = _gameTimeBackup;
            GameState.TimeScale = _timeScaleBackup;
            GameState.GameDay = _gameDayBackup;
            GameState.GameStartDateIso = _gameStartDateBackup;

            MinimapAgentRegistry.ClearAll();
            DestroyExistingGameClocks();
        }

        [Test]
        public void UndoSettings_ClampsValuesAndEmitsChangeEvent()
        {
            int eventCount = 0;
            System.Action handler = () => eventCount++;

            UndoSettings.OnChanged += handler;
            try
            {
                UndoSettings.MaxUndos = UndoSettings.MIN - 100;
                Assert.That(UndoSettings.MaxUndos, Is.EqualTo(UndoSettings.MIN));

                UndoSettings.MaxUndos = 7;
                Assert.That(UndoSettings.MaxUndos, Is.EqualTo(7));

                UndoSettings.MaxUndos = UndoSettings.MAX + 100;
                Assert.That(UndoSettings.MaxUndos, Is.EqualTo(UndoSettings.MAX));

                Assert.That(eventCount, Is.EqualTo(3));
            }
            finally
            {
                UndoSettings.OnChanged -= handler;
            }
        }

        [Test]
        public void UIIntents_EmitDeliversIntentToSubscribersInOrder()
        {
            var received = new List<UIIntent>();
            System.Action<UIIntent> handler = intent => received.Add(intent);

            UIIntents.OnIntent += handler;
            try
            {
                UIIntents.Emit(UIIntent.OpenScheduleCreator);
                UIIntents.Emit(UIIntent.ClosePersonnelPanel);
            }
            finally
            {
                UIIntents.OnIntent -= handler;
            }

            Assert.That(received, Is.EqualTo(new[]
            {
                UIIntent.OpenScheduleCreator,
                UIIntent.ClosePersonnelPanel
            }));
        }

        [Test]
        public void MinimapAgentRegistry_RegistersReplacesAndClearsAgentsByType()
        {
            var first = new GameObject("minimap-first");
            var replacement = new GameObject("minimap-replacement");
            var train = new GameObject("minimap-train");
            try
            {
                first.transform.position = new Vector3(1f, 2f, 3f);
                replacement.transform.position = new Vector3(4f, 5f, 6f);
                train.transform.position = new Vector3(7f, 8f, 9f);

                MinimapAgentRegistry.Register(1, MinimapAgentRegistry.AgentType.Employee, first.transform);
                MinimapAgentRegistry.Register(1, MinimapAgentRegistry.AgentType.Employee, replacement.transform);
                MinimapAgentRegistry.Register(2, MinimapAgentRegistry.AgentType.Employee, null);
                MinimapAgentRegistry.Register(3, MinimapAgentRegistry.AgentType.Train, train.transform);

                Assert.That(MinimapAgentRegistry.GetCount(MinimapAgentRegistry.AgentType.Employee), Is.EqualTo(1));
                Assert.That(MinimapAgentRegistry.GetPositions(MinimapAgentRegistry.AgentType.Employee).ToArray(), Is.EqualTo(new[] { replacement.transform.position }));
                Assert.That(MinimapAgentRegistry.GetCount(MinimapAgentRegistry.AgentType.Train), Is.EqualTo(1));

                MinimapAgentRegistry.Unregister(1, MinimapAgentRegistry.AgentType.Employee);
                Assert.That(MinimapAgentRegistry.GetCount(MinimapAgentRegistry.AgentType.Employee), Is.EqualTo(0));

                MinimapAgentRegistry.ClearType(MinimapAgentRegistry.AgentType.Train);
                Assert.That(MinimapAgentRegistry.GetCount(MinimapAgentRegistry.AgentType.Train), Is.EqualTo(0));
            }
            finally
            {
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(replacement);
                Object.DestroyImmediate(train);
            }
        }

        [Test]
        public void GameClock_RollsOverMultipleDaysAndEmitsPreviousDates()
        {
            var clock = CreateGameClock();
            var endedDates = new List<string>();
            System.Action<string> handler = endedDates.Add;

            GameState.OnDayEnded += handler;
            try
            {
                GameState.GameStartDateIso = "2026-05-30";
                GameState.GameDay = 0;
                GameState.GameTimeSeconds = 86400f * 2f + 123f;
                GameState.TimeScale = 1f;
                GameState.IsPaused = false;

                InvokeUpdate(clock);

                Assert.That(GameState.GameDay, Is.EqualTo(2));
                Assert.That(GameState.GameTimeSeconds, Is.InRange(123f, 124f));
                Assert.That(endedDates, Is.EqualTo(new[] { "2026-05-30", "2026-05-31" }));
            }
            finally
            {
                GameState.OnDayEnded -= handler;
            }
        }

        [Test]
        public void GameClock_DoesNotAdvanceOrRolloverWhenPaused()
        {
            var clock = CreateGameClock();
            GameState.GameStartDateIso = "2026-05-30";
            GameState.GameDay = 0;
            GameState.GameTimeSeconds = 86400f + 10f;
            GameState.TimeScale = 1f;
            GameState.IsPaused = true;

            InvokeUpdate(clock);

            Assert.That(GameState.GameDay, Is.EqualTo(0));
            Assert.That(GameState.GameTimeSeconds, Is.EqualTo(86400f + 10f));
        }

        private static GameClock CreateGameClock()
        {
            var go = new GameObject("GameClockTests");
            return go.AddComponent<GameClock>();
        }

        private static void InvokeUpdate(GameClock clock)
        {
            typeof(GameClock)
                .GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(clock, null);
        }

        private static void DestroyExistingGameClocks()
        {
            foreach (var clock in Resources.FindObjectsOfTypeAll<GameClock>())
                Object.DestroyImmediate(clock.gameObject);
        }
    }
}
