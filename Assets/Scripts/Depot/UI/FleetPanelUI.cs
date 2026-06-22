using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using RailwayManager.Fleet;
using RailwayManager.SharedUI;
using RailwayManager.SharedUI.Localization;

namespace DepotSystem
{
    // Data models, enums, and catalogs moved to RailwayManager.Fleet namespace
    // (Assets/Scripts/Fleet/Data/ and Assets/Scripts/Fleet/Catalogs/)

    /// <summary>
    /// Prawie-peĹ‚noekranowy panel zarzÄ…dzania taborem.
    /// Trzy zakĹ‚adki wewnÄ™trzne: MĂłj tabor, SkĹ‚ady, Kup tabor.
    /// Dane z RailwayManager.Fleet.FleetService i FleetCatalog.
    /// </summary>
    public partial class FleetPanelUI : MonoBehaviour
    {
        // â”€â”€ inner tab enum â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private enum FleetTab { MyFleet, Consists, BuyFleet }

        // â”€â”€ colours â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private static readonly Color PanelBg       = UITheme.WithAlpha(UITheme.OverlayPanelStrong, 0.96f);
        private static readonly Color TopBarBg      = UITheme.WithAlpha(UITheme.TopBarInset, 0.98f);
        private static readonly Color TabBarBg      = UITheme.WithAlpha(UITheme.PrimarySurface, 0.96f);
        private static readonly Color TabActive     = UITheme.PrimaryAccent;
        private static readonly Color TabNormal     = UITheme.SecondarySurface;
        private static readonly Color FilterBarBg   = UITheme.WithAlpha(UITheme.PrimarySurface, 0.94f);
        private static readonly Color InputBg       = UITheme.TopBarInset;
        private static readonly Color RowBg         = UITheme.WithAlpha(UITheme.SecondarySurface, 0.92f);
        private static readonly Color RowHover      = UITheme.RaisedSurface;
        private static readonly Color HeaderRowBg   = UITheme.WithAlpha(UITheme.TopBarInset, 0.98f);
        private static readonly Color TextPrimary   = UITheme.PrimaryText;
        private static readonly Color TextMuted     = UITheme.SecondaryText;
        private static readonly Color TextAccent    = UITheme.PrimaryAccent;
        private static readonly Color BtnSecondary  = UITheme.SecondarySurface;

        // Status colors
        private static readonly Color StatusMovingMap   = new(0.30f, 0.85f, 0.40f, 1f);  // zielony
        private static readonly Color StatusStoppedMap  = new(0.40f, 0.60f, 1.00f, 1f);  // niebieski
        private static readonly Color StatusStoppedDepot= new(0.95f, 0.80f, 0.20f, 1f);  // zolty
        private static readonly Color StatusMovingDepot = new(1.00f, 0.60f, 0.20f, 1f);  // pomaranczowy
        private static readonly Color StatusRepair      = new(0.90f, 0.30f, 0.30f, 1f);  // czerwony
        private static readonly Color StatusOOS         = new(0.40f, 0.40f, 0.45f, 1f);  // szary

        // Condition bar colors — semantyczne tokeny UITheme (MUI re-skin: spójne, miększe tony)
        private static readonly Color CondGood   = UITheme.Success;
        private static readonly Color CondMed    = UITheme.Warning;
        private static readonly Color CondBad    = UITheme.Danger;

        // Type thumbnail colors → VehicleChipStyle (TD-032 DRY)

        // â”€â”€ layout constants â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private const float TopBarH    = 70f;
        private const float TabBarH    = 50f;
        private const float FilterBarH = 50f;
        private const float RowH       = 56f; // MUI re-skin: gęściej (było 68) — mniej pustej przestrzeni
        private const float ConsistRowH = 80f;

