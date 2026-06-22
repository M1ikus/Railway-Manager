using TMPro;
using UnityEngine;
using UnityEngine.UI;
using RailwayManager.Core;
using RailwayManager.SharedUI;
using RailwayManager.SharedUI.Localization;

namespace DepotSystem
{
    /// <summary>
    /// Partial: content refresh (UpdateContent po ShowPopup, UpdateTypeButtons z fit check
    /// per typ) + OnConfirm handler.
    /// </summary>
    public partial class RoomTypePopupUI
    {
        private void UpdateContent()
        {
            if (currentRoom == null)
                return;

            float width = currentRoom.bounds.width;
            float height = currentRoom.bounds.height;
            titleText.text = string.Format(LocalizationService.Get("popup_room_type.header_format"), currentRoom.areaSqM.ToString("F0"));
            sizeInfoText.text = string.Format(LocalizationService.Get("popup_room_type.size_format"), width, height);

            UpdateTypeButtons();
        }

        private void UpdateTypeButtons()
        {
            if (buttonContainer == null)
                return;

            for (int i = 0; i < buttonContainer.childCount; i++)
            {
                var btnObj = buttonContainer.GetChild(i).gameObject;
                var btn = btnObj.GetComponent<Button>();
                if (btn == null)
                    continue;

                int index = i;
                if (index >= roomTypeDefs.Length)
                    break;

                var (type, _, _) = roomTypeDefs[index];
                // Fix 2026-05-03: inline check niezależny od roomSystem instance.
                // Cached roomSystem mógł się zgubić (scenic instance recreated po DepotManager
                // reload), powodując że wszystkie typy pokazywały "ZA MAŁE" mimo że pokój
                // był wystarczająco duży.
                bool fits = currentRoom != null && IsRoomFitsType(currentRoom, type);

                btn.interactable = fits;

                var background = btnObj.GetComponent<Image>();
                if (background != null)
                    background.color = selectedType == type ? selectedColor : fits ? buttonColor : lockedButtonColor;

                btn.colors = UITheme.CreateColorBlock(
                    selectedType == type ? selectedColor : fits ? buttonColor : lockedButtonColor,
                    fits ? buttonHoverColor : lockedButtonColor,
                    fits ? UITheme.Border : lockedButtonColor,
                    selectedType == type ? selectedColor : fits ? buttonColor : lockedButtonColor,
                    lockedButtonColor);

                var iconBadge = btnObj.transform.Find("IconBadge")?.GetComponent<Image>();
                if (iconBadge != null)
                {
                    iconBadge.color = selectedType == type
                        ? UITheme.WithAlpha(UITheme.InverseText, 0.18f)
                        : fits ? UITheme.WithAlpha(UITheme.Border, 0.35f) : UITheme.WithAlpha(UITheme.DisabledText, 0.18f);
                }

                var iconText = btnObj.transform.Find("IconBadge/Icon")?.GetComponent<TextMeshProUGUI>();
                if (iconText != null)
                {
                    iconText.color = selectedType == type
                        ? UITheme.InverseText
                        : fits ? UITheme.PrimaryText : UITheme.DisabledText;
                    iconText.fontStyle = selectedType == type ? FontStyles.Bold : FontStyles.Normal;
                }

                var nameText = btnObj.transform.Find("LabelColumn/Label")?.GetComponent<TextMeshProUGUI>();
                if (nameText != null)
                {
                    nameText.color = selectedType == type
                        ? UITheme.InverseText
                        : fits ? UITheme.PrimaryText : UITheme.DisabledText;
                    nameText.fontStyle = FontStyles.Bold;
                }

                var sizeText = btnObj.transform.Find("LabelColumn/SizeReq")?.GetComponent<TextMeshProUGUI>();
                if (sizeText != null)
                {
                    if (RoomRequirements.MinSize.ContainsKey(type))
                    {
                        var (minWidth, minDepth, _) = RoomRequirements.MinSize[type];
                        sizeText.text = string.Format(LocalizationService.Get("popup_room_type.min_size_format"), minWidth, minDepth);
                        sizeText.color = fits ? validColor : invalidColor;
                    }
                    else
                    {
                        sizeText.text = string.Empty;
                    }
                }

                var statusBadge = btnObj.transform.Find("StatusBadge")?.GetComponent<Image>();
                if (statusBadge != null)
                {
                    Color badgeColor = selectedType == type
                        ? UITheme.WithAlpha(UITheme.InverseText, 0.18f)
                        : fits ? UITheme.WithAlpha(validColor, 0.24f) : UITheme.WithAlpha(invalidColor, 0.24f);
                    UITheme.ApplySurface(statusBadge, badgeColor, UIShapePreset.Pill);
                }

                var statusText = btnObj.transform.Find("StatusBadge/Label")?.GetComponent<TextMeshProUGUI>();
                if (statusText != null)
                {
                    statusText.text = selectedType == type ? "WYBRANO" : fits ? "PASUJE" : "ZA MALE";
                    statusText.color = selectedType == type
                        ? UITheme.InverseText
                        : fits ? validColor : invalidColor;
                    statusText.fontStyle = FontStyles.Bold;
                }
            }

            if (confirmButton != null)
                confirmButton.interactable = selectedType != RoomType.None;

            if (selectedTypeText != null)
            {
                if (selectedType != RoomType.None && RoomRequirements.MinSize.ContainsKey(selectedType))
                    selectedTypeText.text = string.Format(LocalizationService.Get("popup_room_type.selected_format"), RoomRequirements.MinSize[selectedType].label);
                else
                    selectedTypeText.text = LocalizationService.Get("popup_room_type.select_prompt");

                selectedTypeText.color = selectedType != RoomType.None ? UITheme.PrimaryText : UITheme.SecondaryText;
                selectedTypeText.fontStyle = selectedType != RoomType.None ? FontStyles.Bold : FontStyles.Normal;
            }

            if (selectedTypeMetaText != null)
            {
                if (selectedType != RoomType.None && RoomRequirements.MinSize.ContainsKey(selectedType))
                {
                    var (minWidth, minDepth, _) = RoomRequirements.MinSize[selectedType];
                    selectedTypeMetaText.text = string.Format(LocalizationService.Get("popup_room_type.min_size_format"), minWidth, minDepth);
                    selectedTypeMetaText.color = validColor;
                }
                else if (currentRoom != null)
                {
                    selectedTypeMetaText.text = string.Format(LocalizationService.Get("popup_room_type.size_format"), currentRoom.bounds.width, currentRoom.bounds.height);
                    selectedTypeMetaText.color = UITheme.SecondaryText;
                }
                else
                {
                    selectedTypeMetaText.text = string.Empty;
                }
            }

            if (selectedTypeCard != null)
            {
                UITheme.ApplySurface(
                    selectedTypeCard,
                    selectedType != RoomType.None ? UITheme.WithAlpha(UITheme.PrimaryAccent, 0.16f) : UITheme.TopBarInset,
                    UIShapePreset.Inset);
            }
        }

        private void OnConfirm()
        {
            if (currentRoom == null || selectedType == RoomType.None)
                return;
            // Lazy refresh — gdyby cached roomSystem był null (scenic instance recreated)
            if (roomSystem == null)
                roomSystem = DepotServices.Get<RoomDetectionSystem>();
            if (roomSystem == null)
            {
                Log.Warn("[RoomTypePopupUI] OnConfirm: RoomDetectionSystem nie istnieje — confirm zignorowany");
                return;
            }

            roomSystem.SetRoomType(currentRoom.roomId, selectedType);
            ClosePopup();
        }
    }
}
