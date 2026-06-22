using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using RailwayManager.SaveLoad;

namespace RailwayManager.Tests.EditMode
{
    public class MigrationRunnerTests
    {
        [SetUp]
        public void SetUp()
        {
            MigrationRunner.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            MigrationRunner.Reset();
        }

        [Test]
        public void Migrate_ChainsDiscoveredMigratorsUntilTargetVersion()
        {
            var data = new JObject { ["name"] = "before" };

            var migrated = MigrationRunner.Migrate("_editmode_chain", 1, 3, data);

            Assert.That(migrated.Value<string>("name"), Is.EqualTo("before"));
            Assert.That(migrated.Value<bool>("v2"), Is.True);
            Assert.That(migrated.Value<bool>("v3"), Is.True);
            Assert.That(migrated.Value<int>("version"), Is.EqualTo(3));
        }

        [Test]
        public void Migrate_ReturnsInputWhenVersionsAlreadyMatchOrSourceIsNewer()
        {
            var sameVersion = new JObject { ["value"] = 1 };
            var newerSource = new JObject { ["value"] = 2 };

            Assert.That(MigrationRunner.Migrate("_editmode_chain", 2, 2, sameVersion), Is.SameAs(sameVersion));
            Assert.That(MigrationRunner.Migrate("_editmode_chain", 5, 3, newerSource), Is.SameAs(newerSource));
        }

        [Test]
        public void Migrate_ThrowsMigrationGapWhenChainIsMissing()
        {
            var ex = Assert.Throws<MigrationGapException>(() =>
                MigrationRunner.Migrate("_editmode_chain", 3, 5, new JObject()));

            Assert.That(ex.ModuleId, Is.EqualTo("_editmode_chain"));
            Assert.That(ex.FromVersion, Is.EqualTo(3));
            Assert.That(ex.TargetVersion, Is.EqualTo(5));
        }

        [Test]
        public void Discovery_ReportsMigratorsSortedBySourceVersion()
        {
            Assert.That(MigrationRunner.HasMigratorsFor("_editmode_chain"), Is.True);

            var migrators = MigrationRunner.GetMigratorsFor("_editmode_chain");

            Assert.That(migrators.Count, Is.EqualTo(2));
            Assert.That(migrators[0].SourceVersion, Is.EqualTo(1));
            Assert.That(migrators[1].SourceVersion, Is.EqualTo(2));
        }

        [Test]
        public void Discovery_WarnsOnDuplicateSourceVersion()
        {
            // TD-012: dwa migratory _editmode_dup mają tę samą SourceVersion=1 → discovery ostrzega.
            LogAssert.Expect(LogType.Warning, new Regex("DUPLIKAT migratora.*_editmode_dup"));

            var migrators = MigrationRunner.GetMigratorsFor("_editmode_dup");

            Assert.That(migrators.Count, Is.EqualTo(2), "oba duplikaty wykryte (nie silent-dropped)");
            Assert.That(migrators[0].SourceVersion, Is.EqualTo(1));
            Assert.That(migrators[1].SourceVersion, Is.EqualTo(1));
        }

        public class EditModeChainMigratorV1V2 : IMigrator
        {
            public string ModuleId => "_editmode_chain";
            public int SourceVersion => 1;
            public int TargetVersion => 2;

            public JObject Migrate(JObject input)
            {
                input["v2"] = true;
                input["version"] = 2;
                return input;
            }
        }

        public class EditModeChainMigratorV2V3 : IMigrator
        {
            public string ModuleId => "_editmode_chain";
            public int SourceVersion => 2;
            public int TargetVersion => 3;

            public JObject Migrate(JObject input)
            {
                input["v3"] = true;
                input["version"] = 3;
                return input;
            }
        }

        // TD-012: dwa migratory z TYM SAMYM (ModuleId, SourceVersion) — discovery musi ostrzec.
        public class EditModeDupMigratorA : IMigrator
        {
            public string ModuleId => "_editmode_dup";
            public int SourceVersion => 1;
            public int TargetVersion => 2;
            public JObject Migrate(JObject input) { input["dupA"] = true; return input; }
        }

        public class EditModeDupMigratorB : IMigrator
        {
            public string ModuleId => "_editmode_dup";
            public int SourceVersion => 1;
            public int TargetVersion => 2;
            public JObject Migrate(JObject input) { input["dupB"] = true; return input; }
        }
    }
}
