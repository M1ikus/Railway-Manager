using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using RailwayManager.Core;
using RailwayManager.Fleet;

namespace RailwayManager.SaveLoad.Modules
{
    /// <summary>
    /// BUG-021: Persystencja 5 long-running job services wprowadzonych przez M-Modernization
    /// (60-90 dni job cycles).
    ///
    /// Module ID: "maintenanceJobs". Schema v1.
    ///
    /// Pola w bundle:
    /// - modernization (JArray ModernizationJob) — modernizacje pojazdów MM-10 (External ZNTK
    ///   + Internal Hall lvl5: EN57→Ryba, EU07→160, SM42→6Dg)
    /// - modification (JArray VehicleModificationJob) — modyfikacje MM-11 (External + Internal:
    ///   wymiana wózków, wyposażenie, zmiana funkcji wagonu)
    /// - outdoor (JArray OutdoorJob) — outdoor equipment services MM-9 (WashZone, Turntable,
    ///   PitLift, FuelStation, WaterService, paint_bay self-paint)
    /// - selfPainting (JArray SelfPaintingJob) — paint_bay self-paint MM-12 (mebel + service,
    ///   tańsze niż ZNTK external)
    /// - painting (JArray PaintingJob) — external paint w ZNTK (M-FC-9)
    /// - nextJobId per service (modernizationNextId, modificationNextId, outdoorNextId,
    ///   selfPaintingNextId) — żeby nie kolizjować ID po load. PaintingJob nie ma counter
    ///   (keyed by vehicleId, max 1 active per vehicle).
    ///
    /// Wszystkie services używają wzorca <c>RestoreFromSave(jobs, nextJobId)</c> dla load
    /// i <c>GetNextJobId()</c> dla save.
    ///
    /// Bez tego module: save w środku trwającego 60-90 dni job → load → ServicePit slot
    /// zwolniony ale pojazd dalej OutOfService = ghost vehicle. Zarejestrowane jako BUG-021.
    ///
    /// **TD-031 (manewry serwisowe EnRoute):** gdy save zostanie zrobiony w trakcie dojazdu
    /// consistu do stanowiska (faza EnRoute), <c>DepotMoveTask</c> z delegatem <c>onCompleted</c>
    /// nie jest serializowany — DepotSavable celowo pomija te manewry. Pozycja składu jest jednak
    /// zachowana (occupancy grafu + visual odtworzone przez DepotSavable). Każdy
    /// <c>RestoreFromSave</c> zostawia więc job w fazie EnRoute (status pojazdu „wznawianie po load"),
    /// a watchdog w Maintenance (<c>OutdoorEquipmentMovementBridge.RecoverInterruptedServiceMovements</c>
    /// wywoływany z <c>WorkshopManager.Update</c>) wykrywa orphan (EnRoute bez aktywnego
    /// DepotMoveTask) i re-issue manewr gdy scena zajezdni jest gotowa — analogicznie do tego jak
    /// <c>DeliveryService</c> domyka osierocone dostawy po load. To NIE może być zrobione tu
    /// synchronicznie, bo ModuleOrder deserializuje maintenance/maintenanceJobs PRZED depot_3d
    /// (graf torów jeszcze nie istnieje). Analogiczny recovery slotów przeglądów (P1-P5) żyje
    /// w <c>WorkshopManager</c> (moduł "maintenance").
    /// </summary>
    public class MaintenanceJobsSavable : ISavable
    {
        public string ModuleId => "maintenanceJobs";
        public int SchemaVersion => 1;

