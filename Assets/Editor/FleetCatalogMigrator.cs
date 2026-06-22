using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RailwayManager.Fleet;
using UnityEditor;
using UnityEngine;

namespace RailwayManager.EditorTools
{
    /// <summary>
    /// M-FC-1: Migracja `new_models.json` (płaski SKU per pojazd) → `families.json`
    /// (rodzina + matrix wariantów). Konsoliduje:
    /// <list type="bullet">
    /// <item>FLIRT 4 SKU (L4268/LM4268/ER160/ED160) → 1 rodzina FLIRT z 4 wariantami</item>
    /// <item>SA 2 SKU (SA137/SA138) → 1 rodzina SA_new z 2 wariantami</item>
    /// <item>EU160 Griffin → 1 rodzina z 1 wariantem (multi-system pre-defined)</item>
    /// </list>
    /// Coach_264_Universal pomija — wagon zastąpiony konfiguratorem (M-FC-2).
    ///
    /// <para>Uruchom: <c>Tools > Fleet > Migrate new_models.json -> families.json</c>.
    /// Wygenerowany plik trzeba zatwierdzić ręcznie (review mapowania voltageConfigId,
    /// description rodziny, default values) i nie nadpisywać go bez potrzeby.</para>
    /// </summary>
    public static class FleetCatalogMigrator
    {
        [Serializable] private class NewModelsWrapper { public List<NewVehicleModel> models = new(); }
        [Serializable] private class FamiliesWrapper { public List<FleetFamily> families = new(); }

        [MenuItem("Tools/Fleet/Migrate new_models.json -> families.json")]
        public static void Migrate()
        {
            string basePath = Path.Combine(Application.streamingAssetsPath, "Fleet");
            string srcPath = Path.Combine(basePath, "new_models.json");
            string dstPath = Path.Combine(basePath, "families.json");

            if (!File.Exists(srcPath))
            {
                Debug.LogError($"[FleetCatalogMigrator] Source not found: {srcPath}");
                return;
            }

            string json = File.ReadAllText(srcPath);
            var wrapper = JsonUtility.FromJson<NewModelsWrapper>(json);
            if (wrapper == null || wrapper.models == null || wrapper.models.Count == 0)
            {
                Debug.LogError($"[FleetCatalogMigrator] Source empty or invalid: {srcPath}");
                return;
            }

            // Group by `family` field
            var grouped = new Dictionary<string, List<NewVehicleModel>>();
            foreach (var m in wrapper.models)
            {
                if (string.IsNullOrEmpty(m.family))
                {
                    Debug.LogWarning($"[FleetCatalogMigrator] Skip {m.seriesId}: no family field");
                    continue;
                }

                // M-FC-1: pomijamy Coach_264_Universal (zastąpione konfiguratorem wagonu w M-FC-2)
                if (m.seriesId == "Coach_264_Universal" || m.family == "Coach_264_Modern")
                {
                    Debug.Log($"[FleetCatalogMigrator] Skip {m.seriesId} (replaced by wagon configurator in M-FC-2)");
                    continue;
                }

                if (!grouped.ContainsKey(m.family)) grouped[m.family] = new List<NewVehicleModel>();
                grouped[m.family].Add(m);
            }

            var families = new FamiliesWrapper();
            foreach (var kvp in grouped)
            {
                families.families.Add(BuildFamily(kvp.Key, kvp.Value));
            }

            string outJson = JsonUtility.ToJson(families, prettyPrint: true);
            File.WriteAllText(dstPath, outJson);

            int variantCount = 0;
            foreach (var f in families.families) variantCount += f.variants.Count;

            Debug.Log($"[FleetCatalogMigrator] OK — {families.families.Count} families ({variantCount} variants) → {dstPath}");
            AssetDatabase.Refresh();
        }

