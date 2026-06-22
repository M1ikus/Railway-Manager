using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DepotSystem.Schemas.Generators;
using DepotSystem.Schemas.Placement;
using RailwayManager.Core;

namespace DepotSystem.Schemas.UI
{
    /// <summary>
    /// MD-5 — panel UI parametrów schematów.
    ///
    /// Plik partial — root trzyma stan + lifecycle + public API. Logika podzielona:
    /// <list type="bullet">
    ///   <item><c>SchemaPanelUI.Build.cs</c> — procedural BuildUI (Canvas + Header + List + Params)</item>
    ///   <item><c>SchemaPanelUI.List.cs</c> — refresh listy, search/filter, list items, selection</item>
    ///   <item><c>SchemaPanelUI.Params.cs</c> — handlery parameter change + live preview regen</item>
    ///   <item><c>SchemaPanelUI.Actions.cs</c> — snapshot create / place / save preset flows</item>
    ///   <item><c>SchemaPanelUI.Helpers.cs</c> — data clone/average + generic widget builders</item>
    /// </list>
    ///
    /// Layout:
    /// - Lewa strona (30%): scrollable lista wszystkich schematów z catalog (built-in + user)
    /// - Prawa strona (70%): panel parametrów wybranego schematu
    ///   - Header z nazwą + opisem
    ///   - Slider trackCount (per kategoria limity)
    ///   - Slider trackSpacing (4.0-6.0m, krok 0.1m)
    ///   - Dropdown turnoutType (R190 / R300 / Crossover_R190)
    ///   - Toggle mirror
    ///   - Toggle "Zaawansowane" (placeholder w MD-5 MVP — pełen advanced grid w future polish)
    ///   - Button "Stawiaj" → wywołuje TurnoutSchemaPlacer.StartPlacement
    ///
    /// Tweak parameters → tworzy lokalną kopię (edit copy) — original w catalog niezmienny.
    /// Klik na inną pozycję resetuje panel do parameters wybranej (decyzja A7 spec'a).
    ///
    /// MVP: procedural UI, Unity standard components, UITheme tylko dla kolorów paneli.
    /// Pełen UITheme styling + UIBuilders w M-UIPolish.
    /// </summary>
    public partial class SchemaPanelUI : MonoBehaviour
    {
        public static SchemaPanelUI Instance { get; private set; }

        [Header("Panel size")]
        public Vector2 panelSize = new Vector2(900, 600);
        public float listWidthRatio = 0.32f;

        // ── UI references (built procedurally) ──
        private Canvas _canvas;
        private GameObject _root;
        private GameObject _listContent;             // VerticalLayoutGroup wewnątrz ScrollView
        private GameObject _paramsContent;           // panel parametrów (right side)

        // Param widgets
        private TMP_Text _selectedHeaderLabel;
        private TMP_Text _selectedDescLabel;
        private Slider _trackCountSlider;
        private TMP_Text _trackCountValueLabel;
        private Slider _spacingSlider;
        private TMP_Text _spacingValueLabel;
        private TMP_Dropdown _turnoutTypeDropdown;
        private Toggle _mirrorToggle;
        private Toggle _advancedToggle;
        private GameObject _advancedSection;
        // Per-pair foldout (TD-002): empty label gdy schemat niezaznaczony, rows container
        // z N-1 wierszami (slider trackSpacings[i] + dropdown turnoutTypes[i]) gdy aktywny.
        private TMP_Text _advancedEmptyLabel;
        private GameObject _advancedRowsContainer;
        private readonly List<Slider> _advancedSpacingSliders = new();
        private readonly List<TMP_Text> _advancedSpacingValueLabels = new();
        private readonly List<TMP_Dropdown> _advancedTurnoutDropdowns = new();
        private Button _placeButton;
        private Button _saveAsButton;
        private Button _closeButton;

        // ── State ──
        private TurnoutSchemaDefinition _selectedDef;          // zaznaczony schemat z listy (immutable reference)
        private SchemaParameters _editParams;                  // lokalna kopia parameters do edycji
        private TurnoutSchemaCategory _selectedCategory;
        private ITurnoutSchemaGenerator _selectedGenerator;
        private List<Button> _listButtons = new();
        private bool _suppressEvents;                          // żeby SetValue na slider'ach nie triggerował OnChanged

        // ── MD-10 browse filter/search ──
        private TMP_InputField _searchInput;
        private TMP_Dropdown _categoryFilterDropdown;
        private TMP_Text _listSummaryLabel;
        private string _searchQuery = "";
        private int _categoryFilterIndex = 0;  // 0 = All, 1+ = enum index
        private static readonly string[] CategoryFilterOptions = new[] {
            "Wszystkie", "Ladder", "Throat", "Scissors", "Custom"
        };

        // ── List options dropdown ──
        private static readonly string[] TurnoutTypeOptions = new[] {
            SchemaTurnoutType.R190,
            SchemaTurnoutType.R300,
            SchemaTurnoutType.Crossover_R190
        };

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            BuildUI();
            Hide();  // domyślnie ukryty
        }

        void OnDestroy()
        {
            // Crash-hunt #5: odepnij handlery od SnapshotSelectionTool (singleton — przeżywa rebuild
            // panelu). Bez tego zniszczony panel zostaje subskrybowany → OnSelectionConfirmed odpala
            // na martwym MonoBehaviour → MissingReferenceException + wyciek.
            var tool = DepotSystem.Schemas.Selection.SnapshotSelectionTool.Instance;
            if (tool != null)
            {
                tool.OnSelectionConfirmed -= OnSnapshotSelectionConfirmed;
                tool.OnSelectionCancelled -= OnSnapshotSelectionCancelled;
            }

            if (Instance == this) Instance = null;
        }

        // ════════════════════════════════════════
        //  PUBLIC API
        // ════════════════════════════════════════

        public void Show()
        {
            if (!TurnoutSchemaCatalog.IsLoaded) TurnoutSchemaCatalog.LoadAll();
            RefreshList();

            // Anuluj aktywny placement w tle — gdy gracz otwiera panel ponownie po Stawiaj,
            // preview poprzedniego schematu znika żeby nie zasłaniał (gracz wybiera od nowa).
            var placer = TurnoutSchemaPlacer.Instance;
            if (placer != null && placer.IsActive)
            {
                placer.CancelPlacement();
            }

            if (_root != null) _root.SetActive(true);
        }

        public void Hide()
        {
            if (_root != null) _root.SetActive(false);
        }

        public void Toggle()
        {
            if (_root == null) return;
            if (_root.activeSelf) Hide(); else Show();
        }

        public bool IsVisible => _root != null && _root.activeSelf;
    }
}
