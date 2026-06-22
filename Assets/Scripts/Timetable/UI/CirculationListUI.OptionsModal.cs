using UnityEngine;
using UnityEngine.UI;
using RailwayManager.Core;
using RailwayManager.SharedUI;
using RailwayManager.SharedUI.Localization;

namespace RailwayManager.Timetable
{
    public partial class CirculationListUI
    {
        private void OnOptionsClicked(int circId)
        {
            var c = CirculationService.GetCirculation(circId);
            if (c == null) return;

            _optionsCircId = circId;
            if (_optionsModal == null) BuildOptionsModal();

            if (_optNameInput != null) _optNameInput.text = c.name ?? "";
            if (_optIsOneTimeToggle != null) _optIsOneTimeToggle.isOn = c.isOneTime;
            for (int i = 0; i < 7; i++)
                if (_optDayToggles[i] != null) _optDayToggles[i].isOn = c.calendar.Runs(i);
            if (_optWeeksInput != null) _optWeeksInput.text = c.weeksValid.ToString();
            if (_optOneTimeDateInput != null)
                _optOneTimeDateInput.text = string.IsNullOrEmpty(c.oneTimeDateIso)
                    ? System.DateTime.Today.ToString("yyyy-MM-dd")
                    : c.oneTimeDateIso;
            if (_optNotesInput != null) _optNotesInput.text = c.notes ?? "";

            UpdateOptionsModalVisibility();
            _optionsModal.SetActive(true);
        }

        private void CloseOptionsModal()
        {
            if (_optionsModal != null) _optionsModal.SetActive(false);
            _optionsCircId = -1;
        }

        private void UpdateOptionsModalVisibility()
        {
            if (_optIsOneTimeToggle == null) return;

            bool oneTime = _optIsOneTimeToggle.isOn;
            if (_optRecurringLabel != null) _optRecurringLabel.gameObject.SetActive(!oneTime);
            if (_optOneTimeLabel != null) _optOneTimeLabel.gameObject.SetActive(oneTime);
            foreach (var t in _optDayToggles)
                if (t != null) t.transform.parent.gameObject.SetActive(!oneTime);
            if (_optWeeksInput != null) _optWeeksInput.transform.parent.gameObject.SetActive(!oneTime);
            if (_optOneTimeDateInput != null) _optOneTimeDateInput.transform.parent.gameObject.SetActive(oneTime);
        }

        private void OnOptionsSaveClicked()
        {
            var c = CirculationService.GetCirculation(_optionsCircId);
            if (c == null)
            {
                CloseOptionsModal();
                return;
            }

            string name = _optNameInput?.text?.Trim();
            if (string.IsNullOrEmpty(name)) name = $"Obieg {c.id}";
            c.name = name;

            c.isOneTime = _optIsOneTimeToggle != null && _optIsOneTimeToggle.isOn;
            if (c.isOneTime)
            {
                string dateStr = _optOneTimeDateInput?.text?.Trim();
                if (!string.IsNullOrEmpty(dateStr) && IsoTime.TryParseDate(dateStr, out var parsed))
                {
                    c.oneTimeDateIso = parsed.ToString("yyyy-MM-dd");
                }
                else
                {
                    c.oneTimeDateIso = System.DateTime.Today.ToString("yyyy-MM-dd");
                    Log.Warn($"[CirculationList] Options: nieparsowalna data '{dateStr}', ustawiono dzis");
                }
            }
            else
            {
                var mask = new DayMask();
                for (int i = 0; i < 7; i++)
                    if (_optDayToggles[i] != null && _optDayToggles[i].isOn)
                        mask.Set(i, true);
                if (mask.Count() == 0) mask = DayMask.Daily();
                c.calendar = mask;

                int weeks = 4;
                if (_optWeeksInput != null) int.TryParse(_optWeeksInput.text, out weeks);
                if (weeks < 0) weeks = 0;
                c.weeksValid = weeks;
            }

            c.notes = _optNotesInput?.text;

            Log.Info($"[CirculationList] Zapisano opcje obiegu #{c.id} '{c.name}' "
                     + (c.isOneTime ? $"(jednorazowy {c.oneTimeDateIso})" : $"(kalendarz {FormatDayMask(c.calendar)}, {c.weeksValid} tyg.)"));
            CloseOptionsModal();
            Refresh();
        }

