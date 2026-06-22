using TMPro;
using UnityEngine;
using UnityEngine.UI;
using DepotSystem.Schemas.Generators;
using RailwayManager.Core;
using RailwayManager.SharedUI;

namespace DepotSystem.Schemas.UI
{
    /// <summary>
    /// Partial: list rendering (search + filter, list items, empty state, delete user
    /// custom) + selection handler który populuje params panel.
    /// </summary>
    public partial class SchemaPanelUI
    {
        // ════════════════════════════════════════
        //  LIST FILTER/SEARCH HANDLERS
        // ════════════════════════════════════════

        private void OnSearchChanged(string query)
        {
            _searchQuery = query?.Trim() ?? "";
            RefreshList();
        }

        private void OnCategoryFilterChanged(int index)
        {
            _categoryFilterIndex = index;
            RefreshList();
        }

        // ════════════════════════════════════════
        //  LIST REFRESH
        // ════════════════════════════════════════

        private void RefreshList()
        {
            if (_listContent == null) return;

            // Cleanup old
            foreach (var btn in _listButtons)
                if (btn != null) Destroy(btn.gameObject);
            _listButtons.Clear();

            // Filter + search
            string queryLower = _searchQuery.ToLowerInvariant();
            string filterCategory = _categoryFilterIndex > 0 && _categoryFilterIndex < CategoryFilterOptions.Length
                ? CategoryFilterOptions[_categoryFilterIndex]
                : null;

            int shownCount = 0;
            foreach (var def in TurnoutSchemaCatalog.AllSchemas)
            {
                // Filter by category
                if (filterCategory != null && def.category != filterCategory) continue;

                // Filter by search query (case-insensitive contains in name)
                if (!string.IsNullOrEmpty(queryLower)
                    && !def.name.ToLowerInvariant().Contains(queryLower)
                    && !def.id.ToLowerInvariant().Contains(queryLower))
                    continue;

                CreateListItem(def);
                shownCount++;
            }

            if (_listSummaryLabel != null)
            {
                string categoryText = _categoryFilterIndex > 0 && _categoryFilterIndex < CategoryFilterOptions.Length
                    ? CategoryFilterOptions[_categoryFilterIndex]
                    : "Wszystkie";
                _listSummaryLabel.text = $"{shownCount} wynikow  |  filtr: {categoryText}";
            }

            // Empty state
            if (shownCount == 0)
            {
                CreateEmptyStateLabel();
            }
        }

        private void CreateEmptyStateLabel()
        {
            var go = new GameObject("EmptyState");
            go.transform.SetParent(_listContent.transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0, 84);

            var bg = go.AddComponent<Image>();
            UITheme.ApplySurface(bg, UITheme.WithAlpha(UITheme.TopBarInset, 0.92f), UIShapePreset.Panel);

            var label = go.AddComponent<TextMeshProUGUI>();
            label.text = string.IsNullOrEmpty(_searchQuery)
                ? $"<color=#888888><i>Brak schematów dla filtra: {CategoryFilterOptions[_categoryFilterIndex]}</i></color>"
                : $"<color=#888888><i>Brak wyników dla \"{_searchQuery}\"</i></color>";
            label.fontSize = 12;
            label.color = Color.white;
            label.alignment = TextAlignmentOptions.Center;
            label.richText = true;
            label.margin = new Vector4(12f, 12f, 12f, 12f);
            label.textWrappingMode = TextWrappingModes.Normal;

            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 84;
        }

        private void CreateListItem(TurnoutSchemaDefinition def)
        {
            bool isUserCustom = TurnoutSchemaCatalog.FindUserCustom(def.id) != null;

            var go = new GameObject($"ListItem_{def.id}");
            go.transform.SetParent(_listContent.transform, false);

            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0, 72);

            var img = go.AddComponent<Image>();
            UITheme.ApplySurface(img, UITheme.SecondarySurface, UIShapePreset.Panel);

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.colors = UITheme.CreateColorBlock(
                UITheme.SecondarySurface,
                UITheme.RaisedSurface,
                UITheme.WithAlpha(UITheme.PrimaryAccent, 0.34f),
                UITheme.RaisedSurface,
                UITheme.WithAlpha(UITheme.Border, 0.55f));

            // Label (z miejscem na Delete button po prawej dla user custom)
            var labelGO = new GameObject("Label");
            labelGO.transform.SetParent(go.transform, false);
            var labelRT = labelGO.AddComponent<RectTransform>();
            labelRT.anchorMin = Vector2.zero;
            labelRT.anchorMax = Vector2.one;
            labelRT.offsetMin = new Vector2(12, 8);
            labelRT.offsetMax = new Vector2(isUserCustom ? -42 : -12, -8);

            var label = labelGO.AddComponent<TextMeshProUGUI>();
            string typeIcon = def.type == "snapshot" ? "SNAPSHOT" : "GENERATIVE";
            string sourceTag = isUserCustom ? "<color=#FFD970>[user]</color>" : "<color=#88AAFF>[built-in]</color>";
            label.text = $"<size=13><b>{def.name}</b></size>\n<size=10><color=#AAAAAA>{def.category} · {typeIcon} · {sourceTag}</color></size>\n<size=10><color=#7FD2FF>Kliknij, aby otworzyc parametry</color></size>";
            label.text = $"<size=13><b>{typeIcon}{def.name}</b></size>\n<size=10><color=#AAAAAA>{def.category} · {sourceTag}</color></size>";
            label.fontSize = 13;
            label.color = Color.white;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.text = $"<size=13><b>{def.name}</b></size>\n<size=10><color=#AAAAAA>{def.category} · {typeIcon} · {sourceTag}</color></size>\n<size=10><color=#7FD2FF>Kliknij, aby otworzyc parametry</color></size>";
            label.alignment = TextAlignmentOptions.TopLeft;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.richText = true;

            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 72;

