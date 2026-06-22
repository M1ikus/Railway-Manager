using System.Collections.Generic;
using UnityEngine;
using RailwayManager.Core;
using RailwayManager.Timetable.Notifications;

namespace RailwayManager.Timetable
{
    /// <summary>
    /// Zarządzanie rezerwacjami segmentów toru i peronów stacyjnych.
    /// Sprawdzanie kolizji, auto-przypisanie peronów, rezerwacja przy zatwierdzeniu.
    /// </summary>
    public static class ReservationManager
    {
        public struct CollisionInfo
        {
            public int stopIndex;            // -1 jeśli kolizja segmentowa (nie na stacji)
            public string stationName;       // nazwa stacji/segmentu
            public int collidingTimetableId;
            public string description;       // np. "KOLIZJA z #10001 o 06:12"
        }

        // ─────────────────────────────────────────────
        //  Platform data: OSM (railway=platform) + JSON override (StationTrackData).
        //  Brak fallbacku — stacje bez platform data dostają empty list z FindPlatformsForStation
        //  → CheckCollisions/AutoAssignPlatforms skipuje peron, block-section reservation gra normalnie.
        //  Synthetic platform generation wywalone w F1.0.5 (2026-05-10) bo wprowadzało fake trackRef'y
        //  niespójne z realną topologią OSM (np. Aleksandrów Kujawski real 1/2/5/15 vs fake 1/2/3/4).
        //  Single source of truth: init.Platforms (OSM) + Assets/StreamingAssets/TimetableData/station_tracks.json.
        // ─────────────────────────────────────────────

        // ─────────────────────────────────────────────
        //  Sprawdzanie kolizji (BEZ rezerwacji)
        // ─────────────────────────────────────────────

        /// <summary>
        /// Sprawdza kolizje dla rozkładu BEZ rezerwowania — zwraca listę konfliktów.
        /// Używane w kreatorze po "Oblicz postoje" żeby pokazać czerwone highlighty.
        /// </summary>
        public static List<CollisionInfo> CheckCollisions(
            List<TimetableStop> stops,
            Route route,
            PathfindingGraph graph,
            int startMinutesFromMidnight,
            TimetableInitializer init)
        {
            var collisions = new List<CollisionInfo>();
            if (stops == null || route == null || graph == null || init == null) return collisions;

            long baseSec = startMinutesFromMidnight * 60L;

            // Sprawdź perony — tylko stacje z rozjazdami (isMajorStation), nie przystanki
            for (int i = 0; i < stops.Count; i++)
            {
                var stop = stops[i];
                if (stop.stopType == StopType.Transit) continue;

                // Przystanki (halts) nie biorą udziału w rezerwacji peronów
                // — zajętość toru na szlaku pokrywa block section system
                bool isMajorStation = false;
                if (init.Stations != null)
                {
                    foreach (var st in init.Stations)
                    {
                        if (st.name == stop.stationName) { isMajorStation = st.isMajorStation; break; }
                    }
                }
                if (!isMajorStation) continue;

                long absArrival = baseSec + stop.plannedArrivalSec;
                long absDeparture = baseSec + stop.plannedDepartureSec;
                if (absDeparture <= absArrival) absDeparture = absArrival + 30;

                var platforms = FindPlatformsForStation(stop.stationNodeId, init);
                if (platforms.Count == 0) continue;

                bool anyFree = false;
                foreach (var p in platforms)
                {
                    if (TimetableService.PlatformReservations.IsFree(p.platformId, absArrival, absDeparture))
                    {
                        anyFree = true;
                        break;
                    }
                }

                if (!anyFree)
                {
                    var existingRes = TimetableService.PlatformReservations.GetReservations(platforms[0].platformId);
                    int collidingTt = existingRes.Count > 0 ? existingRes[0].timetableId : -1;

                    collisions.Add(new CollisionInfo
                    {
                        stopIndex = i,
                        stationName = stop.stationName,
                        collidingTimetableId = collidingTt,
                        description = $"KOLIZJA — perony zajęte o {FormatTime(absArrival)}"
                    });
                }
            }

            // Odcinki blokowe — auto-rozwiąż konflikty (dodaj oczekiwanie na stacjach)
            ResolveBlockConflicts(collisions, route, graph, baseSec, stops, init);

            return collisions;
        }

