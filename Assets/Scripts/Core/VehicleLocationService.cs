using System;
using System.Collections.Generic;
using UnityEngine;

namespace RailwayManager.Core
{
    /// <summary>
    /// Stan lokalizacji pojazdu w świecie gry.
    ///
    /// M9c handshake Map↔Depot — pojazd zawsze gdzieś jest (nie teleportuje się).
    /// Tranzycje stanów wywoływane przez simulatory (TrainRunSimulator, DepotMovementSimulator)
    /// via VehicleLocationService API.
    /// </summary>
    public enum VehicleLocationType
    {
        /// <summary>Pojazd fizycznie w zajezdni, na torze parkingowym/manewrowym.</summary>
        InDepot,
        /// <summary>Manewruje w zajezdni jako część consist'u jadącego do bramy.</summary>
        ExitingDepot,
        /// <summary>Stoi na torze stacyjnym (mapa 2D) — peron, czeka na kurs lub skończył.</summary>
        AtStation,
        /// <summary>W trasie (aktywny TrainRun na mapie 2D).</summary>
        OnRoute,
        /// <summary>Wjeżdża przez bramę depot (aktywny DepotMovementSimulator task).</summary>
        EnteringDepot,
        /// <summary>Edge case: między stacjami bez aktywnego TrainRun (relokacja, awaria).</summary>
        InTransit,
    }

    /// <summary>Stan lokalizacji pojedynczego pojazdu (M9c).</summary>
    [Serializable]
    public class VehicleLocationRecord
    {
        public int vehicleId;
        public VehicleLocationType type = VehicleLocationType.InDepot;

        /// <summary>
        /// ID stacji — valid dla <see cref="VehicleLocationType.AtStation"/>.
        /// Dla <see cref="VehicleLocationType.InDepot"/> = <see cref="GameState.HomeDepotStationId"/>.
        /// </summary>
        public int stationId = -1;

        /// <summary>ID toru w zajezdni — valid dla InDepot. -1 = nieznany (świeżo dostarczony).</summary>
        public int depotTrackId = -1;

        /// <summary>Aktywny TrainRun (gdy OnRoute lub w trakcie handshake'u). -1 = brak.</summary>
        public int currentTrainRunId = -1;

        /// <summary>
        /// Tymczasowy consistId z DepotMovementSimulator — valid dla ExitingDepot/EnteringDepot
        /// oraz gdy pojazd jest część consist'u w zajezdni (manewr). -1 = brak.
        /// </summary>
        public int currentConsistId = -1;

        /// <summary>
        /// Pozycja 2D na mapie OSM (metry w układzie świata gry). Update'owana:
        /// - AtStation: set na pozycję peronu przy tranzycji
        /// - OnRoute: update per tick z TrainRunSimulator
        /// - InDepot/Exiting/EnteringDepot: pozycja home station (pojazd nie jest "na mapie 2D")
        /// </summary>
        public Vector2 worldMapPosition;
    }

    /// <summary>
    /// M9c: Authority dla lokalizacji każdego pojazdu gracza. Cross-scene singleton —
    /// trzyma state niezależnie od tego która scena jest aktywna (Depot/Map).
    ///
    /// Transitions wywoływane przez simulatory:
    /// - DepotMovementSimulator: Set*Depot* podczas exit/entry
    /// - TrainRunSimulator: SetOnRoute przy spawn, UpdateRoutePosition per tick, SetAtStation przy despawn
    /// - Fleet (zakup pojazdu): SetInDepot dla świeżo dostarczonych
    ///
    /// Events:
    /// - OnLocationChanged — dla UI notifications i logiki handshake'u (DispatchService etc.)
    /// </summary>
    public class VehicleLocationService : MonoBehaviour
    {
        public static VehicleLocationService Instance { get; private set; }

        readonly Dictionary<int, VehicleLocationRecord> _records = new();

        /// <summary>
        /// Per-type index utrzymywany inkrementalnie w <see cref="Emit"/> i <see cref="GetOrCreate"/>.
        /// Eliminuje alokację per query w <see cref="GetByType"/>/<see cref="GetInDepot"/>/<see cref="GetOnRoute"/>
        /// (poprzednio każde wywołanie tworzyło nową <c>List&lt;&gt;</c> + iterowało wszystkie records
        /// liniowo — kosztowne w hot path IdleVehicleVisualizer/CleanerWorkflow przy 1000+ pojazdów).
        /// Tranzycje stanu są rzadkie (dispatch/despawn), więc linear remove z listy O(n) akceptowalny.
        /// </summary>
        readonly Dictionary<VehicleLocationType, List<VehicleLocationRecord>> _byType = new()
        {
            { VehicleLocationType.InDepot,        new List<VehicleLocationRecord>() },
            { VehicleLocationType.ExitingDepot,   new List<VehicleLocationRecord>() },
            { VehicleLocationType.AtStation,      new List<VehicleLocationRecord>() },
            { VehicleLocationType.OnRoute,        new List<VehicleLocationRecord>() },
            { VehicleLocationType.EnteringDepot,  new List<VehicleLocationRecord>() },
            { VehicleLocationType.InTransit,      new List<VehicleLocationRecord>() },
        };