        private static FleetFamily BuildFamily(string familyId, List<NewVehicleModel> sourceModels)
        {
            // Use first SKU for family-level metadata
            var first = sourceModels[0];

            var family = new FleetFamily
            {
                familyId = NormalizeFamilyId(familyId),
                displayName = ExtractFamilyDisplayName(first),
                manufacturer = first.manufacturer,
                country = string.IsNullOrEmpty(first.country) ? "PL" : first.country,
                type = first.type,
                inProductionFromYear = first.inProductionFromYear,
                inProductionToYear = first.inProductionToYear,
                introducedToPolandYear = first.introducedToPolandYear,
                status = first.status,
                description = ExtractFamilyDescription(first),
                historicalFactoid = first.historicalFactoid,
                factoryLocation = first.factoryLocation,
                variants = new List<FleetVariantSpec>()
            };

            foreach (var m in sourceModels)
            {
                family.variants.Add(BuildVariant(m));
            }

            return family;
        }

        private static FleetVariantSpec BuildVariant(NewVehicleModel m)
        {
            string voltageId = MapVoltageConfigId(m.voltages, m.supportedTractions);
            int memberCount = m.coachCount > 0 ? m.coachCount : 1;

            return new FleetVariantSpec
            {
                memberCount = memberCount,
                voltageConfigId = voltageId,
                variantLabel = BuildVariantLabel(memberCount, voltageId, m.type),

                maxSpeedKmh = m.maxSpeedKmh,
                powerKw = m.powerKw,
                wheelbase = m.wheelbase,
                lengthM = m.lengthM,
                emptyMassTons = m.emptyMassTons,
                maxLoadedMassTons = m.maxLoadedMassTons,
                brakingMassTons = m.brakingMassTons,
                brakeRegime = m.brakeRegime,
                passengerSeatsBase = m.passengerSeats,
                seatBreakdownBase = m.seatBreakdown ?? new List<SeatCount>(),
                accelerationMps2 = m.accelerationMps2,
                decelerationMps2 = m.decelerationMps2,
                comfortClassBase = m.comfortClass,

                supportedTractions = m.supportedTractions ?? new List<TractionType>(),
                voltages = m.voltages ?? new List<string>(),

                defaultSafetySystems = m.safetySystemsInstalled ?? new List<string>(),
                defaultComfortFeatures = m.comfortFeatures ?? new List<string>(),

                basePrice = m.basePrice,
                operationalCostPerKmGroszy = m.operationalCostPerKmGroszy,

                reliabilityScore = m.reliabilityScore,
                breakdownRiskFactor = m.breakdownRiskFactor,
                maintenanceCostFactor = m.maintenanceCostFactor,
                componentRisk = m.componentRisk ?? new ComponentRiskFactors(),
                inspectionIntervalKmP1 = m.inspectionIntervalKmP1,
                inspectionIntervalKmP2 = m.inspectionIntervalKmP2,
                inspectionIntervalKmP3 = m.inspectionIntervalKmP3,
                inspectionIntervalYearsP4 = m.inspectionIntervalYearsP4,
                inspectionIntervalYearsP5 = m.inspectionIntervalYearsP5,

                defaultPurpose = InferDefaultPurpose(memberCount, m.suggestedCategoryGroups),
                suggestedCategoryGroups = m.suggestedCategoryGroups ?? new List<string>(),

                minPlatformLengthM = m.minPlatformLengthM,
                requiresMaintenanceCapabilities = m.requiresMaintenanceCapabilities ?? new List<string>(),
                canBePulledByDiesel = m.canBePulledByDiesel,
                isShuntingLocomotive = m.isShuntingLocomotive,

                variantDescription = m.description,
                variantFactoid = "" // family-level factoid wystarczy
            };
        }

        // ── Helpery ──────────────────────────────────────────

