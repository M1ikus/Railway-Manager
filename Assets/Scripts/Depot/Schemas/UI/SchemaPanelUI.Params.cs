using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using DepotSystem.Schemas.Generators;

namespace DepotSystem.Schemas.UI
{
    /// <summary>
    /// Partial: handlery zmiany parametrów (slider/dropdown/toggle) + propagacja
    /// do _editParams + live-preview regeneration jeśli placement aktywny.
    /// </summary>
    public partial class SchemaPanelUI
    {
        // ════════════════════════════════════════
        //  PARAMETERS LOGIC
        // ════════════════════════════════════════

        private void ApplyTrackCountLimits()
        {
            if (_trackCountSlider == null) return;

            switch (_selectedCategory)
            {
                case TurnoutSchemaCategory.Ladder:
                    _trackCountSlider.minValue = LadderSchemaGenerator.MinTrackCount;
                    _trackCountSlider.maxValue = LadderSchemaGenerator.MaxTrackCount;
                    break;
                case TurnoutSchemaCategory.Throat:
                    _trackCountSlider.minValue = ThroatSchemaGenerator.MinTrackCount;
                    _trackCountSlider.maxValue = ThroatSchemaGenerator.MaxTrackCount;
                    break;
                case TurnoutSchemaCategory.Scissors:
                    _trackCountSlider.minValue = 2;
                    _trackCountSlider.maxValue = 2;
                    break;
                case TurnoutSchemaCategory.Trapez:
                    _trackCountSlider.minValue = 2;
                    _trackCountSlider.maxValue = 2;
                    break;
                default:
                    _trackCountSlider.minValue = 2;
                    _trackCountSlider.maxValue = 8;
                    break;
            }
        }

        private void OnTrackCountChanged(float value)
        {
            if (_suppressEvents || _editParams == null) return;
            int newCount = Mathf.RoundToInt(value);
            _editParams.trackCount = newCount;

            // Reset arrays — następna geometria użyje shorthand
            _editParams.trackSpacings = null;
            _editParams.turnoutTypes = null;

            if (_trackCountValueLabel != null) _trackCountValueLabel.text = newCount.ToString();
            RebuildAdvancedRows();
            RegenerateLivePreviewIfActive();
        }

        private void OnSpacingChanged(float value)
        {
            if (_suppressEvents || _editParams == null) return;
            float clamped = Mathf.Round(value * 10f) / 10f;  // step 0.1m
            clamped = SchemaParameters.ClampSpacing(clamped);
            _editParams.trackSpacing = clamped;
            _editParams.trackSpacings = null;  // force shorthand → expand all
            if (_spacingValueLabel != null) _spacingValueLabel.text = clamped.ToString("F1");
            RegenerateLivePreviewIfActive();
        }

        private void OnTurnoutTypeChanged(int index)
        {
            if (_suppressEvents || _editParams == null) return;
            if (index < 0 || index >= TurnoutTypeOptions.Length) return;
            _editParams.turnoutType = TurnoutTypeOptions[index];
            _editParams.turnoutTypes = null;
            RegenerateLivePreviewIfActive();
        }

        private void OnMirrorToggled(bool state)
        {
            if (_suppressEvents || _editParams == null) return;
            _editParams.mirror = state;
            RegenerateLivePreviewIfActive();
        }

        private void OnAdvancedToggled(bool state)
        {
            if (_advancedSection != null) _advancedSection.SetActive(state);
            if (state) RebuildAdvancedRows();  // lazy build — generujemy dopiero gdy gracz otworzy
        }

        // ════════════════════════════════════════
        //  PER-PAIR FOLDOUT (TD-002)
        // ════════════════════════════════════════