        /// <summary>
        /// Event emitowany przy każdej tranzycji stanu pojazdu.
        /// Args: (vehicleId, oldType, newType). Nie emitujemy przy UpdateRoutePosition (tylko przy zmianie type).
        /// </summary>
        public event Action<int, VehicleLocationType, VehicleLocationType> OnLocationChanged;

        /// <summary>
        /// Bootstrap — tworzy singleton GO na DontDestroyOnLoad. Wywoływać przy starcie gry
        /// (GameCreator completion lub pierwszy zawołanie Instance).
        /// </summary>
        public static VehicleLocationService EnsureExists()
        {
            if (Instance != null) return Instance;

            var go = new GameObject("VehicleLocationService");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<VehicleLocationService>();
            Log.Info("[VehicleLocationService] Bootstrapped");
            return Instance;
        }

        /// <summary>
        /// BUG-039: clear cross-session state przy "Nowa gra" — singleton DontDestroyOnLoad
        /// utrzymuje records ze starej sesji (pojazd #5 ze starej kampanii pokazany jako
        /// "AtStation" w nowej grze). Wywoływać w bootstrap nowej gry / save load.
        /// </summary>
        public static void ResetAll()
        {
            if (Instance == null) return;
            int count = Instance._records.Count;
            Instance._records.Clear();
            foreach (var list in Instance._byType.Values)
                list.Clear();
            if (count > 0)
                Log.Info($"[VehicleLocationService] Reset all state ({count} records cleared)");
        }

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
        }

        // ── Queries ─────────────────────────────────────────────────

        /// <summary>Zwraca record pojazdu. Null gdy nigdy nie rejestrowany.</summary>
        public VehicleLocationRecord Get(int vehicleId) =>
            _records.TryGetValue(vehicleId, out var r) ? r : null;

        /// <summary>
        /// Zwraca wszystkie pojazdy o danym state type. **Bez alokacji** — zwraca
        /// referencję do internal listy (per-type index, maintained inkrementalnie).
        /// </summary>
        /// <remarks>
        /// Caller NIE może modyfikować zwróconej kolekcji (jest read-only view), ale też
        /// **nie powinien wywoływać Set*/Reset*** w trakcie iteracji nad tą listą —
        /// taka mutacja invalidate'uje enumerator. Wzorzec użycia: <c>foreach + use record + done</c>.
        /// </remarks>
        public IReadOnlyList<VehicleLocationRecord> GetByType(VehicleLocationType type)
        {
            return _byType.TryGetValue(type, out var list) ? list : System.Array.Empty<VehicleLocationRecord>();
        }

        /// <summary>Zwraca pojazdy aktualnie w zajezdni (InDepot). Bez alokacji.</summary>
        public IReadOnlyList<VehicleLocationRecord> GetInDepot() => GetByType(VehicleLocationType.InDepot);

        /// <summary>
        /// Zwraca pojazdy stojące na danej stacji (AtStation). Linear scan + alokacja —
        /// nie hot path (zero callerów w EA, tylko diagnostyka/przyszłe UI), nie warto
        /// utrzymywać per-station index.
        /// </summary>
        public IReadOnlyList<VehicleLocationRecord> GetAtStation(int stationId)
        {
            var atStation = _byType[VehicleLocationType.AtStation];
            var list = new List<VehicleLocationRecord>(atStation.Count);
            foreach (var r in atStation)
                if (r.stationId == stationId) list.Add(r);
            return list;
        }

        /// <summary>Zwraca pojazdy aktualnie w trasie (OnRoute). Bez alokacji.</summary>
        public IReadOnlyList<VehicleLocationRecord> GetOnRoute() => GetByType(VehicleLocationType.OnRoute);

        /// <summary>Wszystkie records (readonly enumeracja).</summary>
        public IReadOnlyCollection<VehicleLocationRecord> AllRecords => _records.Values;

