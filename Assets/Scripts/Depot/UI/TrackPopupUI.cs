using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using RailwayManager.Core;
using RailwayManager.SharedUI;
using RailwayManager.SharedUI.Localization;

namespace DepotSystem
{
    /// <summary>
    /// Popup po klikniÄ™ciu toru w trybie Select.
    /// Pokazuje nazwÄ™, info, sieÄ‡ trakcyjnÄ…, przyciski akcji
    /// oraz przycisk "Dodaj tory rĂłwnolegĹ‚e" z dialogiem.
    /// Generuje siÄ™ proceduralnie.
    /// </summary>
    public partial class TrackPopupUI : MonoBehaviour
    {
        [Header("Map Reference")]
        [Tooltip("KÄ…t torĂłw zewnÄ™trznych wzglÄ™dem osi Z+ (world). Ustaw tak samo jak w TrackBuildStateMachine.")]
        [SerializeField] private float mapNorthAngle = 90f;

        [Header("Colors")]
        [SerializeField] private Color panelColor = default;
        [SerializeField] private Color headerColor = default;
        [SerializeField] private Color sectionColor = default;
        [SerializeField] private Color inputColor = default;
        [SerializeField] private Color primaryButtonColor = default;
        [SerializeField] private Color secondaryButtonColor = default;
        [SerializeField] private Color dangerButtonColor = default;
        [SerializeField] private Color catenaryOnColor = Color.yellow;
        [SerializeField] private Color catenaryOffColor = default;

        // GĹ‚Ăłwny popup
        private GameObject popupPanel;
        private TMP_InputField trackNameInput;
        private TextMeshProUGUI trackTypeText;
        private TMP_InputField lengthInput;
        private TMP_InputField radiusInput;
        private TextMeshProUGUI angleValueText;
        private Button applyParamsButton;
        private Image catenaryIcon;
        private Button renameButton;
        private Button deleteButton;
        private Button toggleCatenaryButton;
        private Button parallelButton;
        private Button closeButton;

        // Dialog torĂłw rĂłwnolegĹ‚ych
        private GameObject parallelDialog;
        private TMP_InputField leftCountInput;
        private TMP_InputField rightCountInput;
        private TMP_InputField spacingInput;
        private Toggle catenaryToggle;
        private Button generateButton;
        private Button cancelParallelButton;

        // Popup rozjazdu
        private GameObject turnoutPopupPanel;
        private TextMeshProUGUI turnoutNameText;
        private TextMeshProUGUI turnoutInfoText;
        private Button deleteTurnoutButton;
        private Button closeTurnoutButton;

        private TrackGraph trackGraph;
        private ParallelTrackGenerator parallelGenerator;
        private int selectedTrackId = -1;
        private TurnoutEntity selectedTurnout;
        private Camera mainCamera;

        // â”€â”€ Input System â”€â”€
        private InputActions _inputActions;
        private InputActions.VehicleActions _vehicleActions;
        private InputActions.UIPopupActions _popupActions;

        void Awake()
        {
            ApplyDefaultPalette();
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

        void OnDestroy()
        {
            _inputActions?.Dispose();
        }

        /// <summary>
        /// Czy ktorykolwiek z popupow jest widoczny (main track popup / parallel dialog / turnout popup).
        /// Uzywane przez PauseMenuUI dla koordynacji ESC.
        /// </summary>
        public bool IsAnyPopupVisible()
        {
            if (popupPanel != null && popupPanel.activeSelf) return true;
            if (parallelDialog != null && parallelDialog.activeSelf) return true;
            if (turnoutPopupPanel != null && turnoutPopupPanel.activeSelf) return true;
            return false;
        }

        void Start()
        {
            mainCamera = Camera.main;
            trackGraph = DepotServices.Get<TrackGraph>();
            parallelGenerator = DepotServices.Get<ParallelTrackGenerator>();
        }

        void Update()
        {
            if (DepotUIManager.Instance == null) return;

            // Selekcja torĂłw: dziaĹ‚a w trybie Select ORAZ gdy ĹĽadne narzÄ™dzie budowania nie jest aktywne
            var tool = DepotUIManager.Instance.CurrentTool;
            if (tool != ToolMode.Select) return;

            // ESC sprawdz PRZED guardem IsPointerOverUI (patrz BuildingPopupUI rationale)
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                if (parallelDialog != null && parallelDialog.activeSelf)
                {
                    PauseMenuUI.LastEscConsumedFrame = Time.frameCount;
                    CloseParallelDialog();
                    return;
                }
                if (turnoutPopupPanel != null && turnoutPopupPanel.activeSelf)
                {
                    PauseMenuUI.LastEscConsumedFrame = Time.frameCount;
                    CloseTurnoutPopup();
                    return;
                }
                if (popupPanel != null && popupPanel.activeSelf)
                {
                    PauseMenuUI.LastEscConsumedFrame = Time.frameCount;
                    ClosePopup();
                    return;
                }
            }

            if (DepotUIManager.Instance.IsPointerOverUI()) return;

            // JeĹ›li w trakcie wybierania docelowego toru dla consist'u â€” skip
            // (DepotConsistSelectionHandler obsĹ‚uĹĽy klik)
            if (DepotConsistSelectionHandler.HasActiveSelection) return;

            if (_vehicleActions.Select.WasPressedThisFrame())
                TrySelectTrack();
        }

