using System;
using System.Collections.Generic;

namespace RailwayManager.Core.Assistant
{
    /// <summary>
    /// M11 AS-1a: centralny rejestr capability asystenta. Wzorzec rejestracji jak
    /// <c>SaveActionsHook.Register</c> — moduły wyższych warstw (Depot/Timetable/Personnel)
    /// rejestrują swoje capability w Core bez cyklu asmdef; asystent (SharedUI) operuje
    /// wyłącznie na meta z tego rejestru.
    ///
    /// Lifecycle: moduły rejestrują w swoich bootstrapach (po <c>Bootstrap.OnLateInit</c>),
    /// <see cref="Clear"/> przy nowej grze / powrocie do MainMenu (static state żyje
    /// cross-scene — wzorzec BUG-039 jak VehicleLocationService.ResetAll).
    /// </summary>
    public static class AssistantCapabilityRegistry
    {
        private static readonly Dictionary<string, IAssistantCapability> _byId =
            new Dictionary<string, IAssistantCapability>();

        /// <summary>
        /// Emitowane po każdej zmianie zawartości rejestru (Register/Unregister/Clear).
        /// Konsument: AssistantPanelUI refresh listy.
        /// </summary>
        public static event Action OnChanged;

        public static int Count => _byId.Count;

        /// <summary>
        /// Widok wartości rejestru (zero-alokacji). NIE mutować rejestru podczas iteracji
        /// (wzorzec jak VehicleLocationService.GetByType). Potrzebny snapshot → <see cref="GetAll"/>.
        /// </summary>
        public static IEnumerable<IAssistantCapability> All => _byId.Values;

        /// <summary>
        /// Rejestruje capability. False + Log.Warn przy null / pustym Id / duplikacie
        /// (pierwsza rejestracja wygrywa — duplikat to błąd bootstrapu modułu).
        /// </summary>
        public static bool Register(IAssistantCapability capability)
        {
            if (capability == null)
            {
                Log.Warn("[AssistantRegistry] Register(null) — ignored");
                return false;
            }
            if (string.IsNullOrEmpty(capability.Id))
            {
                Log.Warn("[AssistantRegistry] Register z pustym Id — ignored");
                return false;
            }
            if (_byId.ContainsKey(capability.Id))
            {
                Log.Warn($"[AssistantRegistry] Duplikat capability id '{capability.Id}' — pierwsza rejestracja wygrywa");
                return false;
            }

            _byId[capability.Id] = capability;
            Log.Info($"[AssistantRegistry] Registered '{capability.Id}' ({capability.Category}, autoExec={capability.CanAutoExecute}) — total {_byId.Count}");
            OnChanged?.Invoke();
            return true;
        }

        /// <summary>Usuwa capability (teardown modułu / testy). False gdy id nieznane.</summary>
        public static bool Unregister(string id)
        {
            if (string.IsNullOrEmpty(id) || !_byId.Remove(id)) return false;
            Log.Info($"[AssistantRegistry] Unregistered '{id}' — total {_byId.Count}");
            OnChanged?.Invoke();
            return true;
        }

        /// <summary>Null gdy nieznane id.</summary>
        public static IAssistantCapability Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return _byId.TryGetValue(id, out var cap) ? cap : null;
        }

        /// <summary>Snapshot do bufora caller'a (zero-alokacji przy reuse bufora między klatkami).</summary>
        public static void GetAll(List<IAssistantCapability> buffer)
        {
            if (buffer == null) return;
            buffer.Clear();
            foreach (var cap in _byId.Values) buffer.Add(cap);
        }

        /// <summary>
        /// Nowa gra / powrót do MainMenu / testy. Czyści rejestr — capability rejestrują się
        /// ponownie w bootstrapach modułów przy starcie świata.
        /// </summary>
        public static void Clear()
        {
            if (_byId.Count == 0) return;
            int n = _byId.Count;
            _byId.Clear();
            Log.Info($"[AssistantRegistry] Cleared {n} capabilities");
            OnChanged?.Invoke();
        }
    }
}
