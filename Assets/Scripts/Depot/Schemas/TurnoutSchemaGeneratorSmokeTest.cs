using UnityEngine;
using DepotSystem.Schemas.Generators;
using DepotSystem.Schemas.Placement;
using DepotSystem.Schemas.Selection;
using DepotSystem.Schemas.Snapshot;
using DepotSystem.Schemas.UI;
using RailwayManager.Core;

namespace DepotSystem.Schemas
{
    /// <summary>
    /// MD-1 smoke test — sprawdza działanie 3 generatorów (Ladder/Throat/Scissors)
    /// + deserializacji JSON. Wynik renderowany jako Gizmos w Scene view (linie torów +
    /// sphere'y dla origin rozjazdów + sphere'y dla endpointów).
    ///
    /// Użycie:
    /// 1. Wrzuć ten komponent jako GameObject na scenie Depot (Inspector "Add Component")
    /// 2. Right-click w Inspectorze → wybierz jedną z opcji ContextMenu
    /// 3. Zobacz wynik w Scene view (Gizmos muszą być włączone)
    ///
    /// MD-1 nie integruje się z PrefabTrackBuilder/TurnoutPlacer — to przyjdzie w MD-3+
    /// (placement UX). Tutaj testujemy tylko że geometria się generuje sensownie.
    /// </summary>
    public class TurnoutSchemaGeneratorSmokeTest : MonoBehaviour
    {
        [Header("Last generated geometry (debug only — set by ContextMenu actions)")]
        [SerializeField] private bool _hasGeometry = false;

        [Header("Gizmo colors")]
        [SerializeField] private Color _trackColor = new Color(0.3f, 0.7f, 1.0f);     // cyan
        [SerializeField] private Color _maneuverColor = new Color(1.0f, 0.6f, 0.2f);  // orange
        [SerializeField] private Color _turnoutOriginColor = new Color(1.0f, 0.2f, 0.2f); // red
        [SerializeField] private Color _endpointColor = new Color(0.2f, 1.0f, 0.2f);  // green
        [SerializeField] private float _turnoutOriginRadius = 0.8f;
        [SerializeField] private float _endpointRadius = 1.2f;

        // Cached geometry (re-rendered in OnDrawGizmos)
        private SchemaGeometry _lastGeometry;
        private string _lastSchemaName;

        // ════════════════════════════════════════
        //  CONTEXT MENU — Generative tests
        // ════════════════════════════════════════

        [ContextMenu("Generate Ladder 3T")]
        public void GenerateLadder3T()
        {
            var gen = TurnoutSchemaGeneratorRegistry.Get(TurnoutSchemaCategory.Ladder);
            var p = gen.DefaultParameters();
            p.trackCount = 3;
            RunAndStore(gen, p, "Ladder 3T R190 5.0m");
        }

        [ContextMenu("Generate Ladder 4T")]
        public void GenerateLadder4T()
        {
            var gen = TurnoutSchemaGeneratorRegistry.Get(TurnoutSchemaCategory.Ladder);
            var p = gen.DefaultParameters();
            p.trackCount = 4;
            RunAndStore(gen, p, "Ladder 4T R190 5.0m");
        }

        [ContextMenu("Generate Ladder 5T")]
        public void GenerateLadder5T()
        {
            var gen = TurnoutSchemaGeneratorRegistry.Get(TurnoutSchemaCategory.Ladder);
            var p = gen.DefaultParameters();
            p.trackCount = 5;
            RunAndStore(gen, p, "Ladder 5T R190 5.0m");
        }

        [ContextMenu("Generate Ladder 5T mirror")]
        public void GenerateLadder5TMirror()
        {
            var gen = TurnoutSchemaGeneratorRegistry.Get(TurnoutSchemaCategory.Ladder);
            var p = gen.DefaultParameters();
            p.trackCount = 5;
            p.mirror = true;
            RunAndStore(gen, p, "Ladder 5T R190 5.0m mirror");
        }

