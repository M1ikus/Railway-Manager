using UnityEngine;
using RailwayManager.Core;
using DepotSystem;
using DepotSystem.Furniture;
using DepotSystem.Furniture.Placement;

namespace RailwayManager.Personnel.Workflows
{
    /// <summary>
    /// TD-034 D: silnik czynności osobistych (walk → reserve mebel → timer → release → resume).
    /// Workflowy wołają <see cref="MaybeStartMidShift"/> (z bezpiecznego stanu pracy/idle),
    /// <see cref="TryBeginArrivalLocker"/> (start zmiany), <see cref="TryBeginDepartureLocker"/> (koniec)
    /// oraz <see cref="Tick"/> (na górze Tick'a, obsługa trwającej czynności).
    ///
    /// <para>Rezerwacja mebla przez <see cref="FurnitureOccupancyService"/> (sloty per-mebel — decyzja #2),
    /// nawigacja przez TD-033, czasy/efekty z <see cref="PersonnelBalanceConstants"/> (placeholder → M-Balance).</para>
    ///
    /// <para>Seam (#1): <see cref="Provider"/> wymienialny na SimulatedNeedProvider bez ruszania workflowów.</para>
    /// </summary>
    public static class PersonalActivities
    {
        /// <summary>TD-034 #1 seam — domyślnie deterministyczny harmonogram; podmienialne (test/future).</summary>
        public static IPersonalNeedProvider Provider = new ScheduledNeedProvider();

        /// <summary>Absolutny game-time (s) — spójny z workflowStateFinishGameTime w workflowach.</summary>
        public static long NowAbs() => (long)GameState.GameTimeSeconds + (long)GameState.GameDay * 86400L;

        // ── Start czynności ───────────────────────────────────────────

        /// <summary>
        /// Z bezpiecznego stanu (WorkingAtStation/AwaitingDeparture/między-zadaniami) — sprawdza provider
        /// i jeśli czynność due, rozpoczyna ją. Zwraca true gdy rozpoczęto (workflow ma return).
        /// </summary>
        public static bool MaybeStartMidShift(Employee e, EmployeeWalkSimulator sim, long now)
        {
            if (Provider == null) return false;
            var due = Provider.GetDueActivity(e, now);
            if (!due.HasValue) return false;
            var kind = due.Value.kind;
            if (kind == PersonalActivityKind.LockerIn) return false; // przebranie obsługuje arrival, nie mid-shift
            if (TryBegin(e, sim, kind, now)) return true;
            MarkConsumed(e, kind, now); // brak mebla → skonsumuj okno, nie spamuj prób co tick
            return false;
        }

        /// <summary>Przebranie w robocze na początku zmiany (role operacyjne). True gdy rozpoczęto.
        /// Degraded: brak wolnej szafki → przebranie "przy bramie" (wearingWorkClothes=true) bez wizyty,
        /// żeby nie retry'ować w kółko.</summary>
        public static bool TryBeginArrivalLocker(Employee e, EmployeeWalkSimulator sim)
        {
            if (!ScheduledNeedProvider.RoleNeedsWorkClothes(e.role)) return false;
            if (e.wearingWorkClothes) return false;
            if (TryBegin(e, sim, PersonalActivityKind.LockerIn, NowAbs())) return true;

            e.wearingWorkClothes = true; // degraded
            SyncClothes(e, true);
            Log.Info($"[PersonalActivities] Brak wolnej szafki dla #{e.employeeId} ({e.role}) — przebranie pominięte (degraded).");
            return false;
        }

