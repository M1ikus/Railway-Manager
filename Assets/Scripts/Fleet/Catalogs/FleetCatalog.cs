using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using RailwayManager.Core;

namespace RailwayManager.Fleet
{
    /// <summary>
    /// Statyczny katalog danych taboru ładowany z JSON (StreamingAssets/Fleet/).
    /// Zawiera: rodziny pojazdów (M-FC-1), legacy modele,
    /// początkowy rynek wtórny.
    /// </summary>
    public static class FleetCatalog
    {
        /// <summary>M-FC-1: Rodziny pojazdów z wariantami (FLIRT, SA, EU160 Griffin, ...).
        /// Nowy format zastępujący NewModels. NewModels zostaje jako legacy do M-FC-3.</summary>
        public static List<FleetFamily> Families { get; private set; } = new();

        /// <summary>M-FC-2: Definicje pudeł wagonowych dla konfiguratora wagonu.</summary>
        public static List<WagonBodyDef> WagonBodies { get; private set; } = new();

        /// <summary>M-FC-2: Definicje wózków wagonowych dla konfiguratora wagonu.</summary>
        public static List<WagonBogieDef> WagonBogies { get; private set; } = new();

        /// <summary>Legacy: płaska lista SKU. Zachowane do M-FC-3 dla MarketGenerator i innych konsumentów.</summary>
        public static List<NewVehicleModel> NewModels { get; private set; } = new();

        public static List<FleetMarketVehicle> InitialMarket { get; private set; } = new();
        public static bool IsLoaded { get; private set; }

        [Serializable] private class FamiliesWrapper { public List<FleetFamily> families = new(); }
        [Serializable] private class WagonBodiesWrapper { public List<WagonBodyDef> bodies = new(); }
        [Serializable] private class WagonBogiesWrapper { public List<WagonBogieDef> bogies = new(); }
        [Serializable] private class NewModelsWrapper { public List<NewVehicleModel> models = new(); }
        [Serializable] private class MarketWrapper { public List<FleetMarketVehicle> vehicles = new(); }

        public static void LoadAll()
        {
            if (IsLoaded) return;

            string basePath = AppPaths.FleetCatalogDir;

            Families = LoadJson<FamiliesWrapper>(Path.Combine(basePath, "families.json"))?.families
                       ?? new List<FleetFamily>();
            WagonBodies = LoadJson<WagonBodiesWrapper>(Path.Combine(basePath, "wagon_bodies.json"))?.bodies
                          ?? new List<WagonBodyDef>();
            WagonBogies = LoadJson<WagonBogiesWrapper>(Path.Combine(basePath, "wagon_bogies.json"))?.bogies
                          ?? new List<WagonBogieDef>();
            NewModels = LoadJson<NewModelsWrapper>(Path.Combine(basePath, "new_models.json"))?.models
                        ?? new List<NewVehicleModel>();
            InitialMarket = LoadJson<MarketWrapper>(Path.Combine(basePath, "initial_market.json"))?.vehicles
                            ?? new List<FleetMarketVehicle>();

            // Zamien relatywne hinty przegladow na pelny harmonogram (moment gry = 0)
            foreach (var mv in InitialMarket)
            {
                mv.inspections = InspectionSchedule.Reconstruct(
                    nowGameTime: 0,
                    currentMileage: mv.mileageKm,
                    hoursSinceP1: mv.ins_hoursSinceP1,
                    daysSinceP2:  mv.ins_daysSinceP2,
                    kmSinceP3:    mv.ins_kmSinceP3,
                    kmSinceP4:    mv.ins_kmSinceP4,
                    yearsSinceP4: mv.ins_yearsSinceP4,
                    kmSinceP5:    mv.ins_kmSinceP5,
                    yearsSinceP5: mv.ins_yearsSinceP5);

                // M-FC-8: deterministic paintSeed z fields pojazdu (jeśli JSON nie podał)
                if (mv.paintSeed == 0)
                    mv.paintSeed = MarketLiveryGenerator.FallbackSeedForVehicle(mv);
            }

            int variantCount = 0;
            foreach (var f in Families) variantCount += f.variants.Count;

            // M-FC-6: paint editor catalogs
            PaintPresetsCatalog.Load();
            DecalCatalog.Load();

            IsLoaded = true;
            Log.Info($"[FleetCatalog] Loaded: {Families.Count} families ({variantCount} variants), {WagonBodies.Count} wagon bodies, {WagonBogies.Count} wagon bogies, {NewModels.Count} legacy models, {InitialMarket.Count} market vehicles, {PaintPresetsCatalog.Presets.Count} paint presets, {DecalCatalog.Decals.Count} decals");
        }

        /// <summary>M-FC-1: Znajdź rodzinę po familyId. Zwraca null jeśli brak.</summary>
        public static FleetFamily FindFamily(string familyId)
        {
            if (string.IsNullOrEmpty(familyId)) return null;
            foreach (var f in Families)
                if (f.familyId == familyId) return f;
            return null;
        }

        /// <summary>M-FC-1: Znajdź wariant w rodzinie po memberCount + voltageConfigId. Zwraca null jeśli brak.</summary>
        public static FleetVariantSpec FindVariant(string familyId, int memberCount, string voltageConfigId)
        {
            var family = FindFamily(familyId);
            if (family == null) return null;
            foreach (var v in family.variants)
                if (v.memberCount == memberCount && v.voltageConfigId == voltageConfigId) return v;
            return null;
        }

        /// <summary>M-FC-2: Znajdź definicję pudła wagonu po id. Zwraca null jeśli brak.</summary>
        public static WagonBodyDef FindWagonBody(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var b in WagonBodies)
                if (b.id == id) return b;
            return null;
        }

        /// <summary>M-FC-2: Znajdź definicję wózka wagonu po id. Zwraca null jeśli brak.</summary>
        public static WagonBogieDef FindWagonBogie(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var b in WagonBogies)
                if (b.id == id) return b;
            return null;
        }

        private static T LoadJson<T>(string path) where T : class
        {
            try
            {
                if (!File.Exists(path))
                {
                    Log.Warn($"[FleetCatalog] File not found: {path}");
                    return null;
                }
                string json = File.ReadAllText(path);
                return JsonUtility.FromJson<T>(json);
            }
            catch (Exception e)
            {
                Log.Error($"[FleetCatalog] Failed to load {path}: {e.Message}");
                return null;
            }
        }
    }
}
