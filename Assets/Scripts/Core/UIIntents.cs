using System;

namespace RailwayManager.Core
{
    /// <summary>
    /// Komunikaty cross-scene między Depot a Timetable/Maintenance UI. Używane gdy
    /// jeden moduł chce poprosić o pokazanie/zamknięcie panelu który żyje w innym
    /// asmdef bez bezpośredniej referencji.
    ///
    /// Wcześniej (do 2026-05-10) ten sam mechanizm był 5 publicznych statycznych
    /// `bool Pending*` flag w <see cref="SceneController"/>. Każdy konsument
    /// pollował własną flagę w Update(), każdy producent ustawiał flagę. Skala
    /// nie wytrzymywała — nowy panel cross-scene = +1 flaga + +1 polling.
    ///
    /// Event-driven API redukuje to do jednego eventu z enum'em. Producenci
    /// emitują (<c>UIIntents.Emit</c>), konsumenci subskrybują w
    /// <c>OnEnable</c>/<c>OnDisable</c>.
    ///
    /// **Co zostało w SceneController:** <see cref="SceneController.TimetablePopupOpen"/>
    /// — to nie intent (one-shot), tylko stan globalny "czy modal popup jest otwarty"
    /// czytany przez DepotOrbitCamera dla input gating. Inny pattern, zostaje.
    /// </summary>
    public enum UIIntent
    {
        /// <summary>One-shot: otwórz listę rozkładów (TimetableListUI).</summary>
        OpenScheduleCreator,
        /// <summary>One-shot: otwórz listę obiegów (CirculationListUI).</summary>
        OpenCirculationList,

        /// <summary>Toggle show: panel finansów (FinancePanelUI).</summary>
        OpenFinancesPanel,
        /// <summary>Toggle hide: panel finansów.</summary>
        CloseFinancesPanel,

        /// <summary>Toggle show: panel warsztatów (WorkshopsPanelUI).</summary>
        OpenWorkshopsPanel,
        /// <summary>Toggle hide: panel warsztatów.</summary>
        CloseWorkshopsPanel,

        /// <summary>Toggle show: panel magazynu części (PartsPanelUI).</summary>
        OpenPartsPanel,
        /// <summary>Toggle hide: panel magazynu części.</summary>
        ClosePartsPanel,

        /// <summary>Toggle show: panel personelu (PersonnelMainTabUI).</summary>
        OpenPersonnelPanel,
        /// <summary>Toggle hide: panel personelu.</summary>
        ClosePersonnelPanel,

        /// <summary>M11 AS-5c: otwórz planner połączeń asystenta (RelationPlannerUI w Timetable).</summary>
        OpenRelationPlanner,
    }

    public static class UIIntents
    {
        /// <summary>
        /// Emitowane gdy producent (Depot UI / MaintenanceAlerts) chce komunikować
        /// intent. Konsumenci subskrybują w lifecycle gameobjectu.
        /// </summary>
        public static event Action<UIIntent> OnIntent;

        public static void Emit(UIIntent intent)
        {
            OnIntent?.Invoke(intent);
        }
    }
}
