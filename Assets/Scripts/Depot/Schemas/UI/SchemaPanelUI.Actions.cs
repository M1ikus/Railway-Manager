using UnityEngine;
using DepotSystem.Schemas.Placement;
using DepotSystem.Schemas.Selection;
using DepotSystem.Schemas.Snapshot;
using RailwayManager.Core;

namespace DepotSystem.Schemas.UI
{
    /// <summary>
    /// Partial: akcje gracza wymagające delegacji do innych komponentów —
    /// snapshot create flow (SnapshotSelectionTool + SnapshotSerializer + SaveDialog),
    /// place flow (TurnoutSchemaPlacer.StartPlacement),
    /// save preset flow (SchemaSaveDialogUI.Show).
    /// </summary>
    public partial class SchemaPanelUI
    {
        // ════════════════════════════════════════
        //  SNAPSHOT CREATE flow
        // ════════════════════════════════════════

        private void OnCreateSnapshotClicked()
        {
            // Aktywuj SnapshotSelectionTool — gracz przeciąga rectangle nad istniejącymi torami
            var tool = SnapshotSelectionTool.Instance;
            if (tool == null)
            {
                var go = new GameObject("SnapshotSelectionTool (auto-created by SchemaPanelUI)");
                tool = go.AddComponent<SnapshotSelectionTool>();
            }

            // Subscribe (idempotent — odpinamy stary handler na wypadek ponownego kliknięcia)
            tool.OnSelectionConfirmed -= OnSnapshotSelectionConfirmed;
            tool.OnSelectionConfirmed += OnSnapshotSelectionConfirmed;
            tool.OnSelectionCancelled -= OnSnapshotSelectionCancelled;
            tool.OnSelectionCancelled += OnSnapshotSelectionCancelled;

            Hide();  // ukryj panel żeby gracz mógł zaznaczać tory
            tool.StartSelection();
        }

        /// <summary>
        /// Handler firowany po LMB released w SnapshotSelectionTool. Serializuje selekcję,
        /// generuje SchemaGeometry dla thumbnail render, i otwiera SchemaSaveDialogUI.
        ///
        /// Workflow:
        /// 1. SnapshotSerializer.Serialize(result) → snapshotGeometry (lokalne coords).
        /// 2. SnapshotToSchemaGeometryConverter.Convert(snapshotGeometry) → schemaGeometry
        ///    (do thumbnail render).
        /// 3. SchemaSaveDialogUI.ShowForSnapshot(snapshotGeom, schemaGeom, callback).
        /// 4. Callback po save: refresh list + show panel.
        /// </summary>
        private void OnSnapshotSelectionConfirmed(SnapshotSelectionResult result)
        {
            if (result == null || result.IsEmpty)
            {
                Log.Warn("[SchemaPanelUI] Snapshot selection empty — pokazuję panel z powrotem");
                Show();
                return;
            }

            Log.Info($"[SchemaPanelUI] Snapshot selection confirmed: {result.selectedTracks.Count} tracks, {result.selectedTurnouts.Count} turnouts. Otwieram save dialog.");

            // Serialize selekcję do snapshot geometry (lokalne coords względem selectionCenter)
            var snapshotGeom = SnapshotSerializer.Serialize(result);
            if (snapshotGeom == null)
            {
                Log.Error("[SchemaPanelUI] Snapshot serialization zwróciło null");
                Show();
                return;
            }

            // Convert do SchemaGeometry — potrzebne dla thumbnail render w save dialog
            var schemaGeom = SnapshotToSchemaGeometryConverter.Convert(snapshotGeom);

            // Open save dialog z snapshot type (= type=snapshot, geometry zaszyta w JSON)
            var dialog = SchemaSaveDialogUI.Instance ?? DepotServices.Get<SchemaSaveDialogUI>();
            if (dialog == null)
            {
                var go = new GameObject("SchemaSaveDialogUI (auto-created by SchemaPanelUI)");
                dialog = go.AddComponent<SchemaSaveDialogUI>();
            }

            dialog.ShowForSnapshot(snapshotGeom, schemaGeom, savedDef => {
                Log.Info($"[SchemaPanelUI] Snapshot saved: '{savedDef.id}' = '{savedDef.name}' (type={savedDef.type}, tracks={savedDef.snapshotGeometry?.tracks?.Length}, turnouts={savedDef.snapshotGeometry?.turnouts?.Length})");
                // Refresh list — nowy snapshot pojawi się w "User custom"
                RefreshList();
                Show();
            });
        }

