using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using RailwayManager.SharedUI;
using RailwayManager.SharedUI.Localization;

namespace RailwayManager.Timetable
{
    public partial class CirculationListUI
    {
        private void RefreshCirculations()
        {
            if (_circulationsContent == null) return;
            foreach (Transform ch in _circulationsContent) Destroy(ch.gameObject);

            if (CirculationService.Circulations.Count == 0)
            {
                var empty = new GameObject("Empty");
                empty.transform.SetParent(_circulationsContent, false);
                empty.AddComponent<LayoutElement>().preferredHeight = 52;
                UITheme.ApplySurface(
                    empty.AddComponent<Image>(),
                    UITheme.WithAlpha(UITheme.RaisedSurface, 0.72f),
                    UIShapePreset.Inset);
                var tObj = new GameObject("T", typeof(RectTransform));
                tObj.transform.SetParent(empty.transform, false);
                var trt = (RectTransform)tObj.transform;
                trt.anchorMin = Vector2.zero;
                trt.anchorMax = Vector2.one;
                trt.offsetMin = new Vector2(10, 6);
                trt.offsetMax = new Vector2(-10, -6);
                var t = tObj.AddComponent<TextMeshProUGUI>();
                t.fontSize = 12;
                t.alignment = TextAlignmentOptions.Center;
                t.text = "Brak obieg\u00F3w. [+ Nowy pusty] lub [Wygeneruj auto].";
                UITheme.ApplyTmpText(t, UIThemeTextRole.Secondary);
                return;
            }

            foreach (var c in CirculationService.Circulations)
            {
                if (c == null) continue;
                BuildCirculationRow(_circulationsContent, c);
            }
        }

        private void RefreshSchedulesPool()
        {
            if (_schedulesPoolContent == null) return;
            foreach (Transform ch in _schedulesPoolContent) Destroy(ch.gameObject);

            var usedIds = new HashSet<int>();
            foreach (var c in CirculationService.Circulations)
            {
                if (c?.steps == null) continue;
                foreach (var s in c.steps)
                    usedIds.Add(s.timetableId);
            }

            int count = 0;
            foreach (var tt in TimetableService.Timetables)
            {
                if (tt == null) continue;
                if (tt.status != TimetableStatus.Active) continue;
                if (usedIds.Contains(tt.id)) continue;
                BuildScheduleTile(_schedulesPoolContent, tt);
                count++;
            }

            if (count == 0)
            {
                var empty = new GameObject("Empty");
                empty.transform.SetParent(_schedulesPoolContent, false);
                empty.AddComponent<LayoutElement>().preferredHeight = 48;
                UITheme.ApplySurface(
                    empty.AddComponent<Image>(),
                    UITheme.WithAlpha(UITheme.RaisedSurface, 0.72f),
                    UIShapePreset.Inset);
                var tObj = new GameObject("T", typeof(RectTransform));
                tObj.transform.SetParent(empty.transform, false);
                var trt = (RectTransform)tObj.transform;
                trt.anchorMin = Vector2.zero;
                trt.anchorMax = Vector2.one;
                trt.offsetMin = new Vector2(10, 6);
                trt.offsetMax = new Vector2(-10, -6);
                var t = tObj.AddComponent<TextMeshProUGUI>();
                t.fontSize = 11;
                t.alignment = TextAlignmentOptions.Center;
                t.text = LocalizationService.Get("timetable.circulations.pool.empty");
                UITheme.ApplyTmpText(t, UIThemeTextRole.Secondary);
            }
        }

        private void RefreshWarnings()
        {
            if (_warningsContent == null) return;
            foreach (Transform ch in _warningsContent) Destroy(ch.gameObject);

            if (_flashMessage != null)
            {
                AddWarning(
                    string.Format(
                        LocalizationService.Get("timetable.circulations.warning.flash_prefix_format"),
                        _flashMessage),
                    new Color(1f, 0.45f, 0.35f));
            }

            int count = 0;
            foreach (var c in CirculationService.Circulations)
            {
                if (c == null) continue;
                if (c.status != CirculationStatus.Draft) continue;
                if (count >= 10) break;
                AddWarning(
                    string.Format(
                        LocalizationService.Get("timetable.circulations.warning.draft_no_vehicle_format"),
                        c.name),
                    new Color(1f, 0.65f, 0.3f));
                count++;
            }

            if (count == 0 && _flashMessage == null)
            {
                AddWarning(
                    LocalizationService.Get("timetable.circulations.warning.all_vehicles_assigned"),
                    new Color(0.4f, 0.85f, 0.4f));
            }
        }

        private void FlashError(string message)
        {
            _flashMessage = message;
            _flashExpireTime = Time.unscaledTime + 5f;
        }

        private void AddWarning(string text, Color color)
        {
            var o = new GameObject("Warn");
            o.transform.SetParent(_warningsContent, false);
            o.AddComponent<LayoutElement>().preferredHeight = 24;
            UITheme.ApplySurface(
                o.AddComponent<Image>(),
                UITheme.WithAlpha(color, 0.16f),
                UIShapePreset.Inset);
            var hlg = o.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = UITheme.Padding(UITheme.Spacing.Sm, UITheme.Spacing.Xxs);
            hlg.spacing = UITheme.Spacing.Sm;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = true;
            hlg.childAlignment = TextAnchor.MiddleLeft;

            var txObj = new GameObject("T", typeof(RectTransform));
            txObj.transform.SetParent(o.transform, false);
            txObj.AddComponent<LayoutElement>().flexibleWidth = 1;
            var tx = txObj.AddComponent<TextMeshProUGUI>();
            tx.fontSize = 10;
            tx.text = text;
            tx.alignment = TextAlignmentOptions.MidlineLeft;
            UITheme.ApplyTmpText(
                tx,
                color.g > 0.8f ? UIThemeTextRole.Success : color.r > 0.95f ? UIThemeTextRole.Danger : UIThemeTextRole.Warning);
            tx.color = color;
        }
    }
}