        [ContextMenu("Generate Throat 3T")]
        public void GenerateThroat3T()
        {
            var gen = TurnoutSchemaGeneratorRegistry.Get(TurnoutSchemaCategory.Throat);
            var p = gen.DefaultParameters();
            p.trackCount = 3;
            // Throat 3T = 2 rozjazdy: R190 + R300
            p.turnoutTypes = new[] { SchemaTurnoutType.R190, SchemaTurnoutType.R300 };
            RunAndStore(gen, p, "Throat 3T R190+R300");
        }

        [ContextMenu("Generate Throat 4T")]
        public void GenerateThroat4T()
        {
            var gen = TurnoutSchemaGeneratorRegistry.Get(TurnoutSchemaCategory.Throat);
            var p = gen.DefaultParameters();
            p.trackCount = 4;
            p.turnoutTypes = new[] { SchemaTurnoutType.R190, SchemaTurnoutType.R190, SchemaTurnoutType.R300 };
            RunAndStore(gen, p, "Throat 4T R190+R190+R300");
        }

        [ContextMenu("Generate Scissors")]
        public void GenerateScissors()
        {
            var gen = TurnoutSchemaGeneratorRegistry.Get(TurnoutSchemaCategory.Scissors);
            var p = gen.DefaultParameters();
            RunAndStore(gen, p, "Scissors R190 5.0m");
        }

        // ════════════════════════════════════════
        //  CONTEXT MENU — JSON deserialize tests
        // ════════════════════════════════════════

        [ContextMenu("Test JSON deserialize (mixed Ladder)")]
        public void TestJsonMixedLadder()
        {
            string json = @"{
                ""schemaFormatVersion"": 1,
                ""id"": ""test_mixed_ladder"",
                ""name"": ""Mixed Ladder Test"",
                ""category"": ""Ladder"",
                ""type"": ""generative"",
                ""parameters"": {
                    ""trackCount"": 5,
                    ""trackSpacings"": [5.0, 5.0, 6.0, 5.0],
                    ""turnoutTypes"": [""R190"", ""R190"", ""R300"", ""R300""],
                    ""mirror"": false
                }
            }";

            var def = TurnoutSchemaDefinition.FromJson(json);
            if (def == null)
            {
                Log.Error("[SmokeTest] JSON deserialize failed");
                return;
            }
            Log.Info($"[SmokeTest] Deserialized: id='{def.id}', name='{def.name}', category={def.ParseCategory()}, trackCount={def.parameters.trackCount}");

            var geom = TurnoutSchemaGeneratorRegistry.GenerateFromDefinition(def);
            if (geom == null)
            {
                Log.Error("[SmokeTest] Generate from definition failed");
                return;
            }
            StoreGeometry(geom, def.name);
        }

        [ContextMenu("Test JSON deserialize (shorthand)")]
        public void TestJsonShorthand()
        {
            string json = @"{
                ""schemaFormatVersion"": 1,
                ""id"": ""test_shorthand"",
                ""name"": ""Shorthand Ladder Test"",
                ""category"": ""Ladder"",
                ""type"": ""generative"",
                ""parameters"": {
                    ""trackCount"": 4,
                    ""trackSpacing"": 5.5,
                    ""turnoutType"": ""R300"",
                    ""mirror"": false
                }
            }";

            var def = TurnoutSchemaDefinition.FromJson(json);
            if (def == null)
            {
                Log.Error("[SmokeTest] JSON deserialize failed");
                return;
            }
            Log.Info($"[SmokeTest] Deserialized shorthand: id='{def.id}', name='{def.name}'");

