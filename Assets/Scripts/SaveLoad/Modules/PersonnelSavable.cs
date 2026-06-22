using Newtonsoft.Json.Linq;
using UnityEngine;
using System.Collections.Generic;
using RailwayManager.Core;
using RailwayManager.Personnel;

namespace RailwayManager.SaveLoad.Modules
{
    /// <summary>
    /// M13-7: Persystencja Personnel — pracownicy + harmonogramy + turnusy +
    /// rynek kandydatów + ogłoszenia + hotele + dispatch queue.
    ///
    /// Module ID: "personnel". Schema v1 (pre-EA reset 2026-05-15 — wcześniej v5
    /// z 4 identity migratorami; po EA każdy bump = real migrator).
    ///
    /// Backward compat dla starych save'ów: brak migrator'ów → SaveOrchestrator wykrywa
    /// sourceVersion > current=1, robi direct deserialize, każde nowe pole ma fallback
    /// `?? default` w Deserialize (qualifications/dispatchActive/researchPaths/etc).
    /// Historia (zachowana dla kontekstu):
    /// - v2 dodała "qualifications" (BUG-020)
    /// - v3 dodała "dispatchActive"/"dispatchPending"/"dispatchNextId" (BUG-022)
    /// - v4 dodała Employee.moraleBreakdown (BUG-060)
    /// - v5 dodała R&D + assignments + traffic priorities (BUG-086..088)
    ///
    /// Pola w bundle (zlepione w jednym module — Personnel ma wiele serwisów,
    /// ale wszystkie tworzą spójny stan personalny):
    /// - employees (JArray Employee) — pracownicy ze stanami (status/morale/
    ///   fatigue/skill/salary/shift)
    /// - schedules (JObject int→EmployeeSchedule) — indywidualne harmonogramy
    /// - qualifications (JObject int→EmployeeQualifications) — kwalifikacje (BUG-020, v2+)
    /// - crewCirculations (JArray CrewCirculation) — turnusy ze stepami i hotelami
    /// - candidates (JArray EmployeeCandidate) — aktualny rynek kandydatów
    /// - postings (JArray JobPosting) — aktywne ogłoszenia rekrutacyjne
    /// - hotelBookings (JArray HotelBooking) — wszystkie rezerwacje hotelowe
    /// - companyDefaultHotelTier (int enum) — Settings: domyślna klasa hotelu firmy
    /// - dispatchActive/dispatchPending (JArray DispatchActionEntry) — kolejka dyspozytorska (BUG-022, v3+)
    /// - dispatchNextId (int) — licznik dispatchId (BUG-022, v3+)
    /// </summary>
    public class PersonnelSavable : ISavable
    {
        public string ModuleId => "personnel";
        public int SchemaVersion => 1; // pre-EA reset 2026-05-15; bump po EA = real migrator

