using UnityEngine;
using RailwayManager.Core;

namespace RailwayManager.Personnel
{
    /// <summary>
    /// Bridge cross-asmdef dla otwierania panelu personelu z Depot UI.
    /// Depot.asmdef NIE referuje Personnel (cykl) — komunikacja przez `UIIntents` bus
    /// (analogicznie do FinancePanelUI/WorkshopsPanelUI/PartsPanelUI).
    ///
    /// Auto-spawn przez `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]` — subskrybuje
    /// `UIIntents.OnIntent` zanim jakikolwiek Awake odpali. Handler:
    /// - `OpenPersonnelPanel` → `PersonnelMainTabUI.EnsureExists().Show()`
    /// - `ClosePersonnelPanel` → `PersonnelMainTabUI.Instance?.Hide()`
    /// </summary>
    public static class PersonnelToolBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterIntentHandler()
        {
            UIIntents.OnIntent += HandleIntent;
        }

        private static void HandleIntent(UIIntent intent)
        {
            switch (intent)
            {
                case UIIntent.OpenPersonnelPanel:
                    PersonnelMainTabUI.EnsureExists().Show();
                    break;
                case UIIntent.ClosePersonnelPanel:
                    // Hide() jest idempotent (root.SetActive(false) + _isVisible=false), bez konieczności pre-check.
                    if (PersonnelMainTabUI.Instance != null)
                        PersonnelMainTabUI.Instance.Hide();
                    break;
            }
        }
    }
}
