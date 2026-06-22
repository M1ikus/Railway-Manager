using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using RailwayManager.Core;
using RailwayManager.Core.Difficulty;
using RailwayManager.Core.GameRules;
using RailwayManager.SharedUI;
using RailwayManager.SharedUI.Localization;

namespace RailwayManager.GameCreator
{
    /// <summary>
    /// MB-1 Phase A (rozszerzenie M13-13): Difficulty selector + Custom editor + Game rules.
    ///
    /// Zmiany vs M13-13:
    /// - <see cref="MultiLineDropdown"/> z subtitle per preset (Łatwy / dla początkujących, etc.)
    /// - Slidery edytowalne dla NON-Custom presetów — zmiana auto-switchuje na Custom (zachowując
    ///   pozostałe wartości aktualnego presetu jako baseline)
    /// - StartBudget jako <see cref="TMP_InputField"/> (wartość PLN, nie multiplier — UX-friendly)
    /// - Per-modifier ranges (Breakdown/Events/DelayProp/Subsidy = 0.0–3.0 dla soft-off,
    ///   Cost/Salary/Hotel = 0.5–2.0, Demand/Tolerance = 0.5–1.5)
    /// - Grupowanie 10 mnożników po kategoriach (5 grup z headerami: Ekonomia/Pasażerowie/Tabor/Personel/Wydarzenia)
    /// - Tooltipy z "?" mini-button przy każdym sliderze/toggle (hover/click → popup)
    /// - Live preview panel "Po zatwierdzeniu otrzymasz:" z 4 metrykami (kasa start, awarie, pensje, dotacja)
    /// - 6 game rules toggles (3 z M13 + MaintenanceComponentDecay + EmployeeMorale + WeatherImpact)
    ///
    /// Phase B (osobno): wire-up modifierów i rules do gameplay code (Economy/Maintenance/etc.).
    ///
    /// Difficulty rozbita na partial files:
    /// - <c>GameCreatorUI.Difficulty.cs</c>           — pola, modifier defs, rule defs,
    ///                                                    PopulateDifficultySection +
    ///                                                    BuildDifficultyDropdownRow +
    ///                                                    ApplyDifficultyAndRulesOnStart (ten plik)
    /// - <c>GameCreatorUI.Difficulty.Modifiers.cs</c> — BuildModifierGroups + 10 sliders/input rows
    /// - <c>GameCreatorUI.Difficulty.Tooltip.cs</c>   — tooltip "?" subsystem
    /// - <c>GameCreatorUI.Difficulty.Presets.cs</c>   — preset switching + live preview
    /// - <c>GameCreatorUI.Difficulty.Rules.cs</c>     — 6 game rules toggles
    /// </summary>
    public partial class GameCreatorUI
    {
        // ── Constants (base values dla preview) ─────
        // M6.5-8: bumpnięte zgodnie z user requirement: budżet startowy musi pokrywać
        //   PEŁNOPRAWNY DEPOT (tory + sieć trakcyjna + pomieszczenia) + zakup CHOĆ JEDNEGO
        //   NOWEGO ZESPOŁU TRAKCYJNEGO zależnie od preset'u.
        //
        // Decomposition (z ConstructionConstants + new_models.json):
        //
        //   Pełnoprawny minimal depot:
        //     Tory zajezdniowe 1-2 km × 5M       =  5-10M
        //     Sieć trakcyjna 1-2 km × 1M         =  1-2M
        //     Hala P1 500 m² × 4k                =  2M
        //     Biuro 100 m² × 5k                  =  0.5M
        //     Magazyn 100 m² × 2.5k              =  0.25M
        //     Pomieszczenie socjalne 80 m² × 4k  =  0.32M
        //     Plac manewrowy + drogi 5k m²       =  2M
        //     Płot kolejowy 1km × 600            =  0.6M
        //     Działka peryferyjna 1 ha × 100     =  1M
        //   Razem minimal depot: ~13-18M zł
        //
        //   1 nowy zespół trakcyjny:
        //     Realistic: SA137 12M (najtańszy nowy)
        //     Hard: SA138 14M lub Griffin EU160 25M
        //     Normal: FLIRT_L4268 24M lub FLIRT_LM4268 35M
        //     Easy: FLIRT_ER160 58M (5-czł. premium)
        //
        //   Bufor 1-2 mies pensji (5 osób × ~55k/mies brutto+ZUS) = ~5-10M
        //
        // BUG-009: stałe przeniesione do `Core/Difficulty/DifficultyConstants.cs`
        // (CLAUDE.md zakazuje magic numbers w UI partial). Aliasy lokalne dla zwięzłości
        // odwołań w istniejących wzorach difficulty multipliers.
        private const long BASE_STARTING_BUDGET_PLN = DifficultyConstants.BaseStartingBudgetPln;
        private const int  BASE_DRIVER_SALARY_PLN   = DifficultyConstants.BaseDriverSalaryPln;
        private const int  BASE_BREAKDOWN_PER_KM    = DifficultyConstants.BaseBreakdownPerKm;

