using System.Linq;
using RailwayManager.Fleet;
using RailwayManager.SharedUI;
using RailwayManager.SharedUI.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DepotSystem
{
    /// <summary>
    /// Partial FleetPanelUI — zakladka "Skladow" (consists).
    /// Lista zestawionych skladow taboru z miniaturkami pojazdow.
    /// </summary>
    public partial class FleetPanelUI
    {
        // ── CONSISTS TAB ─────────────────────────────

        private void PopulateConsists()
        {
            if (_consists.Count == 0)
            {
                _emptyLbl.text = LocalizationService.Get("fleet.consists.empty");
                _emptyLbl.gameObject.SetActive(true);
                return;
            }

            foreach (var c in _consists)
                BuildConsistRow(c);
        }

        private void BuildConsistRow(FleetConsistData c)
        {
            string routeFallback = c.route ?? "\u2014";
            var row = NewGO(c.name, _contentParent);
            row.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, ConsistRowH);
            var rowImg = row.AddComponent<Image>();
            UITheme.ApplySurface(rowImg, RowBg, UIShapePreset.Inset);
            row.AddComponent<LayoutElement>().preferredHeight = ConsistRowH;

            var hl = row.AddComponent<HorizontalLayoutGroup>();
            hl.padding  = UITheme.Padding(UITheme.Spacing.Lg, UITheme.Spacing.Md);
            hl.spacing  = UITheme.Spacing.Md;
            hl.childAlignment      = TextAnchor.MiddleLeft;
            hl.childControlWidth   = false;
            hl.childControlHeight  = false;
            hl.childForceExpandWidth  = false;
            hl.childForceExpandHeight = false;

            // Consist summary
            var nameCol = NewGO("NameCol", row.transform);
            var nameColImage = nameCol.AddComponent<Image>();
            UITheme.ApplySurface(nameColImage, UITheme.WithAlpha(UITheme.TopBarInset, 0.45f), UIShapePreset.Panel);
            nameCol.GetComponent<RectTransform>().sizeDelta = new Vector2(220f, 64f);
            var nameVL = nameCol.AddComponent<VerticalLayoutGroup>();
            nameVL.padding = UITheme.Padding(UITheme.Spacing.Md, UITheme.Spacing.Sm);
            nameVL.childAlignment = TextAnchor.MiddleLeft;
            nameVL.childControlWidth   = false;
            nameVL.childControlHeight  = false;
            nameVL.childForceExpandWidth  = false;
            nameVL.childForceExpandHeight = false;
            nameVL.spacing = UITheme.Spacing.Xxs;
            nameCol.AddComponent<LayoutElement>().preferredWidth = 220f;

            // M-Windows P3: klik nazwy składu → pływające okno składu (chipy pojazdów + drill-down)
            var nameBtn = nameCol.AddComponent<Button>();
            nameBtn.targetGraphic = nameColImage;
            var capturedConsist = c;
            nameBtn.onClick.AddListener(() => ConsistWindowUI.Open(ConsistView.FromFleetConsist(capturedConsist)));

            var nameLbl = MakeTMP("Name", nameCol.transform);
            nameLbl.text      = c.name;
            nameLbl.fontSize  = 20;
            nameLbl.fontStyle = FontStyles.Bold;
            nameLbl.color     = TextAccent;
            nameLbl.raycastTarget = false;
            nameLbl.gameObject.AddComponent<LayoutElement>().preferredWidth = 196f;

            var routeLbl = MakeTMP("Route", nameCol.transform);
            routeLbl.text      = routeFallback;
            routeLbl.fontSize  = 13;
            routeLbl.color     = TextMuted;
            routeLbl.raycastTarget = false;
            routeLbl.gameObject.AddComponent<LayoutElement>().preferredWidth = 196f;

            // Vehicle strip
            var vehiclesCol = NewGO("Vehicles", row.transform);
            var vehiclesImage = vehiclesCol.AddComponent<Image>();
            UITheme.ApplySurface(vehiclesImage, UITheme.WithAlpha(UITheme.TopBarInset, 0.38f), UIShapePreset.Panel);
            vehiclesCol.GetComponent<RectTransform>().sizeDelta = new Vector2(430f, 56f);
            var vHL = vehiclesCol.AddComponent<HorizontalLayoutGroup>();
            vHL.padding = UITheme.Padding(UITheme.Spacing.Md, UITheme.Spacing.Sm);
            vHL.spacing = UITheme.Spacing.Sm;
            vHL.childAlignment      = TextAnchor.MiddleLeft;
            vHL.childControlWidth   = false;
            vHL.childControlHeight  = false;
            vHL.childForceExpandWidth  = false;
            vHL.childForceExpandHeight = false;
            vehiclesCol.AddComponent<LayoutElement>().preferredWidth = 430f;

            foreach (int vid in c.vehicleIds)
            {
                var vehicle = _vehicles.FirstOrDefault(v => v.id == vid);
                if (vehicle == null) continue;

                var chip = NewGO($"V_{vid}", vehiclesCol.transform);
                chip.GetComponent<RectTransform>().sizeDelta = new Vector2(118f, 38f);
                var chipImage = chip.AddComponent<Image>();
                UITheme.ApplySurface(chipImage, GetThumbnailColor(vehicle.type), UIShapePreset.Pill);
                chip.AddComponent<LayoutElement>().preferredWidth = 118f;

                var chipHL = chip.AddComponent<HorizontalLayoutGroup>();
                chipHL.padding = UITheme.Padding(UITheme.Spacing.Md, UITheme.Spacing.Xs);
                chipHL.spacing = UITheme.Spacing.Sm;
                chipHL.childAlignment = TextAnchor.MiddleCenter;
                chipHL.childControlWidth = false;
                chipHL.childControlHeight = false;
                chipHL.childForceExpandWidth = false;
                chipHL.childForceExpandHeight = false;

                var typeLbl = MakeTMP("Type", chip.transform);
                typeLbl.text      = GetTypeShortLabel(vehicle.type);
                typeLbl.fontSize  = 10;
                typeLbl.fontStyle = FontStyles.Bold;
                typeLbl.color     = TextPrimary;
                typeLbl.alignment = TextAlignmentOptions.Left;
                typeLbl.raycastTarget = false;
                typeLbl.gameObject.AddComponent<LayoutElement>().preferredWidth = 32f;

                var chipLbl = MakeTMP("Lbl", chip.transform);
                chipLbl.text      = vehicle.number;
                chipLbl.fontSize  = 11;
                chipLbl.color     = TextPrimary;
                chipLbl.alignment = TextAlignmentOptions.Right;
                chipLbl.raycastTarget = false;
                chipLbl.gameObject.AddComponent<LayoutElement>().preferredWidth = 66f;
            }

            // Spacer
            var sp = NewGO("Sp", row.transform);
            sp.AddComponent<LayoutElement>().flexibleWidth = 1f;

            // Status card
            var statusCol = NewGO("StatusCol", row.transform);
            var statusImage = statusCol.AddComponent<Image>();
            UITheme.ApplySurface(statusImage, UITheme.WithAlpha(UITheme.TopBarInset, 0.45f), UIShapePreset.Panel);
            statusCol.GetComponent<RectTransform>().sizeDelta = new Vector2(154f, 46f);
            statusCol.AddComponent<LayoutElement>().preferredWidth = 154f;

            var statusHL = statusCol.AddComponent<HorizontalLayoutGroup>();
            statusHL.padding = UITheme.Padding(UITheme.Spacing.Md, UITheme.Spacing.Sm);
            statusHL.spacing = UITheme.Spacing.Sm;
            statusHL.childAlignment = TextAnchor.MiddleLeft;
            statusHL.childControlWidth = false;
            statusHL.childControlHeight = false;
            statusHL.childForceExpandWidth = false;
            statusHL.childForceExpandHeight = false;

            var statusDot = NewGO("Dot", statusCol.transform);
            statusDot.GetComponent<RectTransform>().sizeDelta = new Vector2(12f, 12f);
            var statusDotImage = statusDot.AddComponent<Image>();
            UITheme.ApplySurface(statusDotImage, GetStatusColor(c.status), UIShapePreset.Pill);
            statusDot.AddComponent<LayoutElement>().preferredWidth = 12f;

            var statusLbl = MakeTMP("Status", statusCol.transform);
            statusLbl.text      = GetStatusText(c.status);
            statusLbl.fontSize  = 15;
            statusLbl.color     = GetStatusColor(c.status);
            statusLbl.raycastTarget = false;
            statusLbl.gameObject.AddComponent<LayoutElement>().preferredWidth = 110f;

            // Hover
            row.AddComponent<HoverImageColor>().Init(rowImg, RowBg, RowHover);
        }
    }
}
