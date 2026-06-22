using UnityEngine;
using RailwayManager.Core;

namespace DepotSystem.Schemas.Placement
{
    /// <summary>
    /// MD-3 MVP Placement UX — komponent obsługujący wybór schematu + preview + snap + confirm.
    ///
    /// Plik partial — root trzyma stan + lifecycle. Logika podzielona:
    /// <list type="bullet">
    ///   <item><c>TurnoutSchemaPlacer.Placement.cs</c> — public API (Start/Cancel/Confirm) + PHASE 1/2</item>
    ///   <item><c>TurnoutSchemaPlacer.Update.cs</c> — Update tick + cursor/keyboard input</item>
    ///   <item><c>TurnoutSchemaPlacer.Snap.cs</c> — multi-endpoint snap detection + A13 adaptive proposal</item>
    ///   <item><c>TurnoutSchemaPlacer.Preview.cs</c> — preview transform + geometry regen + cleanup</item>
    /// </list>
    ///
    /// Workflow:
    /// 1. <c>StartPlacement</c> — przekazuje <see cref="TurnoutSchemaDefinition"/>, generuje
    ///    geometrię, tworzy <see cref="SchemaPreviewRenderer"/> child GameObject, aktywuje Update tick
    /// 2. <c>Update</c> — co frame: raycast cursor → world, snap detection, apply transform,
    ///    klawiszologia (Ctrl+Scroll rotacja 5°, R mirror toggle, Esc anuluj, LMB confirm)
    /// 3. <c>ConfirmPlacement</c> — replay geometrii w global coords (PHASE 1 PlaceTrackWithPolyline
    ///    + PHASE 2 PlaceTurnoutOnChain z FindStraightChain + TrimChainToSet).
    ///
    /// Klawiszologia (decyzja MD-3, reuse z TurnoutPlacementStateMachine):
    /// - <b>Ctrl + Scroll</b> — rotacja schematu 5° step (Ctrl gate koordynuje z Camera.Depot.Zoom)
    /// - <b>R</b> — mirror flip (lewo↔prawo, tylko generative, dla snapshot ignorowane)
    /// - <b>Klik LMB</b> — confirm placement
    /// - <b>Esc</b> — anuluj placement
    /// - <b>Shift hold</b> — disable snap (free placement)
    /// </summary>
    public partial class TurnoutSchemaPlacer : MonoBehaviour
    {
        public static TurnoutSchemaPlacer Instance { get; private set; }

        [Header("Snap settings")]
        [Tooltip("Maksymalny dystans (m) snap'u endpointu schematu do istniejącego endpointu toru. " +
                 "Zwiększone z 0.5m do 3.0m bo gracz nie musi idealnie ustawić cursor — snap działa magnetycznie.")]
        public float snapToleranceMeters = 3.0f;

        [Tooltip("Krok rotacji w stopniach przy Ctrl+Scroll.")]
        public float rotationStepDeg = 5f;

        [Header("State (debug only — set by Update)")]
        [SerializeField] private bool _isActive;
        [SerializeField] private string _currentSchemaName;
        [SerializeField] private float _currentRotationDeg;
        [SerializeField] private bool _currentMirror;
        [SerializeField] private bool _hasSnap;
        [SerializeField] private int _snappedEndpointCount;
        [SerializeField] private bool _autoRotationApplied;
        [SerializeField] private bool _hasAdaptiveProposal;
        [SerializeField] private float _proposedSpacingMeters;

        [Header("MD-4 features")]
        [Tooltip("Włącz A13 prompt — sprawdza czy zmiana spacing dałaby lepszy multi-snap. Heavy operation.")]
        public bool enableAdaptivePrompt = true;

        [Tooltip("Co ile frame'ów sprawdzać A13 candidate (1 = co frame, 30 = co pół sekundy przy 60fps).")]
        public int adaptiveCheckFrameThrottle = 30;

        [Tooltip("Włącz auto-rotation correction (±45° dociąganie do współliniowości najbliższego toru).")]
        public bool enableAutoRotation = true;

        [Tooltip("Sticky snap release distance (m) — gdy najbliższy endpoint schematu oddali się od " +
                 "snap target dalej niż to, sticky się dezaktywuje. Hysteresis vs snapToleranceMeters: " +
                 "acquire działa przy ≤3m, sticky drży aż endpoint nie odpłynie >8m. Za duża wartość = " +
                 "schemat udaje że jest snap'nięty mimo że wizualnie odjechał daleko.")]
        public float stickyReleaseDistance = 8f;

        [Tooltip("Debug log — wypisuje stan snap w każdym frame'cie (włącz tylko podczas debugowania).")]
        public bool showSnapDebugLog = false;

        private int _debugLogCounter;

        // ── Runtime state ──
        private TurnoutSchemaDefinition _currentSchema;
        private SchemaGeometry _currentGeometry;        // generative or snapshot-converted geometry
        private SchemaPreviewRenderer _previewRenderer;
        private Vector3 _cursorWorldPos;
        private Vector3 _snapTranslation;               // offset applied gdy snap aktywny
        private SnapPointSystem _snapPointSystem;
        private TrackGraph _trackGraph;
        private PrefabTrackBuilder _trackBuilder;
        private Camera _mainCamera;
        private SnapResult _lastSnapResult;
        private int _adaptiveCheckCounter;

        public bool IsActive => _isActive;
        public TurnoutSchemaDefinition CurrentSchema => _currentSchema;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            CleanupPreview();
        }
    }
}
