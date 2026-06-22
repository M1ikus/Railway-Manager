using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using RailwayManager.Core;
using RailwayManager.SharedUI;
using RailwayManager.SharedUI.Localization;

namespace RailwayManager.Timetable.Simulation
{
    /// <summary>
    /// Popup UI z informacjami o klikniętej stacji/przystanku.
    /// Pokazuje: nazwa, typ (station/halt), tory z occupancy, rozkłady przejeżdżające.
    /// Dla halts: [TBD] przycisk "Uruchom przystanek" (upgrade za opłatą, M6 Ekonomia).
    /// </summary>
    public class StationPopupUI : MonoBehaviour
    {
        public static StationPopupUI Instance { get; private set; }

        MapSystem.StationMarker _shown;

        Canvas _canvas;
        GameObject _panel;
        TextMeshProUGUI _titleText;
        TextMeshProUGUI _infoText;
        Button _closeButton;

        private static readonly Color PanelBg = UITheme.WithAlpha(UITheme.OverlayPanelStrong, 0.97f);
        private static readonly Color ContentBg = UITheme.WithAlpha(UITheme.PrimarySurface, 0.86f);

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            BuildUI();
            Hide();
            MapClickHandler.OnStationClicked += OnStationClicked;
        }

        void OnDestroy()
        {
            MapClickHandler.OnStationClicked -= OnStationClicked;
            if (Instance == this) Instance = null;
        }

        void LateUpdate()
        {
            if (_shown == null) return;
            UpdateInfo();
        }

        void OnStationClicked(MapSystem.StationMarker marker)
        {
            if (marker == null) return;
            _shown = marker;
            _panel.SetActive(true);
            UpdateInfo();
            Log.Info($"[StationPopupUI] Show: '{marker.stationName}'");
        }

        public void Hide()
        {
            _shown = null;
            if (_panel != null) _panel.SetActive(false);
        }

        // ── Update info ─────────────────────────────────────────────

