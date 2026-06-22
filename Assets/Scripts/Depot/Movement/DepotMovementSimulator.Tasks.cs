using System.Collections.Generic;
using UnityEngine;
using RailwayManager.Core;

namespace DepotSystem
{
    public partial class DepotMovementSimulator
    {
        // ── Public API: Enqueue / Spawn ──────────────────────────────

        /// <summary>
        /// TD-031: czy skład ma jakikolwiek pojazd z napędem (może jechać sam). Zestaw samych wagonów /
        /// luźny wagon → false (nie rusza się bez lokomotywy; sprzęganie = TD-032). Wyliczane z
        /// FleetVehicleData: powerKw &gt; 0, typ samobieżny (EMU/DMU/loko), lub supportedTractions ≠ [None].
        /// </summary>
        public static bool ConsistHasTraction(List<int> vehicleIds)
        {
            if (vehicleIds == null || vehicleIds.Count == 0) return false;
            foreach (var v in RailwayManager.Fleet.FleetService.OwnedVehicles)
            {
                if (!vehicleIds.Contains(v.id)) continue;
                if (v.powerKw > 0) return true;
                if (v.type == RailwayManager.Fleet.FleetVehicleType.ElectricLocomotive
                    || v.type == RailwayManager.Fleet.FleetVehicleType.DieselLocomotive
                    || v.type == RailwayManager.Fleet.FleetVehicleType.EMU
                    || v.type == RailwayManager.Fleet.FleetVehicleType.DMU) return true;
                if (v.supportedTractions != null)
                    foreach (var t in v.supportedTractions)
                        if (t != RailwayManager.Fleet.TractionType.None) return true;
            }
            return false;
        }

