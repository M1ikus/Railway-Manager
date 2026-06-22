using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using RailwayManager.SaveLoad;

namespace RailwayManager.Tests.EditMode
{
    public class SaveRegistryTests
    {
        [SetUp]
        public void SetUp()
        {
            SaveRegistry.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            SaveRegistry.Clear();
        }

        [Test]
        public void Register_ReplacesExistingModuleWithSameId()
        {
            var first = new DummySavable("fleet", 1);
            var replacement = new DummySavable("fleet", 2);

            Assert.That(SaveRegistry.Register(first), Is.True);
            Assert.That(SaveRegistry.Count, Is.EqualTo(1));
            Assert.That(SaveRegistry.Get("fleet"), Is.SameAs(first));

            Assert.That(SaveRegistry.Register(replacement), Is.False);
            Assert.That(SaveRegistry.Count, Is.EqualTo(1));
            Assert.That(SaveRegistry.Get("fleet"), Is.SameAs(replacement));
        }

        [Test]
        public void Register_RejectsNullAndEmptyModuleIds()
        {
            Assert.That(SaveRegistry.Register(null), Is.False);
            Assert.That(SaveRegistry.Register(new DummySavable("", 1)), Is.False);
            Assert.That(SaveRegistry.Count, Is.EqualTo(0));
        }

        [Test]
        public void All_EnumeratesKnownModulesFirstThenUnknownIdsAlphabetically()
        {
            SaveRegistry.Register(new DummySavable("zzz_custom", 1));
            SaveRegistry.Register(new DummySavable("fleet", 1));
            SaveRegistry.Register(new DummySavable("aaa_custom", 1));
            SaveRegistry.Register(new DummySavable("world", 1));
            SaveRegistry.Register(new DummySavable("timetable", 1));

            var ids = SaveRegistry.All.Select(module => module.ModuleId).ToArray();

            Assert.That(ids, Is.EqualTo(new[]
            {
                "world",
                "fleet",
                "timetable",
                "aaa_custom",
                "zzz_custom"
            }));
        }

        [Test]
        public void MissingFromModuleOrder_ExcludesRegisteredKnownModules()
        {
            SaveRegistry.Register(new DummySavable("world", 1));
            SaveRegistry.Register(new DummySavable("fleet", 1));

            var missing = SaveRegistry.GetMissingFromModuleOrder();

            Assert.That(missing, Does.Not.Contain("world"));
            Assert.That(missing, Does.Not.Contain("fleet"));
            Assert.That(missing, Does.Contain("timetable"));
            Assert.That(missing, Does.Contain("economy"));
        }

        [Test]
        public void Unregister_RemovesExistingModuleAndIgnoresUnknownIds()
        {
            SaveRegistry.Register(new DummySavable("fleet", 1));

            Assert.That(SaveRegistry.Unregister("fleet"), Is.True);
            Assert.That(SaveRegistry.Get("fleet"), Is.Null);
            Assert.That(SaveRegistry.Count, Is.EqualTo(0));

            Assert.That(SaveRegistry.Unregister("fleet"), Is.False);
            Assert.That(SaveRegistry.Unregister(""), Is.False);
        }

        private class DummySavable : ISavable
        {
            public DummySavable(string moduleId, int schemaVersion)
            {
                ModuleId = moduleId;
                SchemaVersion = schemaVersion;
            }

            public string ModuleId { get; }
            public int SchemaVersion { get; }

            public JObject Serialize() => new JObject();
            public void Deserialize(JObject data, int sourceVersion) { }
            public void InitializeDefault() { }
        }
    }
}
