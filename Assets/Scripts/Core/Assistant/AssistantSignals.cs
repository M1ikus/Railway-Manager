using System;

namespace RailwayManager.Core.Assistant
{
    /// <summary>Rodzaj sygnału moduł→asystent.</summary>
    public enum AssistantSignalKind
    {
        /// <summary>Moduł ma gotową sugestię dla advisora (bridge z OnSuggestionAvailable — AS-6).</summary>
        Suggestion,

        /// <summary>Panel otwarty i zamknięty bez wykonania akcji (wejście stuck-detectora, AS-1b).</summary>
        PanelAbandoned,

        /// <summary>Anulowanie akcji w toku (seria canceli = sygnał „gracz się gubi").</summary>
        ActionCanceled,

        /// <summary>Gracz zaczął aktywnie używać narzędzia (advisor: cisza — zasada UX „nie szepcz gdy aktywny").</summary>
        ToolActive,

        /// <summary>Gracz odłożył narzędzie (advisor może znów szeptać).</summary>
        ToolIdle,

        Custom
    }

    /// <summary>
    /// Sygnał z modułu do asystenta. <c>contextKey</c> spina się z SuggestionMemoryService
    /// (dedup/snooze — filtruje warstwa SharedUI, nie Core). <c>messageKey</c> = opcjonalny
    /// klucz i18n szeptu. <c>payload</c> = dane sugestii (Core ich nie interpretuje).
    /// </summary>
    public readonly struct AssistantSignal
    {
        public readonly AssistantSignalKind kind;
        public readonly string sourceId;
        public readonly string contextKey;
        public readonly string messageKey;
        public readonly object payload;

        public AssistantSignal(AssistantSignalKind kind, string sourceId,
            string contextKey = null, string messageKey = null, object payload = null)
        {
            this.kind = kind;
            this.sourceId = sourceId;
            this.contextKey = contextKey;
            this.messageKey = messageKey;
            this.payload = payload;
        }
    }

    /// <summary>
    /// M11 AS-1a: bus sygnałów moduł→asystent — uogólnienie <c>UIIntents</c> (payload zamiast
    /// gołego enuma). Producenci: suggestion services (AS-6 bridges), panele UI (sygnały stuck),
    /// tool state machines (ToolActive/ToolIdle). Konsument: drivery asystenta w SharedUI.
    /// Emit jest fire-and-forget — brak subskrybentów = no-op (jak UIIntents.Emit).
    /// </summary>
    public static class AssistantSignals
    {
        public static event Action<AssistantSignal> OnSignal;

        public static void Emit(in AssistantSignal signal)
        {
            OnSignal?.Invoke(signal);
        }

        /// <summary>Convenience overload — buduje strukturę w miejscu.</summary>
        public static void Emit(AssistantSignalKind kind, string sourceId,
            string contextKey = null, string messageKey = null, object payload = null)
        {
            OnSignal?.Invoke(new AssistantSignal(kind, sourceId, contextKey, messageKey, payload));
        }
    }
}
