using System.Collections.Generic;
using System.Text;

namespace RailwayManager.Core
{
    /// <summary>
    /// Stack-based pause ownership (2026-05-13).
    ///
    /// Wcześniej <see cref="GameState.IsPaused"/> była pojedynczą flagą — wielu „posiadaczy"
    /// pauzy walczyło o tę samą wartość. Bug-pattern: user otwiera popup →
    /// <c>IsPaused = true</c>; w trakcie alert breakdown → też <c>IsPaused = true</c>
    /// (idempotent OK); user zamyka popup → <c>IsPaused = false</c> → świat startuje przy
    /// nadal aktywnym alercie.
    ///
    /// Stack rozwiązuje to przez explicit ownership:
    /// <code>
    /// PauseStack.Push("popup.timetable_creator");
    /// PauseStack.Push("alert.breakdown");
    /// // pause aktywne dopóki stack > 0
    /// PauseStack.Pop("popup.timetable_creator"); // nadal paused (alert trzyma)
    /// PauseStack.Pop("alert.breakdown");         // teraz unpause
    /// </code>
    ///
    /// Współistnieje z legacy <see cref="GameState.IsPaused"/> setter (bare flag słów
    /// kluczowych jak <c>SpeedPauseButton</c> w TopBar) — pause aktywne gdy **legacy lub stack**
    /// (OR). Migracja write-callerów na <see cref="Push"/>/<see cref="Pop"/> jest opt-in.
    ///
    /// <b>Idempotentne:</b> Push tym samym owner'em wiele razy = 1 entry. Pop bez Push = no-op.
    /// Dwukrotny Push z różnymi reasonami od tego samego subsystemu nie konfliktuje — to
    /// dwa różne sloty.
    /// </summary>
    public static class PauseStack
    {
        // HashSet bo Push idempotent (drugi Push tym samym owner'em = no-op), kolejność nieważna.
        private static readonly HashSet<string> _owners = new();

        /// <summary>True gdy stack ma jakichkolwiek właścicieli pauzy.</summary>
        public static bool HasOwners => _owners.Count > 0;

        /// <summary>Liczba aktualnie aktywnych właścicieli (diagnostic).</summary>
        public static int Count => _owners.Count;

        /// <summary>
        /// Dodaje owner'a do stacku. Idempotent (drugi Push tym samym owner'em = no-op).
        /// Returns true jeśli faktycznie dodany (był nowy), false jeśli już był w stack.
        /// </summary>
        public static bool Push(string owner)
        {
            if (string.IsNullOrEmpty(owner)) return false;
            return _owners.Add(owner);
        }

        /// <summary>
        /// Usuwa owner'a ze stacku. Returns true jeśli faktycznie usunięty, false jeśli
        /// nie było go w stack (warning-worthy w callerach — np. Pop bez Push to bug).
        /// </summary>
        public static bool Pop(string owner)
        {
            if (string.IsNullOrEmpty(owner)) return false;
            return _owners.Remove(owner);
        }

        /// <summary>
        /// Pełen reset stacku — wszystkich właścicieli. Wywoływać przy nowej grze /
        /// save load (analogicznie <see cref="VehicleLocationService.ResetAll"/>).
        /// </summary>
        public static void Clear()
        {
            int count = _owners.Count;
            _owners.Clear();
            if (count > 0)
                Log.Info($"[PauseStack] Cleared {count} owner(s)");
        }

        /// <summary>Czy konkretny owner trzyma pauzę.</summary>
        public static bool Contains(string owner)
        {
            return !string.IsNullOrEmpty(owner) && _owners.Contains(owner);
        }

        /// <summary>Snapshot aktualnych właścicieli (do save/load lub diagnostyki).</summary>
        public static IReadOnlyCollection<string> Owners => _owners;

        /// <summary>Diagnostyczny dump — używać w debug logach gdy podejrzewasz stuck pauzę.</summary>
        public static string DescribeForLog()
        {
            if (_owners.Count == 0) return "PauseStack: empty";
            var sb = new StringBuilder($"PauseStack ({_owners.Count} owner(s)): ");
            bool first = true;
            foreach (var o in _owners)
            {
                if (!first) sb.Append(", ");
                sb.Append(o);
                first = false;
            }
            return sb.ToString();
        }
    }
}