        void UpdateInfo()
        {
            if (_shown == null) return;

            string typeLabel = LocalizationService.Get(_shown.stationType == "halt"
                ? "popup_station.type_label_halt"
                : "popup_station.type_label_station");
            _titleText.text = string.Format(LocalizationService.Get("popup_station.title_format"), _shown.stationName);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine(string.Format(LocalizationService.Get("popup_station.type_format"), typeLabel, _shown.stationType));

            // Znajdź RailwayStation z TimetableInitializer — dla metadata
            var init = TimetableInitializer.Instance;
            int stationId = -1;
            if (init != null && init.Stations != null)
            {
                foreach (var rs in init.Stations)
                {
                    if (rs.name != _shown.stationName) continue;
                    stationId = rs.stationId;
                    if (!string.IsNullOrEmpty(rs.voivodeship))
                        sb.AppendLine(string.Format(LocalizationService.Get("popup_station.voivodeship_format"), rs.voivodeship));
                    if (!string.IsNullOrEmpty(rs.cityName))
                        sb.AppendLine(string.Format(LocalizationService.Get("popup_station.city_format"), rs.cityName));
                    break;
                }
            }

            // M6-7: Pasażerowie czekający + reputacja stacji
            var pm = RailwayManager.Timetable.Economy.PassengerManager.Instance;
            var rep = RailwayManager.Timetable.Economy.ReputationManager.Instance;
            if (stationId >= 0 && pm != null)
            {
                int waiting = pm.CountWaitingAt(stationId);
                string waitingColor = ToHtmlColor(waiting == 0
                    ? UITheme.SecondaryText
                    : (waiting > 20 ? UITheme.Warning : UITheme.Success));
                sb.AppendLine(string.Format(LocalizationService.Get("popup_station.waiting_format"), waitingColor, waiting));
            }
            if (stationId >= 0 && rep != null)
            {
                int r = rep.GetForStation(stationId);
                string repColor = ToHtmlColor(UITheme.GetReputationColor(r));
                sb.AppendLine(string.Format(LocalizationService.Get("popup_station.reputation_format"), repColor, r));
            }

            // Perony z occupancy
            sb.AppendLine();
            var platforms = GetPlatformsForStation(_shown.stationName);
            sb.AppendLine(string.Format(LocalizationService.Get("popup_station.platforms_label_format"), platforms.Count));
            if (platforms.Count == 0)
            {
                sb.AppendLine(LocalizationService.Get("popup_station.no_platforms"));
            }
            else
            {
                var simulator = TrainRunSimulator.Instance;
                var platOcc = simulator?.PlatformOccupancy;
                foreach (var p in platforms)
                {
                    string status;
                    if (platOcc != null && platOcc.TryGetValue(p.platformId, out int trainRunId))
                    {
                        var trainName = GetTrainName(trainRunId);
                        status = string.Format(LocalizationService.Get("popup_station.platform_status_busy_format"), trainName);
                    }
                    else
                    {
                        status = LocalizationService.Get("popup_station.platform_status_free");
                    }
                    string peronLabel;
                    if (!string.IsNullOrEmpty(p.platformName) && p.platformName != "?")
                        peronLabel = string.Format(LocalizationService.Get("popup_station.platform_label_named_format"), p.platformName);
                    else if (!string.IsNullOrEmpty(p.trackRef))
                        peronLabel = string.Format(LocalizationService.Get("popup_station.platform_label_track_format"), p.trackRef);
                    else
                        peronLabel = string.Format(LocalizationService.Get("popup_station.platform_label_id_format"), p.platformId);
                    sb.AppendLine(string.Format(LocalizationService.Get("popup_station.platform_row_format"), peronLabel, status));
                }
            }

            // Rozkłady przejeżdżające
            sb.AppendLine();
            sb.AppendLine(LocalizationService.Get("popup_station.timetables_label"));
            int timetableCount = CountTimetablesThroughStation(_shown.stationName, out var exampleList);
            if (timetableCount == 0)
            {
                sb.AppendLine(LocalizationService.Get("popup_station.no_timetables"));
            }
            else
            {
                foreach (var name in exampleList)
                    sb.AppendLine(string.Format(LocalizationService.Get("popup_station.timetable_row_format"), name));
                if (timetableCount > exampleList.Count)
                    sb.AppendLine(string.Format(LocalizationService.Get("popup_station.timetable_more_format"), timetableCount - exampleList.Count));
            }

            // [TBD] Halt activation
            if (_shown.stationType == "halt")
            {
                sb.AppendLine();
                sb.AppendLine(LocalizationService.Get("popup_station.halt_activation_hint"));
            }

            _infoText.text = sb.ToString();
        }

        static List<StationPlatform> GetPlatformsForStation(string stationName)
        {
            var result = new List<StationPlatform>();
            var init = TimetableInitializer.Instance;
            if (init == null || init.Platforms == null || init.Stations == null) return result;

            // Znajdź RailwayStation po nazwie → jej pathNodeId
            int stationNodeId = -1;
            foreach (var rs in init.Stations)
            {
                if (rs.name == stationName)
                {
                    stationNodeId = rs.pathNodeId;
                    break;
                }
            }
            if (stationNodeId < 0) return result;

            foreach (var p in init.Platforms)
                if (p.stationNodeId == stationNodeId)
                    result.Add(p);
            return result;
        }

        static string GetTrainName(int trainRunId)
        {
            foreach (var tr in TimetableService.TrainRuns)
                if (tr.id == trainRunId)
                    return tr.trainNumberSnapshot;
            return string.Format(LocalizationService.Get("popup_station.train_fallback_format"), trainRunId);
        }

        static int CountTimetablesThroughStation(string stationName, out List<string> examples)
        {
            examples = new List<string>();
            int count = 0;
            foreach (var tt in TimetableService.Timetables)
            {
                bool hit = false;
                foreach (var stop in tt.stops)
                {
                    if (stop.stationName == stationName) { hit = true; break; }
                }
                if (hit)
                {
                    count++;
                    if (examples.Count < 8)
                        examples.Add(string.Format(LocalizationService.Get("popup_station.tt_name_format"), tt.trainNumber, tt.name));
                }
            }
            return count;
        }

        // ── UI build (wspólny wzorzec z TrainPopupUI) ──────────────

