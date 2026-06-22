using UnityEngine;
using RailwayManager.Core;

namespace DepotSystem
{
    public partial class DepotMovementSimulator
    {
        // ── State machine + Movement ─────────────────────────────────

        void ProcessTask(DepotMoveTask task, float deltaSec)
        {
            switch (task.state)
            {
                case DepotMoveState.Queued:
                    task.state = DepotMoveState.Pathfinding;
                    break;

                case DepotMoveState.Pathfinding:
                    ExecutePathfinding(task);
                    break;

                case DepotMoveState.Moving:
                    AdvanceMovement(task, deltaSec);
                    break;

                case DepotMoveState.Completed:
                    // Fallback — FinalizeTask zwykle wywoływane w AdvanceMovement przy transition.
                    // Ten case runs tylko gdy task trafi tu w innych okolicznościach.
                    FinalizeTask(task);
                    break;

                case DepotMoveState.Failed:
                    Log.Warn($"[DepotMovementSim] Task failed: consist#{task.consistId} — {task.failureReason}");
                    // Clean up visual jeśli jakiś powstał
                    break;
            }
        }

        // ── Task finalization ────────────────────────────────────────

        /// <summary>
        /// Wywoływane gdy task przechodzi do Completed — release tracks, update marker,
        /// promote pending task. Musi być idempotentne (flag _finalized).
        /// </summary>
        void FinalizeTask(DepotMoveTask task)
        {
            if (task.finalized) return;
            task.finalized = true;

            bool willPromote = _pendingNextTask.ContainsKey(task.consistId); // TD-032: re-route nie odpala promptu sprzęgu

            // TD-031: occupancy = interwały aktualizowane co tick. Przy finalizacji footprint jest już
            // poprawny (UpdateOccupantIntervalsForTask zostawił interwały tylko tam gdzie consist fizycznie
            // stoi). Nie zwalniamy "całych torów" — to robił model binarny.

            // Zaktualizuj ConsistMarker.currentTrackId z rzeczywistej pozycji visual'u
            if (_consistVisuals.TryGetValue(task.consistId, out var visual) && visual != null)
            {
                var marker = visual.GetComponent<ConsistMarker>();
                if (marker != null)
                {
                    var pos = visual.transform.position;
                    pos.y -= VehicleYHeight;
                    int actualTrack = FindNearestTrackToPosition(pos);
                    marker.currentTrackId = actualTrack >= 0 ? actualTrack : task.toTrackId;
                }
            }

            Log.Info($"[DepotMovementSim] Task finalized: consist#{task.consistId} on track#{task.toTrackId}");

            // Exit flow: despawn obsługiwany w AdvanceMovement (gdy visual przekroczy granicę).
            // Jeśli task zakończył się z exitAfterComplete=true ale bez despawn'u (brake pre-gate),
            // oznacza to że zatrzymał się przed bramą — przekaż flagę do pending (re-route).
            if (task.exitAfterComplete)
            {
                if (_pendingNextTask.TryGetValue(task.consistId, out var pendingForExit))
                {
                    pendingForExit.exitAfterComplete = true;
                    Log.Info($"[DepotMovementSim] Przekazano flagę exit do pending task consist#{task.consistId} " +
                             $"(zatrzymał się przed bramą: {task.stopDistanceM:F1}m / {task.totalLengthM:F1}m)");
                }
            }

            // Entry flow: consist wjechał i zatrzymał się przed bramą (pień permanentnego toru).
            // Emituj event — M9c potwierdzi handshake, gracz dostaje kontrolę.
            if (task.entryOnComplete)
            {
                Log.Info($"[DepotMovementSim] Consist#{task.consistId} ENTERED depot — emituję OnConsistEnteredDepot");
                OnConsistEnteredDepot?.Invoke(task.consistId, task.vehicleIds);
            }

            // Promuj pending task (re-route po hamowaniu)
            if (_pendingNextTask.TryGetValue(task.consistId, out var pending))
            {
                _pendingNextTask.Remove(task.consistId);

                // Określ actual fromTrackId z aktualnej pozycji visual'u
                if (_consistVisuals.TryGetValue(task.consistId, out var v) && v != null)
                {
                    var pos = v.transform.position;
                    pos.y -= VehicleYHeight;
                    int actualTrack = FindNearestTrackToPosition(pos);
                    if (actualTrack >= 0) pending.fromTrackId = actualTrack;
                }
                _tasks.Add(pending);
                Log.Info($"[DepotMovementSim] Promoted pending task for consist#{task.consistId}: " +
                         $"now moving from track#{pending.fromTrackId} → #{pending.toTrackId}");
            }

            // MM-18: callback per-task (np. OutdoorEquipmentJobService przejście EnRoute → Servicing).
            // Wywołane PO release zasobów + emisji OnConsistEnteredDepot żeby subscriber widział
            // świeży stan grafu i markera.
            if (task.onCompleted != null)
            {
                try
                {
                    task.onCompleted.Invoke(task.state);
                }
                catch (System.Exception ex)
                {
                    Log.Warn($"[DepotMovementSim] task.onCompleted threw for consist#{task.consistId}: {ex.Message}");
                }
            }

            // TD-032: dojazd DO STYKU za innym składem (ruch gracza — nie exit/entry/serwis, bez re-route)
            // → prompt sprzęgu nad punktem styku.
            if (task.arrivedContactBlockerId >= 0 && !task.exitAfterComplete && !task.entryOnComplete
                && task.onCompleted == null && !willPromote)
            {
                OnConsistArrivedAtContact?.Invoke(task.consistId, task.arrivedContactBlockerId, task.arrivedContactWorldPos);
            }
            task.arrivedContactBlockerId = -1;
        }