        /// <summary>
        /// Mapuje listę voltage strings na stabilny id wariantu.
        /// Diesel (pusta lista) → "diesel". Single 3kV → "3kV". Multi-system → "3kV+15kV+25kV" itp.
        /// </summary>
        private static string MapVoltageConfigId(List<string> voltages, List<TractionType> tractions)
        {
            // Spalinowe: zawsze "diesel"
            if (tractions != null && tractions.Contains(TractionType.Diesel)) return "diesel";

            if (voltages == null || voltages.Count == 0) return "passive"; // wagon

            // Skrót: "3kV DC" → "3kV", "15kV AC 16.7Hz" → "15kV", "25kV AC 50Hz" → "25kV"
            var parts = new List<string>();
            foreach (var v in voltages)
            {
                if (v.StartsWith("3kV")) parts.Add("3kV");
                else if (v.StartsWith("15kV")) parts.Add("15kV");
                else if (v.StartsWith("25kV")) parts.Add("25kV");
                else if (v.StartsWith("1.5kV")) parts.Add("1.5kV");
                else parts.Add(v); // unknown — passthrough
            }
            return string.Join("+", parts);
        }

        private static string BuildVariantLabel(int memberCount, string voltageId, FleetVehicleType type)
        {
            string voltagePart = voltageId switch
            {
                "diesel" => "spalinowy",
                "passive" => "wagon",
                _ => voltageId
            };

            if (type == FleetVehicleType.PassengerCar) return voltagePart;
            if (type == FleetVehicleType.ElectricLocomotive || type == FleetVehicleType.DieselLocomotive)
                return voltagePart;

            // EMU/DMU
            return $"{memberCount} człon{(memberCount == 1 ? "" : memberCount < 5 ? "y" : "ów")}, {voltagePart}";
        }

        /// <summary>Heurystyka: 2-3 człony → aglomeracja/regional, 5+ członów → dalekobieżny.</summary>
        private static string InferDefaultPurpose(int memberCount, List<string> categoryGroups)
        {
            if (categoryGroups != null)
            {
                foreach (var g in categoryGroups)
                {
                    if (g.Contains("Express") || g.Contains("InterregionalFast")) return "longDistance";
                    if (g.Contains("Agglomeration")) return "agglomeration";
                }
            }
            if (memberCount <= 2) return "agglomeration";
            if (memberCount <= 3) return "regional";
            return "longDistance";
        }

        private static string NormalizeFamilyId(string raw)
        {
            // "FLIRT" → "FLIRT", "SA_new_family" → "SA_new", "EU160_family" → "EU160_Griffin"
            if (raw == "SA_new_family") return "SA_new";
            if (raw == "EU160_family") return "EU160_Griffin";
            return raw;
        }

        private static string ExtractFamilyDisplayName(NewVehicleModel m)
        {
            // Przykład: "Stadler FLIRT L-4268 (2 człony)" → "Stadler FLIRT"
            //          "Newag EU160 Griffin"             → "Newag EU160 Griffin"
            //          "Pesa SA137 (2 człony)"           → "Pesa SA13x"
            string name = m.modelName;

            // Usuń wszystko od pierwszego nawiasu
            int parenIdx = name.IndexOf('(');
            if (parenIdx > 0) name = name.Substring(0, parenIdx).Trim();

            // Usuń konkretne oznaczenie wariantu (L-4268, ER160, ED160, LM-4268)
            // Dla FLIRT: "Stadler FLIRT L-4268" → "Stadler FLIRT"
            int dashIdx = name.IndexOf(" L-");
            if (dashIdx < 0) dashIdx = name.IndexOf(" ER");
            if (dashIdx < 0) dashIdx = name.IndexOf(" ED");
            if (dashIdx < 0) dashIdx = name.IndexOf(" LM-");
            if (dashIdx > 0) name = name.Substring(0, dashIdx).Trim();

            // SA137/SA138 → SA13x
            if (name.EndsWith("SA137") || name.EndsWith("SA138"))
                name = name.Substring(0, name.Length - 1) + "x";

            return name;
        }

        private static string ExtractFamilyDescription(NewVehicleModel m)
        {
            // Generic family-level description z pierwszego SKU. Można edytować ręcznie po migracji.
            if (string.IsNullOrEmpty(m.description)) return "";
            return m.description;
        }

        // ── Smoke test ───────────────────────────────────────

