using System.Collections.Generic;
using UnityEngine;
using RailwayManager.Core;

namespace DepotSystem
{
    /// <summary>
    /// Główny koordynator systemu zajezdni 3D.
    /// Przechowuje referencje do podsystemów i inicjalizuje zajezdnię.
    /// </summary>
    public class DepotManager : MonoBehaviour
    {
        [Header("Depot Configuration")]
        [Tooltip("Punkt wejściowy do zajezdni (połączenie z główną siecią 2D)")]
        public Vector2 entryPoint = Vector2.zero;

        [Header("System References")]
        public GroundGenerator groundGenerator;
        public DepotFenceSystem fenceSystem;
        public TrackGraph trackGraph;
        public PrefabTrackBuilder trackBuilder;
        public CatenaryGenerator catenaryGenerator;
        public ElectrificationStateMachine electrificationStateMachine;
        public VehicleController vehicleController;
        public ExitTrackController exitTrackController;
        public DepotOrbitCamera orbitCamera;
        public TrackBuildStateMachine trackBuildStateMachine;
        public SnapPointSystem snapPointSystem;
        public ParallelTrackGenerator parallelTrackGenerator;
        public TurnoutPlacer turnoutPlacer;
        public TurnoutPlacementStateMachine turnoutPlacementStateMachine;
        public WallBuildingSystem wallBuildingSystem;
        public RoomDetectionSystem roomDetectionSystem;

        [Header("Default Layout")]
        [Tooltip("Czy generować domyślny układ torów na starcie")]
        public bool generateDefaultLayout = true;

        [Tooltip("Odstęp między torami wjazdowymi (m)")]
        public float entryTrackSpacing = 5f;

        [Tooltip("Długość toru wjazdowego wewnątrz zajezdni (m)")]
        public float entryTrackInternalLength = 30f;

        [Header("Debug")]
        public bool showGizmos = true;

        void Start()
        {
            FindReferences();

            // M-Economy Faza 5: layout startowy zajezdni jest DARMOWY (setup, nie budowa gracza) —
            // suppress, żeby external tracks / default layout nie obciążały gracza na starcie.
            ConstructionBilling.SuppressCharging = true;
            try
            {
                // Permanentne tory zewnętrzne (za bramą) — ZAWSZE generowane, niezależnie od
                // generateDefaultLayout. Niemożliwe do usunięcia przez gracza. Służą jako
                // punkt wyjazdu/wjazdu z zajezdni (M9b Etap 5 — detekcja przez IsOutsideDepot).
                GenerateExternalTracks();

                if (generateDefaultLayout)
                    GenerateDefaultLayout();
            }
            finally
            {
                ConstructionBilling.SuppressCharging = false;
            }

            Log.Info("[DepotManager] Depot initialized");
        }

        [Header("External (Permanent) Tracks")]
        [Tooltip("Długość odcinka zewnętrznego (od bramy w lewo, poza płot) [m]. Musi być >> długość pociągu, żeby cały skład mieścił się za ogrodzeniem.")]
        public float externalTrackOutsideLength = 150f;

        [Tooltip("Długość odcinka wewnętrznego (od bramy w prawo, wewnątrz obszaru budowlanego) [m]. Krótki pień do którego gracz podpina swoje tory.")]
        public float externalTrackInsideLength = 30f;

        [Tooltip("Liczba torów zewnętrznych przy bramie lewej. Docelowo skalowane wraz z rozbudową zajezdni (initially 1, upgrades add more).")]
        public int externalTrackCount = 1;

        [Tooltip("Odstęp między torami zewnętrznymi (m, gdy count > 1)")]
        public float externalTrackSpacing = 5f;