        /// <summary>
        /// Trasuje wyjście pracownika przez szafkę (przebranie w prywatne): zamiast prostego walk do bramy →
        /// walk do szafki (reset wearingWorkClothes + release) → walk do bramy → <paramref name="onArriveHome"/>.
        /// Chain bez timera, bo pracownik jest już OFF-shift (workflow.Tick go nie tika — timer by nie zaskoczył).
        /// True gdy zarouted (caller pomija własny gate-walk). Degraded: brak szafki → reset clothes, false.
        /// </summary>
        public static bool TryRouteDepartureViaLocker(Employee e, EmployeeWalkSimulator sim, Vector3 gatePos, System.Action onArriveHome)
        {
            if (!ScheduledNeedProvider.RoleNeedsWorkClothes(e.role) || !e.wearingWorkClothes) return false;
            var occ = FurnitureOccupancyService.Instance;
            var visual = sim.GetVisual(e.employeeId);
            if (occ == null || visual == null) { e.wearingWorkClothes = false; return false; }

            int inst = occ.FindNearestFreeByFunction(ObjectFunction.StoragePersonal, visual.transform.position, null);
            if (inst < 0 || !occ.TryReserve(inst, e.employeeId)) { e.wearingWorkClothes = false; return false; }
            Vector3 seat = occ.GetSeatPoint(inst) ?? visual.transform.position;

            sim.EnqueueTask(new EmployeeWalkTask
            {
                employeeId = e.employeeId,
                destination = seat,
                targetFurnitureId = inst,
                purpose = "Departure→Locker",
                onArrive = () => { occ.ReleaseInstance(inst); e.wearingWorkClothes = false; SyncClothes(e, false); },
                nextTask = new EmployeeWalkTask
                {
                    employeeId = e.employeeId,
                    destination = gatePos,
                    purpose = "Locker→Home",
                    onArrive = onArriveHome
                }
            });
            return true;
        }

        static bool TryBegin(Employee e, EmployeeWalkSimulator sim, PersonalActivityKind kind, long now)
        {
            var occ = FurnitureOccupancyService.Instance;
            if (occ == null) return false;
            var visual = sim.GetVisual(e.employeeId);
            if (visual == null) return false;
            Vector3 from = visual.transform.position;

            ResolveTarget(kind, out ObjectFunction fn, out RoomType? roomFilter);
            int inst = occ.FindNearestFreeByFunction(fn, from, roomFilter);
            if (inst < 0 && roomFilter.HasValue) inst = occ.FindNearestFreeByFunction(fn, from, null); // fallback poza pokój
            if (inst < 0) return false;
            if (!occ.TryReserve(inst, e.employeeId)) return false;
            Vector3 seat = occ.GetSeatPoint(inst) ?? from;

            e.workflowState = EmployeeWorkflowState.GoingToPersonal;
            e.activeActivityKind = (int)kind;
            e.workflowTargetId = inst;
            visual.SetWorkingAnim(false);
            sim.EnqueueTask(new EmployeeWalkTask
            {
                employeeId = e.employeeId,
                destination = seat,
                targetFurnitureId = inst,
                purpose = $"Personal:{kind}",
                onArrive = () => OnArrive(e, sim, kind)
            });
            return true;
        }

        static void OnArrive(Employee e, EmployeeWalkSimulator sim, PersonalActivityKind kind)
        {
            // Anti-zombie: czynność mogła zostać anulowana w trakcie dojścia (koniec zmiany / zwolnienie
            // zmieniły stan z GoingToPersonal). Wtedy nie wchodź w doing-state — zwolnij mebel i wyjdź.
            if (e.workflowState != EmployeeWorkflowState.GoingToPersonal)
            {
                FurnitureOccupancyService.Instance?.Release(e.employeeId);
                return;
            }
            e.workflowState = DoingStateFor(kind);
            e.workflowStateFinishGameTime = NowAbs() + (long)DurationSecFor(kind);
            var v = sim.GetVisual(e.employeeId);
            if (v != null) v.SetWorkingAnim(true);
        }

        // ── Obsługa trwającej czynności (na górze workflow.Tick) ──────

        /// <summary>
        /// Obsługuje stany czynności osobistej (na górze workflow.Tick). Zwraca true gdy czynność trwa
        /// (workflow ma return). Po zakończeniu (timer) woła kontynuację: LockerIn (przyjście) →
        /// <paramref name="afterArrivalLocker"/>, Bathroom/Break → <paramref name="afterMidShift"/>.
        /// LockerOut NIE przechodzi tędy — wyjście trasowane chainem w <see cref="TryRouteDepartureViaLocker"/>.
        /// </summary>
        public static bool Tick(Employee e, EmployeeWalkSimulator sim, long now,
            System.Action afterArrivalLocker, System.Action afterMidShift)
        {
            switch (e.workflowState)
            {
                case EmployeeWorkflowState.GoingToPersonal:
                    return true; // idzie do mebla — czekaj na walk callback

                case EmployeeWorkflowState.ChangingClothes:
                case EmployeeWorkflowState.UsingBathroom:
                case EmployeeWorkflowState.OnBreak:
                    if (now >= e.workflowStateFinishGameTime)
                    {
                        bool wasArrivalLocker = e.activeActivityKind == (int)PersonalActivityKind.LockerIn;
                        Complete(e);
                        if (wasArrivalLocker) afterArrivalLocker?.Invoke();
                        else afterMidShift?.Invoke();
                    }
                    return true;

                default:
                    return false;
            }
        }

