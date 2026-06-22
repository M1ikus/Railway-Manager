using System.Linq;
using NUnit.Framework;
using RailwayManager.Core;

namespace RailwayManager.Tests.EditMode
{
    public class CorePauseAndDlcTests
    {
        [SetUp]
        public void SetUp()
        {
            PauseStack.Clear();
            GameState.IsPaused = false;
            GameState.ResetDlcCountries();
        }

        [TearDown]
        public void TearDown()
        {
            PauseStack.Clear();
            GameState.IsPaused = false;
            GameState.ResetDlcCountries();
        }

        [Test]
        public void PauseStack_PushPopAreIdempotentAndDescribeOwners()
        {
            Assert.That(PauseStack.Push("modal.save_load"), Is.True);
            Assert.That(PauseStack.Push("modal.save_load"), Is.False);
            Assert.That(PauseStack.Push("alert.breakdown"), Is.True);

            Assert.That(PauseStack.Count, Is.EqualTo(2));
            Assert.That(PauseStack.Contains("modal.save_load"), Is.True);
            Assert.That(PauseStack.DescribeForLog(), Does.Contain("modal.save_load"));
            Assert.That(PauseStack.DescribeForLog(), Does.Contain("alert.breakdown"));

            Assert.That(PauseStack.Pop("modal.save_load"), Is.True);
            Assert.That(PauseStack.Pop("modal.save_load"), Is.False);
            Assert.That(PauseStack.Count, Is.EqualTo(1));

            PauseStack.Clear();
            Assert.That(PauseStack.HasOwners, Is.False);
            Assert.That(PauseStack.DescribeForLog(), Is.EqualTo("PauseStack: empty"));
        }

        [Test]
        public void GameState_IsPausedCombinesLegacyPauseAndPauseStack()
        {
            Assert.That(GameState.IsPaused, Is.False);

            PauseStack.Push("popup.timetable");
            Assert.That(GameState.IsPaused, Is.True);

            GameState.IsPaused = false;
            Assert.That(GameState.IsPaused, Is.True, "Stack owner should keep the game paused even if legacy flag is false.");

            PauseStack.Pop("popup.timetable");
            Assert.That(GameState.IsPaused, Is.False);

            GameState.IsPaused = true;
            Assert.That(GameState.IsPaused, Is.True);

            PauseStack.Push("alert");
            PauseStack.Clear();
            Assert.That(GameState.IsPaused, Is.True, "Clearing stack should not clear legacy pause.");
        }

        [Test]
        public void DlcCountries_DefaultToPolandAndProtectLastCountry()
        {
            Assert.That(GameState.ActiveDlcCountries.ToArray(), Is.EqualTo(new[] { "PL" }));
            Assert.That(GameState.IsCountryActive("PL"), Is.True);

            Assert.That(GameState.RemoveDlcCountry("PL"), Is.False);
            Assert.That(GameState.ActiveDlcCountries.ToArray(), Is.EqualTo(new[] { "PL" }));
            Assert.That(GameState.IsCountryActive(""), Is.False);
            Assert.That(GameState.IsCountryActive(null), Is.False);
        }

        [Test]
        public void DlcCountries_AddIsIdempotentAndRemoveKeepsAtLeastOneCountry()
        {
            GameState.AddDlcCountry("DE");
            GameState.AddDlcCountry("DE");
            GameState.AddDlcCountry(null);

            Assert.That(GameState.ActiveDlcCountries.ToArray(), Is.EqualTo(new[] { "PL", "DE" }));
            Assert.That(GameState.IsCountryActive("DE"), Is.True);

            Assert.That(GameState.RemoveDlcCountry("DE"), Is.True);
            Assert.That(GameState.ActiveDlcCountries.ToArray(), Is.EqualTo(new[] { "PL" }));
            Assert.That(GameState.RemoveDlcCountry("missing"), Is.False);
        }
    }
}