        // ── Ruch 3D po polyline (Etap 2) ─────────────────────────────

        /// <summary>Cap prędkości przy cofaniu (realistycznie wolniej niż do przodu).</summary>
        const float DepotReverseSpeedMps = 4f; // ~14 km/h

        void AdvanceMovement(DepotMoveTask task, float deltaSec)
        {
            if (task.polyline == null || task.polyline.Count < 2 || task.totalLengthM <= 0f)
            {
                task.state = DepotMoveState.Completed;
                return;
            }

            EnsureVisualForConsist(task);

            // TD-031: cel gracza/końca polyline ograniczony dynamicznym capem (najbliższa INNA jednostka
            // w kierunku jazdy). cap przeliczony w pass 1 (RecomputeAllDynamicStopCaps) z początku ticku.
            float planned = task.stopDistanceM > 0f ? task.stopDistanceM : task.totalLengthM;
            bool forward = planned >= task.currentDistanceM;
            float cap = task.dynamicStopCapM;

            float effectiveEnd;
            if (forward)
            {
                effectiveEnd = float.IsPositiveInfinity(cap) ? planned : Mathf.Min(planned, cap);
                if (effectiveEnd < task.currentDistanceM) effectiveEnd = task.currentDistanceM; // nie cofaj przez cap
            }
            else
            {
                effectiveEnd = float.IsNegativeInfinity(cap) ? planned : Mathf.Max(planned, cap);
                if (effectiveEnd > task.currentDistanceM) effectiveEnd = task.currentDistanceM;
            }

            // TD-031/032: cap wiążący (jednostka z przodu, nie cel gracza) + połowa długości (offset do nosa).
            bool capBinding = forward ? (cap < planned - 0.01f) : (cap > planned + 0.01f);
            float halfLen = ComputeConsistScale(task.vehicleIds).z * 0.5f;

            // SIGNED physics: currentSpeedMps > 0 = do przodu (+ polyline direction), < 0 = cofanie.
            // signedRemaining > 0 = cel przed nami (po polyline), < 0 = za nami (trzeba cofnąć).
            float signedRemaining = effectiveEnd - task.currentDistanceM;
            float absRemaining = Mathf.Abs(signedRemaining);
            float absSpeed = Mathf.Abs(task.currentSpeedMps);

            // Warunek stopu: dojechał do dozwolonego końca (cel gracza LUB styk z jednostką) + mała prędkość.
            // Gdy effectiveEnd = styk z jednostką STOJĄCĄ → consist kończy ruch przy styku. Gdy jednostka
            // z przodu JEDZIE → follower ma absSpeed>0.5 i NIE kończy (jedzie za nią — naturalne podążanie).
            if (absRemaining < 0.5f && absSpeed < 0.5f)
            {
                task.currentDistanceM = effectiveEnd;
                task.currentSpeedMps = 0f;
                UpdateVisualPosition(task);
                UpdateOccupantIntervalsForTask(task);
                CaptureContactArrival(task, capBinding, forward, halfLen);
                FinalizeTask(task);
                task.state = DepotMoveState.Completed;
                return;
            }

            // Wybierz target velocity (signed):
            // - Pożądany kierunek = znak signedRemaining (cel przed → +, za → -)
            // - Jeśli jedziemy w złym kierunku → brake do 0 (nie skaczemy do przeciwnego znaku w 1 ticku)
            // - Jeśli w dobrym kierunku i zostało brakingDist lub mniej → brake do 0 (zatrzyma się ~w celu)
            // - W przeciwnym razie → accel do cruise speed (+DepotCruiseSpeedMps lub -DepotReverseSpeedMps)
            // BUG-047: porównanie kierunków — `currentDir`/`desiredDir` są wynikami ternary
            // z dokładnymi wartościami {-1f, 0f, 1f} (epsilon 0.01 w currentDir filtrze już
            // załatwia residual velocity). `Mathf.Approximately` dla pełnej zgodności z
            // Unity best practice (float == NaN safety).
            float desiredDir = signedRemaining > 0f ? 1f : (signedRemaining < 0f ? -1f : 0f);
            float currentDir = task.currentSpeedMps > 0.01f ? 1f : (task.currentSpeedMps < -0.01f ? -1f : 0f);

            float brakingDist = (absSpeed * absSpeed) / (2f * DepotDecelMps2);
            bool currentNonZero = !Mathf.Approximately(currentDir, 0f);
            bool desiredNonZero = !Mathf.Approximately(desiredDir, 0f);
            bool wrongDirection = currentNonZero && desiredNonZero && !Mathf.Approximately(currentDir, desiredDir);
            bool shouldBrake = wrongDirection || (Mathf.Approximately(currentDir, desiredDir) && absRemaining <= brakingDist + absSpeed * deltaSec);

            float targetSpeed;
            if (shouldBrake || Mathf.Approximately(desiredDir, 0f))
            {
                targetSpeed = 0f;
            }
            else
            {
                float cruise = desiredDir > 0f ? DepotCruiseSpeedMps : -DepotReverseSpeedMps;
                targetSpeed = cruise;
            }

            // TD-031: wolny dojazd (crawl) gdy zbliżamy się do JEDNOSTKI z przodu (cap wiążący, nie cel
            // gracza) i jesteśmy blisko — manewr podczepiania ~1.5 m/s. Dla zwykłego celu crawl off.
            if (capBinding && absRemaining < DepotOccupancyConstants.ApproachSlowdownDistM
                && !Mathf.Approximately(targetSpeed, 0f))
            {
                float crawl = DepotOccupancyConstants.CouplingApproachSpeedMps;
                if (targetSpeed > crawl) targetSpeed = crawl;
                else if (targetSpeed < -crawl) targetSpeed = -crawl;
            }

            // Ramp speed toward target (signed)
            if (task.currentSpeedMps < targetSpeed)
            {
                float rate = (task.currentSpeedMps < 0f) ? DepotDecelMps2 : DepotAccelMps2;
                task.currentSpeedMps += rate * deltaSec;
                if (task.currentSpeedMps > targetSpeed) task.currentSpeedMps = targetSpeed;
            }
            else if (task.currentSpeedMps > targetSpeed)
            {
                float rate = (task.currentSpeedMps > 0f) ? DepotDecelMps2 : DepotAccelMps2;
                task.currentSpeedMps -= rate * deltaSec;
                if (task.currentSpeedMps < targetSpeed) task.currentSpeedMps = targetSpeed;
            }

            // Aktualizuj pozycję (signed)
            task.currentDistanceM += task.currentSpeedMps * deltaSec;

            // Clamp do zakresu polyline — consist nie może wyjechać za fizyczny koniec toru
            if (task.currentDistanceM < 0f) task.currentDistanceM = 0f;
            if (task.currentDistanceM > task.totalLengthM) task.currentDistanceM = task.totalLengthM;

            // Exit flow: gdy visual przejedzie margines za bramą → despawn + event (niezależnie od stopu)
            // M-Fleet-4: margines = połowa consist'u + stały 15m buforu, żeby tail cube'a nie wystawał
            if (task.exitAfterComplete && _consistVisuals.TryGetValue(task.consistId, out var exitCheckVisual) && exitCheckVisual != null)
            {
                float dynamicMargin = ComputeConsistScale(task.vehicleIds).z * 0.5f + 15f;
                if (IsBeyondDespawnMargin(exitCheckVisual.transform.position, dynamicMargin))
                {
                    _graph.RemoveConsistEverywhere(task.consistId); // TD-031: usuń footprint ze wszystkich torów
                    Destroy(exitCheckVisual);
                    _consistVisuals.Remove(task.consistId);

                    Log.Info($"[DepotMovementSim] Consist#{task.consistId} przekroczył granicę depot — EXITED, emituję event");
                    OnConsistExitedDepot?.Invoke(task.consistId, task.vehicleIds);

                    task.finalized = true;
                    task.state = DepotMoveState.Completed;
                    return;
                }
            }

            // TD-031: zapisz footprint po ruchu (zanim ew. complete) — by inni movery widzieli nas w pass 1.
            UpdateOccupantIntervalsForTask(task);

            // Ponowny check stopu po update pozycji (dla snap gdy dojdziemy po reverse / do styku)
            float postAbsRemaining = Mathf.Abs(effectiveEnd - task.currentDistanceM);
            float postAbsSpeed = Mathf.Abs(task.currentSpeedMps);
            if (postAbsRemaining < 0.5f && postAbsSpeed < 0.5f)
            {
                task.currentDistanceM = effectiveEnd;
                task.currentSpeedMps = 0f;
                UpdateVisualPosition(task);
                UpdateOccupantIntervalsForTask(task);
                CaptureContactArrival(task, capBinding, forward, halfLen);
                FinalizeTask(task);
                task.state = DepotMoveState.Completed;
                return;
            }

            UpdateVisualPosition(task);
        }