        /// <summary>
        /// Sprawdza kolizje dla wszystkich wystąpień rozkładu zgodnie z FrequencySpec.
        /// Dla Single = jedno wywołanie CheckCollisions. Dla Takt = iteracja po wszystkich
        /// startach. Zwraca pierwszą kolizję dla każdego slotu (lub pustą jeśli OK).
        /// </summary>
        public static List<CollisionInfo> CheckCollisionsForFrequency(
            List<TimetableStop> stops,
            Route route,
            PathfindingGraph graph,
            FrequencySpec frequency,
            TimetableInitializer init)
        {
            var all = new List<CollisionInfo>();
            if (stops == null || route == null || init == null) return all;

            if (frequency.type == FrequencyType.Single)
            {
                all.AddRange(CheckCollisions(stops, route, graph,
                    frequency.firstRunMinutesFromMidnight, init));
                return all;
            }

            // Takt: każdy run sprawdzamy osobno
            int interval = frequency.intervalMinutes;
            if (interval <= 0) return all;
            for (int t = frequency.firstRunMinutesFromMidnight;
                 t <= frequency.lastRunMinutesFromMidnight;
                 t += interval)
            {
                int normalized = t % (24 * 60);
                var c = CheckCollisions(stops, route, graph, normalized, init);
                if (c != null && c.Count > 0)
                {
                    // Dodaj prefix godziny do description każdej kolizji
                    foreach (var ci in c)
                    {
                        var copy = ci;
                        copy.description = $"[{normalized / 60:D2}:{normalized % 60:D2}] {ci.description}";
                        all.Add(copy);
                    }
                }
            }
            return all;
        }

        // ─────────────────────────────────────────────
        //  Auto-przypisanie peronów
        // ─────────────────────────────────────────────