        /// <summary>
        /// Dodaje zadanie ruchu do kolejki. Simulator wykona pathfinding w następnym
        /// FixedUpdate i rozpocznie ruch (Etap 2+).
        /// </summary>
        /// <param name="isSelfMove">TD-031: true = ruch własnym napędem (gracz/serwis) — wymaga
        /// napędu w składzie; false = ruch sterowany przez system (wjazd z zewnątrz, debug) — omija regułę.</param>
        /// <returns>true jeśli dodano; false gdy niepoprawne parametry / brak napędu.</returns>
        public bool EnqueueMove(int consistId, List<int> vehicleIds, int fromTrackId, int toTrackId,
                                Vector3? targetWorldPos = null, bool isSelfMove = true)
        {
            EnsureGraph();

            // TD-031: skład bez napędu nie rusza się sam (luźny wagon). isSelfMove=false dla ruchów
            // sterowanych przez system (wjazd z zewnątrz, debug) — te omijają regułę.
            if (isSelfMove && !ConsistHasTraction(vehicleIds))
            {
                Log.Warn($"[DepotMovementSim] consist#{consistId} bez napędu (luźny wagon) — nie może " +
                         $"jechać sam; wymaga lokomotywy (sprzęganie: TD-032)");
                return false;
            }

            // Znajdź aktywny task tego consist'u (jeśli jest)
            DepotMoveTask activeTask = null;
            foreach (var t in _tasks)
            {
                if (t.consistId == consistId) { activeTask = t; break; }
            }

            // Przygotuj nowy task (fromTrackId ustalany niżej po override)
            var newTask = new DepotMoveTask
            {
                consistId = consistId,
                vehicleIds = vehicleIds ?? new List<int>(),
                fromTrackId = fromTrackId,
                toTrackId = toTrackId,
                targetWorldPos = targetWorldPos,
                state = DepotMoveState.Queued
            };

            if (activeTask != null && activeTask.state == DepotMoveState.Moving && activeTask.currentSpeedMps > 0.1f)
            {
                // Consist mid-move. Sprawdź kierunek nowego taska:
                // - zgodny z bieżącym → podmień task in-place zachowując prędkość (brak hamowania)
                // - przeciwny → klasyczny flow: hamuj do 0, nowy task do pending
                //
                // Robimy inline pathfinding nowego taska (ExecutePathfinding rezerwuje tory tym samym
                // consistId co activeTask, więc IsTrackFreeFor przepuści).

                // Override fromTrackId z pozycji visual'u
                if (_consistVisuals.TryGetValue(consistId, out var vMid) && vMid != null)
                {
                    var pos = vMid.transform.position;
                    pos.y -= VehicleYHeight;
                    int actualTrack = FindNearestTrackToPosition(pos);
                    if (actualTrack >= 0) newTask.fromTrackId = actualTrack;
                }

                newTask.state = DepotMoveState.Pathfinding;
                _tasks.Add(newTask);
                ExecutePathfinding(newTask);

                if (newTask.state == DepotMoveState.Moving && newTask.polyline != null && newTask.polyline.Count >= 2)
                {
                    // Tangent na nowej polyline w aktualnej pozycji consist'u
                    Vector3 newTangent = SampleAtDistance(newTask, newTask.currentDistanceM).tangent;
                    Vector3 oldTangent = SampleAtDistance(activeTask, activeTask.currentDistanceM).tangent;
                    bool sameDir = Vector3.Dot(newTangent, oldTangent) > 0f;

                    if (sameDir)
                    {
                        // KONTYNUUJ — przenieś prędkość. TD-031: occupancy jest per-consistId (te same
                        // interwały), więc nie „dziedziczymy rezerwacji"; scalamy reservedTrackIds tylko
                        // jako zbiór dotkniętych torów (info/cleanup). Footprint utrzymuje per-tick
                        // UpdateOccupantIntervalsForTask, a stary footprint czyści RemoveConsistEverywhere.
                        newTask.currentSpeedMps = activeTask.currentSpeedMps;

                        if (activeTask.reservedTrackIds != null)
                        {
                            if (newTask.reservedTrackIds == null)
                                newTask.reservedTrackIds = new List<int>();
                            foreach (int oldTid in activeTask.reservedTrackIds)
                            {
                                if (!newTask.reservedTrackIds.Contains(oldTid))
                                    newTask.reservedTrackIds.Add(oldTid);
                            }
                        }
                        _tasks.Remove(activeTask);

                        Log.Info($"[DepotMovementSim] Consist#{consistId} re-route SAME DIRECTION — " +
                                 $"kontynuuje ze speed={newTask.currentSpeedMps:F1}m/s " +
                                 $"(stop={newTask.stopDistanceM:F1}m, total={newTask.totalLengthM:F1}m)");
                        return true;
                    }

                    // Kierunek przeciwny — cofnij inline pathfinding, wróć do brake+pending.
                    if (newTask.reservedTrackIds != null)
                    {
                        foreach (int trackId in newTask.reservedTrackIds)
                        {
                            // Zwolnij tylko te rezerwacje które NIE należą do activeTask
                            // (inaczej zwolnilibyśmy tor po którym consist aktualnie jedzie).
                            if (activeTask.reservedTrackIds != null && activeTask.reservedTrackIds.Contains(trackId))
                                continue;
                            _graph.FreeTrackForConsist(trackId, consistId);
                        }
                    }
                }

                _tasks.Remove(newTask);
                newTask.state = DepotMoveState.Queued;
                newTask.polyline = null;
                newTask.cumDistM = null;
                newTask.pathNodeIds = null;
                newTask.reservedTrackIds = null;
                newTask.currentDistanceM = 0f;
                newTask.totalLengthM = 0f;
                newTask.stopDistanceM = 0f;

                // Hamowanie do 0 + pending
                float brakingDist = (activeTask.currentSpeedMps * activeTask.currentSpeedMps) / (2f * DepotDecelMps2);
                activeTask.stopDistanceM = Mathf.Min(
                    activeTask.currentDistanceM + brakingDist + 2f, // 2m safety margin
                    activeTask.totalLengthM);
                activeTask.targetWorldPos = null;

                _pendingNextTask[consistId] = newTask;

                Log.Info($"[DepotMovementSim] Consist#{consistId} OPPOSITE DIRECTION re-route — hamuje " +
                         $"(speed={activeTask.currentSpeedMps:F1}m/s, brakingDist={brakingDist:F1}m, " +
                         $"newStop={activeTask.stopDistanceM:F1}m). New task queued as pending.");
                return true;
            }

            // Consist stoi (lub brak aktywnego taska) — anuluj stary task (jeśli stoi w Queued/Pathfinding) i startuj nowy
            for (int i = _tasks.Count - 1; i >= 0; i--)
            {
                if (_tasks[i].consistId == consistId)
                {
                    if (_tasks[i].reservedTrackIds != null)
                    {
                        foreach (int trackId in _tasks[i].reservedTrackIds)
                            _graph.FreeTrackForConsist(trackId, consistId);
                    }
                    _tasks.RemoveAt(i);
                }
            }

            // Override fromTrackId z aktualnej pozycji visual'u (gdyby marker.currentTrackId był stale)
            if (_consistVisuals.TryGetValue(consistId, out var existingVisual) && existingVisual != null)
            {
                var pos = existingVisual.transform.position;
                pos.y -= VehicleYHeight;
                int actualTrack = FindNearestTrackToPosition(pos);
                if (actualTrack >= 0 && actualTrack != newTask.fromTrackId)
                    newTask.fromTrackId = actualTrack;
            }

            _tasks.Add(newTask);
            Log.Info($"[DepotMovementSim] Enqueued: consist#{consistId} from track#{newTask.fromTrackId} → #{toTrackId}" +
                     (targetWorldPos.HasValue ? $" at worldPos={targetWorldPos.Value}" : ""));
            return true;
        }