        /// <summary>
        /// Generuje permanentne tory zewnętrzne za bramą lewą (od strony torów).
        /// Zaczynają się dokładnie na linii płotu i wychodzą <c>externalTrackLength</c>
        /// metrów na zewnątrz (ujemne X). Oznaczone IsPermanent=true — gracz nie może ich
        /// usunąć ani zmodyfikować. DepotMovementSimulator traktuje ich zewnętrzne node'y
        /// jako punkt wyjścia (IsOutsideDepot → despawn visual + emit OnConsistExitedDepot).
        /// </summary>
        [ContextMenu("Generate External Tracks")]
        public void GenerateExternalTracks()
        {
            if (trackBuilder == null) trackBuilder = DepotServices.Get<PrefabTrackBuilder>();
            if (trackGraph == null) trackGraph = DepotServices.Get<TrackGraph>();
            if (fenceSystem == null) fenceSystem = DepotServices.Get<DepotFenceSystem>();

            if (trackBuilder == null || trackGraph == null || fenceSystem == null)
            {
                Log.Warn("[DepotManager] GenerateExternalTracks: brakuje trackBuilder/trackGraph/fenceSystem");
                return;
            }

            Vector3 gatePos = fenceSystem.GetLeftGatePosition();
            int count = Mathf.Max(1, externalTrackCount);

            // Rozłóż tory symetrycznie wokół środka bramy
            float totalSpan = (count - 1) * externalTrackSpacing;
            float startZ = gatePos.z - totalSpan / 2f;

            for (int i = 0; i < count; i++)
            {
                float z = startZ + i * externalTrackSpacing;

                // Spannuje bramę: outer (poza płotem) → inner (wewnątrz, pień dla gracza)
                Vector3 outerEnd = new Vector3(gatePos.x - externalTrackOutsideLength, 0f, z);
                Vector3 innerEnd = new Vector3(gatePos.x + externalTrackInsideLength, 0f, z);

                string name = count == 1 ? "Tor zewnętrzny" : $"Tor zewnętrzny {i + 1}";
                var segment = trackBuilder.PlaceTrackSegment(outerEnd, innerEnd, name, DepotTrackType.Entry);
                if (segment == null) continue;

                // Oznacz jako permanentny — blokada usuwania przez PrefabTrackBuilder.RemoveTrack
                var trackData = trackGraph.GetTrack(segment.GraphTrackId);
                if (trackData != null) trackData.IsPermanent = true;

                Log.Info($"[DepotManager] External track #{segment.GraphTrackId} ('{name}') generated: " +
                          $"{outerEnd} → {innerEnd} (outside={externalTrackOutsideLength}m, inside={externalTrackInsideLength}m)");
            }
        }

        /// <summary>
        /// Znajduje referencje do podsystemów (jeśli nie przypisane)
        /// </summary>
        private void FindReferences()
        {
            if (groundGenerator == null) groundGenerator = DepotServices.Get<GroundGenerator>();
            if (fenceSystem == null) fenceSystem = DepotServices.Get<DepotFenceSystem>();
            if (trackGraph == null) trackGraph = DepotServices.Get<TrackGraph>();
            if (trackBuilder == null) trackBuilder = DepotServices.Get<PrefabTrackBuilder>();
            if (catenaryGenerator == null) catenaryGenerator = DepotServices.Get<CatenaryGenerator>();
            if (electrificationStateMachine == null) electrificationStateMachine = DepotServices.Get<ElectrificationStateMachine>();
            if (vehicleController == null) vehicleController = DepotServices.Get<VehicleController>();
            if (exitTrackController == null) exitTrackController = DepotServices.Get<ExitTrackController>();
            if (orbitCamera == null) orbitCamera = DepotServices.Get<DepotOrbitCamera>();
            if (trackBuildStateMachine == null) trackBuildStateMachine = DepotServices.Get<TrackBuildStateMachine>();
            if (snapPointSystem == null) snapPointSystem = DepotServices.Get<SnapPointSystem>();
            if (parallelTrackGenerator == null) parallelTrackGenerator = DepotServices.Get<ParallelTrackGenerator>();
            if (turnoutPlacer == null) turnoutPlacer = DepotServices.Get<TurnoutPlacer>();
            if (turnoutPlacementStateMachine == null) turnoutPlacementStateMachine = DepotServices.Get<TurnoutPlacementStateMachine>();
            if (wallBuildingSystem == null) wallBuildingSystem = DepotServices.Get<WallBuildingSystem>();
            if (roomDetectionSystem == null) roomDetectionSystem = DepotServices.Get<RoomDetectionSystem>();

            // Dodaj brakujące komponenty
            if (trackGraph == null) trackGraph = gameObject.AddComponent<TrackGraph>();
            if (vehicleController == null) vehicleController = gameObject.AddComponent<VehicleController>();
            if (trackBuildStateMachine == null) trackBuildStateMachine = gameObject.AddComponent<TrackBuildStateMachine>();
            if (snapPointSystem == null) snapPointSystem = gameObject.AddComponent<SnapPointSystem>();
            if (parallelTrackGenerator == null) parallelTrackGenerator = gameObject.AddComponent<ParallelTrackGenerator>();
            if (turnoutPlacer == null) turnoutPlacer = gameObject.AddComponent<TurnoutPlacer>();
            if (turnoutPlacementStateMachine == null) turnoutPlacementStateMachine = gameObject.AddComponent<TurnoutPlacementStateMachine>();
            if (catenaryGenerator == null) catenaryGenerator = gameObject.AddComponent<CatenaryGenerator>();
            if (electrificationStateMachine == null) electrificationStateMachine = gameObject.AddComponent<ElectrificationStateMachine>();
            if (wallBuildingSystem == null) wallBuildingSystem = gameObject.AddComponent<WallBuildingSystem>();
            if (roomDetectionSystem == null) roomDetectionSystem = gameObject.AddComponent<RoomDetectionSystem>();

            // M9b: DepotMovementSimulator — driver ruchu pociągów w zajezdni
            if (DepotServices.Get<DepotMovementSimulator>() == null)
            {
                var simGo = new GameObject("DepotSimulation");
                simGo.AddComponent<DepotMovementSimulator>();
                simGo.AddComponent<ConsistPopupUI>();
                simGo.AddComponent<DepotConsistSelectionHandler>();
                simGo.AddComponent<ConsistCouplingPromptUI>();   // TD-032 popup-na-styku
                simGo.AddComponent<Nav.DepotNavService>();       // TD-033 nawigacja pracowników
                simGo.AddComponent<Furniture.Placement.FurnitureOccupancyService>(); // TD-034 occupancy mebli
            }
        }

