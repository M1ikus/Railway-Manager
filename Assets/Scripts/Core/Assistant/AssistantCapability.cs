using System;
using System.Collections.Generic;

namespace RailwayManager.Core.Assistant
{
    /// <summary>
    /// Kategoria capability — grupowanie w panelu asystenta (AssistantPanelUI).
    /// Odpowiada modułowi-właścicielowi domeny, nie warstwie asmdef.
    /// </summary>
    public enum AssistantCapabilityCategory
    {
        Depot,
        Fleet,
        Timetable,
        Personnel,
        Economy
    }

    /// <summary>
    /// Pojedynczy krok instrukcji poziomu [1] „pokaż jak".
    /// <c>messageKey</c> to klucz i18n (tłumaczy warstwa SharedUI przez LocalizationService).
    /// <c>highlightTargetId</c> to opcjonalny identyfikator elementu UI do podświetlenia —
    /// interpretuje go moduł-właściciel capability (Core nie zna paneli modułów).
    /// </summary>
    [Serializable]
    public class AssistantGuidanceStep
    {
        public string messageKey;
        public string highlightTargetId;

        /// <summary>
        /// AS-5c: opcjonalny intent UI emitowany przy pokazaniu kroku (np. otwarcie
        /// plannera połączeń). Orchestrator emituje przez UIIntents — moduł-właściciel
        /// panelu subskrybuje. Null = krok czysto instruktażowy.
        /// </summary>
        public UIIntent? uiIntent;
    }

    /// <summary>Instrukcja poziomu [1] — sekwencja kroków. Każda capability ją ma (AS-D5).</summary>
    public class AssistantGuidance
    {
        public List<AssistantGuidanceStep> steps = new List<AssistantGuidanceStep>();
    }

    /// <summary>
    /// Propozycja poziomu [2] „zaproponuj" — preview do akceptacji przez gracza (AS-D3).
    /// Renderowana generycznie przez AssistantPlanPreviewUI (SharedUI).
    /// <c>payload</c> to wewnętrzne dane capability (np. GenerationResult) odczytywane
    /// z powrotem w Apply — Core ich nie interpretuje.
    /// </summary>
    public class AssistantPlan
    {
        public string capabilityId;
        public string title;
        public List<string> previewLines = new List<string>();

        /// <summary>Koszt w groszach; 0 = bez kosztu. Plan z kosztem &gt; 0 nigdy nie idzie w auto-mode (AS-D6).</summary>
        public long costGroszy;

        public string effectSummary;
        public object payload;

        /// <summary>Czas gry (GameState.GameTimeSeconds) w momencie Plan() — capability waliduje świeżość w Apply.</summary>
        public long createdAtGameSec;
    }

    /// <summary>
    /// M11 AS-1a: kontrakt pojedynczej akcji, którą zna asystent gracza.
    /// Formalizacja istniejącego wzorca Plan→Apply (CirculationAutoGenerator.Generate/ApplyAll,
    /// CrewCirculationAutoGenerator.Generate/Commit). Pełny spec: memory/tutorial_m11_design.md.
    ///
    /// Eskalacja AS-D4: [1] GetGuidance (każda capability) → [2] Plan (tylko CanAutoExecute)
    /// → [3] Apply. Kontrakt krytyczny (AS-D3): <see cref="Plan"/> NIE MUTUJE stanu gry —
    /// liczy i zwraca preview; commit wykonuje wyłącznie <see cref="Apply"/> po akceptacji gracza.
    ///
    /// Rejestracja: moduł-właściciel woła <c>AssistantCapabilityRegistry.Register</c> w swoim
    /// bootstrapie (wzorzec SaveActionsHook/ISavable) — Core nie referuje modułów.
    /// </summary>
    public interface IAssistantCapability
    {
        /// <summary>Stabilny identyfikator, np. "circulation.autogen". Unikalny w rejestrze.</summary>
        string Id { get; }

        AssistantCapabilityCategory Category { get; }

        /// <summary>Czy akcja ma sens w bieżącym stanie gry (gating listy w panelu + reguł monitora).</summary>
        bool CanExecute();

        /// <summary>Poziom [1]: instrukcja „pokaż jak". Nigdy null (AS-D5: każda capability uczy).</summary>
        AssistantGuidance GetGuidance();

        /// <summary>Czy istnieje „mózg" (poziomy [2]/[3]). False = capability guidance-only, sufit na [1] (AS-D5).</summary>
        bool CanAutoExecute { get; }

        /// <summary>
        /// AS-D6: czy capability kwalifikuje się do opt-in auto-mode („rób automatycznie").
        /// TYLKO akcje addytywne/odwracalne (wynik usuwalny istniejącymi narzędziami).
        /// Default false — adapter włącza świadomie. Niezależnie od flagi: plan z kosztem
        /// &gt; 0 NIGDY nie idzie auto (twarda zasada — kasa zawsze za zgodą).
        /// </summary>
        bool AutoModeAllowed => false;

        /// <summary>
        /// Poziom [2]: policz propozycję BEZ side-efektów. Null = brak mózgu (!CanAutoExecute)
        /// albo nie ma nic do zaproponowania w bieżącym stanie gry.
        /// </summary>
        AssistantPlan Plan();

        /// <summary>
        /// Poziom [3]: wykonaj zaakceptowany plan. Zwraca false, gdy plan nieważny / cudzy /
        /// przeterminowany (capability waliduje) — wtedy nic nie zostało zmienione.
        /// </summary>
        bool Apply(AssistantPlan plan);
    }
}