        /// <summary>
        /// Wyślij consist na wyjazd z depot. AUTOMATYCZNIE wykrywa tory zewnętrzne
        /// (te których node'y są poza GroundGenerator.BuildableArea — za ogrodzeniem).
        /// Consist pathfinduje do najbliższego outside node'u; podczas jazdy, gdy visual
        /// przekroczy granicę depot, emituje OnConsistExitedDepot i despawnuje.
        /// </summary>
        public bool EnqueueExit(int consistId, List<int> vehicleIds)
        {
            EnsureGraph();
            if (_graph == null) return false;

            // TD-031: bez napędu nie wyjedzie sam (wymaga lokomotywy, TD-032).
            if (!ConsistHasTraction(vehicleIds))
            {
                Log.Warn($"[DepotMovementSim] EnqueueExit: consist#{consistId} bez napędu — nie może " +
                         $"wyjechać sam (wymaga lokomotywy, TD-032)");
                return false;
            }

            if (!_consistVisuals.TryGetValue(consistId, out var visual) || visual == null)
            {
                Log.Warn($"[DepotMovementSim] EnqueueExit: consist#{consistId} has no visual");
                return false;
            }

            var outsideNodes = GetOutsideNodes();
            if (outsideNodes.Count == 0)
            {
                Log.Warn($"[DepotMovementSim] EnqueueExit: brak node'ów poza granicą depot " +
                         "(dobuduj tor wyjazdowy wychodzący za ogrodzenie)");
                return false;
            }

            // Określ current fromTrackId z pozycji visual'u
            var pos = visual.transform.position;
            pos.y -= VehicleYHeight;
            int fromTrackId = FindNearestTrackToPosition(pos);
            if (fromTrackId < 0)
            {
                Log.Warn($"[DepotMovementSim] EnqueueExit: cannot determine fromTrackId for consist#{consistId}");
                return false;
            }

            // Wybierz outside node dający najkrótszą osiągalną ścieżkę → znajdź track zawierający jego edge.
            // Strategia: dla każdego outside node'u znajdź track który ma ten node jako endpoint,
            // potem spróbuj pathfind — wybierz pierwszy działający.
            int bestTrackId = -1;
            Vector3 bestTargetPos = Vector3.zero;
            float bestStraightDist = float.MaxValue;

            foreach (int outNodeId in outsideNodes)
            {
                // Znajdź tracki których EdgeIds zawierają edge kończący się w tym outside node'ie
                foreach (var kvp in _graph.Tracks)
                {
                    var trackData = kvp.Value;
                    if (trackData.EdgeIds == null) continue;

                    bool hasOutNode = false;
                    foreach (int eid in trackData.EdgeIds)
                    {
                        var edge = _graph.GetEdge(eid);
                        if (edge == null) continue;
                        if (edge.FromNodeId == outNodeId || edge.ToNodeId == outNodeId)
                        { hasOutNode = true; break; }
                    }
                    if (!hasOutNode) continue;

                    var outNode = _graph.GetNode(outNodeId);
                    if (outNode == null) continue;

                    float d = Vector3.Distance(visual.transform.position, outNode.Position);
                    if (d < bestStraightDist)
                    {
                        bestStraightDist = d;
                        bestTrackId = trackData.TrackId;
                        bestTargetPos = outNode.Position;
                    }
                }
            }

            if (bestTrackId < 0)
            {
                Log.Warn($"[DepotMovementSim] EnqueueExit: żaden outside node nie należy do żadnego toru");
                return false;
            }

            // Wywołaj EnqueueMove z targetWorldPos = pozycja outside node'u.
            // Heurystyka kierunku w ExecutePathfinding wybierze właściwy endpoint.
            if (!EnqueueMove(consistId, vehicleIds, fromTrackId, bestTrackId, bestTargetPos)) return false;

            // Zaznacz nowy task (albo pending) flagą exitAfterComplete
            foreach (var t in _tasks)
            {
                if (t.consistId == consistId && t.toTrackId == bestTrackId)
                {
                    t.exitAfterComplete = true;
                    break;
                }
            }
            if (_pendingNextTask.TryGetValue(consistId, out var pending) && pending.toTrackId == bestTrackId)
                pending.exitAfterComplete = true;

            // M9c-D F6: oznacz pojazdy jako ExitingDepot (manewr do bramy). Wcześniej stan zostawał
            // InDepot aż do przekroczenia granicy — ExitingDepot był martwym kodem, UI nie pokazywał
            // fazy wyjazdu. trainRunId=-1: handshake ustawi OnRoute przy OnConsistExitedDepot.
            if (vehicleIds != null)
            {
                var locSvc = RailwayManager.Core.VehicleLocationService.Instance;
                if (locSvc != null)
                    foreach (int vid in vehicleIds)
                        locSvc.SetExitingDepot(vid, consistId, -1);
            }

            Log.Info($"[DepotMovementSim] EnqueueExit: consist#{consistId} → outside track#{bestTrackId} " +
                     $"(target pos={bestTargetPos}, straight dist={bestStraightDist:F1}m)");
            return true;
        }