        /// <summary>
        /// Handler dla anulowania selekcji (Esc lub external CancelSelection).
        /// Pokazuje panel z powrotem żeby gracz mógł wybrać inną akcję.
        /// </summary>
        private void OnSnapshotSelectionCancelled()
        {
            Log.Info("[SchemaPanelUI] Snapshot selection anulowana — pokazuję panel z powrotem");
            Show();
        }

        // ════════════════════════════════════════
        //  PLACE flow
        // ════════════════════════════════════════

        private void OnPlaceClicked()
        {
            if (_selectedDef == null)
            {
                Log.Warn("[SchemaPanelUI] OnPlaceClicked: no schema selected");
                return;
            }

            // Generative schemat wymaga _editParams. Snapshot użyje swojego snapshotGeometry
            // (parameters i _editParams są null dla snapshot).
            if (_selectedDef.IsGenerative && _editParams == null)
            {
                Log.Warn("[SchemaPanelUI] OnPlaceClicked: generative schema bez _editParams");
                return;
            }

            // Skonstruuj edited definition. Dla generative — clone + override parameters z _editParams.
            // Dla snapshot — clone (snapshotGeometry shared).
            var editDef = CloneDefinition(_selectedDef);
            if (_selectedDef.IsGenerative)
                editDef.parameters = _editParams;

            // Wywołaj placer (lazy auto-create gdy nie istnieje w scenie)
            var placer = EnsurePlacer();
            placer.StartPlacement(editDef);
            Log.Info($"[SchemaPanelUI] Started placement for '{editDef.name}' (type={editDef.type})");

            // Hide panel żeby gracz miał czysty widok do confirmacji placement.
            // Gracz może otworzyć ponownie klikając SCH w sub-toolbar (ForceShow z MD-X fix).
            Hide();
        }

        private TurnoutSchemaPlacer EnsurePlacer()
        {
            if (TurnoutSchemaPlacer.Instance != null) return TurnoutSchemaPlacer.Instance;
            var existing = DepotServices.Get<TurnoutSchemaPlacer>();
            if (existing != null) return existing;

            var go = new GameObject("TurnoutSchemaPlacer (auto-created by SchemaPanelUI)");
            return go.AddComponent<TurnoutSchemaPlacer>();
        }

        // ════════════════════════════════════════
        //  SAVE PRESET flow
        // ════════════════════════════════════════

        /// <summary>
        /// MD-6 — opens save dialog z aktualnymi parametrami + wygenerowaną geometrią.
        /// Po zapisie callback refresh'uje listę żeby nowy preset się pojawił.
        /// </summary>
        private void OnSaveAsClicked()
        {
            if (_selectedDef == null || _editParams == null)
            {
                Log.Warn("[SchemaPanelUI] OnSaveAsClicked: no schema selected");
                return;
            }

            // Wygeneruj aktualną geometrię z editParams (potrzebną do thumbnail render)
            SchemaGeometry geometry = null;
            if (_selectedDef.IsGenerative && _selectedGenerator != null)
            {
                int turnoutCount = _selectedGenerator.ComputeTurnoutCount(_editParams.trackCount);
                _editParams.Normalize(turnoutCount);
                geometry = _selectedGenerator.Generate(_editParams);
            }

            if (geometry == null)
            {
                Log.Warn("[SchemaPanelUI] OnSaveAsClicked: could not generate geometry for thumbnail");
            }

            // Open save dialog
            var dialog = SchemaSaveDialogUI.Instance ?? DepotServices.Get<SchemaSaveDialogUI>();
            if (dialog == null)
            {
                var go = new GameObject("SchemaSaveDialogUI (auto-created)");
                dialog = go.AddComponent<SchemaSaveDialogUI>();
            }

            dialog.Show(_selectedDef, _editParams, geometry, OnPresetSaved);
        }

        private void OnPresetSaved(TurnoutSchemaDefinition savedDef)
        {
            Log.Info($"[SchemaPanelUI] Preset saved: '{savedDef.id}' = '{savedDef.name}' — refreshing list");
            RefreshList();
            // Auto-select nowo zapisany schemat
            OnSchemaSelected(savedDef);
        }
    }
}
