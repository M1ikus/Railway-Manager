using TMPro;
using UnityEngine;
using UnityEngine.UI;
using RailwayManager.Fleet;
using RailwayManager.SharedUI;
using RailwayManager.SharedUI.Localization;

namespace RailwayManager.Maintenance
{
    public partial class WorkshopsPanelUI
    {
        // ═══════════════════════════════════════════
        //  M7-5: External ZNTK picker — wybór zewnętrznego zakładu
        // ═══════════════════════════════════════════

        void ShowExternalPicker(int vehicleId, InspectionLevel level, FleetVehicleType type)
        {
            _externalPickerTitle.text = string.Format(LocalizationService.Get("maintenance.workshops.external.picker_title_format"),
                vehicleId, (int)level + 1, type);

            // Clear existing rows
            foreach (var r in _externalPickerRows) if (r != null) Destroy(r);
            _externalPickerRows.Clear();

            var compatible = ExternalWorkshopCatalog.FindCompatible(level, type);
            if (compatible.Count == 0)
            {
                var noneRow = new GameObject("NoneRow", typeof(RectTransform));
                noneRow.transform.SetParent(_externalPickerContent, false);
                var nrt = noneRow.GetComponent<RectTransform>();
                nrt.anchorMin = new Vector2(0f, 1f);
                nrt.anchorMax = new Vector2(1f, 1f);
                nrt.pivot = new Vector2(0.5f, 1f);
                nrt.sizeDelta = new Vector2(0f, 82f);
                var noneBg = noneRow.AddComponent<Image>();
                UITheme.ApplySurface(noneBg, UITheme.WithAlpha(UITheme.SecondarySurface, 0.78f), UIShapePreset.Inset);
                var noneEyebrow = AddText(noneRow.transform, "Eyebrow",
                    "BRAK DOSTEPNYCH PARTNEROW",
                    10, TextAlignmentOptions.TopLeft, UIThemeTextRole.Accent);
                noneEyebrow.fontStyle = FontStyles.Bold;
                noneEyebrow.rectTransform.anchorMin = new Vector2(0f, 0.52f);
                noneEyebrow.rectTransform.anchorMax = new Vector2(1f, 1f);
                noneEyebrow.rectTransform.offsetMin = new Vector2(14f, 10f);
                noneEyebrow.rectTransform.offsetMax = new Vector2(-14f, -4f);

                var noneText = AddText(noneRow.transform, "Text",
                    LocalizationService.Get("maintenance.workshops.external.picker_no_workshops"),
                    13, TextAlignmentOptions.TopLeft, UIThemeTextRole.Secondary);
                noneText.rectTransform.anchorMin = new Vector2(0f, 0f);
                noneText.rectTransform.anchorMax = new Vector2(1f, 0.56f);
                noneText.rectTransform.offsetMin = new Vector2(14f, 8f);
                noneText.rectTransform.offsetMax = new Vector2(-14f, -6f);
                _externalPickerRows.Add(noneRow);
            }
            else
            {
                foreach (var w in compatible)
                {
                    _externalPickerRows.Add(CreateExternalPickerRow(w, vehicleId, level));
                }
            }

            _externalPickerPanel.SetActive(true);
        }

        GameObject CreateExternalPickerRow(ExternalWorkshop w, int vehicleId, InspectionLevel level)
        {
            var row = new GameObject($"ExtRow_{w.id}", typeof(RectTransform));
            row.transform.SetParent(_externalPickerContent, false);
            var rt = row.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(0f, 98f);

            var bg = row.AddComponent<Image>();
            UITheme.ApplySurface(bg, UITheme.WithAlpha(UITheme.SecondarySurface, 0.8f), UIShapePreset.Inset);

            string priceColor = ToHtmlColor(w.priceMultiplier >= 1f ? UITheme.Warning : UITheme.Success);
            string durColor = ToHtmlColor(w.durationMultiplier >= 1f ? UITheme.Warning : UITheme.Success);
            string capabilities = string.Join(", ", w.canPerform);

            var title = AddText(row.transform, "Title", w.name, 15, TextAlignmentOptions.TopLeft, UIThemeTextRole.Primary);
            title.fontStyle = FontStyles.Bold;
            title.rectTransform.anchorMin = new Vector2(0f, 0.54f);
            title.rectTransform.anchorMax = new Vector2(0.7f, 1f);
            title.rectTransform.offsetMin = new Vector2(14f, 10f);
            title.rectTransform.offsetMax = new Vector2(-8f, -6f);

            var meta = AddText(
                row.transform,
                "Meta",
                $"{w.locationStationName}  |  Obsluga: {capabilities}",
                12,
                TextAlignmentOptions.TopLeft,
                UIThemeTextRole.Secondary);
            meta.rectTransform.anchorMin = new Vector2(0f, 0.26f);
            meta.rectTransform.anchorMax = new Vector2(0.7f, 0.62f);
            meta.rectTransform.offsetMin = new Vector2(14f, 2f);
            meta.rectTransform.offsetMax = new Vector2(-8f, -4f);

            var stats = AddText(
                row.transform,
                "Stats",
                $"<color={priceColor}>Cena x{w.priceMultiplier:F2}</color>   <color={durColor}>Czas x{w.durationMultiplier:F2}</color>",
                12,
                TextAlignmentOptions.BottomLeft,
                UIThemeTextRole.Primary);
            stats.rectTransform.anchorMin = new Vector2(0f, 0f);
            stats.rectTransform.anchorMax = new Vector2(0.7f, 0.3f);
            stats.rectTransform.offsetMin = new Vector2(14f, 8f);
            stats.rectTransform.offsetMax = new Vector2(-8f, -2f);

            var sendBtn = CreateButton(row.transform, LocalizationService.Get("maintenance.workshops.external.send_btn"),
                new Vector2(-12f, 0f), new Vector2(132f, 46f),
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f));
            UITheme.ApplySurface(sendBtn.image, UITheme.WithAlpha(UITheme.Success, 0.78f), UIShapePreset.Button);
            sendBtn.colors = UITheme.CreateColorBlock(
                UITheme.WithAlpha(UITheme.Success, 0.78f),
                UITheme.Success,
                UITheme.Darken(UITheme.Success, 0.18f),
                UITheme.WithAlpha(UITheme.Success, 0.78f),
                UITheme.WithAlpha(UITheme.Border, 0.55f));
            string wid = w.id;
            sendBtn.onClick.AddListener(() =>
            {
                if (WorkshopManager.Instance != null)
                {
                    bool ok = WorkshopManager.Instance.SendToExternal(vehicleId, level, wid);
                    if (ok)
                    {
                        HideExternalPicker();
                        _refreshTimer = RefreshInterval;
                    }
                }
            });

            return row;
        }

        void HideExternalPicker()
        {
            if (_externalPickerPanel != null) _externalPickerPanel.SetActive(false);
            foreach (var r in _externalPickerRows) if (r != null) Destroy(r);
            _externalPickerRows.Clear();
        }

    }
}
