using System.Collections.Generic;
using TMPro;
using UnityEngine;
using RailwayManager;
using RailwayManager.Core;
using RailwayManager.Core.Rendering;
using RailwayManager.SharedUI;

namespace MapSystem
{
    /// <summary>
    /// M9c-5: Wizualizator pojazdów idle na mapie 2D.
    /// Subskrybuje <see cref="VehicleLocationService.OnLocationChanged"/> i renderuje
    /// małe ikony dla pojazdów w stanie <see cref="VehicleLocationType.AtStation"/>
    /// (stoją na peronie między kursami).
    ///
    /// Grupuje po stationId — wiele pojazdów na jednej stacji pokazuje się jako
    /// jeden icon z licznikiem "×N" zamiast N nakładających się ikon.
    ///
    /// Pojazdy OnRoute są renderowane przez TrainRunSimulator (to nie ten sam mechanizm).
    /// Pojazdy InDepot nie są widoczne na mapie (są w 3D zajezdni).
    /// </summary>
    public class IdleVehicleVisualizer : MonoBehaviour
    {
        const float IconYHeight = 11f;           // nad railways (8), pod trainami (12)
        const float IconSize = 150f;             // mniejszy niż train (300)
        static readonly Color IdleColor = new Color(0.85f, 0.85f, 0.4f, 0.9f); // żółtawy

        Transform _container;
        Material _material;
        bool _subscribedToLocationService;

        /// <summary>Jeden visual per station (group rendering).</summary>
        class StationGroup
        {
            public int stationId;
            public Vector2 worldPos;
            public GameObject visual;
            public TextMeshPro countLabel;
            public HashSet<int> vehicleIds = new();
        }

        readonly Dictionary<int, StationGroup> _groups = new(); // stationId → group
        readonly Dictionary<int, int> _vehicleToStation = new(); // vehicleId → stationId (reverse lookup)

        void Awake()
        {
            var containerGo = new GameObject("IdleVehicleVisuals");
            containerGo.layer = SceneController.MapLayer;
            containerGo.transform.SetParent(transform);
            _container = containerGo.transform;

            _material = MaterialFactory.CreateUnlit();
            MaterialFactory.SetBaseColor(_material, IdleColor);
        }

        void OnEnable()
        {
            // Delay subscribe — service może jeszcze nie istnieć
            Invoke(nameof(SubscribeLocationService), 0.1f);
        }

        void OnDisable()
        {
            CancelInvoke(nameof(SubscribeLocationService));
            UnsubscribeLocationService();
        }

        void OnDestroy()
        {
            UnsubscribeLocationService();
            if (_material != null) Destroy(_material);
        }

        void SubscribeLocationService()
        {
            UnsubscribeLocationService();

            var svc = VehicleLocationService.Instance ?? VehicleLocationService.EnsureExists();
            svc.OnLocationChanged += HandleLocationChanged;
            _subscribedToLocationService = true;

            // Backfill — istniejące AtStation vehicles (gdy subscribe po wielu transitions)
            foreach (var rec in svc.GetByType(VehicleLocationType.AtStation))
                AddVehicleToStation(rec.vehicleId, rec.stationId, rec.worldMapPosition);

            Log.Info("[IdleVehicleVisualizer] Subscribed to VehicleLocationService");
        }

        void UnsubscribeLocationService()
        {
            if (!_subscribedToLocationService)
                return;

            if (VehicleLocationService.Instance != null)
                VehicleLocationService.Instance.OnLocationChanged -= HandleLocationChanged;
            _subscribedToLocationService = false;
        }

        void HandleLocationChanged(int vehicleId, VehicleLocationType oldType, VehicleLocationType newType)
        {
            // Remove z poprzedniej stacji (jeśli byl AtStation)
            if (_vehicleToStation.TryGetValue(vehicleId, out int oldStationId))
            {
                RemoveVehicleFromStation(vehicleId, oldStationId);
                _vehicleToStation.Remove(vehicleId);
            }

            // Dodaj do nowej stacji (jeśli AtStation)
            if (newType == VehicleLocationType.AtStation)
            {
                var rec = VehicleLocationService.Instance?.Get(vehicleId);
                if (rec != null)
                    AddVehicleToStation(vehicleId, rec.stationId, rec.worldMapPosition);
            }
        }

        void AddVehicleToStation(int vehicleId, int stationId, Vector2 pos)
        {
            if (!_groups.TryGetValue(stationId, out var group))
            {
                group = CreateGroup(stationId, pos);
                _groups[stationId] = group;
            }
            group.vehicleIds.Add(vehicleId);
            _vehicleToStation[vehicleId] = stationId;
            UpdateGroupLabel(group);
        }

        void RemoveVehicleFromStation(int vehicleId, int stationId)
        {
            if (!_groups.TryGetValue(stationId, out var group)) return;
            group.vehicleIds.Remove(vehicleId);
            if (group.vehicleIds.Count == 0)
            {
                if (group.visual != null) Destroy(group.visual);
                _groups.Remove(stationId);
            }
            else
            {
                UpdateGroupLabel(group);
            }
        }

        StationGroup CreateGroup(int stationId, Vector2 pos)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = $"IdleStation_{stationId}";
            go.layer = SceneController.MapLayer;
            go.transform.SetParent(_container);
            go.transform.position = new Vector3(pos.x, IconYHeight, pos.y);
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // poziomo
            go.transform.localScale = new Vector3(IconSize, IconSize, 1f);

            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null) mr.sharedMaterial = _material;
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);

            // Label z licznikiem
            var textGo = new GameObject("Count");
            textGo.transform.SetParent(go.transform, false);
            textGo.transform.localPosition = new Vector3(0f, 0f, -0.5f);
            textGo.transform.localRotation = Quaternion.Euler(0f, 0f, 0f);
            textGo.transform.localScale = new Vector3(2f / IconSize, 2f / IconSize, 1f); // skala w local
            textGo.layer = SceneController.MapLayer;

            // TMP zamiast legacy TextMesh (lepsza jakość SDF + polskie znaki via UITheme.TmpFont
            // fallback chain). Skala 3D-world ustawiona przez transform.localScale powyżej.
            var tmp = textGo.AddComponent<TextMeshPro>();
            tmp.text = "1";
            tmp.fontSize = 6;  // TMP używa innej skali niż TextMesh (40 + characterSize 0.15) — kalibracja empiryczna pod localScale 2/IconSize
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.black;
            tmp.font = UITheme.TmpFont;
            tmp.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Overflow;

            return new StationGroup
            {
                stationId = stationId,
                worldPos = pos,
                visual = go,
                countLabel = tmp,
                vehicleIds = new HashSet<int>()
            };
        }

        void UpdateGroupLabel(StationGroup group)
        {
            if (group.countLabel == null) return;
            int n = group.vehicleIds.Count;
            group.countLabel.text = n > 1 ? $"×{n}" : "";
        }
    }
}