        private void TrySelectTrack()
        {
            if (mainCamera == null) mainCamera = Camera.main;
            if (mainCamera == null) return;
            if (Mouse.current == null) return;

            Ray ray = mainCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
            if (Physics.Raycast(ray, out RaycastHit hit, 1000f))
            {
                // Szukaj tagu "Track" na trafionym obiekcie lub jego przodkach
                Transform trackRoot = FindTrackRoot(hit.collider.transform);

                if (trackRoot != null)
                {
                    var builder = DepotServices.Get<PrefabTrackBuilder>();
                    if (builder != null)
                    {
                        foreach (var placed in builder.PlacedTracks)
                        {
                            if (placed.TrackObject == trackRoot.gameObject)
                            {
                                // SprawdĹş czy segment naleĹĽy do rozjazdu
                                if (builder.TryGetTurnoutForTrack(placed.GraphTrackId, out TurnoutEntity turnout))
                                {
                                    ShowTurnoutPopup(turnout, hit.point);
                                }
                                else
                                {
                                    ShowPopup(placed.GraphTrackId, hit.point);
                                }
                                return;
                            }
                        }
                    }
                }
                else
                {
                    // KlikniÄ™to poza torem - zamknij popup
                    if (popupPanel != null && popupPanel.activeSelf)
                        ClosePopup();
                    if (turnoutPopupPanel != null && turnoutPopupPanel.activeSelf)
                        CloseTurnoutPopup();
                }
            }
            else
            {
                // KlikniÄ™to w pustkÄ™ - zamknij popup
                if (popupPanel != null && popupPanel.activeSelf)
                    ClosePopup();
                if (turnoutPopupPanel != null && turnoutPopupPanel.activeSelf)
                    CloseTurnoutPopup();
            }
        }

        /// <summary>
        /// Szuka roota toru w hierarchii. Collider moĹĽe byÄ‡ dzieckiem TrackObject.
        /// Zwraca Transform z tagiem "Track" ktĂłry jest w PlacedTracks.
        /// </summary>
        private Transform FindTrackRoot(Transform target)
        {
            // IdĹş w gĂłrÄ™ hierarchii szukajÄ…c obiektu z tagiem "Track"
            // ktĂłry jest root'em toru (bezpoĹ›rednio pod tracksParent)
            Transform current = target;
            Transform lastTagged = null;

            while (current != null)
            {
                if (current.CompareTag("Track"))
                    lastTagged = current;
                current = current.parent;
            }

            return lastTagged;
        }

        private void ApplyDefaultPalette()
        {
            if (panelColor == default)
                panelColor = UITheme.WithAlpha(UITheme.OverlayPanelStrong, 0.98f);
            if (headerColor == default)
                headerColor = UITheme.TopBarInset;
            if (sectionColor == default)
                sectionColor = UITheme.WithAlpha(UITheme.TopBarInset, 0.95f);
            if (inputColor == default)
                inputColor = UITheme.TopBarInset;
            if (primaryButtonColor == default)
                primaryButtonColor = UITheme.PrimaryAccent;
            if (secondaryButtonColor == default)
                secondaryButtonColor = UITheme.SecondarySurface;
            if (dangerButtonColor == default)
                dangerButtonColor = UITheme.Danger;
            if (catenaryOffColor == default)
                catenaryOffColor = UITheme.WithAlpha(UITheme.Border, 0.95f);
        }

    }
}