        // Column widths (shared between header and data rows)
        private const float COL_THUMB   = 80f;
        private const float COL_NAME    = 180f;
        private const float COL_STATUS  = 160f;
        private const float COL_TASK    = 280f;
        private const float COL_CONSIST = 140f;
        private const float COL_MILEAGE    = 120f;
        private const float COL_SEATS      = 90f;
        private const float COL_INSPECTION = 150f;
        private const float COL_COND       = 120f;
        private const float COL_LOCATION   = 140f;
        private const float COL_PRICE      = 120f;

        // Inspection column colors
        private static readonly Color InspOk      = new(0.55f, 0.55f, 0.60f, 1f);  // daleko â€” szary
        private static readonly Color InspWarn    = new(0.95f, 0.80f, 0.20f, 1f);  // <5000 km â€” ĹĽĂłĹ‚ty
        private static readonly Color InspUrgent  = new(0.90f, 0.30f, 0.30f, 1f);  // <1000 km â€” czerwony

        // Passenger type set â€” used to decide if seats column is visible
        private static readonly HashSet<FleetVehicleType> PassengerTypes = new()
        {
            FleetVehicleType.EMU,
            FleetVehicleType.DMU,
            FleetVehicleType.PassengerCar
        };

        /// <summary>Czy kolumna "Miejsca" powinna byÄ‡ widoczna (filtr zawiera typ z miejscami pasaĹĽerskimi).</summary>
        private bool ShowSeatsColumn =>
            _activeTypeFilters.Count == 0 || // "Wszystkie" â€” nie pokazuj (bo Lok teĹĽ jest)
            _activeTypeFilters.Overlaps(PassengerTypes);

        // Ale ukryj gdy TYLKO lokomotywy sÄ… zaznaczone
        private bool ShouldShowSeatsColumn()
        {
            // "Wszystkie" (brak filtrĂłw) â€” pokazuj
            if (_activeTypeFilters.Count == 0) return true;
            // Ukryj tylko gdy zaznaczone WYĹÄ„CZNIE lokomotywy
            return _activeTypeFilters.Overlaps(PassengerTypes);
        }

        // Market colors
        private static readonly Color PriceColor    = new(0.40f, 0.85f, 0.40f, 1f); // zielony â€” cena
        private static readonly Color BtnBuy        = new(0.20f, 0.55f, 0.20f, 1f); // zielony przycisk
        private static readonly Color BtnBuyHover   = new(0.25f, 0.65f, 0.25f, 1f);
        private static readonly Color MarketSubTabBg = UITheme.WithAlpha(UITheme.PrimarySurface, 0.94f);
        private const float MarketSubTabH = 40f;

        // Sort arrow colors
        private static readonly Color ArrowActive   = new(1.00f, 0.85f, 0.20f, 1f); // ĹĽĂłĹ‚ty â€” aktywne
        private static readonly Color ArrowInactive  = new(0.35f, 0.35f, 0.40f, 1f); // ciemny â€” nieaktywne

        /// <summary>
        /// Czy kolumna "PrzeglÄ…d" jest widoczna (wyĹĽsze poziomy trudnoĹ›ci).
        /// Ustawiane zewnÄ™trznie lub na podstawie GameState.
        /// </summary>
        public bool ShowInspectionColumn => RailwayManager.Core.Difficulty.DifficultyService.ShowInspectionColumnHint;

        // â”€â”€ references â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private GameObject _root;
        private FleetTab _activeTab = FleetTab.MyFleet;

        // Top bar
        private TextMeshProUGUI _titleLbl;
        private TextMeshProUGUI _counterLbl;

        // Tab buttons
        private readonly List<(FleetTab tab, Image bg, TextMeshProUGUI lbl)> _tabButtons = new();

        // Filter bar
        private GameObject _filterBarGO;
        private TMP_InputField _searchField;
        private string _searchText = "";
        private readonly HashSet<FleetVehicleType> _activeTypeFilters = new(); // empty = all
        private readonly List<(FleetVehicleType? type, Image bg, TextMeshProUGUI lbl)> _typeFilterButtons = new();