        public JObject Serialize()
        {
            // Schedules: Dictionary<int, EmployeeSchedule> → JObject z int-keyed string keys
            var schedulesJObj = new JObject();
            foreach (var kv in PersonnelService.Schedules)
                schedulesJObj[kv.Key.ToString()] = JObject.FromObject(kv.Value);

            // Qualifications: Dictionary<int, EmployeeQualifications> (BUG-020, v2+).
            // Save tylko dla aktywnych employees — zwolnieni nie potrzebują quals (BUG-026).
            var qualificationsJObj = new JObject();
            foreach (var kv in PersonnelService.Qualifications)
            {
                var emp = PersonnelService.GetById(kv.Key);
                if (emp == null || !emp.IsActive) continue;
                qualificationsJObj[kv.Key.ToString()] = JObject.FromObject(kv.Value);
            }

            var researchUnlocksJObj = new JObject();
            foreach (var kv in ResearchUnlocks.Global.AllEffects)
                researchUnlocksJObj[kv.Key] = kv.Value;

            var workshopAssignmentsJObj = new JObject();
            foreach (var kv in WorkshopAssignmentService.GetAssignmentsSnapshot())
                workshopAssignmentsJObj[kv.Key.ToString()] = JArray.FromObject(kv.Value);

            return new JObject
            {
                ["employees"]                = JArray.FromObject(PersonnelService.Employees),
                ["schedules"]                = schedulesJObj,
                ["qualifications"]           = qualificationsJObj,
                ["crewCirculations"]         = JArray.FromObject(CrewCirculationService.All),
                ["candidates"]               = JArray.FromObject(CandidateMarketService.Candidates),
                ["postings"]                 = JArray.FromObject(JobPostingService.ActivePostings),
                ["hotelBookings"]            = JArray.FromObject(HotelBookingService.AllBookings),
                ["companyDefaultHotelTier"]  = (int)HotelBookingService.CompanyDefaultHotelTier,
                // BUG-088: R&D + unlocks
                ["researchPaths"]            = JArray.FromObject(ResearchService.GetPathsSnapshot()),
                ["researchUnlocks"]          = researchUnlocksJObj,
                // BUG-088: role-specific assignments not represented by Employee alone
                ["ticketClerkAssignments"]   = JArray.FromObject(TicketClerkService.GetAssignmentsSnapshot()),
                ["workshopAssignments"]      = workshopAssignmentsJObj,
                // BUG-088: runtime toggles / UI priorities
                ["trafficPriorityWorkshopOverdue"]    = TrafficControlService.PriorityWorkshopOverdue,
                ["trafficPriorityScheduledDeparture"] = TrafficControlService.PriorityScheduledDeparture,
                ["trafficPriorityWashBayPlanned"]     = TrafficControlService.PriorityWashBayPlanned,
                ["trafficPriorityParkingReshuffle"]   = TrafficControlService.PriorityParkingReshuffle,
                ["autoCleaningEnabled"]      = CleaningService.AutoCleaningEnabled,
                ["autoWashingEnabled"]       = CleaningService.AutoWashingEnabled,
                ["payrollLastPaidYearMonth"] = PayrollService.LastPaidYearMonth,
                // BUG-022 (v3+): dispatch queue (active + pending + counter)
                ["dispatchActive"]           = JArray.FromObject(DispatchActionService.GetActiveSnapshot()),
                ["dispatchPending"]          = JArray.FromObject(DispatchActionService.GetPendingSnapshot()),
                ["dispatchNextId"]           = DispatchActionService.GetNextDispatchId(),
                // BUG-079/080/081: counter'y _nextId żeby nowo dodane entity nie kolizjowały po load
                ["nextEmployeeId"]           = PersonnelService.GetNextEmployeeId(),
                ["nextJobPostingId"]         = JobPostingService.GetNextPostingId(),
                ["nextCrewCirculationId"]    = CrewCirculationService.GetNextId(),
                ["nextCandidateId"]          = PersonnelMarketGenerator.GetNextCandidateId()
            };
        }

