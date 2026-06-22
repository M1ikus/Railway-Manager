using TMPro;
using UnityEngine;
using UnityEngine.UI;
using RailwayManager.Core;
using RailwayManager.SharedUI;
using RailwayManager.SharedUI.Localization;

namespace RailwayManager.Timetable.Simulation
{
    /// <summary>
    /// Popup UI z informacjami o klikniętym torze/linii (edge w PathfindingGraph).
    /// Pokazuje: numer linii (jeśli znany), Vmax, elektryfikacja, track_ref,
    /// status (aktywna/unused), opcja reaktywacji unused torów za opłatą [TBD M6].
    /// </summary>
    public class TrackPopupUI : MonoBehaviour
    {
        public static TrackPopupUI Instance { get; private set; }

        int _shownEdgeId = -1;
        Vector2 _clickWorldPos;

        Canvas _canvas;
        GameObject _panel;
        TextMeshProUGUI _titleText;
        TextMeshProUGUI _infoText;
        Button _closeButton;
        Button _reactivateButton;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            BuildUI();
            Hide();
            MapClickHandler.OnTrackClicked += OnTrackClicked;
        }

        void OnDestroy()
        {
            MapClickHandler.OnTrackClicked -= OnTrackClicked;
            if (Instance == this) Instance = null;
        }

        void OnTrackClicked(int edgeId, Vector2 worldPos)
        {
            _shownEdgeId = edgeId;
            _clickWorldPos = worldPos;
            _panel.SetActive(true);
            UpdateInfo();
            Log.Info($"[TrackPopupUI] Show: edge#{edgeId}");
        }

        public void Hide()
        {
            _shownEdgeId = -1;
            if (_panel != null) _panel.SetActive(false);
        }

        void UpdateInfo()
        {
            if (_shownEdgeId < 0) return;

            var init = TimetableInitializer.Instance;
            if (init == null || init.Graph == null) return;

            var edge = init.Graph.GetEdge(_shownEdgeId);

            _titleText.text = LocalizationService.Get("popup_track.title");

            var sb = new System.Text.StringBuilder();
            sb.AppendLine(string.Format(LocalizationService.Get("popup_track.edge_id_format"), _shownEdgeId));
            sb.AppendLine(string.Format(LocalizationService.Get("popup_track.segment_format"), edge.segmentId));
            sb.AppendLine(string.Format(LocalizationService.Get("popup_track.length_format"), edge.lengthM.ToString("F0")));
            sb.AppendLine(string.Format(LocalizationService.Get("popup_track.vmax_format"), edge.maxSpeedKmh));

            if (edge.metadata != null)
            {
                if (edge.metadata.TryGetValue("railway:track_ref", out var trackRef) ||
                    edge.metadata.TryGetValue("track_ref", out trackRef))
                    sb.AppendLine(string.Format(LocalizationService.Get("popup_track.track_ref_format"), trackRef));

                if (edge.metadata.TryGetValue("ref", out var lineRef))
                    sb.AppendLine(string.Format(LocalizationService.Get("popup_track.line_ref_format"), lineRef));

                bool electrified = edge.metadata.TryGetValue("electrified", out var elec)
                                   && !string.IsNullOrEmpty(elec) && elec != "no";
                sb.AppendLine(electrified
                    ? string.Format(LocalizationService.Get("popup_track.electrified_format"), elec)
                    : LocalizationService.Get("popup_track.non_electrified"));

                if (edge.metadata.TryGetValue("usage", out var usage))
                    sb.AppendLine(string.Format(LocalizationService.Get("popup_track.usage_format"), usage));

                if (edge.metadata.TryGetValue("service", out var service))
                    sb.AppendLine(string.Format(LocalizationService.Get("popup_track.service_format"), service));

                bool isUnused = edge.metadata.TryGetValue("railway", out var r) && r == "abandoned"
                                || edge.metadata.TryGetValue("disused", out var d) && d == "yes";
                if (isUnused)
                {
                    sb.AppendLine();
                    sb.AppendLine(LocalizationService.Get("popup_track.status_inactive"));
                    sb.AppendLine(LocalizationService.Get("popup_track.reactivate_hint"));
                    _reactivateButton.gameObject.SetActive(true);
                }
                else
                {
                    sb.AppendLine();
                    sb.AppendLine(LocalizationService.Get("popup_track.status_active"));
                    _reactivateButton.gameObject.SetActive(false);
                }
            }

            sb.AppendLine();
            sb.AppendLine(string.Format(LocalizationService.Get("popup_track.click_pos_format"),
                _clickWorldPos.x.ToString("F0"), _clickWorldPos.y.ToString("F0")));

            _infoText.text = sb.ToString();
        }

        void OnReactivateClicked()
        {
            Log.Info($"[TrackPopupUI] Reactivate requested for edge#{_shownEdgeId} — TBD M6 Ekonomia");
            // [TBD] integracja z M6 (koszt, finansowanie)
        }

        void BuildUI()
        {
            var canvasGo = new GameObject("TrackPopupCanvas");
            canvasGo.transform.SetParent(transform);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 210;
            // MUI-10: standard canvas scaler config (ref 1920×1080, match 0.5)
            UITheme.ApplyCanvasScaler(canvasGo.AddComponent<CanvasScaler>());
            canvasGo.AddComponent<GraphicRaycaster>();

            _panel = new GameObject("Panel");
            _panel.transform.SetParent(_canvas.transform, false);
            var prt = _panel.AddComponent<RectTransform>();
            prt.anchorMin = new Vector2(1f, 0f);
            prt.anchorMax = new Vector2(1f, 0f);
            prt.pivot = new Vector2(1f, 0f);
            prt.anchoredPosition = new Vector2(-20f, 20f);
            prt.sizeDelta = new Vector2(340f, 280f);

            var bg = _panel.AddComponent<Image>();
            bg.color = new Color(0.12f, 0.08f, 0.08f, 0.92f);

            _titleText = PopupUIUtils.CreateText(_panel.transform, "Title", 16,
                TextAlignmentOptions.TopLeft, new Vector2(10f, -10f), new Vector2(280f, 25f), UIThemeTextRole.Accent);
            _titleText.richText = true;

            _infoText = PopupUIUtils.CreateText(_panel.transform, "Info", 12,
                TextAlignmentOptions.TopLeft, new Vector2(10f, -40f), new Vector2(320f, 200f),
                UIThemeTextRole.Primary);
            _infoText.richText = true;

            _closeButton = PopupUIUtils.CreateButton(_panel.transform, LocalizationService.Get("popup_track.close_btn"),
                new Vector2(305f, -5f), new Vector2(30f, 30f));
            _closeButton.onClick.AddListener(Hide);

            _reactivateButton = PopupUIUtils.CreateButton(_panel.transform, LocalizationService.Get("popup_track.reactivate_btn"),
                new Vector2(10f, -245f), new Vector2(160f, 28f));
            _reactivateButton.onClick.AddListener(OnReactivateClicked);
            _reactivateButton.gameObject.SetActive(false);
        }
    }
}
