using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using RailwayManager.Core;
using RailwayManager.SaveLoad;

namespace RailwayManager.Tests.EditMode
{
    public class SaveLoadRoundtripTests
    {
        private const string TestFolder = "Saves_EditModeTests";
        private const string TestSlotId = "editmode_roundtrip";

        [SetUp]
        public void SetUp()
        {
            SaveRegistry.Clear();
            DeleteTestFolder();
        }

        [TearDown]
        public void TearDown()
        {
            SaveRegistry.Clear();
            DeleteTestFolder();
        }

        [Test]
        public async Task SaveOrchestrator_RoundTripsRegisteredModules()
        {
            var moduleA = new DummyModuleA { CounterValue = 42, NameValue = "Test A" };
            var moduleB = new DummyModuleB { Pi = 3.14159, EnabledFlag = true, ListValues = new[] { 1, 2, 3, 4, 5 } };

            Assert.That(SaveRegistry.Register(moduleA), Is.True);
            Assert.That(SaveRegistry.Register(moduleB), Is.True);

            var storage = new LocalDiskStorage(TestFolder);
            var orchestrator = new SaveOrchestrator(storage, "editmode-test");

            bool saved = await orchestrator.SaveAsync(
                TestSlotId,
                "EditMode roundtrip",
                SaveTypes.Test,
                playtime: 123.45,
                gameTimeIso: "2026-05-29T12:00:00");

            Assert.That(saved, Is.True);
            Assert.That(File.Exists(Path.Combine(storage.SaveFolder, TestSlotId + LocalDiskStorage.FileExtension)), Is.True);

            moduleA.CounterValue = 0;
            moduleA.NameValue = "mutated";
            moduleB.Pi = 0;
            moduleB.EnabledFlag = false;
            moduleB.ListValues = new int[0];

            var loadResult = await orchestrator.LoadAsync(TestSlotId);
            Assert.That(loadResult.IsSuccess, Is.True, loadResult.ErrorMessage);

            Assert.That(moduleA.CounterValue, Is.EqualTo(42));
            Assert.That(moduleA.NameValue, Is.EqualTo("Test A"));
            Assert.That(moduleB.Pi, Is.EqualTo(3.14159).Within(0.000001));
            Assert.That(moduleB.EnabledFlag, Is.True);
            Assert.That(moduleB.ListValues, Is.EquivalentTo(new[] { 1, 2, 3, 4, 5 }));
        }

        private static void DeleteTestFolder()
        {
            string path = Path.Combine(AppPaths.PersistentRoot, TestFolder);
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, recursive: true);
            }
            catch
            {
                // Best-effort cleanup: a locked file should fail the test itself, not teardown.
            }
        }

        private class DummyModuleA : ISavable
        {
            public string ModuleId => "_editmode_a";
            public int SchemaVersion => 1;

            public int CounterValue;
            public string NameValue = "";

            public JObject Serialize() => new JObject
            {
                ["counter"] = CounterValue,
                ["name"] = NameValue
            };

            public void Deserialize(JObject data, int sourceVersion)
            {
                CounterValue = data.Value<int>("counter");
                NameValue = data.Value<string>("name") ?? "";
            }

            public void InitializeDefault()
            {
                CounterValue = 0;
                NameValue = "";
            }
        }

        private class DummyModuleB : ISavable
        {
            public string ModuleId => "_editmode_b";
            public int SchemaVersion => 1;

            public double Pi;
            public bool EnabledFlag;
            public int[] ListValues = new int[0];

            public JObject Serialize() => new JObject
            {
                ["pi"] = Pi,
                ["enabled"] = EnabledFlag,
                ["values"] = new JArray(ListValues ?? new int[0])
            };

            public void Deserialize(JObject data, int sourceVersion)
            {
                Pi = data.Value<double>("pi");
                EnabledFlag = data.Value<bool>("enabled");
                ListValues = (data["values"] as JArray)?.ToObject<int[]>() ?? new int[0];
            }

            public void InitializeDefault()
            {
                Pi = 0;
                EnabledFlag = false;
                ListValues = new int[0];
            }
        }
    }
}