        // ── UI state ────────────────────────────────

        private MultiLineDropdown _ddDifficultyPreset;
        private TMP_InputField    _budgetInputField;          // StartBudget jako PLN input (special)
        private GameObject        _customSlidersContainer;
        private TextMeshProUGUI   _lblPresetDescription;

        // 9 modifier sliders (StartBudget jest w input field, nie slider — index 0 zostaje null)
        private readonly Slider[] _customSliders = new Slider[10];
        // Per-slider value labels (do refresh przy preset switch)
        private readonly TextMeshProUGUI[] _customSliderValueLabels = new TextMeshProUGUI[10];

        // Dictionary GameRule → Toggle
        private readonly Dictionary<GameRule, Toggle> _ruleToggles = new Dictionary<GameRule, Toggle>();

        // Live preview labels (4 metryki)
        private readonly TextMeshProUGUI[] _previewLabels = new TextMeshProUGUI[4];

        // Tooltip popup: 2026-05-14 migracja do centralnego SharedUI/TooltipManager (MUI-3).
        // Wcześniej własny ad-hoc system z _tooltipGO/_tooltipLbl — duplikat singletonu.

        // Suppress flag: set true gdy slider value się zmienia przez kod (preset switch),
        // żeby onValueChanged nie triggerował auto-switch na Custom.
        private bool _suppressAutoCustom;

        // ── Modifier defs (rozszerzone vs M13-13) ──

        private struct ModifierDef
        {
            public string i18nKey;       // label
            public string tooltipKey;    // for "?" button
            public string groupKey;      // section grouping
            public float  min, max;      // slider range
            public bool   isInputField;  // true tylko dla StartBudget (PLN input)
            public Func<DifficultyModifiers, float> getter;
            public Action<DifficultyModifiers, float> setter;
        }

