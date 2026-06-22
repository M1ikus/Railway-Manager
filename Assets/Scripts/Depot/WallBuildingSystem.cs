using System.Collections.Generic;
using UnityEngine;
using RailwayManager.Core;

namespace DepotSystem
{
    /// <summary>
    /// Typ otworu w ścianie (drzwi / okno).
    /// </summary>
    public enum OpeningType { Door, Window, TrackGate }

    /// <summary>
    /// Dane otworu wstawionego w ścianę.
    /// </summary>
    [System.Serializable]
    public class WallOpening
    {
        public int openingId;
        public OpeningType type;
        /// <summary>Pozycja środka otworu wzdłuż ściany w metrach od startPos.</summary>
        public float distanceOnWall;
        public GameObject openingObject;
    }

    /// <summary>
    /// Dane jednego segmentu ściany.
    /// </summary>
    [System.Serializable]
    public class WallSegment
    {
        public int wallId;
        public Vector3 startPos;
        public Vector3 endPos;
        public float height = 3f;
        public GameObject wallObject;
        public List<WallOpening> openings = new();

        /// <summary>Identyfikator budynku (grupy 4 ścian) do którego należy.</summary>
        public int buildingId = -1;

        public float Length => Vector3.Distance(startPos, endPos);

        /// <summary>Kierunek (znormalizowany) od start do end.</summary>
        public Vector3 Direction => (endPos - startPos).normalized;
    }

    /// <summary>
    /// System budowania ścian w zajezdni.
    /// Mechanika: zaznacz dwa narożniki prostokąta → powstają 4 ściany.
    /// Obsługuje też wstawianie drzwi, okien oraz wyburzanie (ściany + tory).
    ///
    /// Klasa rozbita na partial files:
    /// - <c>WallBuildingSystem.cs</c>           — pola, lifecycle, Update dispatcher (ten plik)
    /// - <c>WallBuildingSystem.Build.cs</c>     — budowanie pomieszczeń (rect z 2 kliknięć)
    /// - <c>WallBuildingSystem.Mesh.cs</c>      — generowanie mesh ścian + ramy drzwi/okien
    /// - <c>WallBuildingSystem.Openings.cs</c>  — wstawianie drzwi i okien
    /// - <c>WallBuildingSystem.Selection.cs</c> — narzędzie Select
    /// - <c>WallBuildingSystem.Demolish.cs</c>  — narzędzie Demolish (ściany + tory + sieć trakcyjna)
    /// - <c>WallBuildingSystem.Removal.cs</c>   — public API: Remove*/UndoCreate*/UndoRemove*
    /// - <c>WallBuildingSystem.Helpers.cs</c>   — find/color/coords/preview objects
    /// </summary>
    public partial class WallBuildingSystem : MonoBehaviour
    {
        // ─── Konfiguracja ───
        [Header("Wall Settings")]
        [SerializeField] private float wallThickness = 0.2f;
        [SerializeField] private float wallHeight = 3f;
        [SerializeField] private float gridSize = 1f;
        [SerializeField] private float minBuildingSize = 2f;

        [Header("Door / Window")]
        [SerializeField] private float doorWidth = 1f;
        [SerializeField] private float doorHeight = 2f;
        [SerializeField] private float windowWidth = 1f;
        [SerializeField] private float windowHeight = 1f;
        [SerializeField] private float windowBottomOffset = 1f;

        [Header("MM-15: Track gate (brama wjazdowa do warsztatu)")]
        [SerializeField] private float trackGateWidth = 4f;

        /// <summary>MM-15: szerokość otworu per typ (helper żeby nie duplikować ?:).</summary>
        public float GetOpeningWidth(OpeningType type) => type switch
        {
            OpeningType.Door => doorWidth,
            OpeningType.Window => windowWidth,
            OpeningType.TrackGate => trackGateWidth,
            _ => doorWidth,
        };

        [Header("Colors")]
        [SerializeField] private Color previewValid = new Color(1f, 0.9f, 0.3f, 0.4f);
        [SerializeField] private Color previewInvalid = new Color(1f, 0.3f, 0.3f, 0.4f);
        [SerializeField] private Color wallColor = new Color(0.7f, 0.7f, 0.7f, 1f);
        [SerializeField] private Color selectedColor = new Color(0.3f, 0.7f, 1f, 1f);
        [SerializeField] private Color demolishColor = new Color(1f, 0.2f, 0.2f, 0.8f);
        [SerializeField] private Color doorFrameColor = new Color(0.45f, 0.3f, 0.15f, 1f);
        [SerializeField] private Color windowGlassColor = new Color(0.6f, 0.8f, 1f, 0.4f);

        // ─── Stan ───
        private enum WallBuildState { Idle, PlacingCornerA, PreviewingRect }
        private WallBuildState state = WallBuildState.Idle;

        private Vector3 cornerA;
        private GameObject previewParent;
        private GameObject previewFill;
        private LineRenderer[] previewLines = new LineRenderer[4];
        private TextMesh previewInfoTextMesh;
        private TextMesh previewStatusTextMesh;
        private WallBuildValidationReason previewValidationReason = WallBuildValidationReason.None;
        private bool rectEndSelectionPaused = false;

        [Header("Preview HUD")]
        [SerializeField] private float previewInfoHeight = 1.95f;
        [SerializeField] private float previewStatusHeight = 3.05f;

        private const string RectPausedMessage =
            "Pierwszy rog zachowany - porusz myszka, aby wskazac nowy przeciwlegly rog - ESC wylacz";

