using UnityEngine;
using UnityEngine.InputSystem;
using RailwayManager.Core;

namespace DepotSystem
{
    /// <summary>
    /// M9b Etap 4: state machine selekcji consist'u w zajezdni.
    ///
    /// Stany:
    /// - Idle: LMB = raycast → if ConsistMarker → Select + popup
    /// - ConsistSelected: LMB = raycast → if Track → EnqueueMove + Deselect
    /// - Zawsze: ESC / RMB = Deselect
    /// - Aktywny tylko w DepotUIManager.CurrentTool == ToolMode.Select
    /// </summary>
    public class DepotConsistSelectionHandler : MonoBehaviour
    {
        public static DepotConsistSelectionHandler Instance { get; private set; }

        /// <summary>
        /// True gdy jakiś consist jest wybrany i czekamy na klik docelowego toru.
        /// Inne UI (TrackPopupUI, TrainPopupUI) powinny skipować swoje obsługi LMB w tym stanie.
        /// </summary>
        public static bool HasActiveSelection { get; private set; }

        enum State { Idle, ConsistSelected }

        State _state = State.Idle;
        ConsistMarker _selectedConsist;
        Camera _camera;

        InputActions _inputActions;
        InputActions.VehicleActions _vehicleActions;
        InputActions.UIPopupActions _popupActions;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Log.Warn($"[ConsistSelection] Duplicate on '{gameObject.name}' — destroying");
                Destroy(this);
                return;
            }
            Instance = this;