            var geom = TurnoutSchemaGeneratorRegistry.GenerateFromDefinition(def);
            if (geom == null) { Log.Error("[SmokeTest] Generate failed"); return; }
            StoreGeometry(geom, def.name);
        }

        [ContextMenu("Test JSON serialize (round-trip)")]
        public void TestJsonRoundTrip()
        {
            // Stwórz definicję, serialize, deserialize, porównaj
            var original = new TurnoutSchemaDefinition
            {
                id = "test_roundtrip",
                name = "Roundtrip Test",
                category = "Throat",
                type = "generative",
                author = "SmokeTest",
                version = "1.0",
                parameters = new SchemaParameters
                {
                    trackCount = 4,
                    trackSpacing = 5.0f,
                    turnoutType = SchemaTurnoutType.R190,
                }
            };

            string json = original.ToJson();
            Log.Info($"[SmokeTest] Serialized:\n{json}");

            var roundTripped = TurnoutSchemaDefinition.FromJson(json);
            if (roundTripped == null) { Log.Error("[SmokeTest] Roundtrip deserialize failed"); return; }

            bool ok = roundTripped.id == original.id
                && roundTripped.name == original.name
                && roundTripped.parameters.trackCount == original.parameters.trackCount
                && Mathf.Approximately(roundTripped.parameters.trackSpacing, original.parameters.trackSpacing);
            Log.Info($"[SmokeTest] Roundtrip {(ok ? "OK" : "FAILED")}");
        }

        // ════════════════════════════════════════
        //  CONTEXT MENU — Catalog (MD-2)
        // ════════════════════════════════════════

        [ContextMenu("Catalog: LoadAll")]
        public void CatalogLoadAll()
        {
            TurnoutSchemaCatalog.LoadAll();
            Log.Info($"[SmokeTest] Catalog loaded: {TurnoutSchemaCatalog.BuiltIn.Count} built-in + {TurnoutSchemaCatalog.UserCustom.Count} user + {TurnoutSchemaCatalog.Workshop.Count} workshop = {TurnoutSchemaCatalog.AllSchemas.Count} total");
        }

        [ContextMenu("Catalog: List all schemas")]
        public void CatalogListAll()
        {
            if (!TurnoutSchemaCatalog.IsLoaded) TurnoutSchemaCatalog.LoadAll();

            Log.Info($"[SmokeTest] === Built-in ({TurnoutSchemaCatalog.BuiltIn.Count}) ===");
            foreach (var def in TurnoutSchemaCatalog.BuiltIn)
                Log.Info($"  [{def.id}] '{def.name}' ({def.category}, type={def.type}, trackCount={def.parameters?.trackCount})");

            Log.Info($"[SmokeTest] === User custom ({TurnoutSchemaCatalog.UserCustom.Count}) ===");
            foreach (var def in TurnoutSchemaCatalog.UserCustom)
                Log.Info($"  [{def.id}] '{def.name}' ({def.category}, type={def.type})");

            Log.Info($"[SmokeTest] === Workshop ({TurnoutSchemaCatalog.Workshop.Count}) ===");
            foreach (var def in TurnoutSchemaCatalog.Workshop)
                Log.Info($"  [{def.id}] '{def.name}'");
        }

        [ContextMenu("Catalog: Generate from builtin (ladder 4T)")]
        public void CatalogGenerateBuiltinLadder4T()
        {
            CatalogGenerateById("builtin_ladder_4t");
        }

        [ContextMenu("Catalog: Generate from builtin (throat 4T)")]
        public void CatalogGenerateBuiltinThroat4T()
        {
            CatalogGenerateById("builtin_throat_4t");
        }

        [ContextMenu("Catalog: Generate from builtin (scissors)")]
        public void CatalogGenerateBuiltinScissors()
        {
            CatalogGenerateById("builtin_scissors");
        }

        [ContextMenu("Catalog: Save current as test custom")]
        public void CatalogSaveCurrentAsCustom()
        {
            if (!TurnoutSchemaCatalog.IsLoaded) TurnoutSchemaCatalog.LoadAll();

            // Tworzymy dummy custom Ladder 6T R190 z spacing 5.5m (różny od built-in)
            var def = new TurnoutSchemaDefinition
            {
                id = "test_user_ladder_6t",
                name = "Test User Ladder 6T",
                description = "Smoke test custom — Ladder 6T R190 5.5m (różny od built-in 5T 5.0m)",
                category = "Ladder",
                type = "generative",
                author = "SmokeTest",
                tags = new[] { "test", "ladder" },
                version = "1.0",
                parameters = new SchemaParameters
                {
                    trackCount = 6,
                    trackSpacing = 5.5f,
                    turnoutType = SchemaTurnoutType.R190,
                    mirror = false,
                }
            };

            bool ok = TurnoutSchemaCatalog.SaveUser(def);
            Log.Info($"[SmokeTest] Save custom: {(ok ? "OK" : "FAILED")} → '{def.id}'");
        }

        [ContextMenu("Catalog: Delete test_user_ladder_6t")]
        public void CatalogDeleteTestUser()
        {
            bool ok = TurnoutSchemaCatalog.DeleteUser("test_user_ladder_6t");
            Log.Info($"[SmokeTest] Delete custom: {(ok ? "OK" : "FAILED")}");
        }

        [ContextMenu("Catalog: Reload")]
        public void CatalogReload()
        {
            TurnoutSchemaCatalog.Reload();
            Log.Info($"[SmokeTest] Catalog reloaded: {TurnoutSchemaCatalog.AllSchemas.Count} total");
        }

        [ContextMenu("Catalog: Open user folder in explorer")]
        public void CatalogOpenUserFolder()
        {
            string path = TurnoutSchemaCatalog.GetUserFolderPath();
            Log.Info($"[SmokeTest] User folder: {path}");
            // Open w explorer (Windows)
            Application.OpenURL("file:///" + path.Replace("\\", "/"));
        }

        [ContextMenu("Catalog: Show paths")]
        public void CatalogShowPaths()
        {
            Log.Info($"[SmokeTest] Built-in folder: {TurnoutSchemaCatalog.GetBuiltInFolderPath()}");
            Log.Info($"[SmokeTest] User folder:     {TurnoutSchemaCatalog.GetUserFolderPath()}");
        }

        private void CatalogGenerateById(string id)
        {
            if (!TurnoutSchemaCatalog.IsLoaded) TurnoutSchemaCatalog.LoadAll();

            var def = TurnoutSchemaCatalog.FindById(id);
            if (def == null)
            {
                Log.Error($"[SmokeTest] Schema '{id}' not found in catalog");
                return;
            }
            Log.Info($"[SmokeTest] Found: '{def.name}' ({def.category})");

            var geom = TurnoutSchemaGeneratorRegistry.GenerateFromDefinition(def);
            if (geom == null)
            {
                Log.Error($"[SmokeTest] Generate from '{id}' failed");
                return;
            }
            StoreGeometry(geom, $"[catalog] {def.name}");
        }

        // ════════════════════════════════════════
        //  CONTEXT MENU — Placement (MD-3 MVP)
        // ════════════════════════════════════════

        [ContextMenu("Placement: Start (builtin ladder 4T)")]
        public void PlacementStartLadder4T()
        {
            PlacementStartFromCatalog("builtin_ladder_4t");
        }

        [ContextMenu("Placement: Start (builtin throat 4T)")]
        public void PlacementStartThroat4T()
        {
            PlacementStartFromCatalog("builtin_throat_4t");
        }

        [ContextMenu("Placement: Start (builtin scissors)")]
        public void PlacementStartScissors()
        {
            PlacementStartFromCatalog("builtin_scissors");
        }

        [ContextMenu("Placement: Start (builtin ladder 3T)")]
        public void PlacementStartLadder3T()
        {
            PlacementStartFromCatalog("builtin_ladder_3t");
        }

        [ContextMenu("Placement: Cancel")]
        public void PlacementCancel()
        {
            var placer = EnsurePlacer();
            placer?.CancelPlacement();
        }

        [ContextMenu("Placement: Force confirm")]
        public void PlacementForceConfirm()
        {
            var placer = EnsurePlacer();
            placer?.ConfirmPlacement();
        }

        [ContextMenu("Placement: Clear last confirmed (gizmos)")]
        public void PlacementClearLastConfirmed()
        {
            var placer = EnsurePlacer();
            placer?.ClearLastConfirmed();
        }

        [ContextMenu("Placement: A13 — Accept adaptive proposal")]
        public void PlacementAcceptAdaptive()
        {
            var placer = EnsurePlacer();
            if (placer == null) return;
            if (!placer.HasAdaptiveProposal)
            {
                Log.Warn("[SmokeTest] No A13 proposal active");
                return;
            }
            placer.AcceptAdaptiveProposal();
        }

        [ContextMenu("Placement: A13 — Reject adaptive proposal")]
        public void PlacementRejectAdaptive()
        {
            var placer = EnsurePlacer();
            placer?.RejectAdaptiveProposal();
        }

        [ContextMenu("Placement: Toggle auto-rotation")]
        public void PlacementToggleAutoRotation()
        {
            var placer = EnsurePlacer();
            if (placer == null) return;
            placer.enableAutoRotation = !placer.enableAutoRotation;
            Log.Info($"[SmokeTest] Auto-rotation: {(placer.enableAutoRotation ? "ON" : "OFF")}");
        }

        [ContextMenu("Placement: Toggle adaptive prompt")]
        public void PlacementToggleAdaptive()
        {
            var placer = EnsurePlacer();
            if (placer == null) return;
            placer.enableAdaptivePrompt = !placer.enableAdaptivePrompt;
            Log.Info($"[SmokeTest] A13 adaptive prompt: {(placer.enableAdaptivePrompt ? "ON" : "OFF")}");
        }

        // ════════════════════════════════════════
        //  CONTEXT MENU — Schema Panel UI (MD-5)
        // ════════════════════════════════════════

        [ContextMenu("UI: Show schema panel")]
        public void UIShowPanel()
        {
            var panel = EnsureSchemaPanel();
            panel.Show();
        }

        [ContextMenu("UI: Hide schema panel")]
        public void UIHidePanel()
        {
            var panel = EnsureSchemaPanel();
            panel.Hide();
        }

        [ContextMenu("UI: Toggle schema panel")]
        public void UITogglePanel()
        {
            var panel = EnsureSchemaPanel();
            panel.Toggle();
        }

        [ContextMenu("UI: Open save dialog (test ladder 4T params)")]
        public void UIOpenSaveDialog()
        {
            // Test scenario: render thumbnail + open save dialog z hardcoded ladder 4T
            if (!TurnoutSchemaCatalog.IsLoaded) TurnoutSchemaCatalog.LoadAll();

            var baseDef = TurnoutSchemaCatalog.FindById("builtin_ladder_4t");
            if (baseDef == null)
            {
                Log.Error("[SmokeTest] builtin_ladder_4t not found");
                return;
            }

            // Generate geometry dla thumbnail
            var generator = DepotSystem.Schemas.Generators.TurnoutSchemaGeneratorRegistry.Get(TurnoutSchemaCategory.Ladder);
            var editParams = new SchemaParameters
            {
                trackCount = 5,
                trackSpacing = 5.5f,
                turnoutType = SchemaTurnoutType.R190,
                mirror = false,
            };
            editParams.Normalize(generator.ComputeTurnoutCount(editParams.trackCount));
            var geometry = generator.Generate(editParams);

            // Open dialog
            var dialog = DepotSystem.Schemas.UI.SchemaSaveDialogUI.Instance;
            if (dialog == null)
            {
                var go = new GameObject("SchemaSaveDialogUI (auto-created by SmokeTest)");
                dialog = go.AddComponent<DepotSystem.Schemas.UI.SchemaSaveDialogUI>();
            }
            dialog.Show(baseDef, editParams, geometry, savedDef => {
                Log.Info($"[SmokeTest] Save callback: '{savedDef.id}' = '{savedDef.name}'");
            });
        }

        // ════════════════════════════════════════
        //  CONTEXT MENU — Snapshot selection (MD-7)
        // ════════════════════════════════════════

        [ContextMenu("Snapshot: Start selection mode")]
        public void SnapshotStartSelection()
        {
            var tool = EnsureSnapshotTool();
            tool.StartSelection();
        }

        [ContextMenu("Snapshot: Cancel selection")]
        public void SnapshotCancelSelection()
        {
            var tool = EnsureSnapshotTool();
            tool.CancelSelection();
        }

        private SnapshotSelectionTool EnsureSnapshotTool()
        {
            if (SnapshotSelectionTool.Instance != null) return SnapshotSelectionTool.Instance;

            var existing = FindAnyObjectByType<SnapshotSelectionTool>();
            if (existing != null) return existing;

            // Auto-create + subscribe to event (placeholder MD-7, MD-8 podepnie real save flow)
            var go = new GameObject("SnapshotSelectionTool (auto-created by SmokeTest)");
            var tool = go.AddComponent<SnapshotSelectionTool>();
            tool.OnSelectionConfirmed += OnSnapshotSelectionConfirmed;
            tool.OnSelectionCancelled += OnSnapshotSelectionCancelled;
            return tool;
        }

        private void OnSnapshotSelectionConfirmed(SnapshotSelectionResult result)
        {
            Log.Info($"[SmokeTest] Snapshot selection confirmed event: {result}");

            // MD-8: serialize → render thumbnail → open save dialog z type=snapshot
            var snapshotGeom = SnapshotSerializer.Serialize(result);
            if (snapshotGeom == null)
            {
                Log.Error("[SmokeTest] Snapshot serialization failed");
                return;
            }

            // Convert do SchemaGeometry żeby thumbnail render zadziałał
            var schemaGeom = SnapshotToSchemaGeometryConverter.Convert(snapshotGeom);

            // Open save dialog z snapshot type
            var dialog = SchemaSaveDialogUI.Instance;
            if (dialog == null)
            {
                var go = new GameObject("SchemaSaveDialogUI (auto-created by SmokeTest)");
                dialog = go.AddComponent<SchemaSaveDialogUI>();
            }
            dialog.ShowForSnapshot(snapshotGeom, schemaGeom, savedDef => {
                Log.Info($"[SmokeTest] Snapshot saved: '{savedDef.id}' = '{savedDef.name}' (type={savedDef.type}, geometry tracks={savedDef.snapshotGeometry?.tracks?.Length}, turnouts={savedDef.snapshotGeometry?.turnouts?.Length})");
            });
        }

        private void OnSnapshotSelectionCancelled()
        {
            Log.Info("[SmokeTest] Snapshot selection cancelled event");
        }

        [ContextMenu("UI: Render thumbnail (ladder 4T) + log base64 length")]
        public void UIRenderThumbnailTest()
        {
            var generator = DepotSystem.Schemas.Generators.TurnoutSchemaGeneratorRegistry.Get(TurnoutSchemaCategory.Ladder);
            var p = generator.DefaultParameters();
            p.trackCount = 4;
            p.Normalize(generator.ComputeTurnoutCount(p.trackCount));
            var geom = generator.Generate(p);

            string base64 = DepotSystem.Schemas.Placement.SchemaThumbnailGenerator.RenderThumbnailBase64(geom);
            Log.Info($"[SmokeTest] Thumbnail base64 length: {base64.Length} chars (~{base64.Length * 3 / 4} bytes PNG)");
        }

        private SchemaPanelUI EnsureSchemaPanel()
        {
            if (SchemaPanelUI.Instance != null) return SchemaPanelUI.Instance;

            var existing = FindAnyObjectByType<SchemaPanelUI>();
            if (existing != null) return existing;

            // Auto-create
            var go = new GameObject("SchemaPanelUI (auto-created by SmokeTest)");
            return go.AddComponent<SchemaPanelUI>();
        }

        private void PlacementStartFromCatalog(string id)
        {
            if (!TurnoutSchemaCatalog.IsLoaded) TurnoutSchemaCatalog.LoadAll();

            var def = TurnoutSchemaCatalog.FindById(id);
            if (def == null)
            {
                Log.Error($"[SmokeTest] Schema '{id}' not found in catalog");
                return;
            }

            var placer = EnsurePlacer();
            placer.StartPlacement(def);
        }

        /// <summary>
        /// Znajduje istniejący <see cref="TurnoutSchemaPlacer"/> lub tworzy nowy GameObject.
        /// MVP — w produkcji TurnoutSchemaPlacer ma być jednym z toolów depot toolbar.
        /// </summary>
        private TurnoutSchemaPlacer EnsurePlacer()
        {
            if (TurnoutSchemaPlacer.Instance != null) return TurnoutSchemaPlacer.Instance;

            var existing = FindAnyObjectByType<TurnoutSchemaPlacer>();
            if (existing != null) return existing;

            // Auto-create
            var go = new GameObject("TurnoutSchemaPlacer (auto-created by SmokeTest)");
            return go.AddComponent<TurnoutSchemaPlacer>();
        }

        // ════════════════════════════════════════
        //  CONTEXT MENU — Misc
        // ════════════════════════════════════════

        [ContextMenu("Clear")]
        public void Clear()
        {
            _lastGeometry = null;
            _lastSchemaName = null;
            _hasGeometry = false;
            Log.Info("[SmokeTest] Geometry cleared");
        }

        // ════════════════════════════════════════
        //  HELPERS
        // ════════════════════════════════════════

        private void RunAndStore(ITurnoutSchemaGenerator gen, SchemaParameters p, string label)
        {
            int turnoutCount = gen.ComputeTurnoutCount(p.trackCount);
            p.Normalize(turnoutCount);

            if (!gen.ValidateParameters(p, out var err))
            {
                Log.Warn($"[SmokeTest] '{label}' validation: {err}");
            }

            var geom = gen.Generate(p);
            StoreGeometry(geom, label);
        }

        private void StoreGeometry(SchemaGeometry geom, string label)
        {
            _lastGeometry = geom;
            _lastSchemaName = label;
            _hasGeometry = geom != null;

            if (geom == null)
            {
                Log.Error($"[SmokeTest] '{label}' returned null geometry");
                return;
            }

            Log.Info($"[SmokeTest] '{label}' generated: {geom.tracks.Count} tracks, {geom.turnouts.Count} turnouts, {geom.endpoints.Count} endpoints, bounds={geom.bounds.size}, centroid={geom.centroid}");
        }

        // ════════════════════════════════════════
        //  GIZMOS — render last geometry
        // ════════════════════════════════════════

        void OnDrawGizmos()
        {
            if (_lastGeometry == null) return;

            // Render w lokalnych współrzędnych komponentu (transform.position offset)
            Vector3 origin = transform.position;

            // Tory — kolor zależy od typu (Parking/Entry/Exit cyan, Maneuver pomarańczowy)
            foreach (var track in _lastGeometry.tracks)
            {
                if (track.polyline == null || track.polyline.Count < 2) continue;
                Gizmos.color = track.trackTypeName == "Maneuver" ? _maneuverColor : _trackColor;
                for (int i = 0; i < track.polyline.Count - 1; i++)
                {
                    Gizmos.DrawLine(origin + track.polyline[i], origin + track.polyline[i + 1]);
                }
            }

            // Rozjazdy — sphere przy origin
            Gizmos.color = _turnoutOriginColor;
            foreach (var turnout in _lastGeometry.turnouts)
            {
                Gizmos.DrawSphere(origin + turnout.origin, _turnoutOriginRadius);
                // Strzałka kierunku
                Gizmos.DrawLine(origin + turnout.origin, origin + turnout.origin + turnout.direction * 3f);
            }

            // Endpointy — większe sphere'y
            Gizmos.color = _endpointColor;
            foreach (var endpoint in _lastGeometry.endpoints)
            {
                Gizmos.DrawWireSphere(origin + endpoint, _endpointRadius);
            }

            // Bounds wireframe (orientacyjnie)
            Gizmos.color = Color.gray;
            Gizmos.DrawWireCube(origin + _lastGeometry.bounds.center, _lastGeometry.bounds.size);
        }
    }
}
