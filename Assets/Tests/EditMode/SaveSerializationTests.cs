using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using RailwayManager.Core;
using RailwayManager.SaveLoad;

namespace RailwayManager.Tests.EditMode
{
    public class SaveSerializationTests
    {
        private const string TestFolder = "Saves_EditModeStorageTests";

        [SetUp]
        public void SetUp()
        {
            DeleteTestFolder();
        }

        [TearDown]
        public void TearDown()
        {
            DeleteTestFolder();
        }

        [Test]
        public void BundleSerializer_RoundTripsManifestModulesAndHmac()
        {
            var bundle = CreateSignedBundle("Roundtrip slot", "2026-05-30T09:10:00Z");

            byte[] bytes = BundleSerializer.Serialize(bundle);
            var loaded = BundleSerializer.Deserialize(bytes);
            var manifestOnly = BundleSerializer.DeserializeManifestOnly(bytes);

            Assert.That(loaded.Manifest.SlotName, Is.EqualTo("Roundtrip slot"));
            Assert.That(loaded.Manifest.SaveType, Is.EqualTo(SaveTypes.Test));
            Assert.That(loaded.Manifest.ModuleVersions["alpha"], Is.EqualTo(3));
            Assert.That(loaded.Manifest.ModuleVersions["beta"], Is.EqualTo(1));
            Assert.That(loaded.Modules["alpha"].Value<int>("count"), Is.EqualTo(7));
            Assert.That(loaded.Modules["beta"].Value<bool>("enabled"), Is.True);
            Assert.That(HmacService.VerifyDetailed(loaded), Is.EqualTo(HmacVerifyResult.Match));

            Assert.That(manifestOnly.SlotName, Is.EqualTo("Roundtrip slot"));
            Assert.That(manifestOnly.ModuleVersions.Keys, Is.EquivalentTo(new[] { "alpha", "beta" }));
        }

        [Test]
        public void Hmac_DetectsModuleAndManifestTampering()
        {
            var moduleTampered = CreateSignedBundle("Tamper module", "2026-05-30T09:11:00Z");
            moduleTampered.Modules["alpha"]["count"] = 999;

            var manifestTampered = CreateSignedBundle("Tamper manifest", "2026-05-30T09:12:00Z");
            manifestTampered.Manifest.SlotName = "Changed after signing";

            Assert.That(HmacService.VerifyDetailed(moduleTampered), Is.EqualTo(HmacVerifyResult.Mismatch));
            Assert.That(HmacService.VerifyDetailed(manifestTampered), Is.EqualTo(HmacVerifyResult.Mismatch));
        }

        [Test]
        public void Hmac_IsDeterministicForDifferentModuleInsertionOrder()
        {
            var first = CreateUnsignedBundle("Order A", "2026-05-30T09:13:00Z");
            first.AddModule("alpha", 3, new JObject { ["count"] = 7 });
            first.AddModule("beta", 1, new JObject { ["enabled"] = true });

            var second = CreateUnsignedBundle("Order A", "2026-05-30T09:13:00Z");
            second.AddModule("beta", 1, new JObject { ["enabled"] = true });
            second.AddModule("alpha", 3, new JObject { ["count"] = 7 });

            Assert.That(HmacService.ComputeHmac(first), Is.EqualTo(HmacService.ComputeHmac(second)));
        }

        [Test]
        public void LocalDiskStorage_ValidatesSlotIds()
        {
            Assert.That(LocalDiskStorage.IsValidSlotId("save_001"), Is.True);
            Assert.That(LocalDiskStorage.IsValidSlotId("Auto-Save_01"), Is.True);

            Assert.That(LocalDiskStorage.IsValidSlotId(null), Is.False);
            Assert.That(LocalDiskStorage.IsValidSlotId(""), Is.False);
            Assert.That(LocalDiskStorage.IsValidSlotId("../escape"), Is.False);
            Assert.That(LocalDiskStorage.IsValidSlotId("has space"), Is.False);
            Assert.That(LocalDiskStorage.IsValidSlotId("CON"), Is.False);
            Assert.That(LocalDiskStorage.IsValidSlotId("lpt1"), Is.False);
            Assert.That(LocalDiskStorage.IsValidSlotId(new string('a', 65)), Is.False);
        }

        [Test]
        public async Task LocalDiskStorage_SaveListLoadDeleteRoundTrip()
        {
            var storage = new LocalDiskStorage(TestFolder);
            var older = CreateSignedBundle("Older", "2026-05-30T09:14:00Z");
            var newer = CreateSignedBundle("Newer", "2026-05-30T09:15:00Z");

            Assert.That(await storage.SaveAsync("older_slot", older), Is.True);
            Assert.That(await storage.SaveAsync("newer_slot", newer), Is.True);
            Assert.That(await storage.ExistsAsync("older_slot"), Is.True);

            var slots = await storage.ListAsync();

            Assert.That(slots.Select(slot => slot.SlotId).ToArray(), Is.EqualTo(new[] { "newer_slot", "older_slot" }));
            Assert.That(slots[0].SlotName, Is.EqualTo("Newer"));
            Assert.That(slots[0].SaveType, Is.EqualTo(SaveTypes.Test));
            Assert.That(slots[0].FileSizeBytes, Is.GreaterThan(0));

            var loaded = await storage.LoadAsync("newer_slot");
            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded.Manifest.SlotName, Is.EqualTo("Newer"));
            Assert.That(loaded.Modules["alpha"].Value<int>("count"), Is.EqualTo(7));

            Assert.That(await storage.DeleteAsync("older_slot"), Is.True);
            Assert.That(await storage.ExistsAsync("older_slot"), Is.False);
            Assert.That(await storage.DeleteAsync("missing_slot"), Is.True);
        }

        private static SaveBundle CreateSignedBundle(string slotName, string savedAt)
        {
            var bundle = CreateUnsignedBundle(slotName, savedAt);
            bundle.AddModule("alpha", 3, new JObject { ["count"] = 7 });
            bundle.AddModule("beta", 1, new JObject { ["enabled"] = true });
            bundle.Manifest.Hmac = HmacService.ComputeHmac(bundle);
            return bundle;
        }

        private static SaveBundle CreateUnsignedBundle(string slotName, string savedAt) => new SaveBundle
        {
            Manifest = new SaveManifest
            {
                GameVersion = "editmode-test",
                BundleSchemaVersion = 1,
                Playtime = 123.5,
                GameTimeIso = "2026-05-30T09:00:00",
                SavedAt = savedAt,
                SaveType = SaveTypes.Test,
                SlotName = slotName
            }
        };

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
                // Best-effort cleanup: storage assertions will expose locked files.
            }
        }
    }
}
