using UnityEngine;
using UnityEngine.UI;
using RailwayManager.SharedUI;
using RailwayManager.SharedUI.Localization;

namespace RailwayManager.Timetable
{
    public partial class CirculationListUI
    {
        private void OpenDeadheadModal(int circId, int insertIdx, string from, string to, int originalNextId)
        {
            if (_deadheadModal == null) BuildDeadheadModal();
            _deadheadCircId = circId;
            _deadheadInsertIdx = insertIdx;
            _deadheadFrom = from;
            _deadheadTo = to;
            _deadheadOriginalNext = originalNextId;
            _deadheadModalText.text = string.Format(LocalizationService.Get("timetable.circulations.modal.deadhead.body_format"), from, to);
            _deadheadModal.SetActive(true);
        }

        private void CloseDeadheadModal()
        {
            if (_deadheadModal != null) _deadheadModal.SetActive(false);
            _deadheadCircId = -1;
        }

        private void OnDeadheadYesClicked()
        {
            int circId = _deadheadCircId;
            int insertIdx = _deadheadInsertIdx;
            string from = _deadheadFrom;
            string to = _deadheadTo;
            int origNext = _deadheadOriginalNext;
            CloseDeadheadModal();

            int startMin = 6 * 60;
            var c = CirculationService.GetCirculation(circId);
            if (c != null && insertIdx > 0 && insertIdx <= c.steps.Count)
            {
                var prevTt = TimetableService.GetTimetable(c.steps[insertIdx - 1].timetableId);
                if (prevTt != null)
                {
                    startMin = prevTt.EndMinutes + CirculationValidator.ReverseMarginEmuMinutes;
                    if (startMin >= 24 * 60) startMin -= 24 * 60;
                }
            }

            if (TimetableCreatorUI.Instance == null) return;

            TimetableCreatorUI.Instance.OpenWithPreset(new TimetableCreatorUI.CreatorPreset
            {
                startStationName = from,
                endStationName = to,
                commercialCategoryId = "sluzbowy",
                compositionMode = CompositionMode.MultipleUnit,
                startMinutesFromMidnight = startMin,
                presetReason = $"Deadhead dla obiegu #{circId}",
                onConfirmed = (newTimetable) =>
                {
                    if (newTimetable == null) return;
                    var circ = CirculationService.GetCirculation(circId);
                    if (circ == null) return;
                    circ.steps.Add(new CirculationStep(newTimetable.id, StepKind.Deadhead));
                    if (origNext > 0)
                        circ.steps.Add(new CirculationStep(origNext, StepKind.Commercial));
                    Show();
                    Refresh();
                }
            });
        }

        private void OpenReturnTripModal(int circId, int baseTimetableId)
        {
            if (_returnTripModal == null) BuildReturnTripModal();
            _returnTripCircId = circId;
            _returnTripBaseTimetableId = baseTimetableId;
            var tt = TimetableService.GetTimetable(baseTimetableId);
            if (tt == null) { CloseReturnTripModal(); return; }

            string unknown = LocalizationService.Get("timetable.circulations.format.unknown");
            string s = tt.FirstStop?.stationName ?? unknown;
            string e = tt.LastStop?.stationName ?? unknown;
            _returnTripModalText.text = string.Format(LocalizationService.Get("timetable.circulations.modal.return_trip.body_format"), s, e);
            _returnTripModal.SetActive(true);
        }

        private void CloseReturnTripModal()
        {
            if (_returnTripModal != null) _returnTripModal.SetActive(false);
            _returnTripCircId = -1;
            _returnTripBaseTimetableId = -1;
        }

        private void OnReturnTripYesClicked()
        {
            int circId = _returnTripCircId;
            int baseId = _returnTripBaseTimetableId;
            var baseTt = TimetableService.GetTimetable(baseId);
            CloseReturnTripModal();
            if (baseTt == null || TimetableCreatorUI.Instance == null) return;

            string from = baseTt.LastStop?.stationName;
            string to = baseTt.FirstStop?.stationName;
            if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to)) return;

            int startMin = baseTt.EndMinutes + CirculationValidator.ReverseMarginEmuMinutes;
            if (startMin >= 24 * 60) startMin -= 24 * 60;

