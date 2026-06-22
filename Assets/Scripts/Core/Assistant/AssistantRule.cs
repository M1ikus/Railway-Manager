using System;

namespace RailwayManager.Core.Assistant
{
    /// <summary>Rodzaj reguły next-step monitora.</summary>
    public enum AssistantRuleKind
    {
        /// <summary>Krok ścieżki startowej (sekwencja przez priorytety malejąco). Gated przez toggle AS-D2/D7.</summary>
        Onboarding,

        /// <summary>Reaktywny problem/okazja (advisor) — działa przez całą grę.</summary>
        Reactive
    }

    /// <summary>
    /// M11 AS-1b: pojedyncza reguła next-step monitora, rejestrowana przez moduł-właściciel
    /// (wzorzec jak IAssistantCapability — moduły wyższych warstw rejestrują w Core).
    ///
    /// Predykat <see cref="isActive"/> zwraca true, gdy warunek wymaga uwagi gracza (krok
    /// onboardingu niespełniony / problem reaktywny występuje). Predykaty są STANOWE, nie
    /// skryptowe — czytają realny stan gry (introspekcja zajezdni itd.), dzięki czemu start
    /// z presetem / częściową zajezdnią działa z natury (spec: „Reguły MVP", „Preset-aware").
    /// Predykat NIE MUTUJE stanu i powinien być tani (wołany co ~1s).
    ///
    /// Sugerowane pasma priorytetów: Onboarding 900-1000 (kolejne kroki malejąco — pierwszy
    /// niespełniony wygrywa), Reactive 100-500 (wg pilności). Wyższy priorytet wygrywa;
    /// remis rozstrzyga kolejność rejestracji (determinizm).
    /// </summary>
    public class AssistantRule
    {
        public string id;
        public AssistantRuleKind kind;
        public int priority;

        /// <summary>True = warunek zachodzi (reguła aktywna). Czyta stan gry; NIE mutuje.</summary>
        public Func<bool> isActive;

        /// <summary>Capability powiązana z regułą (guidance / eskalacja do Plan).</summary>
        public string capabilityId;

        /// <summary>Klucz i18n szeptu dla drivera (SharedUI tłumaczy).</summary>
        public string messageKey;

        /// <summary>
        /// Klucz dla SuggestionMemoryService (dedup/snooze — filtr wpinany przez SharedUI
        /// w NextStepMonitor.SetOfferFilter). Null → driver używa <see cref="id"/>.
        /// </summary>
        public string contextKey;
    }
}
