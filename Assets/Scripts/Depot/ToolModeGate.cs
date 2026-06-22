using System;
using UnityEngine;

namespace DepotSystem
{
    /// <summary>
    /// Helper composition do tool-mode state machines: zarządza
    /// <c>MonoBehaviour.enabled</c> w oparciu o <see cref="DepotUIManager.CurrentTool"/>.
    ///
    /// Eliminuje powtarzający się anti-pattern w Update():
    /// <code>
    /// void Update() {
    ///     if (DepotUIManager.Instance == null) return;
    ///     if (DepotUIManager.Instance.CurrentTool != ToolMode.X) {
    ///         CancelState(); HideThis(); ClearThat();  // defensive cleanup
    ///         return;
    ///     }
    ///     // ... faktyczna praca
    /// }
    /// </code>
    /// (~10 klas × ~60 FPS × ~5-8 branch sprawdzeń = ~3000-5000 wasted ops/s.)
    ///
    /// Po refaktorze klient:
    /// <code>
    /// void Start() {
    ///     _toolGate = new ToolModeGate(this, m => m == ToolMode.BuildCatenary, OnDeactivated);
    ///     _toolGate.Start();
    /// }
    /// void OnDestroy() { _toolGate?.Stop(); }
    /// void Update() { /* tylko faktyczna praca, bez tool-check */ }
    /// </code>
    /// Unity nie wywołuje Update/LateUpdate/FixedUpdate gdy <c>enabled = false</c>.
    /// Subscribe na <see cref="DepotUIManager.OnReady"/> obsługuje race condition gdy
    /// klient Start uruchamia się przed Awake DepotUIManager.
    /// </summary>
    public sealed class ToolModeGate
    {
        private readonly Behaviour _component;
        private readonly Func<ToolMode, bool> _isActiveFor;
        private readonly Action _onDeactivated;
        private bool _started;

        /// <param name="component">MonoBehaviour którego <c>enabled</c> ma być zarządzany.</param>
        /// <param name="isActiveFor">Predykat tool → czy komponent ma być aktywny.</param>
        /// <param name="onDeactivated">
        /// Optional callback wywoływany w momencie przejścia enabled=true → enabled=false
        /// (= cleanup state'u: CancelBuild, HidePreview, ClearSelection itp.).
        /// </param>
        public ToolModeGate(Behaviour component, Func<ToolMode, bool> isActiveFor, Action onDeactivated = null)
        {
            _component = component != null ? component : throw new ArgumentNullException(nameof(component));
            _isActiveFor = isActiveFor ?? throw new ArgumentNullException(nameof(isActiveFor));
            _onDeactivated = onDeactivated;
        }

        public void Start()
        {
            if (_started) return;
            _started = true;
            DepotUIManager.OnReady += OnReadyOrChanged;
            if (DepotUIManager.Instance != null)
                DepotUIManager.Instance.OnToolChanged += OnToolChanged;
            UpdateEnabled();
        }

        public void Stop()
        {
            if (!_started) return;
            _started = false;
            DepotUIManager.OnReady -= OnReadyOrChanged;
            if (DepotUIManager.Instance != null)
                DepotUIManager.Instance.OnToolChanged -= OnToolChanged;
        }

        private void OnReadyOrChanged()
        {
            // DepotUIManager pojawił się po Start — dopinamy subskrypcję OnToolChanged.
            if (DepotUIManager.Instance != null)
                DepotUIManager.Instance.OnToolChanged += OnToolChanged;
            UpdateEnabled();
        }

        private void OnToolChanged(ToolMode mode) => UpdateEnabled();

        private void UpdateEnabled()
        {
            if (_component == null) return;  // destroyed
            bool wasEnabled = _component.enabled;
            var ui = DepotUIManager.Instance;
            _component.enabled = ui != null && _isActiveFor(ui.CurrentTool);
            if (wasEnabled && !_component.enabled)
                _onDeactivated?.Invoke();
        }
    }
}