        [MenuItem("Tools/Fleet/Smoke Test families.json")]
        public static void SmokeTest()
        {
            string basePath = Path.Combine(Application.streamingAssetsPath, "Fleet");
            string path = Path.Combine(basePath, "families.json");

            if (!File.Exists(path))
            {
                Debug.LogError($"[FleetCatalog SmokeTest] FAIL: families.json missing at {path}");
                return;
            }

            string json = File.ReadAllText(path);
            var wrapper = JsonUtility.FromJson<FamiliesWrapper>(json);
            if (wrapper == null || wrapper.families == null)
            {
                Debug.LogError($"[FleetCatalog SmokeTest] FAIL: cannot parse families.json");
                return;
            }

            int issues = 0;

            // Check 1: oczekiwana liczba rodzin (3) i wariantów (7)
            if (wrapper.families.Count != 3)
            {
                Debug.LogError($"[FleetCatalog SmokeTest] FAIL: expected 3 families, got {wrapper.families.Count}");
                issues++;
            }

            int totalVariants = 0;
            foreach (var f in wrapper.families) totalVariants += f.variants.Count;
            if (totalVariants != 10)
            {
                Debug.LogError($"[FleetCatalog SmokeTest] FAIL: expected 10 variants total (4 FLIRT + 2 SA + 4 EU160), got {totalVariants}");
                issues++;
            }

            // Check 2: FLIRT 3-człon × 3kV ma basePrice = 35M (match z LM-4268)
            var flirt = wrapper.families.Find(f => f.familyId == "FLIRT");
            if (flirt == null) { Debug.LogError("[FleetCatalog SmokeTest] FAIL: FLIRT family missing"); issues++; }
            else
            {
                var v3 = flirt.variants.Find(v => v.memberCount == 3 && v.voltageConfigId == "3kV");
                if (v3 == null) { Debug.LogError("[FleetCatalog SmokeTest] FAIL: FLIRT 3-człon × 3kV variant missing"); issues++; }
                else if (v3.basePrice != 35000000)
                {
                    Debug.LogError($"[FleetCatalog SmokeTest] FAIL: FLIRT 3-człon basePrice expected 35M, got {v3.basePrice}");
                    issues++;
                }
                else if (v3.lengthM != 62.5f)
                {
                    Debug.LogError($"[FleetCatalog SmokeTest] FAIL: FLIRT 3-człon lengthM expected 62.5, got {v3.lengthM}");
                    issues++;
                }
            }

            // Check 3: SA_new 2-człon × diesel basePrice = 12M (match z SA137)
            var sa = wrapper.families.Find(f => f.familyId == "SA_new");
            if (sa == null) { Debug.LogError("[FleetCatalog SmokeTest] FAIL: SA_new family missing"); issues++; }
            else
            {
                var v2 = sa.variants.Find(v => v.memberCount == 2 && v.voltageConfigId == "diesel");
                if (v2 == null) { Debug.LogError("[FleetCatalog SmokeTest] FAIL: SA_new 2-człon × diesel variant missing"); issues++; }
                else if (v2.basePrice != 12000000)
                {
                    Debug.LogError($"[FleetCatalog SmokeTest] FAIL: SA_new 2-człon basePrice expected 12M, got {v2.basePrice}");
                    issues++;
                }
            }

            // Check 4 (M-FC-4): EU160 Griffin — 4 voltage variants (1 / 2 / 3 / 4 systems)
            var eu160 = wrapper.families.Find(f => f.familyId == "EU160_Griffin");
            if (eu160 == null) { Debug.LogError("[FleetCatalog SmokeTest] FAIL: EU160_Griffin family missing"); issues++; }
            else if (eu160.variants.Count != 4) { Debug.LogError($"[FleetCatalog SmokeTest] FAIL: EU160 expected 4 variants (1/2/3/4 systems), got {eu160.variants.Count}"); issues++; }
            else
            {
                // Single-system: NO ETCS L2 default (krajowy)
                var v1 = eu160.variants.Find(v => v.voltageConfigId == "3kV");
                if (v1 == null) { Debug.LogError("[FleetCatalog SmokeTest] FAIL: EU160 missing single-system 3kV variant"); issues++; }
                else if (v1.defaultSafetySystems.Contains("ETCS L2"))
                {
                    Debug.LogError("[FleetCatalog SmokeTest] FAIL: EU160 single-system 3kV shouldn't have ETCS L2 default (krajowy)");
                    issues++;
                }

                // Multi-system (2/3/4): ETCS L2 in defaults (dla TEN-T)
                foreach (var voltId in new[] { "3kV+25kV", "3kV+15kV+25kV", "3kV+15kV+25kV+1.5kV" })
                {
                    var v = eu160.variants.Find(x => x.voltageConfigId == voltId);
                    if (v == null) { Debug.LogError($"[FleetCatalog SmokeTest] FAIL: EU160 missing variant {voltId}"); issues++; continue; }
                    if (!v.defaultSafetySystems.Contains("ETCS L2"))
                    {
                        Debug.LogError($"[FleetCatalog SmokeTest] FAIL: EU160 multi-system {voltId} should have ETCS L2 default");
                        issues++;
                    }
                }

                // Prices ascending z liczbą systemów
                var sortedByPrice = eu160.variants.OrderBy(v => v.basePrice).Select(v => v.voltageConfigId).ToList();
                var expectedOrder = new[] { "3kV", "3kV+25kV", "3kV+15kV+25kV", "3kV+15kV+25kV+1.5kV" };
                for (int i = 0; i < expectedOrder.Length; i++)
                {
                    if (sortedByPrice[i] != expectedOrder[i])
                    {
                        Debug.LogError($"[FleetCatalog SmokeTest] FAIL: EU160 price ordering: expected {expectedOrder[i]} at idx {i}, got {sortedByPrice[i]}");
                        issues++;
                        break;
                    }
                }
            }

            // Check 5: Coach_264_Universal NIE ma być w families (zastąpione konfiguratorem M-FC-2)
            var coach = wrapper.families.Find(f => f.familyId == "Coach" || f.familyId == "Coach_264_Modern");
            if (coach != null)
            {
                Debug.LogError("[FleetCatalog SmokeTest] FAIL: Coach family exists — should be removed (replaced by wagon configurator in M-FC-2)");
                issues++;
            }

            // Check 6: FleetCatalog.LoadAll() działa i ładuje rodziny
            FleetCatalog.LoadAll();
            if (FleetCatalog.Families.Count == 0)
            {
                Debug.LogError("[FleetCatalog SmokeTest] FAIL: FleetCatalog.LoadAll() loaded 0 families");
                issues++;
            }

            // Check 7: FindFamily / FindVariant API działa
            var found = FleetCatalog.FindFamily("FLIRT");
            if (found == null) { Debug.LogError("[FleetCatalog SmokeTest] FAIL: FindFamily('FLIRT') returned null"); issues++; }

            var foundVariant = FleetCatalog.FindVariant("FLIRT", 3, "3kV");
            if (foundVariant == null) { Debug.LogError("[FleetCatalog SmokeTest] FAIL: FindVariant('FLIRT', 3, '3kV') returned null"); issues++; }

            // Check 8 (M-FC-2): WagonBodies + WagonBogies załadowane
            if (FleetCatalog.WagonBodies.Count != 2)
            {
                Debug.LogError($"[FleetCatalog SmokeTest] FAIL: expected 2 wagon bodies, got {FleetCatalog.WagonBodies.Count}");
                issues++;
            }
            if (FleetCatalog.WagonBogies.Count != 3)
            {
                Debug.LogError($"[FleetCatalog SmokeTest] FAIL: expected 3 wagon bogies, got {FleetCatalog.WagonBogies.Count}");
                issues++;
            }

            // Check 9 (M-FC-2): FindWagonBody / FindWagonBogie API
            var body245 = FleetCatalog.FindWagonBody("UIC-X-24.5m");
            if (body245 == null) { Debug.LogError("[FleetCatalog SmokeTest] FAIL: FindWagonBody('UIC-X-24.5m') returned null"); issues++; }
            else if (body245.lengthM != 24.5f)
            {
                Debug.LogError($"[FleetCatalog SmokeTest] FAIL: UIC-X-24.5m lengthM expected 24.5, got {body245.lengthM}");
                issues++;
            }

            var bogieDisc = FleetCatalog.FindWagonBogie("tarczowy-szynowy");
            if (bogieDisc == null) { Debug.LogError("[FleetCatalog SmokeTest] FAIL: FindWagonBogie('tarczowy-szynowy') returned null"); issues++; }
            else if (bogieDisc.maxSpeedKmh != 200)
            {
                Debug.LogError($"[FleetCatalog SmokeTest] FAIL: tarczowy-szynowy maxSpeedKmh expected 200, got {bogieDisc.maxSpeedKmh}");
                issues++;
            }

            // Check 10 (M-FC-2): Coach_264_Universal NIE w new_models.json (przeniesione do konfiguratora)
            var coachLegacy = FleetCatalog.NewModels.Find(m => m.seriesId == "Coach_264_Universal");
            if (coachLegacy != null)
            {
                Debug.LogError("[FleetCatalog SmokeTest] FAIL: Coach_264_Universal still in new_models.json (should be removed in M-FC-2)");
                issues++;
            }

            // Check 11 (M-FC-6): Paint presets załadowane (>= 5)
            if (PaintPresetsCatalog.Presets.Count < 5)
            {
                Debug.LogError($"[FleetCatalog SmokeTest] FAIL: expected >= 5 paint presets, got {PaintPresetsCatalog.Presets.Count}");
                issues++;
            }
            var bottomStripe = PaintPresetsCatalog.Find("bottomStripe");
            if (bottomStripe == null) { Debug.LogError("[FleetCatalog SmokeTest] FAIL: PaintPresetsCatalog.Find('bottomStripe') returned null"); issues++; }
            else if (bottomStripe.thickness <= 0) { Debug.LogError("[FleetCatalog SmokeTest] FAIL: bottomStripe thickness <= 0"); issues++; }

            // Check 12 (M-FC-6): Decals catalog (>= 25)
            if (DecalCatalog.Decals.Count < 25)
            {
                Debug.LogError($"[FleetCatalog SmokeTest] FAIL: expected >= 25 decals, got {DecalCatalog.Decals.Count}");
                issues++;
            }
            var wheelchair = DecalCatalog.Find("icon-wheelchair");
            if (wheelchair == null) { Debug.LogError("[FleetCatalog SmokeTest] FAIL: DecalCatalog.Find('icon-wheelchair') returned null"); issues++; }
            int digitCount = DecalCatalog.ByCategory("digit").Count;
            if (digitCount != 10) { Debug.LogError($"[FleetCatalog SmokeTest] FAIL: expected 10 digit decals, got {digitCount}"); issues++; }

            // Check 13 (M-FC-6): PaintSerializer roundtrip
            var paintIn = PaintSerializer.CreateDefault(3, "#DC0000");
            paintIn.segments[0].stripes.Add(new StripeLayer { presetId = "bottomStripe", positionY = 0.85f, thickness = 0.05f, color = "#FFFFFF", mode = StripeMode.Solid });
            paintIn.segments[1].decals.Add(new DecalLayer { symbolId = "icon-wheelchair", positionX = 0.1f, positionY = 0.5f, scale = 1.0f, color = "#0066CC" });
            paintIn.segments[2].baseColor = "#0033A0";

            string serialized = PaintSerializer.Serialize(paintIn);
            if (string.IsNullOrEmpty(serialized))
            {
                Debug.LogError("[FleetCatalog SmokeTest] FAIL: PaintSerializer.Serialize returned empty");
                issues++;
            }
            else
            {
                var paintOut = PaintSerializer.Deserialize(serialized);
                if (paintOut == null) { Debug.LogError("[FleetCatalog SmokeTest] FAIL: PaintSerializer.Deserialize returned null"); issues++; }
                else
                {
                    if (paintOut.segments.Count != 3) { Debug.LogError($"[FleetCatalog SmokeTest] FAIL: roundtrip segments {paintOut.segments.Count} != 3"); issues++; }
                    else if (paintOut.segments[0].baseColor != "#DC0000") { Debug.LogError($"[FleetCatalog SmokeTest] FAIL: roundtrip seg0 baseColor: {paintOut.segments[0].baseColor}"); issues++; }
                    else if (paintOut.segments[0].stripes.Count != 1 || paintOut.segments[0].stripes[0].presetId != "bottomStripe")
                    {
                        Debug.LogError("[FleetCatalog SmokeTest] FAIL: roundtrip seg0 stripes mismatch");
                        issues++;
                    }
                    else if (paintOut.segments[1].decals.Count != 1 || paintOut.segments[1].decals[0].symbolId != "icon-wheelchair")
                    {
                        Debug.LogError("[FleetCatalog SmokeTest] FAIL: roundtrip seg1 decals mismatch");
                        issues++;
                    }
                    else
                    {
                        Debug.Log($"[FleetCatalog SmokeTest] PaintSerializer roundtrip OK — string length {serialized.Length} chars");
                    }
                }
            }

            // Check 14 (M-FC-8): MarketLiveryGenerator deterministic
            var paintA = MarketLiveryGenerator.Generate(seed: 12345, FleetVehicleType.EMU, segmentCount: 3);
            var paintB = MarketLiveryGenerator.Generate(seed: 12345, FleetVehicleType.EMU, segmentCount: 3);
            if (paintA.segments.Count != paintB.segments.Count
                || paintA.segments[0].baseColor != paintB.segments[0].baseColor
                || paintA.segments[0].stripes.Count != paintB.segments[0].stripes.Count
                || paintA.segments[0].decals.Count != paintB.segments[0].decals.Count)
            {
                Debug.LogError("[FleetCatalog SmokeTest] FAIL: MarketLiveryGenerator NOT deterministic");
                issues++;
            }

            // Check 15 (M-FC-8): different seed = different paint (high probability)
            var paintC = MarketLiveryGenerator.Generate(seed: 67890, FleetVehicleType.EMU, segmentCount: 3);
            if (paintA.segments[0].baseColor == paintC.segments[0].baseColor
                && paintA.segments[0].stripes.Count == paintC.segments[0].stripes.Count
                && paintA.segments[0].decals.Count == paintC.segments[0].decals.Count)
            {
                Debug.LogWarning("[FleetCatalog SmokeTest] WARN: seeds 12345 i 67890 dały identyczny paint (statystycznie rzadkie)");
            }

            // Check 16 (M-FC-8): InitialMarket pojazdy mają non-zero paintSeed po LoadAll
            int zeroSeeds = 0;
            foreach (var mv in FleetCatalog.InitialMarket)
            {
                if (mv.paintSeed == 0) zeroSeeds++;
            }
            if (zeroSeeds > 0)
            {
                Debug.LogError($"[FleetCatalog SmokeTest] FAIL: {zeroSeeds} initial market vehicles z paintSeed=0 (powinny być fallback'owane)");
                issues++;
            }

            // Check 17 (M-FC-8): GetOrResolvePaint cache works
            if (FleetCatalog.InitialMarket.Count > 0)
            {
                var mv = FleetCatalog.InitialMarket[0];
                var p1 = mv.GetOrResolvePaint();
                var p2 = mv.GetOrResolvePaint();
                if (!ReferenceEquals(p1, p2))
                {
                    Debug.LogError("[FleetCatalog SmokeTest] FAIL: GetOrResolvePaint nie cache'uje (zwraca różne instancje)");
                    issues++;
                }
            }

            // Check 18 (M-FC-9): ExternalWorkshop ma paint cost+time
            ExternalWorkshopCatalog.LoadAll();
            int workshopsWithPaint = 0;
            foreach (var w in ExternalWorkshopCatalog.GetAll())
            {
                if (w.paintCostPln > 0 && w.paintTimeDays > 0) workshopsWithPaint++;
            }
            if (workshopsWithPaint < 3)
            {
                Debug.LogError($"[FleetCatalog SmokeTest] FAIL: expected >=3 ZNTK with paint services, got {workshopsWithPaint}");
                issues++;
            }

            // Check 19 (M-FC-10): InteriorMixCalculator works
            var testMix = new System.Collections.Generic.List<SeatZoneSlot>
            {
                new SeatZoneSlot { startPercent = 0, endPercent = 70, type = SeatZoneType.SecondClassOpen },
                new SeatZoneSlot { startPercent = 70, endPercent = 100, type = SeatZoneType.Bicycle }
            };
            int seats = InteriorMixCalculator.CalculateSeats(testMix, 24.5f);
            int comfort = InteriorMixCalculator.CalculateComfortClass(testMix, null);
            if (seats <= 0 || comfort < 1 || comfort > 5)
            {
                Debug.LogError($"[FleetCatalog SmokeTest] FAIL: InteriorMixCalculator wrong output: seats={seats}, comfort={comfort}");
                issues++;
            }
            if (!InteriorMixCalculator.IsValid(testMix, out var mixError))
            {
                Debug.LogError($"[FleetCatalog SmokeTest] FAIL: testMix invalid: {mixError}");
                issues++;
            }

            // Check 20 (M-FC-10): PaintSerializer max-size guard (5 stripes + 8 decals × 12 segs nie crashuje)
            var bigPaint = PaintSerializer.CreateDefault(5, "#FAFAFA");
            for (int i = 0; i < bigPaint.segments.Count; i++)
            {
                for (int s = 0; s < 5; s++)
                    bigPaint.segments[i].stripes.Add(new StripeLayer { color = "#000000", positionY = 0.5f, thickness = 0.05f });
                for (int d = 0; d < 8; d++)
                    bigPaint.segments[i].decals.Add(new DecalLayer { symbolId = "icon-wifi", positionX = 0.5f, positionY = 0.5f, color = "#000000" });
            }
            string bigSerialized = PaintSerializer.Serialize(bigPaint);
            var bigDeserialized = PaintSerializer.Deserialize(bigSerialized);
            if (bigDeserialized == null || bigDeserialized.segments.Count != 5
                || bigDeserialized.segments[0].stripes.Count != 5
                || bigDeserialized.segments[0].decals.Count != 8)
            {
                Debug.LogError($"[FleetCatalog SmokeTest] FAIL: full paint serialization broken (string length: {bigSerialized?.Length ?? 0})");
                issues++;
            }
            else
            {
                Debug.Log($"[FleetCatalog SmokeTest] Full paint roundtrip OK — 5 segs × (5 stripes + 8 decals) = {bigSerialized.Length} chars");
            }

            // Check 21 (M-FC-10): Paint over-limit deserialization rejected
            var overflowPaint = PaintSerializer.CreateDefault(2, "#FFFFFF");
            for (int i = 0; i < 6; i++) // 6 > MAX 5
                overflowPaint.segments[0].stripes.Add(new StripeLayer { color = "#000000" });
            string overflowSerialized = PaintSerializer.Serialize(overflowPaint);
            var overflowResult = PaintSerializer.Deserialize(overflowSerialized);
            if (overflowResult != null)
            {
                Debug.LogError("[FleetCatalog SmokeTest] FAIL: PaintSerializer accepted over-limit paint (>MAX_STRIPES_PER_SEGMENT)");
                issues++;
            }

            if (issues == 0)
            {
                Debug.Log($"[FleetCatalog SmokeTest] OK — {wrapper.families.Count} families, {totalVariants} variants, full M-FC-1..10 flow passed.");
            }
            else
            {
                Debug.LogError($"[FleetCatalog SmokeTest] {issues} issue(s) found.");
            }
        }
    }
}
