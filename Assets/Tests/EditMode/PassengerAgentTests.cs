using NUnit.Framework;
using RailwayManager.Timetable.Economy;
using UnityEngine;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// M6-1: testy PassengerAgent POCO — stan życia agenta + helpery. Czysty POCO (zero zależności),
    /// EditMode. Logika manager'a (spawn/abandon/board) wymaga grafu+OD → PlayMode (PassengerSimulationTests).
    /// </summary>
    public class PassengerAgentTests
    {
        [Test]
        public void IsAlive_TrueForActiveStates()
        {
            Assert.That(new PassengerAgent { state = PassengerState.WaitingAtStation }.IsAlive, Is.True);
            Assert.That(new PassengerAgent { state = PassengerState.Boarding }.IsAlive, Is.True);
            Assert.That(new PassengerAgent { state = PassengerState.OnTrain }.IsAlive, Is.True);
            Assert.That(new PassengerAgent { state = PassengerState.Alighting }.IsAlive, Is.True);
        }

        [Test]
        public void IsAlive_FalseForTerminalStates()
        {
            Assert.That(new PassengerAgent { state = PassengerState.Arrived }.IsAlive, Is.False,
                "Arrived → do usunięcia z puli.");
            Assert.That(new PassengerAgent { state = PassengerState.Abandoned }.IsAlive, Is.False,
                "Abandoned (przekroczona cierpliwość) → do usunięcia.");
        }

        [Test]
        public void GetStraightLineDistance_UsesLookup()
        {
            var a = new PassengerAgent { originStationId = 1, destinationStationId = 2 };
            Vector2 Lookup(int id) => id == 1 ? new Vector2(0f, 0f) : new Vector2(300f, 400f);
            // 3-4-5 trójkąt: distance = 500.
            Assert.That(a.GetStraightLineDistance(Lookup), Is.EqualTo(500f).Within(0.01f));
        }

        [Test]
        public void DefaultState_IsWaitingAtStation()
        {
            // Świeży agent startuje WaitingAtStation (na peronie, czeka na pociąg).
            Assert.That(new PassengerAgent().state, Is.EqualTo(PassengerState.WaitingAtStation));
            Assert.That(new PassengerAgent().IsAlive, Is.True);
            Assert.That(new PassengerAgent().currentTrainRunId, Is.EqualTo(-1), "Domyślnie nie w pociągu.");
        }
    }
}
