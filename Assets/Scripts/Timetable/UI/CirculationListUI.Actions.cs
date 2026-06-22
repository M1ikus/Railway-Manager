using System.Collections.Generic;
using RailwayManager.Core;

namespace RailwayManager.Timetable
{
    public partial class CirculationListUI
    {
        private void OnNewEmptyClicked()
        {
            var newCirc = new Circulation
            {
                name = "Obieg",
                steps = new List<CirculationStep>(),
                calendar = DayMask.Daily(),
                status = CirculationStatus.Draft,
                weeksValid = 4,
                assignedVehicleIds = new List<int>()
            };
            var added = CirculationService.AddCirculation(newCirc);
            added.name = $"Obieg {added.id}";
            _expandedIds.Add(added.id);
            Refresh();
        }

        private void OnAutoGenClicked()
        {
            OpenAutoGenSettingsModal();
        }

        private void ToggleExpand(int circId)
        {
            if (_expandedIds.Contains(circId)) _expandedIds.Remove(circId);
            else _expandedIds.Add(circId);
            RefreshCirculations();
        }

        private void OnAssignVehicleClicked(int circId)
        {
            var c = CirculationService.GetCirculation(circId);
            if (c == null) return;
            if (VehicleAssignmentModal.Instance != null)
            {
                VehicleAssignmentModal.Instance.Open(c);
            }
            else
            {
                Log.Warn("[CirculationList] VehicleAssignmentModal not bootstrapped - fallback to legacy");
                OpenAssignModal(c);
            }
        }

        private void OnDuplicateClicked(int circId)
        {
            var src = CirculationService.GetCirculation(circId);
            if (src == null) return;
            var copy = new Circulation
            {
                name = src.name + " (kopia)",
                steps = new List<CirculationStep>(),
                assignedVehicleIds = new List<int>(),
                calendar = new DayMask { bits = src.calendar.bits },
                weeksValid = src.weeksValid,
                status = CirculationStatus.Draft,
                notes = src.notes
            };
            CirculationService.AddCirculation(copy);
            _expandedIds.Add(copy.id);
            Refresh();
        }

        private void OnDeleteClicked(int circId)
        {
            if (CirculationService.RemoveCirculation(circId))
            {
                _expandedIds.Remove(circId);
                Refresh();
            }
        }

        private void OnStatusChanged(int circId, int dropdownIdx)
        {
            var newStatus = dropdownIdx switch
            {
                0 => CirculationStatus.Draft,
                1 => CirculationStatus.Active,
                2 => CirculationStatus.Paused,
                3 => CirculationStatus.Archived,
                _ => CirculationStatus.Draft
            };
            var c = CirculationService.GetCirculation(circId);
            if (c == null) return;
            if (newStatus == CirculationStatus.Active && !c.HasVehicle)
            {
                Log.Warn($"[CirculationList] #{circId} nie moze byc Active - brak pojazdu");
                RefreshCirculations();
                return;
            }

            if (newStatus == CirculationStatus.Active)
            {
                var homeIssues = CirculationValidator.ValidateHomeStation(c.steps);
                foreach (var issue in homeIssues)
                {
                    if (issue.severity == CirculationValidator.IssueSeverity.Error)
                    {
                        Log.Warn($"[CirculationList] #{circId} nie moze byc Active - {issue.message}");
                        RefreshCirculations();
                        return;
                    }
                }
            }

            CirculationService.SetStatus(circId, newStatus);
            Refresh();
        }

        private void OnRemoveStepClicked(int circId, int stepIdx)
        {
            var c = CirculationService.GetCirculation(circId);
            if (c == null || stepIdx < 0 || stepIdx >= c.steps.Count) return;
            c.steps.RemoveAt(stepIdx);
            Refresh();
        }

        private void OnMoveStepUp(int circId, int stepIdx)
        {
            var c = CirculationService.GetCirculation(circId);
            if (c == null || stepIdx <= 0 || stepIdx >= c.steps.Count) return;
            var tmp = c.steps[stepIdx];
            c.steps[stepIdx] = c.steps[stepIdx - 1];
            c.steps[stepIdx - 1] = tmp;
            RefreshCirculations();
        }

        private void OnMoveStepDown(int circId, int stepIdx)
        {
            var c = CirculationService.GetCirculation(circId);
            if (c == null || stepIdx < 0 || stepIdx >= c.steps.Count - 1) return;
            var tmp = c.steps[stepIdx];
            c.steps[stepIdx] = c.steps[stepIdx + 1];
            c.steps[stepIdx + 1] = tmp;
            RefreshCirculations();
        }

        private void OnScheduleDroppedOnCirculation(int circId, int timetableId)
        {
            var c = CirculationService.GetCirculation(circId);
            if (c == null) return;
            var tt = TimetableService.GetTimetable(timetableId);
            if (tt == null) return;

            _expandedIds.Add(circId);

            var errors = CirculationValidator.GetNextStepErrors(c.steps, timetableId);
            CirculationValidator.SequenceIssue? firstError = null;
            foreach (var e in errors)
            {
                if (e.severity == CirculationValidator.IssueSeverity.Error)
                {
                    firstError = e;
                    break;
                }
            }

            if (firstError.HasValue && c.steps.Count > 0)
            {
                string msg = firstError.Value.message;
                bool isStationMismatch =
                    msg.StartsWith("Stacje", System.StringComparison.OrdinalIgnoreCase)
                    && msg.IndexOf("spinaj", System.StringComparison.OrdinalIgnoreCase) >= 0;
                if (isStationMismatch)
                {
                    var lastTt = TimetableService.GetTimetable(c.steps[c.steps.Count - 1].timetableId);
                    string fromStation = lastTt?.LastStop?.stationName ?? "?";
                    string toStation = tt.FirstStop?.stationName ?? "?";
                    OpenDeadheadModal(circId, c.steps.Count, fromStation, toStation, timetableId);
                    return;
                }

                FlashError($"Nie mozna dodac '{tt.name}' do obiegu #{circId}: {msg}");
                Log.Warn($"[CirculationList] Drop zablokowany: {msg}");
                RefreshWarnings();
                return;
            }

            c.steps.Add(new CirculationStep(timetableId, StepKind.Commercial));
            Log.Info($"[CirculationList] Dodano krok #{c.steps.Count} do obiegu #{circId}: rozklad #{timetableId}");

            bool isFirstStep = c.steps.Count == 1;

            int pruned = CirculationService.PruneIncompatibleVehicles(c);
            if (pruned > 0)
                Log.Info($"[CirculationList] Auto-prune: usunieto {pruned} niepasujacych przypisan po dodaniu rozkladu");

            Refresh();

            if (isFirstStep)
                OpenReturnTripModal(circId, timetableId);
        }
    }
}