        public List<VehicleLocationRecord> GetSnapshot()
        {
            var result = new List<VehicleLocationRecord>(_records.Count);
            foreach (var r in _records.Values)
            {
                if (r == null) continue;
                result.Add(new VehicleLocationRecord
                {
                    vehicleId = r.vehicleId,
                    type = r.type,
                    stationId = r.stationId,
                    depotTrackId = r.depotTrackId,
                    currentTrainRunId = r.currentTrainRunId,
                    currentConsistId = r.currentConsistId,
                    worldMapPosition = r.worldMapPosition
                });
            }
            return result;
        }

        public void RestoreSnapshot(IList<VehicleLocationRecord> records)
        {
            _records.Clear();
            foreach (var list in _byType.Values)
                list.Clear();
            if (records != null)
            {
                foreach (var r in records)
                {
                    if (r == null || r.vehicleId <= 0) continue;
                    var rec = new VehicleLocationRecord
                    {
                        vehicleId = r.vehicleId,
                        type = r.type,
                        stationId = r.stationId,
                        depotTrackId = r.depotTrackId,
                        currentTrainRunId = r.currentTrainRunId,
                        currentConsistId = r.currentConsistId,
                        worldMapPosition = r.worldMapPosition
                    };
                    _records[rec.vehicleId] = rec;
                    _byType[rec.type].Add(rec);
                }
            }
            Log.Info($"[VehicleLocationService] Restored {_records.Count} vehicle location record(s)");
        }

        // ── Transitions ─────────────────────────────────────────────

        /// <summary>
        /// Pojazd jest w zajezdni (parking, manewr, stoi). Wywoływane:
        /// - Przy zakupie (świeżo dostarczony): depotTrackId=-1 (nieznany parking)
        /// - Przy OnConsistEnteredDepot: po zakończeniu entry flow
        /// - Ręcznie (debug)
        /// </summary>
        public void SetInDepot(int vehicleId, int depotTrackId = -1)
        {
            var r = GetOrCreate(vehicleId);
            var oldType = r.type;
            r.type = VehicleLocationType.InDepot;
            r.stationId = GameState.HomeDepotStationId;
            r.depotTrackId = depotTrackId;
            r.currentTrainRunId = -1;
            r.currentConsistId = -1;
            Emit(vehicleId, oldType, r.type);
        }

        /// <summary>
        /// Pojazd manewruje w zajezdni jako część consist'u jadącego do bramy.
        /// Wywoływane przez handshake gdy EnqueueExit rusza (consist jeszcze nie przekroczył bramy).
        /// </summary>
        public void SetExitingDepot(int vehicleId, int consistId, int trainRunId)
        {
            var r = GetOrCreate(vehicleId);
            var oldType = r.type;
            r.type = VehicleLocationType.ExitingDepot;
            r.currentConsistId = consistId;
            r.currentTrainRunId = trainRunId;
            Emit(vehicleId, oldType, r.type);
        }

        /// <summary>
        /// Pojazd w trasie na mapie. Wywoływane przez TrainRunSimulator przy spawn.
        /// </summary>
        public void SetOnRoute(int vehicleId, int trainRunId, Vector2 worldPos)
        {
            var r = GetOrCreate(vehicleId);
            var oldType = r.type;
            r.type = VehicleLocationType.OnRoute;
            r.currentTrainRunId = trainRunId;
            r.currentConsistId = -1;
            r.depotTrackId = -1;
            r.worldMapPosition = worldPos;
            Emit(vehicleId, oldType, r.type);
        }

        /// <summary>
        /// Per-tick update pozycji pojazdu w trasie. NIE emituje OnLocationChanged (type się nie zmienia).
        /// </summary>
        public void UpdateRoutePosition(int vehicleId, Vector2 worldPos)
        {
            if (_records.TryGetValue(vehicleId, out var r))
                r.worldMapPosition = worldPos;
        }

        /// <summary>
        /// Pojazd stoi na peronie stacji — idle lub po zakończeniu kursu.
        /// Wywoływane przez TrainRunSimulator przy despawn (gdy nie home) lub Fleet przy zakupie
        /// (jeśli polityka dostawy przewiduje peron zamiast depot).
        /// </summary>
        public void SetAtStation(int vehicleId, int stationId, Vector2 worldPos)
        {
            var r = GetOrCreate(vehicleId);
            var oldType = r.type;
            r.type = VehicleLocationType.AtStation;
            r.stationId = stationId;
            r.currentTrainRunId = -1;
            r.currentConsistId = -1;
            r.depotTrackId = -1;
            r.worldMapPosition = worldPos;
            Emit(vehicleId, oldType, r.type);
        }