        // Column header (sort state)
        private GameObject _columnHeaderGO;
        private string _sortField = "series";
        private bool _sortAscending = true;
        private readonly List<(string field, TextMeshProUGUI arrowUp, TextMeshProUGUI arrowDown)> _sortArrows = new();

        // Scroll area
        private Transform _contentParent;
        private RectTransform _contentRT;
        private GameObject _viewportGO;
        private GameObject _scrollRectGO;
        private TextMeshProUGUI _emptyLbl;

        // Placeholder for "Kup tabor" â€” nowe
        private enum MarketSubTab { Used, NewConfigurator }
        private MarketSubTab _marketSubTab = MarketSubTab.Used;
        private GameObject _marketSubTabBarGO;
        private readonly List<(MarketSubTab tab, Image bg, TextMeshProUGUI lbl)> _marketSubTabButtons = new();
        private GameObject _marketFilterBarGO;
        private TMP_InputField _marketSearchField;
        private string _marketSearchText = "";
        private string _marketSortField = "price";
        private bool _marketSortAscending = true;
        private readonly HashSet<FleetVehicleType> _marketTypeFilters = new();
        private readonly List<(FleetVehicleType? type, Image bg, TextMeshProUGUI lbl)> _marketTypeFilterButtons = new();
        private readonly List<(string field, TextMeshProUGUI arrowUp, TextMeshProUGUI arrowDown)> _marketSortArrows = new();
        private GameObject _configuratorGO;
        private GameObject _marketDetailPopupGO;
        private FleetVehicleData _currentOwnedDetailVehicle;
        private FleetMarketVehicle _currentMarketDetailVehicle;
        private Transform _configRightContent;
        private TextMeshProUGUI _configTotalPriceLbl;
        private TextMeshProUGUI _configQuantityLbl;
        private TextMeshProUGUI _configTimeLbl;
        private readonly List<GameObject> _configQuantityWidgets = new(); // hidden until a wagon/family is selected
        private int _configQuantity = 1;
        private Transform _configRightContentRoot; // always points to the real scrollable content

        // Cart UI state (actual list in FleetService.Cart)
        private GameObject _cartPopupGO;
        private TextMeshProUGUI _cartCountLbl; // badge on cart icon
        private GameObject _cartButtonGO;
        private static readonly Color CartBadgeColor = new(0.90f, 0.30f, 0.30f, 1f);

        // Shortcuts to FleetService lists (BUG-035: read-only views; mutate via FleetService API)
        private IReadOnlyList<FleetVehicleData> _vehicles => FleetService.OwnedVehicles;
        private IReadOnlyList<FleetConsistData> _consists => FleetService.Consists;
        private IReadOnlyList<FleetMarketVehicle> _marketVehicles => FleetService.MarketVehicles;
        private IReadOnlyList<CartItem> _cart => FleetService.Cart;

        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        //  PUBLIC API
        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        public bool IsVisible => _root != null && _root.activeSelf;
        public bool IsOpen => IsVisible;

        void Awake()
        {
            // M-Windows P3: static ref do otwierania okna pojazdu zewsząd (też z Mapy — patrz
            // FleetPanelUI.OpenVehicleWindow). FleetPanelUI żyje w scenie Depot (ładowanej raz),
            // więc Instance jest stabilny na czas sesji; metoda działa nawet gdy panel inactive.
            Instance = this;

            // Ustaw wĹ‚asny RectTransform ĹĽeby wypeĹ‚niÄ‡ canvas
            // (z marginesem na lewy toolbar 70px i top bar 40px)
            var selfRT = GetComponent<RectTransform>();
            if (selfRT == null) selfRT = gameObject.AddComponent<RectTransform>();
            selfRT.anchorMin = Vector2.zero;
            selfRT.anchorMax = Vector2.one;
            selfRT.offsetMin = new Vector2(MainTabBarUI.PanelWidth, 0f);
            // MUI re-skin: -52 (wysokość górnego paska; spójnie z nav railem / RoomBuildPanel) — panel nie nachodzi na TopBar
            selfRT.offsetMax = new Vector2(0f, -52f);

            // Data now comes from FleetService (initialized by bootstrap)
            if (!FleetService.IsInitialized)
                FleetService.Initialize();

            BuildUI();
            _root.SetActive(false);
        }