        // 10 modifierów. Index = pozycja w DifficultyModifiers field order (zachowuję dla backwards compat).
        private static readonly ModifierDef[] _modifierDefs = new ModifierDef[]
        {
            // 0: StartBudget — input field PLN (special)
            new ModifierDef {
                i18nKey   = "difficulty.modifier.start_budget",
                tooltipKey = "difficulty.tooltip.start_budget",
                groupKey  = "difficulty.group.economy",
                min       = 0.5f, max = 3.0f,
                isInputField = true,
                getter    = m => m.StartBudgetMultiplier,
                setter    = (m, v) => m.StartBudgetMultiplier = v
            },
            // 1: OperationalCost
            new ModifierDef {
                i18nKey   = "difficulty.modifier.operational_cost",
                tooltipKey = "difficulty.tooltip.operational_cost",
                groupKey  = "difficulty.group.economy",
                min       = 0.5f, max = 2.0f,
                getter    = m => m.OperationalCostMultiplier,
                setter    = (m, v) => m.OperationalCostMultiplier = v
            },
            // 2: BreakdownChance — 0.0 = soft-off
            new ModifierDef {
                i18nKey   = "difficulty.modifier.breakdown_chance",
                tooltipKey = "difficulty.tooltip.breakdown_chance",
                groupKey  = "difficulty.group.fleet",
                min       = 0.0f, max = 3.0f,
                getter    = m => m.BreakdownChanceMultiplier,
                setter    = (m, v) => m.BreakdownChanceMultiplier = v
            },
            // 3: PassengerDemand
            new ModifierDef {
                i18nKey   = "difficulty.modifier.passenger_demand",
                tooltipKey = "difficulty.tooltip.passenger_demand",
                groupKey  = "difficulty.group.passengers",
                min       = 0.5f, max = 1.5f,
                getter    = m => m.PassengerDemandMultiplier,
                setter    = (m, v) => m.PassengerDemandMultiplier = v
            },
            // 4: Salary
            new ModifierDef {
                i18nKey   = "difficulty.modifier.salary",
                tooltipKey = "difficulty.tooltip.salary",
                groupKey  = "difficulty.group.personnel",
                min       = 0.5f, max = 2.0f,
                getter    = m => m.SalaryMultiplier,
                setter    = (m, v) => m.SalaryMultiplier = v
            },
            // 5: Subsidy — 0.0 = soft-off (no subsidies, używać razem z VoivodeshipSubsidies rule)
            new ModifierDef {
                i18nKey   = "difficulty.modifier.subsidy",
                tooltipKey = "difficulty.tooltip.subsidy",
                groupKey  = "difficulty.group.economy",
                min       = 0.0f, max = 2.0f,
                getter    = m => m.SubsidyMultiplier,
                setter    = (m, v) => m.SubsidyMultiplier = v
            },
            // 6: DelayPropagation — 0.0 = soft-off (opóźnienia nie kaskadują)
            new ModifierDef {
                i18nKey   = "difficulty.modifier.delay_propagation",
                tooltipKey = "difficulty.tooltip.delay_propagation",
                groupKey  = "difficulty.group.fleet",
                min       = 0.0f, max = 2.5f,
                getter    = m => m.DelayPropagationMultiplier,
                setter    = (m, v) => m.DelayPropagationMultiplier = v
            },
            // 7: EventFrequency — 0.0 = soft-off (brak random events)
            new ModifierDef {
                i18nKey   = "difficulty.modifier.event_frequency",
                tooltipKey = "difficulty.tooltip.event_frequency",
                groupKey  = "difficulty.group.events",
                min       = 0.0f, max = 3.0f,
                getter    = m => m.EventFrequencyMultiplier,
                setter    = (m, v) => m.EventFrequencyMultiplier = v
            },
            // 8: HotelCost
            new ModifierDef {
                i18nKey   = "difficulty.modifier.hotel_cost",
                tooltipKey = "difficulty.tooltip.hotel_cost",
                groupKey  = "difficulty.group.personnel",
                min       = 0.5f, max = 2.0f,
                getter    = m => m.HotelCostMultiplier,
                setter    = (m, v) => m.HotelCostMultiplier = v
            },
            // 9: TicketPriceTolerance
            new ModifierDef {
                i18nKey   = "difficulty.modifier.ticket_price_tolerance",
                tooltipKey = "difficulty.tooltip.ticket_price_tolerance",
                groupKey  = "difficulty.group.passengers",
                min       = 0.5f, max = 1.5f,
                getter    = m => m.TicketPriceToleranceMultiplier,
                setter    = (m, v) => m.TicketPriceToleranceMultiplier = v
            }
        };

        // 5 groups (kolejność wyświetlania)
        private static readonly string[] _modifierGroupOrder = new[]
        {
            "difficulty.group.economy",
            "difficulty.group.fleet",
            "difficulty.group.passengers",
            "difficulty.group.personnel",
            "difficulty.group.events"
        };