        /// <summary>
        /// Pojazd wjeżdża przez bramę depot. Wywoływane przez handshake gdy
        /// DepotMovementSimulator.SpawnConsistAtEntry jest zainicjowane.
        /// </summary>
        public void SetEnteringDepot(int vehicleId, int consistId)
        {
            var r = GetOrCreate(vehicleId);
            var oldType = r.type;
            r.type = VehicleLocationType.EnteringDepot;
            r.currentConsistId = consistId;
            r.currentTrainRunId = -1;
            Emit(vehicleId, oldType, r.type);
        }

        /// <summary>
        /// Edge case: pojazd gdzieś między stacjami bez aktywnego TrainRun.
        /// Użycie docelowo: awaria w trasie, relokacja wagonu bez kursu, reset po bugu.
        /// </summary>
        public void SetInTransit(int vehicleId, Vector2 worldPos)
        {
            var r = GetOrCreate(vehicleId);
            var oldType = r.type;
            r.type = VehicleLocationType.InTransit;
            r.currentTrainRunId = -1;
            r.currentConsistId = -1;
            r.worldMapPosition = worldPos;
            Emit(vehicleId, oldType, r.type);
        }

        // ── Helpers ─────────────────────────────────────────────────

        VehicleLocationRecord GetOrCreate(int vehicleId)
        {
            if (_records.TryGetValue(vehicleId, out var r)) return r;
            r = new VehicleLocationRecord { vehicleId = vehicleId };
            _records[vehicleId] = r;
            // Per-type index: rejestracja pierwszej pozycji (default `r.type = InDepot`).
            // Późniejsze Set* przesuwają record między listami via Emit.
            _byType[r.type].Add(r);
            return r;
        }

        void Emit(int vehicleId, VehicleLocationType oldType, VehicleLocationType newType)
        {
            if (oldType == newType) return;
            // Maintain per-type index: move record between bucket lists.
            // Lookup record ponownie (caller już zaktualizował r.type w Set*).
            if (_records.TryGetValue(vehicleId, out var r))
            {
                _byType[oldType].Remove(r); // O(n) per bucket, ale tranzycje rzadkie
                _byType[newType].Add(r);
            }
            OnLocationChanged?.Invoke(vehicleId, oldType, newType);
        }

        // ── Debug ───────────────────────────────────────────────────

        [ContextMenu("Debug: Dump all vehicle locations")]
        public void DebugDumpAll()
        {
            if (_records.Count == 0)
            {
                Log.Info("[VehicleLocationService] (empty — no vehicles tracked)");
                return;
            }
            Log.Info($"[VehicleLocationService] {_records.Count} vehicles tracked:");
            foreach (var r in _records.Values)
            {
                string extra = r.type switch
                {
                    VehicleLocationType.InDepot => $" track#{r.depotTrackId} home={r.stationId}",
                    VehicleLocationType.AtStation => $" station#{r.stationId} pos=({r.worldMapPosition.x:F0},{r.worldMapPosition.y:F0})",
                    VehicleLocationType.OnRoute => $" run#{r.currentTrainRunId} pos=({r.worldMapPosition.x:F0},{r.worldMapPosition.y:F0})",
                    VehicleLocationType.ExitingDepot => $" consist#{r.currentConsistId} run#{r.currentTrainRunId}",
                    VehicleLocationType.EnteringDepot => $" consist#{r.currentConsistId}",
                    VehicleLocationType.InTransit => $" pos=({r.worldMapPosition.x:F0},{r.worldMapPosition.y:F0})",
                    _ => ""
                };
                Log.Info($"  vehicle#{r.vehicleId}: {r.type}{extra}");
            }
        }

        [ContextMenu("Debug: Seed test vehicle (id=101) InDepot")]
        public void DebugSeedTestVehicleInDepot()
        {
            SetInDepot(101, depotTrackId: -1);
            Log.Info("[VehicleLocationService] Seeded vehicle#101 InDepot");
        }

        [ContextMenu("Debug: Seed test vehicle (id=101) AtStation(home)")]
        public void DebugSeedTestVehicleAtHome()
        {
            if (GameState.HomeDepotStationId < 0)
            {
                Log.Warn("[VehicleLocationService] HomeDepotStationId not set — cannot seed AtStation");
                return;
            }
            SetAtStation(101, GameState.HomeDepotStationId, Vector2.zero);
            Log.Info($"[VehicleLocationService] Seeded vehicle#101 AtStation(home={GameState.HomeDepotStationId})");
        }
    }
}
