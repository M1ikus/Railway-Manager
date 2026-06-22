using System;
using System.Collections.Generic;
using UnityEngine;

namespace DepotSystem
{
    /// <summary>Stan zadania ruchu w zajezdni (M9b).</summary>
    public enum DepotMoveState
    {
        /// <summary>W kolejce, czeka na wykonanie.</summary>
        Queued,
        /// <summary>Transient: szukanie ścieżki (1 tick).</summary>
        Pathfinding,
        /// <summary>W ruchu (Etap 2 implementuje interpolację).</summary>
        Moving,
        /// <summary>Dotarł do toru docelowego.</summary>
        Completed,
        /// <summary>Błąd: brak ścieżki, tor zajęty, inne.</summary>
        Failed
    }

    /// <summary>
    /// Zadanie ruchu składu (consist) między torami w zajezdni.
    /// Produkowane przez API (gracz, Circulation start, gameplay), konsumowane przez DepotMovementSimulator.
    /// </summary>
    public class DepotMoveTask
    {
        // ── Input ────────────────────────────────────────────

        public int consistId;
        public List<int> vehicleIds;   // atomiczny skład — ruchy razem
        public int fromTrackId;
        public int toTrackId;

        /// <summary>
        /// M8-11 / D34: Priorytet wyznaczany przez <see cref="IDepotTaskPriorityProvider"/>.
        /// 0-100 (0=default FCFS gdy brak providera, 100=workshop overdue).
        /// Wykorzystane przy sortowaniu Queued tasks (jesli provider obecny).
        /// </summary>
        public int priority;

        // ── State machine ────────────────────────────────────

        public DepotMoveState state = DepotMoveState.Queued;
        public string failureReason;

        // ── Computed at Pathfinding ──────────────────────────

        /// <summary>Ścieżka w grafie (lista nodeId). Null gdy pathfinding jeszcze nie wykonany.</summary>
        public List<int> pathNodeIds;

        /// <summary>Konkatenacja edge.Polyline wzdłuż ścieżki (3D punkty).</summary>
        public List<Vector3> polyline;

        /// <summary>Kumulatywna odległość na polyline [m]. cumDistM[i] = dystans od polyline[0] do polyline[i].</summary>
        public float[] cumDistM;

        public float totalLengthM;

        /// <summary>
        /// Pozycja docelowa w świecie (gdzie kliknął gracz).
        /// Null = brak override, consist jedzie do końca polyline (totalLengthM).
        /// Set = consist zatrzymuje się gdy currentDistanceM >= stopDistanceM (<= totalLengthM).
        /// </summary>
        public Vector3? targetWorldPos;

        /// <summary>
        /// Override end-of-movement distance [m]. Default = totalLengthM.
        /// Gdy targetWorldPos set, obliczane jako projekcja targetWorldPos na polyline.
        /// </summary>
        public float stopDistanceM;

        /// <summary>
        /// Lista trackId na ścieżce od fromTrack do toTrack (włącznie z intermediate).
        /// Wszystkie są zarezerwowane whole-path przed startem ruchu.
        /// </summary>
        public List<int> reservedTrackIds;

        /// <summary>Liczba tick'ów w state=Queued (do detekcji stuck tasks).</summary>
        public int queuedTicks;

        /// <summary>Flag — czy FinalizeTask już wywołane (idempotencja).</summary>
        public bool finalized;

        /// <summary>Czy po Completed destroyować visual + emitować OnConsistExitedDepot (wyjazd z depot).</summary>
        public bool exitAfterComplete;

        /// <summary>Czy po Completed emitować OnConsistEnteredDepot (wjazd do depot — consist gotowy do sterowania).</summary>
        public bool entryOnComplete;

        /// <summary>
        /// MM-18: callback wywoływany po FinalizeTask (Completed lub Failed). Subskrybuje go
        /// service producer (np. OutdoorEquipmentJobService) żeby przejść z fazy EnRoute do
        /// Servicing — start timer count-down.
        ///
        /// Args: <c>state</c> = Completed (dotarł) lub Failed (np. brak ścieżki).
        /// Wywoływany dokładnie raz, po zwolnieniu rezerwacji torów.
        /// </summary>
        public Action<DepotMoveState> onCompleted;

        // ── Runtime (Etap 2+) ────────────────────────────────

        /// <summary>Postęp pociągu wzdłuż polyline [0, totalLengthM].</summary>
        public float currentDistanceM;

        /// <summary>Aktualna prędkość [m/s].</summary>
        public float currentSpeedMps;

        // ── TD-031: mapowanie polyline↔tor + dynamiczny cap dojazdu ──

        /// <summary>
        /// TD-031: segmenty toru wzdłuż polyline taska — po jednym na każdy distinct tor na ścieżce,
        /// w kolejności jazdy. Pozwala mapować <see cref="currentDistanceM"/> (na polyline) na
        /// track-local dist, żeby zapisywać footprint w occupancy grafu i czytać "co przede mną".
        /// Budowane w <c>BuildPolylineFromPath</c>.
        /// </summary>
        public List<TaskTrackSegment> trackSegments;

        /// <summary>
        /// TD-031: dynamiczny limit dojazdu [m na polyline] = pozycja nosa za najbliższą jednostką
        /// z przodu (z buforem styku). <see cref="float.PositiveInfinity"/> gdy nikogo nie ma z przodu.
        /// Liczony co tick; AdvanceMovement bierze min(stopDistanceM, dynamicStopCapM).
        /// </summary>
        public float dynamicStopCapM = float.PositiveInfinity;

        /// <summary>TD-031: id jednostki wyznaczającej dynamicStopCapM (logi/diagnostyka). -1 = brak.</summary>
        public int dynamicStopBlockerId = -1;

        /// <summary>TD-032: gdy ruch zakończył się dojazdem DO STYKU za innym składem — jego consistId
        /// (do promptu sprzęgu). -1 = zakończono nie przy styku (wolny cel gracza).</summary>
        public int arrivedContactBlockerId = -1;
        /// <summary>TD-032: świat-pozycja punktu styku (nos składu) dla promptu sprzęgu.</summary>
        public Vector3 arrivedContactWorldPos;
    }

    /// <summary>
    /// TD-031: jeden tor na ścieżce taska + jego rozciągłość w przestrzeni polyline taska.
    /// Mapuje dystans-na-polyline [polyStartM, polyEndM] na track-local [0, trackLenM].
    /// reversedVsTrack = jazda przeciwna do osi toru (Start→End wg GetTrackPolyline): wtedy
    /// track-local = polyEndM − taskDist (zamiast taskDist − polyStartM).
    /// </summary>
    public class TaskTrackSegment
    {
        public int trackId;
        public float polyStartM;
        public float polyEndM;
        public bool reversedVsTrack;
        public float trackLenM;
    }
}