            _inputActions = new InputActions();
            RailwayManager.Core.Settings.RebindingService.ApplyOverridesTo(_inputActions);
            _vehicleActions = _inputActions.Vehicle;
            _popupActions = _inputActions.UIPopup;
        }

        void OnEnable()
        {
            _vehicleActions.Enable();
            _popupActions.Enable();
        }

        void OnDisable()
        {
            _vehicleActions.Disable();
            _popupActions.Disable();
        }

        // ── Tool-mode gate ──
        private DepotSystem.ToolModeGate _toolGate;

        void Start()
        {
            _camera = Camera.main;
            // M-Windows P3-ops: operacyjne okno składu zastępuje ConsistPopupUI (kontrakt 1:1).
            var win = ConsistOperationalWindow.Instance;
            win.OnCloseRequested += Deselect;
            win.OnGoRequested += ExecutePendingMove;
            win.OnExitRequested += ExecuteExit;
            win.OnDecoupleRequested += ExecuteDecouple;
            win.OnCoupleAdjacentRequested += ExecuteCoupleAdjacent;

            DepotMovementSimulator.OnConsistExitedDepot += OnConsistExited;

            _toolGate = new DepotSystem.ToolModeGate(
                this,
                m => m == DepotSystem.ToolMode.Select,
                Deselect);
            _toolGate.Start();
        }

        void OnDestroy()
        {
            _toolGate?.Stop();
            _inputActions?.Dispose();
            var win = ConsistOperationalWindow.Instance;
            win.OnCloseRequested -= Deselect;
            win.OnGoRequested -= ExecutePendingMove;
            win.OnExitRequested -= ExecuteExit;
            win.OnDecoupleRequested -= ExecuteDecouple;
            win.OnCoupleAdjacentRequested -= ExecuteCoupleAdjacent;

            DepotMovementSimulator.OnConsistExitedDepot -= OnConsistExited;
        }

        void OnConsistExited(int consistId, System.Collections.Generic.List<int> vehicleIds)
        {
            // Consist opuścił depot → auto-deselect jeśli był wybrany
            if (_selectedConsist != null && _selectedConsist.consistId == consistId)
                Deselect();
        }

        /// <summary>Wywołane przez przycisk "Wyjedź z depot" w ConsistPopupUI.</summary>
        void ExecuteExit()
        {
            if (_selectedConsist == null) return;
            var sim = DepotMovementSimulator.Instance;
            if (sim == null) return;

            bool success = sim.EnqueueExit(_selectedConsist.consistId, _selectedConsist.vehicleIds);
            if (!success)
                Log.Warn("[ConsistSelection] EnqueueExit failed — check if Exit tracks exist in graph");
        }

        /// <summary>TD-032: wywołane przez ConsistPopupUI po wyborze miejsca cięcia (picker rozprzęgania).</summary>
        void ExecuteDecouple(int cutIndex)
        {
            if (_selectedConsist == null) return;
            var sim = DepotMovementSimulator.Instance;
            if (sim == null) return;

            bool ok = sim.DecoupleConsist(_selectedConsist.consistId, cutIndex);
            if (!ok)
                Log.Warn($"[ConsistSelection] DecoupleConsist failed consist#{_selectedConsist.consistId} cut={cutIndex}");
            Deselect();   // stary marker zniszczony przez split → wyczyść selekcję + zamknij popup
        }

        /// <summary>TD-032 H: ręczny couple z sąsiadującym stojącym składem (przycisk „Połącz z sąsiednim").</summary>
        void ExecuteCoupleAdjacent()
        {
            if (_selectedConsist == null) return;
            var sim = DepotMovementSimulator.Instance;
            if (sim == null) return;

            int adjacent = sim.FindAdjacentCouplableConsist(_selectedConsist.consistId);
            if (adjacent < 0) { Log.Warn("[ConsistSelection] CoupleAdjacent: brak sąsiada do sprzęgu."); return; }

            bool ok = sim.CoupleConsists(_selectedConsist.consistId, adjacent); // selected = survivor
            if (!ok)
                Log.Warn($"[ConsistSelection] CoupleAdjacent failed consist#{_selectedConsist.consistId} + #{adjacent}");
            Deselect();   // survivor visual respawn → stary marker zniszczony
        }

        void Update()
        {
            // ESC — deselect (sprawdzaj PRZED IsPointerOverUI — pattern z TrainPopupUI)
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                if (_state != State.Idle)
                {
                    PauseMenuUI.LastEscConsumedFrame = Time.frameCount;
                    Deselect();
                    return;
                }
            }

            if (DepotUIManager.Instance.IsPointerOverUI()) return;

            // RMB — deselect (tylko gdy coś wybrane)
            if (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame)
            {
                if (_state != State.Idle)
                {
                    Deselect();
                    return;
                }
            }

            // LMB — kontekstowe: Idle → select consist, ConsistSelected → select target track
            // Uwaga: używamy direct Mouse.current zamiast _vehicleActions.Select bo to drugie
            // może nie być bound do LMB w tej instancji InputActions.
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                if (_state == State.Idle)
                    TrySelectConsist();
                else if (_state == State.ConsistSelected)
                    TrySelectTargetTrack();
            }
        }

        // ── Selection logic ─────────────────────────────────────────

        void TrySelectConsist()
        {
            if (_camera == null) _camera = Camera.main;
            if (_camera == null || Mouse.current == null) return;

            var ray = _camera.ScreenPointToRay(Mouse.current.position.ReadValue());
            if (!Physics.Raycast(ray, out var hit, 1000f, ~0, QueryTriggerInteraction.Collide))
                return;

            var marker = hit.collider.GetComponentInParent<ConsistMarker>();
            if (marker == null) return;

            Select(marker);
        }

        void TrySelectTargetTrack()
        {
            try
            {
                TrySelectTargetTrackImpl();
            }
            catch (System.Exception ex)
            {
                Log.Warn($"[ConsistSelection] EXCEPTION in TrySelectTargetTrack: {ex.Message}\n{ex.StackTrace}");
            }
        }

        void TrySelectTargetTrackImpl()
        {
            if (_camera == null) _camera = Camera.main;
            if (_camera == null || Mouse.current == null || _selectedConsist == null) return;

            var ray = _camera.ScreenPointToRay(Mouse.current.position.ReadValue());
            if (!Physics.Raycast(ray, out var hit, 1000f))
                return;

            var builder = DepotServices.Get<PrefabTrackBuilder>();
            if (builder == null) return;

            // Walk up hierarchy — find any ancestor which matches a PlacedTrackSegment.TrackObject
            int targetTrackId = -1;
            Transform t = hit.collider.transform;
            while (t != null && targetTrackId < 0)
            {
                foreach (var placed in builder.PlacedTracks)
                {
                    if (placed.TrackObject == t.gameObject)
                    {
                        targetTrackId = placed.GraphTrackId;
                        break;
                    }
                }
                t = t.parent;
            }

            if (targetTrackId < 0) return;

            // Ustaw pending target w oknie (bez ruchu) — user musi kliknąć "Jedź"
            ConsistOperationalWindow.Instance.SetPendingTarget(targetTrackId, hit.point);
        }

        /// <summary>Wywołane przez przycisk "Jedź" w ConsistPopupUI.</summary>
        void ExecutePendingMove()
        {
            if (_selectedConsist == null) return;
            var win = ConsistOperationalWindow.Instance;
            if (win.PendingTargetTrackId < 0) return;

            var sim = DepotMovementSimulator.Instance;
            if (sim == null) return;

            int targetTrackId = win.PendingTargetTrackId;
            Vector3? targetPos = win.PendingTargetWorldPos;

            bool success = sim.EnqueueMove(
                _selectedConsist.consistId,
                _selectedConsist.vehicleIds,
                _selectedConsist.currentTrackId,
                targetTrackId,
                targetPos);

            if (success)
            {
                Log.Info($"[ConsistSelection] Go! consist#{_selectedConsist.consistId} " +
                         $"→ track#{targetTrackId} at {targetPos}");
                win.ClearPendingTarget();
                // Okno zostaje — user widzi ruch pociągu. Deselect dopiero przez X/ESC/RMB.
            }
            else
            {
                Log.Warn("[ConsistSelection] EnqueueMove returned false");
            }
        }

        void Select(ConsistMarker marker)
        {
            _selectedConsist = marker;
            _state = State.ConsistSelected;
            HasActiveSelection = true;

            // Highlight na żółto
            var sim = DepotMovementSimulator.Instance;
            if (sim != null && sim.SelectedMaterial != null && marker.meshRenderer != null)
            {
                marker.meshRenderer.sharedMaterial = sim.SelectedMaterial;
            }

            ConsistOperationalWindow.Instance.Show(marker, true);
            Log.Info($"[ConsistSelection] Selected consist#{marker.consistId}");
        }

        void Deselect()
        {
            if (_selectedConsist != null && _selectedConsist.meshRenderer != null
                && _selectedConsist.originalMaterial != null)
            {
                _selectedConsist.meshRenderer.sharedMaterial = _selectedConsist.originalMaterial;
            }

            _selectedConsist = null;
            _state = State.Idle;
            HasActiveSelection = false;
            ConsistOperationalWindow.Instance.Hide();
        }

        // ── Helpers ─────────────────────────────────────────────────

        /// <summary>Szuka roota toru (tag="Track") w hierarchii. Wzorzec z TrackPopupUI.</summary>
        static Transform FindTrackRoot(Transform t)
        {
            while (t != null)
            {
                if (t.CompareTag("Track")) return t;
                t = t.parent;
            }
            return null;
        }
    }
}