        /// <summary>
        /// Przypisuje wolne perony do postojów z platformId=-1.
        /// M-TimetableUX F1.8 (Strategy A — alternate platform):
        /// 1. Preferred: jeśli stop.trackRef ustawione (UI hint lub F1.4 user choice) AND matching peron jest free → użyj go
        /// 2. Fallback: first-free peron z dostępnych
        /// 3. Strategy A failure: brak free → platformId zostaje -1 (caller widzi z log warning)
        ///
        /// User-set <c>stop.platformId &gt;= 0</c> jest **respected** (no overwrite).
        /// </summary>
        public static void AutoAssignPlatforms(
            Timetable tt,
            TimetableInitializer init)
        {
            if (tt?.stops == null || init == null) return;
            long baseSec = tt.frequency.firstRunMinutesFromMidnight * 60L;

            int alternateAssigned = 0;
            int unassignable = 0;

            foreach (var stop in tt.stops)
            {
                if (stop.stopType == StopType.Transit) continue;
                if (stop.platformId >= 0) continue; // user-set lub already assigned

                long absArr = baseSec + stop.plannedArrivalSec;
                long absDep = baseSec + stop.plannedDepartureSec;
                if (absDep <= absArr) absDep = absArr + 30;

                var platforms = FindPlatformsForStation(stop.stationNodeId, init);
                if (platforms.Count == 0) continue;

                // Strategy A step 1: prefer matching trackRef (user hint OR F1.4 explicit choice)
                bool assignedToPreferred = false;
                if (!string.IsNullOrEmpty(stop.trackRef))
                {
                    foreach (var p in platforms)
                    {
                        if (p.trackRef != stop.trackRef) continue;
                        if (!TimetableService.PlatformReservations.IsFree(p.platformId, absArr, absDep)) continue;
                        stop.platformId = p.platformId;
                        assignedToPreferred = true;
                        break;
                    }
                }
                if (assignedToPreferred) continue;

                // Strategy A step 2: first-free alternate
                bool assignedToAlternate = false;
                foreach (var p in platforms)
                {
                    if (!TimetableService.PlatformReservations.IsFree(p.platformId, absArr, absDep)) continue;
                    stop.platformId = p.platformId;
                    assignedToAlternate = true;
                    if (!string.IsNullOrEmpty(stop.trackRef) && p.trackRef != stop.trackRef)
                    {
                        alternateAssigned++;
                        // F1.18: notification dla user'a — alternate platform użyty
                        // F1.16 Expert polish: diagnosticDetails z platformId + time window
                        TimetableNotificationService.Add(
                            NotificationSeverity.Info,
                            NotificationType.PlatformConflictResolved,
                            $"Tor {stop.trackRef} zajęty, użyto toru {p.trackRef} w {stop.stationName} {FormatTime(absArr)}",
                            stopIndex: tt.stops.IndexOf(stop),
                            timeOfDaySec: (int)(absArr % 86400),
                            sourceTimetableId: tt.id,
                            diagnosticDetails: $"platformId={p.platformId}, window={absArr}-{absDep}, totalPlatforms={platforms.Count}");
                    }
                    break;
                }

                // Strategy A step 3: failure — no free platform
                if (!assignedToAlternate)
                {
                    unassignable++;
                    // F1.18: notification cannot fit (Strategy A failed — Strategy B może uratować w ResolveBlockConflicts, ale platform-specific failure jest sygnał dla user'a)
                    // F1.16 Expert polish: diagnosticDetails z all platform IDs + window
                    var platformIdsList = new System.Text.StringBuilder();
                    for (int pi = 0; pi < platforms.Count; pi++)
                    {
                        if (pi > 0) platformIdsList.Append(",");
                        platformIdsList.Append(platforms[pi].platformId);
                    }
                    TimetableNotificationService.Add(
                        NotificationSeverity.Warning,
                        NotificationType.CannotFit,
                        $"Wszystkie tory zajęte w {stop.stationName} {FormatTime(absArr)}-{FormatTime(absDep)} — przesuń start lub użyj alternative trasy",
                        stopIndex: tt.stops.IndexOf(stop),
                        timeOfDaySec: (int)(absArr % 86400),
                        sourceTimetableId: tt.id,
                        diagnosticDetails: $"platforms=[{platformIdsList}], window={absArr}-{absDep}, attemptedTrackRef={stop.trackRef}");
                }
            }

            if (alternateAssigned > 0 || unassignable > 0)
                Log.Info($"[F1.8] AutoAssignPlatforms: {alternateAssigned} alternate platform(s) used " +
                         $"(user trackRef occupied), {unassignable} unassigned (Strategy A failed — all platforms occupied)");
        }

        // ─────────────────────────────────────────────
        //  Rezerwacja po zatwierdzeniu
        // ─────────────────────────────────────────────

