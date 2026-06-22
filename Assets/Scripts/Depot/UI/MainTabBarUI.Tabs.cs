using TMPro;
using UnityEngine;
using RailwayManager.SharedUI;

namespace DepotSystem
{
    /// <summary>
    /// Partial: tab interaction (klik → dispatch UIIntent / show panel / change tool),
    /// state synchronizacja z DepotUIManager.OnToolChanged, visual update + summary text.
    /// </summary>
    public partial class MainTabBarUI
    {
        private void OnToolChanged(ToolMode mode)
        {
            if (mode == ToolMode.Select && activeTab != MainTab.Select && activeTab != MainTab.Build)
            {
                activeTab = MainTab.Select;
                OnTabChanged?.Invoke(activeTab); // event przy KAŻDEJ zmianie (konsument: DepotExpandPanelUI)
                HideBuildMenu();
                UpdateVisuals();
            }
        }

        private void OnTabClicked(TabButton tb)
        {
            if (!tb.unlocked)
                return;

            if (tb.tab == activeTab && tb.tab != MainTab.Select)
            {
                activeTab = MainTab.Select;
                OnTabChanged?.Invoke(activeTab); // toggle-off też jest zmianą zakładki
                CloseAllPanels();
                UpdateVisuals();
                return;
            }

            CloseAllPanels();

            activeTab = tb.tab;
            OnTabChanged?.Invoke(activeTab);

            if (tb.tab == MainTab.Select)
            {
                if (DepotUIManager.Instance != null)
                    DepotUIManager.Instance.CurrentTool = ToolMode.Select;
            }
            else if (tb.tab == MainTab.Build)
            {
                ShowBuildMenu();
            }
            else if (tb.tab == MainTab.Fleet)
            {
                if (fleetPanel != null)
                    fleetPanel.Show();
            }
            else if (tb.tab == MainTab.Schedules)
            {
                RailwayManager.Core.UIIntents.Emit(RailwayManager.Core.UIIntent.OpenScheduleCreator);
            }
            else if (tb.tab == MainTab.Circulations)
            {
                RailwayManager.Core.UIIntents.Emit(RailwayManager.Core.UIIntent.OpenCirculationList);
            }
            else if (tb.tab == MainTab.Finances)
            {
                RailwayManager.Core.UIIntents.Emit(RailwayManager.Core.UIIntent.OpenFinancesPanel);
            }
            else if (tb.tab == MainTab.Workshops)
            {
                RailwayManager.Core.UIIntents.Emit(RailwayManager.Core.UIIntent.OpenWorkshopsPanel);
            }
            else if (tb.tab == MainTab.Parts)
            {
                RailwayManager.Core.UIIntents.Emit(RailwayManager.Core.UIIntent.OpenPartsPanel);
            }
            else if (tb.tab == MainTab.Staff)
            {
                // Depot asmdef NIE referuje Personnel (hierarchia asmdef) — używamy UIIntent bus.
                // Subskrybent: PersonnelToolBootstrap w Personnel asmdef.
                RailwayManager.Core.UIIntents.Emit(RailwayManager.Core.UIIntent.OpenPersonnelPanel);
            }

            UpdateVisuals();
        }

        /// <summary>Zamyka wszystkie otwarte panele (fleet, build menu, timetable, tool mode).</summary>
        private void CloseAllPanels()
        {
            HideBuildMenu();
            if (fleetPanel != null && fleetPanel.IsVisible)
                fleetPanel.Hide();
            if (DepotUIManager.Instance != null &&
                DepotUIManager.Instance.CurrentTool != ToolMode.Select)
                DepotUIManager.Instance.CurrentTool = ToolMode.Select;

            RailwayManager.Core.SceneController.TimetablePopupOpen = false;
            // Schedule/Circulation są one-shot intents — nie wymagają Close*. Toggle panels
            // (Finances/Workshops/Parts) muszą dostać explicit Close, żeby się schowały.
            RailwayManager.Core.UIIntents.Emit(RailwayManager.Core.UIIntent.CloseFinancesPanel);
            RailwayManager.Core.UIIntents.Emit(RailwayManager.Core.UIIntent.CloseWorkshopsPanel);
            RailwayManager.Core.UIIntents.Emit(RailwayManager.Core.UIIntent.ClosePartsPanel);
            RailwayManager.Core.UIIntents.Emit(RailwayManager.Core.UIIntent.ClosePersonnelPanel);
        }

        private void ShowBuildMenu()
        {
            if (buildMenu != null)
                buildMenu.Show();
        }

        private void HideBuildMenu()
        {
            if (buildMenu != null)
                buildMenu.Hide();
        }

        public void SetActiveTab(MainTab tab)
        {
            activeTab = tab;
            OnTabChanged?.Invoke(activeTab);
            if (tab == MainTab.Build)
                ShowBuildMenu();
            else
                HideBuildMenu();
            UpdateVisuals();
        }

        private void UpdateVisuals()
        {
            foreach (var tb in tabButtons)
            {
                bool isActive = tb.tab == activeTab;
                Color background = isActive ? activeColor : tb.unlocked ? normalColor : lockedColor;

                if (tb.background != null)
                    tb.background.color = background;

                if (tb.button != null)
                {
                    tb.button.interactable = tb.unlocked;
                    tb.button.colors = DepotUIPanelPrimitives.CreateButtonColors(
                        background,
                        isActive ? UITheme.PrimaryAccentHover : UITheme.RaisedSurface,
                        isActive ? UITheme.Darken(UITheme.PrimaryAccentHover, 0.18f) : UITheme.Border);
                }

                if (tb.accent != null)
                    tb.accent.gameObject.SetActive(isActive);

                if (tb.iconText != null)
                    tb.iconText.color = DepotUIPanelPrimitives.GetIconColor(isActive, tb.unlocked);

                if (tb.labelText != null)
                {
                    tb.labelText.color = DepotUIPanelPrimitives.GetLabelColor(isActive, tb.unlocked);
                    tb.labelText.fontStyle = isActive ? FontStyles.Bold : FontStyles.Normal;
                }
            }

            if (headerTitleText != null)
                headerTitleText.text = "Nawigacja";

            if (headerStateText != null)
                headerStateText.text = GetTabSummary(activeTab);
        }

        public void UnlockTab(MainTab tab)
        {
            var tb = tabButtons.Find(t => t.tab == tab);
            if (tb != null)
            {
                tb.unlocked = true;
                UpdateVisuals();
            }
        }

        private static string GetTabSummary(MainTab tab) => tab switch
        {
            MainTab.Select => "Tryb: wybor",
            MainTab.Map => "Tryb: mapa",
            MainTab.Schedules => "Tryb: rozklady",
            MainTab.Circulations => "Tryb: obiegi",
            MainTab.Fleet => "Tryb: tabor",
            MainTab.Staff => "Tryb: personel",
            MainTab.Finances => "Tryb: finanse",
            MainTab.Workshops => "Tryb: warsztat",
            MainTab.Parts => "Tryb: magazyn",
            MainTab.Build => "Tryb: budowanie",
            _ => "Tryb: wybor"
        };
    }
}