        public void Deserialize(JObject data, int sourceVersion)
        {
            // Employees
            PersonnelService.Employees.Clear();
            var emps = data["employees"] as JArray;
            int maxEmployeeId = 0;
            if (emps != null)
            {
                foreach (var item in emps)
                {
                    var e = item.ToObject<Employee>();
                    if (e != null)
                    {
                        PersonnelService.Employees.Add(e);
                        if (e.employeeId > maxEmployeeId) maxEmployeeId = e.employeeId;
                    }
                }
            }
            // BUG-079: restore _nextEmployeeId (fallback do max+1 dla starych saveów bez "nextEmployeeId").
            int nextEmployeeId = data.Value<int?>("nextEmployeeId") ?? (maxEmployeeId + 1);
            PersonnelService.RestoreNextEmployeeId(nextEmployeeId);

            // Rebuild O(1) lookup _byId po direct Add do Employees (save/load bypassuje Hire).
            PersonnelService.RebuildIndexes();

            // Schedules
            PersonnelService.Schedules.Clear();
            if (data["schedules"] is JObject schedJObj)
            {
                foreach (var kv in schedJObj)
                {
                    if (int.TryParse(kv.Key, out int empId))
                    {
                        var sched = kv.Value.ToObject<EmployeeSchedule>();
                        if (sched != null) PersonnelService.Schedules[empId] = sched;
                    }
                }
            }

            // Qualifications (BUG-020, v2+). Migrator v1→v2: brak pola = pusty dictionary
            // (default empty quals = "wszystkie permissive" w EA, gameplay omija check).
            PersonnelService.Qualifications.Clear();
            if (data["qualifications"] is JObject qualsJObj)
            {
                foreach (var kv in qualsJObj)
                {
                    if (int.TryParse(kv.Key, out int empId))
                    {
                        var quals = kv.Value.ToObject<EmployeeQualifications>();
                        if (quals != null) PersonnelService.Qualifications[empId] = quals;
                    }
                }
            }

            // Crew circulations (BUG-081: restore _nextId via RestoreFromSave)
            var crewList = new System.Collections.Generic.List<CrewCirculation>();
            int maxCrewId = 0;
            var crews = data["crewCirculations"] as JArray;
            if (crews != null)
            {
                foreach (var item in crews)
                {
                    var c = item.ToObject<CrewCirculation>();
                    if (c != null)
                    {
                        crewList.Add(c);
                        if (c.crewCirculationId > maxCrewId) maxCrewId = c.crewCirculationId;
                    }
                }
            }
            int nextCrewId = data.Value<int?>("nextCrewCirculationId") ?? (maxCrewId + 1);
            CrewCirculationService.RestoreFromSave(crewList, nextCrewId);

            // Candidates market
            CandidateMarketService.Candidates.Clear();
            int maxCandidateId = 0;
            var cands = data["candidates"] as JArray;
            if (cands != null)
            {
                foreach (var item in cands)
                {
                    var c = item.ToObject<EmployeeCandidate>();
                    if (c != null)
                    {
                        CandidateMarketService.Candidates.Add(c);
                        if (c.candidateId > maxCandidateId) maxCandidateId = c.candidateId;
                    }
                }
            }
            int nextCandidateId = data.Value<int?>("nextCandidateId") ?? (maxCandidateId + 1);
            PersonnelMarketGenerator.RestoreNextCandidateId(nextCandidateId);

            // Job postings (BUG-036 + BUG-080: bulk replace via RestoreFromSave z nextId)
            var postingsList = new System.Collections.Generic.List<JobPosting>();
            int maxPostingId = 0;
            if (data["postings"] is JArray posts)
            {
                foreach (var item in posts)
                {
                    var p = item.ToObject<JobPosting>();
                    if (p != null)
                    {
                        postingsList.Add(p);
                        if (p.jobPostingId > maxPostingId) maxPostingId = p.jobPostingId;
                    }
                }
            }
            int nextPostingId = data.Value<int?>("nextJobPostingId") ?? (maxPostingId + 1);
            JobPostingService.RestoreFromSave(postingsList, nextPostingId);

            // Hotel bookings
            HotelBookingService.AllBookings.Clear();
            var hotels = data["hotelBookings"] as JArray;
            if (hotels != null)
            {
                foreach (var item in hotels)
                {
                    var h = item.ToObject<HotelBooking>();
                    if (h != null) HotelBookingService.AllBookings.Add(h);
                }
            }

            // Company default hotel tier
            int tierInt = data.Value<int?>("companyDefaultHotelTier") ?? (int)HotelTier.Standard;
            HotelBookingService.CompanyDefaultHotelTier = (HotelTier)tierInt;

            // R&D state + unlocks (BUG-088, v5+)
            var researchPaths = new List<ResearchPath>();
            if (data["researchPaths"] is JArray researchArr)
            {
                foreach (var item in researchArr)
                {
                    var p = item.ToObject<ResearchPath>();
                    if (p != null) researchPaths.Add(p);
                }
            }
            ResearchService.EnsureExists();
            ResearchService.RestoreFromSave(researchPaths);

            var unlocks = new Dictionary<string, float>();
            if (data["researchUnlocks"] is JObject unlocksObj)
            {
                foreach (var kv in unlocksObj)
                    unlocks[kv.Key] = kv.Value.Value<float>();
            }
            ResearchUnlocks.Global.RestoreFromSave(unlocks);

            // Ticket clerk assignments (station → employee)
            var ticketAssignments = new List<StationAssignment>();
            if (data["ticketClerkAssignments"] is JArray ticketArr)
            {
                foreach (var item in ticketArr)
                {
                    var assignment = item.ToObject<StationAssignment>();
                    if (assignment != null) ticketAssignments.Add(assignment);
                }
            }
            TicketClerkService.EnsureExists();
            TicketClerkService.RestoreFromSave(ticketAssignments);

            // Workshop mechanic assignments (slot → mechanics)
            var workshopAssignments = new Dictionary<int, List<int>>();
            if (data["workshopAssignments"] is JObject workshopObj)
            {
                foreach (var kv in workshopObj)
                {
                    if (!int.TryParse(kv.Key, out int slotId)) continue;
                    var ids = kv.Value.ToObject<List<int>>();
                    if (ids != null) workshopAssignments[slotId] = ids;
                }
            }
            WorkshopAssignmentService.EnsureExists();
            WorkshopAssignmentService.RestoreFromSave(workshopAssignments);

            TrafficControlService.PriorityWorkshopOverdue =
                data.Value<int?>("trafficPriorityWorkshopOverdue") ?? PersonnelBalanceConstants.TrafficPriorityWorkshopOverdue;
            TrafficControlService.PriorityScheduledDeparture =
                data.Value<int?>("trafficPriorityScheduledDeparture") ?? PersonnelBalanceConstants.TrafficPriorityScheduledDeparture;
            TrafficControlService.PriorityWashBayPlanned =
                data.Value<int?>("trafficPriorityWashBayPlanned") ?? PersonnelBalanceConstants.TrafficPriorityWashBayPlanned;
            TrafficControlService.PriorityParkingReshuffle =
                data.Value<int?>("trafficPriorityParkingReshuffle") ?? PersonnelBalanceConstants.TrafficPriorityParkingReshuffle;

            CleaningService.AutoCleaningEnabled = data.Value<bool?>("autoCleaningEnabled") ?? true;
            CleaningService.AutoWashingEnabled = data.Value<bool?>("autoWashingEnabled") ?? true;

            PayrollService.RestoreLastPaidYearMonth(data.Value<string>("payrollLastPaidYearMonth") ?? "");

            // Dispatch queue (BUG-022, v3+). Migrator v2→v3: brak pól = pusta kolejka
            // (nowo wczytany save bez dispatch'ów = manualny dispatch wymagany dla queued tasks).
            var dispatchActive = new System.Collections.Generic.List<DispatchActionEntry>();
            var dispatchPending = new System.Collections.Generic.List<DispatchActionEntry>();
            int dispatchNextId = 1;
            if (data["dispatchActive"] is JArray actArr)
            {
                foreach (var item in actArr)
                {
                    var entry = item.ToObject<DispatchActionEntry>();
                    if (entry != null) dispatchActive.Add(entry);
                }
            }
            if (data["dispatchPending"] is JArray pendArr)
            {
                foreach (var item in pendArr)
                {
                    var entry = item.ToObject<DispatchActionEntry>();
                    if (entry != null) dispatchPending.Add(entry);
                }
            }
            dispatchNextId = data.Value<int?>("dispatchNextId") ?? 1;
            DispatchActionService.RestoreFromSave(dispatchActive, dispatchPending, dispatchNextId);

            Log.Info($"[PersonnelSavable] Restored: {PersonnelService.Employees.Count} employees, " +
                     $"{PersonnelService.Schedules.Count} schedules, " +
                     $"{PersonnelService.Qualifications.Count} qualifications, " +
                     $"{CrewCirculationService.All.Count} crew turnusy, " +
                     $"{CandidateMarketService.Candidates.Count} candidates, " +
                     $"{JobPostingService.ActivePostings.Count} postings, " +
                     $"{HotelBookingService.AllBookings.Count} hotels, " +
                     $"{ticketAssignments.Count} ticket assignments, " +
                     $"{workshopAssignments.Count} workshop slots, " +
                     $"{dispatchActive.Count} active dispatches + {dispatchPending.Count} pending");
        }