        /// <summary>
        /// Rezerwuje segmenty + perony dla zatwierdzonego rozkładu.
        /// Dla Single = jedno wystąpienie. Dla Takt = N wystąpień, ten sam set
        /// peronów/bloków rezerwowany dla każdego startu osobno (różne baseSec).
        /// </summary>
        public static void ReserveForTimetable(
            Timetable tt,
            Route route,
            PathfindingGraph graph)
        {
            if (tt?.stops == null || route?.nodeIds == null) return;

            // Lista startów do zarezerwowania
            var startsMin = new List<int>();
            if (tt.frequency.type == FrequencyType.Takt && tt.frequency.intervalMinutes > 0)
            {
                for (int t = tt.frequency.firstRunMinutesFromMidnight;
                     t <= tt.frequency.lastRunMinutesFromMidnight;
                     t += tt.frequency.intervalMinutes)
                {
                    startsMin.Add(t % (24 * 60));
                }
            }
            else
            {
                startsMin.Add(tt.frequency.firstRunMinutesFromMidnight);
            }

            int platformsReserved = 0;
            int segmentsReserved = 0;
            var init = TimetableInitializer.Instance;
            var blocks = (init != null && tt.stops.Count >= 2)
                ? BuildRouteBlocks(tt.stops, route, init)
                : null;

            foreach (int startMin in startsMin)
            {
                long baseSec = startMin * 60L;

                // Rezerwuj perony
                foreach (var stop in tt.stops)
                {
                    if (stop.platformId < 0 || stop.stopType == StopType.Transit) continue;
                    long absArr = baseSec + stop.plannedArrivalSec;
                    long absDep = baseSec + stop.plannedDepartureSec;
                    if (absDep <= absArr) absDep = absArr + 30;

                    TimetableService.ReservePlatform(stop.platformId, absArr, absDep, 0, tt.id);
                    platformsReserved++;
                }

                // Rezerwuj odcinki blokowe
                if (blocks != null)
                {
                    foreach (var blk in blocks)
                    {
                        long secStart = baseSec + blk.departureSec;
                        long secEnd = baseSec + blk.arrivalSec;
                        if (secEnd <= secStart) secEnd = secStart + 10;

                        TimetableService.ReserveBlockSection(blk.blockKey, secStart, secEnd, 0, tt.id);
                        segmentsReserved++;
                    }
                }
            }

            Log.Info($"[ReservationManager] Reserved {platformsReserved} platforms, "
                     + $"{segmentsReserved} block sections for timetable #{tt.id} "
                     + $"({startsMin.Count} run(s))");
        }

        // ─────────────────────────────────────────────
        //  Helpers
        // ─────────────────────────────────────────────

        private static List<StationPlatform> FindPlatformsForStation(
            int stationNodeId, TimetableInitializer init)
        {
            var result = new List<StationPlatform>();
            if (init?.Platforms == null || init.Graph == null) return result;

            var stationPos = init.Graph.GetNode(stationNodeId).position;
            const float radiusSq = 800f * 800f;

            foreach (var plat in init.Platforms)
            {
                if (plat.stationNodeId < 0 || plat.stationNodeId >= init.Graph.NodeCount) continue;
                var platPos = init.Graph.GetNode(plat.stationNodeId).position;
                if ((platPos - stationPos).sqrMagnitude < radiusSq)
                    result.Add(plat);
            }

            if (result.Count > 8)
                result.RemoveRange(8, result.Count - 8);

            return result;
        }