        // Opening preview
        private GameObject openingPreview;
        private WallSegment hoveredWall;

        // Selection
        private WallSegment selectedWall;

        // Demolish
        private WallSegment demolishTarget;
        private PlacedTrackSegment demolishTrackTarget;

        // ─── Dane ───
        private List<WallSegment> allWalls = new();
        private int nextWallId = 1;
        private int nextOpeningId = 1;
        private int nextBuildingId = 1;

        private Camera mainCamera;
        private Bounds? buildableArea;

        // ─── Eventy ───
        /// <summary>Wywoływany po każdej zmianie ścian (dodanie/usunięcie/edycja).</summary>
        public event System.Action OnWallsChanged;

        /// <summary>Wszystkie ściany w systemie.</summary>
        public IReadOnlyList<WallSegment> AllWalls => allWalls;

        // ═══════════════════════════════════════════
        //  SAVE/LOAD API (zamiast reflection w DepotSavable)
        // ═══════════════════════════════════════════

        /// <summary>
        /// Bulk replace ścian + counter'ów z save'a. Public API zamiast reflection na
        /// private fieldy (`allWalls`, `nextWallId`, `nextOpeningId`, `nextBuildingId`).
        /// Counter'y &lt;1 są klampowane do 1 (musi być &gt;=1, allokuje pre-increment).
        /// </summary>
        public void RestoreFromSave(IList<WallSegment> walls,
                                    int nextWallIdIn, int nextOpeningIdIn, int nextBuildingIdIn)
        {
            allWalls.Clear();
            if (walls != null) allWalls.AddRange(walls);
            nextWallId = nextWallIdIn > 0 ? nextWallIdIn : 1;
            nextOpeningId = nextOpeningIdIn > 0 ? nextOpeningIdIn : 1;
            nextBuildingId = nextBuildingIdIn > 0 ? nextBuildingIdIn : 1;
            OnWallsChanged?.Invoke();
        }

        /// <summary>Reset wall system (jak nowa gra / DepotSavable.InitializeDefault).</summary>
        public void ClearAllForReset()
        {
            allWalls.Clear();
            nextWallId = 1;
            nextOpeningId = 1;
            nextBuildingId = 1;
            OnWallsChanged?.Invoke();
        }

        // ═══════════════════════════════════════════
        //  LIFECYCLE
        // ═══════════════════════════════════════════

        // ── Input System ──
        private InputActions _inputActions;
        private InputActions.ToolBuildActions _toolBuild;

        void Awake()
        {
            _inputActions = new InputActions();
            RailwayManager.Core.Settings.RebindingService.ApplyOverridesTo(_inputActions);
            _toolBuild = _inputActions.ToolBuild;
        }

        void OnEnable()
        {
            _toolBuild.Enable();
        }

        void OnDisable()
        {
            _toolBuild.Disable();
        }

        void OnDestroy()
        {
            _toolGate?.Stop();
            _inputActions?.Dispose();
        }

        // ── Tool-mode gate ──
        private ToolModeGate _toolGate;

        void Start()
        {
            mainCamera = Camera.main;

            var ground = DepotServices.Get<GroundGenerator>();
            if (ground != null)
                buildableArea = ground.BuildableArea;

            CreatePreviewObjects();

            _toolGate = new ToolModeGate(
                this,
                m => m == ToolMode.BuildRoom || m == ToolMode.Demolish || m == ToolMode.Select,
                OnToolDeactivated);
            _toolGate.Start();
        }

        private void OnToolDeactivated()
        {
            if (state != WallBuildState.Idle) CancelBuild();
            HideWallPreview();
            HideOpeningPreview();
            ClearSelection();
            ClearDemolishTarget();
            ClearDemolishTrackTarget();
        }

        void Update()
        {
            var tool = DepotUIManager.Instance.CurrentTool;

            // --- Budowanie pomieszczeń ---
            if (tool == ToolMode.BuildRoom)
            {
                var action = DepotUIManager.Instance.CurrentRoomAction;
                if (action == RoomActionMode.BuildRoom)
                {
                    HandleWallBuild();
                }
                else
                {
                    if (state != WallBuildState.Idle) CancelBuild();
                    HideWallPreview();
                    HideOpeningPreview();
                }
                ClearSelection();
                ClearDemolishTarget();
                ClearDemolishTrackTarget();
                return;
            }

            // --- Wyburzanie ---
            if (tool == ToolMode.Demolish)
            {
                HandleDemolish();
                HideWallPreview();
                HideOpeningPreview();
                ClearSelection();
                return;
            }

            // --- Selekcja ---
            if (tool == ToolMode.Select)
            {
                HandleSelect();
                HideWallPreview();
                HideOpeningPreview();
                ClearDemolishTarget();
                ClearDemolishTrackTarget();
            }
        }

        void LateUpdate()
        {
            if (mainCamera == null) mainCamera = Camera.main;
            if (mainCamera == null) return;

            if (previewInfoTextMesh != null && previewInfoTextMesh.gameObject.activeSelf)
                previewInfoTextMesh.transform.rotation = mainCamera.transform.rotation;

            if (previewStatusTextMesh != null && previewStatusTextMesh.gameObject.activeSelf)
                previewStatusTextMesh.transform.rotation = mainCamera.transform.rotation;
        }
    }

    public enum WallBuildValidationReason
    {
        None,
        Ready,
        TooSmall,
        RoomTypeTooSmall,
        OutsideBuildableArea,
        OverlapWalls,
        OverlapTracks
    }
}
