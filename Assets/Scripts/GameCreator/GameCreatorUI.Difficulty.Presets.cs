using UnityEngine;
using UnityEngine.UI;
using TMPro;
using RailwayManager.Core;
using RailwayManager.Core.Difficulty;
using RailwayManager.SharedUI;
using RailwayManager.SharedUI.Localization;

namespace RailwayManager.GameCreator
{
    public partial class GameCreatorUI
    {
        // ═══════════════════════════════════════════
        //  PRESET SWITCHING + LIVE PREVIEW PANEL
        // ═══════════════════════════════════════════

        private void OnDifficultyPresetChanged(int newValue)
        {
            var preset = DropdownIndexToPreset(newValue);
            _difficultyPresetValue = preset;

            // Caption pokazuje tylko nazwę bez subtitle (zamknięty dropdown jest wąski)
            if (_ddDifficultyPreset != null && _ddDifficultyPreset.captionText != null)
            {
                _ddDifficultyPreset.captionText.text = LocalizationService.Get(
                    GetPresetLocKey(preset));
            }

            // Description label refresh
            if (_lblPresetDescription != null)
                _lblPresetDescription.text = LocalizationService.Get(GetPresetDescKey(preset));

            // Pre-fill controls z preset values, ALE Custom celowo nie resetuje sliderów do 1.0:
            //   Scenariusz UX: gracz wybiera Hard, potem przełącza na Custom żeby tweakować.
            //   Resetowanie do 1.0 zmusiłoby go zaczynać od zera (gorsze niż "kontynuuj od Hard'a + zmień jeden").
            //   _difficultyModifiersValue dla Custom synchronizujemy z UI w CaptureActiveSectionState.
            if (preset != DifficultyPreset.Custom)
            {
                _difficultyModifiersValue = DifficultyPresetCatalog.Get(preset);
                ApplyPresetToControls(preset);
            }

            RefreshLivePreview();
        }

        /// <summary>Wypełnia slidery + input field wartościami z presetu. Suppress'uje
        /// auto-switch na Custom (bo to nasza programowa zmiana, nie user edit).</summary>
        private void ApplyPresetToControls(DifficultyPreset preset)
        {
            ApplyModifiersToControls(DifficultyPresetCatalog.Get(preset));

            // M11 AS-D2: default proaktywności asystenta per preset (Easy/Normal ON,
            // Hard/Realistic OFF). Gracz może ręcznie nadpisać po wyborze presetu —
            // jego zmiana trzyma się do kolejnej zmiany presetu (jak slidery).
            bool assistantOn = preset == DifficultyPreset.Easy || preset == DifficultyPreset.Normal;
            _gameRuleValues[Core.GameRules.GameRule.AssistantProactivity] = assistantOn;
            if (_ruleToggles.TryGetValue(Core.GameRules.GameRule.AssistantProactivity, out var assistantToggle)
                && assistantToggle != null)
            {
                assistantToggle.SetIsOnWithoutNotify(assistantOn);
            }
        }

        private void ApplyModifiersToControls(DifficultyModifiers modifiers)
        {
            if (modifiers == null) modifiers = new DifficultyModifiers();
            _suppressAutoCustom = true;
            try
            {
                for (int i = 0; i < _modifierDefs.Length; i++)
                {
                    float val = _modifierDefs[i].getter(modifiers);

                    if (_modifierDefs[i].isInputField && _budgetInputField != null)
                    {
                        long pln = (long)(BASE_STARTING_BUDGET_PLN * val);
                        _budgetInputField.SetTextWithoutNotify(pln.ToString());
                        if (_customSliderValueLabels[i] != null)
                            _customSliderValueLabels[i].text = pln.ToString();
                    }
                    else if (_customSliders[i] != null)
                    {
                        _customSliders[i].SetValueWithoutNotify(val);
                        if (_customSliderValueLabels[i] != null)
                            _customSliderValueLabels[i].text = $"{val:F2}x";
                    }
                }
            }
            finally
            {
                _suppressAutoCustom = false;
            }
        }

        private void ApplyStoredDifficultyToControls()
        {
            if (_ddDifficultyPreset == null) return;

            var preset = _difficultyPresetValue;
            _ddDifficultyPreset.SetValueWithoutNotify(PresetToDropdownIndex(preset));
            if (_ddDifficultyPreset.captionText != null)
                _ddDifficultyPreset.captionText.text = LocalizationService.Get(GetPresetLocKey(preset));
            if (_lblPresetDescription != null)
                _lblPresetDescription.text = LocalizationService.Get(GetPresetDescKey(preset));

            if (preset == DifficultyPreset.Custom)
                ApplyModifiersToControls(_difficultyModifiersValue);
            else
                ApplyPresetToControls(preset);

            RefreshLivePreview();
        }

