using TMPro;
using UnityEngine;
using UnityEngine.UI;
using RailwayManager.Core;
using RailwayManager.SharedUI;

namespace DepotSystem
{
    /// <summary>
    /// Popup wyboru typu pomieszczenia.
    /// Pojawia sie automatycznie po wykryciu nowego zamknietego pokoju.
    ///
    /// Plik partial — root trzyma stan + lifecycle + public API + event handler.
    /// Logika podzielona:
    /// <list type="bullet">
    ///   <item><c>RoomTypePopupUI.Content.cs</c> — UpdateContent + UpdateTypeButtons (fit check per typ) + OnConfirm</item>
    ///   <item><c>RoomTypePopupUI.Build.cs</c> — procedural BuildUI (Hero + TypeSection scroll + SelectedCard + BottomRow) + CreateTypeButton + widget helpers</item>
    /// </list>
    /// </summary>
    public partial class RoomTypePopupUI : MonoBehaviour
    {
        [Header("Colors")]
        [SerializeField] private Color panelColor = default;
        [SerializeField] private Color headerColor = default;
        [SerializeField] private Color buttonColor = default;
        [SerializeField] private Color buttonHoverColor = default;
        [SerializeField] private Color validColor = default;
        [SerializeField] private Color invalidColor = default;
        [SerializeField] private Color confirmColor = default;
        [SerializeField] private Color selectedColor = default;
        [SerializeField] private Color lockedButtonColor = default;

        private GameObject popupPanel;
        private TextMeshProUGUI titleText;
        private TextMeshProUGUI sizeInfoText;
        private Transform buttonContainer;
        private Button confirmButton;
        private Button cancelButton;
        private TextMeshProUGUI selectedTypeText;
        private TextMeshProUGUI selectedTypeMetaText;
        private Image selectedTypeCard;

        private RoomDetectionSystem roomSystem;
        private DetectedRoom currentRoom;
        private RoomType selectedType = RoomType.None;

        // Labels jako klucze i18n; resolve via LocalizationService.Get przy renderze.
        private static readonly (RoomType type, string icon, string labelKey)[] roomTypeDefs = new[]
        {
            (RoomType.Hall,              "HAL", "popup_room_type.type.hall"),
            (RoomType.Storage,           "MAG", "popup_room_type.type.storage"),
            (RoomType.Dispatcher,        "DSP", "popup_room_type.type.dispatcher"),
            (RoomType.Office,            "BIO", "popup_room_type.type.office"),
            (RoomType.Social,            "SOC", "popup_room_type.type.social"),
            (RoomType.Supervisor,        "NAC", "popup_room_type.type.supervisor"),
            (RoomType.Bathroom,          "LAZ", "popup_room_type.type.bathroom"),
            (RoomType.Locker,            "SZT", "popup_room_type.type.locker"),
            (RoomType.Corridor,          "KOR", "popup_room_type.type.corridor"),
            (RoomType.TrafficController, "DR",  "popup_room_type.type.traffic_controller"),
        };

        void Start()
        {
            ApplyDefaultPalette();
            roomSystem = DepotServices.Get<RoomDetectionSystem>();
            if (roomSystem != null)
                roomSystem.OnNewRoomDetected += OnNewRoomDetected;
        }

        void OnDestroy()
        {
            if (roomSystem != null)
                roomSystem.OnNewRoomDetected -= OnNewRoomDetected;
        }

        private void OnNewRoomDetected(DetectedRoom room)
        {
            ShowPopup(room);
        }

        /// <summary>
        /// Inline check czy pokój jest wystarczająco duży dla danego typu — nie wymaga
        /// roomSystem instance (fix 2026-05-03 dla bug'a "wszystkie ZA MAŁE").
        /// Logika identyczna z RoomDetectionSystem.IsRoomLargeEnough, ale niezależna.
        /// Zakłada gridSize = 1m (spójne z RoomDetectionSystem default).
        /// </summary>
        private static bool IsRoomFitsType(DetectedRoom room, RoomType type)
        {
            if (room == null) return false;
            if (!RoomRequirements.MinSize.ContainsKey(type)) return true;

            var (minW, minD, _) = RoomRequirements.MinSize[type];
            float roomW = room.bounds.width;
            float roomD = room.bounds.height;

            // Sprawdź oba obroty (room może być postawiony w dwóch orientacjach)
            return (roomW >= minW && roomD >= minD) || (roomW >= minD && roomD >= minW);
        }

        private void ApplyDefaultPalette()
        {
            if (panelColor == default)
                panelColor = UITheme.WithAlpha(UITheme.OverlayPanelStrong, 0.98f);
            if (headerColor == default)
                headerColor = UITheme.TopBarInset;
            if (buttonColor == default)
                buttonColor = UITheme.SecondarySurface;
            if (buttonHoverColor == default)
                buttonHoverColor = UITheme.RaisedSurface;
            if (validColor == default)
                validColor = UITheme.Success;
            if (invalidColor == default)
                invalidColor = UITheme.Danger;
            if (confirmColor == default)
                confirmColor = UITheme.PrimaryAccent;
            if (selectedColor == default)
                selectedColor = UITheme.PrimaryAccent;
            if (lockedButtonColor == default)
                lockedButtonColor = UITheme.WithAlpha(UITheme.Border, 0.65f);
        }

        public void ShowPopup(DetectedRoom room)
        {
            currentRoom = room;
            selectedType = RoomType.None;

            if (popupPanel == null)
                BuildUI();

            UpdateContent();
            popupPanel.SetActive(true);
        }

        public void ClosePopup()
        {
            if (popupPanel != null)
                popupPanel.SetActive(false);
            currentRoom = null;
        }
    }
}
