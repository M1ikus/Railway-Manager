using NUnit.Framework;
using DepotSystem;                       // TrackBuildSubMode
using DepotSystem.OutdoorEquipment;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// M-Furniture/M-Modernization: testy OutdoorEquipmentDefinitions — katalog infrastruktury
    /// outdoor (myjnie/obrotnice/podnośniki/stacje paliw/wodowanie). Czysty katalog, EditMode.
    /// Pokrywa: presety rozmiarów, mapping sub-mode→typ, max długość pojazdu per urządzenie.
    /// </summary>
    public class OutdoorEquipmentTests
    {
        [Test]
        public void Presets_DefinedForAllTypes()
        {
            foreach (OutdoorEquipmentType t in System.Enum.GetValues(typeof(OutdoorEquipmentType)))
                Assert.That(OutdoorEquipmentDefinitions.Presets.ContainsKey(t), Is.True,
                    $"Brak presetu dla {t}.");
        }

        [Test]
        public void Presets_HavePositiveMinDimensions()
        {
            foreach (var kv in OutdoorEquipmentDefinitions.Presets)
            {
                Assert.That(kv.Value.minWidth, Is.GreaterThan(0f), $"{kv.Key} minWidth > 0.");
                Assert.That(kv.Value.minDepth, Is.GreaterThan(0f), $"{kv.Key} minDepth > 0.");
                Assert.That(kv.Value.label, Is.Not.Null.And.Not.Empty, $"{kv.Key} ma label.");
            }
        }

        [Test]
        public void WashZone_HasExpectedPreset()
        {
            var p = OutdoorEquipmentDefinitions.Presets[OutdoorEquipmentType.WashZone];
            Assert.That(p.minWidth, Is.EqualTo(8f), "Myjnia min szerokość 8m.");
            Assert.That(p.minDepth, Is.EqualTo(6f), "Myjnia min głębokość 6m.");
            Assert.That(p.label, Is.EqualTo("Myjnia"));
        }

        [Test]
        public void Turntable_LargerThanPitLift()
        {
            var tt = OutdoorEquipmentDefinitions.Presets[OutdoorEquipmentType.Turntable];
            var pl = OutdoorEquipmentDefinitions.Presets[OutdoorEquipmentType.PitLift];
            Assert.That(tt.minWidth, Is.GreaterThan(pl.minWidth), "Obrotnica szersza niż podnośnik.");
            Assert.That(tt.minWidth, Is.EqualTo(tt.minDepth), "Obrotnica kwadratowa (12×12).");
        }

        // ── FromSubMode mapping ──────────────────────────────────────

        [Test]
        public void FromSubMode_MapsOutdoorSubModes()
        {
            Assert.That(OutdoorEquipmentDefinitions.FromSubMode(TrackBuildSubMode.WashZone),
                Is.EqualTo(OutdoorEquipmentType.WashZone));
            Assert.That(OutdoorEquipmentDefinitions.FromSubMode(TrackBuildSubMode.Turntable),
                Is.EqualTo(OutdoorEquipmentType.Turntable));
            Assert.That(OutdoorEquipmentDefinitions.FromSubMode(TrackBuildSubMode.PitLift),
                Is.EqualTo(OutdoorEquipmentType.PitLift));
            Assert.That(OutdoorEquipmentDefinitions.FromSubMode(TrackBuildSubMode.FuelStation),
                Is.EqualTo(OutdoorEquipmentType.FuelStation));
        }

        [Test]
        public void FromSubMode_NonOutdoor_ReturnsNull()
        {
            // Sub-mode'y torowe/rozjazdowe NIE są outdoor equipment.
            Assert.That(OutdoorEquipmentDefinitions.FromSubMode(TrackBuildSubMode.Track), Is.Null);
            Assert.That(OutdoorEquipmentDefinitions.FromSubMode(TrackBuildSubMode.TurnoutR190), Is.Null);
            Assert.That(OutdoorEquipmentDefinitions.FromSubMode(TrackBuildSubMode.Schemas), Is.Null);
        }

        // ── GetMaxVehicleLength ──────────────────────────────────────

        [Test]
        public void MaxVehicleLength_WashZoneFitsFullTrain()
        {
            // Myjnia obsługuje pełen skład EZT (najdłuższy), podnośnik tylko krótki pojazd.
            float wash = OutdoorEquipmentDefinitions.GetMaxVehicleLength(OutdoorEquipmentType.WashZone);
            float turntable = OutdoorEquipmentDefinitions.GetMaxVehicleLength(OutdoorEquipmentType.Turntable);
            float pitlift = OutdoorEquipmentDefinitions.GetMaxVehicleLength(OutdoorEquipmentType.PitLift);

            Assert.That(wash, Is.GreaterThan(turntable), "Myjnia obsługuje dłuższe pojazdy niż obrotnica.");
            Assert.That(turntable, Is.GreaterThan(pitlift), "Obrotnica (pojedynczy pojazd) > podnośnik (krótki).");
            Assert.That(wash, Is.GreaterThanOrEqualTo(64f), "Myjnia mieści pełen skład EZT (≥64m).");
        }

        [Test]
        public void MaxVehicleLength_FuelStation_HandlesLongVehicles()
        {
            Assert.That(OutdoorEquipmentDefinitions.GetMaxVehicleLength(OutdoorEquipmentType.FuelStation),
                Is.EqualTo(50f), "Stacja paliw tankuje długie pojazdy (50m).");
        }

        [Test]
        public void MaxVehicleLength_AllTypesPositive()
        {
            foreach (OutdoorEquipmentType t in System.Enum.GetValues(typeof(OutdoorEquipmentType)))
                Assert.That(OutdoorEquipmentDefinitions.GetMaxVehicleLength(t), Is.GreaterThan(0f),
                    $"{t} obsługuje pojazd o dodatniej długości.");
        }
    }
}
