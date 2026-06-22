using UnityEngine;

namespace RailwayManager.Personnel
{
    /// <summary>
    /// M8-10: Placeholder "brama zajezdni" — punkt spawnu/despawnu pracownikow.
    ///
    /// Gracz moze umiescic w scenie Depot (Add Component → DepotGateMarker). Pozycja transform
    /// jest uzywana przez <see cref="PersonnelDispatcher3D"/> jako spawn/despawn point.
    ///
    /// Jesli nie ma zadnego DepotGateMarker w scenie: fallback na <see cref="DefaultPosition"/>
    /// (domyslnie 0,0,0).
    ///
    /// M-Models: zastap prefabem bramy (siatka + portal wejscia) — sam komponent pozostaje.
    /// </summary>
    public class DepotGateMarker : MonoBehaviour
    {
        public static DepotGateMarker Instance { get; private set; }

        public static Vector3 DefaultPosition { get; set; } = Vector3.zero;

        /// <summary>Globalna pozycja bramy — Instance lub DefaultPosition.</summary>
        public static Vector3 GetPosition()
        {
            if (Instance != null && Instance.gameObject.activeInHierarchy)
                return Instance.transform.position;

            // Sprobuj znalezc w scenie (np. po reload)
            var found = FindAnyObjectByType<DepotGateMarker>();
            if (found != null)
            {
                Instance = found;
                return found.transform.position;
            }

            return DefaultPosition;
        }

        void Awake()
        {
            if (Instance == null) Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.2f, 0.8f, 0.3f, 0.6f);
            Gizmos.DrawSphere(transform.position, 0.5f);
            Gizmos.color = new Color(0.2f, 0.8f, 0.3f, 1f);
            Gizmos.DrawWireSphere(transform.position, 1f);
        }
    }
}