        /// <summary>
        /// Generuje N-1 wierszy per-pair (slider trackSpacings[i] + dropdown turnoutTypes[i]).
        /// Wywoływane przy zmianie schematu, trackCount, lub otwarciu Advanced foldoutu.
        /// </summary>
        private void RebuildAdvancedRows()
        {
            if (_advancedRowsContainer == null) return;

            // Destroy old rows + clear widget references
            foreach (Transform child in _advancedRowsContainer.transform)
                Destroy(child.gameObject);
            _advancedSpacingSliders.Clear();
            _advancedSpacingValueLabels.Clear();
            _advancedTurnoutDropdowns.Clear();

            // No schema → show empty state
            if (_selectedDef == null || _editParams == null || _selectedGenerator == null
                || !_selectedDef.IsGenerative)
            {
                if (_advancedEmptyLabel != null) _advancedEmptyLabel.gameObject.SetActive(true);
                _advancedRowsContainer.SetActive(false);
                return;
            }

            // Normalize żeby arrays były gotowe (Generative wymaga trackSpacings + turnoutTypes)
            int turnoutCount = _selectedGenerator.ComputeTurnoutCount(_editParams.trackCount);
            _editParams.Normalize(turnoutCount);

            int spacingsCount = _editParams.trackSpacings?.Length ?? 0;
            int turnoutsCount = _editParams.turnoutTypes?.Length ?? 0;
            int rowCount = Mathf.Max(spacingsCount, turnoutsCount);

            if (rowCount == 0)
            {
                if (_advancedEmptyLabel != null) _advancedEmptyLabel.gameObject.SetActive(true);
                _advancedRowsContainer.SetActive(false);
                return;
            }

            if (_advancedEmptyLabel != null) _advancedEmptyLabel.gameObject.SetActive(false);
            _advancedRowsContainer.SetActive(true);

            _suppressEvents = true;
            try
            {
                for (int i = 0; i < rowCount; i++)
                {
                    int capturedIndex = i;

                    var row = CreateHorizontalRow(_advancedRowsContainer.transform);
                    SetLayoutPreferredHeight(row, 32);

                    var label = CreateLabel(row.transform, $"Para {i + 1}:", 11);
                    SetLayoutPreferredWidth(label.gameObject, 60);

                    // Spacing slider (gdy i < spacingsCount)
                    if (i < spacingsCount)
                    {
                        var spSlider = CreateSlider(row.transform,
                            SchemaParameters.MinSpacing, SchemaParameters.MaxSpacing,
                            _editParams.trackSpacings[i]);
                        spSlider.wholeNumbers = false;
                        spSlider.onValueChanged.AddListener(v => OnAdvancedSpacingChanged(capturedIndex, v));
                        _advancedSpacingSliders.Add(spSlider);

                        var spLabel = CreateLabel(row.transform,
                            _editParams.trackSpacings[i].ToString("F1"), 11);
                        SetLayoutPreferredWidth(spLabel.gameObject, 40);
                        _advancedSpacingValueLabels.Add(spLabel);
                    }
                    else
                    {
                        _advancedSpacingSliders.Add(null);
                        _advancedSpacingValueLabels.Add(null);
                    }

                    // Turnout type dropdown (gdy i < turnoutsCount)
                    if (i < turnoutsCount)
                    {
                        int defaultIdx = System.Array.IndexOf(TurnoutTypeOptions, _editParams.turnoutTypes[i]);
                        if (defaultIdx < 0) defaultIdx = 0;
                        var dropdown = CreateDropdown(row.transform,
                            new List<string>(TurnoutTypeOptions), defaultIdx);
                        dropdown.onValueChanged.AddListener(v => OnAdvancedTurnoutTypeChanged(capturedIndex, v));
                        _advancedTurnoutDropdowns.Add(dropdown);
                    }
                    else
                    {
                        _advancedTurnoutDropdowns.Add(null);
                    }
                }
            }
            finally
            {
                _suppressEvents = false;
            }
        }

        private void OnAdvancedSpacingChanged(int index, float value)
        {
            if (_suppressEvents || _editParams == null || _editParams.trackSpacings == null) return;
            if (index < 0 || index >= _editParams.trackSpacings.Length) return;

            float clamped = Mathf.Round(value * 10f) / 10f;  // step 0.1m
            clamped = SchemaParameters.ClampSpacing(clamped);
            _editParams.trackSpacings[index] = clamped;

            // Po edycji per-pair shorthand trackSpacing nie jest już źródłem prawdy.
            // 0f = "użyj trackSpacings array" wg konwencji SchemaParameters.
            _editParams.trackSpacing = 0f;

            if (index < _advancedSpacingValueLabels.Count && _advancedSpacingValueLabels[index] != null)
                _advancedSpacingValueLabels[index].text = clamped.ToString("F1");

            RegenerateLivePreviewIfActive();
        }

        private void OnAdvancedTurnoutTypeChanged(int index, int dropdownIndex)
        {
            if (_suppressEvents || _editParams == null || _editParams.turnoutTypes == null) return;
            if (index < 0 || index >= _editParams.turnoutTypes.Length) return;
            if (dropdownIndex < 0 || dropdownIndex >= TurnoutTypeOptions.Length) return;

            _editParams.turnoutTypes[index] = TurnoutTypeOptions[dropdownIndex];

            // Po edycji per-rozjazd shorthand turnoutType nie jest już źródłem prawdy.
            _editParams.turnoutType = "";

            RegenerateLivePreviewIfActive();
        }

        /// <summary>
        /// Live preview update — gdy gracz tweakuje parametr i placement jest aktywny,
        /// regeneruje geometrię w placerze. W MD-5 MVP wywołuje StartPlacement ponownie
        /// z nowym editDef. Polish (incremental update bez restartu) post-EA.
        /// </summary>
        private void RegenerateLivePreviewIfActive()
        {
            var placer = DepotSystem.Schemas.Placement.TurnoutSchemaPlacer.Instance;
            if (placer == null || !placer.IsActive) return;

            var editDef = CloneDefinition(_selectedDef);
            editDef.parameters = _editParams;
            placer.StartPlacement(editDef);
        }
    }
}