        // === i18n hot-reload (M13-4h-1) ===

        void OnEnable()
        {
            LocalizationService.OnLanguageChanged += OnLocaleChanged;
        }

        void OnDisable()
        {
            LocalizationService.OnLanguageChanged -= OnLocaleChanged;
        }

        private void OnLocaleChanged()
        {
            // Title + counter (counter ma format args, UpdateCounter robi LocalizationService.Get)
            if (_titleLbl != null) _titleLbl.text = LocalizationService.Get("fleet.title");
            UpdateCounter();

            // Tab labels (refs zachowane w _tabButtons)
            foreach (var (tab, _, lbl) in _tabButtons)
            {
                if (lbl == null) continue;
                lbl.text = tab switch
                {
                    FleetTab.MyFleet  => LocalizationService.Get("fleet.tabs.my_fleet"),
                    FleetTab.Consists => LocalizationService.Get("fleet.tabs.consists"),
                    FleetTab.BuyFleet => LocalizationService.Get("fleet.tabs.buy_fleet"),
                    _                 => lbl.text
                };
            }

            // Repopulate active tab content ĹĽeby refresh dynamic labels (status, vehicle types
            // w rows, empty messages, consist format itd.). Filter bar + column headers sÄ…
            // built raz w BuildUI â€” pozostajÄ… w starym jÄ™zyku do nastÄ™pnego otwarcia panelu
            // (akceptowalny edge case pre-EA, fix w M13-4h-5 jeĹ›li bÄ™dzie potrzeba).
            if (IsVisible)
            {
                if (_activeTab == FleetTab.MyFleet) PopulateMyFleet();
                else if (_activeTab == FleetTab.BuyFleet) PopulateContent();
                else if (_activeTab == FleetTab.Consists) PopulateConsists();
            }
        }

        public void Show()
        {
            _root.SetActive(true);
            // Pełnoekranowy panel → chowa HUD świata (minimapa + alerty utrzymania sprawdzają tę flagę) i gate'uje kamerę.
            RailwayManager.Core.SceneController.FullscreenOverlayOpen = true;
            _searchText = "";
            if (_searchField != null) _searchField.text = "";
            SwitchTab(FleetTab.MyFleet);
        }

        public void Hide()
        {
            _root.SetActive(false);
            RailwayManager.Core.SceneController.FullscreenOverlayOpen = false;
        }

        /// <summary>
        /// ObsĹ‚uga ESC â€” zamyka popup/pod-zakĹ‚adki od wewnÄ…trz na zewnÄ…trz.
        /// Zwraca true jeĹ›li coĹ› zamknÄ…Ĺ‚ (popup), false jeĹ›li panel powinien siÄ™ zamknÄ…Ä‡.
        /// </summary>
        public bool HandleEscape()
        {
            // 1. Zamknij popup koszyka
            if (_cartPopupGO != null)
            {
                CloseCartPopup();
                return true;
            }

            // 2. Zamknij popup szczegĂłĹ‚Ăłw rynku
            if (_marketDetailPopupGO != null)
            {
                CloseMarketDetailPopup();
                return true;
            }
            // 2. Panel sam siÄ™ zamknie (return false â†’ caller zrobi Hide)
            return false;
        }

        // Scaffold glownego widoku wyciagniety do FleetPanelUI.Layout.cs
        // BuildCartButton wyciagniete do FleetPanelUI.Cart.cs