        private void SetOptDayMask(byte bits)
        {
            for (int i = 0; i < 7; i++)
                if (_optDayToggles[i] != null)
                    _optDayToggles[i].isOn = (bits & (1 << i)) != 0;
        }

        private void BuildOptionsModal()
        {
            var canvas = _panel.transform.parent;
            _optionsModal = new GameObject("OptionsModal", typeof(RectTransform));
            _optionsModal.transform.SetParent(canvas, false);

            var mrt = (RectTransform)_optionsModal.transform;
            mrt.anchorMin = new Vector2(0.25f, 0.15f);
            mrt.anchorMax = new Vector2(0.75f, 0.85f);
            mrt.offsetMin = Vector2.zero;
            mrt.offsetMax = Vector2.zero;

            UITheme.ApplySurface(
                _optionsModal.AddComponent<Image>(),
                UITheme.WithAlpha(UITheme.OverlayPanelStrong, 0.98f),
                UIShapePreset.PanelLarge);

            var vlg = _optionsModal.AddComponent<VerticalLayoutGroup>();
            vlg.padding = UITheme.Padding(UITheme.Spacing.Xl, UITheme.Spacing.Lg);
            vlg.spacing = UITheme.Spacing.Sm;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var titleCard = MakeHRow(_optionsModal.transform, 30);
            UITheme.ApplySurface(titleCard.AddComponent<Image>(), UITheme.WithAlpha(UITheme.SecondarySurface, 0.34f), UIShapePreset.Panel);
            MakeText(titleCard.transform, LocalizationService.Get("timetable.circulations.modal.options.title"), 14, Color.white);

            var nameRow = MakeHRow(_optionsModal.transform, 26);
            UITheme.ApplySurface(nameRow.AddComponent<Image>(), UITheme.WithAlpha(UITheme.TopBarInset, 0.92f), UIShapePreset.Inset);
            MakeText(nameRow.transform, LocalizationService.Get("timetable.circulations.modal.options.name_label"), 11, UITheme.SecondaryText, preferredWidth: 70);
            _optNameInput = MakeInputField(nameRow.transform, "", 400);

            var typeRow = MakeHRow(_optionsModal.transform, 26);
            UITheme.ApplySurface(typeRow.AddComponent<Image>(), UITheme.WithAlpha(UITheme.TopBarInset, 0.92f), UIShapePreset.Inset);
            MakeText(typeRow.transform, LocalizationService.Get("timetable.circulations.modal.options.type_label"), 11, UITheme.SecondaryText, preferredWidth: 70);
            _optIsOneTimeToggle = MakeSimpleToggle(typeRow.transform, LocalizationService.Get("timetable.circulations.modal.options.type_one_time_toggle"), false);
            _optIsOneTimeToggle.onValueChanged.AddListener(_ => UpdateOptionsModalVisibility());

            _optRecurringLabel = MakeText(_optionsModal.transform, LocalizationService.Get("timetable.circulations.modal.options.recurring_section"), 11, UITheme.PrimaryAccent);

            var daysRow = MakeHRow(_optionsModal.transform, 26);
            MakeText(daysRow.transform, LocalizationService.Get("timetable.circulations.modal.options.days_label"), 11, UITheme.SecondaryText, preferredWidth: 70);
            string[] dayKeys = {
                "timetable.creator.day_short.mon",
                "timetable.creator.day_short.tue",
                "timetable.creator.day_short.wed",
                "timetable.creator.day_short.thu",
                "timetable.creator.day_short.fri",
                "timetable.creator.day_short.sat",
                "timetable.creator.day_short.sun"
            };
            for (int i = 0; i < 7; i++)
                _optDayToggles[i] = MakeDayToggle(daysRow.transform, LocalizationService.Get(dayKeys[i]), true);
            MakeBtn(daysRow.transform, LocalizationService.Get("timetable.circulations.modal.options.preset_workdays"), () => SetOptDayMask(DayMask.Weekdays), new Color(0.25f, 0.4f, 0.55f), 50);
            MakeBtn(daysRow.transform, LocalizationService.Get("timetable.circulations.modal.options.preset_weekend"), () => SetOptDayMask(DayMask.Weekend), new Color(0.25f, 0.4f, 0.55f), 50);
            MakeBtn(daysRow.transform, LocalizationService.Get("timetable.circulations.modal.options.preset_everyday"), () => SetOptDayMask(DayMask.EveryDay), new Color(0.25f, 0.4f, 0.55f), 50);
            UITheme.ApplySurface(daysRow.AddComponent<Image>(), UITheme.WithAlpha(UITheme.TopBarInset, 0.92f), UIShapePreset.Inset);

            var weeksRow = MakeHRow(_optionsModal.transform, 26);
            UITheme.ApplySurface(weeksRow.AddComponent<Image>(), UITheme.WithAlpha(UITheme.TopBarInset, 0.92f), UIShapePreset.Inset);
            MakeText(weeksRow.transform, LocalizationService.Get("timetable.circulations.modal.options.valid_label"), 11, UITheme.SecondaryText, preferredWidth: 100);
            _optWeeksInput = MakeInputField(weeksRow.transform, "4", 50);
            MakeText(weeksRow.transform, LocalizationService.Get("timetable.circulations.modal.options.valid_hint"), 10, UITheme.DisabledText, preferredWidth: 200);

            _optOneTimeLabel = MakeText(_optionsModal.transform, LocalizationService.Get("timetable.circulations.modal.options.one_time_section"), 11, UITheme.PrimaryAccent);

            var dateRow = MakeHRow(_optionsModal.transform, 26);
            UITheme.ApplySurface(dateRow.AddComponent<Image>(), UITheme.WithAlpha(UITheme.TopBarInset, 0.92f), UIShapePreset.Inset);
            MakeText(dateRow.transform, LocalizationService.Get("timetable.circulations.modal.options.date_label"), 11, UITheme.SecondaryText, preferredWidth: 70);
            _optOneTimeDateInput = MakeInputField(dateRow.transform, System.DateTime.Today.ToString("yyyy-MM-dd"), 140, LocalizationService.Get("timetable.circulations.modal.options.date_placeholder"));

            var notesRow = MakeHRow(_optionsModal.transform, 26);
            UITheme.ApplySurface(notesRow.AddComponent<Image>(), UITheme.WithAlpha(UITheme.TopBarInset, 0.92f), UIShapePreset.Inset);
            MakeText(notesRow.transform, LocalizationService.Get("timetable.circulations.modal.options.notes_label"), 11, UITheme.SecondaryText, preferredWidth: 70);
            _optNotesInput = MakeInputField(notesRow.transform, "", 400, LocalizationService.Get("timetable.circulations.modal.options.notes_placeholder"));

            var btnRow = MakeHRow(_optionsModal.transform, 30);
            UITheme.ApplySurface(btnRow.AddComponent<Image>(), UITheme.WithAlpha(UITheme.TopBarInset, 0.92f), UIShapePreset.Inset);
            MakeBtn(btnRow.transform, LocalizationService.Get("timetable.circulations.modal.common.cancel"), CloseOptionsModal, new Color(0.3f, 0.3f, 0.4f), 120);
            MakeBtn(btnRow.transform, LocalizationService.Get("timetable.circulations.modal.options.save_btn"), OnOptionsSaveClicked, new Color(0.2f, 0.7f, 0.3f), 120);

            _optionsModal.SetActive(false);
        }
    }
}