        /// <summary>
        /// M9b Etap 5b: wjazd consist'u do depot z zewnątrz.
        /// Spawnuje visual na outside endpoint permanentnego toru zewnętrznego,
        /// auto-enqueue move do inside endpoint (pień za bramą). Consist zatrzymuje się
        /// przed bramą i czeka na sterowanie gracza. Emituje OnConsistEnteredDepot po dotarciu.
        /// </summary>
        /// <param name="consistId">ID consist'u (powinien być unikalny — poprzedni visual zostaje zdestroyowany jeśli jeszcze istnieje)</param>
        /// <param name="vehicleIds">Lista ID pojazdów w składzie</param>
        /// <param name="preferredTrackId">Opcjonalnie — konkretny permanent track (np. gdy wiele bram). -1 = pierwszy dostępny.</param>
        public bool SpawnConsistAtEntry(int consistId, List<int> vehicleIds, int preferredTrackId = -1)
        {
            EnsureGraph();
            if (_graph == null) return false;

            // Znajdź permanentny tor z outside endpoint (kandydat do spawnu).
            // Jeśli preferredTrackId podany — użyj go (musi być permanent + mieć outside node).
            DepotTrackData spawnTrack = null;
            int outsideNodeId = -1;
            int insideNodeId = -1;

            foreach (var kvp in _graph.Tracks)
            {
                var trackData = kvp.Value;
                if (!trackData.IsPermanent) continue;
                if (preferredTrackId >= 0 && trackData.TrackId != preferredTrackId) continue;

                int foundOutside = -1, foundInside = -1;
                var endpoints = GetTrackEndpointNodes(trackData);
                foreach (int nid in endpoints)
                {
                    var node = _graph.GetNode(nid);
                    if (node == null) continue;
                    if (IsOutsideDepot(node.Position)) foundOutside = nid;
                    else foundInside = nid;
                }
                if (foundOutside >= 0 && foundInside >= 0)
                {
                    spawnTrack = trackData;
                    outsideNodeId = foundOutside;
                    insideNodeId = foundInside;
                    break;
                }
            }

            if (spawnTrack == null)
            {
                Log.Warn($"[DepotMovementSim] SpawnConsistAtEntry: brak permanentnego toru z outside+inside endpoint " +
                         $"(preferredTrackId={preferredTrackId})");
                return false;
            }

            // Zdestroy poprzedni visual jeśli istnieje (clean re-spawn)
            if (_consistVisuals.TryGetValue(consistId, out var oldVisual) && oldVisual != null)
            {
                Destroy(oldVisual);
                _consistVisuals.Remove(consistId);
            }

            // Spawn visual na outside endpoint
            var outsideNode = _graph.GetNode(outsideNodeId);
            var insideNode = _graph.GetNode(insideNodeId);
            Vector3 spawnPos = outsideNode.Position;
            Vector3 dirToInside = (insideNode.Position - outsideNode.Position).normalized;

            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = $"Consist_{consistId}";
            go.transform.SetParent(_visualsContainer);
            go.transform.localScale = ComputeConsistScale(vehicleIds);
            go.transform.position = new Vector3(spawnPos.x, spawnPos.y + VehicleYHeight, spawnPos.z);
            if (dirToInside.sqrMagnitude > 0.001f)
                go.transform.rotation = Quaternion.LookRotation(dirToInside, Vector3.up);

            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null && _visualMaterial != null)
                mr.sharedMaterial = _visualMaterial;

