using TMPro;
using UnityEngine;
using UnityEngine.UI;
using DepotSystem.RoomLevel;
using RailwayManager.Core;
using RailwayManager.SharedUI;

namespace DepotSystem
{
    /// <summary>
    /// Partial: content update (UpdateContent po ShowFor lub OnLevelChanged event),
    /// checklista wiersze (AddChecklistRow), upgrade button handler (OnUpgradeClicked).
    /// </summary>
    public partial class RoomLevelPopupUI
    {
        private void UpdateContent()
        {
            if (currentRoom == null)
                return;

            var svc = RoomLevelService.EnsureExists();
            string typeLabel = RoomBonusDescriptions.GetRoomTypeDisplayName(currentRoom.roomType).ToUpperInvariant();
            int currentLvl = svc.GetRoomLevel(currentRoom.roomId);
            int max = RoomLevelCatalog.GetMaxLevel(currentRoom.roomType);

            titleText.text = typeLabel;
            levelText.text = $"Lvl {currentLvl}/{max}";
            currentBonusText.text = RoomBonusDescriptions.GetCurrentBonus(currentRoom.roomType, currentLvl);

            for (int i = checklistContainer.childCount - 1; i >= 0; i--)
                Destroy(checklistContainer.GetChild(i).gameObject);

            var elig = svc.CheckEligibility(currentRoom.roomId);

            if (elig.isMaxLevel)
            {
                nextLevelHeaderText.text = "Maksymalny poziom osiagniety";
                nextLevelHeaderText.color = validColor;
                readinessText.text = "Ten pokoj jest juz w najlepszej dostepnej wersji.";
                readinessText.color = validColor;
                costText.text = "Dalszy awans nie jest wymagany.";
                costText.color = UITheme.SecondaryText;
                upgradeButton.interactable = false;
                upgradeButtonLabel.text = "MAKS";
                upgradeButtonLabel.color = UITheme.SecondaryText;
                return;
            }

            nextLevelHeaderText.text = $"Checklista nastepnego poziomu ({elig.targetLevel})";
            nextLevelHeaderText.color = UITheme.PrimaryText;

            AddChecklistRow(
                $"Powierzchnia: {elig.currentAreaSqM:F0}/{elig.requiredAreaSqM:F0} m2",
                elig.sizeOk);

            if (elig.furnitureChecks != null)
            {
                foreach (var check in elig.furnitureChecks)
                {
                    string label = $"{FormatRequirementLabel(check.requirement)}: {check.actualCount}/{check.requirement.count}";
                    AddChecklistRow(label, check.ok);
                }
            }

            readinessText.text = elig.canUpgrade
                ? "Wszystkie wymagania sa spelnione. Mozesz awansowac ten pokoj."
                : "Brakuje jeszcze kilku warunkow do kolejnego poziomu.";
            readinessText.color = elig.canUpgrade ? validColor : UITheme.SecondaryText;
            costText.text = $"Koszt awansu: {RoomBonusDescriptions.GetUpgradeCostLabel(currentLvl)}";
            costText.color = UITheme.PrimaryText;
            upgradeButton.interactable = elig.canUpgrade;
            upgradeButtonLabel.text = "AWANSUJ";
            upgradeButtonLabel.color = elig.canUpgrade ? UITheme.InverseText : UITheme.SecondaryText;
        }

        private static string FormatRequirementLabel(FurnitureRequirement req)
        {
            return req.kind switch
            {
                FurnitureReqKind.Compound => req.id == "WorkstationOfficeComplete" ? "Stanowisko biurkowe (biurko+monitor+krzeslo)"
                    : req.id == "WorkstationTrafficComplete" ? "Stanowisko dyzurnego (konsola+monitor+krzeslo)"
                    : req.id,
                FurnitureReqKind.Function => req.id switch
                {
                    "ServicePit" => "Kanal serwisowy (dowolny rozmiar)",
                    "WashStation" => "Element myjni",
                    "StorageGoods" => "Regal",
                    _ => req.id
                },
                _ => req.id,
            };
        }

        private void AddChecklistRow(string label, bool ok)
        {
            var row = new GameObject(ok ? "Row_OK" : "Row_FAIL");
            row.transform.SetParent(checklistContainer, false);
            row.AddComponent<RectTransform>();
            var bg = row.AddComponent<Image>();
            UITheme.ApplySurface(
                bg,
                UITheme.WithAlpha(ok ? UITheme.Success : UITheme.Warning, ok ? 0.12f : 0.10f),
                UIShapePreset.Inset);

            var le = row.AddComponent<LayoutElement>();
            le.preferredHeight = 34f;

            var layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.padding = UITheme.Padding(UITheme.Spacing.Sm, UITheme.Spacing.Xs);
            layout.spacing = UITheme.Spacing.Md;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            layout.childAlignment = TextAnchor.MiddleLeft;

            var iconObj = new GameObject("Icon");
            iconObj.transform.SetParent(row.transform, false);
            iconObj.AddComponent<RectTransform>();
            var iconBg = iconObj.AddComponent<Image>();
            UITheme.ApplySurface(iconBg, ok ? validColor : invalidColor, UIShapePreset.Pill);
            var iconLE = iconObj.AddComponent<LayoutElement>();
            iconLE.preferredWidth = 34f;
            iconLE.preferredHeight = 20f;

            // Text na osobnym GO (Image + Text na jednym GO → NRE, patrz commit 2a3907e)
            var iconTextObj = new GameObject("Text");
            iconTextObj.transform.SetParent(iconObj.transform, false);
            var iconTextRt = iconTextObj.AddComponent<RectTransform>();
            iconTextRt.anchorMin = Vector2.zero;
            iconTextRt.anchorMax = Vector2.one;
            iconTextRt.offsetMin = Vector2.zero;
            iconTextRt.offsetMax = Vector2.zero;
            var iconText = iconTextObj.AddComponent<TextMeshProUGUI>();
            UITheme.ApplyTmpText(iconText, UIThemeTextRole.Primary);
            iconText.text = ok ? "OK" : "!";
            iconText.fontSize = 11;
            iconText.fontStyle = FontStyles.Bold;
            iconText.alignment = TextAlignmentOptions.Center;
            iconText.color = UITheme.InverseText;
            iconText.raycastTarget = false;

            var lblObj = new GameObject("Label");
            lblObj.transform.SetParent(row.transform, false);
            lblObj.AddComponent<RectTransform>();
            var lblLE = lblObj.AddComponent<LayoutElement>();
            lblLE.flexibleWidth = 1f;

            var lbl = lblObj.AddComponent<TextMeshProUGUI>();
            UITheme.ApplyTmpText(lbl, UIThemeTextRole.Primary);
            lbl.text = label;
            lbl.fontSize = 12;
            lbl.alignment = TextAlignmentOptions.MidlineLeft;
            lbl.textWrappingMode = TextWrappingModes.Normal;
            lbl.overflowMode = TextOverflowModes.Overflow;
            lbl.color = ok ? UITheme.PrimaryText : UITheme.SecondaryText;
        }

        private void OnUpgradeClicked()
        {
            if (currentRoom == null)
                return;

            var svc = RoomLevelService.EnsureExists();
            bool ok = svc.TryUpgrade(currentRoom.roomId, out var failureReason);
            if (!ok)
            {
                Log.Warn($"[RoomLevelPopupUI] Awans odrzucony: {failureReason}");
                return;
            }

            UpdateContent();
        }
    }
}