        /// <summary>User zmienił wartość mnożnika — switchuj dropdown na Custom (bez resetowania
        /// pozostałych mnożników, bo OnDifficultyPresetChanged dla Custom skip'uje ApplyPresetToControls).
        /// Zmieniona wartość jest już w UI (slider/input zostały poruszone przez user).</summary>
        private void SwitchToCustomKeepingValues(int changedIdx, float newValue)
        {
            _difficultyPresetValue = DifficultyPreset.Custom;

            // Sprawdź czy już na Custom — jeśli tak, no-op
            if (_ddDifficultyPreset != null && _ddDifficultyPreset.value == 4) return;

            // SetValueWithoutNotify żeby nie wywołać OnDifficultyPresetChanged (które by zresetowało
            // wszystkie kontrole do wartości presetu Custom = wszystko 1.0).
            _ddDifficultyPreset?.SetValueWithoutNotify(4); // 4 = Custom idx

            // Manual update caption + description (bo SetValueWithoutNotify nie triggeruje listener)
            if (_ddDifficultyPreset != null && _ddDifficultyPreset.captionText != null)
            {
                _ddDifficultyPreset.captionText.text = LocalizationService.Get(
                    GetPresetLocKey(DifficultyPreset.Custom));
            }
            if (_lblPresetDescription != null)
                _lblPresetDescription.text = LocalizationService.Get(
                    GetPresetDescKey(DifficultyPreset.Custom));

            Log.Info($"[GameCreatorUI] Modifier {_modifierDefs[changedIdx].i18nKey} edited " +
                     $"(value={newValue:F2}) → preset switched to Custom.");
        }

        // `internal static` — testowane przez GameCreatorSmokeTest (roundtrip + key non-null).

        internal static DifficultyPreset DropdownIndexToPreset(int idx) => idx switch
        {
            0 => DifficultyPreset.Easy,
            1 => DifficultyPreset.Normal,
            2 => DifficultyPreset.Hard,
            3 => DifficultyPreset.Realistic,
            4 => DifficultyPreset.Custom,
            _ => DifficultyPreset.Normal
        };

        internal static int PresetToDropdownIndex(DifficultyPreset preset) => preset switch
        {
            DifficultyPreset.Easy => 0,
            DifficultyPreset.Normal => 1,
            DifficultyPreset.Hard => 2,
            DifficultyPreset.Realistic => 3,
            DifficultyPreset.Custom => 4,
            _ => 1
        };

        internal static string GetPresetLocKey(DifficultyPreset preset) => preset switch
        {
            DifficultyPreset.Easy      => "difficulty.preset.easy",
            DifficultyPreset.Normal    => "difficulty.preset.normal",
            DifficultyPreset.Hard      => "difficulty.preset.hard",
            DifficultyPreset.Realistic => "difficulty.preset.realistic",
            DifficultyPreset.Custom    => "difficulty.preset.custom",
            _                          => "difficulty.preset.normal"
        };

        internal static string GetPresetDescKey(DifficultyPreset preset) => preset switch
        {
            DifficultyPreset.Easy      => "difficulty.preset_desc.easy",
            DifficultyPreset.Normal    => "difficulty.preset_desc.normal",
            DifficultyPreset.Hard      => "difficulty.preset_desc.hard",
            DifficultyPreset.Realistic => "difficulty.preset_desc.realistic",
            DifficultyPreset.Custom    => "difficulty.preset_desc.custom",
            _                          => "difficulty.preset_desc.normal"
        };

        // ═════════════════════════════════════════════
        //  LIVE PREVIEW PANEL (Opcja C placement — między difficulty a game rules)
        // ═════════════════════════════════════════════

