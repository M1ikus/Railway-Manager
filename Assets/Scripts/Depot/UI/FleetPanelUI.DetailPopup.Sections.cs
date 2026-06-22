using System.Collections.Generic;
using RailwayManager.Fleet;
using RailwayManager.SharedUI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DepotSystem
{
    /// <summary>
    /// Partial FleetPanelUI - wspolne sekcje tresci popupu szczegolow pojazdu.
    /// </summary>
    public partial class FleetPanelUI
    {
        private static readonly Dictionary<int, bool> _inspectionExpanded = new();

        private static readonly (string label, System.Func<VehicleComponents, float> getter)[] _componentRows =
        {
            ("Silnik", c => c.engineCondition),
            ("Hamulce", c => c.brakeCondition),
            ("Drzwi", c => c.doorsCondition),
            ("Klima", c => c.acCondition),
            ("Pudlo", c => c.bodyCondition),
            ("Kola", c => c.wheelsCondition),
            ("El. instal.", c => c.electricalCondition),
            ("Wnetrze", c => c.interiorCondition),
            ("Oswietl.", c => c.lightsCondition),
            ("Toalety", c => c.toiletsCondition),
            ("Pantograf", c => c.pantographCondition),
            ("Sprzeg", c => c.couplingCondition),
        };

        private void PopupSectionTitle(Transform parent, string text)
        {
            var lbl = MakeTMP("Title", parent);
            lbl.text = text;
            lbl.fontSize = 14;
            lbl.fontStyle = FontStyles.Bold;
            lbl.color = TextAccent;
            lbl.raycastTarget = false;
            lbl.gameObject.AddComponent<LayoutElement>().preferredHeight = 20f;
        }

        private void PopupInfoLine(Transform parent, string label, string value, Color? valColor = null)
        {
            var row = NewGO("Info", parent);
            var rowImage = row.AddComponent<Image>();
            UITheme.ApplySurface(rowImage, UITheme.WithAlpha(UITheme.TopBarInset, 0.38f), UIShapePreset.Inset);

            var hl = row.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = UITheme.Spacing.Sm;
            hl.padding = UITheme.Padding(UITheme.Spacing.Sm, UITheme.Spacing.Xxs);
            hl.childAlignment = TextAnchor.MiddleLeft;
            hl.childControlWidth = true;
            hl.childControlHeight = false;
            hl.childForceExpandWidth = false;
            row.AddComponent<LayoutElement>().preferredHeight = 22f;

            var left = MakeTMP("L", row.transform);
            left.text = label;
            left.fontSize = 13;
            left.color = TextMuted;
            left.raycastTarget = false;
            left.gameObject.AddComponent<LayoutElement>().preferredWidth = 120f;

            var val = MakeTMP("V", row.transform);
            val.text = value;
            val.fontSize = 13;
            val.color = valColor ?? TextPrimary;
            val.raycastTarget = false;
            val.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        }

        private void BuildComponentsSection(Transform parent, FleetVehicleData v)
        {
            PopupSectionTitle(parent, "Stan komponentow");
            if (v.components == null)
            {
                PopupInfoLine(parent, "(brak danych)", "");
                return;
            }

            foreach (var (label, getter) in _componentRows)
            {
                float val = getter(v.components);
                if (val < 0f)
                    continue;

                var rowGO = NewGO($"Cmp_{label}", parent);
                var rowImage = rowGO.AddComponent<Image>();
                UITheme.ApplySurface(rowImage, UITheme.WithAlpha(UITheme.TopBarInset, 0.38f), UIShapePreset.Inset);
                var le = rowGO.AddComponent<LayoutElement>();
                le.preferredHeight = 24f;

                var hlg = rowGO.AddComponent<HorizontalLayoutGroup>();
                hlg.spacing = UITheme.Spacing.Sm;
                hlg.padding = UITheme.Padding(UITheme.Spacing.Sm, UITheme.Spacing.Xxs);
                hlg.childControlWidth = false;
                hlg.childControlHeight = true;
                hlg.childForceExpandWidth = false;
                hlg.childAlignment = TextAnchor.MiddleLeft;

                var lblGO = NewGO("L", rowGO.transform);
                var lblTxt = MakeTMP("T", lblGO.transform);
                lblTxt.text = label;
                lblTxt.fontSize = 12;
                lblTxt.color = TextPrimary;
                lblTxt.raycastTarget = false;
                FillRT(lblTxt.gameObject);
                var lblLE = lblGO.AddComponent<LayoutElement>();
                lblLE.preferredWidth = 90f;

                var barBg = NewGO("Bar", rowGO.transform);
                var bgImg = barBg.AddComponent<Image>();
                UITheme.ApplySurface(bgImg, UITheme.WithAlpha(UITheme.TopBarInset, 0.96f), UIShapePreset.Inset);
                var barLE = barBg.AddComponent<LayoutElement>();
                barLE.preferredWidth = 140f;
                barLE.preferredHeight = 14f;

                var fillGO = NewGO("Fill", barBg.transform);
                var fillRt = fillGO.GetComponent<RectTransform>();
                fillRt.anchorMin = new Vector2(0f, 0f);
                fillRt.anchorMax = new Vector2(Mathf.Clamp01(val / 100f), 1f);
                fillRt.pivot = new Vector2(0f, 0.5f);
                fillRt.offsetMin = Vector2.zero;
                fillRt.offsetMax = Vector2.zero;
                var fillImg = fillGO.AddComponent<Image>();
                UITheme.ApplySurface(fillImg, GetConditionColor(val), UIShapePreset.Inset);
                fillImg.raycastTarget = false;

                var pctGO = NewGO("Pct", rowGO.transform);
                var pctTxt = MakeTMP("T", pctGO.transform);
                pctTxt.text = $"{val:F0}%";
                pctTxt.fontSize = 12;
                pctTxt.color = GetConditionColor(val);
                pctTxt.alignment = TextAlignmentOptions.MidlineRight;
                pctTxt.raycastTarget = false;
                FillRT(pctTxt.gameObject);
                var pctLE = pctGO.AddComponent<LayoutElement>();
                pctLE.preferredWidth = 40f;
            }
        }

        private void BuildMaintenanceHistorySection(Transform parent, FleetVehicleData v)
        {
            PopupSectionTitle(parent, "Historia przegladow");

            if (v.history == null || v.history.Count == 0)
            {
                PopupInfoLine(parent, "(brak)", "");
                return;
            }

            string dayColor = ColorUtility.ToHtmlStringRGB(TextMuted);
            string costColor = ColorUtility.ToHtmlStringRGB(UITheme.Danger);

            int show = System.Math.Min(6, v.history.Count);
            for (int i = v.history.Count - 1, n = 0; i >= 0 && n < show; i--, n++)
            {
                var rec = v.history[i];
                var rowGO = NewGO($"Hist_{n}", parent);
                var rowImage = rowGO.AddComponent<Image>();
                UITheme.ApplySurface(rowImage, UITheme.WithAlpha(UITheme.TopBarInset, 0.38f), UIShapePreset.Inset);
                var le = rowGO.AddComponent<LayoutElement>();
                le.preferredHeight = 24f;

                var txt = MakeTMP("T", rowGO.transform);
                long day = rec.gameTimeSeconds / 86400L;
                string dayLabel = $"D{day}";
                string costStr = rec.cost > 0 ? $"  <color=#{costColor}>-{rec.cost:N0}zl</color>" : "";
                txt.text = $"<size=11><color=#{dayColor}>{dayLabel}</color>  <b>{rec.recordType}</b>{costStr}</size>";
                txt.fontSize = 12;
                txt.color = TextPrimary;
                txt.raycastTarget = false;
                FillRT(txt.gameObject);
            }
        }

        // ── M-FC-9: Paint section (ZNTK paint job) ──────────

        private void BuildPaintSection(Transform parent, FleetVehicleData v)
        {
            PopupSectionTitle(parent, "Malowanie (ZNTK)");

            // Aktualne paint summary
            string baseColor = v.paintDefinition?.segments != null && v.paintDefinition.segments.Count > 0
                ? v.paintDefinition.segments[0].baseColor
                : "(brak)";
            int stripes = 0, decals = 0;
            if (v.paintDefinition?.segments != null)
            {
                foreach (var s in v.paintDefinition.segments)
                {
                    stripes += s.stripes?.Count ?? 0;
                    decals += s.decals?.Count ?? 0;
                }
            }
            PopupInfoLine(parent, "Kolor podstawowy", baseColor);
            PopupInfoLine(parent, "Paski / dekale", $"{stripes} / {decals}");

            // Aktywny paint job (jeśli jest)
            var activeJob = PaintingJobService.GetActiveJobForVehicle(v.id);
            if (activeJob != null)
            {
                long now = RailwayManager.Core.GameState.GameDay * 86400L + (long)RailwayManager.Core.GameState.GameTimeSeconds;
                long remaining = activeJob.completionGameTime - now;
                int remainingDays = (int)System.Math.Ceiling(remaining / 86400.0);
                string workshopName = ExternalWorkshopCatalog.GetById(activeJob.workshopId)?.name ?? activeJob.workshopId;

                var statusGO = NewGO("ActiveJob", parent);
                var statusImg = statusGO.AddComponent<Image>();
                UITheme.ApplySurface(statusImg, UITheme.WithAlpha(UITheme.Warning, 0.20f), UIShapePreset.Inset);
                statusGO.AddComponent<LayoutElement>().preferredHeight = 50f;
                var statusVL = statusGO.AddComponent<VerticalLayoutGroup>();
                statusVL.padding = UITheme.Padding(UITheme.Spacing.Sm);
                statusVL.spacing = UITheme.Spacing.Xxs;
                statusVL.childForceExpandWidth = true; statusVL.childForceExpandHeight = false;
                statusVL.childControlWidth = true; statusVL.childControlHeight = true;

                var statusLbl = MakeTMP("StatusLbl", statusGO.transform);
                statusLbl.text = $"<b>W malowaniu:</b> {workshopName} — pozostało {remainingDays} dni";
                statusLbl.fontSize = 12; statusLbl.color = TextPrimary;
                statusLbl.richText = true; statusLbl.raycastTarget = false;

                var cancelGO = NewGO("CancelBtn", statusGO.transform);
                cancelGO.AddComponent<LayoutElement>().preferredHeight = 22f;
                var cancelImg = cancelGO.AddComponent<Image>();
                UITheme.ApplySurface(cancelImg, UITheme.WithAlpha(UITheme.Danger, 0.30f), UIShapePreset.Button);
                var cancelLbl = MakeTMP("Lbl", cancelGO.transform);
                cancelLbl.text = $"Anuluj malowanie (refund 50% = {activeJob.costPln / 2:N0} zł)";
                cancelLbl.fontSize = 10; cancelLbl.color = TextPrimary;
                cancelLbl.alignment = TextAlignmentOptions.Center;
                cancelLbl.raycastTarget = false; FillRT(cancelLbl.gameObject);
                var cancelBtn = cancelGO.AddComponent<Button>();
                cancelBtn.targetGraphic = cancelImg;
                cancelBtn.transition = Selectable.Transition.None;
                int capturedId = v.id;
                cancelBtn.onClick.AddListener(() => {
                    long nowTime = RailwayManager.Core.GameState.GameDay * 86400L + (long)RailwayManager.Core.GameState.GameTimeSeconds;
                    if (PaintingJobService.Cancel(capturedId, nowTime))
                        OnPopupRefreshNeeded();
                });
                return; // brak listy ZNTK gdy aktywny job
            }

            // Lista ZNTK z opcją wysyłki
            var workshops = ExternalWorkshopCatalog.GetAll();
            int compatibleCount = 0;
            foreach (var w in workshops)
            {
                if (w.paintCostPln <= 0) continue;
                if (!w.CanServeType(v.type)) continue;
                BuildZntkPaintRow(parent, v, w);
                compatibleCount++;
            }

            if (compatibleCount == 0)
            {
                PopupInfoLine(parent, "(brak)", "Żaden ZNTK nie maluje tego typu pojazdu");
            }
        }

        private void BuildZntkPaintRow(Transform parent, FleetVehicleData v, ExternalWorkshop w)
        {
            var rowGO = NewGO($"ZNTK_{w.id}", parent);
            var rowImg = rowGO.AddComponent<Image>();
            UITheme.ApplySurface(rowImg, UITheme.WithAlpha(UITheme.TopBarInset, 0.38f), UIShapePreset.Inset);
            rowGO.AddComponent<LayoutElement>().preferredHeight = 36f;

            var hl = rowGO.AddComponent<HorizontalLayoutGroup>();
            hl.padding = UITheme.Padding(UITheme.Spacing.Sm, UITheme.Spacing.Xs);
            hl.spacing = UITheme.Spacing.Sm;
            hl.childAlignment = TextAnchor.MiddleLeft;
            hl.childForceExpandWidth = false; hl.childForceExpandHeight = false;
            hl.childControlWidth = false; hl.childControlHeight = true;

            var infoLbl = MakeTMP("Info", rowGO.transform);
            infoLbl.text = $"<b>{w.name}</b>  <size=10><color=#{ColorUtility.ToHtmlStringRGB(TextMuted)}>{w.paintCostPln:N0} zł / {w.paintTimeDays} dni</color></size>";
            infoLbl.fontSize = 11; infoLbl.color = TextPrimary;
            infoLbl.richText = true; infoLbl.raycastTarget = false;
            infoLbl.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            bool canAfford = RailwayManager.Core.GameState.Money >= w.paintCostPln;
            var sendGO = NewGO("SendBtn", rowGO.transform);
            sendGO.AddComponent<LayoutElement>().preferredWidth = 80f;
            sendGO.GetComponent<LayoutElement>().preferredHeight = 26f;
            var sendImg = sendGO.AddComponent<Image>();
            UITheme.ApplySurface(sendImg,
                canAfford ? UITheme.WithAlpha(UITheme.Success, 0.30f) : UITheme.WithAlpha(UITheme.Border, 0.30f),
                UIShapePreset.Button);
            var sendLbl = MakeTMP("Lbl", sendGO.transform);
            sendLbl.text = canAfford ? "Wyślij" : "Brak gotówki";
            sendLbl.fontSize = 10; sendLbl.color = TextPrimary;
            sendLbl.alignment = TextAlignmentOptions.Center;
            sendLbl.raycastTarget = false; FillRT(sendLbl.gameObject);
            if (canAfford)
            {
                var sendBtn = sendGO.AddComponent<Button>();
                sendBtn.targetGraphic = sendImg;
                sendBtn.transition = Selectable.Transition.None;
                int capturedId = v.id;
                string capturedWId = w.id;
                sendBtn.onClick.AddListener(() => {
                    long nowTime = RailwayManager.Core.GameState.GameDay * 86400L + (long)RailwayManager.Core.GameState.GameTimeSeconds;
                    // Refresh paint = zachowaj aktualny (M-FC-10 polish dorobi paint editor reuse)
                    if (PaintingJobService.Schedule(capturedId, capturedWId, null, nowTime).IsSuccess())
                        OnPopupRefreshNeeded();
                });
            }
        }

        /// <summary>M-FC-9: po akcji w paint section trzeba odświeżyć popup żeby pokazać nowy stan.</summary>
        private void OnPopupRefreshNeeded()
        {
            // M-Windows P2: akcja mogła zmienić dowolny pojazd → odśwież WSZYSTKIE otwarte okna
            // detalu (poprawne niezależnie które okno wywołało akcję) + market modal (jeśli otwarty).
            RefreshAllOwnedDetailWindows();
            RefreshCurrentDetailPopup();
        }

        private void BuildSeatBreakdownSection(Transform parent, int totalSeats, List<SeatCount> breakdown)
        {
            var header = MakeTMP("SeatBreakHeader", parent);
            header.text = $"Rozlozenie miejsc ({totalSeats})";
            header.fontSize = 13;
            header.fontStyle = FontStyles.Bold;
            header.color = TextMuted;
            header.alignment = TextAlignmentOptions.MidlineLeft;
            header.raycastTarget = false;
            header.gameObject.AddComponent<LayoutElement>().preferredHeight = 20f;

            if (breakdown == null || breakdown.Count == 0)
            {
                var none = MakeTMP("SeatBreakNone", parent);
                none.text = "-";
                none.fontSize = 13;
                none.color = TextMuted;
                none.alignment = TextAlignmentOptions.MidlineLeft;
                none.raycastTarget = false;
                none.gameObject.AddComponent<LayoutElement>().preferredHeight = 18f;
                return;
            }

            foreach (var sc in breakdown)
            {
                if (sc.count <= 0)
                    continue;

                var row = NewGO($"SeatRow_{sc.type}", parent);
                var rowImage = row.AddComponent<Image>();
                UITheme.ApplySurface(rowImage, UITheme.WithAlpha(UITheme.TopBarInset, 0.38f), UIShapePreset.Inset);
                row.AddComponent<LayoutElement>().preferredHeight = 22f;

                var hlg = row.AddComponent<HorizontalLayoutGroup>();
                hlg.spacing = UITheme.Spacing.Sm;
                hlg.padding = UITheme.Padding(UITheme.Spacing.Sm, UITheme.Spacing.Xxs);
                hlg.childControlWidth = false;
                hlg.childControlHeight = true;
                hlg.childForceExpandHeight = true;

                Color zoneColor = ZoneDescriptorCatalog.All.TryGetValue(sc.type, out var zd) ? zd.color : TextMuted;
                var dot = NewGO("Dot", row.transform);
                var dotImg = dot.AddComponent<Image>();
                UITheme.ApplySurface(dotImg, zoneColor, UIShapePreset.Pill);
                var dotLE = dot.AddComponent<LayoutElement>();
                dotLE.preferredWidth = 10f;
                dotLE.preferredHeight = 10f;

                string label = ZoneDescriptorCatalog.All.TryGetValue(sc.type, out var zd2) ? zd2.label : sc.type.ToString();
                var nameLbl = MakeTMP("Name", row.transform);
                nameLbl.text = label;
                nameLbl.fontSize = 13;
                nameLbl.color = TextPrimary;
                nameLbl.alignment = TextAlignmentOptions.MidlineLeft;
                nameLbl.raycastTarget = false;
                var nLE = nameLbl.gameObject.AddComponent<LayoutElement>();
                nLE.flexibleWidth = 1f;

                var cntLbl = MakeTMP("Count", row.transform);
                cntLbl.text = sc.count.ToString();
                cntLbl.fontSize = 13;
                cntLbl.fontStyle = FontStyles.Bold;
                cntLbl.color = TextPrimary;
                cntLbl.alignment = TextAlignmentOptions.MidlineRight;
                cntLbl.raycastTarget = false;
                cntLbl.gameObject.AddComponent<LayoutElement>().preferredWidth = 40f;
            }
        }

        private void BuildInspectionCollapsible(Transform parent, FleetVehicleData v)
        {
            var header = NewGO("InsHeader", parent);
            var headerImage = header.AddComponent<Image>();
            UITheme.ApplySurface(headerImage, UITheme.WithAlpha(UITheme.SecondarySurface, 0.98f), UIShapePreset.Inset);
            header.AddComponent<LayoutElement>().preferredHeight = 26f;

            var hlg = header.AddComponent<HorizontalLayoutGroup>();
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;
            hlg.padding = UITheme.Padding(UITheme.Spacing.Sm, 0f);
            hlg.spacing = UITheme.Spacing.Sm;

            bool expanded = _inspectionExpanded.TryGetValue(v.id, out bool isExpanded) && isExpanded;

            var arrowLbl = MakeTMP("Arrow", header.transform);
            arrowLbl.text = expanded ? "v" : ">";
            arrowLbl.fontSize = 14;
            arrowLbl.fontStyle = FontStyles.Bold;
            arrowLbl.color = TextAccent;
            arrowLbl.alignment = TextAlignmentOptions.MidlineLeft;
            arrowLbl.raycastTarget = false;
            arrowLbl.gameObject.AddComponent<LayoutElement>().preferredWidth = 14f;

            var titleLbl = MakeTMP("Lbl", header.transform);
            titleLbl.text = "Harmonogram przegladow";
            titleLbl.fontSize = 14;
            titleLbl.fontStyle = FontStyles.Bold;
            titleLbl.color = TextPrimary;
            titleLbl.alignment = TextAlignmentOptions.MidlineLeft;
            titleLbl.raycastTarget = false;
            var titleLE = titleLbl.gameObject.AddComponent<LayoutElement>();
            titleLE.flexibleWidth = 1f;

            if (expanded)
                BuildInspectionBody(parent, v);

            var headerBtn = header.AddComponent<Button>();
            headerBtn.targetGraphic = headerImage;
            int capturedId = v.id;
            headerBtn.onClick.AddListener(() =>
            {
                _inspectionExpanded[capturedId] = !expanded;
                OnPopupRefreshNeeded();
            });
        }

        private GameObject BuildInspectionBody(Transform parent, FleetVehicleData v)
        {
            var body = NewGO("InsBody", parent);
            var bodyImage = body.AddComponent<Image>();
            UITheme.ApplySurface(bodyImage, UITheme.WithAlpha(UITheme.TopBarInset, 0.96f), UIShapePreset.Inset);

            var vlg = body.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = UITheme.Spacing.Xxs;
            vlg.childAlignment = TextAnchor.UpperLeft;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.padding = UITheme.Padding(UITheme.Spacing.Md, UITheme.Spacing.Sm);

            var bodyLE = body.AddComponent<LayoutElement>();
            bodyLE.preferredHeight = 6 + 5 * 22 + 12;

            long now = NowGameTime;
            for (int i = 0; i <= 4; i++)
            {
                var level = (InspectionLevel)i;
                var status = v.inspections.GetStatus(level, now, v.mileageKm);
                BuildInspectionRow(body.transform, status);
            }

            return body;
        }

        private void BuildInspectionRow(Transform parent, InspectionSchedule.LevelStatus s)
        {
            var row = NewGO($"Row{s.level}", parent);
            row.AddComponent<LayoutElement>().preferredHeight = 20f;

            var rowLayout = row.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = UITheme.Spacing.Sm;
            rowLayout.childControlWidth = false;
            rowLayout.childControlHeight = true;
            rowLayout.childForceExpandHeight = true;

            var levelLbl = MakeTMP("Lvl", row.transform);
            levelLbl.text = s.level.ToString();
            levelLbl.fontSize = 13;
            levelLbl.fontStyle = FontStyles.Bold;
            levelLbl.color = TextPrimary;
            levelLbl.alignment = TextAlignmentOptions.MidlineLeft;
            levelLbl.raycastTarget = false;
            levelLbl.gameObject.AddComponent<LayoutElement>().preferredWidth = 30f;

            var limitLbl = MakeTMP("Lim", row.transform);
            limitLbl.text = InspectionSchedule.LevelLimitText(s.level);
            limitLbl.fontSize = 11;
            limitLbl.color = TextMuted;
            limitLbl.alignment = TextAlignmentOptions.MidlineLeft;
            limitLbl.raycastTarget = false;
            limitLbl.gameObject.AddComponent<LayoutElement>().preferredWidth = 150f;

            var toneLbl = MakeTMP("Tone", row.transform);
            toneLbl.text = GetInspectionToneLabel(s.progress);
            toneLbl.fontSize = 11;
            toneLbl.fontStyle = FontStyles.Bold;
            toneLbl.color = GetInspectionColorFromProgress(s.progress);
            toneLbl.alignment = TextAlignmentOptions.MidlineLeft;
            toneLbl.raycastTarget = false;
            toneLbl.gameObject.AddComponent<LayoutElement>().preferredWidth = 56f;

            var remLbl = MakeTMP("Rem", row.transform);
            string remText = FormatInspectionRemaining(s);
            if (s.hasKmLimit && s.hasTimeLimit)
            {
                string km = InspectionSchedule.FormatRemainingKm(s.remainingKm);
                string tm = InspectionSchedule.FormatRemainingTime(s.remainingSec);
                remText = $"{km} / {tm}";
            }

            remLbl.text = remText;
            remLbl.fontSize = 12;
            remLbl.color = GetInspectionColorFromProgress(s.progress);
            remLbl.alignment = TextAlignmentOptions.MidlineLeft;
            remLbl.raycastTarget = false;
            var remLE = remLbl.gameObject.AddComponent<LayoutElement>();
            remLE.flexibleWidth = 1f;
        }
    }
}