        /// <summary>
        /// Zwraca pozycję 3D i tangent (znormalizowany kierunek) na polyline dla danego dystansu.
        /// Binary search O(log N) na cumDistM.
        /// </summary>
        static (Vector3 pos, Vector3 tangent) SampleAtDistance(DepotMoveTask task, float distM)
        {
            var poly = task.polyline;
            var cum = task.cumDistM;

            if (poly == null || poly.Count < 2) return (Vector3.zero, Vector3.forward);
            if (distM <= 0f) return (poly[0], (poly[1] - poly[0]).normalized);
            if (distM >= task.totalLengthM)
            {
                int last = poly.Count - 1;
                return (poly[last], (poly[last] - poly[last - 1]).normalized);
            }

            int lo = 0, hi = cum.Length - 1;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (cum[mid] < distM) lo = mid + 1;
                else hi = mid;
            }
            int idx = Mathf.Max(lo - 1, 0);

            float segStart = cum[idx];
            float segEnd = cum[idx + 1];
            float t = (distM - segStart) / Mathf.Max(segEnd - segStart, 0.001f);
            Vector3 pos = Vector3.Lerp(poly[idx], poly[idx + 1], t);
            Vector3 tangent = (poly[idx + 1] - poly[idx]).normalized;
            return (pos, tangent);
        }
    }
}