            var capturedDef = def;
            btn.onClick.AddListener(() => OnSchemaSelected(capturedDef));

            // Delete button (tylko dla user custom)
            if (isUserCustom)
            {
                var delGO = new GameObject("DeleteBtn");
                delGO.transform.SetParent(go.transform, false);
                var delRT = delGO.AddComponent<RectTransform>();
                delRT.anchorMin = new Vector2(1, 0.5f);
                delRT.anchorMax = new Vector2(1, 0.5f);
                delRT.pivot = new Vector2(1, 0.5f);
                delRT.sizeDelta = new Vector2(30, 30);
                delRT.anchoredPosition = new Vector2(-6, 0);

                var delImg = delGO.AddComponent<Image>();
                UITheme.ApplySurface(delImg, UITheme.WithAlpha(UITheme.Danger, 0.24f), UIShapePreset.Button);

                var delBtn = delGO.AddComponent<Button>();
                delBtn.targetGraphic = delImg;

                var delLabelGO = new GameObject("Label");
                delLabelGO.transform.SetParent(delGO.transform, false);
                var delLabelRT = delLabelGO.AddComponent<RectTransform>();
                delLabelRT.anchorMin = Vector2.zero;
                delLabelRT.anchorMax = Vector2.one;
                delLabelRT.offsetMin = delLabelRT.offsetMax = Vector2.zero;
                var delLabel = delLabelGO.AddComponent<TextMeshProUGUI>();
                delLabel.text = "X";
                delLabel.fontSize = 14;
                delLabel.color = Color.white;
                delLabel.alignment = TextAlignmentOptions.Center;
                delLabel.fontStyle = FontStyles.Bold;

                string capturedId = def.id;
                delBtn.onClick.AddListener(() => OnDeleteUserSchema(capturedId));
            }

            _listButtons.Add(btn);
        }

        private void OnDeleteUserSchema(string id)
        {
            // MVP: bez confirmation dialog (polish: dorzucić "Czy na pewno?")
            bool ok = TurnoutSchemaCatalog.DeleteUser(id);
            if (ok)
            {
                Log.Info($"[SchemaPanelUI] Deleted user schema: '{id}'");
                if (_selectedDef != null && _selectedDef.id == id)
                {
                    _selectedDef = null;
                    _editParams = null;
                    if (_selectedHeaderLabel != null) _selectedHeaderLabel.text = "Wybierz schemat z listy";
                    if (_selectedDescLabel != null) _selectedDescLabel.text = "";
                }
                RefreshList();
            }
            else
            {
                Log.Error($"[SchemaPanelUI] Delete failed for '{id}'");
            }
        }

        // ════════════════════════════════════════
        //  SCHEMA SELECTION (populates params panel)
        // ════════════════════════════════════════

        private void OnSchemaSelected(TurnoutSchemaDefinition def)
        {
            _selectedDef = def;

            // Lokalna kopia parameters (Wariant A z spec'a — klik resetuje panel do parameters wybranej)
            if (def.IsGenerative && def.parameters != null)
            {
                _editParams = ClonePameters(def.parameters);
            }
            else
            {
                _editParams = new SchemaParameters();
            }

            _selectedCategory = def.ParseCategory();
            _selectedGenerator = TurnoutSchemaGeneratorRegistry.Get(_selectedCategory);

            // Update header
            if (_selectedHeaderLabel != null) _selectedHeaderLabel.text = def.name;
            if (_selectedDescLabel != null) _selectedDescLabel.text = string.IsNullOrEmpty(def.description) ? def.category : $"{def.category} — {def.description}";

            // Apply parameters to widgets (suppress events)
            _suppressEvents = true;
            try
            {
                // TrackCount slider — limity per kategoria
                ApplyTrackCountLimits();
                if (_trackCountSlider != null) _trackCountSlider.SetValueWithoutNotify(_editParams.trackCount);
                if (_trackCountValueLabel != null) _trackCountValueLabel.text = _editParams.trackCount.ToString();

                // Spacing slider
                float avgSpacing = AverageSpacing(_editParams);
                if (_spacingSlider != null) _spacingSlider.SetValueWithoutNotify(avgSpacing);
                if (_spacingValueLabel != null) _spacingValueLabel.text = avgSpacing.ToString("F1");

                // TurnoutType dropdown
                int idx = System.Array.IndexOf(TurnoutTypeOptions, GetCurrentTurnoutType(_editParams));
                if (idx < 0) idx = 0;
                if (_turnoutTypeDropdown != null) _turnoutTypeDropdown.SetValueWithoutNotify(idx);

                // Mirror toggle
                if (_mirrorToggle != null) _mirrorToggle.SetIsOnWithoutNotify(_editParams.mirror);

                // Advanced collapsed
                if (_advancedToggle != null) _advancedToggle.SetIsOnWithoutNotify(false);
                if (_advancedSection != null) _advancedSection.SetActive(false);
            }
            finally
            {
                _suppressEvents = false;
            }

            // TD-002: rebuild per-pair rows dla nowego schematu (nawet jeśli foldout zamknięty
            // — gdy gracz otworzy będą gotowe). Rebuild i tak zrobi się też w OnAdvancedToggled.
            RebuildAdvancedRows();

            Log.Info($"[SchemaPanelUI] Selected: '{def.name}' (category={def.category}, type={def.type})");
        }
    }
}
