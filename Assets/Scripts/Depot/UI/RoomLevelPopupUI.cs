using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using DepotSystem.RoomLevel;
using RailwayManager.Core;
using RailwayManager.SharedUI;

namespace DepotSystem
{
    /// <summary>
    /// Popup poziomu pokoju z checklista wymagan i przyciskiem awansu.
    ///
    /// Plik partial — root trzyma stan + lifecycle + public API + event handlers.
    /// Logika podzielona:
    /// <list type="bullet">
    ///   <item><c>RoomLevelPopupUI.Content.cs</c> — UpdateContent + AddChecklistRow + OnUpgradeClicked</item>
    ///   <item><c>RoomLevelPopupUI.Build.cs</c> — procedural BuildUI (Hero/Bonus/Requirements/Summary/BottomRow) + widget helpers</item>
    /// </list>
    /// </summary>
    public partial class RoomLevelPopupUI : MonoBehaviour
    {
        [Header("Colors")]
        [SerializeField] private Color panelColor = default;
        [SerializeField] private Color headerColor = default;
        [SerializeField] private Color buttonColor = default;
        [SerializeField] private Color buttonHoverColor = default;
        [SerializeField] private Color validColor = default;
        [SerializeField] private Color invalidColor = default;
        [SerializeField] private Color confirmColor = default;
        [SerializeField] private Color lockedButtonColor = default;

        private GameObject popupPanel;
        private TextMeshProUGUI titleText;
        private TextMeshProUGUI levelText;
        private TextMeshProUGUI currentBonusText;
        private TextMeshProUGUI nextLevelHeaderText;
        private Transform checklistContainer;
        private TextMeshProUGUI readinessText;
        private TextMeshProUGUI costText;
        private Button upgradeButton;
        private TextMeshProUGUI upgradeButtonLabel;

        private RoomDetectionSystem roomSystem;
        private DetectedRoom currentRoom;
        private bool _eventsSubscribed;

        private readonly Dictionary<int, RoomType> _lastSeenTypes = new();

        void Start()
        {
            ApplyDefaultPalette();
            roomSystem = DepotServices.Get<RoomDetectionSystem>();
            SubscribeEvents();
            CaptureCurrentTypes();
        }

        void OnEnable()
        {
            if (roomSystem == null)
                roomSystem = DepotServices.Get<RoomDetectionSystem>();
            SubscribeEvents();
        }

        void OnDisable()
        {
            UnsubscribeEvents();
        }

        void OnDestroy()
        {
            UnsubscribeEvents();
            RoomLevelService.OnLevelChanged -= OnLevelChangedHandler;
        }

        private void SubscribeEvents()
        {
            if (_eventsSubscribed)
                return;

            if (roomSystem != null)
                roomSystem.OnRoomsChanged += OnRoomsChanged;

            RoomLevelService.OnLevelChanged += OnLevelChangedHandler;
            _eventsSubscribed = true;
        }

        private void UnsubscribeEvents()
        {
            if (!_eventsSubscribed)
                return;

            if (roomSystem != null)
                roomSystem.OnRoomsChanged -= OnRoomsChanged;

            _eventsSubscribed = false;
        }

        private void CaptureCurrentTypes()
        {
            if (roomSystem == null)
                return;

            _lastSeenTypes.Clear();
            foreach (var room in roomSystem.Rooms)
            {
                if (room != null)
                    _lastSeenTypes[room.roomId] = room.roomType;
            }
        }

        private void OnRoomsChanged()
        {
            if (roomSystem == null)
                return;

            DetectedRoom autoTrigger = null;

            foreach (var room in roomSystem.Rooms)
            {
                if (room == null)
                    continue;

                _lastSeenTypes.TryGetValue(room.roomId, out var prevType);
                if (prevType != room.roomType
                    && room.roomType != RoomType.None
                    && RoomLevelCatalog.IsLvlable(room.roomType))
                {
                    autoTrigger = room;
                }

                _lastSeenTypes[room.roomId] = room.roomType;
            }

            if (autoTrigger != null)
                ShowFor(autoTrigger.roomId);
        }

        private void OnLevelChangedHandler(int roomId, int oldLvl, int newLvl)
        {
            if (currentRoom != null && currentRoom.roomId == roomId && popupPanel != null && popupPanel.activeSelf)
                UpdateContent();
        }

        public void ShowFor(int roomId)
        {
            if (roomSystem == null)
                roomSystem = DepotServices.Get<RoomDetectionSystem>();
            if (roomSystem == null)
            {
                Log.Warn("[RoomLevelPopupUI] RoomDetectionSystem not found");
                return;
            }

            DetectedRoom found = null;
            foreach (var room in roomSystem.Rooms)
            {
                if (room != null && room.roomId == roomId)
                {
                    found = room;
                    break;
                }
            }

            if (found == null)
            {
                Log.Warn($"[RoomLevelPopupUI] Room #{roomId} nie istnieje");
                return;
            }

            if (!RoomLevelCatalog.IsLvlable(found.roomType))
            {
                Log.Info($"[RoomLevelPopupUI] Room #{roomId} ({found.roomType}) nielvlable - popup pominiety");
                return;
            }

            currentRoom = found;
            if (popupPanel == null)
                BuildUI();

            UpdateContent();
            popupPanel.SetActive(true);
        }

        public void Close()
        {
            if (popupPanel != null)
                popupPanel.SetActive(false);
            currentRoom = null;
        }

        private void ApplyDefaultPalette()
        {
            if (panelColor == default) panelColor = UITheme.WithAlpha(UITheme.OverlayPanelStrong, 0.98f);
            if (headerColor == default) headerColor = UITheme.TopBarInset;
            if (buttonColor == default) buttonColor = UITheme.SecondarySurface;
            if (buttonHoverColor == default) buttonHoverColor = UITheme.RaisedSurface;
            if (validColor == default) validColor = UITheme.Success;
            if (invalidColor == default) invalidColor = UITheme.Danger;
            if (confirmColor == default) confirmColor = UITheme.PrimaryAccent;
            if (lockedButtonColor == default) lockedButtonColor = UITheme.WithAlpha(UITheme.Border, 0.65f);
        }
    }
}