        /// <summary>
        /// Sprawdza odcinki blokowe i automatycznie dodaje czas oczekiwania na stacjach
        /// gdy slot jest zajęty. Modyfikuje stops (wydłuża postój).
        /// Zwraca info o dodanych czekaniach w collisions.
        /// </summary>
        /// <summary>
        /// Sprawdza bloki station-to-station i dodaje oczekiwanie na stacjach gdy slot zajęty.
        /// </summary>
        private static void ResolveBlockConflicts(
            List<CollisionInfo> collisions,
            Route route, PathfindingGraph graph,
            long baseSec, List<TimetableStop> stops,
            TimetableInitializer init)
        {
            if (stops == null || stops.Count < 2 || init == null) return;

            var blocks = BuildRouteBlocks(stops, route, init);
            int totalDelay = 0;

            // Diagnostic logging (2026-05-11): user reportował że single-track bidirectional
            // collision nie tworzy postoju. Verbose log każdego bloku + result IsFree.
            Log.Info($"[ResolveBlockConflicts] Checking {blocks.Count} blocks, baseSec={baseSec}");

            for (int bi = 0; bi < blocks.Count; bi++)
            {
                var blk = blocks[bi];

                long secStart = baseSec + blk.departureSec + totalDelay;
                long secEnd = baseSec + blk.arrivalSec + totalDelay;
                if (secEnd <= secStart) secEnd = secStart + 10;

                bool isFree = TimetableService.BlockSectionReservations.IsFree(blk.blockKey, secStart, secEnd);
                int existingCount = TimetableService.BlockSectionReservations.GetReservations(blk.blockKey).Count;
                Log.Info($"[ResolveBlockConflicts]   Block {bi}: {blk.fromStation}→{blk.toStation} " +
                         $"blockKey={blk.blockKey} window=[{secStart},{secEnd}] isFree={isFree} existingReservations={existingCount}");

                if (isFree)
                    continue;

                var reservations = TimetableService.BlockSectionReservations.GetReservations(blk.blockKey);
                long latestEnd = 0;
                foreach (var r in reservations)
                    if (r.Overlaps(secStart, secEnd) && r.endGameTimeSec > latestEnd)
                        latestEnd = r.endGameTimeSec;

                if (latestEnd <= secStart) continue;

                int neededDelay = (int)(latestEnd - secStart) + 10;
                totalDelay += neededDelay;

                int waitStopIdx = blk.waitStopIdx;
                int colTt = reservations.Count > 0 ? reservations[0].timetableId : -1;

                stops[waitStopIdx].plannedDepartureSec += neededDelay;
                for (int j = waitStopIdx + 1; j < stops.Count; j++)
                {
                    stops[j].plannedArrivalSec += neededDelay;
                    stops[j].plannedDepartureSec += neededDelay;
                }

                // Przebuduj bloki z aktualnymi czasami
                blocks = BuildRouteBlocks(stops, route, init);

                collisions.Add(new CollisionInfo
                {
                    stopIndex = waitStopIdx,
                    stationName = blk.fromStation,
                    collidingTimetableId = colTt,
                    description = $"Oczekiwanie {neededDelay / 60}min {neededDelay % 60}s " +
                        $"({blk.fromStation}→{blk.toStation}, poc. #{colTt})"
                });

                // F1.18: notifications dla user'a — Strategy B (extend dwell) auto-resolution
                // F1.16 Expert polish: diagnosticDetails z blockKey + neededDelay + colliding TT
                int newDwellSec = stops[waitStopIdx].plannedDepartureSec - stops[waitStopIdx].plannedArrivalSec;
                int waitTimeOfDaySec = (int)((baseSec + stops[waitStopIdx].plannedArrivalSec) % 86400);
                TimetableNotificationService.Add(
                    NotificationSeverity.Info,
                    NotificationType.BlockConflictResolved,
                    $"Pociąg czeka {neededDelay / 60} min {neededDelay % 60} s na zwolnienie bloku " +
                    $"{blk.fromStation}→{blk.toStation} (rozkład #{colTt})",
                    stopIndex: waitStopIdx,
                    timeOfDaySec: waitTimeOfDaySec,
                    sourceTimetableId: -1,
                    diagnosticDetails: $"blockKey={blk.blockKey}, latestEnd={latestEnd}, neededDelay={neededDelay}s, collidingTT={colTt}");
                TimetableNotificationService.Add(
                    NotificationSeverity.Info,
                    NotificationType.DwellExtended,
                    $"Postój wydłużony do {newDwellSec / 60}:{newDwellSec % 60:D2} w {blk.fromStation} (regulacja ruchu)",
                    stopIndex: waitStopIdx,
                    timeOfDaySec: waitTimeOfDaySec,
                    sourceTimetableId: -1,
                    diagnosticDetails: $"originalDwell={newDwellSec - neededDelay}s, extendedTo={newDwellSec}s, totalDelaySoFar={totalDelay}s");
            }

            if (totalDelay > 0)
                Log.Info($"[ReservationManager] Auto-resolved block conflicts: +{totalDelay}s total delay");
        }