            var col = go.GetComponent<Collider>();
            if (col != null) col.isTrigger = true;

            var marker = go.AddComponent<ConsistMarker>();
            marker.consistId = consistId;
            marker.vehicleIds = vehicleIds ?? new List<int>();
            marker.currentTrackId = spawnTrack.TrackId;
            marker.meshRenderer = mr;
            marker.originalMaterial = _visualMaterial;

            _consistVisuals[consistId] = go;

            // Znajdź pozycję linii bramy na polyline — punkt gdzie polyline przechodzi
            // z outside do inside. Consist zatrzymuje się przed bramą (z noskiem na linii),
            // całym cielskiem za bramą — nie wjeżdża w obszar budowlany gracza.
            Vector3? gateCrossing = FindGateCrossingOnTrack(spawnTrack);
            Vector3 stopTarget;
            if (gateCrossing.HasValue)
            {
                // M-Fleet-4: offset = połowa długości consist'u + 2m buforu, żeby
                // cube center był offset'owany od linii bramy o tyle, że nos cube'u
                // trafi mniej więcej na bramę. Działa dla krótkich SM42 (14m) i długich
                // FLIRT ED160 (160m).
                Vector3 toOutside = (outsideNode.Position - insideNode.Position);
                if (toOutside.sqrMagnitude > 0.001f)
                    toOutside.Normalize();
                float consistLength = ComputeConsistScale(vehicleIds).z;
                float entryStopOffsetM = consistLength * 0.5f + 2f;
                stopTarget = gateCrossing.Value + toOutside * entryStopOffsetM;
            }
            else
            {
                stopTarget = insideNode.Position; // fallback — gdyby tor nie przecinał granicy
            }

            // Auto-enqueue move do linii bramy
            if (!EnqueueMove(consistId, vehicleIds, spawnTrack.TrackId, spawnTrack.TrackId, stopTarget, isSelfMove: false))
            {
                Log.Warn($"[DepotMovementSim] SpawnConsistAtEntry: EnqueueMove failed dla consist#{consistId}");
                // Visual zostaje — gracz może go ruszyć manualnie (albo event SpawnFailed? na razie nie)
                return false;
            }

            // Zaznacz task flagą entryOnComplete
            foreach (var t in _tasks)
            {
                if (t.consistId == consistId && t.toTrackId == spawnTrack.TrackId)
                {
                    t.entryOnComplete = true;
                    break;
                }
            }

