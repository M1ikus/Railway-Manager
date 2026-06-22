using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RailwayManager.Core.Settings
{
    /// <summary>
    /// Static service zarządzający bindings overrides dla InputActions (M13-2).
    ///
    /// Architektura: każdy serwis Unity (Camera, ToolStateMachine, Popup, etc.) tworzy
    /// własną instance <see cref="InputActions"/> w Awake(). Po <c>new InputActions()</c>
    /// callsite wywołuje <see cref="ApplyOverridesTo"/> żeby zaaplikować zapisane bindings
    /// overrides z PlayerPrefs.
    ///
    /// Persystencja: pojedynczy JSON blob w PlayerPrefs pod kluczem
    /// <c>Settings.Control.Rebindings</c> (format Unity InputSystem
    /// <see cref="InputActionRebindingExtensions.SaveBindingOverridesAsJson"/>).
    ///
    /// Hot-reload (zmiana binding w UI → natychmiast działa w już aktywnych instancjach):
    /// NIE w M13-2 (info "wymaga restartu sceny" w Settings UI). Hot-reload przez subscription
    /// na <see cref="OnRebindingsChanged"/> może być dodane post-EA gdyby user'zy poprosili.
    /// </summary>
    public static class RebindingService
    {
        private const string K_REBINDINGS = "Settings.Control.Rebindings";

        /// <summary>Emitowane po Save / Reset overrides. Konsumenci mogą reapplyować na swoich instancjach.</summary>
        public static event Action OnRebindingsChanged;

        // Cache JSON loaded from PlayerPrefs (lazy, invalidated on Save/Reset/Reload)
        private static string _cachedJson;
        private static bool _cacheLoaded;

        // ─── Apply ───────────────────────────────────────

        /// <summary>
        /// Aplikuje zapisane bindings overrides do nowo utworzonej instancji <see cref="InputActions"/>.
        /// Wywołać po <c>new InputActions()</c> w Awake każdego serwisu.
        /// No-op jeśli brak overrides w PlayerPrefs.
        /// </summary>
        public static void ApplyOverridesTo(InputActions actions)
        {
            if (actions == null) return;

            string json = GetCachedJson();
            if (string.IsNullOrEmpty(json)) return;

            try
            {
                actions.asset.LoadBindingOverridesFromJson(json);
            }
            catch (Exception e)
            {
                Log.Warn($"[RebindingService] Failed to apply overrides: {e.Message}");
            }
        }

        // ─── Save / Reset ────────────────────────────────

        /// <summary>
        /// Zapisuje overrides z podanej instancji <see cref="InputActions"/> do PlayerPrefs.
        /// Wywoływane po user-driven rebind w Settings UI.
        /// Emituje <see cref="OnRebindingsChanged"/> event.
        /// </summary>
        public static void SaveOverrides(InputActions actions)
        {
            if (actions == null) { Log.Warn("[RebindingService] SaveOverrides(null) ignored"); return; }

            string json = actions.asset.SaveBindingOverridesAsJson();
            _cachedJson = json;
            _cacheLoaded = true;
            PlayerPrefs.SetString(K_REBINDINGS, json);
            PlayerPrefs.Save();
            OnRebindingsChanged?.Invoke();
            Log.Info("[RebindingService] Overrides saved");
        }

        /// <summary>
        /// Reset wszystkich overrides do domyślnych wartości z .inputactions JSON.
        /// Modyfikuje podaną instancję <see cref="InputActions"/> + czyści PlayerPrefs.
        /// </summary>
        public static void ResetAllOverrides(InputActions actions)
        {
            if (actions != null)
                actions.asset.RemoveAllBindingOverrides();

            _cachedJson = null;
            _cacheLoaded = true; // Loaded as empty
            PlayerPrefs.DeleteKey(K_REBINDINGS);
            PlayerPrefs.Save();
            OnRebindingsChanged?.Invoke();
            Log.Info("[RebindingService] Overrides reset to defaults");
        }

        public static string GetSavedOverridesJson()
        {
            return GetCachedJson();
        }

        public static void RestoreSavedOverridesJson(string json, InputActions actions = null)
        {
            bool hasJson = !string.IsNullOrEmpty(json);
            _cachedJson = hasJson ? json : null;
            _cacheLoaded = true;

            if (hasJson)
                PlayerPrefs.SetString(K_REBINDINGS, json);
            else
                PlayerPrefs.DeleteKey(K_REBINDINGS);

            PlayerPrefs.Save();

            if (actions != null)
            {
                actions.asset.RemoveAllBindingOverrides();
                if (hasJson)
                {
                    try
                    {
                        actions.asset.LoadBindingOverridesFromJson(json);
                    }
                    catch (Exception e)
                    {
                        Log.Warn($"[RebindingService] Failed to restore overrides: {e.Message}");
                    }
                }
            }

            OnRebindingsChanged?.Invoke();
            Log.Info("[RebindingService] Overrides restored");
        }

        /// <summary>
        /// Reset overrides dla pojedynczej akcji. Caller musi wywołać <see cref="SaveOverrides"/>
        /// żeby propagować do PlayerPrefs.
        /// </summary>
        public static void ResetAction(InputAction action)
        {
            if (action == null) return;
            action.RemoveAllBindingOverrides();
        }

        /// <summary>
        /// Reset overrides dla pojedynczego binding (używane gdy chcesz reset konkretnej kombinacji
        /// klawiszy w composite binding, np. tylko "W" w WSAD bez ruszania reszty).
        /// Caller musi wywołać <see cref="SaveOverrides"/>.
        /// </summary>
        public static void ResetBinding(InputAction action, int bindingIndex)
        {
            if (action == null) return;
            if (bindingIndex < 0 || bindingIndex >= action.bindings.Count) return;
            action.RemoveBindingOverride(bindingIndex);
        }

        // ─── Cache management ─────────────────────────────

        private static string GetCachedJson()
        {
            if (!_cacheLoaded)
            {
                _cachedJson = PlayerPrefs.GetString(K_REBINDINGS, "");
                _cacheLoaded = true;
            }
            return _cachedJson;
        }

        /// <summary>
        /// Force reload from PlayerPrefs. Użyteczne dla testów / po external edit PlayerPrefs.
        /// </summary>
        public static void ReloadFromPlayerPrefs()
        {
            _cacheLoaded = false;
            _cachedJson = null;
        }

        // ─── Interactive rebind helper ───────────────────

        /// <summary>
        /// Rozpoczyna interaktywny rebind dla konkretnego <paramref name="bindingIndex"/>
        /// w <paramref name="action"/>. Wywołuje <paramref name="onComplete"/> z parametrem
        /// <c>true</c> przy success, <c>false</c> przy cancel/timeout.
        ///
        /// Caller jest odpowiedzialny za:
        /// - disable akcji przed rebind (Unity wymaga)
        /// - re-enable po rebind
        /// - SaveOverrides po success (nie wywoływane automatycznie żeby umożliwić batch save)
        ///
        /// Przykład użycia w UI (M13-2b):
        /// <code>
        /// action.Disable();
        /// RebindingService.BeginRebind(action, bindingIndex,
        ///     onComplete: success => {
        ///         action.Enable();
        ///         if (success) RebindingService.SaveOverrides(actions);
        ///     });
        /// </code>
        /// </summary>
        public static InputActionRebindingExtensions.RebindingOperation BeginRebind(
            InputAction action, int bindingIndex, Action<bool> onComplete,
            float timeoutSeconds = 5f)
        {
            if (action == null) { onComplete?.Invoke(false); return null; }

            var op = action.PerformInteractiveRebinding(bindingIndex)
                .WithControlsExcluding("<Mouse>/position")
                .WithControlsExcluding("<Mouse>/delta")
                .WithCancelingThrough("<Keyboard>/escape")
                .WithTimeout(timeoutSeconds)
                .OnComplete(operation =>
                {
                    operation.Dispose();
                    onComplete?.Invoke(true);
                })
                .OnCancel(operation =>
                {
                    operation.Dispose();
                    onComplete?.Invoke(false);
                });

            op.Start();
            return op;
        }
    }
}