        private void SwitchTab(FleetTab tab)
        {
            _activeTab = tab;

            // Update tab visuals
            foreach (var (t, bg, lbl) in _tabButtons)
            {
                bool isActive = t == _activeTab;
                bg.color = isActive ? TabActive : TabNormal;
                if (lbl != null)
                    lbl.color = isActive ? UITheme.InverseText : TextPrimary;

                var button = bg != null ? bg.GetComponent<Button>() : null;
                if (button != null)
                {
                    button.colors = UITheme.CreateColorBlock(
                        isActive ? TabActive : TabNormal,
                        isActive ? UITheme.PrimaryAccentHover : UITheme.RaisedSurface,
                        isActive ? UITheme.Darken(UITheme.PrimaryAccentHover, 0.18f) : UITheme.Border,
                        isActive ? TabActive : TabNormal,
                        UITheme.WithAlpha(UITheme.Border, 0.55f));
                }
            }

            bool isMyFleet  = tab == FleetTab.MyFleet;
            bool isBuyFleet = tab == FleetTab.BuyFleet;
            bool isConsists = tab == FleetTab.Consists;

            // Show/hide filter bars and cart button
            _filterBarGO.SetActive(isMyFleet);
            if (_cartButtonGO != null) _cartButtonGO.SetActive(isBuyFleet);
            _marketSubTabBarGO.SetActive(isBuyFleet);
            _marketFilterBarGO.SetActive(isBuyFleet && _marketSubTab == MarketSubTab.Used);
            _configuratorGO.SetActive(isBuyFleet && _marketSubTab == MarketSubTab.NewConfigurator);

            // Scroll area â€” visible for MyFleet, Consists, and BuyFleet (Used sub-tab)
            bool showScroll = !isBuyFleet || _marketSubTab == MarketSubTab.Used;
            _viewportGO.SetActive(showScroll);
            _scrollRectGO.SetActive(showScroll);

            // Calculate top offset
            float topOffset = TopBarH + TabBarH;
            if (isMyFleet) topOffset += FilterBarH;
            if (isBuyFleet) topOffset += MarketSubTabH;
            if (isBuyFleet && _marketSubTab == MarketSubTab.Used) topOffset += FilterBarH;

            var vpRT = _viewportGO.GetComponent<RectTransform>();
            vpRT.offsetMax = new Vector2(0f, -topOffset);
            var srRT = _scrollRectGO.GetComponent<RectTransform>();
            srRT.offsetMax = new Vector2(0f, -topOffset);

            PopulateContent();
        }

        // â”€â”€ FILTER BAR + COLUMN HEADER wyciagniete do FleetPanelUI.MyFleet.cs â”€â”€

        // â”€â”€ SCROLL AREA â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€


        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        //  POPULATE CONTENT (dispatcher)
        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private void PopulateContent()
        {
            // Clear existing
            foreach (Transform ch in _contentParent)
                Destroy(ch.gameObject);

            _emptyLbl.gameObject.SetActive(false);

            switch (_activeTab)
            {
                case FleetTab.MyFleet:
                    PopulateMyFleet();
                    break;
                case FleetTab.Consists:
                    PopulateConsists();
                    break;
                case FleetTab.BuyFleet:
                    if (_marketSubTab == MarketSubTab.Used)
                        PopulateMarket();
                    break;
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(_contentRT);
        }

        // Filter bar + column header wyciagniete do FleetPanelUI.MyFleet.cs
        // Configurator wyciagniete do FleetPanelUI.Configurator.cs
        // Seat layout generator wyciagniete do FleetPanelUI.SeatLayout.cs
        // Consists tab wyciagniete do FleetPanelUI.Consists.cs
        // Helpery wspolne wyciagniete do FleetPanelUI.Helpers.cs
        // Inspection helpers wyciagniete do FleetPanelUI.Inspection.cs
        // Seat breakdown + inspection collapsible wyciagniete do FleetPanelUI.DetailPopup.cs

    }

    // HoverImageColor wyciagniety do HoverImageColor.cs
}
