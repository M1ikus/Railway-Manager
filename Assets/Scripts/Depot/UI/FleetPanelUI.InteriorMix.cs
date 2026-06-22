using System.Collections.Generic;
using RailwayManager.Fleet;
using RailwayManager.SharedUI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DepotSystem
{
    /// <summary>
    /// M-FC-5: Współdzielony UI miksu stref siedzeń (interiorMix). Używany w wagon
    /// configurator (M-FC-2) i family configurator (M-FC-3) dla EZT/SZT.
    /// UX pre-EA: lista wierszy (slider długości + cycle type button + ↑/↓/✕);
    /// post-EA: drag dividers wzdłuż schemat'a pudła (M12 polish).
    /// </summary>
    public partial class FleetPanelUI
    {
        private static readonly SeatZoneType[] SeatZoneCycleOrder = new[]
        {
            SeatZoneType.SecondClassOpen,
            SeatZoneType.SecondClassCompartment,
            SeatZoneType.FirstClassOpen,
            SeatZoneType.FirstClassCompartment,
            SeatZoneType.Sleeping,
            SeatZoneType.Reclining,
            SeatZoneType.Family,
            SeatZoneType.WheelchairAccessible,
            SeatZoneType.ManagerCompartment,
            SeatZoneType.Bicycle,
            SeatZoneType.SmallCatering,
            SeatZoneType.LargeCatering
        };

        private static string GetSeatZoneLabel(SeatZoneType type) => type switch
        {
            SeatZoneType.SecondClassOpen        => "2 klasa otwarta",
            SeatZoneType.SecondClassCompartment => "2 klasa przedziałowa",
            SeatZoneType.FirstClassOpen         => "1 klasa otwarta",
            SeatZoneType.FirstClassCompartment  => "1 klasa przedziałowa",
            SeatZoneType.Bicycle                => "Rowerowa",
            SeatZoneType.SmallCatering          => "Bar (mała gastronomia)",
            SeatZoneType.LargeCatering          => "Wagon restauracyjny",
            SeatZoneType.Sleeping               => "Sypialna",
            SeatZoneType.Reclining              => "Kuszetka",
            SeatZoneType.Family                 => "Rodzinna",
            SeatZoneType.WheelchairAccessible   => "Dla niepełnosprawnych",
            SeatZoneType.ManagerCompartment     => "Przedział kierownika",
            _ => type.ToString()
        };

        private static SeatZoneType CycleNextZoneType(SeatZoneType current)
        {
            for (int i = 0; i < SeatZoneCycleOrder.Length; i++)
            {
                if (SeatZoneCycleOrder[i] == current)
                    return SeatZoneCycleOrder[(i + 1) % SeatZoneCycleOrder.Length];
            }
            return SeatZoneCycleOrder[0];
        }

        private static List<SeatZoneSlot> CloneSeatZoneSlots(List<SeatZoneSlot> source)
        {
            var copy = new List<SeatZoneSlot>(source.Count);
            foreach (var s in source)
                copy.Add(new SeatZoneSlot { startPercent = s.startPercent, endPercent = s.endPercent, type = s.type });
            return copy;
        }

        private struct PresetDef
        {
            public string name;
            public List<SeatZoneSlot> slots;
        }

        private static readonly PresetDef[] InteriorPresets = new[]
        {
            new PresetDef {
                name = "2kl otwarta 100%",
                slots = new List<SeatZoneSlot> {
                    new SeatZoneSlot { startPercent = 0, endPercent = 100, type = SeatZoneType.SecondClassOpen }
                }
            },
            new PresetDef {
                name = "2kl przedziałowa 100%",
                slots = new List<SeatZoneSlot> {
                    new SeatZoneSlot { startPercent = 0, endPercent = 100, type = SeatZoneType.SecondClassCompartment }
                }
            },
            new PresetDef {
                name = "1kl otwarta 100%",
                slots = new List<SeatZoneSlot> {
                    new SeatZoneSlot { startPercent = 0, endPercent = 100, type = SeatZoneType.FirstClassOpen }
                }
            },
            new PresetDef {
                name = "Sypialna",
                slots = new List<SeatZoneSlot> {
                    new SeatZoneSlot { startPercent = 0, endPercent = 100, type = SeatZoneType.Sleeping }
                }
            },
            new PresetDef {
                name = "Restauracyjny",
                slots = new List<SeatZoneSlot> {
                    new SeatZoneSlot { startPercent = 0, endPercent = 60, type = SeatZoneType.LargeCatering },
                    new SeatZoneSlot { startPercent = 60, endPercent = 100, type = SeatZoneType.SmallCatering }
                }
            },
            new PresetDef {
                name = "Mieszany 2kl/1kl",
                slots = new List<SeatZoneSlot> {
                    new SeatZoneSlot { startPercent = 0, endPercent = 70, type = SeatZoneType.SecondClassOpen },
                    new SeatZoneSlot { startPercent = 70, endPercent = 100, type = SeatZoneType.FirstClassOpen }
                }
            },
            new PresetDef {
                name = "Z rowerowym",
                slots = new List<SeatZoneSlot> {
                    new SeatZoneSlot { startPercent = 0, endPercent = 75, type = SeatZoneType.SecondClassOpen },
                    new SeatZoneSlot { startPercent = 75, endPercent = 100, type = SeatZoneType.Bicycle }
                }
            },
            new PresetDef {
                name = "Z wheelchair",
                slots = new List<SeatZoneSlot> {
                    new SeatZoneSlot { startPercent = 0, endPercent = 80, type = SeatZoneType.SecondClassOpen },
                    new SeatZoneSlot { startPercent = 80, endPercent = 100, type = SeatZoneType.WheelchairAccessible }
                }
            }
        };

        // ── UI builder ───────────────────────────────────────

        /// <summary>
        /// Buduje sekcję "Wnętrze" w prawym panelu konfiguratora. Mutuje przekazaną listę miksu,
        /// po każdej zmianie wywołuje <paramref name="onMixChanged"/> (parent rebuilduje swój panel).
        /// </summary>
        private void BuildInteriorMixSection(
            List<SeatZoneSlot> mix,
            float bodyLengthM,
            List<string> comfortFeatures,
            System.Action onMixChanged)
        {
            // Ensure default mix if empty
            if (mix.Count == 0)
            {
                mix.Add(new SeatZoneSlot { startPercent = 0, endPercent = 100, type = SeatZoneType.SecondClassOpen });
            }

            // Presety strip
            BuildPresetStrip(mix, onMixChanged);

            // Lista stref
            var listCard = NewGO("InteriorList", _configRightContent);
            var listImg = listCard.AddComponent<Image>();
            UITheme.ApplySurface(listImg, TopBarBg, UIShapePreset.Inset);
            listCard.AddComponent<LayoutElement>().preferredHeight = mix.Count * 58f + 118f;
            var listVL = listCard.AddComponent<VerticalLayoutGroup>();
            listVL.padding = UITheme.Padding(UITheme.Spacing.Sm);
            listVL.spacing = UITheme.Spacing.Xs;
            listVL.childForceExpandWidth = true; listVL.childForceExpandHeight = false;
            listVL.childControlWidth = true; listVL.childControlHeight = true;

            var headerCard = NewGO("InteriorListHeader", listCard.transform);
            var headerImg = headerCard.AddComponent<Image>();
            UITheme.ApplySurface(headerImg, UITheme.WithAlpha(UITheme.SecondarySurface, 0.86f), UIShapePreset.Panel);
            headerCard.AddComponent<LayoutElement>().preferredHeight = 46f;
            var headerVL = headerCard.AddComponent<VerticalLayoutGroup>();
            headerVL.padding = UITheme.Padding(UITheme.Spacing.Md, UITheme.Spacing.Sm);
            headerVL.spacing = UITheme.Spacing.Xxs;
            headerVL.childForceExpandWidth = true; headerVL.childForceExpandHeight = false;
            headerVL.childControlWidth = true; headerVL.childControlHeight = true;

            var hdr = MakeTMP("Hdr", headerCard.transform);
            hdr.text = "Strefy wnetrza";
            hdr.fontSize = 13; hdr.fontStyle = FontStyles.Bold;
            hdr.color = TextPrimary; hdr.raycastTarget = false;

            var hint = MakeTMP("Hint", headerCard.transform);
            hint.text = "Ustaw kolejnosc, dlugosc i rodzaj kazdej strefy wewnatrz pojazdu.";
            hint.fontSize = 10.5f; hint.color = TextMuted;
            hint.raycastTarget = false;

            for (int i = 0; i < mix.Count; i++)
            {
                int capturedIdx = i;
                BuildInteriorMixRow(listCard.transform, mix, capturedIdx, bodyLengthM, onMixChanged);
            }

            // "+ Dodaj strefę" button
            var addGO = NewGO("AddZone", listCard.transform);
            var addImg = addGO.AddComponent<Image>();
            UITheme.ApplySurface(addImg, UITheme.WithAlpha(UITheme.PrimaryAccent, 0.18f), UIShapePreset.Button);
            addGO.AddComponent<LayoutElement>().preferredHeight = 36f;
            var addLbl = MakeTMP("Lbl", addGO.transform);
            addLbl.text = "+ Dodaj strefę";
            addLbl.fontSize = 13; addLbl.color = TextPrimary;
            addLbl.alignment = TextAlignmentOptions.Center;
            addLbl.raycastTarget = false; FillRT(addLbl.gameObject);
            var addBtn = addGO.AddComponent<Button>();
            addBtn.targetGraphic = addImg;
            addBtn.transition = Selectable.Transition.None;
            addBtn.onClick.AddListener(() => {
                // Add new zone with 10% length, push back others
                float currentSum = 0;
                foreach (var s in mix) currentSum += (s.endPercent - s.startPercent);
                float newLen = currentSum < 90 ? 10 : Mathf.Max(1, 100 - currentSum);
                mix.Add(new SeatZoneSlot { endPercent = newLen, type = SeatZoneType.SecondClassOpen });
                InteriorMixCalculator.Normalize(mix);
                onMixChanged?.Invoke();
            });

            // Validation + summary
            BuildValidationSummary(mix, bodyLengthM, comfortFeatures);
        }

        private void BuildPresetStrip(List<SeatZoneSlot> mix, System.Action onMixChanged)
        {
            var stripCard = NewGO("PresetStrip", _configRightContent);
            var stripImg = stripCard.AddComponent<Image>();
            UITheme.ApplySurface(stripImg, UITheme.WithAlpha(UITheme.SecondarySurface, 0.68f), UIShapePreset.Panel);
            stripCard.AddComponent<LayoutElement>().preferredHeight = 72f;

            var hl = stripCard.AddComponent<HorizontalLayoutGroup>();
            hl.padding = UITheme.Padding(UITheme.Spacing.Md);
            hl.spacing = UITheme.Spacing.Sm;
            hl.childAlignment = TextAnchor.MiddleLeft;
            hl.childForceExpandWidth = false; hl.childForceExpandHeight = false;
            hl.childControlWidth = false; hl.childControlHeight = true;

            var presetLbl = MakeTMP("Hdr", stripCard.transform);
            presetLbl.text = "Presety:";
            presetLbl.fontSize = 12; presetLbl.color = TextMuted;
            presetLbl.raycastTarget = false;
            presetLbl.gameObject.AddComponent<LayoutElement>().preferredWidth = 68f;

            // Wrap presets w ScrollRect (8 buttonów × ~120 = 960px > viewport)
            var scrollGO = NewGO("PresetScroll", stripCard.transform);
            var scrollLE = scrollGO.AddComponent<LayoutElement>();
            scrollLE.flexibleWidth = 1f; scrollLE.preferredHeight = 50f;
            var scroll = scrollGO.AddComponent<ScrollRect>();
            scroll.horizontal = true; scroll.vertical = false;
            scroll.scrollSensitivity = 30f;

            var viewport = NewGO("VP", scrollGO.transform);
            var vpRT = viewport.GetComponent<RectTransform>();
            vpRT.anchorMin = Vector2.zero; vpRT.anchorMax = Vector2.one;
            vpRT.offsetMin = Vector2.zero; vpRT.offsetMax = Vector2.zero;
            var vpImg = viewport.AddComponent<Image>();
            UITheme.ApplySurface(vpImg, UITheme.WithAlpha(UITheme.AppBackground, 0.08f), UIShapePreset.Inset);
            viewport.AddComponent<RectMask2D>();
            scroll.viewport = vpRT;

            var content = NewGO("Content", viewport.transform);
            var cRT = content.GetComponent<RectTransform>();
            cRT.anchorMin = new Vector2(0, 0); cRT.anchorMax = new Vector2(0, 1);
            cRT.pivot = new Vector2(0, 0.5f);
            var cHL = content.AddComponent<HorizontalLayoutGroup>();
            cHL.spacing = UITheme.Spacing.Sm; cHL.padding = UITheme.Padding(UITheme.Spacing.Xs);
            cHL.childAlignment = TextAnchor.MiddleLeft;
            cHL.childForceExpandWidth = false; cHL.childForceExpandHeight = false;
            cHL.childControlWidth = false; cHL.childControlHeight = true;
            content.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = cRT;

            foreach (var preset in InteriorPresets)
            {
                var capturedPreset = preset;
                var btnGO = NewGO($"Preset_{preset.name}", content.transform);
                var btnImg = btnGO.AddComponent<Image>();
                UITheme.ApplySurface(btnImg, RowBg, UIShapePreset.Button);
                var btnLE = btnGO.AddComponent<LayoutElement>();
                btnLE.preferredWidth = 130f; btnLE.preferredHeight = 36f;
                var btnLbl = MakeTMP("Lbl", btnGO.transform);
                btnLbl.text = preset.name;
                btnLbl.fontSize = 11; btnLbl.color = TextPrimary;
                btnLbl.alignment = TextAlignmentOptions.Center;
                btnLbl.raycastTarget = false; FillRT(btnLbl.gameObject);
                var btn = btnGO.AddComponent<Button>();
                btn.targetGraphic = btnImg;
                btn.transition = Selectable.Transition.None;
                btn.onClick.AddListener(() => {
                    mix.Clear();
                    foreach (var s in capturedPreset.slots)
                        mix.Add(new SeatZoneSlot { startPercent = s.startPercent, endPercent = s.endPercent, type = s.type });
                    onMixChanged?.Invoke();
                });
                btnGO.AddComponent<HoverImageColor>().Init(btnImg, RowBg, RowHover);
            }
        }

        private void BuildInteriorMixRow(
            Transform parent,
            List<SeatZoneSlot> mix,
            int idx,
            float bodyLengthM,
            System.Action onMixChanged)
        {
            var slot = mix[idx];
            float zoneLen = slot.endPercent - slot.startPercent;

            var row = NewGO($"Zone_{idx}", parent);
            var rowImg = row.AddComponent<Image>();
            UITheme.ApplySurface(rowImg, RowBg, UIShapePreset.Panel);
            row.AddComponent<LayoutElement>().preferredHeight = 56f;

            var hl = row.AddComponent<HorizontalLayoutGroup>();
            hl.padding = UITheme.Padding(UITheme.Spacing.Sm);
            hl.spacing = UITheme.Spacing.Sm;
            hl.childAlignment = TextAnchor.MiddleLeft;
            hl.childForceExpandWidth = false; hl.childForceExpandHeight = false;
            hl.childControlWidth = false; hl.childControlHeight = true;

            var idxChip = NewGO("ZoneChip", row.transform);
            var idxChipImg = idxChip.AddComponent<Image>();
            UITheme.ApplySurface(idxChipImg, UITheme.WithAlpha(UITheme.Border, 0.28f), UIShapePreset.Pill);
            var idxChipLE = idxChip.AddComponent<LayoutElement>();
            idxChipLE.preferredWidth = 42f; idxChipLE.preferredHeight = 30f;
            var idxLbl = MakeTMP("Lbl", idxChip.transform);
            idxLbl.text = $"#{idx + 1}";
            idxLbl.fontSize = 11; idxLbl.fontStyle = FontStyles.Bold;
            idxLbl.color = TextPrimary;
            idxLbl.alignment = TextAlignmentOptions.Center;
            idxLbl.raycastTarget = false; FillRT(idxLbl.gameObject);

            // Cycle type button
            var typeBtnGO = NewGO("TypeBtn", row.transform);
            var typeBtnImg = typeBtnGO.AddComponent<Image>();
            UITheme.ApplySurface(typeBtnImg, UITheme.WithAlpha(UITheme.PrimaryAccent, 0.25f), UIShapePreset.Button);
            var typeBtnLE = typeBtnGO.AddComponent<LayoutElement>();
            typeBtnLE.preferredWidth = 186f; typeBtnLE.preferredHeight = 36f;
            var typeBtnLbl = MakeTMP("Lbl", typeBtnGO.transform);
            typeBtnLbl.text = GetSeatZoneLabel(slot.type);
            typeBtnLbl.fontSize = 12; typeBtnLbl.color = TextPrimary;
            typeBtnLbl.alignment = TextAlignmentOptions.Center;
            typeBtnLbl.raycastTarget = false; FillRT(typeBtnLbl.gameObject);
            var typeBtn = typeBtnGO.AddComponent<Button>();
            typeBtn.targetGraphic = typeBtnImg;
            typeBtn.transition = Selectable.Transition.None;
            typeBtn.onClick.AddListener(() => {
                slot.type = CycleNextZoneType(slot.type);
                onMixChanged?.Invoke();
            });

            // Length slider 1-100
            var sliderGO = NewGO("Slider", row.transform);
            var sliderLE = sliderGO.AddComponent<LayoutElement>();
            sliderLE.preferredWidth = 140f; sliderLE.preferredHeight = 24f;
            BuildLengthSlider(sliderGO.transform, zoneLen, (newLen) => {
                slot.endPercent = slot.startPercent + Mathf.Clamp(newLen, 1, 100);
                InteriorMixCalculator.Normalize(mix);
                onMixChanged?.Invoke();
            });

            // Length info text
            var infoLbl = MakeTMP("Info", row.transform);
            float zoneLengthM = bodyLengthM * zoneLen / 100f;
            int zoneSeats = InteriorMixCalculator.CalculateSeats(
                new List<SeatZoneSlot> { new SeatZoneSlot { startPercent = 0, endPercent = 100, type = slot.type } },
                zoneLengthM);
            infoLbl.text = $"{zoneLen:0}% / {zoneLengthM:0.0}m\n{zoneSeats} miejsc";
            infoLbl.fontSize = 10; infoLbl.color = TextMuted;
            infoLbl.alignment = TextAlignmentOptions.MidlineLeft;
            infoLbl.raycastTarget = false;
            infoLbl.gameObject.AddComponent<LayoutElement>().preferredWidth = 104f;

            // Up button
            BuildSmallBtn(row.transform, "↑", idx > 0, () => {
                if (idx <= 0) return;
                var tmp = mix[idx]; mix[idx] = mix[idx - 1]; mix[idx - 1] = tmp;
                InteriorMixCalculator.Normalize(mix);
                onMixChanged?.Invoke();
            });

            // Down button
            BuildSmallBtn(row.transform, "↓", idx < mix.Count - 1, () => {
                if (idx >= mix.Count - 1) return;
                var tmp = mix[idx]; mix[idx] = mix[idx + 1]; mix[idx + 1] = tmp;
                InteriorMixCalculator.Normalize(mix);
                onMixChanged?.Invoke();
            });

            // Delete button
            BuildSmallBtn(row.transform, "✕", mix.Count > 1, () => {
                if (mix.Count <= 1) return;
                mix.RemoveAt(idx);
                InteriorMixCalculator.Normalize(mix);
                onMixChanged?.Invoke();
            });
        }

        private void BuildSmallBtn(Transform parent, string label, bool enabled, System.Action onClick)
        {
            string displayLabel = label switch
            {
                "â†‘" => "UP",
                "â†“" => "DN",
                "âś•" => "DEL",
                _ => label
            };

            var btnGO = NewGO($"Btn_{label}", parent);
            var btnImg = btnGO.AddComponent<Image>();
            bool destructive = displayLabel == "DEL";
            UITheme.ApplySurface(btnImg,
                enabled
                    ? (destructive ? UITheme.WithAlpha(UITheme.Danger, 0.22f) : UITheme.WithAlpha(UITheme.Border, 0.25f))
                    : UITheme.WithAlpha(RowBg, 0.4f),
                UIShapePreset.Button);
            var le = btnGO.AddComponent<LayoutElement>();
            le.preferredWidth = destructive ? 42f : 36f; le.preferredHeight = 30f;
            var lbl = MakeTMP("Lbl", btnGO.transform);
            lbl.text = displayLabel;
            lbl.fontSize = 11f; lbl.fontStyle = FontStyles.Bold;
            lbl.color = enabled ? (destructive ? UITheme.Danger : TextPrimary) : TextMuted;
            lbl.alignment = TextAlignmentOptions.Center;
            lbl.raycastTarget = false; FillRT(lbl.gameObject);
            if (enabled)
            {
                var btn = btnGO.AddComponent<Button>();
                btn.targetGraphic = btnImg;
                btn.transition = Selectable.Transition.None;
                btn.onClick.AddListener(() => onClick());
            }
        }

        private void BuildLengthSlider(Transform parent, float currentLen, System.Action<float> onChanged)
        {
            var sliderGO = NewGO("SliderRoot", parent);
            var sliderRT = sliderGO.GetComponent<RectTransform>();
            sliderRT.anchorMin = Vector2.zero; sliderRT.anchorMax = Vector2.one;
            sliderRT.offsetMin = Vector2.zero; sliderRT.offsetMax = Vector2.zero;

            var slider = sliderGO.AddComponent<Slider>();
            slider.minValue = 1; slider.maxValue = 100;
            slider.value = Mathf.Clamp(currentLen, 1, 100);
            slider.wholeNumbers = true;

            // Background
            var bg = NewGO("BG", sliderGO.transform);
            var bgRT = bg.GetComponent<RectTransform>();
            bgRT.anchorMin = new Vector2(0, 0.4f); bgRT.anchorMax = new Vector2(1, 0.6f);
            bgRT.offsetMin = Vector2.zero; bgRT.offsetMax = Vector2.zero;
            var bgImg = bg.AddComponent<Image>();
            UITheme.ApplySurface(bgImg, UITheme.WithAlpha(UITheme.Border, 0.5f), UIShapePreset.Inset);

            // Fill area
            var fillArea = NewGO("FillArea", sliderGO.transform);
            var faRT = fillArea.GetComponent<RectTransform>();
            faRT.anchorMin = new Vector2(0, 0.4f); faRT.anchorMax = new Vector2(1, 0.6f);
            faRT.offsetMin = Vector2.zero; faRT.offsetMax = Vector2.zero;

            var fill = NewGO("Fill", fillArea.transform);
            var fillRT = fill.GetComponent<RectTransform>();
            fillRT.anchorMin = Vector2.zero; fillRT.anchorMax = Vector2.one;
            fillRT.offsetMin = Vector2.zero; fillRT.offsetMax = Vector2.zero;
            var fillImg = fill.AddComponent<Image>();
            UITheme.ApplySurface(fillImg, UITheme.PrimaryAccent, UIShapePreset.Inset);

            slider.fillRect = fillRT;

            // Handle
            var handleSlideArea = NewGO("HandleSlideArea", sliderGO.transform);
            var hsaRT = handleSlideArea.GetComponent<RectTransform>();
            hsaRT.anchorMin = Vector2.zero; hsaRT.anchorMax = Vector2.one;
            hsaRT.offsetMin = new Vector2(8, 0); hsaRT.offsetMax = new Vector2(-8, 0);

            var handle = NewGO("Handle", handleSlideArea.transform);
            var handleRT = handle.GetComponent<RectTransform>();
            handleRT.sizeDelta = new Vector2(16, 24);
            var handleImg = handle.AddComponent<Image>();
            UITheme.ApplySurface(handleImg, UITheme.PrimaryAccent, UIShapePreset.Pill);

            slider.handleRect = handleRT;
            slider.targetGraphic = handleImg;

            slider.onValueChanged.AddListener(v => onChanged?.Invoke(v));
        }

        private void BuildValidationSummary(List<SeatZoneSlot> mix, float bodyLengthM, List<string> comfortFeatures)
        {
            float sum = 0;
            foreach (var s in mix) sum += (s.endPercent - s.startPercent);

            int totalSeats = InteriorMixCalculator.CalculateSeats(mix, bodyLengthM);
            int comfortClass = InteriorMixCalculator.CalculateComfortClass(mix, comfortFeatures);

            var summaryCard = NewGO("InteriorSummary", _configRightContent);
            var sImg = summaryCard.AddComponent<Image>();
            bool isValid = Mathf.Abs(sum - 100f) < 0.01f;
            UITheme.ApplySurface(sImg,
                isValid
                    ? UITheme.WithAlpha(UITheme.Success, 0.18f)
                    : UITheme.WithAlpha(UITheme.Warning, 0.18f),
                UIShapePreset.Panel);
            summaryCard.AddComponent<LayoutElement>().preferredHeight = 78f;
            var vl = summaryCard.AddComponent<VerticalLayoutGroup>();
            vl.padding = UITheme.Padding(UITheme.Spacing.Md, UITheme.Spacing.Sm);
            vl.spacing = UITheme.Spacing.Xs;
            vl.childForceExpandWidth = true; vl.childForceExpandHeight = false;
            vl.childControlWidth = true; vl.childControlHeight = true;

            var sumLbl = MakeTMP("Sum", summaryCard.transform);
            string statusIcon = isValid ? "✓" : "⚠";
            string statusText = isValid ? "OK" : "UWAGA";
            sumLbl.text = $"{statusText}  Suma: {sum:0.0}% {(isValid ? "" : $"(brakuje {100 - sum:0.0}%)")}";
            sumLbl.fontSize = 13; sumLbl.fontStyle = FontStyles.Bold;
            sumLbl.color = isValid ? UITheme.Success : UITheme.Warning;
            sumLbl.raycastTarget = false;

            var calcLbl = MakeTMP("Calc", summaryCard.transform);
            calcLbl.text = $"Miejsca: <b>{totalSeats}</b>   Klasa komfortu: <b>{comfortClass}</b> ★";
            calcLbl.fontSize = 13; calcLbl.color = TextPrimary;
            calcLbl.richText = true; calcLbl.raycastTarget = false;

            var noteLbl = MakeTMP("Note", summaryCard.transform);
            noteLbl.text = $"Strefy: {mix.Count}  |  Dlugosc pudla: {bodyLengthM:0.0} m";
            noteLbl.fontSize = 10.5f; noteLbl.color = TextMuted;
            noteLbl.raycastTarget = false;
        }
    }
}