            TimetableCreatorUI.Instance.OpenWithPreset(new TimetableCreatorUI.CreatorPreset
            {
                startStationName = from,
                endStationName = to,
                commercialCategoryId = baseTt.commercialCategoryId,
                compositionMode = baseTt.composition?.mode ?? CompositionMode.MultipleUnit,
                maxSpeedKmh = baseTt.composition?.maxSpeedKmh,
                startMinutesFromMidnight = startMin,
                presetReason = $"Rozklad powrotny dla obiegu #{circId}",
                onConfirmed = (newTimetable) =>
                {
                    if (newTimetable == null) return;
                    var circ = CirculationService.GetCirculation(circId);
                    if (circ == null) return;
                    circ.steps.Add(new CirculationStep(newTimetable.id, StepKind.Commercial));
                    Show();
                    Refresh();
                }
            });
        }

        private void BuildDeadheadModal()
        {
            var canvas = _panel.transform.parent;
            _deadheadModal = new GameObject("DeadheadModal", typeof(RectTransform));
            _deadheadModal.transform.SetParent(canvas, false);

            var mrt = (RectTransform)_deadheadModal.transform;
            mrt.anchorMin = new Vector2(0.3f, 0.35f);
            mrt.anchorMax = new Vector2(0.7f, 0.65f);
            mrt.offsetMin = Vector2.zero;
            mrt.offsetMax = Vector2.zero;

            UITheme.ApplySurface(
                _deadheadModal.AddComponent<Image>(),
                UITheme.WithAlpha(UITheme.OverlayPanelStrong, 0.98f),
                UIShapePreset.PanelLarge);

            var vlg = _deadheadModal.AddComponent<VerticalLayoutGroup>();
            vlg.padding = UITheme.Padding(UITheme.Spacing.Xl, UITheme.Spacing.Lg);
            vlg.spacing = UITheme.Spacing.Md;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var titleRow = MakeHRow(_deadheadModal.transform, 30);
            UITheme.ApplySurface(titleRow.AddComponent<Image>(), UITheme.WithAlpha(UITheme.SecondarySurface, 0.34f), UIShapePreset.Panel);
            MakeText(titleRow.transform, LocalizationService.Get("timetable.circulations.modal.deadhead.title"), 14, Color.white);
            _deadheadModalText = MakeText(_deadheadModal.transform, "", 11, new Color(0.8f, 0.8f, 0.8f));
            _deadheadModalText.gameObject.GetComponent<LayoutElement>().preferredHeight = 90;
            UITheme.ApplySurface(_deadheadModalText.gameObject.AddComponent<Image>(), UITheme.WithAlpha(UITheme.TopBarInset, 0.92f), UIShapePreset.Inset);

            var btnRow = MakeHRow(_deadheadModal.transform, 32);
            UITheme.ApplySurface(btnRow.AddComponent<Image>(), UITheme.WithAlpha(UITheme.TopBarInset, 0.92f), UIShapePreset.Inset);
            MakeBtn(btnRow.transform, LocalizationService.Get("timetable.circulations.modal.common.cancel"), CloseDeadheadModal, new Color(0.3f, 0.3f, 0.4f), 180);
            MakeBtn(btnRow.transform, LocalizationService.Get("timetable.circulations.modal.deadhead.yes_btn"), OnDeadheadYesClicked, new Color(0.2f, 0.7f, 0.3f), 180);

            _deadheadModal.SetActive(false);
        }

        private void BuildReturnTripModal()
        {
            var canvas = _panel.transform.parent;
            _returnTripModal = new GameObject("ReturnTripModal", typeof(RectTransform));
            _returnTripModal.transform.SetParent(canvas, false);

            var mrt = (RectTransform)_returnTripModal.transform;
            mrt.anchorMin = new Vector2(0.3f, 0.35f);
            mrt.anchorMax = new Vector2(0.7f, 0.65f);
            mrt.offsetMin = Vector2.zero;
            mrt.offsetMax = Vector2.zero;

            UITheme.ApplySurface(
                _returnTripModal.AddComponent<Image>(),
                UITheme.WithAlpha(UITheme.OverlayPanelStrong, 0.98f),
                UIShapePreset.PanelLarge);

            var vlg = _returnTripModal.AddComponent<VerticalLayoutGroup>();
            vlg.padding = UITheme.Padding(UITheme.Spacing.Xl, UITheme.Spacing.Lg);
            vlg.spacing = UITheme.Spacing.Md;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var titleRow = MakeHRow(_returnTripModal.transform, 30);
            UITheme.ApplySurface(titleRow.AddComponent<Image>(), UITheme.WithAlpha(UITheme.SecondarySurface, 0.34f), UIShapePreset.Panel);
            MakeText(titleRow.transform, LocalizationService.Get("timetable.circulations.modal.return_trip.title"), 14, Color.white);
            _returnTripModalText = MakeText(_returnTripModal.transform, "", 11, new Color(0.8f, 0.8f, 0.8f));
            _returnTripModalText.gameObject.GetComponent<LayoutElement>().preferredHeight = 80;
            UITheme.ApplySurface(_returnTripModalText.gameObject.AddComponent<Image>(), UITheme.WithAlpha(UITheme.TopBarInset, 0.92f), UIShapePreset.Inset);

            var btnRow = MakeHRow(_returnTripModal.transform, 32);
            UITheme.ApplySurface(btnRow.AddComponent<Image>(), UITheme.WithAlpha(UITheme.TopBarInset, 0.92f), UIShapePreset.Inset);
            MakeBtn(btnRow.transform, LocalizationService.Get("timetable.circulations.modal.return_trip.no_btn"), CloseReturnTripModal, new Color(0.3f, 0.3f, 0.4f), 180);
            MakeBtn(btnRow.transform, LocalizationService.Get("timetable.circulations.modal.return_trip.yes_btn"), OnReturnTripYesClicked, new Color(0.2f, 0.7f, 0.3f), 180);

            _returnTripModal.SetActive(false);
        }
    }
}