        public JObject Serialize()
        {
            return new JObject
            {
                ["modernization"]            = JArray.FromObject(ModernizationJobService.ActiveJobs),
                ["modernizationNextId"]      = ModernizationJobService.GetNextJobId(),
                ["modification"]             = JArray.FromObject(VehicleModificationJobService.ActiveJobs),
                ["modificationNextId"]       = VehicleModificationJobService.GetNextJobId(),
                ["outdoor"]                  = JArray.FromObject(OutdoorEquipmentJobService.ActiveJobs),
                ["outdoorNextId"]            = OutdoorEquipmentJobService.GetNextJobId(),
                ["selfPainting"]             = JArray.FromObject(SelfPaintingService.ActiveJobs),
                ["selfPaintingNextId"]       = SelfPaintingService.GetNextJobId(),
                ["painting"]                 = JArray.FromObject(PaintingJobService.ActiveJobs)
            };
        }

        public void Deserialize(JObject data, int sourceVersion)
        {
            // Modernization
            var modernization = ParseJobArray<ModernizationJob>(data, "modernization");
            int modernizationNextId = data.Value<int?>("modernizationNextId") ?? 1;
            ModernizationJobService.RestoreFromSave(modernization, modernizationNextId);

            // Modification
            var modification = ParseJobArray<VehicleModificationJob>(data, "modification");
            int modificationNextId = data.Value<int?>("modificationNextId") ?? 1;
            VehicleModificationJobService.RestoreFromSave(modification, modificationNextId);

            // Outdoor equipment services
            var outdoor = ParseJobArray<OutdoorJob>(data, "outdoor");
            int outdoorNextId = data.Value<int?>("outdoorNextId") ?? 1;
            OutdoorEquipmentJobService.RestoreFromSave(outdoor, outdoorNextId);

            // Self-painting (paint_bay)
            var selfPainting = ParseJobArray<SelfPaintingJob>(data, "selfPainting");
            int selfPaintingNextId = data.Value<int?>("selfPaintingNextId") ?? 1;
            SelfPaintingService.RestoreFromSave(selfPainting, selfPaintingNextId);

            // External painting (ZNTK)
            var painting = ParseJobArray<PaintingJob>(data, "painting");
            PaintingJobService.RestoreFromSave(painting);

            Log.Info($"[MaintenanceJobsSavable] Restored: {modernization.Count} modernization, " +
                     $"{modification.Count} modification, {outdoor.Count} outdoor, " +
                     $"{selfPainting.Count} self-paint, {painting.Count} paint");
        }

        public void InitializeDefault()
        {
            // #1B: izolacja resetow (jak FleetSavable/PersonnelSavable) — wyjatek jednego
            // resetu nie moze pominac pozostalych (inaczej stare long-running jobs
            // przeciekaja do nowej gry). Per-singleton odpowiednik per-module isolation.
            SafeReset("ModernizationJobService.ResetAll", ModernizationJobService.ResetAll);
            SafeReset("VehicleModificationJobService.ResetAll", VehicleModificationJobService.ResetAll);
            SafeReset("OutdoorEquipmentJobService.ResetAll", OutdoorEquipmentJobService.ResetAll);
            SafeReset("SelfPaintingService.ResetAll", SelfPaintingService.ResetAll);
            SafeReset("PaintingJobService.ResetAll", PaintingJobService.ResetAll);
        }

        /// <summary>
        /// #1B: pojedynczy reset singletona z izolacja wyjatku, zeby nie przerwac
        /// resetu pozostalych w <see cref="InitializeDefault"/>.
        /// </summary>
        private static void SafeReset(string what, System.Action reset)
        {
            try { reset(); }
            catch (System.Exception e)
            {
                Log.Error($"[MaintenanceJobsSavable] Reset '{what}' threw: {e.GetType().Name}: {e.Message}");
            }
        }

        private static List<T> ParseJobArray<T>(JObject data, string key) where T : class
        {
            var result = new List<T>();
            if (data[key] is JArray arr)
            {
                foreach (var item in arr)
                {
                    var entry = item.ToObject<T>();
                    if (entry != null) result.Add(entry);
                }
            }
            return result;
        }
    }

    public static class MaintenanceJobsSavableBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            SaveRegistry.Register(new MaintenanceJobsSavable());
        }
    }
}