            Log.Info($"[DepotMovementSim] SpawnConsistAtEntry: consist#{consistId} spawned at {spawnPos} " +
                     $"(track#{spawnTrack.TrackId}), auto-moves to inside endpoint {insideNode.Position}");
            return true;
        }

        /// <summary>M9c-D F2: czy TrackGraph jest gotowy (initial park startowego taboru czeka na to).</summary>
        public bool IsGraphReady { get { EnsureGraph(); return _graph != null; } }

        /// <summary>
        /// M9c-D F2: parkuje pojazdy na pierwszym wolnym torze parkingowym jako gotowy, klikalny
        /// consist (startowy tabor na nową grę / dostawa bez animacji wjazdu — fallback). Rezerwuje
        /// tor (<see cref="TrackGraph.OccupyTrackByConsist"/>) + tworzy ConsistMarker. Zwraca trackId
        /// lub -1 gdy brak grafu / wolnego toru.
        /// </summary>
        public int ParkConsistOnFreeTrack(int consistId, List<int> vehicleIds)
        {
            EnsureGraph();
            if (_graph == null) return -1;

            float consistLen = ComputeConsistScale(vehicleIds).z;

            // TD-031: szukaj toru z wolną LUKĄ długości consistLen (pakowanie wielu składów na jeden
            // tor gdy zabraknie dedykowanych). Najpierw tory parkingowe, potem dowolny niepermanentny.
            // Iteracja sort po trackId (determinizm).
            DepotTrackData chosen = null;
            float gapStart = 0f;

            var parking = _graph.GetTracksByType(DepotTrackType.Parking);
            parking.Sort((a, b) => a.TrackId.CompareTo(b.TrackId));
            foreach (var t in parking)
            {
                if (t == null) continue;
                if (_graph.TryFindFreeGapForLength(t.TrackId, consistLen, out gapStart)) { chosen = t; break; }
            }

            if (chosen == null)
            {
                var ids = new List<int>(_graph.Tracks.Keys);
                ids.Sort();
                foreach (int tid in ids)
                {
                    var t = _graph.Tracks[tid];
                    if (t == null || t.IsPermanent) continue;
                    if (_graph.TryFindFreeGapForLength(tid, consistLen, out gapStart)) { chosen = t; break; }
                }
            }

            if (chosen == null)
            {
                Log.Warn($"[DepotMovementSim] ParkConsistOnFreeTrack: brak wolnej luki ({consistLen:F1}m) " +
                         $"dla consist#{consistId} ({vehicleIds?.Count ?? 0} pojazdów)");
                return -1;
            }

            // Footprint [front, rear]. Estetyka: gdy tor pusty i consist krótszy — wyśrodkuj (zgodnie
            // z dotychczasowym wyglądem zaparkowanego składu). Przy pakowaniu — od znalezionej luki.
            float front = gapStart;
            float rear = Mathf.Min(gapStart + consistLen, chosen.Length);
            if (_graph.GetOccupants(chosen.TrackId).Count == 0 && consistLen < chosen.Length)
            {
                front = (chosen.Length - consistLen) * 0.5f;
                rear = front + consistLen;
            }

            _graph.SetOccupantInterval(chosen.TrackId, consistId, vehicleIds, front, rear, 1);
            SpawnParkedVisual(consistId, vehicleIds, chosen);
            if (consistId >= _nextConsistId) _nextConsistId = consistId + 1;

            Log.Info($"[DepotMovementSim] Parked consist#{consistId} ({vehicleIds?.Count ?? 0} pojazdów) " +
                     $"na torze#{chosen.TrackId} @ [{front:F1},{rear:F1}]m");
            return chosen.TrackId;
        }