        private void BuildLivePreviewPanel()
        {
            var panel = NewGO("LivePreview", _contentParent);
            var panelLE = panel.AddComponent<LayoutElement>();
            panelLE.preferredHeight = 180f;
            UITheme.ApplySurface(panel.AddComponent<Image>(), UITheme.WithAlpha(UITheme.SecondarySurface, 0.76f), UIShapePreset.Panel);

            var vlg = panel.AddComponent<VerticalLayoutGroup>();
            vlg.padding = UITheme.Padding(UITheme.Spacing.Lg, UITheme.Spacing.Md);
            vlg.spacing = UITheme.Spacing.Sm;
            vlg.childControlWidth = true;
            vlg.childForceExpandWidth = true;

            // Header
            var hdr = MakeTMP("PreviewHdr", panel.transform);
            hdr.text     = LocalizationService.Get("difficulty.preview.title");
            hdr.fontSize = 16;
            hdr.fontStyle = FontStyles.Bold;
            hdr.color    = Accent;
            hdr.gameObject.AddComponent<LayoutElement>().preferredHeight = 22f;
            _activeLabels.Add((hdr, "difficulty.preview.title"));

            // 4 metrics labels
            for (int i = 0; i < 4; i++)
            {
                var metricCard = NewGO($"MetricCard{i}", panel.transform);
                UITheme.ApplySurface(metricCard.AddComponent<Image>(), UITheme.WithAlpha(UITheme.PrimarySurface, 0.9f), UIShapePreset.Inset);
                metricCard.AddComponent<LayoutElement>().preferredHeight = 26f;
                var metricLayout = metricCard.AddComponent<HorizontalLayoutGroup>();
                metricLayout.padding = UITheme.Padding(UITheme.Spacing.Md, UITheme.Spacing.Xs);
                metricLayout.childAlignment = TextAnchor.MiddleLeft;
                metricLayout.childControlWidth = true;
                metricLayout.childControlHeight = true;
                metricLayout.childForceExpandWidth = true;
                metricLayout.childForceExpandHeight = true;

                var lbl = MakeTMP($"Metric{i}", metricCard.transform);
                lbl.fontSize = 14;
                lbl.color    = TextPrimary;
                lbl.alignment = TextAlignmentOptions.Left;
                lbl.gameObject.AddComponent<LayoutElement>().preferredHeight = 18f;
                _previewLabels[i] = lbl;
            }
        }

        private void RefreshLivePreview()
        {
            if (_previewLabels[0] == null) return;

            // Czytamy aktualne wartości z UI
            float startBudget = ReadModifierValue(0); // StartBudget multiplier
            float salary      = ReadModifierValue(4);
            float breakdown   = ReadModifierValue(2);
            float subsidy     = ReadModifierValue(5);

            // Metric 1: Kasa startowa
            long pln = (long)(BASE_STARTING_BUDGET_PLN * startBudget);
            _previewLabels[0].text = string.Format(
                LocalizationService.Get("difficulty.preview.start_budget_format"),
                pln.ToString("N0"));

            // Metric 2: Pensja maszynisty
            int salaryPln = (int)(BASE_DRIVER_SALARY_PLN * salary);
            _previewLabels[1].text = string.Format(
                LocalizationService.Get("difficulty.preview.salary_format"),
                salaryPln.ToString("N0"));

            // Metric 3: Awarie taboru
            string breakdownText;
            if (breakdown <= 0.001f)
                breakdownText = LocalizationService.Get("difficulty.preview.breakdown_disabled");
            else
            {
                int kmPerBreakdown = (int)(BASE_BREAKDOWN_PER_KM / breakdown);
                breakdownText = string.Format(
                    LocalizationService.Get("difficulty.preview.breakdown_format"),
                    breakdown.ToString("F2"), kmPerBreakdown.ToString("N0"));
            }
            _previewLabels[2].text = breakdownText;

            // Metric 4: Dotacja wojewódzka
            string subsidyText;
            if (subsidy <= 0.001f)
                subsidyText = LocalizationService.Get("difficulty.preview.subsidy_disabled");
            else
            {
                int pct = (int)Mathf.Round((subsidy - 1f) * 100f);
                subsidyText = string.Format(
                    LocalizationService.Get("difficulty.preview.subsidy_format"),
                    pct >= 0 ? $"+{pct}" : pct.ToString());
            }
            _previewLabels[3].text = subsidyText;
        }

        private float ReadModifierValue(int idx)
        {
            // Index 0 = StartBudget z input field, reszta z slidera
            if (idx == 0 && _budgetInputField != null)
            {
                if (long.TryParse(_budgetInputField.text, out var pln) && pln > 0)
                    return Mathf.Clamp((float)pln / BASE_STARTING_BUDGET_PLN,
                        _modifierDefs[0].min, _modifierDefs[0].max);
                return 1f;
            }
            return _customSliders[idx] != null ? _customSliders[idx].value : 1f;
        }
    }
}