        static void Complete(Employee e)
        {
            FurnitureOccupancyService.Instance?.Release(e.employeeId);
            long now = NowAbs();
            switch ((PersonalActivityKind)e.activeActivityKind)
            {
                case PersonalActivityKind.LockerIn:  e.wearingWorkClothes = true;  SyncClothes(e, true);  break;
                case PersonalActivityKind.LockerOut: e.wearingWorkClothes = false; SyncClothes(e, false); break;
                case PersonalActivityKind.Bathroom:  e.lastBathroomGameTime = now; break;
                case PersonalActivityKind.Break:
                    e.lastBreakGameTime = now;
                    // Lekki addytywny efekt (#1): ulga zmęczenia. Bathroom/Locker bez efektu stat (diegetyczne).
                    e.currentFatigue = Mathf.Max(0, e.currentFatigue - PersonnelBalanceConstants.BreakFatigueReliefPts);
                    break;
            }
            e.activeActivityKind = -1;
            e.workflowTargetId = -1;
        }

        /// <summary>Awaryjne sprzątnięcie (despawn/fire/end-of-shift mid-activity) — zwolnij mebel + reset.</summary>
        public static void AbortAndRelease(Employee e)
        {
            FurnitureOccupancyService.Instance?.Release(e.employeeId);
            e.activeActivityKind = -1;
            e.workflowTargetId = -1;
        }

        // ── Helpers ───────────────────────────────────────────────────

        /// <summary>Synchronizuje tint kapsuły z aktualnym ubraniem (work=role color, private=stonowany).</summary>
        static void SyncClothes(Employee e, bool work)
        {
            var v = EmployeeWalkSimulator.Instance?.GetVisual(e.employeeId);
            if (v != null) v.SetWorkClothes(work);
        }

        /// <summary>Konsumuje okno czynności (brak mebla) — anty-spam prób co tick.</summary>
        static void MarkConsumed(Employee e, PersonalActivityKind kind, long now)
        {
            if (kind == PersonalActivityKind.Bathroom) e.lastBathroomGameTime = now;
            else if (kind == PersonalActivityKind.Break) e.lastBreakGameTime = now;
        }

        // ── Resolvery ─────────────────────────────────────────────────

        static void ResolveTarget(PersonalActivityKind kind, out ObjectFunction fn, out RoomType? roomFilter)
        {
            switch (kind)
            {
                case PersonalActivityKind.LockerIn:
                case PersonalActivityKind.LockerOut:
                    fn = ObjectFunction.StoragePersonal; roomFilter = null; break;       // szafka: Locker LUB Hala
                case PersonalActivityKind.Bathroom:
                    fn = ObjectFunction.Sanitary; roomFilter = RoomType.Bathroom; break;  // fallback poza pokój w TryBegin
                case PersonalActivityKind.Break:
                    fn = ObjectFunction.SeatingRest; roomFilter = RoomType.Social; break; // fallback poza pokój w TryBegin
                default:
                    fn = ObjectFunction.SeatingRest; roomFilter = null; break;
            }
        }

        static EmployeeWorkflowState DoingStateFor(PersonalActivityKind kind) => kind switch
        {
            PersonalActivityKind.LockerIn  => EmployeeWorkflowState.ChangingClothes,
            PersonalActivityKind.LockerOut => EmployeeWorkflowState.ChangingClothes,
            PersonalActivityKind.Bathroom  => EmployeeWorkflowState.UsingBathroom,
            PersonalActivityKind.Break     => EmployeeWorkflowState.OnBreak,
            _ => EmployeeWorkflowState.UsingBathroom
        };

        static float DurationSecFor(PersonalActivityKind kind) => kind switch
        {
            PersonalActivityKind.LockerIn  => PersonnelBalanceConstants.LockerChangeDurationSec,
            PersonalActivityKind.LockerOut => PersonnelBalanceConstants.LockerChangeDurationSec,
            PersonalActivityKind.Bathroom  => PersonnelBalanceConstants.BathroomDurationSec,
            PersonalActivityKind.Break     => PersonnelBalanceConstants.BreakDurationSec,
            _ => 30f
        };
    }
}
