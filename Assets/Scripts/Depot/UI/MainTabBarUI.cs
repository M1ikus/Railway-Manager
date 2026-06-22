using System;
using System.Collections.Generic;
using TMPro;
using RailwayManager.SharedUI;
using UnityEngine;
using UnityEngine.UI;

namespace DepotSystem
{
    /// <summary>
    /// Identyfikator glownej zakladki (nie zawsze mapuje sie 1:1 na ToolMode).
    /// </summary>
    public enum MainTab
    {
        Select,
        Map,
        Schedules,
        Circulations,
        Fleet,
        Staff,
        Finances,
        Workshops,
        Parts,
        Build
    }

    /// <summary>
    /// Lewy pionowy panel z glownymi zakladkami nawigacji.
    /// Zastepuje dolny toolbar jako punkt wejscia do narzedzi.
    ///
    /// Plik partial — root trzyma stan + lifecycle. Logika podzielona:
    /// <list type="bullet">
    ///   <item><c>MainTabBarUI.Build.cs</c> — procedural BuildUI + CreateHeaderCard + CreateTabButton</item>
    ///   <item><c>MainTabBarUI.Tabs.cs</c> — OnToolChanged/OnTabClicked + UpdateVisuals + UnlockTab + GetTabSummary</item>
    /// </list>
    /// Shared UI primitives (Layout, OptionButtonParts, PanelPrimitives — używane też przez
    /// BuildMenuUI, RoomBuildPanelUI, 4 sub-toolbary, PairSecondaryToolbarUI) wyciągnięte do
    /// osobnego pliku <c>DepotUIPrimitives.cs</c> (2026-05-13).
    /// </summary>
    public partial class MainTabBarUI : MonoBehaviour
    {
        [Header("Colors")]
        [SerializeField] private Color bgColor = default;
        [SerializeField] private Color activeColor = default;
        [SerializeField] private Color normalColor = default;
        [SerializeField] private Color lockedColor = default;

        public const float PanelWidth = 88f;

        private class TabButton
        {
            public MainTab tab;
            public Button button;
            public Image background;
            public Image accent;
            public TMP_Text iconText;
            public TMP_Text labelText;
            public bool unlocked;
            public bool hasSubmenu;
        }

        private readonly List<TabButton> tabButtons = new();
        private MainTab activeTab = MainTab.Select;
        private BuildMenuUI buildMenu;
        private FleetPanelUI fleetPanel;
        private TMP_Text headerTitleText;
        private TMP_Text headerStateText;

        public MainTab ActiveTab => activeTab;
        public event Action<MainTab> OnTabChanged;

        private static readonly (MainTab tab, string icon, string label, bool unlocked, bool hasSubmenu)[] tabDefs =
        {
            (MainTab.Select,       "SEL", "Wybierz",   true,  false),
            // MainTab.Map usunięty 2026-05-17 — redundant z "Mapa 2D" w TopBar (zakładka była unlocked=false, no-op).
            (MainTab.Schedules,    "RJ",  "Rozklady",  true,  false),
            (MainTab.Circulations, "OB",  "Obiegi",    true,  false),
            (MainTab.Fleet,        "TAB", "Tabor",     true,  false),
            (MainTab.Staff,        "HR",  "Personel",  true,  false),  // 2026-05-17: unlocked + handler przez UIIntent.OpenPersonnelPanel
            (MainTab.Finances,     "$",   "Finanse",   true,  false),
            (MainTab.Workshops,    "SER", "Warsztat",  true,  false),
            (MainTab.Parts,        "CZ",  "Magazyn",   true,  false),
            (MainTab.Build,        "BUD", "Budowanie", true,  true),
        };

        void Awake()
        {
            ApplyDefaultPalette();
            BuildUI();
        }

        void Start()
        {
            buildMenu = DepotServices.Get<BuildMenuUI>();
            fleetPanel = DepotServices.Get<FleetPanelUI>();

            if (DepotUIManager.Instance != null)
                DepotUIManager.Instance.OnToolChanged += OnToolChanged;

            UpdateVisuals();
        }

        void OnDestroy()
        {
            if (DepotUIManager.Instance != null)
                DepotUIManager.Instance.OnToolChanged -= OnToolChanged;
        }

        private void ApplyDefaultPalette()
        {
            if (bgColor == default)
                bgColor = UITheme.WithAlpha(UITheme.OverlayPanelStrong, 0.98f);
            if (activeColor == default)
                activeColor = UITheme.PrimaryAccent;
            if (normalColor == default)
                normalColor = UITheme.SecondarySurface;
            if (lockedColor == default)
                lockedColor = UITheme.WithAlpha(UITheme.Border, 0.72f);
        }
    }
}