        /// <summary>
        /// Znajduje ostatni stop PRZED danym dystansem, który jest stacją z mijanką
        /// (isMajorStation = true). Na przystanku osobowym pociąg nie może czekać
        /// na zwolnienie slotu, bo nie ma toru do minięcia.
        /// Pierwsza stacja (idx 0) zawsze kwalifikuje się jako punkt oczekiwania.
        /// </summary>
        // ─────────────────────────────────────────────
        //  Route blocks (station-to-station)
        // ─────────────────────────────────────────────

        /// <summary>
        /// Blok trasy — odcinek między granicami sygnałowymi/stacyjnymi.
        /// Używany zarówno w planning-time (ReserveForTimetable) jak i runtime (TrainRunSimulator).
        /// </summary>
        internal struct RouteBlock
        {
            public string fromStation;
            public string toStation;
            public int departureSec;    // czas wejścia w blok (relative do startu rozkładu)
            public int arrivalSec;      // czas wyjścia z bloku (relative)
            public int blockKey;        // hash do rezerwacji (symetryczny nodeA↔nodeB)
            public int waitStopIdx;     // stop na którym pociąg czeka przy konflikcie
            public int startRouteIdx;   // pozycja startu bloku w route.nodeIds
            public int endRouteIdx;     // pozycja końca bloku w route.nodeIds
        }

        struct RouteSignal
        {
            public int routeIdx;            // pozycja w route.nodeIds
            public SignalFunction function;
        }