        // 6 game rules (3 z M13 + 3 nowe MB-1)
        private static readonly (GameRule rule, string i18nKey, string tooltipKey, bool defaultOn)[] _ruleDefs =
            new (GameRule, string, string, bool)[]
            {
                (GameRule.VehicleBreakdowns,        "game_rules.rule.vehicle_breakdowns",        "game_rules.tooltip.vehicle_breakdowns",        true),
                (GameRule.MaintenanceComponentDecay,"game_rules.rule.maintenance_decay",         "game_rules.tooltip.maintenance_decay",         true),
                (GameRule.RandomEvents,             "game_rules.rule.random_events",             "game_rules.tooltip.random_events",             true),
                (GameRule.WeatherImpact,            "game_rules.rule.weather_impact",            "game_rules.tooltip.weather_impact",            true),
                (GameRule.EmployeeMorale,           "game_rules.rule.employee_morale",           "game_rules.tooltip.employee_morale",           true),
                (GameRule.VoivodeshipSubsidies,     "game_rules.rule.voivodeship_subsidies",     "game_rules.tooltip.voivodeship_subsidies",     true),
                (GameRule.AssistantProactivity,     "game_rules.rule.assistant_proactivity",     "game_rules.tooltip.assistant_proactivity",     true)
            };

        // ═════════════════════════════════════════════
        //  DIFFICULTY SECTION
        // ═════════════════════════════════════════════

        private void PopulateDifficultySection()
        {
            // Section header
            BuildSectionHeaderCard("DiffHdrCard", "difficulty.section_title");

            // Info note
            BuildSectionInfoCard("DiffInfoCard", "difficulty.info_locked");

            // Dropdown preset selector (MultiLineDropdown z subtitle)
            BuildDifficultyDropdownRow();

            // Description (zmieniana per preset)
            var descCard = NewGO("DiffDescCard", _contentParent);
            UITheme.ApplySurface(descCard.AddComponent<Image>(), UITheme.WithAlpha(UITheme.SecondarySurface, 0.74f), UIShapePreset.Panel);
            descCard.AddComponent<LayoutElement>().preferredHeight = 68f;
            var descLayout = descCard.AddComponent<HorizontalLayoutGroup>();
            descLayout.padding = UITheme.Padding(UITheme.Spacing.Lg, UITheme.Spacing.Md);
            descLayout.childAlignment = TextAnchor.UpperLeft;
            descLayout.childControlWidth = true;
            descLayout.childControlHeight = true;
            descLayout.childForceExpandWidth = true;
            descLayout.childForceExpandHeight = true;

            var desc = MakeTMP("DiffDesc", descCard.transform);
            desc.text     = LocalizationService.Get(GetPresetDescKey(DifficultyPreset.Normal));
            desc.fontSize = 16;
            desc.color    = TextPrimary;
            desc.alignment = TextAlignmentOptions.TopLeft;
            // Override default NoWrap (z MakeTMP) — preset description bywa 2-liniowy w długich tłumaczeniach.
            desc.textWrappingMode = TMPro.TextWrappingModes.Normal;
            desc.overflowMode     = TMPro.TextOverflowModes.Overflow;
            var descLE = desc.gameObject.AddComponent<LayoutElement>();
            descLE.preferredHeight = 44f;
            descLE.flexibleHeight  = 0;
            _lblPresetDescription = desc;

            // Modifiers container (5 grup) — ZAWSZE widoczny (slidery active dla wszystkich presetów,
            // edycja triggeruje switch na Custom)
            _customSlidersContainer = NewGO("CustomSliders", _contentParent);
            UITheme.ApplySurface(_customSlidersContainer.AddComponent<Image>(), UITheme.WithAlpha(UITheme.PrimarySurface, 0.92f), UIShapePreset.Panel);
            var contVL = _customSlidersContainer.AddComponent<VerticalLayoutGroup>();
            contVL.spacing = UITheme.Spacing.Sm;
            contVL.padding = UITheme.Padding(UITheme.Spacing.Md);
            contVL.childControlWidth = true;
            contVL.childForceExpandWidth = true;
            contVL.childAlignment = TextAnchor.UpperLeft;
            var contFitter = _customSlidersContainer.AddComponent<ContentSizeFitter>();
            contFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Build groups
            BuildModifierGroups(_customSlidersContainer.transform);

            // Live preview panel (między difficulty a game rules — Opcja C placement)
            BuildLivePreviewPanel();

            // Initial state: pre-fill values dla Normal preset
            ApplyPresetToControls(DifficultyPreset.Normal);
        }