        /// <summary>
        /// Generuje domyślny układ torów zajezdni
        /// </summary>
        [ContextMenu("Generate Default Layout")]
        public void GenerateDefaultLayout()
        {
            if (trackBuilder == null || trackGraph == null)
            {
                Log.Warn("[DepotManager] TrackBuilder or TrackGraph missing!");
                return;
            }

            Bounds ba = groundGenerator != null
                ? groundGenerator.BuildableArea
                : new Bounds(Vector3.zero, new Vector3(2000, 10, 400));

            // Tory wjazdowe są teraz zarządzane przez GenerateExternalTracks() jako permanentne
            // (zawsze obecne, nieusuwalne). GenerateDefaultLayout zostawia pustą zajezdnię —
            // gracz sam buduje parking/maneuver podpięte do pnia external tracks.

            // Narzędzia budowania są domyślnie odblokowane w BuildMenuUI
            // (konfiguracja w buildToolDefs). Nie potrzeba ręcznego UnlockTool.

            // Ustaw kamerę na tory wjazdowe
            var camera = DepotServices.Get<DepotOrbitCamera>();
            if (camera != null && fenceSystem != null)
            {
                Vector3 gatePos = fenceSystem.GetLeftGatePosition();
                camera.FocusOn(new Vector3(gatePos.x + entryTrackInternalLength / 2f, 0, gatePos.z), true);
            }

            Log.Info("[DepotManager] Generated empty depot with 2 entry tracks");
        }

        /// <summary>
        /// Czyści zajezdnię
        /// </summary>
        [ContextMenu("Clear Depot")]
        public void ClearDepot()
        {
            if (trackBuilder != null) trackBuilder.ClearAll();
            if (catenaryGenerator != null) catenaryGenerator.ClearExistingNetwork();
            if (vehicleController != null) vehicleController.ClearVehicles();

            Log.Info("[DepotManager] Depot cleared");
        }

        void OnDrawGizmos()
        {
            if (!showGizmos) return;

            // Punkt wejściowy
            Gizmos.color = Color.green;
            Vector3 entryPos3D = new Vector3(entryPoint.x, 0, entryPoint.y);
            Gizmos.DrawSphere(entryPos3D, 5f);
        }
    }
}