        /// <summary>
        /// Buduje listę bloków wzdłuż trasy z kaskadowym fallbackiem:
        /// 1. Oba końce mają semafory → exit → (block SBL) → entry
        /// 2. Tylko exit → exit → station
        /// 3. Tylko entry → station → entry
        /// 4. Brak semaforów → station → station
        /// </summary>
        internal static List<RouteBlock> BuildRouteBlocks(
            List<TimetableStop> stops, Route route, TimetableInitializer init)
        {
            var blocks = new List<RouteBlock>();
            if (stops == null || stops.Count < 2) return blocks;

            // Pre-build: nodeId → indeks w route.nodeIds (pozycja na trasie)
            var routeNodeToIdx = new Dictionary<int, int>();
            if (route?.nodeIds != null)
            {
                for (int i = 0; i < route.nodeIds.Count; i++)
                {
                    // Bierz pierwsze wystąpienie (pociąg może przejechać przez ten sam node
                    // w obie strony przy reverse, ale pierwsze to odnośnik)
                    if (!routeNodeToIdx.ContainsKey(route.nodeIds[i]))
                        routeNodeToIdx[route.nodeIds[i]] = i;
                }
            }

            // Zbierz sygnały które są na trasie, posortowane po pozycji
            var routeSignals = new List<RouteSignal>();
            if (init?.Signals != null)
            {
                foreach (var sig in init.Signals)
                {
                    if (routeNodeToIdx.TryGetValue(sig.nodeId, out int idx))
                        routeSignals.Add(new RouteSignal { routeIdx = idx, function = sig.function });
                }
                routeSignals.Sort((a, b) => a.routeIdx.CompareTo(b.routeIdx));
            }

            // Znajdź stops które są railway=station + ich routeIdx
            var stationStops = new List<(int stopIdx, int routeIdx)>();
            for (int i = 0; i < stops.Count; i++)
            {
                bool isMajor = false;
                if (init?.Stations != null)
                {
                    foreach (var st in init.Stations)
                    {
                        if (st.name == stops[i].stationName)
                        {
                            isMajor = st.isMajorStation;
                            break;
                        }
                    }
                }
                bool firstOrLast = (i == 0 || i == stops.Count - 1);
                if (!isMajor && !firstOrLast) continue;

                routeNodeToIdx.TryGetValue(stops[i].stationNodeId, out int rIdx);
                stationStops.Add((i, rIdx));
            }

            // Dla każdej pary sąsiednich stacji zbuduj bloki (z podziałem przez semafory)
            for (int si = 0; si < stationStops.Count - 1; si++)
            {
                var from = stationStops[si];
                var to = stationStops[si + 1];

                int depSec = stops[from.stopIdx].plannedDepartureSec;
                int arrSec = stops[to.stopIdx].plannedArrivalSec;
                string fromName = stops[from.stopIdx].stationName;
                string toName = stops[to.stopIdx].stationName;

                // Exit w pierwszych 20% szlaku, entry w ostatnich 20% —
                // ogranicza łapanie exit/entry sąsiednich stacji przy merged nodes.
                int segLen = to.routeIdx - from.routeIdx;
                int exitMaxIdx = from.routeIdx + segLen / 5;
                int entryMinIdx = to.routeIdx - segLen / 5;

                int exitSignalIdx = -1, entrySignalIdx = -1;
                var blockSignalsBetween = new List<int>();

                foreach (var rs in routeSignals)
                {
                    if (rs.routeIdx <= from.routeIdx) continue;
                    if (rs.routeIdx >= to.routeIdx) break;

                    switch (rs.function)
                    {
                        case SignalFunction.Exit:
                            if (rs.routeIdx <= exitMaxIdx && exitSignalIdx < 0)
                                exitSignalIdx = rs.routeIdx;
                            break;
                        case SignalFunction.Entry:
                            if (rs.routeIdx >= entryMinIdx)
                                entrySignalIdx = rs.routeIdx;
                            break;
                        case SignalFunction.Block:
                            blockSignalsBetween.Add(rs.routeIdx);
                            break;
                    }
                }

                // Wyznacz granice: startIdx, [blocks...], endIdx
                int startIdx = exitSignalIdx >= 0 ? exitSignalIdx : from.routeIdx;
                int endIdx = entrySignalIdx >= 0 ? entrySignalIdx : to.routeIdx;

                // Block signals tylko te między startIdx a endIdx.
                // Deduplikacja: sygnały w odległości <= 10 routeIdx traktujemy jako jeden
                // (pary forward/backward na tym samym słupku SBL są blisko siebie na trasie)
                blockSignalsBetween.Sort();
                const int proximityThreshold = 5;
                var validBlockIdx = new List<int>();
                int lastAccepted = -1000;
                foreach (var b in blockSignalsBetween)
                {
                    if (b <= startIdx || b >= endIdx) continue;
                    if (b - lastAccepted < proximityThreshold) continue; // duplikat tego samego słupka
                    validBlockIdx.Add(b);
                    lastAccepted = b;
                }


                // Buduj sub-bloki: startIdx → block1 → block2 → ... → endIdx
                var boundaries = new List<int> { startIdx };
                boundaries.AddRange(validBlockIdx);
                boundaries.Add(endIdx);

                // Interpolacja liniowa czasów po routeIdx (proporcjonalnie do station timing)
                int fromRouteIdx = from.routeIdx;
                int toRouteIdx = to.routeIdx;
                int routeSpan = toRouteIdx - fromRouteIdx;
                if (routeSpan <= 0) routeSpan = 1;
                int timeSpan = arrSec - depSec;

                int nodeFrom = stops[from.stopIdx].stationNodeId;
                int nodeTo = stops[to.stopIdx].stationNodeId;

                for (int bi = 0; bi < boundaries.Count - 1; bi++)
                {
                    int bStart = boundaries[bi];
                    int bEnd = boundaries[bi + 1];

                    float progressStart = (float)(bStart - fromRouteIdx) / routeSpan;
                    float progressEnd = (float)(bEnd - fromRouteIdx) / routeSpan;

                    int blockDepSec = depSec + (int)(progressStart * timeSpan);
                    int blockArrSec = depSec + (int)(progressEnd * timeSpan);
                    if (blockArrSec <= blockDepSec) blockArrSec = blockDepSec + 10;

                    // Klucz bloku bazujący na segmentId (physical track identity):
                    // single-track: oba kierunki mają ten sam segId → ten sam klucz (kolizja)
                    // dual-track: każdy tor ma inny segId → różne klucze (niezależne)
                    int key = ComputeBlockKey(bStart, bEnd, route, init.Graph);

                    blocks.Add(new RouteBlock
                    {
                        fromStation = bi == 0 ? fromName : "block",
                        toStation = bi == boundaries.Count - 2 ? toName : "block",
                        departureSec = blockDepSec,
                        arrivalSec = blockArrSec,
                        blockKey = key,
                        waitStopIdx = from.stopIdx,
                        startRouteIdx = bStart,
                        endRouteIdx = bEnd
                    });
                }
            }

            return blocks;
        }