        private void BuildDifficultyDropdownRow()
        {
            var row = NewGO("DiffPresetRow", _contentParent);
            row.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 64f);
            UITheme.ApplySurface(row.AddComponent<Image>(), RowBg, UIShapePreset.Panel);
            var hl = row.AddComponent<HorizontalLayoutGroup>();
            hl.padding  = UITheme.Padding(UITheme.Spacing.Xl, UITheme.Spacing.Md);
            hl.spacing  = UITheme.Spacing.Xl;
            hl.childAlignment      = TextAnchor.MiddleLeft;
            hl.childControlWidth   = false;
            hl.childControlHeight  = false;
            hl.childForceExpandWidth = false;

            var lbl = MakeTMP("Lbl", row.transform);
            lbl.text     = LocalizationService.Get("difficulty.section_title");
            lbl.fontSize = 18;
            lbl.fontStyle = FontStyles.Bold;
            lbl.color    = TextPrimary;
            var lblLE = lbl.gameObject.AddComponent<LayoutElement>();
            lblLE.preferredWidth  = 220f;
            lblLE.preferredHeight = 36f;
            _activeLabels.Add((lbl, "difficulty.section_title"));

            var ddGO = NewGO("DD", row.transform);
            ddGO.GetComponent<RectTransform>().sizeDelta = new Vector2(280f, 44f);
            var ddImg = ddGO.AddComponent<Image>();
            UITheme.ApplySurface(ddImg, DropdownBg, UIShapePreset.Inset);

            _ddDifficultyPreset = ddGO.AddComponent<MultiLineDropdown>();
            _ddDifficultyPreset.targetGraphic = ddImg;
            _ddDifficultyPreset.itemHeight    = 56f;
            _ddDifficultyPreset.subtitleFontSize = 14f;
            // Subtitle ride na UITheme.SecondaryText (dev2 paleta) zamiast magic hex.
            _ddDifficultyPreset.subtitleColorHex = ColorUtility.ToHtmlStringRGB(UITheme.SecondaryText);
            _ddDifficultyPreset.ClearAllOptions();

            // Add 5 presets z subtitle
            _ddDifficultyPreset.AddOptionWithSubtitle(
                LocalizationService.Get("difficulty.preset.easy"),
                LocalizationService.Get("difficulty.preset_subtitle.easy"));
            _ddDifficultyPreset.AddOptionWithSubtitle(
                LocalizationService.Get("difficulty.preset.normal"),
                LocalizationService.Get("difficulty.preset_subtitle.normal"));
            _ddDifficultyPreset.AddOptionWithSubtitle(
                LocalizationService.Get("difficulty.preset.hard"),
                LocalizationService.Get("difficulty.preset_subtitle.hard"));
            _ddDifficultyPreset.AddOptionWithSubtitle(
                LocalizationService.Get("difficulty.preset.realistic"),
                LocalizationService.Get("difficulty.preset_subtitle.realistic"));
            _ddDifficultyPreset.AddOptionWithSubtitle(
                LocalizationService.Get("difficulty.preset.custom"),
                LocalizationService.Get("difficulty.preset_subtitle.custom"));

            _ddDifficultyPreset.value = 1; // Normal
            _ddDifficultyPreset.captionText.text = LocalizationService.Get("difficulty.preset.normal");
            ddGO.AddComponent<LayoutElement>().preferredWidth = 280f;

