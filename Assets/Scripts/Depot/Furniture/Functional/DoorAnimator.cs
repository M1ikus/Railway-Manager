using UnityEngine;

namespace DepotSystem.Furniture.Functional
{
    /// <summary>
    /// MF-11 — placeholder animator drzwi (cuboid 1×0.2×2m rotuje 0°→90° przy
    /// zbliżeniu pracownika w <see cref="ProximityRadius"/>).
    ///
    /// Komponent dorzucany do stamped door visualization (FurniturePlacer.SpawnStampedVisual
    /// dla itemu z <see cref="SpecialPlacement.WallCell"/>).
    ///
    /// Strategia detekcji pracowników: szukamy <c>GameObject</c>'a "EmployeeVisualsContainer"
    /// (utworzony przez <c>EmployeeWalkSimulator.Awake</c> w Personnel asmdef — Depot nie referuje
    /// Personnel, więc nie używamy typu EmployeeVisual bezpośrednio). Iterujemy children po
    /// pozycji, distance check XZ. ~10-20 pracowników, raz na frame — OK perf-wise dla MVP.
    /// Polish post-EA: subscribe na event z Personnel.
    ///
    /// M-Models swap: real mesh + Animator controller (otwieranie skrzydła zamiast rotacji
    /// całego cuboida). MVP rotuje cały parent — widoczne ale placeholder.
    /// </summary>
    public class DoorAnimator : MonoBehaviour
    {
        public const float ProximityRadius = 1.5f;
        public const float OpenAngleDeg = 90f;
        public const float AnimSpeedDegPerSec = 360f;

        private Transform _container;
        private float _baseRotationY;
        private float _currentOpenDeg;
        private float _targetOpenDeg;
        private bool _baseRotationCaptured;

        void Start()
        {
            CaptureBaseRotation();
            // Lookup container created by EmployeeWalkSimulator. Może być null dopóki simulator
            // nie zainicjalizuje (lazy retry w Update).
            var containerGO = GameObject.Find("EmployeeVisualsContainer");
            if (containerGO != null) _container = containerGO.transform;
        }

        void OnEnable()
        {
            // Re-capture base rotation gdy parent się zmieni (np. po rotate w MF-7)
            _baseRotationCaptured = false;
        }

        void Update()
        {
            if (!_baseRotationCaptured) CaptureBaseRotation();

            if (_container == null)
            {
                var containerGO = GameObject.Find("EmployeeVisualsContainer");
                if (containerGO != null) _container = containerGO.transform;
            }

            bool anyoneNear = false;
            if (_container != null)
            {
                Vector3 myPos = transform.position;
                int count = _container.childCount;
                for (int i = 0; i < count; i++)
                {
                    var t = _container.GetChild(i);
                    if (t == null) continue;
                    Vector3 pos = t.position;
                    float dx = pos.x - myPos.x;
                    float dz = pos.z - myPos.z;
                    float distSq = dx * dx + dz * dz;
                    if (distSq <= ProximityRadius * ProximityRadius)
                    {
                        anyoneNear = true;
                        break;
                    }
                }
            }

            _targetOpenDeg = anyoneNear ? OpenAngleDeg : 0f;

            if (!Mathf.Approximately(_currentOpenDeg, _targetOpenDeg))
            {
                _currentOpenDeg = Mathf.MoveTowards(_currentOpenDeg, _targetOpenDeg, AnimSpeedDegPerSec * Time.deltaTime);
                transform.rotation = Quaternion.Euler(0f, _baseRotationY + _currentOpenDeg, 0f);
            }
        }

        private void CaptureBaseRotation()
        {
            _baseRotationY = transform.eulerAngles.y - _currentOpenDeg;
            _baseRotationCaptured = true;
        }
    }
}