        /// <summary>
        /// Oblicza klucz bloku bazujący na physical track identity (segmentId).
        /// Single-track: oba kierunki używają tego samego segId → kolizja.
        /// Dual-track: każdy tor ma inny segId → niezależne rezerwacje.
        /// Hash łączy kilka segmentów w bloku dla odporności na kolizje.
        /// </summary>
        internal static int ComputeBlockKey(int startIdx, int endIdx, Route route, PathfindingGraph graph)
        {
            int hash = 17;
            int count = 0;
            // Sample do 5 edge'ów z bloku (start, 25%, 50%, 75%, end-1)
            int span = endIdx - startIdx;
            if (span <= 0) span = 1;
            int[] samples = { 0, span / 4, span / 2, (3 * span) / 4, span - 1 };
            foreach (var s in samples)
            {
                int i = startIdx + s;
                if (i < 0 || i >= route.nodeIds.Count - 1) continue;
                int fromNode = route.nodeIds[i];
                int toNode = route.nodeIds[i + 1];

                // Znajdź edge między tymi node'ami
                var node = graph.GetNode(fromNode);
                if (node.edgeIds == null) continue;
                foreach (int eid in node.edgeIds)
                {
                    var edge = graph.GetEdge(eid);
                    if ((edge.fromNodeId == fromNode && edge.toNodeId == toNode)
                        || (edge.fromNodeId == toNode && edge.toNodeId == fromNode))
                    {
                        hash = hash * 31 + edge.segmentId;
                        count++;
                        break;
                    }
                }
            }
            // Fallback jeśli nie udało się znaleźć segmentów
            if (count == 0)
            {
                int nodeA = route.nodeIds[Mathf.Clamp(startIdx, 0, route.nodeIds.Count - 1)];
                int nodeB = route.nodeIds[Mathf.Clamp(endIdx, 0, route.nodeIds.Count - 1)];
                return Mathf.Min(nodeA, nodeB) * 100003 + Mathf.Max(nodeA, nodeB);
            }
            return hash;
        }

        private static bool stationBoundaryContains(int nodeId, TimetableInitializer init)
        {
            if (init?.Stations == null) return false;
            foreach (var st in init.Stations)
                if (st.pathNodeId == nodeId && st.isMajorStation) return true;
            return false;
        }

        private static string FindStationName(int nodeId, TimetableInitializer init)
        {
            if (init?.Stations == null) return $"node#{nodeId}";
            float bestDist = float.MaxValue;
            string bestName = $"node#{nodeId}";
            var pos = init.Graph.GetNode(nodeId).position;
            foreach (var st in init.Stations)
            {
                float d = (st.position - pos).sqrMagnitude;
                if (d < bestDist) { bestDist = d; bestName = st.name; }
            }
            return bestName;
        }

        private static string FormatTime(long absoluteSec)
        {
            int h = (int)((absoluteSec / 3600) % 24);
            int m = (int)((absoluteSec / 60) % 60);
            return $"{h:D2}:{m:D2}";
        }
    }
}