        public void InitializeDefault()
        {
            // #1B: izolacja resetow — kazdy niezalezny singleton resetowany w try/catch,
            // zeby wyjatek jednego nie pominal pozostalych (stale cross-session state
            // przeciekajacy do nowej gry: kolejka dyspozytorska BUG-022, priorytety
            // TrafficControl, turnusy zalogi...). Wiele z tych resetow odpala eventy/
            // logike i moze rzucic. Ten sam kontrakt graceful-degradation co per-module
            // isolation w SaveOrchestrator, tylko per-singleton w obrebie modulu.
            SafeReset("PersonnelService.ResetAll", PersonnelService.ResetAll);
            SafeReset("CrewCirculationService.ResetAll", CrewCirculationService.ResetAll);
            SafeReset("CandidateMarketService.ResetAll", CandidateMarketService.ResetAll);
            SafeReset("JobPostingService.ResetAll", JobPostingService.ResetAll);
            SafeReset("HotelBookingService.ResetAll", HotelBookingService.ResetAll);
            SafeReset("DispatchActionService.ResetAll", DispatchActionService.ResetAll); // BUG-022: clear dispatch queue
            SafeReset("ResearchService.ResetAll", ResearchService.ResetAll);
            SafeReset("ResearchUnlocks.Global.Reset", ResearchUnlocks.Global.Reset);
            SafeReset("TicketClerkService.ResetAll", TicketClerkService.ResetAll);
            SafeReset("WorkshopAssignmentService.ClearAll", WorkshopAssignmentService.ClearAll);
            SafeReset("TrafficControlService.ResetPrioritiesToDefault", TrafficControlService.ResetPrioritiesToDefault);
            SafeReset("PayrollService.Reset", PayrollService.Reset);

            // Trywialne property-set (czyste statyczne assignmenty — nie rzucaja) na koncu
            // bez wrappera: owijanie w try/catch byloby cargo-cult, a flow zawsze tu dochodzi
            // bo metody-resety wyzej sa izolowane.
            CleaningService.AutoCleaningEnabled = true;
            CleaningService.AutoWashingEnabled = true;
            HotelBookingService.CompanyDefaultHotelTier = HotelTier.Standard;
        }

        /// <summary>
        /// #1B: pojedynczy reset singletona z izolacja wyjatku, zeby nie przerwac
        /// resetu pozostalych singletonow w <see cref="InitializeDefault"/>.
        /// </summary>
        private static void SafeReset(string what, System.Action reset)
        {
            try { reset(); }
            catch (System.Exception e)
            {
                Log.Error($"[PersonnelSavable] Reset '{what}' threw: {e.GetType().Name}: {e.Message}");
            }
        }
    }

    public static class PersonnelSavableBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            SaveRegistry.Register(new PersonnelSavable());
        }
    }
}