            _ddDifficultyPreset.onValueChanged.AddListener(OnDifficultyPresetChanged);
            _ddDifficultyPreset.onValueChanged.AddListener(_ => MarkDirty());
        }

        private TextMeshProUGUI BuildSectionHeaderCard(string name, string key)
        {
            var card = NewGO(name, _contentParent);
            UITheme.ApplySurface(card.AddComponent<Image>(), UITheme.WithAlpha(UITheme.SecondarySurface, 0.82f), UIShapePreset.Panel);
            card.AddComponent<LayoutElement>().preferredHeight = 46f;

            var layout = card.AddComponent<HorizontalLayoutGroup>();
            layout.padding = UITheme.Padding(UITheme.Spacing.Lg, UITheme.Spacing.Md);
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = false;
            layout.childControlHeight = false;

            var hdr = MakeTMP("Lbl", card.transform);
            hdr.text = LocalizationService.Get(key);
            hdr.fontSize = 20;
            hdr.fontStyle = FontStyles.Bold;
            hdr.color = Accent;
            hdr.gameObject.AddComponent<LayoutElement>().preferredHeight = 24f;
            _activeLabels.Add((hdr, key));
            return hdr;
        }

        private TextMeshProUGUI BuildSectionInfoCard(string name, string key)
        {
            var card = NewGO(name, _contentParent);
            UITheme.ApplySurface(card.AddComponent<Image>(), UITheme.WithAlpha(UITheme.TopBarInset, 0.92f), UIShapePreset.Inset);
            card.AddComponent<LayoutElement>().preferredHeight = 42f;

            var layout = card.AddComponent<HorizontalLayoutGroup>();
            layout.padding = UITheme.Padding(UITheme.Spacing.Lg, UITheme.Spacing.Md);
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;

            var info = MakeTMP("Lbl", card.transform);
            info.text = LocalizationService.Get(key);
            info.fontSize = 14;
            info.fontStyle = FontStyles.Italic;
            info.color = TextMuted;
            info.gameObject.AddComponent<LayoutElement>().preferredHeight = 20f;
            _activeLabels.Add((info, key));
            return info;
        }

        // ═════════════════════════════════════════════
        //  APPLY ON START
        // ═════════════════════════════════════════════

        private void ApplyDifficultyAndRulesOnStart()
        {
            var preset = _difficultyPresetValue;

            if (preset == DifficultyPreset.Custom)
            {
                DifficultyService.ApplyNewGameConfig(DifficultyPreset.Custom, _difficultyModifiersValue);
            }
            else
            {
                DifficultyService.ApplyNewGameConfig(preset);
            }

            // Game rules (6 toggle'i)
            var rulesConfig = new GameRulesConfig();
            foreach (var def in _ruleDefs)
            {
                bool enabled = _gameRuleValues.TryGetValue(def.rule, out var stored)
                    ? stored
                    : def.defaultOn;
                rulesConfig.Set(def.rule, enabled);
            }
            GameRulesService.ApplyNewGameConfig(rulesConfig);

            // MB-1 Phase B: ustaw GameState.Money zgodnie z difficulty StartBudgetMultiplier.
            // Po ApplyNewGameConfig (powyzej) DifficultyService.Modifiers ma juz wartosci wybranego presetu.
            float startBudgetMult = DifficultyService.Modifiers.StartBudgetMultiplier;
            GameState.Money = (long)(BASE_STARTING_BUDGET_PLN * startBudgetMult);

            int rulesOff = 0;
            foreach (var kv in rulesConfig.All) if (!kv.Value) rulesOff++;
            Log.Info($"[GameCreatorUI] New game start: difficulty={preset}, " +
                     $"rules: {_ruleDefs.Length} total, {rulesOff} OFF, " +
                     $"GameState.Money={GameState.Money} PLN ({startBudgetMult:F2}x base).");
        }
    }
}