        void BuildUI()
        {
            var canvasGo = new GameObject("StationPopupCanvas");
            canvasGo.transform.SetParent(transform);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 205;
            // MUI-10: standard canvas scaler config (ref 1920×1080, match 0.5)
            UITheme.ApplyCanvasScaler(canvasGo.AddComponent<CanvasScaler>());
            canvasGo.AddComponent<GraphicRaycaster>();

            // Panel (pod TrainPopupUI, przesunięty niżej)
            _panel = new GameObject("Panel");
            _panel.transform.SetParent(_canvas.transform, false);
            var prt = _panel.AddComponent<RectTransform>();
            prt.anchorMin = new Vector2(1f, 1f);
            prt.anchorMax = new Vector2(1f, 1f);
            prt.pivot = new Vector2(1f, 1f);
            prt.anchoredPosition = new Vector2(-20f, -280f); // pod train popup (240h+20 margin)
            prt.sizeDelta = new Vector2(360f, 380f);

            var bg = _panel.AddComponent<Image>();
            UITheme.ApplySurface(bg, PanelBg, UIShapePreset.PanelLarge);

            _titleText = PopupUIUtils.CreateText(_panel.transform, "StationName", 16,
                TextAlignmentOptions.TopLeft, new Vector2(14f, -12f), new Vector2(280f, 28f), UIThemeTextRole.Accent);
            _titleText.richText = true;

            var cardGo = new GameObject("InfoCard", typeof(RectTransform));
            cardGo.transform.SetParent(_panel.transform, false);
            var cardRt = cardGo.GetComponent<RectTransform>();
            cardRt.anchorMin = new Vector2(0f, 0f);
            cardRt.anchorMax = new Vector2(1f, 1f);
            cardRt.offsetMin = new Vector2(12f, 12f);
            cardRt.offsetMax = new Vector2(-12f, -48f);
            UITheme.ApplySurface(cardGo.AddComponent<Image>(), ContentBg, UIShapePreset.Panel);

            _infoText = PopupUIUtils.CreateText(cardGo.transform, "Info", 12,
                TextAlignmentOptions.TopLeft, new Vector2(14f, -14f), new Vector2(308f, 300f),
                UIThemeTextRole.Primary);
            _infoText.richText = true;

            _closeButton = PopupUIUtils.CreateButton(_panel.transform, LocalizationService.Get("popup_station.close_btn"),
                new Vector2(318f, -8f), new Vector2(30f, 30f));
            _closeButton.onClick.AddListener(Hide);
        }

        static string ToHtmlColor(Color color)
        {
            return $"#{ColorUtility.ToHtmlStringRGB(color)}";
        }
    }

    /// <summary>Wspólne helpery budowy popup UI.</summary>
    internal static class PopupUIUtils
    {
        public static TextMeshProUGUI CreateText(Transform parent, string name, int fontSize, TextAlignmentOptions alignment,
            Vector2 anchoredPos, Vector2 size, UIThemeTextRole role)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;

            var txt = go.AddComponent<TextMeshProUGUI>();
            UITheme.ApplyTmpText(txt, role);
            txt.fontSize = fontSize;
            txt.alignment = alignment;
            txt.textWrappingMode = TextWrappingModes.Normal;
            txt.overflowMode = TextOverflowModes.Overflow;
            txt.raycastTarget = false;
            return txt;
        }

        public static Button CreateButton(Transform parent, string label, Vector2 anchoredPos, Vector2 size, bool primary = false)
        {
            var go = new GameObject($"Btn_{label}");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;

            var img = go.AddComponent<Image>();
            var btn = go.AddComponent<Button>();
            UITheme.ApplyButtonStyle(btn, img, primary ? UIButtonTone.Primary : UIButtonTone.Secondary, UIShapePreset.Button);

            var textGo = new GameObject("Label");
            textGo.transform.SetParent(go.transform, false);
            var trt = textGo.AddComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;

            var txt = textGo.AddComponent<TextMeshProUGUI>();
            UITheme.ApplyTmpText(txt, primary ? UIThemeTextRole.Inverse : UIThemeTextRole.Primary);
            txt.fontSize = 13;
            txt.alignment = TextAlignmentOptions.Center;
            txt.raycastTarget = false;
            txt.text = label;

            return btn;
        }
    }
}