        /// <summary>
        /// TD-031: wznawia manewr po wczytaniu save'a. Consist jest już ustawiony na dokładnej pozycji
        /// przez <see cref="RestoreParkedVisualsFromGraph"/>; tu re-pathfindujemy od bieżącej pozycji do
        /// toTrack/target i jedziemy dalej (pozycja exact, prędkość od 0 — re-akceleracja).
        ///
        /// Manewry zwykłe (gracz/exit/entry) wznawia <see cref="RailwayManager.SaveLoad"/> DepotSavable.
        /// Manewry SERWISOWE (consist do myjni/warsztatu/obrotnicy/PaintBay) wznawia watchdog w
        /// Maintenance (OutdoorEquipmentMovementBridge / WorkshopManager) — wywołuje tę metodę z tym
        /// samym consistId, a po niej podpina <see cref="DepotMoveTask.onCompleted"/> przez
        /// <see cref="GetActiveTask"/> (przeskok EnRoute→Servicing po dotarciu).
        /// </summary>
        /// <returns>true gdy manewr wystawiony (EnqueueMove ok); false gdy się nie udało
        /// (brak grafu / ścieżki / napędu) — caller robi fallback (np. teleport do Servicing).</returns>
        public bool RestoreActiveMove(int consistId, List<int> vehicleIds, int toTrackId,
                                      Vector3? targetWorldPos, bool exitAfterComplete, bool entryOnComplete)
        {
            EnsureGraph();
            if (_graph == null) return false;

            // fromTrack z aktualnej pozycji visual'u (consist odtworzony na swojej pozycji)
            int fromTrackId = toTrackId;
            if (_consistVisuals.TryGetValue(consistId, out var v) && v != null)
            {
                var pos = v.transform.position;
                pos.y -= VehicleYHeight;
                int t = FindNearestTrackToPosition(pos);
                if (t >= 0) fromTrackId = t;
            }

            // entry = ruch sterowany systemem (omija regułę napędu); reszta (gracz/exit/serwis) = self-move
            bool isSelfMove = !entryOnComplete;
            if (!EnqueueMove(consistId, vehicleIds, fromTrackId, toTrackId, targetWorldPos, isSelfMove))
            {
                Log.Warn($"[DepotMovementSim] RestoreActiveMove: nie wznowiono manewru consist#{consistId} " +
                         $"(brak ścieżki / napędu?) — zostaje zaparkowany na bieżącej pozycji");
                return false;
            }

            // Przepisz flagi exit/entry na świeży task (i pending jeśli powstał)
            foreach (var task in _tasks)
                if (task.consistId == consistId && task.toTrackId == toTrackId)
                {
                    task.exitAfterComplete = exitAfterComplete;
                    task.entryOnComplete = entryOnComplete;
                    break;
                }
            if (_pendingNextTask.TryGetValue(consistId, out var pending) && pending.toTrackId == toTrackId)
            {
                pending.exitAfterComplete = exitAfterComplete;
                pending.entryOnComplete = entryOnComplete;
            }

            Log.Info($"[DepotMovementSim] RestoreActiveMove: wznowiono manewr consist#{consistId} → track#{toTrackId}" +
                     (targetWorldPos.HasValue ? $" @ {targetWorldPos.Value}" : ""));
            return true;
        }

        /// <summary>
        /// M9c-D F5: usuwa zaparkowany consist zawierający dany pojazd (loko wyrusza z depot po wagon).
        /// Niszczy visual + zwalnia zajęty tor. Zwraca true gdy znaleziono i usunięto.
        /// </summary>
        public bool DespawnParkedConsist(List<int> vehicleIds)
        {
            if (vehicleIds == null || vehicleIds.Count == 0) return false;
            int targetVid = vehicleIds[0];

            int foundConsistId = -1;
            foreach (var kv in _consistVisuals)
            {
                var marker = kv.Value != null ? kv.Value.GetComponent<ConsistMarker>() : null;
                if (marker?.vehicleIds != null && marker.vehicleIds.Contains(targetVid))
                {
                    foundConsistId = kv.Key;
                    break;
                }
            }
            if (foundConsistId < 0) return false;

            EnsureGraph();
            if (_graph != null)
                _graph.RemoveConsistEverywhere(foundConsistId); // TD-031: usuń footprint ze wszystkich torów

            if (_consistVisuals.TryGetValue(foundConsistId, out var go) && go != null)
                Destroy(go);
            _consistVisuals.Remove(foundConsistId);

            Log.Info($"[DepotMovementSim] Despawned parked consist#{foundConsistId} (loko #{targetVid} wyrusza po wagon)");
            return true;
        }
    }
}
